"""Pausable combat beats, committed intentions and interruptible techniques.

Only new bouts opt in. No wall clock, world RNG, or view request changes a bout.
Both clients drive the same atomic beat endpoint, with explicit manual stepping.
"""
from copy import deepcopy
import random

from . import martial

TOKENS = {'thrust': '刺', 'slash': '劈', 'crush': '崩', 'focus': '凝'}
BASICS = {
    'basic_thrust': dict(name='直刺', token='thrust', power=.72, accuracy=.08, break_power=.7),
    'basic_slash': dict(name='横劈', token='slash', power=.88, accuracy=0., break_power=1.1),
    'basic_crush': dict(name='崩打', token='crush', power=.8, accuracy=-.04, break_power=2.),
}
MODES = {'fist': ('拳脚', [0, 2]), 'blade': ('兵刃', [2, 4])}
PREPARATIONS = {'advance', 'retreat', 'guard', 'parry', 'breathe', 'wait', 'mode_fist', 'mode_blade'}


def initialize(b):
    b['ruleset'] = 2
    for f in b['fighters']:
        f.update(tokens=[], mobility=100., posture=60., posture_max=60., casting=None,
                 opening_until=0, parrying=False, basic_index=0,
                 mode='blade' if 'river_sword' in f['attacks'] else 'fist')
    b['intent'] = ai_choice(b, 1)


def tokens(f):
    # A scalar form count in a tool/imported fixture remains usable as neutral forms.
    return (f['tokens'] + ['focus'] * max(0, f['forms']-len(f['tokens'])))[:f['forms']]


def payment(f, art):
    pool = tokens(f).copy()
    for needed in art.get('pattern', []):
        key = needed if needed in pool else 'focus'
        if key not in pool:
            return None
        pool.remove(key)
    count = art['forms'] - len(art.get('pattern', []))
    if len(pool) < count:
        return None
    return pool[max(0, count):]


def add_token(f, token):
    f['tokens'] = (tokens(f) + [token])[-4:]
    f['forms'] = len(f['tokens'])


def actions(b, side=0):
    if b['status'] != 'active':
        return []
    f = b['fighters'][side]
    busy = f['casting']
    reach = MODES[f['mode']][1]
    choices = []
    for key, basic in BASICS.items():
        reason = '运功中' if busy else '距离不合' if not reach[0] <= b['distance'] <= reach[1] else ''
        choices.append(dict(id=key, label=basic['name'], kind='basic', enabled=not reason,
                            reason=reason, reach=reach, qi=0, forms=0, token=basic['token'],
                            damage_type='external', description='命中积攒'+TOKENS[basic['token']]+'式'))
    for key in f['attacks']:
        art = martial.ARTS[key]
        reason = ('运功中' if busy else
                  '距离不合' if not art['reach'][0] <= b['distance'] <= art['reach'][1] else
                  '内力不足' if f['qi'] < art['qi'] else
                  f"需要{art['forms']}式" if f['forms'] < art['forms'] else
                  '缺少'+''.join(TOKENS[k] for k in art.get('pattern', []))+'式（凝可替代）' if payment(f, art) is None else
                  '调息'+str(f['cooldowns'][key]-b['round'])+'拍' if f['cooldowns'].get(key, 0) > b['round'] else '')
        choices.append(dict(id=key, label=art['name'], kind='art', enabled=not reason,
                            reason=reason, **{k: art[k] for k in ('reach', 'qi', 'forms', 'damage_type')},
                            cast=art.get('cast', 0), pattern=art.get('pattern', []),
                            description=art['description']))
    specs = [
        ('advance', '进身', '已近身' if b['distance'] == 0 else '脚力不足' if f['mobility'] < 22 else '', '靠近'+str(f['step'])+'格 · 22脚力'),
        ('retreat', '退步', '已至最远' if b['distance'] == 5 else '脚力不足' if f['mobility'] < 22 else '', '远离'+str(f['step'])+'格 · 22脚力'),
        ('guard', '架招', '', '减伤55% · 恢复架势 · 积攒凝式'),
        ('parry', '拆招', '内力不足' if f['qi'] < 6 else '', '6内力 · 化解普攻，反震架势；挡不住摧破'),
        ('breathe', '调息', '运功中' if busy else '', '恢复12内力，受击加重25%'),
        ('wait', '候招', '运功中' if busy else '', '回复脚力，等待距离或时机'),
        ('mode_fist' if f['mode'] == 'blade' else 'mode_blade', '切换拳脚' if f['mode'] == 'blade' else '切换兵刃',
         '尚未装配剑法' if f['mode'] == 'fist' and not any(k in f['attacks'] for k in ('river_sword', 'reed_cut')) else '',
         '普攻距离 0–2' if f['mode'] == 'blade' else '普攻距离 2–4'),
        ('surrender', '认输', '', '收招，结算此战'),
    ]
    if busy:
        specs.insert(0, ('continue', '继续运功', '', '尚需'+str(busy['remaining'])+'拍 · 进退或架招会中止运功'))
    for key, label, reason, desc in specs:
        choices.append(dict(id=key, label=label, kind='tactic', enabled=not reason, reason=reason, description=desc))
    return choices


def auto_action(b, side):
    f = b['fighters'][side]
    if f['casting']:
        return 'continue'
    key = list(BASICS)[f['basic_index'] % 3]
    return key if next(a for a in actions(b, side) if a['id'] == key)['enabled'] else 'wait'


def record(b, side, choice, **extra):
    f = b['fighters'][side]
    row = dict(round=b['round'], side=side, actor=f['name'], action=choice,
               distance=b['distance'], label=extra.pop('label', choice))
    row.update(extra)
    b['log'].append(row)
    return row


def finish(b, winner, reason):
    b.update(status='finished', winner=winner, reason=reason)
    for f in b['fighters']:
        f['casting'] = None


def attack(b, side, key):
    f, target = b['fighters'][side], b['fighters'][1-side]
    basic = key in BASICS
    art = BASICS[key] if basic else martial.ARTS[key]
    reach = MODES[f['mode']][1] if basic else art['reach']
    row = record(b, side, key, label=art['name'], damage=0., hit=False, kind='basic' if basic else 'art')
    if not reach[0] <= b['distance'] <= reach[1]:
        row.update(outcome='out_of_range', text=f"{f['name']}的{art['name']}落空，对手已离开攻击范围。")
        return
    rng = random.Random(b['seed'] + b['round']*1009 + side*9176)
    chance = max(.35, min(.95, .84+(f['agility']-target['agility'])*.006 + art.get('accuracy', 0.) - (.15 if target['evading'] else 0)))
    row['hit_chance'] = round(chance, 4)
    row['damage_type'] = 'external' if basic else art['damage_type']
    if rng.random() >= chance:
        row.update(outcome='evaded', text=f"{f['name']}使出{art['name']}，被对手闪开。")
        target['evading'] = False
        return
    if basic and target['parrying']:
        target['parrying'] = False
        add_token(target, 'focus')
        drain_posture(b, f, 24., row, 1-side)
        row.update(outcome='parried', text=f"{target['name']}拆开{art['name']}，反震24架势。" + ('对手失衡，运功中断！' if row.get('guard_break') else ''))
        return
    kind = row['damage_type']
    guard = target['inner_guard'] if kind == 'internal' else target['outer_guard']
    entry = {} if basic else f['arts'][key]
    matching = 1.12 if not basic and art['affinity'] == f['affinity'] and f['affinity'] != '平和' else 1.
    power = f['attack'] * art['power'] * (1+entry.get('realm', 0)*.14+entry.get('practice', 0)*.002) * matching
    exposed = target['opening_until'] > b['round']
    damage = round(max(2., power*rng.uniform(.88, 1.12)-guard) * (.45 if target['guarding'] else 1.) * (1.25 if exposed else 1.), 2)
    target['vigor'] = round(max(0., target['vigor']-damage), 2)
    wound_key = 'inner_wounds' if kind == 'internal' else 'outer_wounds'
    target[wound_key] = round(target[wound_key]+damage, 2)
    row.update(hit=True, damage=damage, outcome='hit', exposed=exposed, guarded=target['guarding'])
    drain_posture(b, target, max(6., power*.6)*art.get('break_power', 1.) * (1.5 if target['guarding'] else 1.), row, side)
    if basic:
        add_token(f, art['token'])
        f['basic_index'] += 1
    elif art['forms'] == 0:
        add_token(f, 'thrust' if key == 'river_sword' else 'crush')
    target['guarding'] = target['parrying'] = target['evading'] = False
    row['text'] = f"{f['name']}使出{art['name']}，造成{damage:g}点{'内伤' if kind == 'internal' else '外伤'}。" + ('架势崩解，运功中断！' if row.get('guard_break') else '')
    if target['vigor'] <= 0:
        finish(b, side, '气血不支')


def drain_posture(b, target, amount, row, breaker):
    target['posture'] = round(max(0., target['posture']-amount), 2)
    row['posture_damage'] = round(amount, 2)
    if target['posture'] <= 0:
        target['casting'] = None
        target['opening_until'] = b['round']+2
        target['posture'] = round(target['posture_max']*.4, 2)
        row['guard_break'] = True
        row['break_by'] = breaker


def perform(b, choice, side, committed=False):
    if choice == 'auto':
        choice = auto_action(b, side)
    item = next((a for a in actions(b, side) if a['id'] == choice), None)
    allowed_miss = committed and item and item['reason'] == '距离不合' and (choice in BASICS or choice in b['fighters'][side]['attacks'])
    if not item or (not item['enabled'] and not allowed_miss):
        raise ValueError(item['reason'] if item else '不是当前可用招式')
    f = b['fighters'][side]
    f['guarding'] = f['parrying'] = f['evading'] = False
    if f['casting'] and choice != 'continue':
        old = f['casting']['action']
        f['casting'] = None
        record(b, side, 'cancel', text=f"{f['name']}中止{martial.ARTS[old]['name']}，已用内力与式不返还。")
    if choice == 'surrender':
        finish(b, 1-side, '认输')
        record(b, side, choice, text=f"{f['name']}收招认输。")
    elif choice == 'continue':
        cast = f['casting']
        cast['remaining'] -= 1
        if cast['remaining'] <= 0:
            f['casting'] = None
            attack(b, side, cast['action'])
        else:
            record(b, side, choice, text=f"{f['name']}继续运转{martial.ARTS[cast['action']]['name']}，尚需{cast['remaining']}拍。")
    elif choice in BASICS:
        attack(b, side, choice)
    elif choice in martial.ARTS:
        art = martial.ARTS[choice]
        f['qi'] = round(f['qi']-art['qi'], 4)
        f['tokens'] = payment(f, art)
        f['forms'] = len(f['tokens'])
        f['cooldowns'][choice] = b['round']+art['cooldown']+art.get('cast', 0)+1
        if art.get('cast', 0):
            f['casting'] = dict(action=choice, remaining=art['cast'], total=art['cast'])
            record(b, side, choice, label=art['name'], outcome='windup', text=f"{f['name']}开始运转{art['name']}，{art['cast']}拍后出手。")
        else:
            attack(b, side, choice)
    else:
        if choice in ('advance', 'retreat'):
            b['distance'] = max(0, min(5, b['distance']+f['step']*(1 if choice == 'retreat' else -1)))
            f['mobility'] -= 22
            f['evading'] = True
        elif choice == 'guard':
            f['guarding'] = True
            f['posture'] = min(f['posture_max'], f['posture']+18)
            f['qi'] = min(f['qi_max'], f['qi']+4)
            add_token(f, 'focus')
        elif choice == 'parry':
            f['parrying'] = True
            f['qi'] -= 6
        elif choice == 'breathe':
            f['qi'] = min(f['qi_max'], f['qi']+12)
            f['opening_until'] = b['round']+1
        elif choice.startswith('mode_'):
            f['mode'] = choice[5:]
        record(b, side, choice, label=item['label'], text=f"{f['name']}{item['label']}。" + (f"双方距离{b['distance']}格。" if choice in ('advance', 'retreat') else item['description']))


def committed_action(b, side, choice):
    item = next((a for a in actions(b, side) if a['id'] == choice), None)
    if item and (item['enabled'] or item['reason'] == '距离不合'):
        perform(b, choice, side, committed=True)
    else:
        record(b, side, choice, outcome='interrupted', text=b['fighters'][side]['name']+'原定招式未能施展：'+(item['reason'] if item else '运功已中断')+'。')


def ai_choice(b, side):
    f, target = b['fighters'][side], b['fighters'][1-side]
    legal = {a['id']: a for a in actions(b, side) if a['enabled']}
    if not legal:
        return 'wait'
    threat = target['casting']
    if threat:
        art = martial.ARTS[threat['action']]
        # Choose a concrete escape before the other fighter acts; no hidden re-pick.
        for key, sign in [('retreat', 1), ('advance', -1)]:
            d = max(0, min(5, b['distance']+sign*f['step']))
            if key in legal and not art['reach'][0] <= d <= art['reach'][1]:
                return key
        if not f['casting'] and 'guard' in legal:
            return 'guard'
    if f['casting']:
        return 'continue'
    if f['posture'] < 18:
        return 'guard'
    if f['qi'] < 8:
        return 'breathe'
    arts = [a for a in legal.values() if a['id'] in f['attacks']]
    strong = [a for a in arts if a['forms'] > 0]
    if strong:
        return max(strong, key=lambda a: martial.ARTS[a['id']]['power'])['id']
    if target['parrying'] and arts:
        return arts[0]['id']
    basic = auto_action(b, side)
    if basic != 'wait':
        return basic
    if arts:
        return max(arts, key=lambda a: martial.ARTS[a['id']]['power'])['id']
    reach = MODES[f['mode']][1]
    direction = 'advance' if b['distance'] > reach[1] else 'retreat'
    return direction if direction in legal else 'guard'


def step(b, choice):
    actual = auto_action(b, 0) if choice == 'auto' else choice
    item = next((a for a in actions(b) if a['id'] == actual), None)
    if not item or not item['enabled']:
        raise ValueError(item['reason'] if item else '不是当前可用招式')
    enemy = b.get('intent') or ai_choice(b, 1)
    for f in b['fighters']:
        f['mobility'] = min(100., f['mobility']+10)
        f['posture'] = min(f['posture_max'], f['posture']+3)
    # Both sides have committed. Repositioning/defence precedes attacks so a
    # telegraphed cast can actually be escaped or guarded by either fighter.
    order = [(1, enemy), (0, actual)] if enemy in PREPARATIONS and actual not in PREPARATIONS and actual != 'surrender' else [(0, actual), (1, enemy)]
    for side, action in order:
        if b['status'] == 'active':
            committed_action(b, side, action)
    if b['status'] == 'active' and b['round'] >= 60:
        ratios = [f['vigor']/f['vigor_max'] for f in b['fighters']]
        finish(b, None if abs(ratios[0]-ratios[1]) < .04 else int(ratios[1] > ratios[0]), '六十合收招')
    if b['status'] == 'active':
        b['round'] += 1
        b['intent'] = ai_choice(b, 1)


def view(b):
    data = deepcopy(b)
    data['actions'] = actions(b)
    enemy_actions = actions(b, 1)
    intent = next((a for a in enemy_actions if a['id'] == b.get('intent')), {})
    data['opponent_intent'] = intent.get('label', '')
    data['opponent_action'] = deepcopy(intent)
    for f in data['fighters']:
        f['mode_name'], f['basic_reach'] = MODES[f['mode']]
        f['tokens'] = tokens(f)
        if f['casting']:
            f['casting']['name'] = martial.ARTS[f['casting']['action']]['name']
    data['summary'] = [dict(hits=sum(r.get('hit', False) for r in b['log'] if r['side'] == side),
                            damage=round(sum(r.get('damage', 0.) for r in b['log'] if r['side'] == side), 2),
                            breaks=sum(r.get('guard_break', False) for r in b['log'] if r.get('break_by', r['side']) == side)) for side in (0, 1)]
    return data
