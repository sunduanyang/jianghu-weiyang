"""Persistent settlement homes, workplaces and weekly physical encounters.

Registration is a present-day initial condition. It consumes no world RNG,
changes no family history and does not create people, goods or silver.
"""
from collections import Counter
from copy import deepcopy
from functools import lru_cache
import heapq
import zlib

from . import city_maps

VERSION = 1
PEER_ACTIONS = {'befriend', 'court', 'propose', 'aid', 'trade', 'betray', 'duel',
                'mentor', 'teach', 'treatment', 'care', 'neighbor_care',
                'reconcile', 'swear', 'rumor', 'learn_trade', 'seek_teacher'}
SCHEDULED = {'befriend', 'court', 'propose', 'mentor', 'teach', 'treatment', 'rumor', 'care', 'seek_teacher'}
ROLES = {'city': '交易与公共服务', 'town': '加工与乡里集散',
         'village': '农事与原料供给', 'sect': '门中起居与传艺'}


def active(w):
    return bool(getattr(w, 'local_life', None))


def places(w, place):
    city = city_maps._world_layout(w, place)
    return {t['id']: t for t in city.get('settlements', [])}


def entry(w, n):
    return (getattr(w, 'local_life', None) or {}).get('people', {}).get(str(n['id']))


def stable(*parts):
    return zlib.crc32('/'.join(map(str, parts)).encode('utf-8'))


def worksite(w, n):
    city = city_maps._world_layout(w, n['place'])
    kind = 'government' if n.get('office') else city_maps.WORK_PLACES.get(n['job'], 'workshop')
    node = next((v for v in city['nodes'] if v['id'] == kind), None)
    node = node or next(v for v in city['nodes'] if v['id'] == 'residence')
    return dict(place=n['place'], settlement_id=node['settlement_id'], node=node['id'])


def choose_home(w, members):
    n = max(members, key=lambda q: (q['age_months'] >= 192, q['age_months'], -q['id']))
    sites = places(w, n['place'])
    adults = [q for q in members if q['age_months'] >= 192] or members
    sects = {q['faction'] for q in adults}
    sect = f"sect_{w.factions[n['faction']]['key']}" if len(sects) == 1 and n['faction'] is not None else None
    # A mixed household keeps a civilian home; sect membership alone does not move a family.
    if sect in sites:
        return sect
    job = n['career']['profession']
    weights = {'city': 5, 'town': 3, 'village': 2}
    if job in ('农人', '渔人', '猎户', '短工'):
        weights = {'city': 1, 'town': 3, 'village': 7}
    elif job == '匠人':
        weights = {'city': 3, 'town': 7, 'village': 1}
    weighted = [t['id'] for t in sites.values() if t['kind'] != 'sect'
                for _ in range(weights[t['kind']])]
    return weighted[stable(w.seed, min(q['id'] for q in members), 'home') % len(weighted)]


def register(w, n, home=None, reason='现时户居登记', event_id=None):
    home = home or choose_home(w, [n])
    row = dict(home=dict(place=n['place'], settlement_id=home, since=w.month,
                         reason=reason, event_id=event_id), work=worksite(w, n),
               spouse=n.get('spouse'), guardian=n.get('guardian'))
    if n['age_months'] < 120:
        row['work'] = dict(place=n['place'], settlement_id=home, node='residence')
    w.local_life['people'][str(n['id'])] = row
    return row


def initialize(w):
    if active(w):
        return False
    w.local_life = dict(version=VERSION, since=w.month, people={}, production={},
                        production_month=w.month)
    living = {i: n for i, n in w.npcs.items() if n['alive']}
    parent = {i: i for i in living}
    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]; i = parent[i]
        return i
    def join(a, b):
        if b in living and living[a]['place'] == living[b]['place']:
            x, y = root(a), root(b); parent[max(x, y)] = min(x, y)
    for i, n in living.items():
        join(i, n.get('spouse'))
        if n['age_months'] < 192:
            guardian = n.get('guardian') or next((p for p in n['parents'] if p in living), None)
            join(i, guardian)
    groups = {}
    for i, n in living.items():
        groups.setdefault(root(i), []).append(n)
    for group in groups.values():
        home = choose_home(w, group)
        for n in group:
            register(w, n, home)
    return True


def refresh(w, n, event_id=None):
    """Called by simulation transitions, never by a public view."""
    if not active(w) or not n['alive']:
        return None
    row = entry(w, n)
    if row is None or row['home']['place'] != n['place'] or row['home']['settlement_id'] not in places(w, n['place']):
        guardian = w.npcs.get(n.get('guardian')) if n['age_months'] < 192 else None
        g = entry(w, guardian) if guardian and guardian['alive'] and guardian['place'] == n['place'] else None
        if g and g['home']['place'] != n['place']:
            g = refresh(w, guardian, event_id)
        spouse = w.npcs.get(n.get('spouse'))
        partner = entry(w, spouse) if spouse and spouse['alive'] and spouse['place'] == n['place'] else None
        shared = g or (partner if partner and partner['home']['place'] == n['place'] else None)
        row = register(w, n, shared['home']['settlement_id'] if shared else None,
                       '随监护人安家' if g else '迁居登记' if row else '新户登记', event_id)
    if n['age_months'] < 192:
        g = w.npcs.get(n.get('guardian'))
        other = entry(w, g) if g and g['alive'] and g['place'] == n['place'] else None
        if other and row['home']['settlement_id'] != other['home']['settlement_id']:
            row['home'] = dict(place=n['place'], settlement_id=other['home']['settlement_id'],
                               since=w.month, reason='随监护人同住', event_id=event_id)
    spouse = w.npcs.get(n.get('spouse'))
    if row['spouse'] != n.get('spouse') and spouse and spouse['alive'] and spouse['place'] == n['place']:
        other = entry(w, spouse)
        anchor = row if n['id'] < spouse['id'] or not other else other
        home = dict(place=n['place'], settlement_id=anchor['home']['settlement_id'],
                    since=w.month, reason='婚后合户', event_id=event_id)
        row['home'] = home
        if other:
            other['home'] = deepcopy(home); other['spouse'] = n['id']
    row.update(work=worksite(w, n) if n['age_months'] >= 120 else dict(place=n['place'], settlement_id=row['home']['settlement_id'], node='residence'),
               spouse=n.get('spouse'), guardian=n.get('guardian'))
    return row


def reconcile(w):
    for n in w.npcs.values():
        refresh(w, n)
    if active(w) and w.local_life['production_month'] != w.month:
        w.local_life.update(production_month=w.month, production={})


def point(w, n, site_id, node='residence', moving=False):
    site = places(w, n['place'])[site_id]; lot = site['footprint']; size = lot['size']
    # A saved real cell, rather than broadcasting a resident over an entire building.
    index = stable(w.seed, n['id'], site_id, w.month if moving else 'home', w.phase if moving else 0) % (size * size)
    return dict(place=n['place'], settlement_id=site_id, node=node,
                row=lot['row'] + index // size, col=lot['col'] + index % size)


def current(w, n):
    pos = w.city_positions.get(str(n['id']))
    city = city_maps._world_layout(w, n['place']); nodes = {v['id']: v for v in city['nodes']}
    if pos and pos['place'] == n['place'] and pos['node'] in nodes:
        node = nodes[pos['node']]
        site_id = pos.get('settlement_id', node['settlement_id'])
        if site_id not in places(w, n['place']): site_id = node['settlement_id']
        r, c = pos.get('row', node['row']), pos.get('col', node['col'])
        return dict(place=n['place'], settlement_id=site_id, node=pos['node'], row=r, col=c)
    row = entry(w, n)
    if row and row['home']['place'] == n['place']:
        return point(w, n, row['home']['settlement_id'])
    node = nodes['residence']
    return dict(place=n['place'], settlement_id=node['settlement_id'], node='residence', row=node['row'], col=node['col'])


@lru_cache(maxsize=1024)
def _walking_costs(seed, place, version, compact, tier, a):
    city = city_maps._layout(seed, place, version, compact, tier)
    rows=city['rows']; size=len(rows); costs={a:0}; queue=[(0,a)]
    while queue:
        cost,p=heapq.heappop(queue)
        if cost != costs[p]:continue
        for dr,dc in ((-1,0),(1,0),(0,-1),(0,1)):
            b=p[0]+dr,p[1]+dc
            if not (0<=b[0]<size and 0<=b[1]<size) or rows[b[0]][b[1]] in '~^#':continue
            value=cost+(1 if rows[b[0]][b[1]] in '+=' else 3 if rows[b[0]][b[1]]=='t' else 2)
            if value<costs.get(b,float('inf')):
                costs[b]=value;heapq.heappush(queue,(value,b))
    return costs


def distance(w, place, a, b):
    tier = w.city_settlement_profiles.get(str(place), {}).get('tier', 'general')
    return _walking_costs(w.seed, place, w.city_map_version, bool(w.settlements), tier, a).get(b,0)


def meeting(w, n, a):
    if not active(w) or a['kind'] not in PEER_ACTIONS:
        return None
    q = w.npcs.get(a.get('target'))
    if not q or not q['alive'] or q['place'] != n['place']:
        return None
    location = current(w, q)
    if a['kind'] in ('trade', 'treatment', 'learn_trade', 'mentor'):
        kind = {'trade': 'market', 'treatment': 'clinic'}.get(a['kind'])
        row = entry(w, q)
        if kind:
            node = next(v for v in city_maps._world_layout(w, n['place'])['nodes'] if v['id'] == kind)
            location = dict(place=n['place'], settlement_id=node['settlement_id'], node=node['id'], row=node['row'], col=node['col'])
        elif row:
            location = point(w, q, row['work']['settlement_id'], row['work']['node'])
    origin = current(w, n)
    return dict(location=location, participants=[n['id'], q['id']],
                walk_cost=distance(w, n['place'], (origin['row'], origin['col']), (location['row'], location['col'])),
                travel_included=True)


def record_action(w, n, a, event):
    row = refresh(w, n, event['id'])
    if not row: return
    visit = event.get('facts', {}).get('local_visit')
    if visit and n['id'] in visit['participants']:
        location = deepcopy(visit['location'])
    elif a['kind'] == 'work':
        location = point(w, n, row['work']['settlement_id'], row['work']['node'], moving=True)
    elif a['kind'] in ('rest', 'family', 'use_item', 'equip_item'):
        location = point(w, n, row['home']['settlement_id'])
    else:
        node_id = city_maps.venue(w, n, a)
        node = next(v for v in city_maps._world_layout(w, n['place'])['nodes'] if v['id'] == node_id)
        location = point(w, n, node['settlement_id'], node_id, moving=True) if node_id != 'gate' else dict(
            place=n['place'], settlement_id=node['settlement_id'], node=node_id, row=node['row'], col=node['col'])
    w.city_positions[str(n['id'])] = dict(**location, event_id=event['id'], month=w.month,
                                          week=w.phase, action=a['kind'], source='行动落点')
    if visit:
        for i in visit['participants']:
            q = w.npcs.get(i)
            if i != n['id'] and i in event['people'] and q and q['alive'] and q['place'] == n['place']:
                w.city_positions[str(i)] = dict(**visit['location'], event_id=event['id'], month=w.month,
                                                week=w.phase, action=a['kind'], source='共同活动')


def proximity(w, n, q):
    a, b = entry(w, n), entry(w, q)
    if not a or not b: return 0
    return 2 * (a['home']['settlement_id'] == b['home']['settlement_id']) + (a['work']['settlement_id'] == b['work']['settlement_id'])


def strangers(w, n, ids):
    if not ids or not active(w): return ids
    # One neighbour and one rotating regional acquaintance keep small villages connected.
    offset = (w.month + n['id']) % len(ids); rotated = (ids[offset:] + ids[:offset])
    nearest = max(rotated, key=lambda i: proximity(w, n, w.npcs[i]))
    return [nearest] + [i for i in rotated if i != nearest]


def adjustment(w, n, a):
    if not active(w) or a['kind'] not in PEER_ACTIONS: return 0.
    q = w.npcs.get(a.get('target'))
    if not q or q['place'] != n['place']: return 0.
    x, y = current(w, n), current(w, q)
    cost = distance(w, n['place'], (x['row'], x['col']), (y['row'], y['col']))
    return proximity(w, n, q) * 2 - min(6., cost * .12)


def partner_available(w, a):
    if not active(w) or a['kind'] not in SCHEDULED: return True
    from . import social
    q = w.npcs.get(a.get('target'))
    return bool(q and q['alive'] and q['time_left'] >= 1 and social.available(w, q))


def reserve_partner(w, n, a, event):
    if not active(w) or a['kind'] not in SCHEDULED: return
    q = w.npcs.get(a.get('target'))
    if q and q['id'] in event['people'] and not any(x['event_id'] == event['id'] for x in q['monthly_actions']):
        from . import relationships
        relationships.reserve_partner(w, q, a, event, '共同活动')


def production(w, n, result):
    if not active(w): return
    refresh(w, n)
    site = entry(w, n)['work']['settlement_id']; key = f"{n['place']}/{site}"
    row = w.local_life['production'].setdefault(key, dict(place=n['place'], settlement_id=site,
                 actions=0, workers=[], produced={}, inputs={}, sold={}, freight=0., delivered={}))
    row['actions'] += 1
    if n['id'] not in row['workers']: row['workers'].append(n['id'])
    for category in ('produced', 'inputs', 'sold'):
        for good, amount in result.get(category, {}).items():
            row[category][good] = round(row[category].get(good, 0.) + amount, 4)
    result['workplace'] = deepcopy(entry(w, n)['work'])
    row['freight'] = round(row.get('freight', 0.) + result.get('local_delivery', {}).get('paid', 0.), 4)
    for g, amount in result.get('local_delivery', {}).get('goods', {}).items():
        row['delivered'][g] = round(row['delivered'].get(g, 0.) + amount, 4)


def deliver(w, n, sold, sales):
    """The existing regional market collects a funded fee for actual deliveries.

    The stock remains one central market ledger. Work includes collection time;
    this does not invent a second inventory or an unmodelled carrier's wages.
    """
    from . import economics
    if not active(w): return None
    refresh(w, n)
    source = entry(w, n)['work']; sites = places(w, n['place'])
    origin = sites[source['settlement_id']]
    node = next(v for v in city_maps._world_layout(w, n['place'])['nodes'] if v['id'] == 'market')
    goods = {g: a for g, a in sold.items() if economics.GOODS[g][2] != 1 and a > 0}
    cost = distance(w, n['place'], (origin['row'], origin['col']), (node['row'], node['col']))
    charge = round(min(sales * .03, sum(goods.values()) * cost * .002), 4) if source['settlement_id'] != node['settlement_id'] else 0.
    paid = round(min(charge, max(0., n['wealth'])), 4)
    n['wealth'] -= paid; economics.market(w, n['place'])['cash'] += paid
    economics.prepare(n)['expenses'] += paid
    return dict(origin=source['settlement_id'], destination=node['settlement_id'],
                goods=goods, walk_cost=cost, paid=paid, collector='地区市集收运账户', included_in_work=True)


def named(w, loc):
    if not loc: return None
    site = places(w, loc['place']).get(loc['settlement_id'])
    node = next((v for v in city_maps._world_layout(w, loc['place'])['nodes'] if v['id']==loc.get('node')), None)
    return dict(**deepcopy(loc), name=site['name'] if site else '旧居', place_name=w.place_name(loc['place']),
                facility=node['name'] if node else None)


def view(w, n):
    row = entry(w, n)
    if not row: return dict(enabled=False)
    position = current(w, n)
    home = row['home']; work = row['work']
    same_place = home['place'] == work['place']
    sites = places(w, work['place']); a = places(w, home['place'])[home['settlement_id']]; b = sites[work['settlement_id']]
    return dict(enabled=True, working=n['age_months'] >= 120, since=w.local_life['since'], home=named(w, home), work=named(w, work),
                current=named(w, position), commute_cost=distance(w, home['place'], (a['row'], a['col']), (b['row'], b['col'])) if same_place else None,
                basis='住所固定登记；每周按实际行动更新在场位置。通勤与拜访包含在该次行动时间内。')


def annotate(w, city):
    if not active(w): return
    towns = {t['id']: t for t in city.get('settlements', [])}
    for t in towns.values():
        t.update(home_residents=[], workers=[], role=ROLES[t['kind']])
    for n in w.npcs.values():
        if not n['alive']: continue
        row = entry(w, n)
        if not row: continue
        person = dict(id=n['id'], name=n['name'], job=n['job'])
        for field, category in (('home', 'home_residents'), ('work', 'workers')):
            loc = row[field]
            if loc['place'] == city['place'] and loc['settlement_id'] in towns:
                if field != 'work' or n['age_months'] >= 120:
                    towns[loc['settlement_id']][category].append(person.copy())
    for t in towns.values():
        t['production'] = deepcopy(w.local_life['production'].get(f"{city['place']}/{t['id']}", {}))
        t['professions'] = dict(Counter(p['job'] for p in t['workers']))
