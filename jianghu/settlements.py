"""Consolidated playable settlements; historical IDs remain valid in old journals."""
from collections import Counter
from copy import deepcopy

from .atlas import PLACES, ROUTES, distance

VERSION = 1
HUB_NAMES = ('临安府', '建康府', '抚州', '江陵府', '襄阳府', '成都府', '广州', '泉州',
             '中都', '南京开封府', '京兆府', '上京会宁府', '克鲁伦河营地', '兴庆府', '沙州',
             '虎思斡耳朵', '逻些', '大理城', '高昌城', '海东聚落', '黑水营地', '开京', '镰仓', '平安京')
HUBS = tuple(next(p['id'] for p in PLACES if p['name'] == name) for name in HUB_NAMES)
HUB_FOR = {p['id']: min((h for h in HUBS if PLACES[h]['country'] == p['country']),
                        key=lambda h: (distance(p['id'], h), h)) for p in PLACES}
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
    wanted = ('government' if n['office'] else 'residence' if n['age_months'] < 120 else
              'inn' if n['id'] % 7 == 0 else 'gate' if n['id'] % 11 == 0 else
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
    e = w.emit('settlements_consolidated', [], '各处聚落合并为24处主要城镇区域，旧地名与旧年谱保留；居民与门派迁入所属城镇。',
               .9, facts=dict(initial_condition=True, mapping=w.settlements['mapping'], moved=moved,
                              old_count=len(PLACES), new_count=len(HUBS)))
    w.settlements['migration']['event_id'] = e['id']
    for n in w.npcs.values():
        if n['alive']:
            register_position(w, n, e['id'])
    w.reindex()
    added = []
    counts = Counter(n['place'] for n in w.npcs.values() if n['alive'])
    for hub in HUBS:
        target = 48 if PLACES[hub]['name'] == '临安府' else 36 if PLACES[hub]['kind'] == 'capital' else 24
        for _ in range(max(0, target-counts[hub])):
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
    w.settlements['migration']['added'] = added
    # A merged jurisdiction follows its hub's current control; old control is archived above.
    territory.refresh_relations(w)
    for f in w.factions:
        f['country'] = territory.owner(w, f['place'])
    w.emit('population_settled', [], f'24处城镇安置完成，补入{len(added)}名居民；现有{sum(len(v) for v in w.residents.values())}人在世。',
           .85, facts=dict(added=len(added), initial_condition=True,
                           populations={str(h): len(w.residents[h]) for h in HUBS}))
    w.checkpoint()
    return True
