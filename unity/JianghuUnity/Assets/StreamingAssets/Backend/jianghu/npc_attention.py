"""Factual player contacts and public standing guide shared-model attention."""
from copy import deepcopy
import time
from . import social_ranks,sect_ranks

CONTACT_KINDS=('player_talk','player_gift','player_visit','player_duel','spar_start','item_gift')
LABELS={'player_talk':'交谈','player_gift':'赠银','player_visit':'拜访','player_duel':'切磋',
        'spar_start':'约定切磋','item_gift':'赠物'}


def targets(event):
    if event['kind'] not in CONTACT_KINDS:return []
    if event['kind']=='item_gift':
        facts=event.get('facts',{})
        return [facts['recipient']] if facts.get('owner')=='player' and facts.get('recipient') is not None else []
    return event['people']


def record(w,event,live=True):
    state=getattr(w,'npc_attention',None)
    if state is None:return
    if event['importance']>=.65:
        for i in event['people']:state['major_events'][str(i)]=max(event['id'],state['major_events'].get(str(i),0))
    for i in targets(event):
        if i not in w.npcs:continue
        key=str(i);row=state['contacts'].setdefault(key,dict(count=0,events=[]))
        if event['id'] in row['events']:continue
        row['count']+=1
        if event['id']>=row.get('last_event',0):
            row.update(last_event=event['id'],last_month=event['month'],kind=event['kind'])
            if live:
                if not hasattr(w,'_npc_contact_clock'):w._npc_contact_clock={}
                w._npc_contact_clock[key]=time.monotonic()
        row['events']=sorted(row['events']+[event['id']])[-8:]


def initialize(w):
    if not hasattr(w,'_npc_contact_clock'):w._npc_contact_clock={}
    if getattr(w,'npc_attention',None) is not None:return False
    w.npc_attention=dict(version=1,contacts={},major_events={},attempts={})
    placeholders=','.join('?' for _ in CONTACT_KINDS)
    import json
    for (raw,) in w.store.db.execute(f'SELECT data FROM events WHERE kind IN ({placeholders}) ORDER BY id',CONTACT_KINDS):
        record(w,json.loads(raw),live=False)
    for n in w.npcs.values():
        events=n.get('trigger_events',[])
        if events:w.npc_attention['major_events'][str(n['id'])]=max(max(events),w.npc_attention['major_events'].get(str(n['id']),0))
    return True


def contact(w,n):
    return (getattr(w,'npc_attention',None) or {}).get('contacts',{}).get(str(n['id']))


def grade(w,n):
    if social_ranks.is_sect_member(w,n):return sect_ranks.standing(w.factions[n['faction']],n)['grade']
    titles=[social_ranks.occupation(n),n.get('office'),(n.get('social_status') or {}).get('title')]
    return min(social_ranks.grade_for(t) for t in titles if t is not None)


def signature(w,n):
    row=contact(w,n) or {};state=getattr(w,'npc_attention',None) or {}
    return dict(month=w.month,place=n['place'],job=n['job'],faction=n['faction'],office=n.get('office'),
                grade=grade(w,n),contact_event=row.get('last_event',0),
                major_event=state.get('major_events',{}).get(str(n['id']),0))


def profile(w,n):
    row=contact(w,n);g=grade(w,n)
    live=getattr(w,'_npc_contact_clock',{}).get(str(n['id']))
    recent=bool(row and (w.month-row['last_month']<=3 or live is not None and time.monotonic()-live<=900))
    high=g<=3 or bool(n.get('office'));reasons=[]
    if row:reasons.append('最近与玩家互动' if recent else '与玩家有过来往')
    if g<=3:reasons.append(social_ranks.GRADES[g]+'人物')
    elif n.get('office'):reasons.append('在任'+n['office'])
    level='intensive' if recent else 'deep' if row or high else 'standard'
    return dict(level=level,label={'intensive':'重点深思','deep':'深入思考','standard':'日常思考'}[level],
                reasons=reasons,grade=g,recent=recent,contact_event=row['last_event'] if row else 0,
                contact_month=row['last_month'] if row else None,
                memory_limit=8 if recent else 6 if level=='deep' else 3,
                contact_limit=8 if recent else 6 if level=='deep' else 3,
                action_limit=28 if recent else 24 if level=='deep' else 20)


def expedited(w,n):
    if profile(w,n)['level']=='standard':return False
    state=getattr(w,'npc_attention',None) or {};sig=signature(w,n)
    attempted=state.get('attempts',{}).get(str(n['id']))
    reviewed=(n.get('deep_thought') or {}).get('signature')
    return sig!=attempted and sig!=reviewed


def eligible(w,n):
    return n['alive'] and bool(n['age_months']>=120 or contact(w,n) or grade(w,n)<=3 or n.get('office'))


def mark_attempt(w,n,ctx):
    if getattr(w,'npc_attention',None) is None:return
    attempts=w.npc_attention['attempts'];key=str(n['id'])
    ctx['_previous_attention_attempt']=deepcopy(attempts.get(key))
    ctx['_attention_signature']=signature(w,n)
    attempts[key]=deepcopy(ctx['_attention_signature'])


def undo_attempt(w,n,ctx):
    if getattr(w,'npc_attention',None) is None:return
    attempts=w.npc_attention['attempts'];key=str(n['id'])
    if attempts.get(key)!=ctx.get('_attention_signature'):return
    previous=ctx.get('_previous_attention_attempt')
    if previous is None:attempts.pop(key,None)
    else:attempts[key]=deepcopy(previous)


def player_memories(w,n,limit=3):
    from . import npc_memory
    result=[]
    for event in reversed((contact(w,n) or {}).get('events',[])):
        memory=npc_memory.find(w,n,event)
        if memory:result.append(memory)
        if len(result)>=limit:break
    return result


def player_view(w,n):
    row=contact(w,n)
    if not row:return None
    return dict(last_month=row['last_month'],last_event=row['last_event'],interactions=row['count'],
                relationship=deepcopy(w.player.get('ties',{}).get(str(n['id']),{})),
                memories=player_memories(w,n),basis='本人亲历的玩家往来；玩家发言保留为意见，未作为事实兑现。')
