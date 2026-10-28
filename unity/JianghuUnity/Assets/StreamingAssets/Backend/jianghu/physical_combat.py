"""Unity physical combat sessions, save validation and offscreen NPC approximation."""
from copy import deepcopy
import json
import math
from pathlib import Path
import random
from . import martial, combat_attributes, weapons, physical_reference

RULESET, ENGINE, MAX_SECONDS = 4, 'unity-physics2d-v09', 180
PROFILES = weapons.PROFILES
IMPACT = json.loads(Path(__file__).with_name('physical_impact.json').read_text(encoding='utf-8'))
BODY_KEYS = {'pelvis','torso','head','upperFar','upperNear','forearmFar','forearmNear','handFar','handNear',
             'thighFar','thighNear','shinFar','shinNear','footFar','footNear','sword','shield'}

def equipment(w, owner):
    from . import items
    actor = items.actor(w, owner)
    gear = items.bonuses(w, owner)
    item_id = actor['equipment'].get('weapon')
    item = items.get(w, owner, item_id) if item_id else None
    spec = items.spec(item) if item and item['durability'] > 0 else None
    weapon = weapons.profile(item) if spec else {**PROFILES['unarmed'], 'id': 'unarmed'}
    profile = weapon['id']
    shield_id = actor['equipment'].get('shield')
    shield = items.get(w, owner, shield_id) if shield_id else None
    has_shield = bool(shield and shield['durability'] > 0 and not PROFILES[profile]['support_grip'])
    return {**gear, 'weapon_profile':profile, 'weapon_name':spec['name'] if spec else '徒手',
            'weapon_item':item_id if spec else None, 'weapon':weapon, 'shield':has_shield}

def fighter(actor, key, gear=None, *, fresh=False):
    gear = gear or {}
    profile = gear.get('weapon_profile', 'unarmed')
    weapon = {**gear.get('weapon', PROFILES[profile]), 'id':profile,
              'name':gear.get('weapon_name', PROFILES[profile]['name'])}
    hero = actor.get('protagonist', {})
    abilities = combat_attributes.view(actor, gear)
    params = abilities['parameters']
    vigor = round(params['max_health'] * (1 if fresh else max(.25, actor['health']/100)), 4)
    return {'id':key, 'name':str(hero.get('name') or actor.get('name') or '无名客'),
            'vigor':vigor, 'vigor_max':vigor,
            'stamina':params['stamina_max']*(1 if fresh else min(100.,actor['martial']['stamina'])/100),
            'stamina_max':params['stamina_max'], 'balance_max':params['balance_max'],
            'attribute_version':combat_attributes.VERSION, 'combat_ratings':abilities['ratings'],
            'tuning_version':combat_attributes.TUNING['version'], 'weapon_version':weapons.VERSION,
            'combat_parameters':params, 'physique':abilities['physique'],
            'weapon':weapon, 'shield':bool(gear.get('shield')), 'weapon_item':gear.get('weapon_item'),
            'damage_scale':params['damage_scale'], 'handling_scale':params['handling_scale'],
            'damage_reduction':params['damage_reduction'],
            'hits':0, 'damage_dealt':0., 'motion_spent':0., 'attack_count':0,
            'outer_wounds':0., 'inner_wounds':0.}

def start(a,b,seed,battle_id='',spar=True,equipment=None):
    gears = equipment or (None,None)
    fighters = [fighter(a,a.get('id','player'),gears[0],fresh=spar),fighter(b,b['id'],gears[1],fresh=spar)]
    for f in fighters:
        f['physics_profile'] = physical_reference.PROFILE
    return dict(id=battle_id,seed=seed,ruleset=RULESET,engine=ENGINE,revision=1,status='active',spar=spar,
        fighters=fighters,elapsed=0.,frame=None,resource_policy='per-bout-v1' if spar else 'world',
        log=[],summary=[{},{}],winner=None,reason='',settled=False,simulation='unity',hits=0,clashes=0)

def upgrade_tuning(w):
    """Retune an unfinished saved bout without replaying it or healing wounds."""
    b = w.battle
    if not b or b.get('status') != 'active' or b.get('settled') or b.get('ruleset') != RULESET:
        return False
    changed = False
    for side, old in enumerate(b['fighters']):
        if (old.get('tuning_version', 1) >= combat_attributes.TUNING['version']
                and old.get('weapon_version', 0) >= weapons.VERSION):
            continue
        owner = 'player' if side == 0 else old['id']
        actor = w.player if side == 0 else w.npcs[owner]
        current = fighter(actor, old['id'], equipment(w, owner),fresh=b.get('spar',True))
        health_ratio = current['vigor_max']/max(.001, old['vigor_max'])
        stamina_ratio = current['stamina_max']/max(.001, old.get('stamina_max',100.))
        balance_ratio = current['balance_max']/max(.001, old.get('balance_max',100.))
        old['vigor'] = round(old['vigor']*health_ratio,4)
        old['stamina'] = round(old['stamina']*stamina_ratio,4)
        old['outer_wounds'] = round(old.get('outer_wounds',0)*health_ratio,4)
        for key in ('vigor_max','stamina_max','balance_max','attribute_version','tuning_version',
                    'weapon_version','weapon','weapon_item','shield',
                    'combat_ratings','combat_parameters','damage_scale','handling_scale','damage_reduction'):
            old[key] = current[key]
        if b.get('frame'):
            packet = b['frame']['fighters'][side]
            packet['health'] = round(packet['health']*health_ratio,4)
            packet['stamina'] = round(packet['stamina']*stamina_ratio,4)
            packet['balance'] = round(packet['balance']*balance_ratio,4)
        changed = True
    if changed:
        for side,f in enumerate(b['fighters']):
            other = b['fighters'][1-side]
            f['damage_dealt'] = round(other['vigor_max']-other['vigor'],4)
            if len(b.get('summary',[])) > side and b['summary'][side]:
                b['summary'][side]['damage'] = f['damage_dealt']
    return changed


def number(value,lo,hi,label):
    if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value) or not lo<=value<=hi:
        raise ValueError('物理战局数据不合法：'+label)
    return float(value)

def fields(packet,spec,context=''):
    if not isinstance(packet,dict):raise ValueError('物理状态格式不合法')
    return {k:number(packet.get(k),lo,hi,context+'.'+k if context else k) for k,lo,hi in spec}

def validate_frame(battle,frame):
    if not isinstance(frame,dict) or frame.get('engine')!=ENGINE or frame.get('version')!=1:
        raise ValueError('物理战局版本不匹配')
    result = dict(engine=ENGINE,version=1,elapsed=number(frame.get('elapsed'),battle['elapsed'],MAX_SECONDS+5,'elapsed'),
        hits=int(number(frame.get('hits'),battle['hits'],10000,'hits')),
        clashes=int(number(frame.get('clashes'),battle['clashes'],1000000,'clashes')),fighters=[])
    packets=frame.get('fighters')
    if not isinstance(packets,list) or len(packets)!=2:raise ValueError('缺少双方物理状态')
    for fighter_index,(old,p) in enumerate(zip(battle['fighters'],packets)):
        reference = old.get('physics_profile') == physical_reference.PROFILE
        clean=fields(p,[('health',0,old['vigor']+.001),('stamina',0,old.get('stamina_max',100)),('balance',0,old.get('balance_max',100)),('strength',0,1.2),
            ('heat',0,1),('motion_spent',old['motion_spent'],100000),('attack_count',old['attack_count'],10000),('landed_hits',old['hits'],10000)])
        for key, hi in [('stun_left',10),('injury_left',10),('guard_break_left',10),('regen_left',10),('motion_exhausted',1),('swing_seconds',MAX_SECONDS+5)]:
            if key in p:
                clean[key]=number(p[key],0,hi,key)
        bodies=p.get('bodies')
        if not isinstance(bodies,list) or not 16<=len(bodies)<=17:raise ValueError('骨架部件数量不匹配')
        keys=[x.get('key') for x in bodies if isinstance(x,dict)]
        expected=BODY_KEYS-({'shield'} if old['weapon']['support_grip'] else set())
        if len(keys)!=len(set(keys)) or set(keys)!=expected:raise ValueError('骨架部件不匹配')
        body_spec=physical_reference.BODY_FIELDS if reference else [
            ('x',-20,20),('y',-10,20),('angle',-1000000,1000000),
            ('vx',-100,100),('vy',-100,100),('spin',-2000,2000)]
        clean['bodies']=[dict(key=x['key'],**fields(x,body_spec,
            f'fighters[{fighter_index}].bodies.'+x['key'])) for x in bodies]
        feet=p.get('feet')
        if not isinstance(feet,dict) or set(feet)!={'Near','Far'}:raise ValueError('落脚状态缺失')
        clean['feet']={}
        for side,f in feet.items():
            if f.get('state') not in (0,1,2):raise ValueError('落脚阶段不合法')
            x_limit=physical_reference.MAX_POSITION if reference else 20
            y_low=-physical_reference.MAX_POSITION if reference else -10
            y_high=physical_reference.MAX_POSITION if reference else 20
            clean['feet'][side]=dict(state=f['state'],**fields(f,[('phase',0,1),('anchor_x',-x_limit,x_limit),('anchor_y',y_low,y_high),
                ('from_x',-x_limit,x_limit),('from_y',y_low,y_high),('target_x',-x_limit,x_limit),('target_y',y_low,y_high)],
                f'fighters[{fighter_index}].feet.'+side))
        if reference:
            previous = ((battle.get('frame') or {}).get('fighters') or [{}, {}])[fighter_index].get('reference', {})
            clean['reference'] = physical_reference.validate(p.get('reference'), old, previous, number, fields)
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

def impact_damage(mass, speed, weapon_damage=1., attack_scale=1., reduction=0., zone='body'):
    """Shared kinetic law; Unity reads the very same physical_impact.json."""
    if speed < IMPACT['minimum_closing_speed'] or mass <= 0:
        return 0.
    energy = .5 * mass * speed * speed
    return (IMPACT['energy_scale'] * energy ** IMPACT['energy_exponent'] * weapon_damage
            * attack_scale * (1 - max(0., min(1., reduction)))
            * IMPACT['zone_multipliers'].get(zone, 1.))


def autoplay(b):
    """Seeded range/energy/stamina approximation, not a Unity ragdoll simulation."""
    rng=random.Random(b['seed']);fs=b['fighters'];b['simulation']='offscreen-weapon-dynamics'
    clocks=[rng.uniform(.1,.6),rng.uniform(.1,.6)];stamina=[f['stamina'] for f in fs]
    fatigue=[0.,0.]
    neutral=combat_attributes.parameters(dict.fromkeys(combat_attributes.KEYS,50.))
    params=[f.get('combat_parameters',neutral) for f in fs]
    distance=max(f['weapon']['distance'] for f in fs)+.2
    for step in range(1,1201):
        now=step*.05;desired=sum(f['weapon']['distance']*p['movement_scale'] for f,p in zip(fs,params))/sum(p['movement_scale'] for p in params)
        distance+=max(-.08,min(.08,desired-distance))
        for side in (0,1):
            f,other=fs[side],fs[1-side]
            p,op=params[side],params[1-side]
            if min(f['vigor'],other['vigor'])<=0:break
            w=f['weapon'];h=w['handling']*f['handling_scale']
            if now<clocks[side] or stamina[side]<w['cost']*p['effort_scale']:
                stamina[side]=min(f.get('stamina_max',100.),stamina[side]+.05*15*p['regen_scale'])
                fatigue[side]=max(0.,fatigue[side]-.05*p['fatigue_recovery']);continue
            stamina[side]-=w['cost']*p['effort_scale'];f['attack_count']+=1
            inertia=max(.008,w['mass']*(w['reach']+w['tail'])**2/12)
            spin=min(6.5*h,math.sqrt(2*18*.8*p['force_scale']/inertia))
            speed=spin*(.35+w['reach']*.7)*(.45+.55*min(1,stamina[side]/(f.get('stamina_max',100)*.25)))
            fatigue[side]=min(1.,fatigue[side]+.15*p['fatigue_growth'])
            effort=min(stamina[side],max(0,speed-2.4)*.3*math.sqrt(w['mass'])*.35*p['effort_scale']*(1+fatigue[side]*.65))
            stamina[side]-=effort;f['motion_spent']+=effort;clocks[side]=now+.94/h+rng.uniform(.12,.4)*p['recovery_delay_scale']
            if abs(distance-w['distance'])>=.8 or rng.random()<(.40 if other['shield'] else .24):
                b['clashes']+=1;stamina[1-side]=max(0.,stamina[1-side]-min(8,speed*.4*w['pressure'])*op['effort_scale']/op['stability_scale']);continue
            zones=IMPACT['offscreen_hit_zones']
            zone=rng.choices([z['zone'] for z in zones],weights=[z['weight'] for z in zones],k=1)[0]
            raw_damage=impact_damage(w['mass'],speed,w['damage'],f['damage_scale'],other['damage_reduction'],zone)
            damage=raw_damage
            damage=round(min(other['vigor'],damage),4);other['vigor']=max(0.,round(other['vigor']-damage,4))
            clocks[1-side]=max(clocks[1-side],now+(.12+damage*.006)*op['stun_scale'])
            f['hits']+=1;f['damage_dealt']+=damage;other['outer_wounds']+=damage;b['hits']+=1
            b['log'].append(dict(side=side,damage=damage,time=round(now,2),zone=zone,speed=speed,
                weapon_mass=w['mass'],raw_damage=raw_damage,text=f['name']+'兵器接触造成伤势'))
        if min(f['vigor'] for f in fs)<=0:break
    ratios=[f['vigor']/f['vigor_max'] for f in fs]
    b.update(status='finished',elapsed=round(now,2),winner=None if abs(ratios[0]-ratios[1])<.04 else int(ratios[1]>ratios[0]),reason='离屏交手收招')
    b['summary']=[dict(hits=f['hits'],damage=round(f['damage_dealt'],4),attacks=f['attack_count']) for f in fs]
    for f,value in zip(fs,stamina):f['stamina']=round(value,4)
    return b
