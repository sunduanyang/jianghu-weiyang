"""Advance a copy of the actual 1800-person save and audit planning consequences."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import sqlite3
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu.engine import World
from jianghu.store import Store
from scripts.validate_population import audit


def run(source, folder, months=12):
    source, folder = Path(source).resolve(strict=True), Path(folder).resolve()
    folder.mkdir(parents=True, exist_ok=True)
    target = folder / 'simulation.sqlite'
    if target.exists():
        raise ValueError('验证副本已存在，请选择新目录')
    with sqlite3.connect(source.as_uri() + '?mode=ro', uri=True) as src, sqlite3.connect(target) as dst:
        src.backup(dst)
    w = World(store=Store(target), compact=True, durable=False)
    rows = []; start = time.perf_counter()
    try:
        initial_month = w.month; initial_population = len(w.npcs)
        for elapsed in range(1, months + 1):
            tick = time.perf_counter(); w.month_step(); errors = audit(w)
            row = dict(months=elapsed, month=w.month, population=w.summary()['population'], errors=errors,
                       active_workflows=sum(n['alive'] and bool(n['plan']) and n['plan'].get('intent') in ('career_change', 'sect_training') for n in w.npcs.values()),
                       completed_workflows=w.metrics.get('workflows_completed', 0),
                       interruptions=w.metrics.get('workflows_interrupted', 0), seconds=round(time.perf_counter() - tick, 3))
            rows.append(row); print(json.dumps(row, ensure_ascii=False), flush=True)
            assert not errors, errors[:10]
            assert w.metrics['invalid_actions'] == 0
            if elapsed == 2:
                native = folder / 'native-save'; native.mkdir(exist_ok=True)
                w.checkpoint()
                with sqlite3.connect(native / 'world.sqlite') as dst:
                    w.store.db.backup(dst)
                person = next(n for n in w.npcs.values() if n['plan'] and n['plan'].get('intent') in ('career_change', 'sect_training'))
                (folder / 'native-person.txt').write_text(str(person['id']), encoding='utf-8')
        w.checkpoint(); before = json.dumps(w.export(), ensure_ascii=False, sort_keys=True); count = w.store.count()
        again = World(store=w.store, compact=True, durable=False)
        assert json.dumps(again.export(), ensure_ascii=False, sort_keys=True) == before
        assert w.store.count() == count
        events = {i: json.loads(data) for i, data in w.store.db.execute('SELECT id,data FROM events')}
        assert all(c in events and c < i and events[c]['month'] <= e['month'] for i, e in events.items() for c in e['causes'])
        kinds = Counter(e['kind'] for e in events.values())
        result = dict(source=str(source), initial_month=initial_month, initial_population=initial_population,
                      months=months, monthly=rows, exact_reload=True, causal_edges_valid=True,
                      real_model_calls=w.store.db.execute('SELECT count(*) FROM ai_log').fetchone()[0],
                      metrics={k: v for k, v in w.metrics.items() if k.startswith(('plans_', 'workflows_'))},
                      actions={k: kinds[k] for k in ('faction_guide', 'faction_study', 'career', 'work')},
                      seconds=round(time.perf_counter() - start, 3),
                      source_sha256={p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT / 'jianghu').glob('*.py')})
        (folder / 'simulation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps({k: result[k] for k in ('metrics', 'actions', 'seconds')}, ensure_ascii=False), flush=True)
        return result
    finally:
        w.store.close()


if __name__ == '__main__':
    run(sys.argv[1], sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 12)
