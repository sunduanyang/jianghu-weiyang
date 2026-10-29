from . import temperament
from . import combat_attributes
"""Seed a playable cast from geography and the two nine-grade systems.

All assignments are declared opening conditions. Loading a save never calls this
module, and no past appointments, sect duties or biological ancestry are invented.
"""
from collections import Counter
import math

from . import careers, martial, sect_ranks, social_ranks
from .atlas import PLACES
from .sect_catalog import SCHOOLS

VERSION = 2
SECT_SHARE = .28
SCHOOL_WEIGHTS = {s['key']: s['weight'] for s in SCHOOLS}
GRADE_WEIGHTS = (0, 1, 2, 3, 4, 5, 7, 9, 11)
AGES = {1: (42, 64), 2: (36, 60), 3: (31, 54), 4: (27, 48),
        5: (23, 44), 6: (20, 38), 7: (18, 32), 8: (16, 27), 9: (12, 24)}


def apportion(total, weights, minimum=None):
    """Largest remainder allocation: exact total, deterministic ties, no RNG."""
    result = list(minimum or [0] * len(weights))
    remaining = total - sum(result)
    if remaining < 0:
        return apportion(total, [max(1, x) for x in result])
    if not weights or not remaining:
        return result
    denominator = sum(weights)
    shares = [remaining * weight / denominator for weight in weights]
    floors = [math.floor(x) for x in shares]
    result = [a + b for a, b in zip(result, floors)]
    order = sorted(range(len(weights)), key=lambda i: (-(shares[i] - floors[i]), i))
    for i in order[:total - sum(result)]:
        result[i] += 1
    return result


def age(w, n, years):
    n['age_months'] = years * 12 + w.rng.randrange(12)
    n['birth_month'] = w.month - n['age_months']


def trade(w, n, job):
    """An adult starts with the prerequisites for their registered livelihood."""
    p = careers.PROFESSIONS[job]
    adult = n['age_months'] >= 192
    n['job'] = job if adult else '学徒' if n['age_months'] >= 120 else '幼童'
    if adult:
        base = n['combat_attributes']['base']
        base.update({key: max(value, p.get('martial', 0)) for key, value in base.items()})
    xp = max(p['entry'], min(65., (n['age_months'] / 12 - 16) * 1.1 + w.rng.uniform(0, 8))) if adult else 0.
    profession = job if n['age_months'] >= 120 else None
    n['career'].update(profession=profession, experience={job: round(xp, 4)} if profession else {},
                       since=w.month, last_change=w.month - 24, source='九品世界期初登记')


def job_weights(place):
    p = PLACES[place]
    # Shares follow what households actually need each month (food first, few services).
    weights = {'农人': 40, '行商': 9, '药师': 3, '匠人': 13, '镖师': 3,
               '渔人': 5, '猎户': 5, '书生': 4, '艺人': 4, '短工': 6}
    if p['kind'] in ('port', 'island'):
        weights.update(渔人=26, 行商=22, 农人=22, 猎户=1)
    elif p['name'] == '苏州':
        weights.update(渔人=16, 匠人=20, 行商=14)
    elif p['name'] == '凉州':
        weights.update(行商=24, 镖师=12, 渔人=0, 猎户=10)
    elif p['name'] == '大理':
        weights.update(猎户=18, 药师=8, 渔人=8)
    elif p['name'] in ('成都', '襄阳'):
        weights['渔人'] = 10
    if p['kind'] == 'capital':
        weights['书生'] += 8
        weights['匠人'] += 6
    return weights


def civil(w, place, job=None, years=None, title=None):
    n = w.make_npc(place=place)
    if years is None:
        band = w.rng.choices(((7, 9), (10, 15), (16, 50), (51, 67)), (10, 8, 70, 12))[0]
        years = w.rng.randint(*band)
    age(w, n, years)
    weights = job_weights(place)
    job = job or w.rng.choices(list(weights), list(weights.values()))[0]
    trade(w, n, job)
    # Temperament follows the life the person actually leads (own stream; world RNG untouched).
    temperament.assign(w, n, job)
    if title:
        n['social_status'] = dict(title=title, country=PLACES[place]['country'],
                                  since=w.month, source='九品世界期初身份')
        grade = social_ranks.grade_for(title)
        n['wealth'] = round(w.rng.uniform(180, 420) if grade == 4 else
                           w.rng.uniform(450, 1200) if grade == 3 else w.rng.uniform(600, 1600), 2)
        n['reputation'] = w.rng.uniform(15, 50)
    elif n['age_months'] < 192:
        n['wealth'] = round(w.rng.uniform(2, 8), 2)
    elif job == '短工':
        n['wealth'] = round(w.rng.uniform(4, 14), 2)
    else:
        n['wealth'] = round(w.rng.uniform(20, 95), 2)
    return n


# How members of each tradition earn a living: temple fields, herb gardens, escorts,
# street trades. Drawn from a stream keyed by seed and person; the world RNG is untouched.
SECT_TRADES = {
    '佛门': {'农人': 5, '药师': 1, '匠人': 2, '镖师': 1},
    '道门': {'农人': 4, '药师': 1, '书生': 1, '镖师': 1},
    '帮会': {'短工': 3, '艺人': 2, '行商': 2, '镖师': 1},
    '剑派': {'镖师': 2, '猎户': 2, '农人': 2},
    '武人集团': {'镖师': 4, '匠人': 2, '农人': 2},
    '部族武帐': {'猎户': 3, '镖师': 2, '农人': 2},
    '祖灵信仰': {'药师': 1, '农人': 3, '猎户': 2},
    '萨满信仰': {'药师': 1, '猎户': 3, '农人': 2},
}


def sect_trade(w, f, n):
    import hashlib, random
    weights = SECT_TRADES.get(f['tradition'], {'农人': 2, '镖师': 1})
    digest = hashlib.sha256(f"{w.seed}:{n['id']}:sect-trade-v1".encode()).digest()
    return random.Random(int.from_bytes(digest[:8], 'big')).choices(list(weights), list(weights.values()))[0]


def school(w, f, count):
    if count >= 9:
        counts = apportion(count, GRADE_WEIGHTS, [1] * 9)
        grades = [g for g, size in enumerate(counts, 1) for _ in range(size)]
    else:
        grades = [1, 9, 8, 7, 6, 5, 4, 3, 2][:count]
    members = []
    for grade in grades:
        n = w.make_npc(place=f['place'])
        age(w, n, w.rng.randint(*AGES[grade]))
        contribution, required_ability = sect_ranks.REQUIREMENTS[grade] or (110, 78)
        ceiling = (contribution + 20 if grade <= 2 else sect_ranks.REQUIREMENTS[grade - 1][0])
        n.update(faction=f['id'], faction_since=w.month,
                 contribution=round(w.rng.uniform(contribution, ceiling - .1), 4),
                 combat_attributes={'version': combat_attributes.VERSION, 'baseline_version': combat_attributes.TUNING['version'], 'base': dict.fromkeys(combat_attributes.KEYS,
                     round(w.rng.uniform(required_ability, min(95, required_ability + 12)), 2))},
                 reputation=w.rng.uniform(max(0, 40 - grade * 5), max(2, 65 - grade * 6)))
        trade(w, n, sect_trade(w, f, n))
        temperament.assign(w, n, n['career']['profession'], f['tradition'])
        if grade == 1:
            f.update(leader=n['id'], leader_since=w.month, leader_review_since=w.month)
        members.append(n)
    return members


def seed(w, total):
    if total < 0:
        raise ValueError('人数不能为负')
    sites = w.active_places
    sect_total = round(total * SECT_SHARE)
    quotas = apportion(sect_total, [SCHOOL_WEIGHTS[f['key']] for f in w.factions],
                       [9 if sect_total >= 9 * len(w.factions) else 0] * len(w.factions))
    for f, count in zip(w.factions, quotas):
        school(w, f, count)
    capitals = {c['id']: next(p['id'] for p in sites if p['name'] == c['capital']) for c in w.countries}
    weights = [p['weight'] * (1.5 if p['id'] == capitals[p['country']] else 1.2 if p['kind'] == 'port' else 1.) for p in sites]
    minima = [17 if p['id'] == capitals[p['country']] else 12 for p in sites]
    reduced = [17 if p['id'] == capitals[p['country']] else 8 for p in sites]
    if total - sect_total < sum(minima) and total - sect_total >= sum(reduced):
        minima = reduced
    quotas = apportion(total - sect_total, weights, minima)
    for p, count in zip(sites, quotas):
        hub = p['id']
        specs = []
        if count >= 12:
            specs = [('书生', 28, '乡绅'), ('药师', 30, None), ('书生', 24, None),
                     ('行商', 32, None), ('镖师', 27, None), ('匠人', 29, None),
                     ('艺人', 23, None), ('短工', 25, None),
                     (None, 36, None), (None, 40, None), (None, 8, None), (None, 13, None)]
        if hub == capitals[p['country']] and count >= 17:
            # The local 府衙's senior posts and the city's leading households.
            specs += [('书生', 48, '知府'), ('书生', 43, '通判'), ('镖师', 41, '兵马都监'),
                      ('书生', 35, '世家家主'), ('行商', 38, '豪商')]
        # A large city has more than one gentry family and merchant house.
        for extra in range(max(0, count - 60) // 50):
            specs += [('书生', 33 + extra % 9, '乡绅'), ('行商', 34 + extra % 7, '豪商')]
        for job, years, title in specs:
            civil(w, hub, job, years, title)
        for _ in range(count - len(specs)):
            civil(w, hub)
    # Register the households and acquaintances that exist at the start (no past events);
    # children without a registered family then receive an initial guardian as before.
    from . import households
    by_place = {}
    for n in w.npcs.values():
        by_place.setdefault(n['place'], []).append(n)
    household_summary = households.register(w, by_place)
    for p in sites:
        local = [n for n in w.npcs.values() if n['place'] == p['id']]
        adults = [n for n in local if n['faction'] is None and n['age_months'] >= 360
                  and n['wealth'] >= 20 and n['traits']['kindness'] + n['traits']['integrity'] >= 60]
        if not adults:
            adults = [n for n in local if n['age_months'] >= 360]
        for i, n in enumerate(q for q in local if q['age_months'] < 192 and not q['parents']):
            if adults:
                guardian = adults[i % len(adults)]
                n['guardian'] = guardian['id']
                w.link(guardian, n, 25, 30)
    for n in w.npcs.values():
        n.pop('martial', None)
        martial.initialize(n)
        n['identity'] = careers.identity(w, n)
    # Link knowledge only after all final identities exist.
    for n in w.npcs.values():
        for other in n['relationships']:
            w.know(n, w.npcs[int(other)])
    w.population_generation = dict(version=VERSION, month=w.month, requested=total,
                                   sect_population=sect_total, civil_population=total - sect_total,
                                   initial_condition=True, households=household_summary,
                                   household_basis='期初登记婚姻、亲子与乡里旧识，不补写过往事件；其余未成年人期初登记监护')


def place_member(w, n, event_id):
    if n['faction'] is None:
        return
    w.city_positions[str(n['id'])].update(node=f"faction_{n['faction']}", event_id=event_id,
                                         source='门派驻地期初登记')


def census(w):
    living = [n for n in w.npcs.values() if n['alive']]
    return dict(population=len(living),
                civil_grades=dict(Counter(careers.identity(w, n)['grade'] for n in living if n['faction'] is None)),
                sects=[dict(id=f['id'], name=f['name'], place=f['place'], leader=f['leader'],
                            grades=dict(Counter(sect_ranks.standing(f, n)['grade'] for n in living if n['faction'] == f['id'])))
                       for f in w.factions],
                places=[dict(id=p['id'], name=p['name'], population=sum(n['place'] == p['id'] for n in living))
                        for p in w.active_places])
