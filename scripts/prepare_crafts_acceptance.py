"""Prepare a fresh, isolated save for Unity's crafts acceptance."""
import argparse,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from jianghu.engine import World
from jianghu.store import Store
from jianghu import protagonist,crafts,items

def main():
 p=argparse.ArgumentParser();p.add_argument('--save-dir',required=True,type=Path);folder=p.parse_args().save_dir.resolve()
 if not folder.is_relative_to(ROOT/'output'):p.error('Fixture must remain under repository output')
 folder.mkdir(parents=True,exist_ok=True);db=folder/'world.sqlite'
 if db.exists():p.error('Refusing to overwrite an existing save')
 w=World(29029,0,Store(db),durable=False)
 try:
  protagonist.perform(w,dict(action='create',revision=0,month=0,name='技艺验收',sex='女',age=24,place=45))
  w.player['wealth']=2000.;w.player['protagonist']['points']=60
  crafts.prepare(w.player)['skill'].update(forging=0.,medicine=30.,letters=30.)
  w.player['wound']=dict(remaining=4,event_id=0,parts={'torso':dict(severity=.4,kind='cut')});w.player['health']=85.
  people=[]
  for title in ('铸剑师','抢夺对手','切磋对手','袭击对手'):
   n=w.make_npc(place=45);n.update(name=title,age_months=360,health=100.,wealth=300.,time_left=4,spouse=None)
   n['traits'].update(integrity=90.,sociability=90.,kindness=90.,boldness=50.);n['mood'].update(anger=0.,joy=0.)
   w.set_goal(n,'mastery',initial=True);n['wound']=None;n['illness']=None;w.player['ties'][str(n['id'])]=dict(affinity=60.,trust=70.);people.append(n['id'])
  smith=w.npcs[people[0]];smith['job']='匠人';smith['career'].update(profession='匠人',experience={'匠人':70.,'药师':0.,'书生':0.})
  repair=items.create(w,'player','iron_sword','修补验收');repair['durability']=12.;w.player['equipment']['weapon']=repair['id']
  for _ in range(14):items.create(w,'player','iron','技艺验收铁料')
  for _ in range(8):items.create(w,'player','herb','技艺验收药草');items.create(w,'player','salve','技艺验收金疮药')
  items.create(w,'player','hammer','技艺验收工具');w.reindex();w.checkpoint()
  (folder/'crafts-fixture.json').write_text(json.dumps(dict(people=people,repair_id=repair['id']),ensure_ascii=False),encoding='utf-8')
  print(folder)
 finally:w.store.close()
if __name__=='__main__':main()
