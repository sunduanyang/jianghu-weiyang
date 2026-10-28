"""Prepare an isolated native-client fixture; never open the user's save."""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from jianghu.engine import World
from jianghu.store import Store
from jianghu import protagonist, martial, items


def prepare(folder):
    folder.mkdir(parents=True, exist_ok=True)
    path = folder/'world.sqlite'
    if path.exists():
        raise ValueError('Fixture directory already contains a save; use a fresh directory')
    w = World(42, 0, Store(path), durable=False)
    try:
        protagonist.perform(w, dict(action='create', revision=0, month=0, name='秘籍验收', sex='女', age=24, place=45))
        n = w.make_npc(place=45)
        n.update(name='授业师父', age_months=480, health=100., time_left=4)
        n['traits'].update(loyalty=80., compassion=80.)
        n['martial']['learned'] = ['shaolin_g9']
        w.set_goal(n, 'mastery', initial=True)
        martial.sync_actor(w, n)
        items.create(w, n['id'], items.MANUAL_TEMPLATES['shaolin_g9'], '老师备下的授业秘籍')
        w.reindex();w.checkpoint()
        (folder/'data').mkdir(exist_ok=True)
        (folder/'data'/'ai_config.json').write_text(json.dumps({'enabled': False}), encoding='utf-8')
        print(json.dumps({'database': str(path), 'teacher': n['id'], 'art': 'shaolin_g9'}, ensure_ascii=False))
    finally:
        w.store.close()


if __name__ == '__main__':
    p = argparse.ArgumentParser();p.add_argument('save_dir', type=Path)
    prepare(p.parse_args().save_dir)
