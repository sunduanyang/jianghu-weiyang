"""Offline settlement interiors, isolated from the simulation's random stream.

Major layouts are authored; minor layouts use stable seed/place/version rules.
Activity locations are recorded only when an existing action really settles.
"""
from copy import deepcopy
from functools import lru_cache
import hashlib
import heapq
import json
import random

from .atlas import PLACES, FACTIONS, ROUTES
from .city_plans import PLANS
from . import territory

VERSION = 1
TERRAIN = {'.': '平地', '+': '街道', '=': '桥梁', '~': '水域', '^': '山岭',
           't': '林地', ',': '田圃', '#': '城垣'}
CATEGORIES = {
    'gate': ('出入口', '门', '接通城外驿道和航路。'),
    'government': ('官署', '府', '官吏在此处理公务，管辖随城镇控制权变化。'),
    'palace': ('宫府', '宫', '地方权力与礼仪的中心。'),
    'market': ('市集', '市', '行商与艺人往来，交易和合营在此落脚。'),
    'inn': ('客栈', '栈', '行旅歇脚、茶酒消遣与交换见闻的所在。'),
    'clinic': ('医馆', '医', '药师诊治、病者求医的地方。'),
    'workshop': ('作坊', '坊', '匠人劳作，修补器具与置办工具。'),
    'training': ('演武场', '武', '习武、授艺与切磋的场所。'),
    'school': ('书院', '书', '书生授读，乡人研习典籍。'),
    'residence': ('民居', '居', '居民休养、育儿与家人团聚的街坊。'),
    'granary': ('田仓', '仓', '农事、粮食收储与乡里互济。'),
    'garden': ('山水', '景', '园林、草场与林地，供游憩或采集。'),
    'temple': ('寺观', '寺', '香火往来，也是静思修身的清静之地。'),
    'dock': ('舟埠', '舟', '临水作业、渔人谋生与舟旅往来。'),
    'faction': ('组织', '盟', '本地门派或商帮的驻地。'),
}
THEMES = {'water': '江南水乡', 'river': '沿河城郭', 'capital': '都城坊市',
          'frontier': '边关城寨', 'port': '通商海埠', 'mountain': '山麓聚落',
          'oasis': '西域绿洲', 'highland': '高原河谷', 'steppe': '草原营地',
          'coast': '山海武家', 'island': '海岛村落', 'forest': '林海聚落', 'town': '驿道市镇'}
WORK_PLACES = {'农人': 'granary', '行商': 'market', '药师': 'clinic', '匠人': 'workshop',
               '镖师': 'gate', '渔人': 'dock', '猎户': 'garden', '书生': 'school', '艺人': 'market'}


def theme_for(p):
    if p['country'] == 2:
        return 'steppe'
    if p['country'] == 9:
        return 'forest'
    if p['country'] == 8:
        return 'island'
    if p['kind'] == 'mountain':
        return 'mountain'
    if p['kind'] == 'port':
        return 'port'
    if p['country'] in (3, 4, 7):
        return 'oasis'
    if p['country'] == 5:
        return 'highland'
    if p['country'] == 11:
        return 'mountain' if p['name'] == '松本' else 'town'
    if p['country'] == 0 and p['lat'] < 32:
        return 'water'
    return 'capital' if p['kind'] == 'capital' else 'town'


def path_between(rows, start, goal):
    """Minimum walking cost, using bridges rather than walking across water/walls."""
    size = len(rows)
    queue, costs, previous = [(0, start)], {start: 0}, {}
    while queue:
        cost, pos = heapq.heappop(queue)
        if cost != costs[pos]:
            continue
        if pos == goal:
            path = [pos]
            while pos != start:
                pos = previous[pos]
                path.append(pos)
            return list(reversed(path)), cost
        for dr, dc in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            q = pos[0]+dr, pos[1]+dc
            if not (0 <= q[0] < size and 0 <= q[1] < size) or rows[q[0]][q[1]] in '~^#':
                continue
            step = 1 if rows[q[0]][q[1]] in '+=' else 3 if rows[q[0]][q[1]] == 't' else 2
            new = cost+step
            if new < costs.get(q, float('inf')):
                costs[q], previous[q] = new, pos
                heapq.heappush(queue, (new, q))
    return [], 0


def _generated(p, rng):
    size = 9 if p['kind'] in ('city', 'capital') else 7
    middle = size//2
    theme = theme_for(p)
    rows = [['.' for _ in range(size)] for _ in range(size)]
    if theme in ('port', 'island', 'coast'):
        for r in range(size-2, size):
            for c in range(size):
                rows[r][c] = '~'
            rows[r][middle] = '='
    elif theme in ('water', 'oasis'):
        river = rng.choice([2, size-3])
        for r in range(size):
            rows[r][river] = '~'
        for r in (middle, 1):
            rows[r][river] = '='
    elif theme in ('mountain', 'highland', 'coast'):
        for r in range(2):
            for c in range(size):
                if c != middle and rng.random() < .8:
                    rows[r][c] = '^'
    for r in range(size):
        for c in range(size):
            if rows[r][c] == '.' and rng.random() < .14:
                rows[r][c] = 't' if theme in ('forest', 'mountain', 'island') else ','
    for i in range(size):
        if rows[i][middle] not in '~^':
            rows[i][middle] = '+'
        if rows[middle][i] not in '~^':
            rows[middle][i] = '+'
    return rows, theme


@lru_cache(maxsize=512, typed=True)
def _layout(seed, place, version=VERSION):
    if type(place) is not int or not 0 <= place < len(PLACES):
        raise ValueError('地点不存在')
    if type(version) is not int or version != VERSION:
        raise ValueError('内部地图版本暂不支持')
    p = PLACES[place]
    authored = PLANS.get(p['name'])
    digest = hashlib.sha256(f'city/{version}/{seed}/{place}'.encode()).digest()
    rng = random.Random(int.from_bytes(digest[:8], 'big'))
    if authored:
        rows, theme = [list(row) for row in authored['rows']], authored['theme']
    else:
        rows, theme = _generated(p, rng)
    size, middle = len(rows), len(rows)//2
    nodes, used = [], set()
    entrance = (size-1, middle)
    rows[entrance[0]][entrance[1]] = '=' if rows[entrance[0]][entrance[1]] == '~' else '+'
    reachable, pending = {entrance}, [entrance]
    while pending:
        r,c = pending.pop()
        for q in ((r-1,c),(r+1,c),(r,c-1),(r,c+1)):
            if (0 <= q[0] < size and 0 <= q[1] < size and q not in reachable
                    and rows[q[0]][q[1]] not in '~^#'):
                reachable.add(q)
                pending.append(q)

    def add(key, kind, name, hint, **extra):
        candidates = [q for q in reachable if q not in used]
        if not candidates:
            raise ValueError(f"{p['name']}没有可达的设施用地")
        pos = min(candidates, key=lambda q: (abs(q[0]-hint[0])+abs(q[1]-hint[1]), q))
        used.add(pos)
        nodes.append(dict(id=key, kind=kind, name=name, row=pos[0], col=pos[1],
                          label=CATEGORIES[kind][0], glyph=CATEGORIES[kind][1],
                          description=CATEGORIES[kind][2], **extra))

    add('gate', 'gate', '渡口入口' if rows[-1][middle] == '=' else '南向山道' if theme in
        ('mountain','highland') else '营道入口' if theme in ('steppe','forest') else '南门驿口', entrance)
    # Authored anchors win their intended coordinates before shared facilities fill gaps.
    for kind, name, r, c in authored['anchors'] if authored else []:
        add(kind, kind, name, (r,c))
    basic = [('government','地方官署',(2,middle)), ('market','街心市集',(middle,middle-1)),
             ('inn','行旅客栈',(size-3,middle+1)), ('clinic','仁济医馆',(middle-1,2)),
             ('workshop','百工作坊',(middle+1,size-3)), ('training','习武场',(2,size-3)),
             ('residence','居民街坊',(size-3,2)), ('granary','田圃义仓',(size-2,size-2))]
    if size == 9 or authored:
        basic.extend([('school','乡学书院',(2,2)), ('temple','清修寺院',(1,size-3))])
    for kind, name, hint in basic:
        if kind not in {n['id'] for n in nodes}:
            if kind == 'government' and p['country'] in (2,8,9):
                name = '乡部议事帐' if p['country'] != 8 else '聚落议事屋'
            add(kind, kind, name, hint)
    if 'garden' not in {n['id'] for n in nodes}:
        add('garden', 'garden', '郊野草场' if theme == 'steppe' else '城郊林圃', (1,1))
    if theme in ('port','island','coast','water','river') and 'dock' not in {n['id'] for n in nodes}:
        add('dock', 'dock', '水岸舟埠', (size-3,size-2))
    for f in FACTIONS:
        if f['place'] == place:
            add(f"faction_{f['id']}", 'faction', f['name'], (middle-1,middle+1), faction=f['id'])
    for n in nodes:
        route, _ = path_between(rows, entrance, (n['row'],n['col']))
        for r,c in route:
            if rows[r][c] not in '=~^#':
                rows[r][c] = '+'
    result = dict(version=version, place=place, name=p['name'], size=size, theme=theme,
                  theme_label=THEMES[theme], authored=bool(authored),
                  note=authored['note'] if authored else f"依{THEMES[theme]}的水陆、街市和营生规律布局。",
                  road_name=authored['road'] if authored else p['name']+'驿街',
                  rows=[''.join(row) for row in rows], nodes=nodes, entrance='gate')
    result['layout_id'] = hashlib.sha256(json.dumps(result,ensure_ascii=False,sort_keys=True).encode()).hexdigest()[:16]
    return result


def layout(seed, place, version=VERSION):
    return deepcopy(_layout(seed, place, version))


def venue(w, n, action):
    city = _layout(w.seed, n['place'], w.city_map_version)
    ids = {node['id'] for node in city['nodes']}
    kind = action['kind']
    if kind == 'travel':
        wanted = 'gate'
    elif kind == 'work':
        wanted = WORK_PLACES.get(n['job'], 'residence')
    elif kind in ('join','duty','train','teach','mentor','duel','faction_task','faction_study'):
        faction = f"faction_{n['faction']}"
        wanted = faction if faction in ids else 'training'
    elif kind in ('public_service','report','embezzle'):
        wanted = 'government'
    elif kind == 'treatment':
        wanted = 'clinic'
    elif kind in ('trade','buy_item','sell_item','steal','extort'):
        wanted = 'market'
    elif kind in ('repair_item','change_job','learn_trade'):
        wanted = 'workshop'
    elif kind == 'read_item':
        wanted = 'school'
    elif kind in ('leisure','rumor','gamble','smear','swear','reconcile'):
        wanted = 'inn'
    elif kind in ('meditate','play'):
        wanted = 'garden'
    else:
        wanted = 'residence'
    return wanted if wanted in ids else 'market' if wanted == 'dock' else 'residence'


def record_action(w, n, action, event):
    w.city_positions[str(n['id'])] = dict(place=n['place'], node=venue(w,n,action),
                                        event_id=event['id'], month=w.month, action=action['kind'])


def view(w, place):
    city = layout(w.seed, place, w.city_map_version)
    owner = w.countries[territory.owner(w, place)]
    city.update(country=owner['id'], country_name=owner['name'], color=owner['color'], month=w.month,
                occupied=territory.owner(w,place)!=PLACES[place]['country'],
                population=len(w.residents[place]), terrain_legend=TERRAIN)
    present = {node['id']: [] for node in city['nodes']}
    for npc_id in w.residents[place]:
        n = w.npcs[npc_id]
        pos = w.city_positions.get(str(npc_id))
        recorded = bool(pos and pos['place'] == place and pos['node'] in present)
        node = pos['node'] if recorded else 'residence'
        present[node].append(dict(id=npc_id,name=n['name'],job=n['job'],
                                  source='行动落点' if recorded else '乡籍居所',
                                  event_id=pos['event_id'] if recorded else None,
                                  month=pos['month'] if recorded else None))
    for node in city['nodes']:
        node['residents'] = present[node['id']]
        if node['kind'] == 'government':
            node['description'] = f"现由{owner['name']}管辖，公帑、任官和征税按此归属结算。"
            if city['occupied']:
                node['original_name'], node['name'] = node['name'], owner['name']+'驻署'
        route, cost = path_between(city['rows'],
                                  next((n['row'],n['col']) for n in city['nodes'] if n['id']=='gate'),
                                  (node['row'],node['col']))
        node['path'], node['walk_cost'] = route, cost
    city['connections'] = [dict(place=r['b'] if r['a']==place else r['a'],sea=r['sea'],km=r['km'])
                           for r in ROUTES if place in (r['a'],r['b'])]
    return city
