"""Offline historical geography with stable settlement and organization IDs."""
import math

from .historical_map import COUNTRIES, ERA
from . import atlas_expansion

MAP_VERSION = 4
MAP_BOUNDS = [0, 0, 1280, 760]

# name, longitude, latitude, country, region, kind, trade, population weight
_SITES = [
    ('青溪镇', 115.55, 27.45, 0, '江南西路', 'town', 1.05, 5),
    ('栖云山', 115.65, 29, 0, '江南西路', 'mountain', 0.8, 2),
    ('抚州', 116.36, 27.95, 0, '江南西路', 'city', 1.3, 12),
    ('桃花渡', 117.4, 27, 0, '江南西路', 'town', 0.95, 3),
    ('寒石寨', 117.4, 29.5, 0, '江南东路', 'mountain', 0.9, 2),
    ('江陵府', 112.24, 30.34, 0, '荆湖北路', 'city', 1.25, 14),
    ('襄阳府', 112.14, 32.04, 0, '京西南路', 'city', 1.2, 8),
    ('武当山', 111, 32.4, 0, '京西南路', 'mountain', 0.8, 3),
    ('潭州', 112.94, 28.23, 0, '荆湖南路', 'city', 1.15, 8),
    ('衡山', 112.65, 27.25, 0, '荆湖南路', 'mountain', 0.85, 2),
    ('庐州', 117.23, 31.82, 0, '淮南西路', 'city', 1.1, 7),
    ('河南府', 112.45, 34.62, 1, '南京路', 'city', 1.3, 16),
    ('嵩山', 113, 34.5, 1, '南京路', 'mountain', 0.8, 4),
    ('南京开封府', 114.31, 34.8, 1, '南京路', 'city', 1.3, 9),
    ('中都', 116.4, 39.9, 1, '中都路', 'capital', 1.25, 10),
    ('直沽', 117.2, 39.12, 1, '河北东路', 'port', 1.25, 6),
    ('太原府', 112.55, 37.87, 1, '河东北路', 'city', 1.1, 7),
    ('济南府', 117.12, 36.65, 1, '山东东路', 'city', 1.2, 8),
    ('泰山', 117.1, 36.25, 1, '山东西路', 'mountain', 0.85, 2),
    ('沈州', 123.43, 41.8, 1, '东京路', 'city', 1.15, 10),
    ('长白山', 128.07, 42, 1, '上京路', 'mountain', 0.75, 2),
    ('上京会宁府', 126.98, 45.54, 1, '上京路', 'city', 1.05, 7),
    ('黑水营地', 122.54, 52.97, 9, '黑水诸部', 'town', 0.8, 2),
    ('丰州', 111.75, 40.84, 1, '西京路', 'city', 1.05, 8),
    ('克鲁伦河营地', 117.5, 48.5, 2, '草原诸部', 'town', 0.95, 3),
    ('兴庆府', 106.23, 38.49, 3, '兴庆府', 'capital', 1.15, 8),
    ('京兆府', 108.94, 34.34, 1, '京兆府路', 'city', 1.25, 12),
    ('华山', 110.08, 34.48, 1, '京兆府路', 'mountain', 0.8, 3),
    ('兰州', 103.83, 36.06, 1, '临洮路', 'city', 1.05, 6),
    ('沙州', 94.66, 40.14, 3, '沙州', 'city', 1.15, 5),
    ('别失八里', 89.3, 44.0, 7, '别失八里', 'city', 1.15, 8),
    ('疏勒', 75.99, 39.47, 4, '疏勒', 'city', 1.2, 6),
    ('阿力麻里', 81.0, 44.1, 4, '伊丽河谷', 'city', 1.05, 4),
    ('天山', 84.5, 42.8, 4, '西域绿洲', 'mountain', 0.7, 2),
    ('逻些', 91.13, 29.65, 5, '卫地', 'city', 0.95, 8),
    ('日喀则', 88.88, 29.27, 5, '藏地', 'town', 0.85, 3),
    ('西宁州', 101.78, 36.62, 1, '临洮路', 'city', 1.05, 6),
    ('昆仑山', 94.3, 35.5, 5, '高原诸部', 'mountain', 0.7, 2),
    ('成都府', 104.07, 30.67, 0, '成都府路', 'city', 1.25, 14),
    ('重庆府', 106.55, 29.56, 0, '夔州路', 'city', 1.2, 7),
    ('峨眉山', 103.33, 29.52, 0, '成都府路', 'mountain', 0.85, 3),
    ('青城山', 103.56, 30.9, 0, '成都府路', 'mountain', 0.85, 3),
    ('大理城', 100.27, 25.61, 6, '大理府', 'capital', 1.1, 10),
    ('鄯阐府', 102.83, 24.88, 6, '鄯阐府', 'city', 1.1, 7),
    ('矩州', 106.63, 26.65, 0, '夔州路羁縻州', 'city', 1.05, 6),
    ('临安府', 120.16, 30.25, 0, '两浙西路', 'capital', 1.35, 15),
    ('建康府', 118.8, 32.06, 0, '江南东路', 'city', 1.3, 10),
    ('平江府', 120.58, 31.3, 0, '两浙西路', 'city', 1.35, 9),
    ('华亭县', 121.23, 31.02, 0, '两浙西路', 'port', 1.3, 7),
    ('福州', 119.3, 26.08, 0, '福建路', 'port', 1.2, 8),
    ('海东聚落', 120.2, 23, 8, '海东诸部', 'port', 1.1, 6),
    ('广州', 113.26, 23.13, 0, '广南东路', 'city', 1.35, 13),
    ('静江府', 110.3, 25.27, 0, '广南西路', 'city', 1.05, 6),
    ('琼州', 110.33, 20.03, 0, '广南西路', 'port', 1.1, 5),
    ('官富场', 114.17, 22.3, 0, '广南东路', 'port', 1.25, 4),
    ('香山海澳', 113.55, 22.2, 0, '广南东路', 'port', 1.25, 4),
    ('高昌城', 89.53, 42.85, 7, '高昌', 'capital', 1.1, 5),
    ('虎思斡耳朵', 75.3, 42.8, 4, '七河地区', 'capital', 1.15, 7),
]


def project(lon, lat):
    return round(25 + (lon - 73) * 16.5, 2), round(25 + (54 - lat) * 18.5, 2)


_SITES.extend(atlas_expansion.SITES)
PLACES = []
for c in COUNTRIES:
    c['label_x'], c['label_y'] = project(*c['label'])

for i, (name, lon, lat, country, region, kind, trade, weight) in enumerate(_SITES):
    x, y = project(lon, lat)
    PLACES.append(dict(id=i, name=name, lon=lon, lat=lat, x=x, y=y, country=country,
                       region=region, kind=kind, trade=trade, weight=weight,
                       color=COUNTRIES[country]['color']))

# Runtime IDs are dense; save upgrades resolve the stable key / name, never the index.
from .sect_catalog import SCHOOLS

_site_ids = {p['name']: p['id'] for p in PLACES}
FACTIONS = [dict(**f, id=i, place=_site_ids[f['home']],
                 country=PLACES[_site_ids[f['home']]]['country'], leader=None, fund=100.)
            for i, f in enumerate(SCHOOLS)]


def distance(a, b):
    """Great-circle km; also used to order the public route shortlist."""
    a, b = PLACES[a], PLACES[b]
    lat1, lat2 = math.radians(a['lat']), math.radians(b['lat'])
    dlat, dlon = lat2-lat1, math.radians(b['lon']-a['lon'])
    h = math.sin(dlat/2)**2 + math.cos(lat1)*math.cos(lat2)*math.sin(dlon/2)**2
    return round(6371 * 2 * math.asin(min(1, math.sqrt(h))))


# Each landmass has local roads; crossing to an island requires named ports.
def landmass(p):
    if p['country'] == 8:
        return 'haidong'
    if p['country'] == 11:
        if p['region'] == '九州':
            return 'kyushu'
        if p['region'] == '四国':
            return 'shikoku'
        if p['region'] == '西海道':
            return 'tsushima'
        return 'honshu'
    if p['name'] == '耽罗':
        return 'tamna'
    if p['id'] == 53 or p['name'] == '儋州':
        return 'hainan'
    return 'mainland'


_edges = set()
for p in PLACES:
    for q in sorted((q for q in PLACES if q['id'] != p['id'] and landmass(q) == landmass(p)),
                    key=lambda q: distance(p['id'], q['id']))[:4]:
        _edges.add(tuple(sorted((p['id'], q['id']))))
_edges.update(tuple(sorted(e)) for e in [(29, 30), (29, 31), (28, 36), (36, 34), (34, 42),
                                        (25, 23), (23, 19), (19, 21), (21, 24),
                                        (49, 50), (49, 51), (51, 53)])
SEA_LANES = [('明州', '博多津'), ('登州', '西京平壤'), ('金州', '对马府中'),
             ('对马府中', '博多津'), ('博多津', '长门府中'), ('难波津', '阿波国府'),
             ('备前国府', '伊予国府'), ('金州', '耽罗'), ('泉州', '海东聚落'),
             ('雷州', '琼州')]
_sea_edges = {tuple(sorted((_site_ids[a], _site_ids[b]))) for a, b in SEA_LANES}
_sea_edges.update({(49, 50), (51, 53)})
_edges.update(_sea_edges)
ROUTES = [{'a': a, 'b': b, 'km': distance(a, b),
           'sea': (a, b) in _sea_edges} for a, b in sorted(_edges)]
NEIGHBORS = {p['id']: [] for p in PLACES}
for route in ROUTES:
    NEIGHBORS[route['a']].append(route['b'])
    NEIGHBORS[route['b']].append(route['a'])
