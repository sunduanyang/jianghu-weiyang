"""Original martial arts: practice gates, transmission and combat loadouts.

Initial portfolios are derived from existing skill without consuming the world RNG.
This also migrates old saves without rewriting any historical event or skill value.
"""
from copy import deepcopy

REALMS = ['初窥', '通晓', '精纯', '化境']
GATES = [40, 70, 100, 100]
ARTS = {
    'breath': dict(name='正元功', kind='inner', affinity='平和', grade=9, requirement=0,
                   description='调息养元，增加内力与护体。'),
    'iron_root': dict(name='磐石桩', kind='inner', affinity='刚阳', grade=8, requirement=15,
                     description='扎根守中，擅长抵御外伤。'),
    'cold_spring': dict(name='寒泉诀', kind='inner', affinity='柔阴', grade=7, requirement=25,
                       description='凝神聚气，内伤招式威力更高。'),
    'bridge_fist': dict(name='长桥拳', kind='attack', affinity='刚阳', grade=9, requirement=0,
                        damage_type='external', reach=[0, 2], power=1., qi=2, forms=0,
                        cooldown=0, description='近身短打，命中后积攒一式。'),
    'river_sword': dict(name='青溪剑', kind='attack', affinity='平和', grade=9, requirement=0,
                        damage_type='external', reach=[2, 4], power=1.12, qi=4, forms=0,
                        cooldown=0, description='以距离换取先机，适合中远距离。'),
    'wave_palm': dict(name='叠浪掌', kind='attack', affinity='柔阴', grade=8, requirement=12,
                      damage_type='internal', reach=[1, 3], power=1.65, qi=12, forms=2,
                      cooldown=2, description='消耗两式，以内劲穿透外伤防御。'),
    'stone_force': dict(name='破石劲', kind='attack', affinity='刚阳', grade=7, requirement=22,
                        damage_type='external', reach=[0, 1], power=2.15, qi=15, forms=3,
                        cooldown=3, description='近身重击，适合刚阳内功。'),
    'cloud_step': dict(name='游云步', kind='movement', affinity='平和', grade=9, requirement=0,
                       description='进退有度，提高闪避与移动步数。'),
    'swallow_step': dict(name='掠燕身', kind='movement', affinity='柔阴', grade=8, requirement=18,
                         description='轻身避实，精进后一次可移动两格。'),
}
KIND_NAMES = {'inner': '内功', 'attack': '摧破', 'movement': '身法'}


def bounded(value, lo=0, hi=100):
    return round(max(lo, min(hi, value)), 4)


def initialize(actor, player=False):
    if 'martial' in actor:
        return
    actor.setdefault('health', 100.)
    actor.setdefault('skill', 12. if player else 2.)
    skill = actor['skill']
    known = ['breath', 'bridge_fist', 'cloud_step']
    if player or skill >= 15:
        known.append('river_sword')
    if skill >= 25 and not player:
        known += ['wave_palm', 'iron_root']
    practice = min(65., max(5., skill * 1.1))
    actor['martial'] = {
        'arts': {key: {'practice': practice, 'realm': int(practice >= 40)} for key in known},
        'loadout': {'inner': 'breath', 'movement': 'cloud_step',
                    'attacks': [key for key in known if ARTS[key]['kind'] == 'attack'][:3]},
        'focus': 'breath', 'stamina': 100.,
    }


def learn(actor, art_id):
    if art_id not in ARTS:
        raise ValueError('没有这部功法')
    m = actor['martial']
    if art_id in m['arts']:
        raise ValueError('已经学会此功法')
    if actor['skill'] < ARTS[art_id]['requirement']:
        raise ValueError(f"武学须达到{ARTS[art_id]['requirement']}")
    m['arts'][art_id] = {'practice': 0., 'realm': 0}
    if ARTS[art_id]['kind'] == 'attack' and len(m['loadout']['attacks']) < 3:
        m['loadout']['attacks'].append(art_id)
    return art_id


def equip(actor, art_id):
    m = actor['martial']
    if art_id not in m['arts']:
        raise ValueError('尚未学会此功法')
    kind = ARTS[art_id]['kind']
    if kind == 'attack':
        attacks = m['loadout']['attacks']
        if art_id in attacks:
            if len(attacks) == 1:
                raise ValueError('至少保留一门摧破功法')
            attacks.remove(art_id)
        else:
            if len(attacks) >= 3:
                raise ValueError('最多装配三门摧破，请先卸下一门')
            attacks.append(art_id)
    else:
        m['loadout'][kind] = art_id


def practice(actor, rng, art_id=None, breakthrough=False, teacher=None, scale=1., equipment=None):
    m = actor['martial']
    art_id = art_id or m['focus']
    if art_id not in m['arts']:
        raise ValueError('尚未学会此功法')
    entry = m['arts'][art_id]
    stamina_cost = 12*scale
    if actor['health'] <= 30 or m['stamina'] < stamina_cost:
        raise ValueError('伤疲过重，请先休养')
    before = deepcopy(entry)
    gate = GATES[entry['realm']]
    if breakthrough:
        if entry['realm'] >= 3 or entry['practice'] < gate:
            raise ValueError('修习尚未抵达关隘')
        entry['realm'] += 1
        gain = .8*scale
    else:
        if entry['realm'] >= 3:
            raise ValueError('此功法已臻化境')
        if entry['practice'] >= gate:
            raise ValueError('已抵关隘，请先突破')
        progress = rng.uniform(6., 10.) * (1.3 if teacher else 1.) * scale * (1+(equipment or {}).get('training', 0))
        entry['practice'] = bounded(entry['practice'] + progress, hi=gate)
        gain = (rng.uniform(.4, .9)+(equipment or {}).get('training', 0)) * max(.12, 1-actor['skill']/120)*scale
    before_skill = actor['skill']
    actor['skill'] = bounded(actor['skill'] + gain)
    m['stamina'] = bounded(m['stamina'] - stamina_cost)
    m['focus'] = art_id
    return {'art': art_id, 'name': ARTS[art_id]['name'], 'before': before,
            'after': deepcopy(entry), 'breakthrough': breakthrough, 'teacher': teacher,
            'skill_gain': round(actor['skill']-before_skill, 4), 'stamina_cost': stamina_cost}


def auto_practice(actor, rng, scale=1., equipment=None):
    m = actor['martial']
    available = [k for k, e in m['arts'].items() if e['realm'] < 3]
    if not available:
        return {'mastered': True}
    # Rotate focus after a breakthrough; NPCs develop a portfolio over a lifetime.
    focus = min(available, key=lambda k: (m['arts'][k]['realm'], m['arts'][k]['practice'], k))
    e = m['arts'][focus]
    return practice(actor, rng, focus, e['practice'] >= GATES[e['realm']], scale=scale, equipment=equipment)


def transmit(teacher, pupil):
    source, dest = teacher['martial'], pupil['martial']
    options = [k for k in source['arts'] if k not in dest['arts'] and
               pupil['skill'] >= ARTS[k]['requirement']]
    if options:
        key = max(options, key=lambda k: (source['arts'][k]['realm'], ARTS[k]['grade'] * -1, k))
        learn(pupil, key)
        return {'art': key, 'name': ARTS[key]['name'], 'learned': True}
    return None


def recover(actor, amount=8):
    actor['martial']['stamina'] = bounded(actor['martial']['stamina'] + amount)


def stats(actor, equipment=None):
    m = actor['martial']
    inner_id, move_id = m['loadout']['inner'], m['loadout']['movement']
    inner, move = m['arts'][inner_id], m['arts'][move_id]
    strength = inner['realm'] + inner['practice']/100
    agility = move['realm'] + move['practice']/100
    skill = actor['skill']
    gear = equipment or {}
    return {'vigor': round((70 + skill*.65 + strength*6) * max(.25, actor['health']/100), 1),
            'qi': round(32 + strength*12, 1), 'attack': round(9+skill*.38+gear.get('attack', 0), 2),
            'outer_guard': round(3+skill*.06+strength*(3 if inner_id == 'iron_root' else 1.3)+gear.get('defense', 0), 2),
            'inner_guard': round(3+strength*2.4, 2), 'agility': round(7+skill*.1+agility*3, 2),
            'step': 2 if agility >= 1.4 else 1,
            'affinity': ARTS[inner_id]['affinity'], 'inner': ARTS[inner_id]['name']}


def view(actor, equipment=None):
    m = actor['martial']
    return {**deepcopy(m), 'stats': stats(actor, equipment), 'arts': [
        {**deepcopy(ARTS[key]), 'id': key, **deepcopy(entry),
         'realm_name': REALMS[entry['realm']], 'gate': GATES[entry['realm']],
         'ready': entry['realm'] < 3 and entry['practice'] >= GATES[entry['realm']],
         'equipped': key in m['loadout']['attacks'] or key in
                     [m['loadout']['inner'], m['loadout']['movement']]}
        for key, entry in m['arts'].items()]}
