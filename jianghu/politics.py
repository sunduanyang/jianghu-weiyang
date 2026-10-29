"""Travel between the eight regions. No dynasties: no borders, tariffs or diplomacy."""
from copy import deepcopy

from .atlas import COUNTRIES, PLACES, distance
from . import territory


def new():
    countries = deepcopy(COUNTRIES)
    for c in countries:
        c['relations'] = {}
    return countries


def prepare(w):
    # Map colours are presentation: saves pick up the current palette.
    palette = {c['id']: c['color'] for c in COUNTRIES}
    for c in w.countries:
        c['color'] = palette.get(c['id'], c['color'])
        c['relations'] = {}
        c.pop('border_activity', None)
        c.pop('last_year_borders', None)


def record_crossing(w, trip):
    return None


def journey(w, origin, target):
    a, b = PLACES[origin], PLACES[target]
    ca, cb = territory.owner(w, origin), territory.owner(w, target)
    km = distance(origin, target)
    fare = round(3 + km / 110 + (2 if a['kind'] == 'mountain' or b['kind'] == 'mountain' else 0), 2)
    return {'km': km, 'fare': fare, 'toll': 0., 'cost': fare,
            'border': '驿道', 'origin_country': ca, 'destination_country': cb}


def annual(w):
    return None
