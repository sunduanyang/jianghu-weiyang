"""Terrain-biomes-v3 placement contract for the native Unity city maps.

Every placement owns either one cell or a complete 2x2 square. Roads, bridges
and facilities stay individually selectable and never hide under a large tile.
"""
import random

PACK = 'terrain-biomes-v3-20261026'
ASSETS = (
    '01-open-plains', '02-meadow', '03-woodland', '04-bamboo-forest',
    '05-scrubland', '06-wilderness', '07-dryland', '08-farmland',
    '09-rice-paddies', '10-sand-flat', '11-gravel-land', '12-mudflat',
    '13-riverbank', '14-shallows', '15-stream', '16-low-hills',
    '17-mountain-massif', '18-grand-canyon', '19-natural-barrier', '20-deep-forest',
    '21-bog', '22-wetland', '23-lake', '24-river-valley', '25-stone-forest',
    '26-escarpment', '27-sinkhole', '28-terraced-fields', '29-plateau',
    '30-fjord', '31-river-delta', '32-basin',
)


def size_for(place, version=5):
    """Capitals / regional centres; ordinary cities / ports; small settlements."""
    if place['kind'] == 'capital' or place['weight'] >= 8:
        return (40 if version==2 else 30), 'large', ('大城' if version==2 else '大区')
    if place['kind'] in ('city', 'port'):
        return (30 if version==2 else 20), 'medium', ('中城' if version==2 else '中区')
    return (20 if version==2 or version>=5 else 10), 'small', ('小城' if version==2 else '小区')


def placements(rows, nodes, theme, seed, legacy=False):
    rng = random.Random(seed)
    size = len(rows)
    occupied = set()
    lots = {(n['footprint']['row']+r, n['footprint']['col']+c)
            for n in nodes for r in range(n['footprint']['size'])
            for c in range(n['footprint']['size'])}
    dry = theme in ('oasis', 'highland', 'frontier')
    wet = theme in ('water', 'river', 'port', 'coast', 'island')
    single = {'.': [7, 10] if dry else [1, 2], '+': [11], '=': [14],
              '~': [12, 13, 14, 15], '^': [16], '#': [11],
              't': [5, 6] if dry else [3, 4, 5], ',': [9] if wet else [8, 7]}
    large = {'^': [17, 18, 19, 25, 26, 27], 't': [20],
             '~': [23, 24, 30, 31] if wet else [21, 22, 23],
             ',': [28], '.': [29, 32] if dry else [32]}
    result = []
    # Whole four-cell terrain lots, with no overlap and no suppressed movement cell.
    corners = [(r,c) for r in range(0,size-1,2 if legacy else 1)
               for c in range(0,size-1,2 if legacy else 1)]
    if not legacy: rng.shuffle(corners)
    for r,c in corners:
        t = rows[r][c]
        cells = {(r+a, c+b) for a in (0, 1) for b in (0, 1)}
        if (t not in large or cells & (lots | occupied) or any(rows[a][b] != t for a, b in cells)
                or rng.random() > (.16 if t == '.' else .86)):
            continue
        asset = rng.choice(large[t])
        result.append(dict(row=r, col=c, size=2, asset=ASSETS[asset-1]))
        occupied.update(cells)
    for r in range(size):
        for c in range(size):
            if (r, c) not in occupied:
                asset = rng.choice(single[rows[r][c]])
                result.append(dict(row=r, col=c, size=1, asset=ASSETS[asset-1]))
    return sorted(result, key=lambda p: (p['row']+p['col']+2*(p['size']-1), p['row']))
