"""Create a fresh combat fixture; never use or modify a user's save."""
import argparse
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu.engine import World
from jianghu.store import Store
from jianghu import protagonist


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--save-dir', type=Path, required=True)
    folder = parser.parse_args().save_dir.resolve()
    if not folder.is_relative_to(ROOT / 'output'):
        parser.error('Acceptance fixtures must stay under repository output')
    folder.mkdir(parents=True, exist_ok=True)
    path = folder / 'world.sqlite'
    if path.exists():
        parser.error('Refusing to overwrite an existing database')
    world = World(42, 30, Store(path), durable=False, compact=True)
    try:
        protagonist.perform(world, dict(action='create', revision=0, month=0,
                                        name='物理验收', sex='男', age=24, place=45))
        opponent = world.npcs[1]
        opponent.update(place=45, home=45, age_months=288, health=100., alive=True)
        world.reindex()
        world.checkpoint()
        print('Prepared isolated combat fixture: ' + str(folder))
    finally:
        world.store.close()


if __name__ == '__main__':
    main()
