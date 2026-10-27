"""Audit a regenerated snapshot and advance an isolated copy for twelve months."""
from collections import Counter
from copy import deepcopy
import json
from pathlib import Path
import sqlite3
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu import careers, items, population, social_ranks, territory
from jianghu.engine import World
from jianghu.store import Store


def audit(w):
    errors = []
    def check(condition, message):
        if not condition:
            errors.append(message)
    for n in w.npcs.values():
        check(n['wealth'] >= -.00001, f"negative assets {n['id']}")
        if n['alive'] and n['age_months'] < 192 and n['guardian']:
            g = w.npcs[n['guardian']]
            check(g['alive'] and g['place'] == n['place'], f"guardian {n['id']}")
        check(n['identity'] == careers.identity(w, n), f"identity {n['id']}")
        for i in n['inventory']:
            check(w.items['objects'][str(i)]['owner'] == str(n['id']), f"item {i}")
        slots = []
        for action in n.get('monthly_actions', []):
            slots += list(range(action['week'], action['week'] + action['time_cost']))
        check(len(slots) == len(set(slots)), f"schedule {n['id']}")
    holders = []
    for g in w.governments:
        check(g['fund'] >= -.00001 and abs(g['fund'] - (g['opening_fund'] + g['revenue'] - g['expenses'])) < .0001,
              f"government ledger {g['country']}")
        for title, i in g['offices'].items():
            if i:
                n = w.npcs[i]
                check(n['alive'] and n['office'] == title and n['office_country'] == g['country'], f"office {i}")
                holders.append(i)
        view = social_ranks.country_view(w, g['country'])
        check(view['population'] + view['sect_population'] ==
              sum(n['alive'] and territory.owner(w, n['place']) == g['country'] for n in w.npcs.values()),
              f"country census {g['country']}")
    check(len(holders) == len(set(holders)), 'duplicate officials')
    for f in w.factions:
        check(f['fund'] >= -.00001 and abs(f['fund'] - (f['opening_fund'] + f['revenue'] - f['expenses'])) < .0001,
              f"sect ledger {f['id']}")
        if f['leader']:
            leader = w.npcs[f['leader']]
            check(leader['alive'] and leader['faction'] == f['id'], f"leader {f['id']}")
    for c in w.society['communities'].values():
        check(c['fund'] >= -.00001 and abs(c['fund'] - (c['opening_fund'] + c['revenue'] - c['expenses'])) < .0001,
              f"community ledger {c['place']}")
    return errors


def run(source, folder):
    folder.mkdir(parents=True, exist_ok=True)
    target = folder / 'simulation-12-months.sqlite'
    if target.exists():
        raise ValueError('验证副本已存在')
    with sqlite3.connect(source.as_uri() + '?mode=ro', uri=True) as src, sqlite3.connect(target) as dst:
        src.backup(dst)
    w = World(store=Store(target), compact=True, durable=False)
    started = time.perf_counter()
    results = []
    try:
        errors = audit(w)
        assert not errors, errors[:10]
        initial = population.census(w)
        for month in range(1, 13):
            before = time.perf_counter()
            w.month_step()
            errors = audit(w)
            results.append(dict(months=month, month=w.month, living=w.summary()['population'],
                                seconds=round(time.perf_counter() - before, 3), errors=errors))
            print(json.dumps(results[-1], ensure_ascii=False), flush=True)
            assert not errors, errors[:10]
        w.checkpoint()
        expected = json.dumps(w.export(), sort_keys=True, ensure_ascii=False)
        event_count = w.store.count()
        loaded = World(store=w.store, compact=True, population_profile=True, durable=False)
        assert json.dumps(loaded.export(), sort_keys=True, ensure_ascii=False) == expected
        assert w.store.count() == event_count
        events = {i: json.loads(data) for i, data in w.store.db.execute('SELECT id,data FROM events')}
        assert all(c in events and c < i and events[c]['month'] <= e['month']
                   for i, e in events.items() for c in e['causes'])
        report = dict(source=str(source), simulation_copy=str(target), initial_census=initial,
                      months=12, monthly_results=results, exact_reload=True, causal_edges_valid=True,
                      real_model_calls=w.store.db.execute('SELECT count(*) FROM ai_log').fetchone()[0],
                      seconds=round(time.perf_counter() - started, 3), final_census=population.census(w),
                      events=len(events), invalid_actions=w.metrics['invalid_actions'])
        (folder / 'simulation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        return report
    finally:
        w.store.close()


if __name__ == '__main__':
    report = run(Path(sys.argv[1]).resolve(strict=True), Path(sys.argv[2]).resolve())
    print(json.dumps({k: report[k] for k in ('months', 'seconds', 'events', 'invalid_actions', 'exact_reload')}))
