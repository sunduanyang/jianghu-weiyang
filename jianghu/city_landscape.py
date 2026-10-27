"""Seeded, organic settlement landscapes and whole-lot elevation data."""
import heapq
import math
import random

SETTLEMENT_PACK = 'settlement-tiles-1-4-9-20261026'
SETTLEMENT_ASSETS = {1: '01-village-1cell', 2: '02-town-4cell', 3: '03-city-9cell'}
STEPS = ((-1,0),(0,1),(1,0),(0,-1))


def noise_grid(size, rng, spacing):
    count = math.ceil((size-1)/spacing)+2
    lattice = [[rng.random() for _ in range(count)] for _ in range(count)]
    result = []
    for r in range(size):
        row = []
        a, x = divmod(r/spacing, 1)
        a = int(a); x = x*x*(3-2*x)
        for c in range(size):
            b, y = divmod(c/spacing, 1)
            b = int(b); y = y*y*(3-2*y)
            row.append((lattice[a][b]*(1-y)+lattice[a][b+1]*y)*(1-x)
                       +(lattice[a+1][b]*(1-y)+lattice[a+1][b+1]*y)*x)
        result.append(row)
    return result


def landscape(source, size, theme, rng):
    """Warp landmark geography, then grow coherent woods and fields around it."""
    old = len(source)
    woods = noise_grid(size,rng,max(2,size/5))
    fields = noise_grid(size,rng,max(2,size/4))
    warp_r = noise_grid(size,rng,max(3,size/3))
    warp_c = noise_grid(size,rng,max(3,size/3))
    phase = rng.uniform(0,math.tau)
    rows = []
    for r in range(size):
        row = []
        for c in range(size):
            rr = r+(warp_r[r][c]-.5)*size*.30+math.sin(c*.29+phase)*size*.035
            cc = c+(warp_c[r][c]-.5)*size*.30+math.sin(r*.26+phase)*size*.055
            a = max(0,min(old-1,round(rr*(old-1)/(size-1))))
            b = max(0,min(old-1,round(cc*(old-1)/(size-1))))
            t = source[a][b]
            if t in '+=':
                t = '.' if t=='+' else '~'
            if t not in '~^#':
                forest = .42 if theme in ('forest','mountain','island') else .60
                if woods[r][c]>forest:
                    t = 't'
                elif fields[r][c]>.57 and theme not in ('steppe','forest'):
                    t = ','
                else:
                    t = '.'
            row.append(t)
        rows.append(row)
    return rows


def allocate_lots(rows,nodes,tier):
    """Give important sites 3x3/2x2 lots, reserving other entrances first."""
    size = len(rows)
    used = {(n['row'],n['col']) for n in nodes}
    core = 'palace' if any(n['kind']=='palace' for n in nodes) else 'government'
    priorities = {'palace':0,'government':1,'faction':2,'temple':3,'school':4,'market':5}
    for n in sorted(nodes,key=lambda n:(priorities.get(n['kind'],9),n['id'])):
        at = n['row'],n['col']
        desired = 3 if tier=='large' and n['kind']==core else 2 if n['kind'] in priorities else 1
        chosen = None
        for extent in range(desired,0,-1):
            options = []
            for r in range(max(0,at[0]-extent+1),min(at[0],size-extent)+1):
                for c in range(max(0,at[1]-extent+1),min(at[1],size-extent)+1):
                    cells = {(r+a,c+b) for a in range(extent) for b in range(extent)}
                    if (cells-{at}) & used or any(rows[a][b] in '~^#=' for a,b in cells):
                        continue
                    options.append((sum(rows[a][b]=='t' for a,b in cells),r,c,cells))
            if options:
                _,r,c,cells = min(options,key=lambda q:q[:3])
                chosen = r,c,extent,cells
                break
        r,c,extent,cells = chosen or (at[0],at[1],1,{at})
        used.update(cells)
        n['footprint'] = dict(row=r,col=c,size=extent)
        n['settlement_asset'] = SETTLEMENT_ASSETS[extent]


def _road(rows,start,goal,roughness):
    size = len(rows)
    queue = [(0,start)]; costs = {start:0}; previous = {}
    while queue:
        cost,p = heapq.heappop(queue)
        if cost!=costs[p]:
            continue
        if p==goal:
            path = [p]
            while p!=start:
                p = previous[p]; path.append(p)
            return path[::-1]
        for dr,dc in STEPS:
            q = p[0]+dr,p[1]+dc
            if not (0<=q[0]<size and 0<=q[1]<size):
                continue
            t = rows[q[0]][q[1]]
            step = {'+':.38,'=':.5,'.':1.3,',':1.6,'t':2.2,'~':8,'#':12,'^':18}[t]
            value = cost+step+roughness[q[0]][q[1]]*.85
            if value<costs.get(q,float('inf')):
                costs[q]=value; previous[q]=p; heapq.heappush(queue,(value,q))
    return []


def streets(rows,nodes,rng):
    """An irregular branching street network follows cheap land and reuses lanes."""
    size = len(rows)
    rough = noise_grid(size,rng,max(2,size/5))
    connected = [(nodes[0]['row'],nodes[0]['col'])]
    pending = [(n['row'],n['col']) for n in nodes[1:]]
    while pending:
        _,start,end = min((abs(a[0]-b[0])+abs(a[1]-b[1]),a,b) for a in connected for b in pending)
        for r,c in _road(rows,start,end,rough):
            rows[r][c] = '=' if rows[r][c] in '~=' else '+'
        connected.append(end); pending.remove(end)
    # A few short cross lanes create useful loops without a repeated city grid.
    for _ in range(max(1,size//12)):
        a,b = rng.sample(connected,2)
        if abs(a[0]-b[0])+abs(a[1]-b[1])<size*.65:
            for r,c in _road(rows,a,b,rough):
                rows[r][c] = '=' if rows[r][c] in '~=' else '+'


def elevations(rows,nodes,tiles,seed,relief=4,mountain_rise=3):
    """Flat whole lots, varied terraces, water at zero, walkable one-step slopes."""
    size = len(rows); rng = random.Random(seed)
    broad = noise_grid(size,rng,max(2,size/3))
    detail = noise_grid(size,rng,max(2,size/7))
    values = [[broad[r][c]*.50+detail[r][c]*.30+rng.random()*.20
               for c in range(size)] for r in range(size)]
    lo,hi = min(map(min,values)),max(map(max,values))
    levels = [[2+round(relief*(values[r][c]-lo)/max(.01,hi-lo)) for c in range(size)] for r in range(size)]
    for r in range(size):
        for c in range(size):
            if rows[r][c] in '~s': levels[r][c]=0
            elif rows[r][c]=='=': levels[r][c]=2
            elif rows[r][c]=='^': levels[r][c]+=mountain_rise
    groups = []
    membership = {}
    for p in [n['footprint'] for n in nodes]+[t for t in tiles if t['size']>1]:
        cells = [(p['row']+r,p['col']+c) for r in range(p['size']) for c in range(p['size'])]
        i = len(groups); groups.append(cells)
        for q in cells: membership[q]=i
        level = max(levels[r][c] for r,c in cells)
        for r,c in cells: levels[r][c]=level
    changed = True
    while changed:
        changed = False
        for r in range(size):
            for c in range(size):
                if rows[r][c] in '~^#': continue
                ceiling = min((levels[a][b]+1 for dr,dc in STEPS
                               if 0<=(a:=r+dr)<size and 0<=(b:=c+dc)<size and rows[a][b] not in '~^#'),default=levels[r][c])
                if levels[r][c]<=ceiling: continue
                for a,b in groups[membership[r,c]] if (r,c) in membership else [(r,c)]:
                    levels[a][b]=min(levels[a][b],ceiling)
                changed = True
    return levels
