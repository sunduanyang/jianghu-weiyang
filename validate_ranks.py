"""Assessment acceptance runs only on a SQLite backup, never on the active save."""
from copy import deepcopy
import json
from pathlib import Path
import sqlite3
import sys
import time
from jianghu.engine import World
from jianghu.store import Store
from jianghu import economics, careers, rank_progression as ranks, organizations


def run(source,folder,months=12):
    folder=Path(folder);folder.mkdir(parents=True,exist_ok=True);target=folder/'world.sqlite'
    if target.exists():raise ValueError('Use a fresh validation directory')
    with sqlite3.connect(source) as src,sqlite3.connect(target) as dst:src.backup(dst)
    w=World(store=Store(target),durable=False,compact=True,market_economy=True,rank_assessments=False)
    before=deepcopy(w.export());standings={i:careers.identity(w,n) for i,n in w.npcs.items()}
    ranks.initialize(w)
    assert w.rng.getstate()==before['random_state'] and w.month==before['month']
    assert all(n['wealth']==before['npcs'][i]['wealth'] for i,n in w.npcs.items())
    assert all(careers.identity(w,n)==standings[i] for i,n in w.npcs.items())
    rows=[]
    for _ in range(months):
        started=time.perf_counter();w.month_step();live=[n for n in w.npcs.values() if n['alive']]
        audit=economics.audit(w)
        assert abs(audit['cash_residual'])<.01 and all(abs(v)<.001 for v in audit['goods_residuals'].values()),audit
        assert not audit['negative_stock'] and not audit['negative_market_cash']
        assert all(n['wealth']>=-.0001 for n in w.npcs.values())
        assert all(1<=careers.identity(w,n)['grade']<=9 for n in live)
        assert all(organizations.rank(w,n)['grade']!=1 or w.factions[n['faction']]['leader']==n['id'] for n in live if n['faction'] is not None)
        assert all(n['rank_progress']['last_review']==w.month for n in live if n.get('monthly_settlement',{}).get('month')==w.month)
        events=w.store.db.execute("SELECT json_extract(data,'$.facts.direction'),count(*) FROM events WHERE month>? AND kind='identity_change' GROUP BY json_extract(data,'$.facts.direction')",(before['month'],)).fetchall()
        row=dict(month=w.month,population=len(live),seconds=round(time.perf_counter()-started,3),changes=dict(events),
                 hungry=sum(n.get('monthly_settlement',{}).get('food_shortfall',0)>.01 for n in live),audit=audit)
        rows.append(row);print(json.dumps(row,ensure_ascii=False),flush=True);w.checkpoint()
    previous=deepcopy(w.export());count=w.store.count();w.store.close()
    w=World(store=Store(target),durable=False,compact=True,market_economy=True)
    assert w.export()==previous and w.store.count()==count,'Reload changed an assessment'
    ranks.monthly(w);assert w.export()==previous,'Same-month review repeated'
    assert all(abs(g['fund']-g['opening_fund']-g['revenue']+g['expenses'])<.001 for g in w.governments)
    report=dict(source=str(source),copy=str(target),months=months,start_month=before['month'],migration_preserved=True,reload_equal=True,rows=rows)
    (folder/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');w.store.close()


if __name__=='__main__':run(sys.argv[1],sys.argv[2],int(sys.argv[3]) if len(sys.argv)>3 else 12)
