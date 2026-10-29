"""期初家户与乡里旧识：the families and acquaintances a new world starts with.

A new world used to begin as 1800 single strangers. Like the census and initial
guardianship, households are an honest initial condition: couples, the children
they are raising, grown children still living nearby, and a handful of local
acquaintances are registered as they stand at the start. No past events are
written; relationships carry their registered strength only. Registration uses
streams keyed by seed and town, never the world RNG, and only runs when a fresh
profiled world is generated (old saves are not re-written).
"""
import hashlib
import random

from . import appearance, demography, relationships, temperament

VERSION = 1
# Share of adults already married by age band (years), before celibate traditions.
MARRIED_SHARE = ((18, 23, .4), (23, 30, .78), (30, 50, .92), (50, 66, .82))
CELIBATE = ('佛门', '道门')
CHILD_PARENTED = .88
GROWN_CHILD_NEARBY = .45


def stream(w, *parts):
    digest = hashlib.sha256(':'.join(map(str, (w.seed, 'households-v1') + parts)).encode()).digest()
    return random.Random(int.from_bytes(digest[:8], 'big'))


def years(n):
    return n['age_months'] / 12


def marriage_share(age):
    return next((share for lo, hi, share in MARRIED_SHARE if lo <= age < hi), 0.)


def celibate(w, n):
    return n['faction'] is not None and w.factions[n['faction']]['tradition'] in CELIBATE


def set_tie(w, a, b, affinity, trust, romance=0., label=None):
    for p, q, shift in ((a, b, 0.), (b, a, 1.)):
        r = w.relation(p, q['id'])
        r.update(affinity=round(max(-100., min(100., affinity + shift)), 4),
                 trust=round(max(-100., min(100., trust + shift)), 4),
                 romance=round(max(0., min(100., romance)), 4), registered=True)
        r['label'] = label or relationships.label(w, p, q['id'], r)


def marry(w, rng, people):
    """Pair willing, mutually attracted adults of one town, closest suitable age first."""
    seekers = [n for n in people if 18 <= years(n) < 66 and not celibate(w, n)
               and rng.random() < marriage_share(years(n))]
    women = sorted((n for n in seekers if n['sex'] == '女'), key=lambda n: (n['age_months'], n['id']))
    men = [n for n in seekers if n['sex'] == '男']
    couples = []
    for woman in women:
        options = [m for m in men if m['spouse'] is None and -5 <= years(m) - years(woman) <= 15
                   and demography.attracted(woman, m) and demography.attracted(m, woman)]
        if not options:
            continue
        man = min(options, key=lambda m: (abs(years(m) - years(woman) - 3), m['id']))
        couples.append((woman, man))
        woman['spouse'], man['spouse'] = man['id'], woman['id']
    # Same-sex households exist where both partners are so inclined.
    for sex in ('女', '男'):
        rest = [n for n in seekers if n['spouse'] is None and n['sex'] == sex and n['orientation'] != '异性']
        while len(rest) >= 2:
            a = rest.pop(0)
            b = next((q for q in rest if abs(years(q) - years(a)) <= 12 and demography.attracted(a, q)
                      and demography.attracted(q, a)), None)
            if not b:
                continue
            rest.remove(b)
            a['spouse'], b['spouse'] = b['id'], a['id']
            couples.append((a, b))
    for a, b in couples:
        bond = rng.uniform(40, 75)
        set_tie(w, a, b, bond, bond * .8, rng.uniform(35, 70), '夫妻')
    return couples


def surname(w, child, father, used):
    """Children carry the family surname; another given name is chosen if that one is taken."""
    from .engine import GIVEN
    if child['name'][0] == father['name'][0]:
        return
    start = child['id'] % len(GIVEN)
    own = [child['name'][1:]] if len(child['name']) == 3 else []
    for given in own + [GIVEN[(start + k) % len(GIVEN)] for k in range(len(GIVEN))]:
        name = father['name'][0] + given
        if name not in used:
            used.discard(child['name']); used.add(name)
            child['name'] = name
            return


def adopt(w, rng, child, mother, father, used, grown=False):
    child['parents'] = [mother['id'], father['id']]
    for p in (mother, father):
        p['children'].append(child['id'])
    child['generation'] = max(mother.get('generation', 0), father.get('generation', 0)) + 1
    family_father = father if father['sex'] == '男' else mother
    surname(w, child, family_father, used)
    mother['last_birth'] = max(mother.get('last_birth', -60), w.month - child['age_months'])
    if not grown:
        child['guardian'] = mother['id']
        # Children take after the parents raising them; grown children keep their own temperament.
        temperament.inherit(w, child, [mother, father])
        child.pop('appearance', None)
        appearance.prepare(w, child, inherit=True)
    for p in (mother, father):
        set_tie(w, p, child, rng.uniform(50, 75) if not grown else rng.uniform(35, 70),
                rng.uniform(40, 65) if not grown else rng.uniform(30, 60), label='亲子')


def raise_children(w, rng, people, couples, used):
    homes = [(a, b) if a['sex'] == '女' or b['sex'] != '女' else (b, a) for a, b in couples if a['sex'] != b['sex']]
    def fits(mother, father, child, lo, hi):
        gap_m, gap_f = years(mother) - years(child), years(father) - years(child)
        # Siblings are spaced as births are (the engine also waits two years between births).
        spaced = all(abs(w.npcs[c]['age_months'] - child['age_months']) >= 15 for c in mother['children'])
        return lo <= gap_m <= hi and 17 <= gap_f <= 55 and spaced and len(mother['children']) < mother['family_size'] + 1
    adopted = 0
    for child in sorted((n for n in people if n['age_months'] < 192), key=lambda n: (n['age_months'], n['id'])):
        if rng.random() > CHILD_PARENTED:
            continue
        options = [(m, f) for m, f in homes if fits(m, f, child, 17, 42)]
        if options:
            mother, father = min(options, key=lambda h: (len(h[0]['children']), abs(years(h[0]) - years(child) - 26), h[0]['id']))
            adopt(w, rng, child, mother, father, used)
            adopted += 1
    grown = 0
    for n in sorted((n for n in people if 16 <= years(n) < 36 and n['spouse'] is None), key=lambda n: n['id']):
        if rng.random() > GROWN_CHILD_NEARBY:
            continue
        options = [(m, f) for m, f in homes if fits(m, f, n, 18, 42) and n['id'] not in (m['id'], f['id'])]
        if options:
            mother, father = min(options, key=lambda h: (len(h[0]['children']), abs(years(h[0]) - years(n) - 26), h[0]['id']))
            adopt(w, rng, n, mother, father, used, grown=True)
            grown += 1
    return adopted, grown


def acquaint(w, rng, people):
    """A few registered local acquaintances, more for sociable people, drawn toward shared lives."""
    adults = [n for n in people if n['age_months'] >= 192]
    pairs = 0
    for n in adults:
        wanted = 2 + int(n['traits']['sociability'] // 25)
        known = sum(1 for k, r in n['relationships'].items() if r.get('registered') and not r.get('label') in ('夫妻', '亲子'))
        if known >= wanted:
            continue
        others = [q for q in adults if q['id'] != n['id'] and str(q['id']) not in n['relationships']]
        weights = [1 + 2*(q['job'] == n['job']) + 2*(q['faction'] is not None and q['faction'] == n['faction'])
                   + (abs(years(q) - years(n)) <= 10) for q in others]
        for _ in range(min(wanted - known, len(others))):
            q = rng.choices(others, weights)[0]
            i = others.index(q); others.pop(i); weights.pop(i)
            fit = 1 - abs(n['traits']['integrity'] - q['traits']['integrity']) / 100
            affinity = rng.gauss(14 + 22*fit, 12)
            if rng.random() < .08:
                affinity = rng.uniform(-20, -6)  # an old, mild coolness — never a feud
            affinity = max(-24., min(70., affinity))
            set_tie(w, n, q, affinity, affinity * .7 + rng.uniform(-5, 5))
            pairs += 1
    return pairs


def register(w, people_by_place):
    """Register households and acquaintances town by town. Returns a summary for the world."""
    used = {n['name'] for n in w.npcs.values() if n['alive']}
    totals = dict(couples=0, children=0, grown_children=0, acquaintances=0)
    for place, people in sorted(people_by_place.items()):
        rng = stream(w, place)
        couples = marry(w, rng, people)
        children, grown = raise_children(w, rng, people, couples, used)
        totals['couples'] += len(couples); totals['children'] += children; totals['grown_children'] += grown
        totals['acquaintances'] += acquaint(w, rng, people)
    return dict(version=VERSION, initial_condition=True, **totals,
                basis='期初家户与乡里旧识登记：只登记开局时的婚姻、亲子与相识，不补写过往事件')
