"""Actual two-stage reasoning, latest-contact rejection and one-month execution."""
import argparse
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import sqlite3
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from jianghu.engine import World
from jianghu.store import Store
from jianghu.ai import context_for,apply_decision,LocalModel,DEFAULT_CONFIG
from jianghu import npc_attention,economics
from jianghu.model_service import ModelService


def run(source,folder,service):
    folder.mkdir(parents=True,exist_ok=True);path=folder/'world.sqlite'
    if path.exists():raise ValueError('Use a fresh validation directory')
    with sqlite3.connect(source.resolve().as_uri()+'?mode=ro',uri=True) as src:
        with sqlite3.connect(path) as dst:src.backup(dst)
    w=World(store=Store(path),compact=True,durable=False);records=[]
    def request(n,name):
        ctx=context_for(w,n,npc_attention.profile(w,n));before=deepcopy(w.export())
        print('REQUEST',name,n['id'],ctx['_thinking']['level'],flush=True)
        response,ms,usage,raw=LocalModel(DEFAULT_CONFIG,service).request(ctx)
        assert before==w.export()
        row=dict(case=name,npc_id=n['id'],context=ctx,response=response,latency_ms=ms,usage=usage)
        records.append(row)
        print(json.dumps(dict(case=name,seconds=round(ms/1000,2),requests=usage['request_count'])),flush=True)
        return ctx,response,row
    high=next(n for n in w.npcs.values() if n['alive'] and n['age_months']>=192 and npc_attention.grade(w,n)<=3 and not npc_attention.contact(w,n))
    ctx,result,row=request(high,'high-standing');assert row['usage']['request_count']==2
    assert apply_decision(w,ctx,result)[0];assert high['deep_thought']['rounds']==2
    recent=next(n for n in w.npcs.values() if n['alive'] and n['age_months']>=192 and npc_attention.profile(w,n)['level']=='standard')
    first=w.intervene(recent['id'],'talk','我想听听你的打算；你可以按自己的处境决定是否帮助乡里。')
    ctx,result,row=request(recent,'recent-contact-before-new-message')
    second=w.intervene(recent['id'],'talk','刚才的建议先别急着答应，请先顾好自己的生计，再考虑与我合作。')
    before=deepcopy(w.export());accepted,reason=apply_decision(w,ctx,result)
    assert not accepted and before==w.export();row.update(accepted=False,rejection=reason)
    ctx,result,row=request(recent,'latest-contact');assert row['usage']['request_count']==2
    assert result['_deep_review']['player_memory_id']==second['id']
    assert apply_decision(w,ctx,result)[0];row['accepted']=True
    assert recent['deep_thought']['level']=='intensive'
    ordinary=next(n for n in w.npcs.values() if n['alive'] and n['age_months']>=192 and n['id']!=recent['id'] and npc_attention.profile(w,n)['level']=='standard')
    ctx,result,row=request(ordinary,'ordinary');assert row['usage']['request_count']==1
    assert apply_decision(w,ctx,result)[0]
    w.month_step();audit=economics.audit(w)
    executions={str(n['id']):sum(a['source']=='AI' for a in n['monthly_actions']) for n in (high,recent,ordinary)}
    assert abs(audit['cash_residual'])<.002 and max(map(abs,audit['goods_residuals'].values()))<.002
    assert executions[str(high['id'])]+executions[str(recent['id'])]>0,executions
    w.checkpoint();saved=json.loads(json.dumps(w.export()));again=World(store=w.store,compact=True,durable=False)
    assert json.loads(json.dumps(again.export()))==saved
    report=dict(real_requests=sum(r['usage']['request_count'] for r in records),decisions=records,latest_contact_cited=True,
                obsolete_contact_rejected_atomically=True,high_and_recent_two_stages=True,ordinary_one_stage=True,
                actual_plan_steps=w.metrics.get('ai_executed_actions',0),tested_actor_executions=executions,audit=audit,reload_equal=True,
                source_hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in Path('jianghu').glob('*.py')})
    (folder/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');w.store.close()


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('source',type=Path);parser.add_argument('folder',type=Path)
    args=parser.parse_args();service=ModelService()
    try:run(args.source,args.folder,service)
    finally:service.close()
