"""Seed a playable cast from geography and the two nine-grade systems.

All assignments are declared opening conditions. Loading a save never calls this
module, and no past appointments, sect duties or biological ancestry are invented.
"""
from collections import Counter
import math

from . import careers, martial, sect_ranks, social_ranks
from .atlas import PLACES

VERSION = 1
SECT_SHARE = .28
SCHOOL_WEIGHTS = (1.2, 1.8, 1.6, 1.3, 1.1, 1., .95, 1., .8, 1.05, 1., .95, .75, .75)
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
    n['skill'] = max(n['skill'], p.get('martial', 0)) if adult else min(n['skill'], 10.)
    xp = max(p['entry'], min(65., (n['age_months'] / 12 - 16) * 1.1 + w.rng.uniform(0, 8))) if adult else 0.
    profession = job if n['age_months'] >= 120 else None
    n['career'].update(profession=profession, experience={job: round(xp, 4)} if profession else {},
                       since=w.month, last_change=w.month - 24, source='九品世界期初登记')


def job_weights(place):
    p = PLACES[place]
    weights = {'农人': 34, '行商': 12, '药师': 6, '匠人': 13, '镖师': 6,
               '渔人': 5, '猎户': 5, '书生': 6, '艺人': 6, '短工': 7}
    if p['kind'] in ('port', 'island'):
        weights.update(渔人=32, 行商=22, 农人=18, 猎户=1)
    elif p['country'] in (2, 5, 9):
        weights.update(猎户=28, 农人=23, 渔人=1, 行商=8)
    elif p['country'] in (3, 4, 7):
        weights.update(行商=25, 镖师=12, 渔人=0, 猎户=8)
    elif p['country'] in (0, 6, 8, 10, 11):
        weights['渔人'] = 14
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
        contribution, skill = sect_ranks.REQUIREMENTS[grade] or (110, 78)
        ceiling = (contribution + 20 if grade <= 2 else sect_ranks.REQUIREMENTS[grade - 1][0])
        n.update(faction=f['id'], faction_since=w.month,
                 contribution=round(w.rng.uniform(contribution, ceiling - .1), 4),
                 skill=w.rng.uniform(skill, min(95, skill + 12)),
                 reputation=w.rng.uniform(max(0, 40 - grade * 5), max(2, 65 - grade * 6)))
        trade(w, n, '药师' if f['public_axis'] == 'sanitation' else '镖师' if grade <= 7 else '农人')
        # Trade setup must not erase the martial prerequisites of young members.
        n['skill'] = max(skill, n['skill'])
        if grade == 1:
            f.update(leader=n['id'], leader_since=w.month, leader_review_since=w.month)
        members.append(n)
    return members


def seed(w, total):
    if total < 0:
        raise ValueError('人数不能为负')
    sites = w.active_places
    sect_total = round(total * SECT_SHARE)
    quotas = apportion(sect_total, SCHOOL_WEIGHTS,
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
            tribal = w.countries[p['country']]['kind'] == '诸部'
            specs += [('书生', 48, '部族首领' if tribal else '王侯' if p['country'] == 11 else '国主'),
                      ('书生', 43, '公卿' if tribal else '宰相'), ('镖师', 41, '大将军'),
                      ('书生', 35, '世家家主'), ('行商', 38, '豪商')]
        elif count >= 30 and p['kind'] in ('city', 'port'):
            specs.append(('行商', 38, '豪商'))
        for job, years, title in specs:
            civil(w, hub, job, years, title)
        for _ in range(count - len(specs)):
            civil(w, hub)
    # Declare initial guardianship, without fabricating parentage or marriages.
    for p in sites:
        local = [n for n in w.npcs.values() if n['place'] == p['id']]
        adults = [n for n in local if n['faction'] is None and n['age_months'] >= 360
                  and n['wealth'] >= 20 and n['traits']['compassion'] + n['traits']['loyalty'] >= 60]
        if not adults:
            adults = [n for n in local if n['age_months'] >= 360]
        for i, n in enumerate(q for q in local if q['age_months'] < 192):
            if adults:
                guardian = adults[i % len(adults)]
                n['guardian'] = guardian['id']
                w.link(guardian, n, 25, 30)
    for n in w.npcs.values():
        n.pop('martial', None)
        martial.initialize(n)
        if n['faction'] is not None and n['skill'] >= 12:
            f = w.factions[n['faction']]
            martial.learn(n, f['art_id'])
            art = n['martial']['arts'][f['art_id']]
            art.update(practice=round(min(85., n['skill']), 4), realm=int(n['skill'] >= 40))
        n['identity'] = careers.identity(w, n)
    # Link knowledge only after all final identities exist.
    for n in w.npcs.values():
        for other in n['relationships']:
            w.know(n, w.npcs[int(other)])
    w.population_generation = dict(version=VERSION, month=w.month, requested=total,
                                   sect_population=sect_total, civil_population=total - sect_total,
                                   initial_condition=True, household_basis='期初监护登记，不补造亲生谱系')


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
