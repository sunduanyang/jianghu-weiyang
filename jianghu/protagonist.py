"""Player-owned travel and monthly turns, sharing the existing player and save."""
from copy import deepcopy
import math

from .atlas import PLACES, ROUTES
from . import city_maps

POINTS_PER_MONTH = 30
HOME = next(p['id'] for p in PLACES if p['name'] == '临安府')


def initialize(w):
    if 'protagonist' in w.player:
        return
    gate = next(n for n in city_maps.world_layout(w, HOME)['nodes'] if n['id'] == 'gate')
    w.player['protagonist'] = dict(created=False, sex='男', age_months=216, place=HOME,
                                  row=gate['row'], col=gate['col'], points=POINTS_PER_MONTH,
                                  revision=0, started_month=None, records=[])


def on_month(w):
    h = w.player['protagonist']
    if h['created']:
        h['age_months'] += 1
        h['points'] = POINTS_PER_MONTH
        h['revision'] += 1


def connections(w):
    h = w.player['protagonist']
    city = city_maps.world_layout(w, h['place'])
    at_exit = any(n['kind'] in ('gate', 'dock') and (n['row'], n['col']) == (h['row'], h['col'])
                  for n in city['nodes'])
    result = []
    for r in w.routes:
        if h['place'] not in (r['a'], r['b']):
            continue
        target = r['b'] if r['a'] == h['place'] else r['a']
        points = 8 + math.ceil(r['km']/120) + (4 if r['sea'] else 0)
        silver = round(1 + r['km']/250 + (1.5 if r['sea'] else 0), 2)
        reason = ('请先创建主角' if not h['created'] else
                  '请先走到城门或舟埠' if not at_exit else
                  '本月行动力不足，请结束本月' if h['points'] < points else
                  '盘缠不足' if w.player['wealth'] < silver else '')
        result.append(dict(place=target, name=PLACES[target]['name'], km=r['km'], sea=r['sea'],
                           points=points, silver=silver, enabled=not reason, reason=reason))
    return result


def view(w):
    h = deepcopy(w.player['protagonist'])
    city = city_maps.world_layout(w, h['place'])
    node = next((n for n in city['nodes'] if (n['row'], n['col']) == (h['row'], h['col'])), None)
    h.update(name=w.player['name'], max_points=POINTS_PER_MONTH,
             place_name=PLACES[h['place']]['name'], node=node['id'] if node else None,
             position_name=node['name'] if node else city_maps.TERRAIN[city['rows'][h['row']][h['col']]],
             turn=w.month-h['started_month']+1 if h['created'] else 1,
             connections=connections(w),
             journal=[e for i in reversed(h['records'][-8:]) if (e := w.store.event(i))])
    return h


def record(w, kind, text, facts):
    # Player records have no NPC participant; the protagonist keeps their event IDs.
    from .engine import date
    event = w.store.append(dict(month=w.month, date=date(w.month), kind=kind, people=[],
                                summary=text, importance=.7 if kind == 'hero_created' else .4,
                                causes=[], facts=facts))
    h = w.player['protagonist']
    h['records'].append(event['id'])
    return event


def integer(value, label):
    if isinstance(value, bool) or not isinstance(value, int):
        raise ValueError(f'{label}须为整数')
    return value


def perform(w, body):
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
        gate = next(n for n in city_maps.world_layout(w, place)['nodes'] if n['id'] == 'gate')
        w.player['name'] = name.strip()
        h.update(created=True, sex=sex, age_months=age*12, place=place, row=gate['row'], col=gate['col'],
                 started_month=w.month, points=POINTS_PER_MONTH)
        record(w, 'hero_created', f"{w.player['name']}，{age}岁，自{PLACES[place]['name']}踏入江湖。",
               dict(initial_condition=True, place=place, age=age, sex=sex))
    else:
        if not h['created']:
            raise ValueError('请先创建主角')
        if action == 'travel':
            target = integer(body.get('place'), '目的地')
            route = next((r for r in connections(w) if r['place'] == target), None)
            if route is None:
                raise ValueError('只能沿相邻驿道或航路逐段远行')
            if not route['enabled']:
                raise ValueError(route['reason'])
            gate = next(n for n in city_maps.world_layout(w, target)['nodes'] if n['id'] == 'gate')
            origin = h['place']
            w.player['wealth'] = round(w.player['wealth']-route['silver'], 4)
            h.update(place=target, row=gate['row'], col=gate['col'], points=h['points']-route['points'])
            record(w, 'hero_travel', f"{w.player['name']}由{PLACES[origin]['name']}"
                   f"{'乘舟' if route['sea'] else '沿驿道'}抵达{route['name']}，"
                   f"消耗{route['points']}行动力、{route['silver']}银。",
                   dict(origin=origin, destination=target, **route))
        elif action == 'walk':
            place = integer(body.get('place'), '地点')
            row, col = integer(body.get('row'), '行坐标'), integer(body.get('col'), '列坐标')
            if place != h['place']:
                raise ValueError('主角不在此处，请先沿大地图前往')
            city = city_maps.world_layout(w, place)
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
        elif action == 'end_turn':
            record(w, 'hero_end_turn', f"{w.player['name']}结束本月行程，江湖推移一月。",
                   dict(unused_points=h['points'], next_month=w.month+1))
            # The existing monthly simulation also refreshes the protagonist exactly once.
            w.month_step()
            w.checkpoint()
            return view(w)
        else:
            raise ValueError('未知主角指令')
    h['revision'] += 1
    w.checkpoint()
    return view(w)
