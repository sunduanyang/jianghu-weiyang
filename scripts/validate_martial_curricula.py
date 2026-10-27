"""Prepare isolated native fixtures and export the complete nine-grade catalog."""
import argparse
from copy import deepcopy
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu import martial, martial_catalog, combat_attributes, protagonist, items
from jianghu.engine import World
from jianghu.store import Store


def prepare(output):
    output=output.resolve()
    if not output.is_relative_to(ROOT/'output'):
        raise ValueError('Native fixtures must stay inside workspace/output')
    output.mkdir(parents=True,exist_ok=True)
    for label,weapon in [('native-martial-save',None),('native-roundtrip-save','great_sword')]:
        folder=output/label;folder.mkdir(exist_ok=True)
        w=World(42,40,Store(folder/'world.sqlite'),load=False,durable=False,compact=True)
        try:
            for n in w.npcs.values():n.update(age_months=360,health=100.)
            h=w.player['protagonist'];place=h['place']
            protagonist.perform(w,dict(action='create',revision=h['revision'],month=w.month,
                                      name='九品武学验收',sex='男',age=24,place=place))
            w.player['combat_attributes']['base']=dict.fromkeys(combat_attributes.KEYS,50.)
            w.player['combat_attributes']['baseline_version']=combat_attributes.TUNING['version']
            h.update(faction=0,contribution=12.)
            n=w.npcs[1];n.update(faction=None,job='短工',office=None,social_status=None)
            n['career']['profession']='短工'
            for f in w.factions:
                if f['leader'] in (-1,1):f['leader']=None
            if weapon:
                item=items.create(w,'player',weapon,'九品武学原生验收')
                w.player['equipment']['weapon']=item['id']
            w.checkpoint()
            view=w.martial_view()['player']['martial']
            assert view['curriculum']['grade']==5 and len(view['arts'])==5
            (output/(label+'.json')).write_text(json.dumps(view,ensure_ascii=False,indent=2),encoding='utf-8')
        finally:w.store.close()
    (output/'catalog.json').write_text(json.dumps(martial_catalog.catalog(),ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(output=str(output),schools=14,arts=126),ensure_ascii=False))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=ROOT/'output/martial-nine-grades-20261028')
    prepare(parser.parse_args().output)
