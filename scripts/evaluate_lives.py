"""Matched-start behavioral comparison, retaining journals and per-person sequences."""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import sqlite3
import sys
import time

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))


def run(args):
    if args.source:
        sys.path.insert(0,str(Path(args.source)))
    from jianghu.engine import World
    from jianghu.store import Store
    folder=Path(args.output);folder.mkdir(parents=True,exist_ok=True)
    initial=folder/f'initial-{args.population}-seed{args.seed}.sqlite'
    if args.variant=='baseline' and not initial.exists():
        fixture=World(args.seed,args.population,Store(initial),durable=False)
        fixture.checkpoint();fixture.store.close()
    if not initial.exists():raise SystemExit('Run baseline first to freeze the same starting population')
    path=folder/f'{args.variant}-{args.population}-{args.months}-seed{args.seed}-{time.time_ns()}.sqlite'
    src=sqlite3.connect(initial);dst=sqlite3.connect(path);src.backup(dst);dst.close();src.close()
    w=World(store=Store(path),durable=args.durable)
    sequences={};month_counts=[];unique_counts=[];times=[];live_denominator=0;deeds=Counter()
    for _ in range(args.months):
        start=time.perf_counter();w.month_step();times.append(time.perf_counter()-start)
        for n in w.npcs.values():
            if not n['alive'] or n['age_months']<120:continue
            live_denominator+=1
            acts=n.get('monthly_actions')
            if acts is None:
                acts=[n['last_action']] if n['last_action'] and n['last_action']['month']==w.month else []
            kinds=[a['kind'] for a in acts if a.get('role')!='合营伙伴']
            month_counts.append(len(kinds));unique_counts.append(len(set(kinds)))
            deeds.update(kinds);sequences.setdefault(str(n['id']),[]).extend(kinds)
        if w.month%12==0:print(args.variant,args.seed,'year',w.month//12,'population',w.summary()['population'],flush=True)
    w.checkpoint()
    repeat=0;transitions=0
    for seq in sequences.values():
        repeat+=sum(a==b for a,b in zip(seq,seq[1:]));transitions+=max(0,len(seq)-1)
    entropy=-sum((v/sum(deeds.values()))*math.log2(v/sum(deeds.values())) for v in deeds.values())
    bad={'steal','extort','smear','embezzle','betray'}
    negative={k:deeds[k] for k in bad}
    bad_people={i for i,seq in sequences.items() if any(k in bad for k in seq)}
    altruistic={i for i,seq in sequences.items() if any(k in ('aid','care','teach','public_service') for k in seq)}
    invalid_edges=0;private_leaks=0
    for raw, in w.store.db.execute('SELECT data FROM events'):
        e=json.loads(raw)
        for cause in e['causes']:
            parent=w.store.event(cause)
            invalid_edges+=int(not parent or cause>=e['id'] or parent['month']>e['month'])
    for npc_id,raw in w.store.db.execute('SELECT npc_id,data FROM experiences'):
        memory=json.loads(raw);e=w.store.event(memory['event_id'])
        private_leaks+=int(e['facts'].get('secret',False) and npc_id not in e['people'])
    g=w.government
    result={'variant':args.variant,'population':args.population,'seed':args.seed,'months':args.months,'initial_database':str(initial),
            'database':str(path),'durable':args.durable,'adult_person_months':live_denominator,
            'mean_actions_per_adult_month':sum(month_counts)/max(1,len(month_counts)),
            'mean_distinct_kinds_per_adult_month':sum(unique_counts)/max(1,len(unique_counts)),
            'repeat_transition_fraction':repeat/max(1,transitions),'action_entropy_bits':entropy,
            'action_distribution':dict(deeds),'negative_actions':negative,'people_ever_harming':len(bad_people),
            'people_ever_helping':len(altruistic),'people_both_helping_and_harming':len(bad_people&altruistic),
            'wall_seconds':sum(times),'monthly_mean_ms':sum(times)/len(times)*1000,
            'private_leaks':private_leaks,'invalid_causal_edges':invalid_edges,
            'treasury_residual':g['fund']-(g['opening_fund']+g['revenue']-g['expenses']),
            'live_population':w.summary()['population'],'sequences':sequences,
            'source_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in Path(sys.modules[World.__module__].__file__).parent.glob('*.py')}}
    output=folder/f'{args.variant}-{args.population}-{args.months}-seed{args.seed}.json'
    output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:result[k] for k in ('variant','seed','mean_actions_per_adult_month','repeat_transition_fraction','people_ever_harming','people_both_helping_and_harming','wall_seconds')},ensure_ascii=False),flush=True)
    w.store.close()


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--variant',choices=['baseline','current'],required=True);p.add_argument('--source')
    p.add_argument('--seed',type=int,default=42);p.add_argument('--population',type=int,default=100);p.add_argument('--months',type=int,default=60)
    p.add_argument('--durable',action='store_true');p.add_argument('--output',required=True);run(p.parse_args())
