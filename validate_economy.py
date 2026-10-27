"""Isolated, repeatable economy acceptance against a copied save."""
import json
import sqlite3
import sys
import time
from pathlib import Path
from jianghu.engine import World
from jianghu.store import Store
from jianghu import economics, social


def run(source, output, months=12):
    output = Path(output); output.mkdir(parents=True, exist_ok=True)
    target = output / 'economy-world.sqlite'
    if target.exists():
        raise ValueError('Use a fresh acceptance directory')
    with sqlite3.connect(source) as src, sqlite3.connect(target) as dst:
        src.backup(dst)
    w = World(store=Store(target), durable=False, market_economy=True)
    start = w.month
    rows = []
    for _ in range(months):
        began = time.perf_counter(); w.month_step()
        living = [n for n in w.npcs.values() if n['alive']]
        row = dict(month=w.month, population=len(living), seconds=round(time.perf_counter()-began,3),
                   hungry=sum(n.get('monthly_settlement',{}).get('food_shortfall',0) > .01 for n in living),
                   satisfaction=round(sum(economics.prepare(n)['satisfaction'] for n in living)/len(living),4),
                   shipments=w.store.db.execute("SELECT count(*) FROM events WHERE kind IN ('work','trade') AND json_extract(data, '$.facts.production.shipment') IS NOT NULL").fetchone()[0],
                   audit=economics.audit(w))
        rows.append(row); print(json.dumps(row, ensure_ascii=False), flush=True)
        assert abs(row['audit']['cash_residual']) < .03, row
        assert all(abs(x) < .001 for x in row['audit']['goods_residuals'].values()), row
        assert not row['audit']['negative_market_cash'] and not row['audit']['negative_stock'], row
        assert all(n['wealth'] >= -.0001 for n in w.npcs.values())
        w.checkpoint()
    w.checkpoint()
    before=w.export(); w.store.close()
    restored=World(store=Store(target), durable=False, market_economy=True)
    assert restored.export()==before, 'Reload changed the world'
    assert all(abs(g['fund']-g['opening_fund']-g['revenue']+g['expenses']) < .001 for g in restored.governments)
    report=dict(source=str(source),copy=str(target),start_month=start,months=months,rows=rows,
                produced=economics.state(restored)['produced'],consumed=economics.state(restored)['consumed'],
                spoiled=economics.state(restored)['spoiled'],reload_equal=True,
                actions=restored.metrics['actions'],deaths=restored.metrics['death_causes'])
    (output/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    restored.store.close()


if __name__=='__main__':
    run(sys.argv[1],sys.argv[2],int(sys.argv[3]) if len(sys.argv)>3 else 12)
