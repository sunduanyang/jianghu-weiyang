"""Validate all region layouts and old/new simulation parity on copied saves."""
from collections import Counter
from copy import deepcopy
import json
from pathlib import Path
import sqlite3
import sys
from jianghu.engine import World
from jianghu.store import Store
from jianghu import city_maps,economics


def norm(value):return json.loads(json.dumps(value,ensure_ascii=False))


def run(source,folder):
    folder=Path(folder);folder.mkdir(parents=True,exist_ok=True)
    paths=[folder/'before.sqlite',folder/'after.sqlite']
    if any(p.exists() for p in paths):raise ValueError('Use a fresh validation directory')
    with sqlite3.connect(source) as db:
        before=json.loads(db.execute('SELECT data FROM checkpoint WHERE id=1').fetchone()[0])
        for path in paths:
            with sqlite3.connect(path) as dst:db.backup(dst)
    worlds=[World(store=Store(path),compact=True,durable=False) for path in paths]
    old,new=worlds
    old.city_map_version=7;old.city_settlement_profiles={}
    for key in ('row','col'):old.player['protagonist'][key]=before['player']['protagonist'][key]
    assert norm(new.npcs)==before['npcs'];assert norm(new.items)==before['items']
    assert norm(new.economy)==before['economy'];assert norm(new.rng.getstate())==before['random_state']
    assert new.month==before['month'] and new.player['protagonist']['points']==before['player']['protagonist']['points']
    events=new.store.count();snapshot=deepcopy(new.export());rows=[]
    for place in new.active_places:
        legacy=city_maps._world_layout(old,place['id']);city=city_maps.view(new,place['id'])
        plan=city['settlement_layout'];ordinary=[t for t in city['settlements'] if t['kind']!='sect']
        assert sorted(t['footprint']['size']**2 for t in ordinary)==sorted(plan['cells'])
        assert [dict(id=t['id'],footprint=t['footprint']) for t in city['settlements'] if t['kind']=='sect']==[dict(id=t['id'],footprint=t['footprint']) for t in legacy['settlements'] if t['kind']=='sect']
        for key in ('rows','elevations','terrain_tiles'):assert city[key]==legacy[key],(place['name'],key)
        assert {n['id'] for n in city['nodes']}=={n['id'] for n in legacy['nodes']}
        occupants=[n['id'] for t in city['settlements'] for n in t['residents']]
        assert Counter(occupants)==Counter(new.residents[place['id']])
        assert all(n['path'] for n in city['nodes'])
        assert all(t['path'] for t in city['settlements'])
        assert all(index>=0 for row in city['cell_areas'] for index in row)
        rows.append(dict(id=place['id'],name=place['name'],tier=plan['tier'],population=city['population'],cells=plan['cells'],ordinary=len(ordinary),sects=plan['sect_count']))
    assert new.export()==snapshot and new.store.count()==events,'Inspection changed the world'
    for w in worlds:w.month_step()
    for key in ('npcs','economy','factions','governments','city_positions'):
        assert getattr(old,key)==getattr(new,key),('Layout changed the simulation',key)
    assert old.rng.getstate()==new.rng.getstate()
    audit=economics.audit(new)
    assert abs(audit['cash_residual'])<.001 and all(abs(v)<.001 for v in audit['goods_residuals'].values())
    new.checkpoint();saved=deepcopy(new.export());reloaded=World(store=new.store,compact=True,durable=False)
    assert reloaded.export()==saved,'Reload changed the profile or migration'
    report=dict(regions=len(rows),population_before=sum(n['alive'] for n in before['npcs'].values()),
        old_ordinary=4*len(rows),new_ordinary=sum(r['ordinary'] for r in rows),classes=dict(Counter(r['tier'] for r in rows)),
        sects=sum(r['sects'] for r in rows),rows=rows,geography_preserved=True,sects_preserved=True,
        all_facilities_reachable=True,all_people_accounted=True,read_only_views=True,migration_preserved=True,
        one_month_simulation_parity=True,reload_equal=True,audit=audit)
    (folder/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    for w in worlds:w.store.close()
    print(json.dumps({k:v for k,v in report.items() if k!='rows'},ensure_ascii=False),flush=True)


if __name__=='__main__':run(sys.argv[1],sys.argv[2])
