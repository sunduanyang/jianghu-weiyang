"""Multi-generation evidence with lineage, death, office and treasury invariants."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import statistics
import sys
import time

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu.engine import World
from jianghu.store import Store


def run(population, years, seed, durable=False):
    folder=ROOT/'reports'/'generations-v2'
    folder.mkdir(exist_ok=True)
    path=folder/f'world-{population}-{years}years-seed{seed}-{time.time_ns()}.sqlite'
    hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'jianghu').glob('*.py')}
    w=World(seed,population,Store(path),durable=durable)
    snapshots=[];elapsed=[]
    for _ in range(years*12):
        started=time.perf_counter();w.month_step();elapsed.append(time.perf_counter()-started)
        if w.month%120==0:
            s=w.summary()
            row={k:s[k] for k in ('month','population','total_people','marriages','demography')}
            row.update({'births':w.metrics['births'],'deaths':w.metrics['deaths'],
                        'official_changes':w.government['succession_count']})
            snapshots.append(row)
            print(json.dumps({'seed':seed,'population_start':population,'year':w.month//12,
                              'living':s['population'],'births':w.metrics['births'],
                              'deaths':w.metrics['deaths'],'generation':s['demography']['max_generation']+1},ensure_ascii=False),flush=True)
    w.checkpoint()
    errors=Counter()
    dead_month={}
    rows=w.store.db.execute('SELECT data FROM events')
    kinds=Counter()
    examples={}
    for raw, in rows:
        e=json.loads(raw);kinds[e['kind']]+=1
        for cause in e['causes']:
            p=w.store.event(cause)
            if not p or cause>=e['id'] or p['month']>e['month']:errors['causal_edges']+=1
        if e['kind']=='death':
            dead_month[e['facts']['deceased']]=e['month']
            examples.setdefault(e['facts']['cause'],e)
    for n in w.npcs.values():
        if n['generation'] and (len(n['parents'])!=2 or n['generation']!=max(w.npcs[p]['generation'] for p in n['parents'])+1):
            errors['generation']+=1
        for p in n['parents']:
            if n['id'] not in w.npcs[p]['children'] or p>=n['id']:errors['parent_child']+=1
        if n['spouse'] and n['alive']:
            q=w.npcs[n['spouse']]
            if not q['alive'] or q['spouse']!=n['id']:errors['marriage']+=1
        if n['guardian'] and n['alive'] and n['age_months']<192:
            p=w.npcs[n['guardian']]
            if not p['alive'] or p['place']!=n['place']:errors['guardianship']+=1
        if n['wealth'] < -.00001:errors['negative_npc_wealth']+=1
        if not n['alive'] and (not n['death'] or n['wealth']!=0):errors['dead_state']+=1
    holders=[i for i in w.government['offices'].values() if i]
    if len(set(holders))!=len(holders) or any(not w.npcs[i]['alive'] for i in holders):errors['office_holders']+=1
    g=w.government
    residual=g['fund']-(g['opening_fund']+g['revenue']-g['expenses'])
    if abs(residual)>.00001 or g['fund']<-.00001:errors['treasury_balance']+=1
    for npc_id, raw in w.store.db.execute('SELECT npc_id,data FROM experiences'):
        m=json.loads(raw)
        if npc_id in dead_month and m['recorded_month']>dead_month[npc_id]:errors['posthumous_memory']+=1
        e=w.store.event(m['event_id'])
        if e['facts'].get('secret') and npc_id not in e['people']:errors['private_experience']+=1
    s=w.summary()
    result={'seed':seed,'initial_population':population,'years':years,'database':str(path),'source_sha256':hashes,
            'durable_monthly':durable,'wall_seconds':sum(elapsed),'monthly_p95_ms':sorted(elapsed)[int(len(elapsed)*.95)]*1000,
            'births':w.metrics['births'],'deaths':w.metrics['deaths'],'living':s['population'],
            'founders_still_alive':sum(n['alive'] and n['generation']==0 for n in w.npcs.values()),
            'max_generation':s['demography']['max_generation']+1,'population_by_decade':snapshots,
            'death_causes':w.metrics.get('death_causes',{}),'event_kinds':dict(kinds),
            'treasury':{k:g[k] for k in ('fund','opening_fund','revenue','expenses','relief','closed_cases','succession_count')},
            'treasury_residual':residual,'errors':dict(errors),'death_examples':examples}
    output=folder/f'result-{population}-{years}years-seed{seed}.json'
    output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print('RESULT',json.dumps({k:result[k] for k in ('seed','initial_population','years','living','births','deaths','max_generation','errors','wall_seconds')},ensure_ascii=False),flush=True)
    w.store.close()
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--population',type=int,default=100);p.add_argument('--years',type=int,default=120);p.add_argument('--seed',type=int,default=42);p.add_argument('--durable',action='store_true');args=p.parse_args()
    result=run(args.population,args.years,args.seed,args.durable)
    raise SystemExit(1 if result['errors'] else 0)
