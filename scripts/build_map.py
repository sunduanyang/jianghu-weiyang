"""Rebuild the bundled map; network is used only by this authoring script.

    python scripts/build_map.py --reuse-coast   # keep the stored coastline, re-projecting it if needed
"""
import hashlib
import json
import re
from pathlib import Path
import sys
import urllib.request

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from jianghu.atlas import (COUNTRIES, PLACES, MAP_VERSION, MAP_BOUNDS, PROJECTION, LEGACY_PROJECTION,
                           project, unproject)
from jianghu.historical_map import ERA, TERRITORIES, REGIONS, RIVERS

URL = 'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_land.geojson'


def simplify(points, tolerance=.055):
    if len(points) <= 2:
        return points
    a, b = points[0], points[-1]
    dx, dy = b[0]-a[0], b[1]-a[1]
    length = dx*dx+dy*dy
    def distance(p):
        t = max(0, min(1, ((p[0]-a[0])*dx+(p[1]-a[1])*dy)/length)) if length else 0
        return ((p[0]-a[0]-t*dx)**2+(p[1]-a[1]-t*dy)**2)**.5
    index = max(range(1, len(points)-1), key=lambda i: distance(points[i]))
    if distance(points[index]) <= tolerance:
        return [a, b]
    return simplify(points[:index+1], tolerance)[:-1]+simplify(points[index:], tolerance)


def path(points, close=True):
    coords = [project(*p[:2]) for p in points]
    return 'M'+' L'.join(f'{x},{y}' for x, y in coords)+(' Z' if close else '')


def build(raw):
    # Only physical land/coastline is imported. No modern country/province boundaries.
    land = []
    for feature in json.loads(raw)['features']:
        polys = feature['geometry']['coordinates']
        if feature['geometry']['type'] == 'Polygon':
            polys = [polys]
        for polygon in polys:
            for ring in polygon:
                if max(p[0] for p in ring) < 67 or min(p[0] for p in ring) > 145 or max(p[1] for p in ring) < 15:
                    continue
                points = simplify(ring, .016)
                if len(points) >= 4:
                    land.append(path(points))
    return {**political_layers(), 'projection': PROJECTION,
            'coastline_source': URL, 'coastline_sha256': hashlib.sha256(raw).hexdigest(),
            'coastline_license': 'Natural Earth public domain', 'land': ' '.join(land)}


def clip_halfplane(poly, a, b, c):
    """Convex Voronoi half-plane clipping in projected map coordinates."""
    result = []
    for p, q in zip(poly, poly[1:]+poly[:1]):
        dp, dq = a*p[0]+b*p[1]-c, a*q[0]+b*q[1]-c
        if dp <= 1e-8:
            result.append(p)
        if (dp <= 1e-8) != (dq <= 1e-8):
            ratio = dp/(dp-dq)
            result.append((p[0]+ratio*(q[0]-p[0]), p[1]+ratio*(q[1]-p[1])))
    return result


def political_layers(places=PLACES):
    territories = [{'country': t['country'], 'name': COUNTRIES[t['country']]['name'],
                    'kind': COUNTRIES[t['country']]['kind'],
                    'path': ' '.join(path(ring) for ring in t['rings'])} for t in TERRITORIES]
    regions = [dict(country=c, name=name, x=project(lon, lat)[0], y=project(lon, lat)[1])
               for c, name, lon, lat in REGIONS]
    cells = []
    for p in places:
        cell = [(-80, -70), (1320, -70), (1320, 830), (-80, 830)]
        for q in places:
            if p['id'] == q['id']:
                continue
            a, b = 2*(q['x']-p['x']), 2*(q['y']-p['y'])
            c = q['x']**2+q['y']**2-p['x']**2-p['y']**2
            cell = clip_halfplane(cell, a, b, c)
        cells.append({'place': p['id'], 'native_country': p['country'],
                      'path': 'M'+' L'.join(f'{x:.2f},{y:.2f}' for x, y in cell)+' Z'})
    return {'map_version': MAP_VERSION, 'era': ERA, 'projection': PROJECTION,
            'note': '八个地区按邻近城池划分，范围为示意，并非历史疆界。',
            'viewBox': MAP_BOUNDS, 'cells': cells,
            'territories': territories, 'regions': regions,
            'rivers': [{'name': name, 'path': path(points, False)} for name, points in RIVERS]}


def reproject_path(svg, old, new=PROJECTION):
    """Re-project a stored coastline path from one linear projection to another."""
    def point(match):
        x, y = float(match.group(1)), float(match.group(2))
        nx, ny = project(*unproject(x, y, old), new)
        return f'{nx},{ny}'
    return re.sub(r'(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)', point, svg)


if __name__ == '__main__':
    target = Path(__file__).resolve().parents[1]/'art'/'world-map'/'geography.json'
    if '--reuse-coast' in sys.argv:
        stored = json.loads(target.read_text(encoding='utf-8'))
        old = stored.get('projection', LEGACY_PROJECTION)
        if old != PROJECTION:
            stored['land'] = reproject_path(stored['land'], old)
        stored.pop('frontier', None)
        data = {**stored, **political_layers()}
    else:
        local = Path(sys.argv[1]) if len(sys.argv) > 1 else None
        raw = local.read_bytes() if local else urllib.request.urlopen(URL, timeout=30).read()
        data = build(raw)
    target.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    print(f'Bundled map: {target.stat().st_size} bytes')
