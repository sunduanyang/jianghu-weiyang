"""Consolidated playable settlements; historical IDs remain valid in old journals."""
from collections import Counter
from copy import deepcopy
import zlib

from .atlas import PLACES, ROUTES, distance

VERSION = 2
# Version 1: the first 24 regions. Version 2 adds regions so that each power's count follows the land
# it holds (steppe, Western Liao, Tibet and the northern forests had one town each for a quarter of the
# map). Every v1 hub stays a hub, so no resident, faction seat or protagonist position is invalidated.
V1_HUB_NAMES = ('临安府', '建康府', '抚州', '江陵府', '襄阳府', '成都府', '广州', '泉州',
                '中都', '南京开封府', '京兆府', '上京会宁府', '克鲁伦河营地', '兴庆府', '沙州',
                '虎思斡耳朵', '逻些', '大理城', '高昌城', '海东聚落', '黑水营地', '开京', '镰仓', '平安京')
ADDED_HUB_NAMES = ('太原府', '济南府', '辽阳府', '兰州',                                   # 金
                   '斡难河营地', '土兀剌河营地', '鄂尔浑河营地', '杭爱牧集', '阿尔泰牧集',   # 草原诸部
                   '甘州',                                                                 # 西夏
                   '疏勒', '和阗', '阿克苏', '阿力麻里',                                     # 西辽
                   '日喀则', '昌都', '理塘', '纳里牧集',                                     # 吐蕃诸部
                   '鄯阐府',                                                               # 大理
                   '焉耆', '哈密力',                                                       # 高昌回鹘
                   '松林营地', '乌苏里营地', '下江渔集', '北山营地')                         # 林海诸部
HUB_NAMES = V1_HUB_NAMES + ADDED_HUB_NAMES
ADDED_TARGET = 16           # residents registered in each region added by version 2


def _ids(names):
    return tuple(next(p['id'] for p in PLACES if p['name'] == name) for name in names)


def _nearest(hubs):
    return {p['id']: min((h for h in hubs if PLACES[h]['country'] == p['country']),
                         key=lambda h: (distance(p['id'], h), h)) for p in PLACES}


V1_HUBS = _ids(V1_HUB_NAMES)
HUBS = _ids(HUB_NAMES)
V1_HUB_FOR = _nearest(V1_HUBS)
HUB_FOR = _nearest(HUBS)
_edges = {}
for r in ROUTES:
    a, b = sorted((HUB_FOR[r['a']], HUB_FOR[r['b']]))
    if a != b:
        _edges[a, b] = _edges.get((a, b), True) and r['sea']
COMPACT_ROUTES = [dict(a=a, b=b, km=distance(a, b), sea=sea) for (a, b), sea in sorted(_edges.items())]
COMPACT_NEIGHBORS = {h: [] for h in HUBS}
for r in COMPACT_ROUTES:
    COMPACT_NEIGHBORS[r['a']].append(r['b'])
    COMPACT_NEIGHBORS[r['b']].append(r['a'])


def profile():
    return dict(version=VERSION, hubs=list(HUBS), mapping={str(k): v for k, v in HUB_FOR.items()},
                population_registered=True, migration=None)


def canonical(w, place):
    return HUB_FOR[place] if w.settlements else place


def align_factions(w):
    if w.settlements:
        for f in w.factions:
            f['native_place'] = f.get('native_place', f['place'])
            f['place'] = HUB_FOR[f['native_place']]


def register_position(w, n, event_id):
    from . import city_maps
    city = city_maps.world_layout(w, n['place'])
    # Spread by a hash of the id: new worlds deal residents round-robin over the hubs, so a plain
    # id % 7 would put every resident of one hub in the inn whenever the hub count is a multiple of 7.
    spread = zlib.crc32(str(n['id']).encode())
    wanted = ('government' if n['office'] else 'residence' if n['age_months'] < 120 else
              'inn' if spread % 7 == 0 else 'gate' if spread % 11 == 0 else
              city_maps.WORK_PLACES.get(n['job'], 'residence'))
    node = next((q for q in city['nodes'] if q['id'] == wanted), None)
    if node is None:
        node = next(q for q in city['nodes'] if q['id'] == 'residence')
    w.city_positions[str(n['id'])] = dict(place=n['place'], node=node['id'], event_id=event_id,
                                         month=w.month, action='registration', source='乡籍安置')


def activate(w):
    """One migration, explicitly registering added residents at the current month."""
    if w.settlements:
        return False
    from . import city_maps, items, territory
    w.settlements = profile()
    w.settlements['migration'] = dict(month=w.month, original_places=len(PLACES),
                                     territory=deepcopy(w.territory), campaigns=deepcopy(w.campaigns))
    align_factions(w)
    moved = []
    for n in w.npcs.values():
        if not n['alive']:
            continue
        old = n['place']
        n['place'] = HUB_FOR[old]
        if old != n['place']:
            moved.append(dict(npc=n['id'], origin=old, destination=n['place']))
            # Retain the old plan as evidence rather than executing a removed route.
            if n['plan']:
                n['settlement_prior_plan'] = deepcopy(n['plan'])
                n['plan'] = None
    h = w.player['protagonist']
    if h['place'] != HUB_FOR[h['place']]:
        h['place'] = HUB_FOR[h['place']]
        gate = next(n for n in city_maps.world_layout(w, h['place'])['nodes'] if n['id'] == 'gate')
        h.update(row=gate['row'], col=gate['col'], revision=h['revision']+1)
    # Keep existing historical battles intact; current fronts use only the hubs.
    w.campaigns['history'] = []
    e = w.emit('settlements_consolidated', [], f'各处聚落合并为{len(HUBS)}处主要城镇区域，旧地名与旧年谱保留；居民与门派迁入所属城镇。',
               .9, facts=dict(initial_condition=True, mapping=w.settlements['mapping'], moved=moved,
                              old_count=len(PLACES), new_count=len(HUBS)))
    w.settlements['migration']['event_id'] = e['id']
    for n in w.npcs.values():
        if n['alive']:
            register_position(w, n, e['id'])
    w.reindex()
    added = register_population(w, HUBS, lambda hub: 48 if PLACES[hub]['name'] == '临安府' else
                                36 if PLACES[hub]['kind'] == 'capital' else 24 if hub in V1_HUBS else ADDED_TARGET)
    w.settlements['migration']['added'] = added
    # A merged jurisdiction follows its hub's current control; old control is archived above.
    territory.refresh_relations(w)
    for f in w.factions:
        f['country'] = territory.owner(w, f['place'])
    w.emit('population_settled', [], f'{len(HUBS)}处城镇安置完成，补入{len(added)}名居民；现有{sum(len(v) for v in w.residents.values())}人在世。',
           .85, facts=dict(added=len(added), initial_condition=True,
                           populations={str(h): len(w.residents[h]) for h in HUBS}))
    w.checkpoint()
    return True


def register_population(w, hubs, target):
    """Explicitly register residents until each hub reaches target(hub); returns the new NPC ids."""
    from . import items
    added = []
    counts = Counter(n['place'] for n in w.npcs.values() if n['alive'])
    for hub in hubs:
        for _ in range(max(0, target(hub)-counts[hub])):
            n = w.make_npc(place=hub)
            census = w.emit('population_registered', [n['id']],
                            f"{n['name']}以{n['age_months']//12}岁登记为{PLACES[hub]['name']}居民，以{n['job']}为生；此前人生未作模拟。",
                            .25, facts=dict(initial_condition=True, authorized_population_addition=True,
                                            place=hub, age_months=n['age_months'], wealth=n['wealth']))
            w.set_goal(n, w.best_goal(n), census['id'], initial=True)
            for template in ('cloth', 'ration'):
                item = items.create(w, n['id'], template, '新增乡籍期初行装', census['id'])
                if items.spec(item)['category'] in items.SLOTS:
                    n['equipment'][items.spec(item)['category']] = item['id']
            register_position(w, n, census['id'])
            added.append(n['id'])
    w.reindex()
    w.population_limit = max(w.population_limit, len(w.npcs)*2)
    return added


def expand(w):
    """Upgrade a version-1 consolidated world to the area-balanced region set, once.

    Existing hubs, residents, faction members and the protagonist stay where they are. A new region
    starts under whoever held the hub it used to be merged into, and gets explicitly registered
    residents; no past lives are invented for them."""
    if not w.settlements or w.settlements.get('version', 1) >= VERSION:
        return False
    from . import territory
    prior_map = {int(k): v for k, v in (w.settlements.get('mapping') or {}).items()} or V1_HUB_FOR
    old_hubs = set(w.settlements.get('hubs') or V1_HUBS)
    new_hubs = [h for h in HUBS if h not in old_hubs]
    for h in new_hubs:
        source = w.territory[prior_map[h]]
        w.territory[h].update(owner=source['owner'], stability=source['stability'],
                              changed_month=source['changed_month'], event_id=source['event_id'])
    # restore() already re-aligned factions with the new table; their v1 seats come from the old one
    seats = {f['id']: prior_map.get(f.get('native_place', f['place']), f['place']) for f in w.factions}
    w.settlements['expansions'] = w.settlements.get('expansions', []) + [
        dict(month=w.month, from_version=w.settlements.get('version', 1), to_version=VERSION,
             added_hubs=new_hubs, prior_mapping={str(k): v for k, v in prior_map.items()})]
    w.settlements.update(version=VERSION, hubs=list(HUBS), mapping={str(k): v for k, v in HUB_FOR.items()})
    align_factions(w)
    moved = [dict(faction=f['id'], origin=seats[f['id']], destination=f['place']) for f in w.factions
             if seats.get(f['id']) != f['place']]
    e = w.emit('settlements_expanded', [],
               f'按各方疆域广狭新设{len(new_hubs)}处城镇区域，共{len(HUBS)}处；原有居民、门派成员与旧年谱不变。',
               .85, facts=dict(initial_condition=True, added_hubs=new_hubs, moved_factions=moved,
                               owners={str(h): w.territory[h]['owner'] for h in new_hubs}))
    w.settlements['expansions'][-1]['event_id'] = e['id']
    added = register_population(w, new_hubs, lambda hub: ADDED_TARGET)
    w.settlements['expansions'][-1]['added'] = added
    territory.refresh_relations(w)
    for f in w.factions:
        f['country'] = territory.owner(w, f['place'])
    w.emit('population_settled', [], f'新设城镇安置完成，补入{len(added)}名居民；现有{sum(len(v) for v in w.residents.values())}人在世。',
           .85, facts=dict(added=len(added), initial_condition=True,
                           populations={str(h): len(w.residents[h]) for h in new_hubs}))
    w.checkpoint()
    return True
