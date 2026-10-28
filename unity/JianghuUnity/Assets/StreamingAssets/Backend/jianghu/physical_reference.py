"""Versioned state for the independently implemented reference combat rig."""
PROFILE = 'bloody-reference-v1'
# Snapshot bounds are independent of the visible arena. A disarmed weapon or
# severed part can clear its walls and keep travelling/falling at the engine's
# 100 m/s translation limit for a bout of at most 185 seconds. Preserve that
# physical state instead of rejecting the entire checkpoint or settlement.
MAX_POSITION = 20_000
BODY_FIELDS = [('x', -MAX_POSITION, MAX_POSITION), ('y', -MAX_POSITION, MAX_POSITION),
               ('angle', -1000000, 1000000), ('vx', -100, 100), ('vy', -100, 100),
               ('spin', -6000, 6000)]
EXTRA_BODIES = {'belly', 'neck', 'mover', 'headCOM', 'chestCOM', 'pelvisCOM',
                'handCOMNear', 'handCOMFar', 'footCOMNear', 'footCOMFar'}
PARTS = {'head': 70, 'neck': 70, 'belly': 100, 'torso': 100, 'pelvis': 100,
         **{part + side: hp for side in ('Near', 'Far') for part, hp in
            [('upper', 30), ('forearm', 30), ('hand', 50), ('thigh', 40), ('shin', 40), ('foot', 40)]}}


def weight_keys(fighter):
    keys = {'Near'} if fighter['weapon']['id'] != 'unarmed' else set()
    if fighter.get('shield') and not fighter['weapon']['support_grip']:
        keys.add('Far')
    return keys


def validate(data, old, previous, number, fields):
    if not isinstance(data, dict) or data.get('profile') != PROFILE:
        raise ValueError('参考骨架状态缺失或版本不匹配')
    result = dict(profile=PROFILE, **fields(data, [
        ('exhausted_left', 0, 3.001), ('recovering_left', 0, 1.001),
        ('effort_tick', 0, .1667), ('walk_phase', -100000, 100000), ('kick_left', 0, .501)]))
    if not isinstance(data.get('wake_pending'), bool):
        raise ValueError('起身状态格式不合法')
    result['wake_pending'] = data['wake_pending']
    if 'source_motion' in data or 'source_motion' in previous:
        motion=data.get('source_motion')
        if (not isinstance(motion,dict) or type(motion.get('version')) is not int
                or motion['version']!=1 or not isinstance(motion.get('off'),bool)
                or type(motion.get('aggression')) is not int or not 0<=motion['aggression']<=5):
            raise ValueError('原作动作状态版本或标记不合法')
        clip=motion.get('clip')
        names={f'{hands}_{action}' for hands in ('1Hand','2Hands') for action in ('SwingDown','SwingUp','Stab')}
        if not isinstance(clip,str) or clip not in names|{''}:
            raise ValueError('原作动作名称不匹配')
        duration=1 if clip.startswith('2Hands') else .833333334
        time=number(motion.get('time'),0,duration+.00001,'source motion time')
        if (not clip and (time or motion['off']) or clip and not motion['aggression']
                or clip and bool(old['weapon']['support_grip']) and clip.startswith('1Hand')):
            raise ValueError('原作动作与武器或播放状态不一致')
        result['source_motion']=dict(version=1,clip=clip,time=time,off=motion['off'],aggression=motion['aggression'])
    if 'death_state' in data or 'death_state' in previous:
        death=data.get('death_state')
        if (not isinstance(death,dict) or type(death.get('version')) is not int
                or death['version']!=1 or type(death.get('stage')) is not int
                or not 0<=death['stage']<=7 or not isinstance(death.get('body'),dict)):
            raise ValueError('死亡流程状态不合法')
        stage=death['stage'];wait=number(death.get('wait'),0,.02501,'death stage wait')
        if stage<previous.get('death_state',{}).get('stage',0) or stage in (0,7) and wait:
            raise ValueError('死亡流程不可回退或携带无效等待')
        result['death_state']=dict(version=1,stage=stage,wait=wait,
            body=fields(death['body'],BODY_FIELDS,'reference.death_state.body'))
    if 'core_state' in data or 'core_state' in previous:
        core=data.get('core_state')
        if (not isinstance(core,dict) or type(core.get('version')) is not int
                or core['version']!=1 or not isinstance(core.get('bleeding'),bool)):
            raise ValueError('战斗核心状态版本或标记不合法')
        modes=core.get('part_modes');cooldowns=core.get('part_cooldowns')
        if (not isinstance(modes,dict) or set(modes)!=set(PARTS)
                or not isinstance(cooldowns,dict) or set(cooldowns)!=set(PARTS)):
            raise ValueError('战斗核心部位状态不匹配')
        prior_core=previous.get('core_state',{})
        if (not isinstance(data.get('parts'),dict) or set(data['parts'])!=set(PARTS)
                or any(not isinstance(p,dict) for p in data['parts'].values())):
            raise ValueError('战斗核心局部健康格式不合法')
        clean_modes={}
        for key,mode in modes.items():
            local_health=number(data['parts'][key].get('health'),0,PARTS[key]+.001,'core local health')
            if type(mode) is not int or mode not in (0,1,2,3):
                raise ValueError('肢体损伤类型不合法')
            prior_mode=prior_core.get('part_modes',{}).get(key,0)
            if prior_mode and mode!=prior_mode:
                raise ValueError('肢体损伤类型不可回退或替换')
            if mode and (key in ('torso','belly','pelvis') or local_health>.001):
                raise ValueError('损伤类型与局部健康不一致')
            if mode==3 and not data.get('parts',{}).get(key,{}).get('severed',False):
                raise ValueError('切断类型与离体状态不一致')
            clean_modes[key]=mode
        if prior_core.get('bleeding',False) and not core['bleeding']:
            raise ValueError('流血状态不可丢失')
        timers=fields(core,[('bleed_left',0,.651),('impact_left',0,.101)],'reference.core_state')
        count=core.get('impact_count')
        if type(count) is not int or not 0<=count<=3 or not core['bleeding'] and timers['bleed_left']!=0:
            raise ValueError('战斗核心计数或计时不合法')
        result['core_state']=dict(version=1,part_modes=clean_modes,
            part_cooldowns={key:number(v,0,.151,'part cooldown') for key,v in cooldowns.items()},
            bleeding=core['bleeding'],impact_count=count,**timers)
    if 'kick_state' in data or 'kick_state' in previous:
        kick = data.get('kick_state')
        if (not isinstance(kick, dict) or type(kick.get('version')) is not int
                or kick['version'] != 1 or not isinstance(kick.get('paid'), bool)):
            raise ValueError('踢击状态版本或标记不合法')
        result['kick_state'] = dict(version=1, paid=kick['paid'], **fields(kick, [
            ('kicked_left', 0, .501), ('immunity_left', 0, .501)], 'reference.kick_state'))
    bodies = data.get('bodies')
    if (not isinstance(bodies, list) or len(bodies) != len(EXTRA_BODIES)
            or any(not isinstance(b, dict) for b in bodies)
            or {b.get('key') for b in bodies} != EXTRA_BODIES):
        raise ValueError('参考辅助刚体不匹配')
    result['bodies'] = [dict(key=b['key'], **fields(b, BODY_FIELDS,
        'reference.bodies.' + b['key'])) for b in bodies]
    # Earlier local reference snapshots predate physical counterweights. The
    # client can initialize them from the restored item pose, but a saved new
    # graph must never silently downgrade and discard its physical state.
    if 'weapon_rig' in data or 'weapon_rig' in previous:
        if type(data.get('weapon_rig')) is not int or data['weapon_rig'] != 1:
            raise ValueError('武器配重状态版本不匹配')
        weights = data.get('weights')
        expected = weight_keys(old)
        if (not isinstance(weights, list) or len(weights) != len(expected)
                or any(not isinstance(b, dict) for b in weights)
                or {b.get('key') for b in weights} != expected):
            raise ValueError('武器配重刚体不匹配')
        result['weapon_rig'] = 1
        result['weights'] = [dict(key=b['key'], **fields(b, BODY_FIELDS,
            'reference.weights.' + b['key'])) for b in weights]
    parts = data.get('parts')
    if not isinstance(parts, dict) or set(parts) != set(PARTS):
        raise ValueError('局部肢体状态不匹配')
    result['parts'] = {}
    for key, p in parts.items():
        if not isinstance(p, dict):
            raise ValueError('局部肢体状态格式不合法')
        prior = previous.get('parts', {}).get(key, {})
        hp = number(p.get('health'), 0, prior.get('health', PARTS[key]) + .001, 'local health')
        detached = p.get('severed')
        if not isinstance(detached, bool) or prior.get('severed', False) and not detached:
            raise ValueError('离体肢体状态不可回退')
        if detached and (hp > .001 or key in ('torso', 'belly', 'pelvis')):
            raise ValueError('离体肢体状态不合法')
        result['parts'][key] = dict(health=hp, severed=detached)
    hands = data.get('hands')
    if not isinstance(hands, dict) or set(hands) != {'Near', 'Far'}:
        raise ValueError('双手驱动状态缺失')
    baseline = 3 if old['weapon']['support_grip'] else 4.5
    result['hands'] = {side: fields(h, [
        ('anchor_x', -40, 40), ('anchor_y', -30, 30), ('target_x', -MAX_POSITION, MAX_POSITION),
        ('target_y', -MAX_POSITION, MAX_POSITION), ('heat', 0, 185), ('drain', baseline, baseline * 5)],
        'reference.hands.' + side)
        for side, h in hands.items()}
    return result
