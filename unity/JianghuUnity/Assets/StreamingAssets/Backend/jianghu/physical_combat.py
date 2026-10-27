"""Unity physical combat sessions, save validation and offscreen NPC approximation."""
from copy import deepcopy
import json
import math
from pathlib import Path
import random
from . import martial

RULESET, ENGINE, MAX_SECONDS = 4, 'unity-physics2d-v09', 180
PROFILES = json.loads(Path(__file__).with_name('physical_profiles.json').read_text(encoding='utf-8'))
BODY_KEYS = {'pelvis','torso','head','upperFar','upperNear','forearmFar','forearmNear','handFar','handNear',
             'thighFar','thighNear','shinFar','shinNear','footFar','footNear','sword','shield'}

def equipment(w, owner):
    from . import items
    actor = items.actor(w, owner)
    gear = items.bonuses(w, owner)
    item_id = actor['equipment'].get('weapon')
    item = items.get(w, owner, item_id) if item_id else None
    spec = items.spec(item) if item and item['durability'] > 0 else None
    profile = (spec or {}).get('physical_profile')
    if profile is None:
        profile = ('sabre' if '刀' in spec['name'] else 'sword') if spec else 'unarmed'
    profile = profile if profile in PROFILES else 'sword'
    shield_id = actor['equipment'].get('shield')
    shield = items.get(w, owner, shield_id) if shield_id else None
    has_shield = bool(shield and shield['durability'] > 0 and not PROFILES[profile]['support_grip'])
    if shield and not has_shield and shield['durability'] > 0:
        for key in ('attack', 'defense'):
            gear[key] -= items.spec(shield).get(key, 0)
    return {**gear, 'weapon_profile':profile, 'weapon_name':spec['name'] if spec else '徒手',
            'weapon_item':item_id if spec else None, 'shield':has_shield}

def fighter(actor, key, gear=None):
    gear = gear or {}
    stats = martial.stats(actor, gear)
    profile = gear.get('weapon_profile', 'unarmed')
    weapon = {**PROFILES[profile], 'id':profile, 'name':gear.get('weapon_name', PROFILES[profile]['name'])}
    trained = [actor['martial']['arts'][a] for a in actor['martial']['loadout']['attacks']]
    practice = sum(e['realm']+e['practice']/100 for e in trained)/max(1,len(trained))
    hero = actor.get('protagonist', {})
    return {**stats, 'id':key, 'name':str(hero.get('name') or actor.get('name') or '无名客'),
            'vigor_max':stats['vigor'], 'stamina':min(100.,actor['martial']['stamina']),
            'weapon':weapon, 'shield':bool(gear.get('shield')), 'weapon_item':gear.get('weapon_item'),
            'damage_scale':round(min(2.2,.78+stats['attack']*.012+practice*.045),4),
            'handling_scale':round(min(1.20,.94+stats['agility']*.004),4),
            'damage_reduction':round(min(.45,stats['outer_guard']*.018),4),
            'hits':0, 'damage_dealt':0., 'motion_spent':0., 'attack_count':0,
            'outer_wounds':0., 'inner_wounds':0.}

def start(a,b,seed,battle_id='',spar=True,equipment=None):
    gears = equipment or (None,None)
    return dict(id=battle_id,seed=seed,ruleset=RULESET,engine=ENGINE,revision=1,status='active',spar=spar,
        fighters=[fighter(a,a.get('id','player'),gears[0]),fighter(b,b['id'],gears[1])],elapsed=0.,frame=None,
        log=[],summary=[{},{}],winner=None,reason='',settled=False,simulation='unity',hits=0,clashes=0)

def number(value,lo,hi,label):
    if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value) or not lo<=value<=hi:
        raise ValueError('物理战局数据不合法：'+label)
    return float(value)

def fields(packet,spec):
    if not isinstance(packet,dict):raise ValueError('物理状态格式不合法')
    return {k:number(packet.get(k),lo,hi,k) for k,lo,hi in spec}

def validate_frame(battle,frame):
    if not isinstance(frame,dict) or frame.get('engine')!=ENGINE or frame.get('version')!=1:
        raise ValueError('物理战局版本不匹配')
    result = dict(engine=ENGINE,version=1,elapsed=number(frame.get('elapsed'),battle['elapsed'],MAX_SECONDS+5,'elapsed'),
        hits=int(number(frame.get('hits'),battle['hits'],10000,'hits')),
        clashes=int(number(frame.get('clashes'),battle['clashes'],1000000,'clashes')),fighters=[])
    packets=frame.get('fighters')
    if not isinstance(packets,list) or len(packets)!=2:raise ValueError('缺少双方物理状态')
    for old,p in zip(battle['fighters'],packets):
        clean=fields(p,[('health',0,old['vigor']+.001),('stamina',0,100),('balance',0,100),('strength',0,1.2),
            ('heat',0,1),('motion_spent',old['motion_spent'],100000),('attack_count',old['attack_count'],10000),('landed_hits',old['hits'],10000)])
        bodies=p.get('bodies')
        if not isinstance(bodies,list) or not 16<=len(bodies)<=17:raise ValueError('骨架部件数量不匹配')
        keys=[x.get('key') for x in bodies if isinstance(x,dict)]
        expected=BODY_KEYS-({'shield'} if old['weapon']['support_grip'] else set())
        if len(keys)!=len(set(keys)) or set(keys)!=expected:raise ValueError('骨架部件不匹配')
        clean['bodies']=[dict(key=x['key'],**fields(x,[('x',-20,20),('y',-10,20),('angle',-1000000,1000000),
            ('vx',-100,100),('vy',-100,100),('spin',-2000,2000)])) for x in bodies]
        feet=p.get('feet')
        if not isinstance(feet,dict) or set(feet)!={'Near','Far'}:raise ValueError('落脚状态缺失')
        clean['feet']={}
        for side,f in feet.items():
            if f.get('state') not in (0,1,2):raise ValueError('落脚阶段不合法')
            clean['feet'][side]=dict(state=f['state'],**fields(f,[('phase',0,1),('anchor_x',-20,20),('anchor_y',-10,20),
                ('from_x',-20,20),('from_y',-10,20),('target_x',-20,20),('target_y',-10,20)]))
        result['fighters'].append(clean)
    if result['hits']!=sum(int(f['landed_hits']) for f in result['fighters']):raise ValueError('命中计数不一致')
    for i in (0,1):
        if result['fighters'][i]['health']<battle['fighters'][i]['vigor']-.01 and result['fighters'][1-i]['landed_hits']<=0:
            raise ValueError('受伤缺少接触记录')
    return result

def submit(b,action,frame=None):
    if action not in ('checkpoint','complete','surrender'):raise ValueError('旧战斗指令已移除，请使用物理战场')
    clean=validate_frame(b,frame) if frame is not None else None
    if action!='surrender' and clean is None:raise ValueError('缺少物理战局快照')
    if action=='complete' and all(f['health']>0 for f in clean['fighters']) and clean['elapsed']<MAX_SECONDS:
        raise ValueError('尚未分出胜负')
    if clean:
        b['frame']=clean;b['elapsed']=clean['elapsed'];b['hits']=clean['hits'];b['clashes']=clean['clashes']
        for f,p in zip(b['fighters'],clean['fighters']):
            f.update(vigor=p['health'],stamina=p['stamina'],hits=int(p['landed_hits']),attack_count=int(p['attack_count']),motion_spent=p['motion_spent'])
        for i,f in enumerate(b['fighters']):
            f['damage_dealt']=round(b['fighters'][1-i]['vigor_max']-b['fighters'][1-i]['vigor'],4)
            f['outer_wounds']=round(f['vigor_max']-f['vigor'],4)
    b['last_request_revision'],b['last_action']=b['revision'],action;b['revision']+=1
    b['summary']=[dict(hits=f['hits'],damage=f['damage_dealt'],attacks=f['attack_count'],motion_spent=f['motion_spent']) for f in b['fighters']]
    if action in ('complete','surrender'):
        ratios=[f['vigor']/f['vigor_max'] for f in b['fighters']]
        winner=1 if action=='surrender' else None if abs(ratios[0]-ratios[1])<.001 else int(ratios[1]>ratios[0])
        b.update(status='finished',winner=winner,reason='收招认输' if action=='surrender' else '气血不支' if min(ratios)<=0 else '三分钟收招')
        b['log']=[dict(side=i,damage=f['damage_dealt'],hits=f['hits'],text=f['name']+'完成物理交手') for i,f in enumerate(b['fighters'])]
    return b

def view(battle):return deepcopy(battle) if battle else None

def autoplay(b):
    """Seeded range/energy/stamina approximation, not a Unity ragdoll simulation."""
    rng=random.Random(b['seed']);fs=b['fighters'];b['simulation']='offscreen-weapon-dynamics'
    clocks=[rng.uniform(.1,.6),rng.uniform(.1,.6)];stamina=[f['stamina'] for f in fs]
    distance=max(f['weapon']['distance'] for f in fs)+.2
    for step in range(1,1201):
        now=step*.05;desired=sum(f['weapon']['distance'] for f in fs)/2
        distance+=max(-.08,min(.08,desired-distance))
        for side in (0,1):
            f,other=fs[side],fs[1-side]
            if min(f['vigor'],other['vigor'])<=0:break
            w=f['weapon'];h=w['handling']*f['handling_scale']
            if now<clocks[side] or stamina[side]<w['cost']:
                stamina[side]=min(100.,stamina[side]+.05*15);continue
            stamina[side]-=w['cost'];f['attack_count']+=1
            inertia=max(.008,w['mass']*(w['reach']+w['tail'])**2/12)
            spin=min(6.5*h,math.sqrt(2*18*.8/inertia))
            speed=spin*(.35+w['reach']*.7)*(.45+.55*min(1,stamina[side]/25))
            effort=min(stamina[side],max(0,speed-2.4)*.3*math.sqrt(w['mass'])*.35)
            stamina[side]-=effort;f['motion_spent']+=effort;clocks[side]=now+.94/h+rng.uniform(.12,.4)
            if abs(distance-w['distance'])>=.8 or rng.random()<(.40 if other['shield'] else .24):
                b['clashes']+=1;stamina[1-side]=max(0.,stamina[1-side]-min(8,speed*.4*w['pressure']));continue
            mass=1/(1/w['mass']+1/.8)
            damage=max(2,min(25,4+math.sqrt(.5*mass*speed*speed)*4.4))*w['damage']*f['damage_scale']*(1-other['damage_reduction'])
            damage=round(min(other['vigor'],damage),4);other['vigor']=max(0.,round(other['vigor']-damage,4))
            f['hits']+=1;f['damage_dealt']+=damage;other['outer_wounds']+=damage;b['hits']+=1
            b['log'].append(dict(side=side,damage=damage,time=round(now,2),text=f['name']+'兵器接触造成伤势'))
        if min(f['vigor'] for f in fs)<=0:break
    ratios=[f['vigor']/f['vigor_max'] for f in fs]
    b.update(status='finished',elapsed=round(now,2),winner=None if abs(ratios[0]-ratios[1])<.04 else int(ratios[1]>ratios[0]),reason='离屏交手收招')
    b['summary']=[dict(hits=f['hits'],damage=round(f['damage_dealt'],4),attacks=f['attack_count']) for f in fs]
    for f,value in zip(fs,stamina):f['stamina']=round(value,4)
    return b
