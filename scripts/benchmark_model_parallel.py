"""Isolated 1-vs-2-slot NPC inference experiment; never changes game settings."""
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
from copy import deepcopy
import csv
import hashlib
import io
import json
from pathlib import Path
import socket
import sqlite3
import statistics
import subprocess
import sys
import threading
import time
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu.ai import DEFAULT_CONFIG, LocalModel, context_for, apply_decision
from jianghu.engine import World
from jianghu.store import Store
from jianghu import npc_attention
from jianghu.model_service import ModelService, model_command


def read_json(url):
    with urllib.request.urlopen(url,timeout=3) as response:return json.load(response)


def save_json(path,value):
    path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')


def digest(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()


def backup(source,target):
    if target.exists():raise ValueError(f'Use a fresh experiment path: {target}')
    with sqlite3.connect(source.resolve().as_uri()+'?mode=ro',uri=True) as src:
        with sqlite3.connect(target) as dst:src.backup(dst)


def gpu_sample():
    result=subprocess.run(['nvidia-smi','--query-gpu=memory.used,utilization.gpu,temperature.gpu,power.draw',
                           '--format=csv,noheader,nounits'],capture_output=True,text=True,timeout=10,
                          creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0),check=True)
    values=next(csv.reader(io.StringIO(result.stdout)))
    return dict(memory_mib=float(values[0]),utilization_percent=float(values[1]),
                temperature_c=float(values[2]),power_w=float(values[3]))


def prepare(source,folder):
    folder.mkdir(parents=True,exist_ok=False)
    backup(source,folder/'base-world.sqlite')
    world=World(store=Store(folder/'base-world.sqlite'),compact=True,durable=False)
    try:
        standard=[n for n in world.npcs.values() if n['alive'] and n['age_months']>=192
                  and npc_attention.profile(world,n)['level']=='standard']
        high=[n for n in world.npcs.values() if n['alive'] and n['age_months']>=192
              and npc_attention.grade(world,n)<=3 and not npc_attention.contact(world,n)]
        ordinary=standard[:2];recent=standard[2:4]
        assert len(ordinary)==len(recent)==2 and len(high)>=2
        for person in recent:
            world.intervene(person['id'],'talk','请先顾好自己的生计，再考虑是否与我合作；不必急着答应。')
        cases=[]
        for index in range(2):
            for label,person in [('ordinary',ordinary[index]),('high-standing',high[index]),('recent-contact',recent[index])]:
                cases.append(dict(case=f'{label}-{index+1}',context=context_for(world,person,npc_attention.profile(world,person))))
        assert len({row['context']['npc_id'] for row in cases})==6
        save_json(folder/'contexts.json',cases)
        world.checkpoint()
    finally:world.store.close()


def stop_process(process):
    if process is not None and process.poll() is None:
        process.terminate()
        try:process.wait(timeout=5)
        except subprocess.TimeoutExpired:process.kill();process.wait()


def arm(folder,label,parallel,port):
    endpoint=f'http://127.0.0.1:{port}'
    with socket.socket() as probe:
        if probe.connect_ex(('127.0.0.1',port))==0:raise RuntimeError('Experiment port is occupied')
    config={**DEFAULT_CONFIG,'url':endpoint,'enabled':True}
    command,_=model_command(config)
    command[command.index('--parallel')+1]=str(parallel)
    # llama.cpp divides this total context across slots: keep 8192 for EACH NPC.
    command[command.index('-c')+1]=str(8192*parallel)
    cases=json.loads((folder/'contexts.json').read_text(encoding='utf-8'))
    backup(folder/'base-world.sqlite',folder/f'{label}-world.sqlite')
    world=World(store=Store(folder/f'{label}-world.sqlite'),compact=True,durable=False)
    process=None;gpu=None;monitor=None;stop=threading.Event();samples=[];records=[]
    baseline_gpu=gpu_sample()
    try:
        with (folder/f'{label}-server.log').open('wb') as log:
            process=subprocess.Popen(command,cwd=Path(command[0]).parent,stdout=log,stderr=subprocess.STDOUT,
                                     creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
            service=ModelService();deadline=time.monotonic()+90
            while not service.healthy(endpoint):
                if process.poll() is not None:raise RuntimeError('Model startup failed; inspect server log')
                if time.monotonic()>deadline:raise TimeoutError('Model startup')
                time.sleep(.2)
            service.check_model(endpoint,config['model'])
            initial_slots=read_json(endpoint+'/slots')
            assert len(initial_slots)==parallel
            def warmup(index):
                body=dict(model=config['model'],messages=[dict(role='user',content=('测试预热。'*100)+str(index))],
                          max_tokens=16,chat_template_kwargs=dict(enable_thinking=False))
                request=urllib.request.Request(endpoint+'/v1/chat/completions',json.dumps(body).encode(),{'Content-Type':'application/json'})
                with urllib.request.urlopen(request,timeout=80) as response:json.load(response)
            with ThreadPoolExecutor(max_workers=parallel) as pool:
                list(pool.map(warmup,range(2)))
            def watch_slots():
                while not stop.is_set():
                    try:
                        slots=read_json(endpoint+'/slots')
                        samples.append(dict(at=time.perf_counter(),processing=sum(bool(s.get('is_processing')) for s in slots)))
                    except Exception:pass
                    stop.wait(.4)
            monitor=threading.Thread(target=watch_slots,daemon=True);monitor.start()
            with (folder/f'{label}-gpu.csv').open('w',encoding='utf-8') as gpu_log:
                gpu=subprocess.Popen(['nvidia-smi','--query-gpu=timestamp,memory.used,utilization.gpu,temperature.gpu,power.draw',
                                      '--format=csv,noheader,nounits','--loop-ms=500'],stdout=gpu_log,stderr=subprocess.DEVNULL,
                                     creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
                before=deepcopy(world.export())
                started=time.perf_counter()
                def request(index):
                    row=cases[index];begin=time.perf_counter()
                    try:
                        result,latency,usage,raw=LocalModel(config).request(deepcopy(row['context']))
                        return dict(index=index,case=row['case'],npc_id=row['context']['npc_id'],response=result,
                                    latency_ms=latency,usage=usage,queue_ms=(begin-started)*1000,
                                    finished_ms=(time.perf_counter()-started)*1000)
                    except Exception as error:
                        return dict(index=index,case=row['case'],npc_id=row['context']['npc_id'],error=str(error),
                                    usage=getattr(error,'usage',{}),latency_ms=(time.perf_counter()-begin)*1000,
                                    queue_ms=(begin-started)*1000,finished_ms=(time.perf_counter()-started)*1000)
                with ThreadPoolExecutor(max_workers=parallel) as pool:
                    for future in as_completed([pool.submit(request,i) for i in range(len(cases))]):
                        row=future.result();records.append(row)
                        print(json.dumps(dict(arm=label,done=len(records),case=row['case'],seconds=round(row['latency_ms']/1000,2),
                                              error=row.get('error')),ensure_ascii=True),flush=True)
                elapsed=time.perf_counter()-started
                stop.set();monitor.join(timeout=5);stop_process(gpu)
            assert before==world.export(),'Inference mutated world before validation'
            for row in sorted(records,key=lambda r:r['index']):
                if 'error' in row:row.update(accepted=False,validation=row['error'])
                else:
                    accepted,reason=apply_decision(world,cases[row['index']]['context'],row['response'])
                    row.update(accepted=accepted,validation=reason)
            accepted=sum(r['accepted'] for r in records)
            gpu_rows=[]
            for values in csv.reader((folder/f'{label}-gpu.csv').read_text(encoding='utf-8').splitlines()):
                if len(values)>=5:
                    try:gpu_rows.append([float(v.strip()) for v in values[1:5]])
                    except ValueError:pass
            report=dict(arm=label,parallel=parallel,per_slot_context=8192,total_context=8192*parallel,
                        command=command,contexts_sha256=digest(folder/'contexts.json'),jobs=len(cases),accepted=accepted,
                        elapsed_seconds=elapsed,valid_decisions_per_minute=accepted*60/elapsed,
                        mean_service_seconds=statistics.mean(r['latency_ms']/1000 for r in records),
                        mean_queue_to_finish_seconds=statistics.mean(r['finished_ms']/1000 for r in records),
                        request_count=sum(r['usage'].get('request_count',0) for r in records),
                        completion_tokens=sum(r['usage'].get('completion_tokens',0) for r in records),
                        prompt_tokens=sum(r['usage'].get('prompt_tokens',0) for r in records),
                        observed_max_processing_slots=max((s['processing'] for s in samples),default=0),
                        baseline_gpu=baseline_gpu,peak_total_gpu_mib=max((r[0] for r in gpu_rows),default=None),
                        mean_gpu_utilization=statistics.mean(r[1] for r in gpu_rows) if gpu_rows else None,
                        peak_temperature_c=max((r[2] for r in gpu_rows),default=None),
                        decisions=records,warmup_excluded=True)
            world.checkpoint();save_json(folder/f'{label}-report.json',report)
            save_json(folder/f'{label}-slots.json',samples)
            print(json.dumps({k:report[k] for k in ('arm','parallel','accepted','elapsed_seconds','valid_decisions_per_minute','observed_max_processing_slots','peak_total_gpu_mib')}),flush=True)
            if report['observed_max_processing_slots']!=parallel:raise RuntimeError('Requested concurrency was not observed')
            return report
    finally:
        stop.set()
        if monitor:monitor.join(timeout=5)
        stop_process(gpu);stop_process(process);world.store.close()


def run(source,folder,port,order=(1,2,2,1)):
    source_hash=digest(source)
    protected=[ROOT/'jianghu/ai.py',ROOT/'jianghu/model_service.py',ROOT/'.runtime/manifest.json',
               ROOT/'data/ai_config.json',ROOT/'unity/Build/active-build.txt',
               Path(r'C:\Users\User\AppData\LocalLow\JianghuStudio\江湖未央 Unity\Saves\data\ai_config.json')]
    before={str(p):digest(p) for p in protected if p.exists()}
    prepare(source,folder)
    save_json(folder/'environment-before.json',dict(source_sha256=source_hash,protected_files=before))
    reports=[]
    for index,parallel in enumerate(order,1):
        label=f'{index:02d}-'+('single' if parallel==1 else 'dual')
        reports.append(arm(folder,label,parallel,port))
        save_json(folder/'progress.json',reports)
    after={str(p):digest(p) for p in protected if p.exists()}
    assert digest(source)==source_hash,'Input save changed during the experiment'
    summaries=[]
    for parallel in (1,2):
        arms=[r for r in reports if r['parallel']==parallel]
        elapsed=sum(r['elapsed_seconds'] for r in arms);accepted=sum(r['accepted'] for r in arms)
        summaries.append(dict(parallel=parallel,runs=len(arms),jobs=sum(r['jobs'] for r in arms),accepted=accepted,
                              total_seconds=elapsed,valid_decisions_per_minute=accepted*60/elapsed,
                              mean_service_seconds=statistics.mean(r['mean_service_seconds'] for r in arms),
                              mean_queue_to_finish_seconds=statistics.mean(r['mean_queue_to_finish_seconds'] for r in arms),
                              peak_total_gpu_mib=max(r['peak_total_gpu_mib'] for r in arms)))
    report=dict(model=DEFAULT_CONFIG['model'],order=[r['arm'] for r in reports],unique_contexts=6,
                summaries=summaries,arms=reports,source_save_unchanged=True,production_settings_unchanged=(before==after),
                externally_changed_protected_files=[p for p in set(before)|set(after) if before.get(p)!=after.get(p)],
                limitations='Six unique NPC contexts repeated in the specified order; one machine, other apps remain open. GPU memory is whole-device usage; game FPS was not measured. Direct HTTP worker pool simulates proposed parallel scheduling; production AIManager remains single-worker.')
    save_json(folder/'report.json',report)
    print('FINAL',json.dumps(summaries),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path);parser.add_argument('output',type=Path)
    parser.add_argument('--port',type=int,default=11438)
    parser.add_argument('--order',default='1,2,2,1',help='Comma-separated 1/2-slot round order')
    args=parser.parse_args()
    try:order=tuple(map(int,args.order.split(',')))
    except ValueError:parser.error('--order must contain only 1 and 2')
    if not order or set(order)-{1,2} or set(order)!={1,2}:parser.error('--order must include both 1 and 2')
    run(args.source.resolve(),args.output.resolve(),args.port,order)
