"""Ordinary settlement layouts and independent nine-cell sect seats."""
from collections import Counter
from copy import deepcopy

from .city_landscape import SETTLEMENT_ASSETS
from .city_biomes import ASSETS

NAMES = ('原野','草甸','林地','竹林','灌丛','荒野','旱地','农田','稻田','沙地','碎石地','泥滩',
         '河岸','浅滩','溪流','丘陵','群山','峡谷','天险','密林','沼泽','湿地','湖泊','河谷',
         '石林','断崖','天坑','梯田','高台','峡湾','三角洲','盆地')

LAYOUTS = {
    'prosperous': dict(label='繁华地区',keep=('settlement_0','settlement_1','settlement_2'),cells=[9,4,1]),
    'general': dict(label='一般地区',keep=('settlement_0','settlement_1'),cells=[9,4]),
    'sparse': dict(label='人口较少地区',keep=('settlement_0','settlement_2'),cells=[9,1]),
}


def population_tier(population):
    return 'prosperous' if population>=50 else 'general' if population>=25 else 'sparse'


def initialize_profiles(w):
    """Register stable layouts once, independently of day-to-day travel."""
    counts=Counter(n['place'] for n in w.npcs.values() if n['alive'])
    for p in w.active_places:
        key=str(p['id'])
        if key not in w.city_settlement_profiles:
            w.city_settlement_profiles[key]=dict(tier=population_tier(counts[p['id']]),population=counts[p['id']],since=w.month)


def consolidate(city,tier):
    """Reuse version-7 geography, sect lots and stable facility IDs."""
    if tier not in LAYOUTS:raise ValueError('未知聚落布局')
    plan=LAYOUTS[tier];old=city['settlements']
    towns=[t for t in old if t['kind']=='sect' or t['id'] in plan['keep']]
    by_id={t['id']:t for t in towns};main=by_id['settlement_0']
    town=by_id.get('settlement_1');village=by_id.get('settlement_2')
    for t in towns:t['services']=[]
    for node in city['nodes']:
        if node['kind']=='faction':target=by_id[node['settlement_id']]
        elif node['kind']=='workshop':target=town or main
        elif node['kind'] in ('residence','granary','garden'):target=village or town or main
        else:target=main
        node['settlement_id']=target['id']
        if node['kind']!='gate':
            node.update(row=target['row'],col=target['col'],footprint=deepcopy(target['footprint']),
                        settlement_asset=target['settlement_asset'])
        if node['kind'] not in target['services']:target['services'].append(node['kind'])
    city['settlements']=towns
    # Fill removed lots with ground without redrawing roads, water or elevations.
    covered={(t['row']+a,t['col']+b) for t in city['terrain_tiles'] for a in range(t['size']) for b in range(t['size'])}
    missing={(t['footprint']['row']+a,t['footprint']['col']+b) for t in old if t not in towns
             for a in range(t['footprint']['size']) for b in range(t['footprint']['size'])}
    missing-=covered
    for r,c in sorted(missing):
        city['terrain_tiles'].append(dict(row=r,col=c,size=1,asset='07-dryland' if city['theme'] in ('oasis','highland','frontier') else '33-winding-meadow'))
    city['terrain_tiles'].sort(key=lambda t:(t['row']+t['col']+2*(t['size']-1),t['row']))
    city['settlement_layout']=dict(tier=tier,label=plan['label'],ordinary_count=len(plan['keep']),
                                    cells=plan['cells'][:],sect_count=sum(t['kind']=='sect' for t in towns))
    areas(city)
    return city


def settlements(rows,nodes,place,rng,independent_sects=False):
    """Retain the city, town and both villages; append one 3x3 lot per sect."""
    size=len(rows);gate=nodes[0];used={(gate['row'],gate['col'])};result=[]
    sects=[n for n in nodes if n['kind']=='faction'] if independent_sects else []
    specifications=[('city',3,'主城',(.43,.48)),('town',2,'外镇',(.70,.73)),
                    ('village',1,'北村',(.18,.23)),('village',1,'南村',(.77,.23))]
    specifications += [('sect',3,n['name'],(.25,.75)) for n in sects]
    for i,(kind,extent,suffix,hint) in enumerate(specifications):
        hr=round(hint[0]*(size-1))+rng.randint(-max(1,size//9),max(1,size//9))
        hc=round(hint[1]*(size-1))+rng.randint(-max(1,size//9),max(1,size//9))
        candidates=[]
        for r in range(size-extent+1):
            for c in range(size-extent+1):
                cells={(r+a,c+b) for a in range(extent) for b in range(extent)}
                if cells&used:continue
                cost=sum({'~':12,'^':18,'#':14,'=':10,'t':.4}.get(rows[a][b],0) for a,b in cells)
                # Keep open land between settlements rather than arranging a rigid grid.
                crowd=sum(3 for a,b in cells for dr,dc in ((-1,0),(1,0),(0,-1),(0,1)) if (a+dr,b+dc) in used)
                candidates.append((cost+crowd+(abs(r+extent//2-hr)+abs(c+extent//2-hc))*.65,r,c,cells))
        _,r,c,cells=min(candidates,key=lambda q:q[:3]);used.update(cells)
        for a,b in cells:rows[a][b]='+'
        sect=sects[i-4] if kind=='sect' else None
        result.append(dict(id=f"sect_{sect['faction_key']}" if sect else f'settlement_{i}',kind=kind,
                           name=suffix if sect else place['name'] if i==0 else place['name']+suffix,
                           label='门派驻地' if sect else {3:'城市',2:'城镇',1:'村落'}[extent],
                           glyph='宗' if sect else {3:'城',2:'镇',1:'村'}[extent],
                           description=sect['description'] if sect else '整片聚落共享地形、高程、通行和所属属性。市集、客栈等营生在聚落内办理。',
                           row=r+extent//2,col=c+extent//2,footprint=dict(row=r,col=c,size=extent),
                           settlement_asset=SETTLEMENT_ASSETS[extent],services=[],
                           **({k:sect[k] for k in ('faction','faction_key','seat_name','tradition')} if sect else {})))
    for node in nodes:
        index=(1 if node['kind'] in ('workshop','faction') else 2 if node['kind'] in ('residence','granary')
               else 3 if node['kind'] in ('garden','temple','dock') else 0)
        town=(result[4+sects.index(node)] if node in sects else result[index]);node['settlement_id']=town['id']
        if node['kind']!='gate':
            node.update(row=town['row'],col=town['col'],footprint=deepcopy(town['footprint']),
                        settlement_asset=town['settlement_asset'])
        else:
            node['footprint']=dict(row=node['row'],col=node['col'],size=1)
        if node['kind'] not in town['services']:town['services'].append(node['kind'])
    return result


def areas(city):
    """One property record per region, referenced by all of its cells."""
    from .city_gardens import LABELS
    size=city['size'];indices=[[-1]*size for _ in range(size)];result=[]
    lots=[dict(**town['footprint'],asset=town['settlement_asset'],id=town['id'],
               kind=town['kind'],name=town['name'],
               **({k:town[k] for k in ('faction','faction_key')} if town['kind']=='sect' else {}))
          for town in city['settlements']]
    occupied={(p['row']+r,p['col']+c) for p in lots for r in range(p['size']) for c in range(p['size'])}
    for tile in city['terrain_tiles']:
        r,c=tile['row'],tile['col']
        if (r,c) in occupied:continue
        label=LABELS[tile['asset']] if tile['asset'] in LABELS else NAMES[ASSETS.index(tile['asset'])]
        lots.append(dict(**tile,id=f"landscape_{r}_{c}",kind='landscape',name=label))
    for lot in lots:
        r,c,extent=lot['row'],lot['col'],lot['size'];terrain=city['rows'][r][c]
        record=dict(lot,terrain=terrain,elevation=city['elevations'][r][c],passable=terrain not in '~^#',
                    move_cost=1 if terrain in '+=' else 3 if terrain=='t' else 2)
        for a in range(r,r+extent):
            for b in range(c,c+extent):
                assert indices[a][b]==-1
                assert city['rows'][a][b]==terrain
                assert city['elevations'][a][b]==record['elevation']
                indices[a][b]=len(result)
        result.append(record)
    assert all(i>=0 for row in indices for i in row)
    city.update(areas=result,cell_areas=indices)


def area_at(city,row,col):
    if 'cell_areas' in city:return city['areas'][city['cell_areas'][row][col]]
    return None
