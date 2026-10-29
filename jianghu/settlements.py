"""Playable settlements: every region is one city, so each place is its own hub."""
from collections import Counter
import zlib

from .atlas import PLACES, ROUTES, NEIGHBORS

VERSION = 3
# Version 3: eight regions, one city each. Older consolidations (24 and 49 regions of the
# historical East Asian atlas) are not migrated; those saves must start a new world.
HUB_NAMES = tuple(p['name'] for p in PLACES)
HUBS = tuple(p['id'] for p in PLACES)
HUB_FOR = {p['id']: p['id'] for p in PLACES}
COMPACT_ROUTES = ROUTES
COMPACT_NEIGHBORS = NEIGHBORS


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
    city = city_maps._world_layout(w, n['place'])
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
