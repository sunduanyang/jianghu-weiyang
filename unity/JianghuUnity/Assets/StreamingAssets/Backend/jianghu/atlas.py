"""Eight-region geography with stable place, route and organization IDs."""
import math

from .historical_map import COUNTRIES, ERA

MAP_VERSION = 5
MAP_BOUNDS = [0, 0, 1280, 760]
# Linear painted-map projection: the 1280x760 sheet frames the eight regions.
PROJECTION = dict(lon0=87.1, lat0=43.4, sx=26.4, sy=29.6)
# Version 4 framed the whole of East Asia; kept so the coastline can be re-projected offline.
LEGACY_PROJECTION = dict(lon0=73., lat0=54., sx=16.5, sy=18.5)

# name, longitude, latitude, region, district label, kind, trade, population weight
_SITES = [
    ('洛阳', 112.45, 34.62, 0, '河洛', 'city', 1.3, 16),
    ('长安', 108.94, 34.34, 1, '三秦', 'city', 1.25, 14),
    ('襄阳', 112.14, 32.04, 2, '汉水', 'city', 1.2, 13),
    ('成都', 104.07, 30.67, 3, '川西', 'city', 1.25, 14),
    ('苏州', 120.58, 31.3, 4, '吴中', 'city', 1.35, 15),
    ('大理', 100.27, 25.61, 5, '苍洱', 'city', 1.1, 10),
    ('泉州', 118.68, 24.87, 6, '刺桐', 'port', 1.4, 12),
    ('凉州', 102.64, 37.93, 7, '河西', 'city', 1.15, 9),
]

# Post roads between neighbouring regions (all overland).
_ROADS = [('洛阳', '长安'), ('洛阳', '襄阳'), ('洛阳', '苏州'), ('长安', '成都'), ('长安', '凉州'),
          ('长安', '襄阳'), ('襄阳', '成都'), ('襄阳', '苏州'), ('襄阳', '大理'), ('襄阳', '泉州'),
          ('成都', '大理'), ('苏州', '泉州')]


def project(lon, lat, projection=PROJECTION):
    p = projection
    return round(25 + (lon - p['lon0']) * p['sx'], 2), round(25 + (p['lat0'] - lat) * p['sy'], 2)


def unproject(x, y, projection=PROJECTION):
    p = projection
    return (x - 25) / p['sx'] + p['lon0'], p['lat0'] - (y - 25) / p['sy']


PLACES = []
for c in COUNTRIES:
    c['label_x'], c['label_y'] = project(*c['label'])

for i, (name, lon, lat, country, region, kind, trade, weight) in enumerate(_SITES):
    x, y = project(lon, lat)
    PLACES.append(dict(id=i, name=name, lon=lon, lat=lat, x=x, y=y, country=country,
                       region=region, kind=kind, trade=trade, weight=weight,
                       color=COUNTRIES[country]['color']))

from .sect_catalog import SCHOOLS

_site_ids = {p['name']: p['id'] for p in PLACES}
FACTIONS = [dict(**f, id=i, place=_site_ids[f['home']],
                 country=PLACES[_site_ids[f['home']]]['country'], leader=None, fund=100.)
            for i, f in enumerate(SCHOOLS)]
if sorted(f['country'] for f in FACTIONS) != [c['id'] for c in COUNTRIES]:
    raise ValueError('每个地区恰好一个门派')


def distance(a, b):
    """Great-circle km between two places."""
    a, b = PLACES[a], PLACES[b]
    lat1, lat2 = math.radians(a['lat']), math.radians(b['lat'])
    dlat, dlon = lat2-lat1, math.radians(b['lon']-a['lon'])
    h = math.sin(dlat/2)**2 + math.cos(lat1)*math.cos(lat2)*math.sin(dlon/2)**2
    return round(6371 * 2 * math.asin(min(1, math.sqrt(h))))


SEA_LANES = []
_edges = sorted(tuple(sorted((_site_ids[a], _site_ids[b]))) for a, b in _ROADS)
ROUTES = [{'a': a, 'b': b, 'km': distance(a, b), 'sea': False} for a, b in _edges]
NEIGHBORS = {p['id']: [] for p in PLACES}
for route in ROUTES:
    NEIGHBORS[route['a']].append(route['b'])
    NEIGHBORS[route['b']].append(route['a'])
