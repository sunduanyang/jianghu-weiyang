"""Curated, coherent landscape palette for readable native region maps."""
import math
import random

from . import city_biomes, city_landscape

PACK='landscape-mix-20261027'
NEW_ASSETS=(
    ('33-winding-meadow','曲径草坡',1,1),('34-ancient-grove','疏林古木',1,2),
    ('35-jagged-peaks','青灰群峰',2,3),('36-green-spires','苍翠奇峰',2,4),
    ('37-river-terraces','溪谷梯田',2,5),('38-red-mesas','丹霞岩台',2,6),
    ('39-lava-basin','熔岩盆地',2,7),('40-volcanic-caldera','火山口',2,27),
    ('41-deep-sea','深蓝海面',1,28),('42-blue-water','碧蓝水面',1,29),
    ('43-snowfield','皑皑雪原',1,30),
)
ASSETS=city_biomes.ASSETS+tuple(a[0] for a in NEW_ASSETS)
FOOTPRINTS={a:1 if i<16 else 2 for i,a in enumerate(city_biomes.ASSETS)}
FOOTPRINTS.update({a:size for a,_,size,_ in NEW_ASSETS})
LABELS={a:label for a,label,_,_ in NEW_ASSETS}
POOLS={
    '.':('01-open-plains','02-meadow','07-dryland','10-sand-flat','33-winding-meadow','43-snowfield'),
    '+':('33-winding-meadow','07-dryland'),'=':('42-blue-water',),'s':('14-shallows',),
    '~':('42-blue-water','41-deep-sea'),'t':('34-ancient-grove','03-woodland','04-bamboo-forest'),
    ',':('08-farmland','07-dryland'),'^':('16-low-hills','43-snowfield'),'#':('11-gravel-land',),
}
LANDMARKS={
    '.':('29-plateau','32-basin'), '~':('23-lake','24-river-valley','31-river-delta'),
    't':('20-deep-forest',), ',':('28-terraced-fields','37-river-terraces'),
    '^':('17-mountain-massif','35-jagged-peaks','36-green-spires','38-red-mesas','40-volcanic-caldera'),
}
SELECTED=set(a for pool in list(POOLS.values())+list(LANDMARKS.values()) for a in pool)


def landscape(source,size,theme,rng):
    rows=city_landscape.landscape(source,size,theme,rng)
    woods=city_landscape.noise_grid(size,rng,max(3,size/4))
    fields=city_landscape.noise_grid(size,rng,max(3,size/4))
    forest=.48 if theme=='forest' else .56 if theme in ('mountain','island') else .62
    for r in range(size):
        for c in range(size):
            if rows[r][c] in '~^#':continue
            rows[r][c]='t' if woods[r][c]>forest else ',' if fields[r][c]>.57 and theme not in ('forest','steppe') else '.'
    return rows


def placements(rows,nodes,theme,seed,place):
    rng=random.Random(seed);size=len(rows)
    patches=city_landscape.noise_grid(size,rng,max(3,size/4))
    dry=theme in ('oasis','frontier','highland')
    cold=theme=='highland' or theme in ('forest','steppe') and place['lat']>=40
    occupied={(n['footprint']['row']+r,n['footprint']['col']+c) for n in nodes
              for r in range(n['footprint']['size']) for c in range(n['footprint']['size'])}
    lots=set(occupied);occupied=set();result=[];used={t:0 for t in LANDMARKS}
    counts={t:sum(row.count(t) for row in rows) for t in LANDMARKS}
    budgets={t:max(1,math.ceil(count/(22 if t=='t' else 16))) for t,count in counts.items()}
    budgets['~']=max(1,size//8);budgets['.']=max(1,size//12)
    corners=[(r,c) for r in range(size-1) for c in range(size-1)];rng.shuffle(corners)
    for r,c in corners:
        t=rows[r][c];cells={(r+a,c+b) for a in (0,1) for b in (0,1)}
        if t not in LANDMARKS or used[t]>=budgets[t] or cells&(occupied|lots) or any(rows[a][b]!=t for a,b in cells):continue
        if rng.random()>(.28 if t=='.' else .58):continue
        choices=LANDMARKS[t]
        if t=='^':choices=('38-red-mesas','35-jagged-peaks') if dry else ('35-jagged-peaks','36-green-spires','17-mountain-massif')
        if t=='^' and theme=='island' and used[t]==0:choices=('40-volcanic-caldera',)
        if t=='~' and not dry:choices=('23-lake','24-river-valley','31-river-delta')
        asset=choices[min(len(choices)-1,int(patches[r][c]*len(choices)))]
        result.append(dict(row=r,col=c,size=2,asset=asset));occupied.update(cells);used[t]+=1
    for r in range(size):
        for c in range(size):
            if (r,c) in occupied:continue
            t=rows[r][c];value=patches[r][c]
            if t in '.+':
                asset='07-dryland' if dry else '33-winding-meadow' if t=='+' or value>.56 else '02-meadow' if value<.38 else '01-open-plains'
                if cold and t=='.' and value>.62:asset='43-snowfield'
            elif t=='~':asset='41-deep-sea' if theme in ('port','island','coast') and value>.58 else '42-blue-water'
            elif t=='t':asset='04-bamboo-forest' if theme=='water' and value>.65 else '03-woodland' if value>.72 else '34-ancient-grove'
            elif t=='^':asset='43-snowfield' if cold and value>.48 else '16-low-hills'
            elif t==',':asset='07-dryland' if dry and value>.65 else '08-farmland'
            else:asset=POOLS[t][0]
            result.append(dict(row=r,col=c,size=1,asset=asset))
    return sorted(result,key=lambda p:(p['row']+p['col']+2*(p['size']-1),p['row']))
