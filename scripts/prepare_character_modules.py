"""Create a separate migration/render acceptance save, never a player's save."""
import argparse
import json
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu import appearance,protagonist
from jianghu.engine import World
from jianghu.store import Store


def main(folder):
    folder=folder.resolve()
    if not folder.is_relative_to(ROOT/'output'):raise ValueError('An isolated output folder is required')
    folder.mkdir(parents=True,exist_ok=True);path=folder/'world.sqlite'
    if path.exists():raise FileExistsError(path)
    w=World(103,0,Store(path),durable=False)
    try:
        protagonist.perform(w,dict(action='create',revision=0,month=0,name='青岚',sex='女',age=28,place=45))
        for i in range(48):
            n=w.make_npc(place=45);n.update(name=f'人物{i+1:02}',sex='男' if i%2==0 else '女',age_months=(28 if i<24 else 12 if i<36 else 70)*12,health=100.,time_left=4)
            n.pop('appearance',None);appearance.prepare(w,n)
            w.set_goal(n,'mastery',initial=True)
            if i%3==0:
                n['appearance']['version']=1
                for key in appearance.MODULES:n['appearance'].pop(key,None)
            w.player['ties'][str(n['id'])]=dict(affinity=70.,trust=70.)
        w.reindex();w.checkpoint();(folder/'fixture.json').write_text(json.dumps({'people':48,'legacy_profiles':16,'month':w.month},ensure_ascii=False),encoding='utf-8')
        print(path)
    finally:w.store.close()


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('folder',type=Path);main(p.parse_args().folder)
