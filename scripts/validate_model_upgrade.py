"""Compare installed old/new models on identical NPC contexts in isolated saves."""
import argparse
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import sqlite3
import subprocess
import sys
import time
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu.ai import DEFAULT_CONFIG,LocalModel,context_for,apply_decision
from jianghu.engine import World
from jianghu.store import Store
from jianghu import npc_attention,economics
from jianghu.model_service import MODEL_ID,ModelService,model_command


def copy_save(source,target):
    if target.exists():raise ValueError('Use a fresh validation path')
    with sqlite3.connect(source.resolve().as_uri()+'?mode=ro',uri=True) as src:
        with sqlite3.connect(target) as dst:src.backup(dst)


def prepare(source,folder):
    folder.mkdir(parents=True,exist_ok=True)
    target=folder/'context-world.sqlite';copy_save(source,target)
    w=World(store=Store(target),compact=True,durable=False)
    try:
        high=next(n for n in w.npcs.values() if n['alive'] and n['age_months']>=192
                  and npc_attention.grade(w,n)<=3 and not npc_attention.contact(w,n))
        people=[n for n in w.npcs.values() if n['alive'] and n['age_months']>=192
                and npc_attention.profile(w,n)['level']=='standard']
        ordinary,recent=people[:2]
        event=w.intervene(recent['id'],'talk','请先顾好自己的生计，再考虑是否与我合作；不必急着答应。')
        contexts=[dict(case=label,context=context_for(w,n,npc_attention.profile(w,n)))
                  for label,n in (('ordinary',ordinary),('high-standing',high),('recent-contact',recent))]
        w.checkpoint()
        (folder/'contexts.json').write_text(json.dumps(dict(source=str(source),month=w.month,
            player_event=event['id'],contexts=contexts),ensure_ascii=False,indent=2),encoding='utf-8')
        print('Prepared identical contexts on a read-only save copy.',flush=True)
    finally:w.store.close()


def run(which,folder):
    folder=folder.resolve();endpoint='http://127.0.0.1:11436'
    model_id='qwen3:4b' if which=='old' else MODEL_ID
    config={**DEFAULT_CONFIG,'model':model_id,'url':endpoint,'enabled':True}
    if which=='old':
        runtime=ROOT/'.runtime'
        command=[str(runtime/'llama/llama-server.exe'),'-m',str(runtime/'Qwen3-4B-Q4_K_M.gguf'),
                 '--alias',model_id,'-ngl','99','-c','8192','--parallel','1','--host','127.0.0.1',
                 '--port','11436','--jinja','--reasoning-budget','0']
    else:command,runtime=model_command(config)
    service=ModelService()
    if service.healthy(endpoint):raise RuntimeError('Benchmark port 11436 is already occupied')
    copy_save(folder/'context-world.sqlite',folder/f'{which}-world.sqlite')
    w=World(store=Store(folder/f'{which}-world.sqlite'),compact=True,durable=False)
    records=[];process=None
    try:
        with (folder/f'{which}-server.log').open('wb') as log:
            process=subprocess.Popen(command,cwd=Path(command[0]).parent,stdout=log,stderr=subprocess.STDOUT,
                                     creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
            deadline=time.monotonic()+90
            while not service.healthy(endpoint):
                if process.poll() is not None:raise RuntimeError(f'{which} model startup failed; see its server log')
                if time.monotonic()>deadline:raise TimeoutError('Benchmark server startup')
                time.sleep(.2)
            service.check_model(endpoint,model_id)
            warmup=dict(model=model_id,messages=[dict(role='user',content='只回复：好')],max_tokens=8,
                        chat_template_kwargs=dict(enable_thinking=False))
            req=urllib.request.Request(endpoint+'/v1/chat/completions',json.dumps(warmup).encode(),{'Content-Type':'application/json'})
            with urllib.request.urlopen(req,timeout=80) as response:json.load(response)
            contexts=json.loads((folder/'contexts.json').read_text(encoding='utf-8'))
            for row in contexts['contexts']:
                ctx=row['context'];before=deepcopy(w.export())
                result,ms,usage,raw=LocalModel(config).request(ctx)
                assert before==w.export(),'Inference mutated world before validation'
                accepted,reason=apply_decision(w,ctx,result)
                record=dict(case=row['case'],npc_id=ctx['npc_id'],accepted=accepted,validation=reason,
                            latency_ms=ms,usage=usage,response=result)
                records.append(record)
                print(json.dumps(dict(model=which,case=row['case'],accepted=accepted,seconds=round(ms/1000,3)),ensure_ascii=False),flush=True)
            w.month_step();audit=economics.audit(w)
            executions={str(row['npc_id']):sum(a['source']=='AI' for a in w.npcs[row['npc_id']]['monthly_actions']) for row in records}
            w.checkpoint();saved=json.loads(json.dumps(w.export()));again=World(store=w.store,compact=True,durable=False)
            assert json.loads(json.dumps(again.export()))==saved
            assert abs(audit['cash_residual'])<.002 and max(map(abs,audit['goods_residuals'].values()))<.002
            report=dict(model_id=model_id,command=command,contexts_sha256=hashlib.sha256((folder/'contexts.json').read_bytes()).hexdigest(),
                        decisions=records,requests=sum(r['usage']['request_count'] for r in records),
                        all_applied=all(r['accepted'] for r in records),executed_actions=executions,
                        audit=audit,reload_equal=True,warmup_excluded=True)
            (folder/f'{which}-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
            assert report['all_applied'] and sum(executions.values())>0
    finally:
        if process and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=5)
            except subprocess.TimeoutExpired:process.kill();process.wait()
        w.store.close()


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode',choices=('prepare','old','new'))
    parser.add_argument('folder',type=Path)
    parser.add_argument('--source',type=Path)
    args=parser.parse_args()
    if args.mode=='prepare':
        if args.source is None:parser.error('--source is required for prepare')
        prepare(args.source,args.folder)
    else:run(args.mode,args.folder)
