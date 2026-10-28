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
from . import city_biomes
from . import city_landscape
from . import city_regions
from . import city_gardens

VERSION = 8
TERRAIN = {'.': '平地', '+': '街道', '=': '桥梁', 's':'浅滩', '~': '水域', '^': '山岭',
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
    'faction': ('门派驻地', '宗', '独立九格山门、寺院、武帐或祭场，可探访同门、捐助与辞门；入门须与该派三品及以上人物互动。'),
}
THEMES = {'water': '江南水乡', 'river': '沿河城郭', 'capital': '都城坊市',
          'frontier': '边关城寨', 'port': '通商海埠', 'mountain': '山麓聚落',
          'oasis': '西域绿洲', 'highland': '高原河谷', 'steppe': '草原营地',
          'coast': '山海武家', 'island': '海岛村落', 'forest': '林海聚落', 'town': '驿道市镇'}
WORK_PLACES = {'农人': 'granary', '行商': 'market', '药师': 'clinic', '匠人': 'workshop',
               '镖师': 'gate', '渔人': 'dock', '猎户': 'garden', '书生': 'school', '艺人': 'market'}


def _building_footprints(rows, nodes):
    """Reserve whole 1x1 or 2x2 visual lots without moving saved entrances.

    The entrance remains the existing integer node coordinate. A large lot may
    extend into vacant, passable neighbours, never another facility or a barrier.
    Lot allocation is presentation metadata and does not change movement costs.
    """
    priorities = {'palace': 0, 'government': 1, 'faction': 2, 'temple': 3, 'school': 4}
    used = {(n['row'], n['col']) for n in nodes}
    size = len(rows)
    for n in sorted(nodes, key=lambda n: (priorities.get(n['kind'], 9), n['id'])):
        entrance = n['row'], n['col']
        candidates = []
        if n['kind'] in priorities:
            for row in (n['row'] - 1, n['row']):
                for col in (n['col'] - 1, n['col']):
                    cells = {(row + a, col + b) for a in (0, 1) for b in (0, 1)}
                    extra = cells - {entrance}
                    if extra & used or any(not (0 <= r < size and 0 <= c < size)
                                           or rows[r][c] in '~^#=' for r, c in cells):
                        continue
                    candidates.append((sum(rows[r][c] == '+' for r, c in extra), row, col, cells))
        if candidates:
            _, row, col, cells = min(candidates, key=lambda x: x[:3])
            used.update(cells)
            n['footprint'] = dict(row=row, col=col, size=2)
        else:
            n['footprint'] = dict(row=n['row'], col=n['col'], size=1)


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


def scaled_coordinate(value, old_size, new_size):
    return round(value * (new_size-1) / (old_size-1))


def _rebuild(rows, size, rng):
    """Reconstruct geography at the new scale and lay narrow connected streets."""
    old_size = len(rows)
    source = rows
    rows = [[source[min(old_size-1, r*old_size//size)][min(old_size-1, c*old_size//size)]
             for c in range(size)] for r in range(size)]
    # Streets are rebuilt as individual lanes, rather than blown up into blocks.
    for r in range(size):
        for c in range(size):
            if rows[r][c] in '+=':
                rows[r][c] = '~' if rows[r][c] == '=' else '.'
            if rows[r][c] == '.' and rng.random() < .12:
                rows[r][c] = 't' if rng.random() < .45 else ','
    lanes = sorted({size//2, size//4, size*3//4})
    for lane in lanes:
        for i in range(size):
            for r, c in ((lane, i), (i, lane)):
                # Main streets have engineered passes and bridges through barriers.
                rows[r][c] = '=' if rows[r][c] == '~' else '+'
    # Preserve the city's named streets and engineered crossings as narrow
    # lanes at their projected coordinates, including dock anchors on bridges.
    for r in range(old_size):
        for c in range(old_size):
            if source[r][c] not in '+=':
                continue
            rr, cc = scaled_coordinate(r,old_size,size), scaled_coordinate(c,old_size,size)
            rows[rr][cc] = '=' if source[r][c] == '=' or rows[rr][cc] == '~' else '+'
            for dr, dc in ((1,0),(0,1)):
                if r+dr>=old_size or c+dc>=old_size or source[r+dr][c+dc] not in '+=':
                    continue
                end_r = scaled_coordinate(r+dr,old_size,size)
                end_c = scaled_coordinate(c+dc,old_size,size)
                for a in range(rr,end_r+1):
                    for b in range(cc,end_c+1):
                        rows[a][b] = '=' if rows[a][b] == '~' or source[r][c] == '=' and source[r+dr][c+dc] == '=' else '+'
    return rows


@lru_cache(maxsize=512, typed=True)
def _layout(seed, place, version=VERSION, compact=False, settlement_tier='general'):
    if type(place) is not int or not 0 <= place < len(PLACES):
        raise ValueError('地点不存在')
    if type(version) is not int or version not in (1, 2, 3, 4, 5, 6, 7, VERSION):
        raise ValueError('内部地图版本暂不支持')
    if version>=8:
        city=deepcopy(_layout(seed,place,7,compact))
        city_regions.consolidate(city,settlement_tier)
        city['version']=version
        city.pop('layout_id',None)
        city['layout_id']=hashlib.sha256(json.dumps(city,ensure_ascii=False,sort_keys=True).encode()).hexdigest()[:16]
        return city
    p = PLACES[place]
    authored = PLANS.get(p['name'])
    generation_version=6 if version>=7 else version
    digest = hashlib.sha256(f'city/{generation_version}/{seed}/{place}'.encode()).digest()
    rng = random.Random(int.from_bytes(digest[:8], 'big'))
    if authored:
        rows, theme = [list(row) for row in authored['rows']], authored['theme']
    else:
        rows, theme = _generated(p, rng)
    old_size = len(rows)
    if version >= 2:
        new_size, tier, tier_label = city_biomes.size_for(p,version)
        if authored and version==2:
            rng = random.Random(int.from_bytes(hashlib.sha256(f'authored/{place}'.encode()).digest()[:8], 'big'))
        rows = (_rebuild(rows,new_size,rng) if version==2 else
                (city_gardens.landscape if version>=5 else city_landscape.landscape)(rows,new_size,theme,rng))
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
        candidates = ([q for q in ((r,c) for r in range(size) for c in range(size))
                       if q not in used and rows[q[0]][q[1]] not in '~^#']
                      if version>=3 else [q for q in reachable if q not in used])
        if not candidates:
            raise ValueError(f"{p['name']}没有可达的设施用地")
        pos = min(candidates, key=lambda q: (abs(q[0]-hint[0])+abs(q[1]-hint[1]), q))
        used.add(pos)
        nodes.append(dict(id=key, kind=kind, name=name, row=pos[0], col=pos[1],
                          label=CATEGORIES[kind][0], glyph=CATEGORIES[kind][1],
                          description=extra.pop('description', CATEGORIES[kind][2]), **extra))

    add('gate', 'gate', '渡口入口' if rows[-1][middle] == '=' else '南向山道' if theme in
        ('mountain','highland') else '营道入口' if theme in ('steppe','forest') else '南门驿口', entrance)
    # Authored anchors win their intended coordinates before shared facilities fill gaps.
    for kind, name, r, c in authored['anchors'] if authored else []:
        if version >= 2:
            r, c = scaled_coordinate(r, old_size, size), scaled_coordinate(c, old_size, size)
        add(kind, kind, name, (r,c))
    basic = [('government','地方官署',(2,middle)), ('market','街心市集',(middle,middle-1)),
             ('inn','行旅客栈',(size-3,middle+1)), ('clinic','仁济医馆',(middle-1,2)),
             ('workshop','百工作坊',(middle+1,size-3)), ('training','习武场',(2,size-3)),
             ('residence','居民街坊',(size-3,2)), ('granary','田圃义仓',(size-2,size-2))]
    if size >= 9 or authored:
        basic.extend([('school','乡学书院',(2,2)), ('temple','清修寺院',(1,size-3))])
    if version >= 2:
        # Distribute shared facilities throughout the three districts.
        hints = {'government':(.22,.50), 'market':(.50,.42), 'inn':(.78,.58),
                 'clinic':(.42,.22), 'workshop':(.62,.78), 'training':(.22,.78),
                 'residence':(.78,.22), 'granary':(.85,.85),
                 'school':(.22,.22), 'temple':(.12,.75)}
        basic = [(kind, name, tuple(round(v*(size-1)) for v in hints[kind]))
                 for kind, name, _ in basic]
        if version>=3:
            basic = [(kind,name,tuple(max(1,min(size-2,v+rng.randint(-max(1,size//10),max(1,size//10)))) for v in hint))
                     for kind,name,hint in basic]
    for kind, name, hint in basic:
        if kind not in {n['id'] for n in nodes}:
            if kind == 'government' and p['country'] in (2,8,9):
                name = '乡部议事帐' if p['country'] != 8 else '聚落议事屋'
            add(kind, kind, name, hint)
    if 'garden' not in {n['id'] for n in nodes}:
        add('garden', 'garden', '郊野草场' if theme == 'steppe' else '城郊林圃', (1,1))
    if theme in ('port','island','coast','water','river') and 'dock' not in {n['id'] for n in nodes}:
        add('dock', 'dock', '水岸舟埠', (size-3,size-2))
    from .settlements import HUB_FOR
    for f in FACTIONS:
        if (HUB_FOR[f['place']] if compact else f['place']) == place:
            hint = (rng.randint(size//4,size*3//4),rng.randint(size//4,size*3//4)) if version>=3 else (middle-1,middle+1)
            add(f"faction_{f['id']}", 'faction', f['name'], hint, faction=f['id'],
                faction_key=f['key'], seat_name=f['seat_name'], tradition=f['tradition'],
                description=f['ethos']+'。'+f['seat_name']+'，传习'+f['art']+'。')
    towns=None
    if version>=4:
        towns=city_regions.settlements(rows,nodes,p,rng,independent_sects=version>=6)
        city_landscape.streets(rows,[nodes[0]]+towns,rng)
    elif version==3:
        city_landscape.allocate_lots(rows,nodes,tier)
        city_landscape.streets(rows,nodes,rng)
        for n in nodes:
            lot=n['footprint']
            for r in range(lot['row'],lot['row']+lot['size']):
                for c in range(lot['col'],lot['col']+lot['size']):
                    if rows[r][c] in 't,': rows[r][c]='.'
    else:
        for n in nodes:
            route, _ = path_between(rows, entrance, (n['row'],n['col']))
            for r,c in route:
                if rows[r][c] not in '=~^#':
                    rows[r][c] = '+'
        _building_footprints(rows, nodes)
    if version>=7:
        rows=[['s' if cell=='=' else cell for cell in row] for row in rows]
    result = dict(version=version, place=place, name=p['name'], size=size, theme=theme,
                  theme_label=THEMES[theme], authored=bool(authored),
                  note=authored['note'] if authored else f"依{THEMES[theme]}的水陆、街市和营生规律布局。",
                  road_name=authored['road'] if authored else p['name']+'驿街',
                  rows=[''.join(row) for row in rows], nodes=nodes, entrance='gate')
    if version >= 2:
        if version>=5:
            result.update(tier=tier,tier_label=tier_label,art_pack=city_gardens.PACK,terrain_tiles=city_gardens.placements(rows,towns,theme,f'gardens/{seed}/{place}',p))
        else:
            result.update(tier=tier, tier_label=tier_label, art_pack=city_biomes.PACK,
                          terrain_tiles=city_biomes.placements(rows, towns if towns is not None else nodes, theme,
                              f'biomes/{place}/{0 if authored and version==2 else seed}',legacy=version==2))
    if version>=3:
        result.update(settlement_pack=city_landscape.SETTLEMENT_PACK,
                      elevations=city_landscape.elevations(rows,towns if towns is not None else nodes,result['terrain_tiles'],f'elevation/{seed}/{place}',**({'relief':2,'mountain_rise':2} if version>=5 else {})))
    if towns is not None:
        result['settlements']=towns
        city_regions.areas(result)
    result['layout_id'] = hashlib.sha256(json.dumps(result,ensure_ascii=False,sort_keys=True).encode()).hexdigest()[:16]
    return result


def layout(seed, place, version=VERSION, settlement_tier='general'):
    return deepcopy(_layout(seed, place, version,settlement_tier=settlement_tier))


def _world_layout(w, place):
    """Internal read-only view; simulation actions must not copy rendering data."""
    from .settlements import canonical
    place=canonical(w,place)
    tier=getattr(w,'city_settlement_profiles',{}).get(str(place),{}).get('tier','general')
    return _layout(w.seed,place,w.city_map_version,bool(w.settlements),tier)


def world_layout(w, place):
    return deepcopy(_world_layout(w, place))


def upgrade_position(w, old_version):
    """Move legacy save coordinates to the corresponding new facility or safe cell.

    No new events, time costs, world RNG draws, or original-save writes are needed.
    The caller owns checkpointing its own save.
    """
    if old_version == VERSION:
        return
    h = w.player['protagonist']
    from .settlements import canonical
    place = canonical(w, h['place'])
    old = _layout(w.seed, place, old_version, bool(w.settlements))
    city = world_layout(w, place)
    at = h['row'], h['col']
    if old_version>=7 and old['size']==city['size']:
        old_area=city_regions.area_at(old,*at);new_area=city_regions.area_at(city,*at)
        if old_area and new_area and (old_area['id']==new_area['id'] or old_area['kind']=='landscape') and new_area['passable']:
            return
    previous = next((n for n in old['nodes'] if (n['row'], n['col']) == at), None)
    if previous is None:
        previous=next((n for n in old['nodes']
                       if (lot:=n.get('footprint')) and lot['row']<=at[0]<lot['row']+lot['size']
                       and lot['col']<=at[1]<lot['col']+lot['size']),None)
    same = next((n for n in city['nodes'] if previous and n['id'] == previous['id']), None)
    if same:
        target = same['row'], same['col']
    else:
        hint = tuple(scaled_coordinate(v, old['size'], city['size']) for v in at)
        gate = city['nodes'][0]
        reachable, pending = {(gate['row'], gate['col'])}, [(gate['row'], gate['col'])]
        while pending:
            r, c = pending.pop()
            for q in ((r-1,c),(r+1,c),(r,c-1),(r,c+1)):
                if (0 <= q[0] < city['size'] and 0 <= q[1] < city['size'] and q not in reachable
                        and city['rows'][q[0]][q[1]] not in '~^#'):
                    reachable.add(q); pending.append(q)
        target = min(reachable, key=lambda q: (abs(q[0]-hint[0])+abs(q[1]-hint[1]), q))
    h.update(place=place, row=target[0], col=target[1])


def venue(w, n, action):
    city = _world_layout(w, n['place'])
    ids = {node['id'] for node in city['nodes']}
    kind = action['kind']
    if kind == 'travel':
        wanted = 'gate'
    elif kind == 'work':
        wanted = WORK_PLACES.get(n['job'], 'residence')
    elif kind in ('join','duty','train','teach','mentor','duel','faction_task','faction_study','faction_guide'):
        faction = f"faction_{n['faction']}"
        wanted = faction if faction in ids else 'training'
    elif kind in ('public_service','report','embezzle'):
        wanted = 'government'
    elif kind == 'treatment':
        wanted = 'clinic'
    elif kind == 'donate_relief':
        wanted = 'granary'
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
    from . import local_life
    if local_life.active(w):
        local_life.record_action(w, n, action, event)
        return
    w.city_positions[str(n['id'])] = dict(place=n['place'], node=venue(w,n,action),
                                        event_id=event['id'], month=w.month, action=action['kind'])


@lru_cache(maxsize=512, typed=True)
def _gate_paths(seed,place,version,compact,settlement_tier='general'):
    """One shared walking tree for every facility; immutable across views."""
    city=_layout(seed,place,version,compact,settlement_tier)
    rows=city['rows']; size=city['size']
    gate=next(n for n in city['nodes'] if n['id']==city['entrance'])
    start=gate['row'],gate['col']
    queue=[(0,start)]; costs={start:0}; previous={}
    while queue:
        cost,p=heapq.heappop(queue)
        if cost!=costs[p]:continue
        for dr,dc in ((-1,0),(1,0),(0,-1),(0,1)):
            q=p[0]+dr,p[1]+dc
            if not (0<=q[0]<size and 0<=q[1]<size) or rows[q[0]][q[1]] in '~^#':continue
            step=1 if rows[q[0]][q[1]] in '+=' else 3 if rows[q[0]][q[1]]=='t' else 2
            value=cost+step
            if value<costs.get(q,float('inf')):
                costs[q]=value;previous[q]=p;heapq.heappush(queue,(value,q))
    result={}
    for node in city['nodes']:
        at=node['row'],node['col']
        if at not in costs:
            result[node['id']]=((),0);continue
        path=[at];p=at
        while p!=start:p=previous[p];path.append(p)
        result[node['id']]=(tuple(reversed(path)),costs[at])
    return result


def view(w, place):
    from . import local_life
    if type(place) is not int or not 0 <= place < len(PLACES):
        raise ValueError('地点不存在')
    from .settlements import canonical
    place = canonical(w, place)
    city = world_layout(w, place)
    owner = w.countries[territory.owner(w, place)]
    city.update(country=owner['id'], country_name=owner['name'], color=owner['color'], month=w.month,
                occupied=territory.owner(w,place)!=PLACES[place]['country'],
                population=len(w.residents[place]), terrain_legend={k:v for k,v in TERRAIN.items() if k!='=' or city['version']<7})
    present = {node['id']: [] for node in city['nodes']}
    tier=getattr(w,'city_settlement_profiles',{}).get(str(place),{}).get('tier','general')
    access=_gate_paths(w.seed,place,w.city_map_version,bool(w.settlements),tier)
    if city['version']>=8:
        city['settlement_layout']['registration']=deepcopy(getattr(w,'city_settlement_profiles',{}).get(str(place),{}))
    for npc_id in w.residents[place]:
        n = w.npcs[npc_id]
        pos = w.city_positions.get(str(npc_id))
        recorded = bool(pos and pos['place'] == place and pos['node'] in present)
        node = pos['node'] if recorded else 'residence'
        location = local_life.current(w, n) if local_life.active(w) else {}
        present[node].append(dict(id=npc_id,name=n['name'],job=n['job'], **location,
                                  source=pos.get('source', '行动落点') if recorded else '乡籍居所',
                                  event_id=pos['event_id'] if recorded else None,
                                  month=pos['month'] if recorded else None))
    for node in city['nodes']:
        node['residents'] = present[node['id']]
        if node['kind'] == 'government':
            node['description'] = f"现由{owner['name']}管辖，公帑、任官和征税按此归属结算。"
            if city['occupied']:
                node['original_name'], node['name'] = node['name'], owner['name']+'驻署'
        route,cost=access[node['id']]
        node['path'], node['walk_cost'] = list(route),cost
    for town in city.get('settlements',[]):
        services=[n for n in city['nodes'] if n.get('settlement_id')==town['id']]
        town['residents']=[resident for people in present.values() for resident in people
                           if resident.get('settlement_id')==town['id']] if local_life.active(w) else [resident for n in services for resident in n['residents']]
        representative=next(n for n in services if n['kind']!='gate')
        town['path']=deepcopy(representative['path']);town['walk_cost']=representative['walk_cost']
        town['country']=owner['id'];town['country_name']=owner['name']
    local_life.annotate(w, city)
    for area in city.get('areas',[]):
        area['country']=owner['id'];area['country_name']=owner['name']
    city['connections'] = [dict(place=r['b'] if r['a']==place else r['a'],sea=r['sea'],km=r['km'])
                           for r in w.routes if place in (r['a'],r['b'])]
    return city
