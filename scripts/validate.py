"""Reproducible multi-decade / population probes; retains the full event databases."""
import argparse
import hashlib
from collections import Counter
import json
from pathlib import Path
import statistics
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu.engine import World, TRAITS
from jianghu.store import Store


def probe(population, months, seed, durable):
    path=ROOT/'reports'/f'simulation-{population}-{months}-seed{seed}-{time.time_ns()}.sqlite'
    w=World(seed,population,Store(path),durable=durable)
    elapsed=[]
    for _ in range(months):
        t=time.perf_counter();w.month_step();elapsed.append((time.perf_counter()-t)*1000)
    w.checkpoint()
    bad_causes=0;private_leaks=0
    for (raw,) in w.store.db.execute('SELECT data FROM events'):
        e=json.loads(raw)
        for i in e['causes']:
            parent=w.store.event(i)
            bad_causes += not parent or i>=e['id'] or parent['month']>e['month']
    for n in w.npcs.values():
        for m in n['memories']:
            e=w.store.event(m['event_id'])
            if e['facts'].get('secret') and n['id'] not in e['people']:
                private_leaks+=1
    age_goals=[(w.month-n['goal']['since'])/12 for n in w.npcs.values() if n['alive']]
    drift=[abs(n['traits'][k]-n['initial_traits'][k]) for n in w.npcs.values() for k in TRAITS]
    dominant=Counter(n['last_action']['kind'] for n in w.npcs.values() if n['last_action'])
    kinds=dict(w.store.db.execute('SELECT kind,count(*) FROM events GROUP BY kind').fetchall())
    marriage_errors=sum(bool(n['spouse']) and n['alive'] and
                        (not w.npcs[n['spouse']]['alive'] or w.npcs[n['spouse']]['spouse']!=n['id']) for n in w.npcs.values())
    treasury_errors=sum(g['fund'] < -.0001 or abs(g['fund']-g['opening_fund']-g['revenue']+g['expenses']) > .001
                        for g in w.governments)
    office_errors=sum(not w.npcs[i]['alive'] or w.npcs[i]['office'] != office or
                      w.npcs[i]['office_country'] != g['country']
                      for g in w.governments for office, i in g['offices'].items() if i)
    result={k:v for k,v in w.summary().items() if k not in ('npcs','places','factions','player')}
    result.update({'initial_population':population,'simulated_years':months/12,'durable_monthly_snapshot':durable,
                   'source_sha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'jianghu').glob('*.py')},
                   'wall_seconds':sum(elapsed)/1000,'monthly_wall_mean_ms':statistics.mean(elapsed),
                   'monthly_wall_p95_ms':sorted(elapsed)[int(.95*(len(elapsed)-1))],
                   'trait_drift_mean':statistics.mean(drift),'trait_drift_max':max(drift),
                   'active_goal_age_years_median':statistics.median(age_goals),
                   'last_action_distribution':dict(dominant),'event_kinds':kinds,
                   'bad_causal_edges':bad_causes,'private_memory_leaks':private_leaks,
                   'marriage_errors':marriage_errors,'treasury_errors':treasury_errors,
                   'office_errors':office_errors,'database':str(path)})
    w.store.close()
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--populations',nargs='+',type=int,default=[100,500,1000]);p.add_argument('--months',type=int,default=120);p.add_argument('--seed',type=int,default=42);p.add_argument('--durable',action='store_true');p.add_argument('--output',default='simulation_results.json');args=p.parse_args()
    results=[]
    for count in args.populations:
        print('Simulating',count,flush=True)
        result=probe(count,args.months,args.seed,args.durable);results.append(result)
        print(json.dumps({k:result[k] for k in ('initial_population','population','wall_seconds','events','marriages','feuds','bad_causal_edges','marriage_errors')},ensure_ascii=False),flush=True)
        (ROOT/'reports'/args.output).write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
