"""Fixed regional jurisdictions. Each city answers to its own 府衙; there is no conquest.

Guard and stability remain abstract local readiness values (not extra NPCs) that
recover month by month; no polity can campaign against another.
"""
from .atlas import PLACES

GUARDS = {'capital': 80., 'city': 55., 'port': 45., 'town': 35., 'mountain': 60.}
NO_CAMPAIGNS = '江湖八方各归本地府衙，不设攻城'


def new():
    return [dict(owner=p['country'], guard=GUARDS[p['kind']], stability=100.,
                 changed_month=None, last_battle=-6, event_id=None) for p in PLACES]


def owner(w, place):
    from .settlements import canonical
    place = canonical(w, place)
    return w.territory[place]['owner']


def controlled(w, country):
    return [p['id'] for p in w.active_places if owner(w, p['id']) == country]


def refresh_relations(w):
    """Regions have no diplomacy; kept for callers that refresh after loading."""
    return None


def place_view(w, place):
    from .settlements import canonical
    p, t = PLACES[place], w.territory[canonical(w, place)]
    return {**p, **t, 'country': t['owner'], 'native_country': p['country'],
            'color': w.countries[t['owner']]['color'],
            'guard_max': GUARDS[p['kind']], 'occupied': False}


def attack_options(w, target):
    return []


def campaign(w, attacker, target, *, declare=False):
    raise ValueError(NO_CAMPAIGNS)


def monthly(w):
    for p in w.active_places:
        t = w.territory[p['id']]
        t['guard'] = min(GUARDS[p['kind']], t['guard']+2)
        t['stability'] = min(100., t['stability']+5)
