"""Reproducible isolated-world validation; never opens the player's save."""
from collections import Counter
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from jianghu.engine import World
from jianghu.store import Store


def run(population, months, seed):
    w = World(population=population, seed=seed, compact=True, durable=False)
    started = time.perf_counter()
    try:
        for _ in range(months):
            w.month_step()
            for c in w.society['communities'].values():
                assert c['fund'] >= 0
                assert abs(c['fund'] - (c['opening_fund'] + c['revenue'] - c['expenses'])) < .0001
            for n in w.npcs.values():
                assert n['wealth'] >= -.0001
                assert n['time_left'] >= 0
                occupied = []
                for a in n['monthly_actions']:
                    occupied.extend(range(a['week'], a['week'] + a['time_cost']))
                assert len(occupied) == len(set(occupied)), (n['id'], n['monthly_actions'])
        counts = Counter(row[0] for row in w.store.db.execute('select kind from events'))
        w.checkpoint()
        expected = json.loads(json.dumps(w.export()))
        rng, count = w.rng.getstate(), w.store.count()
        loaded = World(store=w.store, compact=True, durable=False)
        assert loaded.society == w.society
        assert loaded.rng.getstate() == rng and w.store.count() == count
        assert json.loads(json.dumps(loaded.export())) == expected
        return {'population_at_start': population, 'months': months, 'seed': seed,
                'seconds': round(time.perf_counter() - started, 2), 'living': w.summary()['population'],
                'events': w.store.count(), 'social_events': {k: counts[k] for k in
                    ('household_support', 'community_relief', 'donate_relief', 'neighbor_care', 'guardianship')},
                'invalid_actions': w.metrics['invalid_actions'], 'society': w.summary()['society'],
                'checks': {'funds_nonnegative': True, 'accounts_balance': True, 'no_schedule_overlap': True,
                           'exact_save_reload': True, 'no_new_events_or_rng_on_reload': True}}
    finally:
        w.store.close()


if __name__ == '__main__':
    report = {'verified_at': datetime.now(timezone(timedelta(hours=8))).isoformat(),
              'save_policy': 'Fresh in-memory worlds; no player saves opened.', 'real_model_calls': 0,
              'runs': [run(100, 120, 42), run(800, 24, 20261026)]}
    path = ROOT / 'validation' / 'social-livelihood.json'
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({**report, 'runs': [{k: v for k, v in row.items() if k != 'society'}
                                       for row in report['runs']]}, ensure_ascii=False, indent=2))
