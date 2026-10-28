"""Create a fresh isolated recruitment fixture, without reading a player save."""
import argparse
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu import protagonist, rank_progression
from jianghu.engine import World
from jianghu.store import Store


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--save-dir', type=Path, required=True)
    folder = parser.parse_args().save_dir.resolve()
    if not folder.is_relative_to(ROOT/'output'):
        parser.error('Acceptance saves must be inside the repository output directory')
    folder.mkdir(parents=True, exist_ok=True)
    path = folder/'world.sqlite'
    if path.exists():
        parser.error('Refusing to overwrite an existing save')
    w = World(93, 0, Store(path), compact=True, durable=False, rank_assessments=True)
    try:
        protagonist.perform(w, dict(action='create', revision=0, month=0,
                                    name='入门验收', sex='女', age=24, place=45))
        people = []
        for grade, contribution, title in ((1, 80., '掌门'), (2, 70., '护法长老'), (3, 35., '长老'), (4, 24., '堂主')):
            n = w.make_npc(place=45)
            n.update(name='验收'+title, faction=0, contribution=contribution, faction_since=0,
                     age_months=360, health=100., wealth=100.)
            w.set_goal(n, 'mastery', initial=True)
            rank_progression.prepare(w, n['id'])['sect']['grade'] = grade
            if grade == 1:
                w.factions[0]['leader'] = n['id']
            people.append(n['id'])
        w.reindex(); w.checkpoint()
        (folder/'fixture.json').write_text(json.dumps(dict(faction=0, people=people), ensure_ascii=False), encoding='utf-8')
        print(json.dumps(dict(save_dir=str(folder), people=people), ensure_ascii=False))
    finally:
        w.store.close()


if __name__ == '__main__':
    main()
