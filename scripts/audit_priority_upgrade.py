"""Back up and compare a paused native save; never advance or rewrite its world."""
import argparse
from copy import deepcopy
from datetime import datetime
import hashlib
import json
from pathlib import Path
import shutil
import sqlite3
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))


MENTAL={'plan','planning_history','deep_thought','llm_policy','traits','beliefs','desires','mood','memories',
        'reflected','reviews','last_review','last_ai_review','last_review_attempt','ai_pending','review_urgency',
        'trigger_events','drift_month','personality_changes','goal','past_goals','last_goal_change','last_event',
        'last_important_event','changes','knowledge'}


def read(path):
    with sqlite3.connect(path.resolve().as_uri()+'?mode=ro',uri=True) as db:
        w=json.loads(db.execute('SELECT data FROM checkpoint WHERE id=1').fetchone()[0])
        row=db.execute('SELECT month,data FROM player_checkpoint WHERE id=1').fetchone()
        if row and row[0]==w['month']:
            player=json.loads(row[1])
            if player['protagonist']['revision']>=w['player'].get('protagonist',{}).get('revision',-1):w['player']=player
        events=db.execute('SELECT id,data FROM events ORDER BY id').fetchall()
    return w,events


def digest(events):return hashlib.sha256(json.dumps(events,ensure_ascii=False,separators=(',',':')).encode()).hexdigest()


def physical(w):
    value={k:v for k,v in w.items() if k not in ('npcs','npc_attention','metrics','random_state')}
    value['npcs']={i:{k:v for k,v in n.items() if k not in MENTAL} for i,n in w['npcs'].items()}
    return value


def catalog_migration(before,after):
    """Recognize the existing physical-combat catalog migration without hiding asset changes."""
    from jianghu.items import PHYSICAL_TEMPLATES
    a=before['items'];b=after['items'];old=a['objects'];new=b['objects']
    added={i:v for i,v in new.items() if i not in old}
    missing=set(PHYSICAL_TEMPLATES)-{i['template'] for i in old.values()}
    if a.get('physical_catalog') or b.get('physical_catalog')!=1:return None
    if {i['template'] for i in added.values()}!=missing or len(added)!=len(missing):return None
    if any(v!=new.get(i) for i,v in old.items()):return None
    if any(v['owner']!='market' or v['origin']!='物理战斗兵器登记' or v['created_month']!=before['month'] for v in added.values()):return None
    expected=deepcopy(a);expected['objects'].update(added);expected['next_id']+=len(added);expected['physical_catalog']=1
    expected['market']['inventory']+=list(added)
    if expected!=b:return None
    return dict(templates=[v['template'] for v in added.values()],count=len(added),existing_objects_unchanged=True,basis='items.initialize physical_catalog migration')


def differences(a,b,path=''):
    if a==b:return []
    if isinstance(a,dict) and isinstance(b,dict):
        return [p for k in sorted(a.keys()|b.keys()) for p in differences(a.get(k),b.get(k),path+'/'+k)]
    return [path]


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('mode',choices=['snapshot','check']);p.add_argument('saves',type=Path);p.add_argument('folder',type=Path);args=p.parse_args()
    args.folder.mkdir(parents=True,exist_ok=True);source=Path((args.saves/'data/active_world.txt').read_text(encoding='utf-8').strip())
    w,events=read(source)
    if args.mode=='snapshot':
        stamp=datetime.now().strftime('%Y%m%d-%H%M%S');backup=args.saves/'backups'/('before-priority-thinking-'+stamp);backup.mkdir(parents=True)
        with sqlite3.connect(source.resolve().as_uri()+'?mode=ro',uri=True) as src:
            with sqlite3.connect(backup/source.name) as dst:src.backup(dst)
        shutil.copy2(args.saves/'data/ai_config.json',backup/'ai_config.json')
        (args.folder/'before.json').write_text(json.dumps(w,ensure_ascii=False),encoding='utf-8')
        report=dict(source=str(source),backup=str(backup),month=w['month'],living=sum(n['alive'] for n in w['npcs'].values()),events=len(events),last_event=events[-1][0] if events else 0,event_digest=digest(events))
        (args.folder/'before-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,ensure_ascii=False))
    else:
        before=json.loads((args.folder/'before.json').read_text(encoding='utf-8'));report=json.loads((args.folder/'before-report.json').read_text(encoding='utf-8'))
        changed=differences(physical(before),physical(w));prefix=[e for e in events if e[0]<=report['last_event']]
        migration=catalog_migration(before,w) if before['items']!=w['items'] else None
        expected=deepcopy(before)
        if migration:expected['items']=w['items']
        unexpected=differences(physical(expected),physical(w))
        result=dict(physical_unchanged=not changed,physical_differences=changed,existing_world_preserved=not unexpected,unexpected_differences=unexpected,catalog_registration=migration,old_history_unchanged=digest(prefix)==report['event_digest'],month=w['month'],living=sum(n['alive'] for n in w['npcs'].values()),old_events=report['events'],new_events=len(events)-len(prefix),attention_migrated=w.get('npc_attention') is not None,backup=report['backup'])
        (args.folder/'upgrade-report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(result,ensure_ascii=False));assert result['existing_world_preserved'] and result['old_history_unchanged']
