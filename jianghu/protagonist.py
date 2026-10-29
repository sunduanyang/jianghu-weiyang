"""Player-owned travel and monthly turns, sharing the existing player and save."""
from copy import deepcopy
import heapq
import math

from .atlas import PLACES, ROUTES
from . import city_maps, appearance

POINTS_PER_MONTH = 30
HOME = next(p['id'] for p in PLACES if p['name'] == '临安府')


def prepare_npc_turn(w):
    """Prepare independent NPC budgets and release obsolete player bookings."""
    from . import social
    h = w.player['protagonist']
    if not h['created'] or h.get('npc_budget_month') == w.month and h.get('interaction_time_policy') == 2:
        return False
    new_turn = h.get('npc_budget_month') != w.month
    for n in w.npcs.values():
        prior = n.get('monthly_actions', [])
        if new_turn:
            if prior:n['previous_month_actions'] = deepcopy(prior)
            social.reset_activity(w, n)
        else:
            booked = [a for a in prior if a.get('kind') == 'player_social' or a.get('id') == 'manual_teaching:player']
            released = sum(a.get('time_cost',1) for a in booked if a.get('month') == w.month)
            n['monthly_actions'] = [a for a in prior if a not in booked]
            n['time_left'] = min(social.activity_capacity(w,n),n['time_left']+released)
    h['npc_budget_month'] = w.month
    h['interaction_time_policy'] = 2
    return True


def initialize(w):
    from . import organizations
    if 'protagonist' in w.player:
        h = w.player['protagonist']
        organizations.prepare(h)
        h.setdefault('faction', None)
        h.setdefault('contribution', 0.)
        if h['created']:
            appearance.prepare(w, h, player=True)
        return
    gate = next(n for n in city_maps._world_layout(w, HOME)['nodes'] if n['id'] == 'gate')
    w.player['protagonist'] = dict(created=False, sex='男', age_months=216, place=HOME,
                                  row=gate['row'], col=gate['col'], points=POINTS_PER_MONTH,
                                  revision=0, started_month=None, records=[])
    initialize(w)


def on_month(w):
    h = w.player['protagonist']
    if h['created']:
        h['age_months'] += 1
        h['points'] = POINTS_PER_MONTH
        h['revision'] += 1


def route_cost(route):
    return (8 + math.ceil(route['km']/120) + (4 if route['sea'] else 0), 0)


def travel_reason(w, points):
    h = w.player['protagonist']
    return ('请先创建主角' if not h['created'] else
            '请先结束切磋，再安排行程' if w.battle and w.battle['status'] == 'active' else
            '精力不足，点击过月' if h['points'] < points else '')


def connections(w):
    h = w.player['protagonist']
    result = []
    for r in w.routes:
        if h['place'] not in (r['a'], r['b']):
            continue
        target = r['b'] if r['a'] == h['place'] else r['a']
        points, silver = route_cost(r)
        reason = travel_reason(w, points)
        result.append(dict(place=target, name=PLACES[target]['name'], km=r['km'], sea=r['sea'],
                           points=points, silver=silver, enabled=not reason, reason=reason))
    return result


def travel_plan(w, target):
    """Quote real routes and the whole legs affordable this month; travel is free."""
    target = integer(target, '目的地')
    if target not in {p['id'] for p in w.active_places}:
        raise ValueError('请选择有效区域')
    h = w.player['protagonist']
    origin = h['place']
    result = dict(origin=origin, place=target, name=PLACES[target]['name'],
                  month=w.month, revision=h['revision'], points=0, silver=0,
                  path=[origin], stops=[PLACES[origin]['name']], legs=[], enabled=False, reason='',
                  travel_legs=[], travel_points=0, travel_place=origin, can_finish=False, needs_month=False)
    if target == origin:
        result['reason'] = '已在此区域'
        return result
    graph = {}
    for route in w.routes:
        for a, b in ((route['a'], route['b']), (route['b'], route['a'])):
            graph.setdefault(a, []).append((b, route))
    costs, previous, queue = {origin: 0}, {}, [(0, origin)]
    while queue:
        points, at = heapq.heappop(queue)
        if costs[at] != points:
            continue
        if at == target:
            break
        for destination, route in graph.get(at, []):
            leg_points, leg_silver = route_cost(route)
            cost = points+leg_points
            if destination in costs and costs[destination] <= cost:
                continue
            costs[destination] = cost
            previous[destination] = (at, dict(origin=at, place=destination, name=PLACES[destination]['name'],
                                             points=leg_points, silver=leg_silver, km=route['km'], sea=route['sea']))
            heapq.heappush(queue, (cost, destination))
    if target not in costs:
        result['reason'] = '没有通往此区域的驿道或航路'
        return result
    path, legs, at = [target], [], target
    while at != origin:
        at, leg = previous[at]
        path.append(at)
        legs.append(leg)
    path.reverse()
    legs.reverse()
    affordable, remaining = [], h['points']
    for leg in legs:
        if remaining < leg['points']:
            break
        affordable.append(leg)
        remaining -= leg['points']
    reason = travel_reason(w, legs[0]['points'])
    result.update(points=costs[target], path=path, stops=[PLACES[p]['name'] for p in path],
                  legs=legs, enabled=not reason, reason=reason, travel_legs=affordable,
                  travel_points=h['points']-remaining,
                  travel_place=affordable[-1]['place'] if affordable else origin,
                  can_finish=len(affordable)==len(legs), needs_month=h['points'] < legs[0]['points'])
    return result


def view(w):
    from . import organizations
    from . import rank_progression
    from .social_ranks import player_identity
    h = deepcopy(w.player['protagonist'])
    h['appearance_view'] = appearance.view(h)
    h['rank_progression'] = rank_progression.view(w,'player')
    city = city_maps._world_layout(w, h['place'])
    node = next((n for n in city['nodes'] if (n['row'], n['col']) == (h['row'], h['col'])), None)
    area = city_maps.city_regions.area_at(city,h['row'],h['col'])
    h.update(name=w.player['name'], max_points=POINTS_PER_MONTH,
             place_name=PLACES[h['place']]['name'], node=area['id'] if area and area['kind']!='landscape' else node['id'] if node else None,
             position_name=area['name'] if area else node['name'] if node else city_maps.TERRAIN[city['rows'][h['row']][h['col']]],
             turn=w.month-h['started_month']+1 if h['created'] else 1,
             connections=connections(w), membership=organizations.player_membership(w), identity=player_identity(w),
             journal=[e for i in reversed(h['records'][-8:]) if (e := w.store.event(i))],
             visits=visits_view(w))
    return h


def visits_view(w):
    from . import visits
    return visits.view(w)


def record(w, kind, text, facts, causes=()):
    # Player records have no NPC participant; the protagonist keeps their event IDs.
    from .engine import date
    event = w.store.append(dict(month=w.month, date=date(w.month), kind=kind, people=[],
                                summary=text, importance=.7 if kind == 'hero_created' else .4,
                                causes=list(causes), facts=facts))
    h = w.player['protagonist']
    h['records'].append(event['id'])
    from . import rank_progression
    rank_progression.on_event(w,event,who='player')
    return event


def integer(value, label):
    if isinstance(value, bool) or not isinstance(value, int):
        raise ValueError(f'{label}须为整数')
    return value


def perform(w, body, defer_end_turn=None):
    h = w.player['protagonist']
    action = body.get('action')
    if w.battle and w.battle['status'] == 'active':
        raise ValueError('请先结束切磋，再安排行程')
    if (integer(body.get('revision'), '行程版本') != h['revision'] or
            integer(body.get('month'), '月份') != w.month):
        raise ValueError('行程已变化，请刷新后再操作')
    if action == 'create':
        if h['created']:
            raise ValueError('已有主角，不能重复创建或重置行程')
        name = body.get('name', '')
        if not isinstance(name, str) or not 1 <= len(name.strip()) <= 12 or any(ord(c) < 32 for c in name):
            raise ValueError('名姓须为1至12个可见字符')
        sex = body.get('sex', '男')
        age = integer(body.get('age', 18), '年龄')
        place = integer(body.get('place', HOME), '出身地点')
        if sex not in ('男', '女') or not 16 <= age <= 60 or not 0 <= place < len(PLACES):
            raise ValueError('请选择性别、16至60岁年龄和有效出身地点')
        if place not in {p['id'] for p in w.active_places}:
            raise ValueError('请选择合并后的主要城镇作为出身地点')
        gate = next(n for n in city_maps._world_layout(w, place)['nodes'] if n['id'] == 'gate')
        w.player['name'] = name.strip()
        h.update(created=True, sex=sex, age_months=age*12, place=place, row=gate['row'], col=gate['col'],
                 birth_place=place, started_month=w.month, points=POINTS_PER_MONTH)
        appearance.prepare(w, h, player=True)
        prepare_npc_turn(w)
        record(w, 'hero_created', f"{w.player['name']}，{age}岁，自{PLACES[place]['name']}踏入江湖。",
               dict(initial_condition=True, place=place, age=age, sex=sex))
    else:
        if not h['created']:
            raise ValueError('请先创建主角')
        if action in ('travel', 'travel_route'):
            target = integer(body.get('place'), '目的地')
            if action == 'travel':
                route = next((r for r in connections(w) if r['place'] == target), None)
                if route is None:
                    raise ValueError('只能沿相邻驿道或航路逐段远行')
                if not route['enabled']:
                    raise ValueError(route['reason'])
                legs = [route]
            else:
                plan = travel_plan(w, target)
                if not plan['enabled']:
                    raise ValueError(plan['reason'])
                legs = plan['travel_legs']
            gates = [next(n for n in city_maps._world_layout(w, leg['place'])['nodes'] if n['id'] == 'gate')
                     for leg in legs]
            for leg, gate in zip(legs, gates):
                origin = h['place']
                h.update(place=leg['place'], row=gate['row'], col=gate['col'], points=h['points']-leg['points'])
                record(w, 'hero_travel', f"{w.player['name']}由{PLACES[origin]['name']}"
                       f"{'乘舟' if leg['sea'] else '沿驿道'}抵达{leg['name']}，消耗{leg['points']}行动力。",
                       dict(leg, origin=origin, destination=leg['place'], target=target, path=[origin, leg['place']]))
        elif action == 'walk':
            place = integer(body.get('place'), '地点')
            row, col = integer(body.get('row'), '行坐标'), integer(body.get('col'), '列坐标')
            if place != h['place']:
                raise ValueError('主角不在此处，请先沿大地图前往')
            city = city_maps._world_layout(w, place)
            if not (0 <= row < city['size'] and 0 <= col < city['size']) or city['rows'][row][col] in '~^#':
                raise ValueError('水域、山岭、城垣或地图外不可通行')
            if (row, col) == (h['row'], h['col']):
                raise ValueError('已经在此处')
            path, cost = city_maps.path_between(city['rows'], (h['row'], h['col']), (row, col))
            if not path:
                raise ValueError('没有可通行的路线')
            if cost > h['points']:
                raise ValueError(f'此行需要{cost}行动力，本月余量不足')
            origin = [h['row'], h['col']]
            h.update(row=row, col=col, points=h['points']-cost)
            node = next((n for n in city['nodes'] if (n['row'], n['col']) == (row, col)), None)
            record(w, 'hero_walk', f"{w.player['name']}在{city['name']}移步至"
                   f"{node['name'] if node else '地格'}（{row+1}，{col+1}），消耗{cost}行动力。",
                   dict(place=place, origin=origin, destination=[row, col], path=path, points=cost))
        elif isinstance(action, str) and action.startswith('sect_'):
            from . import organizations
            organizations.player_action(w, action, integer(body.get('faction'), '门派'), body.get('choice'))
        elif action == 'end_turn':
            if defer_end_turn is not None:return defer_end_turn()
            record(w, 'hero_end_turn', f"{w.player['name']}结束本月行程，江湖推移一月。",
                   dict(unused_points=h['points'], next_month=w.month+1))
            # Player interactions never spend NPC time; NPCs then run their
            # independent schedule before control returns to the player.
            w.month_step()
            w.checkpoint()
            return view(w)
        else:
            raise ValueError('未知主角指令')
    h['revision'] += 1
    prepared = prepare_npc_turn(w)
    if action == 'walk' and not prepared:
        w.store.save_player(w.player, w.month)
    else:
        w.checkpoint()
    return view(w)
