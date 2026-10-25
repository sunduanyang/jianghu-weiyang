"""Rebuild the bundled map; network is used only by this authoring script."""
import hashlib
import json
from pathlib import Path
import sys
import urllib.request

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from jianghu.atlas import COUNTRIES, project
from jianghu.historical_map import ERA, TERRITORIES, SONG_JIN_FRONTIER, REGIONS, RIVERS

URL = 'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_land.geojson'


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
                points = simplify(ring, .04)
                if len(points) >= 4:
                    land.append(path(points))
    territories = [{'country': t['country'], 'name': COUNTRIES[t['country']]['name'],
                    'kind': COUNTRIES[t['country']]['kind'],
                    'path': ' '.join(path(ring) for ring in t['rings'])} for t in TERRITORIES]
    regions = [dict(country=c, name=name, x=project(lon, lat)[0], y=project(lon, lat)[1])
               for c, name, lon, lat in REGIONS]
    return {'map_version': 3, 'era': ERA,
            'coastline_source': URL, 'coastline_sha256': hashlib.sha256(raw).hexdigest(),
            'coastline_license': 'Natural Earth public domain',
            'note': '依据宋金时期格局手绘概略疆界；虚线表示诸部活动范围。路名标示行政归属，不声称精密测绘路界。',
            'viewBox': [0, 0, 1100, 760], 'land': ' '.join(land),
            'territories': territories, 'regions': regions,
            'frontier': path(SONG_JIN_FRONTIER, False),
            'rivers': [{'name': name, 'path': path(points, False)} for name, points in RIVERS]}


if __name__ == '__main__':
    local = Path(sys.argv[1]) if len(sys.argv) > 1 else None
    raw = local.read_bytes() if local else urllib.request.urlopen(URL, timeout=30).read()
    target = Path(__file__).resolve().parents[1]/'static'/'geography.json'
    target.write_text(json.dumps(build(raw), ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    print(f'Bundled map: {target.stat().st_size} bytes')
