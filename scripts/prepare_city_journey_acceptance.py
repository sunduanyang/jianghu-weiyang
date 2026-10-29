"""Create a long route to a one-cell village in a new isolated save."""
import argparse,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from jianghu import protagonist,city_maps
from jianghu.atlas import PLACES
from jianghu.engine import World
from jianghu.store import Store

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--save-dir',type=Path,required=True)
    folder=parser.parse_args().save_dir.resolve()
    if not folder.is_relative_to(ROOT/'output'):parser.error('Fixture must stay under repository output')
    folder.mkdir(parents=True,exist_ok=False)
    w=World(42,0,Store(folder/'world.sqlite'),load=False,durable=False,compact=True)
    try:
        place=next(p['id'] for p in PLACES if p['name']=='克鲁伦河营地')
        protagonist.perform(w,dict(action='create',month=0,revision=0,name='分月行旅验收',sex='男',age=24,place=place))
        city=city_maps.world_layout(w,place);h=w.player['protagonist']
        site=next(s for s in city['settlements'] if s['id']=='settlement_2')
        route,cost=city_maps.path_between(city['rows'],(h['row'],h['col']),(site['row'],site['col']))
        assert cost==35 and h['points']==30,(cost,h['points'])
        w.checkpoint()
        (folder/'fixture.json').write_text(json.dumps(dict(seed=42,place=place,target=site['id'],cost=cost,path=route),ensure_ascii=False,indent=2),encoding='utf-8')
        print(folder)
    finally:w.store.close()
if __name__=='__main__':main()
