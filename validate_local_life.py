"""Read-only source save, isolated migration/weekly-life/economy acceptance."""
import argparse
from collections import Counter
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import sqlite3
import time

from jianghu.engine import World
from jianghu.store import Store
from jianghu import city_maps, economics, local_life


def norm(value):
    return json.loads(json.dumps(value, ensure_ascii=False))


def validate(source, output, months):
    output.mkdir(parents=True, exist_ok=True)
    copied=output/'world.sqlite'
    if copied.exists():raise ValueError('Use a fresh output directory')
    with sqlite3.connect(source.resolve().as_uri()+'?mode=ro',uri=True) as src:
        original=json.loads(src.execute('SELECT data FROM checkpoint WHERE id=1').fetchone()[0])
        events=src.execute('SELECT count(*) FROM events').fetchone()[0]
        with sqlite3.connect(copied) as dst:src.backup(dst)
    w=World(store=Store(copied),compact=True,durable=False)
    after=norm(w.export())
    for key,value in original.items():
        if key!='local_life':assert after[key]==value,('migration changed',key)
    assert events==w.store.count()
    assert len(w.local_life['people'])==sum(n['alive'] for n in w.npcs.values())
    snapshot=deepcopy(w.export()); rows=[]; seen=[]
    for p in w.active_places:
        city=city_maps.view(w,p['id'])
        homes=[q['id'] for t in city['settlements'] for q in t['home_residents']]
        occupants=[q['id'] for t in city['settlements'] for q in t['residents']]
        assert Counter(homes)==Counter(w.residents[p['id']])
        assert Counter(occupants)==Counter(w.residents[p['id']])
        seen+=homes
        for t in city['settlements']:
            rows.append(dict(place=p['name'],site=t['name'],kind=t['kind'],homes=len(t['home_residents']),present=len(t['residents']),workers=len(t['workers'])))
    assert len(seen)==len(set(seen))
    for n in list(w.npcs.values())[:20]:w.npc_detail(n['id'])
    assert snapshot==w.export(),'public views mutated the world'
    iterations=[]; visit_count=0; delivery_count=0; freight=0.
    for _ in range(months):
        start=time.perf_counter();first=w.store.next_id;w.month_step();seconds=time.perf_counter()-start
        audit=economics.audit(w)
        assert abs(audit['cash_residual'])<.002,audit
        assert max(map(abs,audit['goods_residuals'].values()))<.002,audit
        assert not audit['negative_market_cash'] and not audit['negative_stock']
        for (data,) in w.store.db.execute('SELECT data FROM events WHERE id>=?',(first,)):
            e=json.loads(data);visit=e.get('facts',{}).get('local_visit')
            if visit:
                assert len(visit['participants'])==2
                assert set(visit['participants'])<=set(e['people'])
                visit_count+=1
            delivery=e.get('facts',{}).get('production',{}).get('local_delivery')
            if delivery and delivery['goods'] and delivery['origin']!=delivery['destination']:
                delivery_count+=1;freight+=delivery['paid']
        for n in w.npcs.values():
            if not n['alive']:continue
            row=local_life.entry(w,n);assert row['home']['place']==n['place']
            pos=local_life.current(w,n);city=city_maps._world_layout(w,n['place'])
            assert city['rows'][pos['row']][pos['col']] not in '~^#'
            # Two explicitly scheduled responses must never overlap the same week.
            responses=[a for a in n['monthly_actions'] if a.get('source')=='回应邀约']
            assert len({a['week'] for a in responses})==len(responses)
        iterations.append(dict(month=w.month,population=sum(n['alive'] for n in w.npcs.values()),seconds=round(seconds,3),cash_residual=audit['cash_residual'],max_goods_residual=max(map(abs,audit['goods_residuals'].values()))))
        print(json.dumps(iterations[-1]),flush=True)
    w.checkpoint();saved=norm(w.export());again=World(store=w.store,compact=True,durable=False)
    assert norm(again.export())==saved,'Reload changed registered homes or world state'
    report=dict(migration_preserves_all_existing_fields=True,migration_creates_no_events=True,
                views_read_only=True,homes_unique=True,weekly_encounters=visit_count,
                real_deliveries=delivery_count,freight_paid=round(freight,4),rows=rows,months=iterations,
                reload_equal=True,audit=economics.audit(w),
                source_hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(Path('jianghu').glob('*.py'))})
    (output/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    w.store.close()
    print(json.dumps({k:v for k,v in report.items() if k not in ('rows','source_hashes','audit')},ensure_ascii=True),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('source',type=Path);parser.add_argument('output',type=Path);parser.add_argument('--months',type=int,default=12)
    args=parser.parse_args();validate(args.source,args.output,args.months)
