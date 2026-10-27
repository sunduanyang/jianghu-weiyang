"""Actual local-model proposals and their execution on a copied society."""
import argparse
from copy import deepcopy
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import sys
sys.path.insert(0,os.environ.get('JIANGHU_VALIDATION_BACKEND',str(Path(__file__).resolve().parents[1])))
from jianghu.ai import context_for,apply_decision,LocalModel,DEFAULT_CONFIG
from jianghu.engine import World
from jianghu.store import Store
from jianghu import npc_plans,economics,llm_policy
from jianghu.model_service import ModelService


def run(source,folder,service):
    folder.mkdir(parents=True,exist_ok=True);target=folder/'world.sqlite'
    if target.exists():raise ValueError('Use a fresh validation folder')
    with sqlite3.connect(source.resolve().as_uri()+'?mode=ro',uri=True) as src:
        with sqlite3.connect(target) as dst:src.backup(dst)
    w=World(store=Store(target),compact=True,durable=False);results=[];actors=[]
    for role in ('disciple','elder','merchant','healer'):
        n=next(n for n in w.npcs.values() if n['alive'] and n['age_months']>=192 and n['wealth']>=12 and n['health']>=65 and npc_plans.role(w,n)['key']==role)
        actors.append(n);before=deepcopy(w.export());ctx=context_for(w,n);assert before==w.export()
        print('REQUEST',n['id'],role,flush=True)
        response,ms,usage,raw=LocalModel(DEFAULT_CONFIG,service).request(ctx)
        wallets={i:q['wealth'] for i,q in w.npcs.items()};month=w.month;events=w.store.count()
        applied,reason=apply_decision(w,ctx,response)
        assert applied,(reason,response)
        assert {i:q['wealth'] for i,q in w.npcs.items()}==wallets and w.month==month
        assert llm_policy.current(w,n) is not None
        row=dict(npc_id=n['id'],role=role,context=ctx,response=response,applied=applied,
                 validation=reason,latency_ms=ms,usage=usage)
        results.append(row);w.store.log_ai(row)
        print(json.dumps(dict(npc_id=n['id'],applied=applied,seconds=round(ms/1000,2))),flush=True)
    monthly=[]
    for _ in range(4):
        w.month_step();audit=economics.audit(w)
        assert abs(audit['cash_residual'])<.002 and max(map(abs,audit['goods_residuals'].values()))<.002,audit
        row=dict(month=w.month,ai_plan_steps=w.metrics.get('ai_executed_actions',0),
                 policy_actions=w.metrics.get('ai_policy_actions',0),
                 policy_ranking_changes=w.metrics.get('ai_policy_ranking_changes',0),audit=audit)
        monthly.append(row);print(json.dumps({k:v for k,v in row.items() if k!='audit'}),flush=True)
    assert monthly[-1]['ai_plan_steps']>0 and monthly[-1]['policy_actions']>0
    w.checkpoint();state=json.loads(json.dumps(w.export()));loaded=World(store=w.store,compact=True,durable=False)
    assert json.loads(json.dumps(loaded.export()))==state
    result=dict(real_requests=len(results),decisions=results,monthly=monthly,reload_equal=True,
                backend_origin=sys.modules['jianghu.ai'].__file__,
                actor_plans=[dict(id=n['id'],policy=n.get('llm_policy'),last_action=n['last_action']) for n in actors],
                source_hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in Path('jianghu').glob('*.py')})
    (folder/'report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    w.store.close()


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('source',type=Path);parser.add_argument('folder',type=Path)
    args=parser.parse_args();service=ModelService()
    try:run(args.source,args.folder,service)
    finally:service.close()
