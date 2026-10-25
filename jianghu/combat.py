"""Seeded distance combat shared by interactive spars and NPC grievances."""
from copy import deepcopy
import random

from . import martial


def fighter(actor, key):
    stats = martial.stats(actor)
    return {**stats, 'id': key, 'name': actor['name'], 'vigor_max': stats['vigor'],
            'qi_max': stats['qi'], 'forms': 0, 'guarding': False, 'cooldowns': {},
            'outer_wounds': 0., 'inner_wounds': 0., 'evading': False,
            'arts': deepcopy(actor['martial']['arts']),
            'attacks': actor['martial']['loadout']['attacks'].copy()}


def start(a, b, seed, battle_id='', spar=True):
    return {'id': battle_id, 'seed': seed, 'round': 1, 'distance': 3, 'status': 'active',
            'spar': spar, 'fighters': [fighter(a, a.get('id', 'player')), fighter(b, b['id'])],
            'log': [], 'winner': None, 'reason': '', 'settled': False}


def actions(battle, side=0):
    if battle['status'] != 'active':
        return []
    f = battle['fighters'][side]
    choices = []
    for key in f['attacks']:
        art = martial.ARTS[key]
        reason = ('距离不合' if not art['reach'][0] <= battle['distance'] <= art['reach'][1] else
                  '内力不足' if f['qi'] < art['qi'] else
                  f"需要{art['forms']}式" if f['forms'] < art['forms'] else
                  '招式调息中' if f['cooldowns'].get(key, 0) > battle['round'] else '')
        choices.append({'id': key, 'label': art['name'], 'enabled': not reason, 'reason': reason,
                        'reach': art['reach'], 'qi': art['qi'], 'forms': art['forms'],
                        'damage_type': art['damage_type']})
    for key, label, reason in [('advance', '进身', '已近身' if battle['distance'] == 0 else ''),
                               ('retreat', '退步', '已至最远' if battle['distance'] == 5 else ''),
                               ('guard', '守势', ''), ('breathe', '调息', ''), ('surrender', '认输', '')]:
        choices.append({'id': key, 'label': label, 'enabled': not reason, 'reason': reason})
    return choices


def finish(battle, winner, reason):
    battle.update(status='finished', winner=winner, reason=reason)


def perform(battle, choice, side):
    f, target = battle['fighters'][side], battle['fighters'][1-side]
    item = next((a for a in actions(battle, side) if a['id'] == choice), None)
    if not item or not item['enabled']:
        raise ValueError(item['reason'] if item else '不是当前可用招式')
    f['guarding'] = False
    f['evading'] = False
    row = {'round': battle['round'], 'side': side, 'actor': f['name'], 'action': choice,
           'label': item['label'], 'distance': battle['distance']}
    if choice == 'surrender':
        finish(battle, 1-side, '认输')
        row['text'] = f"{f['name']}收招认输。"
    elif choice in ('advance', 'retreat'):
        battle['distance'] = max(0, min(5, battle['distance'] + f['step'] * (1 if choice == 'retreat' else -1)))
        f['evading'] = True
        f['qi'] = min(f['qi_max'], f['qi']+2)
        row['distance'] = battle['distance']
        row['text'] = f"{f['name']}{item['label']}，双方距离变为{battle['distance']}。"
    elif choice == 'guard':
        f['guarding'] = True
        f['qi'] = min(f['qi_max'], f['qi']+4)
        f['forms'] = min(4, f['forms']+1)
        row['text'] = f"{f['name']}凝神守势，减轻下一次受击，积攒一式。"
    elif choice == 'breathe':
        f['qi'] = min(f['qi_max'], f['qi']+12)
        row['text'] = f"{f['name']}运转{f['inner']}调息，恢复12内力。"
    else:
        art, entry = martial.ARTS[choice], f['arts'][choice]
        f['qi'] = round(f['qi']-art['qi'], 4)
        f['forms'] -= art['forms']
        f['cooldowns'][choice] = battle['round'] + art['cooldown'] + 1
        rng = random.Random(battle['seed'] + battle['round']*1009 + side*9176)
        chance = max(.35, min(.95, .84+(f['agility']-target['agility'])*.006 - (.15 if target['evading'] else 0)))
        hit = rng.random() < chance
        damage = 0.
        if hit:
            matching = 1.12 if art['affinity'] == f['affinity'] and f['affinity'] != '平和' else 1.
            guard = target['inner_guard'] if art['damage_type'] == 'internal' else target['outer_guard']
            power = f['attack'] * art['power'] * (1+entry['realm']*.14+entry['practice']*.002) * matching
            damage = round(max(2., power*rng.uniform(.88, 1.12)-guard) * (.45 if target['guarding'] else 1), 2)
            target['vigor'] = round(max(0., target['vigor']-damage), 2)
            wound_key = 'inner_wounds' if art['damage_type'] == 'internal' else 'outer_wounds'
            target[wound_key] = round(target[wound_key]+damage, 2)
            f['forms'] = min(4, f['forms']+1)
        target['guarding'] = False
        target['evading'] = False
        row.update(hit=hit, damage=damage, damage_type=art['damage_type'], hit_chance=round(chance, 4))
        wound = '内伤' if art['damage_type'] == 'internal' else '外伤'
        row['text'] = f"{f['name']}使出{art['name']}，" + (f'造成{damage:g}点{wound}。' if hit else '被对手闪开。')
        if target['vigor'] <= 0:
            finish(battle, side, '气血不支')
    battle['log'].append(row)


def ai_choice(battle, side):
    f = battle['fighters'][side]
    available = [a for a in actions(battle, side) if a['enabled']]
    attacks = [a for a in available if a['id'] in martial.ARTS]
    if attacks:
        return max(attacks, key=lambda a: martial.ARTS[a['id']]['power'])['id']
    if f['qi'] < min(martial.ARTS[k]['qi'] for k in f['attacks']):
        return 'breathe'
    reachable = [martial.ARTS[k] for k in f['attacks'] if f['forms'] >= martial.ARTS[k]['forms']]
    if not reachable:
        return 'guard'
    best = min(reachable, key=lambda a: min(abs(battle['distance']-r) for r in a['reach']))
    if battle['distance'] > best['reach'][1]:
        return 'advance'
    if battle['distance'] < best['reach'][0]:
        return 'retreat'
    return 'guard'


def step(battle, choice):
    # Validate before changing any field, including round and the combat log.
    perform(battle, choice, 0)
    if battle['status'] == 'active':
        perform(battle, ai_choice(battle, 1), 1)
    if battle['status'] == 'active' and battle['round'] >= 60:
        ratios = [f['vigor']/f['vigor_max'] for f in battle['fighters']]
        finish(battle, None if abs(ratios[0]-ratios[1]) < .04 else int(ratios[1] > ratios[0]), '六十合收招')
    if battle['status'] == 'active':
        battle['round'] += 1


def autoplay(battle):
    while battle['status'] == 'active':
        step(battle, ai_choice(battle, 0))
    return battle


def view(battle):
    if not battle:
        return None
    return {**deepcopy(battle), 'actions': actions(battle),
            'opponent_intent': next((a['label'] for a in actions(battle, 1)
                                     if a['id'] == ai_choice(battle, 1)), '') if battle['status'] == 'active' else ''}
