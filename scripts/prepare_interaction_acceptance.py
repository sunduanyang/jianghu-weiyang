"""Create a new, isolated fixture for -interactionGameplaySmoke; never copy a live save."""
import argparse
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu import items, martial, martial_catalog, protagonist
from jianghu.engine import World
from jianghu.store import Store


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--save-dir', type=Path, required=True)
    folder=parser.parse_args().save_dir.resolve()
    if not folder.is_relative_to(ROOT/'output'):
        parser.error('Acceptance saves must be under this repository output directory')
    folder.mkdir(parents=True, exist_ok=True)
    path=folder/'world.sqlite'
    if path.exists(): parser.error('Refusing to overwrite an existing acceptance database')
    w=World(92,0,Store(path),durable=False)
    try:
        protagonist.perform(w,dict(action='create',revision=0,month=0,name='交互验收',sex='女',age=24,place=45))
        ids=[]
        for label in ('甲','乙','丙'):
            n=w.make_npc(place=45);n.update(name='验收'+label,age_months=360,health=100.,wealth=200.,time_left=4,spouse=None,orientation='皆可')
            n['traits'].update(loyalty=90.,sociability=90.,compassion=90.,caution=50.)
            n['mood'].update(anger=0.,joy=0.)
            w.set_goal(n,'mastery',initial=True)
            art=next(k for k in martial_catalog.ARTS if not items.knows(w,'player',k))
            n['martial']['learned'].append(art);martial.sync_actor(w,n)
            w.player['ties'][str(n['id'])]=dict(affinity=80.,trust=70.)
            for _ in range(5):items.create(w,n['id'],'herb','验收世界初始实物')
            ids.append(n['id'])
        for _ in range(10):items.create(w,'player','herb','验收世界初始实物')
        w.player['wealth']=1000.;w.reindex();w.checkpoint()
        (folder/'fixture.json').write_text(json.dumps(dict(people=ids),ensure_ascii=False),encoding='utf-8')
        print(json.dumps(dict(save_dir=str(folder),people=ids),ensure_ascii=False))
    finally:w.store.close()


if __name__=='__main__':main()
