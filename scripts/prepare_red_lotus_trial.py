"""Create a new, private save for testing the supplied red-lotus parts atlas."""
import argparse
import json
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu import appearance,protagonist
from jianghu.engine import World
from jianghu.store import Store


def main(folder):
    folder=folder.resolve()
    if not folder.is_relative_to(ROOT/'output'):raise ValueError('A private output folder is required')
    folder.mkdir(parents=True,exist_ok=True);path=folder/'world.sqlite'
    if path.exists():raise FileExistsError(path)
    w=World(188,0,Store(path),durable=False)
    try:
        protagonist.perform(w,dict(action='create',revision=0,month=0,name='沈行舟',sex='男',age=18,place=45))
        n=w.make_npc(place=45);n.update(name='江修尘星',sex='男',age_months=28*12,job='镖师',health=100.,wealth=200.,time_left=4)
        n.pop('appearance',None);appearance.prepare(w,n);w.set_goal(n,'mastery',initial=True)
        w.player['ties'][str(n['id'])]=dict(affinity=70.,trust=70.)
        w.reindex();w.checkpoint()
        (folder/'fixture.json').write_text(json.dumps(dict(npc=n['id'],trial_art_saved=False),ensure_ascii=False),encoding='utf-8')
        print(path)
    finally:w.store.close()


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('folder',type=Path);main(p.parse_args().folder)
