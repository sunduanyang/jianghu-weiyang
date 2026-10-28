"""Long-run society metrics used by the 2026-10 validation documents.

Runs a fresh world (same configuration as a new game) for N months and writes a
JSON summary: half-yearly snapshots (population, wealth, health, marriage,
friendship, mood, goals), event and action counts, crime concentration, and
long-undertaking outcomes. Optionally also writes a checkpoint summary at an
earlier month, so one long run yields both the 36- and 60-month tables.

    python -X utf8 scripts/society_metrics.py 60 out.json --checkpoint 36 [--seed 7] [--people 1800]
"""
import argparse
import collections
import json
from pathlib import Path
import statistics
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu.engine import World

CRIME = {'secret_theft', 'theft_exposed', 'extortion', 'extortion_resisted', 'slander', 'slander_exposed',
         'betrayal', 'embezzlement'}


def gini(xs):
    xs = sorted(max(0, x) for x in xs)
    n, s = len(xs), sum(xs)
    return 0 if not s else sum((2*i - n + 1) * x for i, x in enumerate(xs)) / (n * s)


def snapshot(w, month, seconds):
    alive = [n for n in w.npcs.values() if n['alive']]
    adults = [n for n in alive if n['age_months'] >= 192]
    wealth = [n['wealth'] for n in adults]
    return dict(
        month=month, seconds=round(seconds, 1), alive=len(alive), adults=len(adults),
        children=sum(n['age_months'] < 192 for n in alive),
        wealth_median=round(statistics.median(wealth), 1), gini=round(gini(wealth), 3),
        poor10=round(sum(x < 10 for x in wealth) / len(wealth), 3),
        health=round(statistics.mean(n['health'] for n in alive), 1),
        wounded=round(sum(bool(n.get('wound')) for n in alive) / len(alive), 3),
        married=round(sum(n['spouse'] is not None for n in adults) / len(adults), 3),
        close_friends=round(statistics.mean(sum(r['affinity'] >= 40 for r in n['relationships'].values()) for n in adults), 2),
        enemies=round(statistics.mean(sum(r['affinity'] <= -25 for r in n['relationships'].values()) for n in adults), 2),
        joy=round(statistics.mean(n['mood']['joy'] for n in alive), 1),
        arts_learned=round(statistics.mean(len(n['martial'].get('learned', [])) for n in adults), 2),
        goals=dict(collections.Counter(n['goal']['kind'] for n in adults).most_common()))


def summary(w, snaps, started):
    kinds, actions, per_actor = collections.Counter(), collections.Counter(), collections.defaultdict(collections.Counter)
    for (data,) in w.store.db.execute('select data from events'):
        e = json.loads(data)
        kinds[e['kind']] += 1
        actor = e.get('facts', {}).get('actor')
        if isinstance(actor, int):
            actions[e['kind']] += 1
            per_actor[actor][e['kind']] += 1
    adults = [n for n in w.npcs.values() if n['age_months'] >= 192]
    crimes = sorted((sum(per_actor[n['id']][k] for k in CRIME) for n in adults), reverse=True)
    total = sum(crimes) or 1
    return dict(snapshots=snaps, events=dict(kinds.most_common()), actions=dict(actions.most_common()),
                metrics={k: v for k, v in w.metrics.items() if isinstance(v, (int, float))},
                undertakings=w.metrics.get('undertakings'),
                crime_total=sum(crimes), crime_top10_share=round(sum(crimes[:max(1, len(crimes)//10)]) / total, 3),
                never_offended=round(sum(c == 0 for c in crimes) / len(crimes), 3),
                events_total=w.store.count(), seconds=round(time.time() - started, 1))


def main():
    p = argparse.ArgumentParser()
    p.add_argument('months', type=int)
    p.add_argument('out')
    p.add_argument('--checkpoint', type=int)
    p.add_argument('--seed', type=int, default=7)
    p.add_argument('--people', type=int, default=1800)
    args = p.parse_args()
    started = time.time()
    w = World(args.seed, args.people, compact=True, durable=False, population_profile=True,
              market_economy=True, rank_assessments=True)
    snaps = [snapshot(w, 0, time.time() - started)]
    for m in range(1, args.months + 1):
        t = time.time()
        w.month_step()
        if m % 6 == 0:
            snaps.append(snapshot(w, m, time.time() - t))
            print(json.dumps(snaps[-1], ensure_ascii=False), flush=True)
        if m == args.checkpoint and m < args.months:
            out = Path(args.out)
            json.dump(summary(w, list(snaps), started), open(out.with_name(f'{out.stem}_m{m}{out.suffix}'), 'w'),
                      ensure_ascii=False, indent=1)
    json.dump(summary(w, snaps, started), open(args.out, 'w'), ensure_ascii=False, indent=1)


if __name__ == '__main__':
    main()
