"""Public, persisted border relations. All border payments enter an actual treasury."""
from copy import deepcopy

from .atlas import COUNTRIES, PLACES, ROUTES, distance
from . import territory

RELATIONS = {'通商': 2., '中立': 5., '交恶': 10.}


def new():
    countries = deepcopy(COUNTRIES)
    for c in countries:
        c['relations'] = {}
    for route in ROUTES:
        a, b = PLACES[route['a']]['country'], PLACES[route['b']]['country']
        if a != b:
            pair = tuple(sorted((a, b)))
            status = ('交恶' if pair == (0, 1) else '通商' if pair in
                      ((0, 6), (4, 7), (3, 4), (0, 10), (0, 11), (10, 11)) else '中立')
            countries[a]['relations'][str(b)] = status
            countries[b]['relations'][str(a)] = status
    return countries


def journey(w, origin, target):
    a, b = PLACES[origin], PLACES[target]
    ca, cb = territory.owner(w, origin), territory.owner(w, target)
    km = distance(origin, target)
    status = '境内' if ca == cb else w.countries[ca]['relations'].get(str(cb), '中立')
    toll = RELATIONS.get(status, 0.)
    fare = round(3 + km / 110 + (2 if a['kind'] == 'mountain' or b['kind'] == 'mountain' else 0), 2)
    return {'km': km, 'fare': fare, 'toll': toll, 'cost': round(fare+toll, 2),
            'border': status, 'origin_country': ca, 'destination_country': cb}


def annual(w):
    territory.refresh_relations(w)
    pairs = sorted({tuple(sorted((territory.owner(w, r['a']), territory.owner(w, r['b']))))
                    for r in ROUTES if territory.owner(w, r['a']) != territory.owner(w, r['b'])})
    # At most one change per year; both sides always see the same public relation.
    if not pairs or w.rng.random() >= .55:
        return
    a, b = w.rng.choice(pairs)
    old = w.countries[a]['relations'][str(b)]
    choices = ['中立'] if old != '中立' else ['通商', '交恶']
    status = w.rng.choice(choices)
    w.countries[a]['relations'][str(b)] = w.countries[b]['relations'][str(a)] = status
    w.emit('diplomacy', [], f"{w.countries[a]['name']}与{w.countries[b]['name']}的边境关系由{old}转为{status}，"
           f"往来关税调整为{RELATIONS[status]:.0f}银。", .88,
           facts={'countries': [a, b], 'previous': old, 'relation': status, 'border_toll': RELATIONS[status]})
