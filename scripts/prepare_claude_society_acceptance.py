"""Create an isolated new-world fixture for the Claude PR #13-18 native acceptance."""
import argparse
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu import economics, fame, npc_plans, protagonist, undertakings
from jianghu.engine import World
from jianghu.store import Store


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--save-dir', type=Path, required=True)
    folder = parser.parse_args().save_dir.resolve()
    if not folder.is_relative_to(ROOT / 'output'):
        parser.error('Acceptance saves must stay inside repository output')
    folder.mkdir(parents=True, exist_ok=True)
    path = folder / 'world.sqlite'
    if path.exists():
        parser.error('Refusing to overwrite an existing save')
    w = World(7, 1800, Store(path), compact=True, durable=False, population_profile=True,
              market_economy=True, rank_assessments=True)
    try:
        place = w.active_places[0]['id']
        family = next(n for n in w.npcs.values() if n['parents'])
        protagonist.perform(w, dict(action='create', revision=0, month=0,
                                    name='社会融合验收', sex='女', age=24, place=place))
        # A named pair makes the new plan and target navigation deterministic.
        # These are test actors in an isolated save, not edits to any player's world.
        pair = []
        for name, sex in [('筹划验收人物', '女'), ('筹划验收对象', '男')]:
            n = w.make_npc(place=place)
            n.update(name=name, sex=sex, age_months=300, health=100., wealth=100.,
                     faction=None, office=None, spouse=None, orientation='异性', pregnancy=None)
            n['traits'].update(kindness=90., integrity=90., boldness=30.)
            n['mood'].update(anger=0., joy=0., fear=0.)
            w.set_goal(n, 'belonging', initial=True)
            pair.append(n)
        n, q = pair
        w.link(n, q, 20, 40)
        w.reindex()
        intent, title, data = undertakings.propose(w, n)
        assert intent == 'courtship' and data['target'] == q['id']
        npc_plans.start(w, n, intent, title, [], **data)
        n['reputation'] = 80.
        for _ in range(12):
            fame.spread(w, n, place)
        assert len(fame.view(w, n)['towns']) > 1
        w.checkpoint()
        fixture = dict(person=n['id'], target=q['id'], family=family['id'], month=w.month,
                       households=w.population_generation['households'],
                       fame=fame.view(w, n), silver=economics.total_cash(w),
                       opening_audit=economics.audit(w),
                       note='Fresh 1800-person world plus two explicit acceptance actors; no personal save or model calls.')
        (folder / 'society-fixture.json').write_text(json.dumps(fixture, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps(dict(save_dir=str(folder), **fixture), ensure_ascii=False))
    finally:
        w.store.close()


if __name__ == '__main__':
    main()
