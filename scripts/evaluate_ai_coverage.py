"""Real local inference, low/wide budgets on matched starts; no mocked decisions."""
import argparse
import hashlib
import json
from pathlib import Path
import sqlite3
import sys
import time

ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from jianghu.engine import World
from jianghu.store import Store
from jianghu.ai import AIManager, DEFAULT_CONFIG


def run(args):
    folder=Path(args.output);folder.mkdir(parents=True,exist_ok=True)
    initial=folder/'ai-start.sqlite'
    if not initial.exists():
        seed=World(42,100,Store(initial),durable=False)
        for _ in range(24):seed.month_step()
        seed.checkpoint();seed.store.close()
    results=[]
    for label,budget,cooldown in [('low',2,6),('wide',24,3)]:
        path=folder/f'ai-{label}-{time.time_ns()}.sqlite'
        src=sqlite3.connect(initial);dst=sqlite3.connect(path);src.backup(dst);dst.close();src.close()
        w=World(store=Store(path),durable=False)
        ai=AIManager({'enabled':True,'reviews_per_month':budget,'cooldown_months':cooldown,'queue_limit':48})
        requests=[];started=time.perf_counter()
        try:
            for month in range(args.months):
                w.month_step();count=ai.schedule(w);print(label,'round',month+1,'scheduled',count,flush=True)
                # Calendar intentionally paused during the experiment; ordinary simulation
                # cost and inference wall time are reported separately, for both arms.
                while ai.pending:
                    ai.collect(w)
                    time.sleep(.05)
                ai.collect(w)
                print(label,'calls',ai.stats['calls'],'applied',w.metrics['ai_applied'],'rejected',ai.stats['rejected'],flush=True)
            w.month_step();w.checkpoint()
            for raw, in w.store.db.execute('SELECT data FROM ai_log'):
                record=json.loads(raw);response=record.get('response') or {}
                requests.append({'npc_id':record['npc_id'],'month':record['month'],'applied':record.get('applied',False),
                                 'action':response.get('action_id'),'baseline':record['context']['baseline_action'],
                                 'reason':response.get('reason'),'traits':record['context']['self']['traits'],
                                 'latency_ms':record['latency_ms'],'error':record.get('error')})
            result={'arm':label,'budget':budget,'months':args.months,'database':str(path),'wall_seconds':time.perf_counter()-started,
                    'calls':ai.stats['calls'],'failures':ai.stats['failed'],'rejected':ai.stats['rejected'],
                    'unique_reviewed':len({r['npc_id'] for r in requests if r['applied']}),
                    'unique_executed':sum(n['last_ai_execution'] is not None for n in w.npcs.values()),
                    'executed_ai_actions':w.metrics.get('ai_executed_actions',0),
                    'changed_reference':sum(r['action']!=r['baseline'] for r in requests if r['applied']),
                    'mean_request_ms':ai.stats['latency_ms']/max(1,ai.stats['successes']),
                    'requests':requests}
            results.append(result)
            (folder/'ai-coverage.json').write_text(json.dumps({'model':DEFAULT_CONFIG['model'],'seed':42,'population':100,
                'prehistory_months':24,'arms':results,'limitations':'Small cohort, same new engine in both arms; coverage and action influence, not a blinded quality rating.',
                'source_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'jianghu').glob('*.py')}},ensure_ascii=False,indent=2),encoding='utf-8')
            print(label,'RESULT',json.dumps({k:result[k] for k in ('calls','unique_reviewed','unique_executed','executed_ai_actions','changed_reference','wall_seconds')},ensure_ascii=False),flush=True)
        finally:
            ai.closed=True;ai.thread.join(timeout=2);w.store.close()


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--months',type=int,default=2);p.add_argument('--output',required=True);run(p.parse_args())
