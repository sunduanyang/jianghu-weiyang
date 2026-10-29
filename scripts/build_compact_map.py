"""Rebuild art/world-map/geography-compact.json for the eight regional cities.

It reuses the land layer of art/world-map/geography.json, recomputes the region cells for
settlements.HUBS and lays a winding road for every route. Roads are a path search
over a cost field (sea and mountain ranges are expensive, a smooth noise field makes them meander, and
cells already used by a road are cheaper so routes share trunks); sea routes are gentle arcs.

    python scripts/build_compact_map.py

Needs NumPy, SciPy and Pillow (requirements-dev.txt); the game itself only reads the JSON.
"""
import heapq
import json
import math
from pathlib import Path
import re
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'unity'))
from jianghu.atlas import PLACES                           # noqa: E402
from jianghu import settlements                            # noqa: E402
from build_map import political_layers                     # noqa: E402
from bake_ui import RANGES, catmull, lonlat               # noqa: E402

CELL = 4                       # path-search grid in map units (map is 1280x760)
GW, GH = 1280 // CELL, 760 // CELL


def polygons(path):
    for section in re.split('[Mm]', path):
        nums = [float(n) for n in re.findall(r'-?\d+(?:\.\d+)?', section)]
        if len(nums) >= 6:
            yield list(zip(nums[::2], nums[1::2]))


def cost_field(land_path):
    land = Image.new('L', (GW, GH), 0)
    d = ImageDraw.Draw(land)
    for poly in polygons(land_path):
        d.polygon([(x / CELL, y / CELL) for x, y in poly], fill=255)
    land = np.array(land) > 127
    rng = np.random.default_rng(1200)
    wander = ndimage.gaussian_filter(rng.random((GH, GW)), 5)
    wander = (wander - wander.min()) / (wander.max() - wander.min())
    cost = 1.0 + wander * 2.6
    ridges = Image.new('L', (GW, GH), 0)
    dr = ImageDraw.Draw(ridges)
    for _, pts, scale in RANGES:
        # lonlat() returns 1280x760 map units
        line = [(x / CELL, y / CELL) for x, y in (lonlat(*p) for p in catmull(pts, 10))]
        dr.line(line, fill=255, width=max(2, int(4 * scale)))
    near_ridge = ndimage.gaussian_filter((np.array(ridges) > 0).astype(float), 1.5)
    cost += near_ridge * 14
    sea = ~land
    shore = ndimage.distance_transform_edt(land)
    cost = np.where(sea, 80.0, cost + np.where(shore < 2, 1.5, 0))
    return cost, land


def search(cost, start, goal, shared):
    """A* on an 8-connected grid; `shared` lowers the cost of cells an earlier road already uses."""
    sx, sy = start
    gx, gy = goal
    best = {start: 0.0}
    came = {}
    heap = [(0.0, start)]
    steps = [(-1, 0, 1), (1, 0, 1), (0, -1, 1), (0, 1, 1), (-1, -1, 1.414), (1, 1, 1.414), (-1, 1, 1.414), (1, -1, 1.414)]
    while heap:
        _, cur = heapq.heappop(heap)
        if cur == goal:
            break
        x, y = cur
        for dx, dy, length in steps:
            nx, ny = x + dx, y + dy
            if not (0 <= nx < GW and 0 <= ny < GH):
                continue
            c = cost[ny, nx] * (.45 if (nx, ny) in shared else 1)
            g = best[cur] + c * length
            if g < best.get((nx, ny), 1e18):
                best[(nx, ny)] = g
                came[(nx, ny)] = cur
                heapq.heappush(heap, (g + math.hypot(gx - nx, gy - ny) * 1.0, (nx, ny)))
    path = [goal]
    while path[-1] != start:
        path.append(came[path[-1]])
    return path[::-1]


def chaikin(points, rounds=3):
    for _ in range(rounds):
        out = [points[0]]
        for a, b in zip(points, points[1:]):
            out += [(a[0] * .75 + b[0] * .25, a[1] * .75 + b[1] * .25), (a[0] * .25 + b[0] * .75, a[1] * .25 + b[1] * .75)]
        out.append(points[-1])
        points = out
    return points


def simplify(points, tol):
    if len(points) <= 2:
        return points
    a, b = points[0], points[-1]
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = dx * dx + dy * dy
    def dist(p):
        t = max(0, min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / length)) if length else 0
        return math.hypot(p[0] - a[0] - t * dx, p[1] - a[1] - t * dy)
    i = max(range(1, len(points) - 1), key=lambda k: dist(points[k]))
    if dist(points[i]) <= tol:
        return [a, b]
    return simplify(points[:i + 1], tol)[:-1] + simplify(points[i:], tol)


FIELD_RNG = np.random.default_rng(1201)
DISPLACE = [ndimage.gaussian_filter(FIELD_RNG.standard_normal((GH, GW)), 4) for _ in range(2)]
DISPLACE = [d / np.abs(d).max() for d in DISPLACE]


def meander(points, land, amp=9.0):
    """Bend a road with a displacement field fixed in map space, so roads that share a trunk bend
    together instead of braiding; the ends stay pinned to the towns."""
    dense = [points[0]]
    for a, b in zip(points, points[1:]):
        n = max(1, int(math.hypot(b[0] - a[0], b[1] - a[1]) / 3))
        dense += [(a[0] + (b[0] - a[0]) * t / n, a[1] + (b[1] - a[1]) * t / n) for t in range(1, n + 1)]
    total = len(dense) - 1
    out = []
    for i, (x, y) in enumerate(dense):
        pin = min(1.0, min(i, total - i) / 6)                 # fade the bend in over ~18 map units
        gx, gy = min(GW - 1, max(0, int(x / CELL))), min(GH - 1, max(0, int(y / CELL)))
        q = (x + DISPLACE[0][gy, gx] * amp * pin, y + DISPLACE[1][gy, gx] * amp * pin)
        qx, qy = min(GW - 1, max(0, int(q[0] / CELL))), min(GH - 1, max(0, int(q[1] / CELL)))
        out.append(q if land[qy, qx] else (x, y))
    return out


def sea_arc(a, b, land):
    """Quadratic arc bowing to whichever side crosses less land."""
    mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy) or 1
    nx, ny = -dy / length, dx / length
    best = None
    for side in (1, -1):
        bend = side * length * .16
        c = (mx + nx * bend, my + ny * bend)
        pts = [((1 - t) ** 2 * a[0] + 2 * (1 - t) * t * c[0] + t * t * b[0],
                (1 - t) ** 2 * a[1] + 2 * (1 - t) * t * c[1] + t * t * b[1]) for t in np.linspace(0, 1, 24)]
        hits = sum(land[min(GH - 1, max(0, int(y / CELL))), min(GW - 1, max(0, int(x / CELL)))] for x, y in pts[3:-3])
        if best is None or hits < best[0]:
            best = (hits, pts)
    return best[1]


def path_string(points):
    return 'M' + ' L'.join(f'{x:.1f},{y:.1f}' for x, y in points)


def roads(land_path):
    cost, land = cost_field(land_path)
    shared = set()
    out = []
    # long routes first so shorter ones join their trunks
    order = sorted(settlements.COMPACT_ROUTES, key=lambda r: -r['km'])
    for r in order:
        a, b = PLACES[r['a']], PLACES[r['b']]
        pa, pb = (a['x'], a['y']), (b['x'], b['y'])
        if r['sea']:
            pts = sea_arc(pa, pb, land)
        else:
            start = (min(GW - 1, int(pa[0] / CELL)), min(GH - 1, int(pa[1] / CELL)))
            goal = (min(GW - 1, int(pb[0] / CELL)), min(GH - 1, int(pb[1] / CELL)))
            cells = search(cost, start, goal, shared)
            shared.update(cells)
            pts = [pa] + [((x + .5) * CELL, (y + .5) * CELL) for x, y in cells[1:-1]] + [pb]
            pts = simplify(pts, 2.5)                     # drop the grid's stair steps
            pts = meander(pts, land)
            pts = simplify(chaikin(pts, 3), .35)
        out.append(dict(a=r['a'], b=r['b'], sea=r['sea'], path=path_string(pts)))
    return sorted(out, key=lambda r: (r['a'], r['b']))


def label_anchors(data):
    """Store each region's land area and land centroid, so the game can place the region's name in
    the middle of its land (cells are clipped to the jianghu range and the coast)."""
    def mask(paths):
        m = Image.new('L', (1280, 760), 0)
        d = ImageDraw.Draw(m)
        for path in paths:
            for poly in polygons(path):
                d.polygon(poly, fill=255)
        return np.array(m) > 127
    land = mask([data['land']])
    territory = {t['country']: mask([t['path']]) for t in data['territories']}
    yy, xx = np.mgrid[0:760, 0:1280]
    for cell in data['cells']:
        m = mask([cell['path']]) & territory[cell['native_country']] & land
        area = int(m.sum())
        cell['area'] = area
        place = PLACES[cell['place']]
        cell['cx'] = round(float(xx[m].mean()), 1) if area else place['x']
        cell['cy'] = round(float(yy[m].mean()), 1) if area else place['y']


def main():
    full = json.loads((ROOT / 'art' / 'world-map' / 'geography.json').read_text(encoding='utf-8'))
    hubs = [PLACES[h] for h in settlements.HUBS]
    data = {**full, **political_layers(hubs)}
    data['note'] = f'江湖分为{len(hubs)}个地区，每区一城；范围按邻近城池示意划分。'
    data['roads'] = roads(full['land'])
    label_anchors(data)
    target = ROOT / 'art' / 'world-map' / 'geography-compact.json'
    target.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    print(f'{target.name}: {len(data["cells"])} cells, {len(data["roads"])} roads, {target.stat().st_size} bytes')


if __name__ == '__main__':
    main()
