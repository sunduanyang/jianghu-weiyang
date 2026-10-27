"""Real local-model interaction checks; only creates isolated fixture saves."""
import argparse
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import shutil
import sys
import time

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu import ai,careers,local_life,player_social,protagonist
from jianghu.engine import World
from jianghu.store import Store


def fixture(path):
    w=World(population=0,store=Store(path),compact=True,durable=True)
    place=w.active_places[0]['id']
    names=['陆怀安','赵小川','宋知远','周利生']
    for name in names:
        n=w.make_npc(place=place)
        n.update(name=name,age_months=360,wealth=80.,health=100.,job='农人',faction=None,
                 office=None,social_status=None,spouse=None,children=[],guardian=None)
        n['career'].update(profession='农人',experience={'农人':20.})
        w.set_goal(n,'wealth',initial=True)
        n['identity']=careers.identity(w,n)
    w.npcs[1]['traits'].update(compassion=95.,loyalty=90.,greed=5.,honesty=90.,sociability=85.)
    w.npcs[2]['wealth']=5.
    w.npcs[4]['traits'].update(compassion=5.,loyalty=15.,greed=95.,honesty=30.)
    w.reindex();local_life.reconcile(w)
    protagonist.perform(w,dict(action='create',revision=0,month=w.month,name='沈行舟',sex='男',age=24,place=place))
    w.player['ties']['1']=dict(affinity=60.,trust=40.)
    w.player['ties']['4']=dict(affinity=-60.,trust=-50.)
    w.link(w.npcs[1],w.npcs[3],40,20)
    w.checkpoint();w.store.close()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=ROOT/'output/player-social-20261028')
    args=parser.parse_args();out=args.output.resolve();out.mkdir(parents=True,exist_ok=True)
    original=out/'fixture.sqlite'
    if original.exists():raise SystemExit('Use a fresh output directory to preserve evidence')
    fixture(original);path=out/'real-world.sqlite';shutil.copy2(original,path)
    config=json.loads((ROOT/'data/ai_config.json').read_text(encoding='utf-8'))
    manager=ai.AIManager(dict(config,enabled=True,queue_limit=1))
    w=World(store=Store(path),compact=True,durable=True)
    initial_total=w.player['wealth']+sum(n['wealth'] for n in w.npcs.values())
    rows=[]
    cases=[(1,'talk','我叫沈行舟，刚走到此地，想听听你最近的日子。'),
           (1,'ask_aid','我有些盘缠周转不过来，可否援助四两？请按你家的实际余力来。'),
           (1,'introduce','可否引荐一位你信得过的熟人，让我有机会结识？'),
           (1,'swear','几次相交，我愿与你结义，你若尚有顾虑，也请直言。'),
           (2,'talk','你近来有什么难处，需要我帮忙吗？'),
           (2,'help','我愿拿出四两，助你眼前用度，你意下如何？'),
           (4,'ask_aid','我们虽有旧怨，还是想问你能否援助我四两盘缠。')]
    try:
        for npc,action,text in cases:
            print(f'{npc} {action}: waiting for two real requests',flush=True)
            manager.interact(w,npc,action,'',player_social.state(w)['revision'])
            deadline=time.monotonic()+200
            while manager.dialogue_pending and time.monotonic()<deadline:
                manager.collect(w);time.sleep(.1)
            row=player_social.view(w,npc)['requests'][-1]
            print(json.dumps({k:row.get(k) for k in ('action','status','choice_id','reply','error')},ensure_ascii=False),flush=True)
            rows.append(row)
            if row['status']!='completed':raise RuntimeError(row['error'] or 'Interaction timed out')
            total=w.player['wealth']+sum(n['wealth'] for n in w.npcs.values())
            if abs(total-initial_total)>1e-8:raise AssertionError('Silver is not conserved')
        before=deepcopy(w.export());w.checkpoint();again=World(store=w.store,compact=True,durable=True)
        normalize=lambda x:json.loads(json.dumps(x,ensure_ascii=False))
        assert normalize(again.export())==normalize(before),'Saved state differs'
        logs=[json.loads(r[0]) for r in w.store.db.execute('SELECT data FROM ai_log ORDER BY id')]
        result=dict(passed=True,model=manager.config['model'],scenarios=len(rows),
                    actual_requests=sum(r.get('request_count',0) for r in logs),
                    rows=rows,silver_conserved=True,save_reload_equal=True,
                    fixture_sha256=hashlib.sha256(original.read_bytes()).hexdigest(),
                    ai_logs=logs,fixture=str(original))
        (out/'report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
        print(json.dumps({k:v for k,v in result.items() if k not in ('rows','ai_logs')},ensure_ascii=False),flush=True)
    finally:
        manager.close();w.store.close()


if __name__=='__main__':main()
