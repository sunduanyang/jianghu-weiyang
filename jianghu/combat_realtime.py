"""Deterministic continuous combat, driven by explicit 0.2 second ticks.

Commands change intent without advancing time. Only tick advances either actor.
Every clock, resource and random draw belongs to the saved battle, not the UI.
The original v1/v2 engines remain available for unfinished historical bouts.
"""
from copy import deepcopy
import random

from . import martial

DT = .2
LIMIT = 120.
FORM_NAMES = {'thrust': '刺', 'slash': '劈', 'crush': '崩'}
WEAPONS = {
    'fist': dict(name='拳脚', reach=[1., 4.], sequence=['crush', 'crush', 'thrust'], speed=.85, power=.9),
    'blade': dict(name='随身剑', reach=[4., 7.], sequence=['thrust', 'slash', 'thrust', 'crush'], speed=.68, power=1.),
    'stone': dict(name='飞石', reach=[7., 10.], sequence=['crush', 'thrust', 'slash'], speed=.52, power=.72),
}
BASICS = {'basic_thrust': 'thrust', 'basic_slash': 'slash', 'basic_crush': 'crush'}
BODY = {'head': '头颈', 'chest': '胸背', 'abdomen': '腰腹', 'arm': '双臂', 'leg': '双腿'}


def initialize(b):
    b.update(ruleset=3, distance=6., elapsed=0., tick=0, serial=0, ai_at=0.)
    for f in b['fighters']:
        skill = max(0., (f['attack']-9)/.38)
        f.update(tokens=[], forms=0, mode='blade' if 'river_sword' in f['attacks'] else 'fist',
                 mobility=100., posture=100., posture_max=100., breath=100.,
                 casting=None, recovery=0., attack_charge=2., move=0, target_distance=None,
                 auto_attack=True, basic_index=0, weapon_ready=0., target='chest',
                 inner_ratio=20, guard_until=0., parry_until=0., step_until=0.,
                 heal_ready=0., openings=[], seals=[], wounds=[], parrying=False,
                 force=round(60+skill*1.4, 1), precision=round(60+f['agility']*3, 1),
                 swiftness=round(60+f['agility']*3.3, 1),
                 unload=round(55+f['outer_guard']*6, 1),
                 deflect=round(55+skill*1.1, 1), dodge=round(55+f['agility']*3, 1),
                 move_speed=round(.8+f['agility']*.025, 3),
                 cast_speed=round(1+skill*.005, 3),
                 attack_speed=round(1+skill*.004, 3))


def reach(art):
    return [round(1+art['reach'][0]*1.5, 1), round(1+art['reach'][1]*1.5, 1)]


def in_range(distance, bounds):
    return bounds[0]-.00001 <= distance <= bounds[1]+.00001


def payment(f, art):
    pool = f['tokens'].copy()
    for token in art.get('pattern', []):
        if token not in pool:
            return None
        pool.remove(token)
    count = art['forms']-len(art.get('pattern', []))
    return pool[count:] if len(pool) >= count else None


def costs(f):
    # An adjustable inner/outer split also controls the two readiness costs.
    return 100-f['inner_ratio'], f['inner_ratio']


def actions(b, side=0):
    if b['status'] != 'active':
        return []
    f = b['fighters'][side]
    busy = '正在施展' if f['casting'] else '招式回气中' if f['recovery'] > 0 else ''
    weapon = WEAPONS[f['mode']]
    choices = []
    def add(key, label, kind='tactic', reason='', **extra):
        choices.append(dict(id=key, label=label, kind=kind, enabled=not reason, reason=reason, **extra))
    for key, token in BASICS.items():
        reason = busy or ('攻势不足' if f['attack_charge'] < 1 else '')
        add(key, FORM_NAMES[token]+'击', 'basic', reason, token=token, reach=weapon['reach'],
            qi=0, forms=0, damage_type='mixed',
            description='消耗一格攻势；依武器式轮积式。射程外挥空也恢复架势。')
    for key in f['attacks']:
        art = martial.ARTS[key]
        outer, inner = costs(f)
        reason = (busy or ('缺少'+''.join(FORM_NAMES[t] for t in art.get('pattern', []))+'式'
                          if payment(f, art) is None else '') or
                  ('架势不足' if f['posture'] < outer else '') or
                  ('提气不足' if f['breath'] < inner else '') or
                  ('内力不足' if f['qi'] < art['qi'] else '') or
                  ('功法调息中' if f['cooldowns'].get(key, 0) > b['elapsed'] else ''))
        add(key, art['name'], 'art', reason, reach=reach(art), qi=art['qi'], forms=art['forms'],
            damage_type=art['damage_type'], pattern=art.get('pattern', []),
            cast=round((.9+art.get('cast', 0)*.65)/f['cast_speed'], 2),
            posture_cost=outer, breath_cost=inner,
            description='消耗'+str(art['forms'])+'式，施展后以当前距离判定命中；内伤比例决定架势、提气消耗与内外伤分配。')
    add('advance', '向前移动', reason='脚力耗尽' if f['mobility'] < 1 else '', description='按住 A；移动不打断施展')
    add('retreat', '向后移动', reason='脚力耗尽' if f['mobility'] < 1 else '', description='按住 D；以0.1距离连续移动')
    add('stop', '停止移动', description='松开 A / D 停步')
    add('tick', '推进时间', description='推进0.2秒；暂停时可单步观察')
    add('auto', '自动战斗', description='自动择距、积式、施展；仅此指令会推进0.2秒')
    add('toggle_auto', '自动普攻：'+('开' if f['auto_attack'] else '关'), description='仅在射程内有攻势且空闲时普攻')
    add('guard', '守御', reason=busy or ('架势不足' if f['posture'] < 15 else ''), description='15架势；维持2秒，承伤降低55%')
    add('parry', '拆招', reason=busy or ('提气不足' if f['breath'] < 15 else ''), description='15提气；1.2秒内化解下一次普攻')
    add('breathe', '调息', reason=busy, description='恢复内力；1秒回气，期间无法攻击')
    add('movement_art', f.get('movement_name', '游云步'), 'movement', busy or ('脚力不足' if f['mobility'] < 20 else ''), description='20脚力；5秒内移动速度提升70%')
    add('heal', '疗伤', 'support', busy or ('尚无新伤' if not f['wounds'] else '') or ('疗伤调息中' if f['heal_ready'] > b['elapsed'] else '') or ('内力不足' if f['qi'] < 10 else ''), description='10内力；施展2秒，治疗新伤；受击会中断')
    add('cancel', '中止施展', reason='' if f['casting'] else '没有正在施展的功法', description='已用资源不返还')
    for key, weapon in WEAPONS.items():
        reason = ('正在使用' if key == f['mode'] else '兵器切换调息中' if f['weapon_ready'] > b['elapsed'] else busy)
        if key == 'blade' and not any(k in f['attacks'] for k in ('river_sword', 'reed_cut')):
            reason = '未装配剑法'
        add('mode_'+key, weapon['name'], 'weapon', reason, reach=weapon['reach'], description='切换后3秒内不能再切换')
    for part, label in BODY.items():
        add('target_'+part, label, 'target', description='选择攻击部位')
    add('ratio_up', '内伤＋', reason='已达100%' if f['inner_ratio'] >= 100 else '', description='内伤比例提高10%，同时改变提气／架势消耗')
    add('ratio_down', '外伤＋', reason='已达100%' if f['inner_ratio'] <= 0 else '', description='外伤比例提高10%')
    add('surrender', '收招认输', description='结算此战')
    return choices


def record(b, side, action, **extra):
    b['serial'] += 1
    row = dict(round=b['round'], time=b['elapsed'], serial=b['serial'], side=side,
               actor=b['fighters'][side]['name'], action=action, distance=b['distance'])
    row.update(extra)
    b['log'].append(row)
    return row


def finish(b, winner, reason):
    b.update(status='finished', winner=winner, reason=reason)
    for f in b['fighters']:
        f.update(casting=None, move=0, target_distance=None)


def occupancy(f):
    return min(12., sum(w['slots'] for w in f['wounds'])+
               sum(w['slots'] for w in f['openings'])+sum(w['slots'] for w in f['seals']))


def check_defeat(b):
    defeated = [i for i, f in enumerate(b['fighters']) if f['vigor'] <= 0 or occupancy(f) >= 12]
    if defeated:
        finish(b, None if len(defeated) == 2 else 1-defeated[0], '伤势格已满')


def attack(b, side, key, committed=None):
    f, target = b['fighters'][side], b['fighters'][1-side]
    basic = key in BASICS
    art = WEAPONS[f['mode']] if basic else martial.ARTS[key]
    committed = committed or f
    body = committed['target']
    inner = committed['inner_ratio']/100
    bounds = art['reach'] if basic else reach(art)
    row = record(b, side, key, label=FORM_NAMES[BASICS[key]]+'击' if basic else art['name'],
                 kind='basic' if basic else 'art', hit=False, damage=0., body=body)
    if basic:
        # The form belongs to the weapon's rotating sequence, not the button.
        sequence = art['sequence']
        token = sequence[f['basic_index'] % len(sequence)]
        f['tokens'] = (f['tokens']+[token])[-9:]
        f['forms'] = len(f['tokens'])
        f['basic_index'] += 1
        f['posture'] = min(100., f['posture']+16)
        f['breath'] = min(100., f['breath']+10)
        row['form'] = token
    if not in_range(b['distance'], bounds):
        row.update(outcome='out_of_range', text=f"{f['name']}的{row['label']}落空，距离已在范围之外。")
        return
    if basic and target['parry_until'] > b['elapsed']:
        target['parry_until'] = 0.
        f['posture'] = max(0., f['posture']-18)
        target['posture'] = min(100., target['posture']+12)
        row.update(outcome='parried', text=target['name']+'化解了普攻。')
        return
    rng = random.Random(b['seed']+b['serial']*9176+b['tick']*1009+side)
    pairs = [('force', 'unload', .4), ('precision', 'deflect', .3), ('swiftness', 'dodge', .3)]
    checks, efficiency = {}, 0.
    for offense, defense, weight in pairs:
        ratio = f[offense]/max(1., target[defense])
        aim = {'head': .8, 'chest': 1., 'abdomen': .95, 'arm': .95, 'leg': .9}[body]
        probability = min(.98, max(.15, .65*ratio*aim))
        passed = rng.random() < probability
        checks[offense] = dict(attack=f[offense], defense=target[defense], passed=passed)
        efficiency += weight if passed else 0.
    row.update(checks=checks, efficiency=round(efficiency, 2))
    if not efficiency:
        target['openings'].append(dict(slots=.22, remaining=3., body=body))
        row.update(outcome='evaded', text=f"{f['name']}使出{row['label']}，被闪避化解。")
        return
    entry = {} if basic else f['arts'][key]
    power = f['attack']*art['power']*(1+entry.get('realm', 0)*.14+entry.get('practice', 0)*.002)
    guard = target['outer_guard']*(1-inner)+target['inner_guard']*inner
    damage = max(1., power*efficiency*rng.uniform(.94, 1.06)-guard*.45)
    damage *= {'head': 1.25, 'chest': 1., 'abdomen': 1.1, 'arm': .9, 'leg': .9}[body]
    if target['guard_until'] > b['elapsed']:
        damage *= .45
    if target['openings']:
        damage *= 1.12
    # The shared actor stats were balanced for one exchange per turn. Scale them
    # for independently charging attacks so a starter can respond and cast.
    damage = round(max(.5, damage*.45), 2)
    external, internal = round(damage*(1-inner), 2), round(damage*inner, 2)
    target['vigor'] = round(max(0., target['vigor']-damage), 2)
    target['outer_wounds'] = round(target['outer_wounds']+external, 2)
    target['inner_wounds'] = round(target['inner_wounds']+internal, 2)
    for kind, amount in [('external', external), ('internal', internal)]:
        if amount:
            target['wounds'].append(dict(kind=kind, damage=amount, slots=amount/target['vigor_max']*12, body=body))
    if body == 'arm':
        target['attack_charge'] = round(max(0., target['attack_charge']-.35*efficiency), 5)
    elif body == 'leg':
        target['mobility'] = round(max(0., target['mobility']-damage*.6), 4)
    if efficiency < .99:
        target['openings'].append(dict(slots=.25, remaining=4., body=body))
    if not basic and inner >= .5:
        target['seals'].append(dict(slots=.4 if body == 'head' else .3, remaining=5., body=body))
    interrupted = target['casting'] and (target['casting']['action'] == 'heal' or (not basic and efficiency >= .7))
    if interrupted:
        target['casting'] = None
        target['recovery'] = .6
        row['guard_break'] = True
    row.update(hit=True, damage=damage, external_damage=external, internal_damage=internal,
               damage_type='internal' if inner >= .5 else 'external', outcome='hit',
               text=f"{f['name']}以{row['label']}击中{BODY[body]}，造成{external:g}外伤、{internal:g}内伤。"+('施展被打断！' if interrupted else ''))


def command(b, choice, side=0):
    item = next((a for a in actions(b, side) if a['id'] == choice), None)
    if not item or not item['enabled']:
        raise ValueError(item['reason'] if item else '不是当前可用指令')
    f = b['fighters'][side]
    if choice in ('tick', 'auto'):
        return
    if choice == 'surrender':
        record(b, side, choice, text=f['name']+'收招认输。')
        finish(b, 1-side, '认输')
    elif choice in ('advance', 'retreat', 'stop'):
        f['move'] = {'advance': -1, 'retreat': 1, 'stop': 0}[choice]
        f['target_distance'] = None
    elif choice == 'toggle_auto':
        f['auto_attack'] = not f['auto_attack']
    elif choice in BASICS:
        f['attack_charge'] = round(f['attack_charge']-1, 5)
        f['recovery'] = .45/f['attack_speed']
        attack(b, side, choice)
        check_defeat(b)
    elif choice in f['attacks']:
        art = martial.ARTS[choice]
        outer, inner = costs(f)
        f['posture'] -= outer
        f['breath'] -= inner
        f['qi'] = round(f['qi']-art['qi'], 4)
        f['tokens'] = payment(f, art)
        f['forms'] = len(f['tokens'])
        duration = item['cast']
        f['casting'] = dict(action=choice, remaining=duration, total=duration,
                            target=f['target'], inner_ratio=f['inner_ratio'])
        f['cooldowns'][choice] = round(b['elapsed']+duration+max(1.5, art['cooldown']), 3)
        record(b, side, choice, label=art['name'], outcome='windup', text=f"{f['name']}开始施展{art['name']}。")
    elif choice == 'cancel':
        old = f['casting']['action']
        f['casting'] = None
        f['recovery'] = .5
        record(b, side, choice, text=f['name']+'主动中止施展，消耗不返还。', cancelled=old)
    elif choice.startswith('mode_'):
        f['mode'] = choice[5:]
        f['weapon_ready'] = b['elapsed']+3
        f['recovery'] = .5
        f['basic_index'] = 0
        record(b, side, choice, text=f['name']+'切换为'+WEAPONS[f['mode']]['name']+'。')
    elif choice.startswith('target_'):
        f['target'] = choice[7:]
    elif choice in ('ratio_up', 'ratio_down'):
        f['inner_ratio'] += 10 if choice == 'ratio_up' else -10
    elif choice == 'guard':
        f['posture'] -= 15
        f['guard_until'] = b['elapsed']+2
        f['recovery'] = .4
        record(b, side, choice, text=f['name']+'架起守势。')
    elif choice == 'parry':
        f['breath'] -= 15
        f['parry_until'] = b['elapsed']+1.2
        f['recovery'] = .3
        record(b, side, choice, text=f['name']+'凝神拆招。')
    elif choice == 'breathe':
        f['qi'] = round(min(f['qi_max'], f['qi']+12), 4)
        f['recovery'] = 1.
        record(b, side, choice, text=f['name']+'调息回气。')
    elif choice == 'movement_art':
        f['mobility'] -= 20
        f['step_until'] = b['elapsed']+5
        record(b, side, choice, text=f['name']+'施展身法，进退加快。')
    elif choice == 'heal':
        f['qi'] -= 10
        f['heal_ready'] = b['elapsed']+12
        f['casting'] = dict(action='heal', remaining=2., total=2.)
        record(b, side, choice, outcome='windup', text=f['name']+'开始疗伤。')


def ai_choice(b, side):
    f = b['fighters'][side]
    if f['casting'] or f['recovery'] > 0:
        return 'tick'
    legal = {a['id']: a for a in actions(b, side) if a['enabled']}
    arts = [a for a in legal.values() if a['kind'] == 'art' and in_range(b['distance'], a['reach'])]
    if arts:
        return max(arts, key=lambda a: martial.ARTS[a['id']]['power'])['id']
    if f['qi'] < 8:
        return 'breathe'
    return 'tick'


def ai_intent(b, side):
    f, opponent = b['fighters'][side], b['fighters'][1-side]
    bounds = WEAPONS[f['mode']]['reach']
    target = (bounds[0]+bounds[1])/2
    cast = opponent['casting']
    if cast and cast['action'] in martial.ARTS and cast['remaining'] > .35:
        danger = reach(martial.ARTS[cast['action']])
        # Commit to an escape on the nearest side; actor speed determines success.
        target = min(10., danger[1]+.5) if b['distance'] > sum(danger)/2 else max(1., danger[0]-.5)
    f['target_distance'] = target
    choice = ai_choice(b, side)
    if choice != 'tick':
        command(b, choice, side)


def heal(f):
    remaining = f['vigor_max']*.15
    for wound in f['wounds']:
        amount = min(wound['damage'], remaining)
        wound['damage'] = round(wound['damage']-amount, 4)
        wound['slots'] = wound['damage']/f['vigor_max']*12
        field = 'inner_wounds' if wound['kind'] == 'internal' else 'outer_wounds'
        f[field] = round(max(0., f[field]-amount), 4)
        f['vigor'] = round(min(f['vigor_max'], f['vigor']+amount), 4)
        remaining -= amount
    f['wounds'] = [w for w in f['wounds'] if w['damage'] > .0001]


def advance_time(b, full_auto=False):
    # Two fixed substeps keep movement at tenths while keeping endpoint traffic low.
    for _ in range(2):
        if b['status'] != 'active':
            break
        b['tick'] += 1
        b['elapsed'] = round(b['elapsed']+.1, 3)
        if b['elapsed'] >= b['ai_at']:
            for side in ([0, 1] if full_auto else [1]):
                ai_intent(b, side)
            b['ai_at'] = round(b['elapsed']+.5, 3)
        delta = 0.
        for f in b['fighters']:
            for field in ('openings', 'seals'):
                for state in f[field]:
                    state['remaining'] = round(state['remaining']-.1, 4)
                f[field] = [state for state in f[field] if state['remaining'] > 0]
            f['recovery'] = round(max(0., f['recovery']-.1), 4)
            f['attack_charge'] = round(min(3., f['attack_charge']+.1*WEAPONS[f['mode']]['speed']*f['attack_speed']), 5)
            f['posture'] = round(min(100., f['posture']+(.2 if f['casting'] else .65)), 4)
            f['breath'] = round(min(100., f['breath']+(.3 if f['casting'] else .9)), 4)
            f['qi'] = round(min(f['qi_max'], f['qi']+.08), 4)
            direction = f['move']
            desired = f['target_distance']
            if desired is not None:
                direction = 0 if abs(desired-b['distance']) < .11 else 1 if desired > b['distance'] else -1
            if direction and f['mobility'] >= 1:
                speed = f['move_speed']*(1.7 if f['step_until'] > b['elapsed'] else 1.)
                delta += direction*speed*.1
                f['mobility'] = round(max(0., f['mobility']-.7), 4)
            else:
                f['mobility'] = round(min(100., f['mobility']+.9), 4)
            f['guarding'] = f['guard_until'] > b['elapsed']
            f['parrying'] = f['parry_until'] > b['elapsed']
        # Keep internal movement precise; round only the public display.
        b['distance'] = round(max(1., min(10., b['distance']+delta)), 5)
        order = [b['tick'] % 2, 1-b['tick'] % 2]
        ready = []
        for side in order:
            f = b['fighters'][side]
            if f['casting']:
                f['casting']['remaining'] = round(max(0., f['casting']['remaining']-.1), 4)
                if f['casting']['remaining'] <= 0:
                    ready.append((side, deepcopy(f['casting'])))
            elif f['auto_attack'] and f['recovery'] <= 0 and f['attack_charge'] >= 1 and in_range(b['distance'], WEAPONS[f['mode']]['reach']):
                command(b, 'basic_thrust', side)
                if b['status'] != 'active':
                    break
        # Cast releases due on the same substep are resolved together, allowing ties.
        if b['status'] == 'active':
            releases = [(side, cast) for side, cast in ready if b['fighters'][side]['casting']]
            for side, _ in releases:
                b['fighters'][side]['casting'] = None
            for side, cast in releases:
                key = cast['action']
                f = b['fighters'][side]
                f['recovery'] = .65
                if key == 'heal':
                    heal(f)
                    record(b, side, key, outcome='healed', text=f['name']+'疗伤完成。')
                else:
                    attack(b, side, key, cast)
            check_defeat(b)
        if b['status'] == 'active' and b['elapsed'] >= LIMIT:
            scores = [occupancy(f) for f in b['fighters']]
            finish(b, None if abs(scores[0]-scores[1]) < .5 else int(scores[0] > scores[1]), '百二十息收招')


def step(b, choice):
    # Validate before incrementing revision, drawing RNG, or changing resources.
    command(b, choice)
    if choice in ('tick', 'auto'):
        advance_time(b, full_auto=choice == 'auto')
    b['round'] += 1


def view(b):
    data = deepcopy(b)
    data['distance'] = round(b['distance'], 1)
    data['actions'] = actions(b)
    data['opponent_intent'] = '正在施展' if b['fighters'][1]['casting'] else '择距积式'
    data['opponent_action'] = {}
    data['rules_name'] = '连续距离战斗'
    data['clock_step'] = DT
    for f in data['fighters']:
        weapon = WEAPONS[f['mode']]
        f.update(mode_name=weapon['name'], basic_reach=weapon['reach'], sequence=weapon['sequence'],
                 occupied_slots=round(occupancy(f), 3), capacity=12)
        slots = []
        for kind in ('wounds', 'openings', 'seals'):
            for state in f[kind]:
                slots.append(dict(kind=state.get('kind', 'opening' if kind == 'openings' else 'seal'),
                                  slots=round(state['slots'], 4), body=BODY[state['body']]))
        f['injury_slots'] = slots
        if f['casting']:
            key = f['casting']['action']
            f['casting']['name'] = '疗伤' if key == 'heal' else martial.ARTS[key]['name']
    data['summary'] = [dict(hits=sum(r.get('hit', False) for r in b['log'] if r['side'] == side),
                            damage=round(sum(r.get('damage', 0.) for r in b['log'] if r['side'] == side), 2),
                            breaks=sum(r.get('guard_break', False) for r in b['log'] if r['side'] == side)) for side in (0, 1)]
    return data
