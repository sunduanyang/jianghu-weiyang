"""Real model: one factual refusal, five generated choices, five isolated continuations."""
from copy import deepcopy
import argparse
import json
from pathlib import Path
import shutil
import sys
import time

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu import ai,player_social
from jianghu.engine import World
from jianghu.store import Store
from validate_player_social import fixture


def settle(manager,w):
    deadline=time.monotonic()+240
    while manager.dialogue_pending and time.monotonic()<deadline:
        manager.collect(w);time.sleep(.1)
    if manager.dialogue_pending:raise RuntimeError('Model timed out')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();out=args.output.resolve();out.mkdir(parents=True,exist_ok=True)
    origin=out/'fixture.sqlite'
    if origin.exists():raise SystemExit('Use a fresh output directory')
    fixture(origin)
    config=json.loads((ROOT/'data/ai_config.json').read_text(encoding='utf-8'))
    manager=ai.AIManager(dict(config,enabled=True,queue_limit=1))
    w=World(store=Store(origin),compact=True,durable=True)
    w.npcs[4]['wealth']=16.
    w.npcs[4]['mood']['anger']=90.
    attempts=[];branches=[]
    try:
        for _ in range(1):
            print('NPC 4: initial preset request',flush=True)
            manager.interact(w,4,'ask_aid','',player_social.state(w)['revision']);settle(manager,w)
            root=player_social.state(w)['requests'][-1];attempts.append(deepcopy(root))
            print(json.dumps({k:root.get(k) for k in ('status','choice_id','reply','persuasion_status','options_error')},ensure_ascii=True),flush=True)
            if root['status']!='completed':raise RuntimeError(root['error'])
            if root['choice_id']=='refuse':break
        if root['choice_id']!='refuse':raise RuntimeError('No refusal observed; evidence kept')
        assert any(c['id']=='give_aid' for c in root['decision_context']['choices']),'Refusal was forced by a resource constraint'
        while root.get('persuasion_status')=='failed' and root.get('options_attempts',0)<3:
            manager.interact(w,4,'talk','',player_social.state(w)['revision'],'options',root['id']);settle(manager,w)
        if root.get('persuasion_status')!='ready':raise RuntimeError(root.get('options_error','No options'))
        assert [o['label'] for o in root['persuasion_options']]==list(player_social.STYLES.values())
        root_id=root['id'];options=deepcopy(root['persuasion_options'])
        w.checkpoint();snapshot=out/'refused-with-options.sqlite'
        with __import__('sqlite3').connect(snapshot) as dst:w.store.db.backup(dst)
        initial_logs=[json.loads(r[0]) for r in w.store.db.execute('SELECT data FROM ai_log')]
        initial_calls=sum(r.get('request_count',0) for r in initial_logs)
        w.store.close()
        for option in options:
            path=out/(option['id']+'.sqlite');shutil.copy2(snapshot,path)
            branch=World(store=Store(path),compact=True,durable=True)
            money=branch.player['wealth'];total=money+sum(n['wealth'] for n in branch.npcs.values())
            points=branch.player['protagonist']['points']
            print('Selected '+option['label']+': '+option['text'],flush=True)
            manager.interact(branch,4,'talk','',player_social.state(branch)['revision'],'persuade',root_id,option['id'])
            settle(manager,branch)
            result=player_social.state(branch)['requests'][-1]
            assert result['status']=='completed',result
            paid=result['choice_id']=='give_aid'
            assert branch.player['wealth']==money+(4 if paid else 0)
            assert abs(total-branch.player['wealth']-sum(n['wealth'] for n in branch.npcs.values()))<1e-8
            assert branch.player['protagonist']['points']==points-1
            try:
                manager.interact(branch,4,'talk','',player_social.state(branch)['revision'],'persuade',root_id,option['id'])
                raise AssertionError('Repeated appeal accepted')
            except ValueError:pass
            logs=[json.loads(r[0]) for r in branch.store.db.execute('SELECT data FROM ai_log')][len(initial_logs):]
            assert len(logs)==1 and logs[0]['applied']
            assert logs[0]['context']['reconsideration']['prior_decision']['choice_id']=='refuse'
            assert logs[0]['context']['reconsideration']['selected_persuasion']==option
            saved=deepcopy(branch.export());branch.checkpoint()
            loaded=World(store=branch.store,compact=True,durable=True)
            normalize=lambda x:json.loads(json.dumps(x,ensure_ascii=False))
            assert normalize(loaded.export())==normalize(saved)
            row=dict(style=option['label'],text=option['text'],decision=result['choice_id'],reply=result['reply'],
                     actual_requests=logs[0]['request_count'],money_delta=branch.player['wealth']-money,
                     context=logs[0]['context'],response=logs[0]['response'])
            branches.append(row);print(json.dumps({k:v for k,v in row.items() if k not in ('context','response')},ensure_ascii=True),flush=True)
            branch.store.close()
        result=dict(passed=True,model=manager.config['model'],initial_requests=initial_calls,
                    actual_requests=initial_calls+sum(r['actual_requests'] for r in branches),
                    initial_attempts=attempts,options=options,branches=branches,
                    branches_share_one_refusal=True,silver_conserved=True,save_reload_equal=True,
                    snapshot=str(snapshot))
        (out/'report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
        print(json.dumps(dict(passed=True,actual_requests=result['actual_requests'],outcomes=[r['decision'] for r in branches])),flush=True)
    finally:manager.close()


if __name__=='__main__':main()
