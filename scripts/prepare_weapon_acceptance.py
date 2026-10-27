"""Create a fresh isolated save and fixture for Unity's weapons acceptance phase."""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from jianghu import items, physical_combat, weapons
from jianghu.engine import World
from jianghu.store import Store


def prepare(output):
    output = output.resolve()
    save = output / 'native-save'
    if (save / 'world.sqlite').exists():
        raise SystemExit('Choose a fresh output directory; acceptance never replaces an existing save.')
    save.mkdir(parents=True, exist_ok=True)
    w = World(42, 4, Store(save / 'world.sqlite'), compact=True,
              market_economy=True, rank_assessments=True, durable=False)
    try:
        w.player['wealth'] = 10000
        for actor in [w.player, *w.npcs.values()]:
            actor['combat_attributes']['base'] = dict.fromkeys(actor['combat_attributes']['base'], 50.)
            actor['health'] = 100.
        for npc in w.npcs.values():
            npc.update(age_months=300, alive=True, place=w.player['protagonist']['place'])
        for _ in range(110):
            items.create(w, 'player', 'iron', '无承重验收')
        cases = []
        original = w.player['equipment']['weapon']
        for key in weapons.CATALOG:
            item = items.create(w, 'player', key, '兵器参数验收')
            w.player['equipment']['weapon'] = item['id']
            fighter = physical_combat.fighter(w.player, 'player', physical_combat.equipment(w, 'player'))
            cases.append(dict(template=key, fighter=fighter))
        w.player['equipment']['weapon'] = original
        w.checkpoint()
        fixture = dict(character_damage_scale=cases[0]['fighter']['damage_scale'], cases=cases)
        (output / 'fixture.json').write_text(json.dumps(fixture, ensure_ascii=False, indent=2), encoding='utf-8')
    finally:
        w.store.close()
    print(output)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    prepare(parser.parse_args().output)
