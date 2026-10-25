"""Offline career validation, with a separate journal and report for each run."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu import careers
from jianghu.engine import World
from jianghu.store import Store


def run(population, months, seed):
    folder = ROOT / 'reports' / 'careers'
    folder.mkdir(parents=True, exist_ok=True)
    name = f'{population}people-{months}months-seed{seed}-{time.time_ns()}'
    path = folder / f'{name}.sqlite'
    started = time.perf_counter()
    w = World(seed=seed, population=population, store=Store(path), durable=False)
    try:
        for _ in range(months):
            w.month_step()
        errors, counts = Counter(), Counter()
        for raw, in w.store.db.execute('SELECT data FROM events ORDER BY id'):
            e = json.loads(raw)
            counts[e['kind']] += 1
            for cause in e['causes']:
                if not w.store.event(cause) or cause >= e['id']:
                    errors['invalid_cause'] += 1
            if e['kind'] in ('learn_trade', 'treatment'):
                f = e['facts']
                teacher = f.get('teacher', f.get('doctor'))
                if teacher not in e['people'] or f.get('payment') not in (2, 4):
                    errors['invalid_professional_payment'] += 1
            if e['kind'] == 'change_job':
                f = e['facts']
                if f['setup_cost'] != careers.PROFESSIONS[f['after']]['cost']:
                    errors['invalid_setup_cost'] += 1
        for n in w.npcs.values():
            if n['wealth'] < 0:
                errors['negative_wealth'] += 1
            if n['alive'] and n['identity'] != careers.identity(w, n):
                errors['stale_identity'] += 1
            if n['age_months'] >= 192 and n['job'] != n['career']['profession']:
                errors['inconsistent_profession'] += 1
            if any(not 0 <= v <= 100 for v in n['career']['experience'].values()):
                errors['invalid_experience'] += 1
        g = w.government
        residual = g['fund']-(g['opening_fund']+g['revenue']-g['expenses'])
        if abs(residual) > .00001 or g['fund'] < 0:
            errors['treasury_balance'] += 1
        if w.metrics['invalid_actions']:
            errors['invalid_actions'] += w.metrics['invalid_actions']
        w.checkpoint()
        # Reload the saved checkpoint without modifying the evidence journal.
        restored = World(store=w.store, durable=False)
        if restored.npcs != w.npcs or restored.rng.getstate() != w.rng.getstate():
            errors['checkpoint_roundtrip'] += 1
        result = {'seed': seed, 'initial_population': population, 'months': months,
                  'living': sum(n['alive'] for n in w.npcs.values()),
                  'wall_seconds': round(time.perf_counter()-started, 3),
                  'database': str(path), 'ai_calls': 0, 'durable_monthly': False,
                  'source_sha256': {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                                    for p in sorted((ROOT / 'jianghu').glob('*.py'))},
                  'events': dict(counts), 'errors': dict(errors), 'treasury_residual': residual,
                  'professions': w.summary()['professions'], 'identity_grades': w.summary()['identity_grades']}
        output = folder / f'{name}.json'
        output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps({'report': str(output), 'errors': dict(errors), 'living': result['living'],
                          'wall_seconds': result['wall_seconds'],
                          'career_actions': {k: counts[k] for k in ('learn_trade', 'change_job', 'identity_change')}},
                         ensure_ascii=False), flush=True)
        return result
    finally:
        w.store.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--population', type=int, default=100)
    parser.add_argument('--months', type=int, default=240)
    parser.add_argument('--seed', type=int, default=42)
    args = parser.parse_args()
    if not 1 <= args.population <= 1000 or not 1 <= args.months <= 1200:
        parser.error('population must be 1..1000; months must be 1..1200')
    result = run(args.population, args.months, args.seed)
    raise SystemExit(bool(result['errors']))
