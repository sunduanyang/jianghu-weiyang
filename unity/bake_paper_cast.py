"""Pack original RGBA cutouts and calibrate skins for one shared FK skeleton.

The source PNGs are never redrawn. Atlas pixels are exact source pixels; the
runtime performs the articulation. Drawn copper rivets supply bind anchors.
"""
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage, signal

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'art/puppet-cast-24'
DEST = ROOT / 'unity/JianghuUnity/Assets/Resources/PaperCast'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def rivets(image):
    a = np.asarray(image)
    labels, _ = ndimage.label((a[:, :, :3].max(axis=2) < 75) & (a[:, :, 3] > 150))
    result = []
    for number, region in enumerate(ndimage.find_objects(labels), 1):
        if region is None:
            continue
        ys, xs = region
        w, h = xs.stop-xs.start, ys.stop-ys.start
        area = int((labels[region] == number).sum())
        if 8 < w < 100 and 8 < h < 100 and .7 < w/h < 1.4 and area > 40:
            result.append([xs.start+w/2, ys.start+h/2])
    return result


def anchor(image, candidates, expected):
    w, h = image.size
    point = min(candidates, key=lambda p: ((p[0]/w-expected[0])**2+(p[1]/h-expected[1])**2), default=None)
    outside_shoulder = point is not None and ((expected[0] > .75 and point[0]/w < .62) or (expected[0] < .25 and point[0]/w > .38))
    if point is None or outside_shoulder or abs(point[1]/h-expected[1]) > .22 or abs(point[0]/w-expected[0]) > .43:
        # Some ink rings touch the silhouette. Locate their circular stroke rather
        # than treating connected-component centres as joints in that case.
        a = np.asarray(image)
        ink = ((a[:, :, :3].max(axis=2) < 105) & (a[:, :, 3] > 120)).astype(float)
        yy, xx = np.mgrid[:h, :w]
        horizontal = .21 if expected[0] < .25 or expected[0] > .75 else .35
        allowed = (np.abs(yy/h-expected[1]) < .17) & (np.abs(xx/w-expected[0]) < horizontal) & (a[:, :, 3] > 150)
        best = (-1, None)
        for radius in range(9, 29):
            ky, kx = np.mgrid[-radius-2:radius+3, -radius-2:radius+3]
            ring = (np.abs(np.hypot(kx, ky)-radius) < 1.4).astype(float)
            score = signal.fftconvolve(ink, ring/ring.sum(), mode='same')
            score -= .08*(np.abs(xx/w-expected[0])+np.abs(yy/h-expected[1]))
            score[~allowed] = -1
            y, x = np.unravel_index(np.argmax(score), score.shape)
            if score[y, x] > best[0]:
                best = (score[y, x], [float(x), float(y)])
        if best[0] < .48:
            raise ValueError(f'Unresolved copper rivet in {image.info.get("source", "cutout")}: {expected}, score={best[0]}')
        point = best[1]
    return point


def pack(images):
    # A shelf atlas with an eight-pixel gutter prevents neighbouring cutouts bleeding.
    width, x, y, row = 1024, 8, 8, 0
    positions = {}
    for name, image in sorted(images.items(), key=lambda kv: (-kv[1].height, kv[0])):
        if x+image.width+8 > width:
            x, y, row = 8, y+row+16, 0
        positions[name] = (x, y)
        x += image.width+16
        row = max(row, image.height)
    height = ((y+row+8+255)//256)*256
    assert height <= 4096
    atlas = Image.new('RGBA', (width, height))
    for name, image in images.items():
        atlas.paste(image, positions[name])
    return atlas, positions


def texture_meta(path):
    template = (ROOT/'unity/JianghuUnity/Assets/Resources/Puppets/part-04e9b5a1beef5dd0.png.meta').read_text(encoding='utf-8')
    guid = hashlib.md5(str(path.relative_to(ROOT)).replace('\\', '/').encode()).hexdigest()
    import re
    template = re.sub(r'guid: [0-9a-f]+', 'guid: '+guid, template, count=1)
    template = template.replace('maxTextureSize: 2048', 'maxTextureSize: 4096').replace('nPOTScale: 1', 'nPOTScale: 0').replace('isReadable: 1', 'isReadable: 0').replace('enableMipMap: 0', 'enableMipMap: 1').replace('filterMode: 1', 'filterMode: 2')
    path.with_name(path.name+'.meta').write_text(template.rstrip()+'\n', encoding='utf-8')


def accessory_binding(name, size):
    w, h = size
    # Props attach to existing joints; none create or modify combat statistics.
    if name in ('apron', 'leather-apron', 'coin-pouch', 'abacus-pendant', 'waist-token'):
        return {'joint': 'hip', 'pivot': [w*.5, h*.08], 'height': 118, 'angle': 0, 'layer': 70}
    if name in ('cloud-shoulder', 'straw-cape', 'kasaya'):
        return {'joint': 'neck', 'pivot': [w*.5, h*.08], 'height': 150, 'angle': 0, 'layer': 42}
    if name in ('bamboo-staff', 'bamboo-stick'):
        return {'joint': 'palmNear', 'pivot': [w*.4, h*.5], 'width': 300, 'angle': 85, 'follow': 'forearmNear', 'layer': 82}
    if name in ('sheathed-saber', 'sheathed-sword', 'sword', 'saber'):
        return {'joint': 'hip', 'pivot': [w*.35, h*.5], 'width': 200, 'angle': 24, 'layer': 18}
    if name in ('bow',):
        return {'joint': 'palmFar', 'pivot': [w*.5, h*.5], 'width': 170, 'angle': -70, 'follow': 'forearmFar', 'layer': 18}
    if name in ('medicine-basket', 'fish-basket'):
        return {'joint': 'hip', 'pivot': [w*.35, h*.1], 'height': 115, 'angle': 0, 'layer': 18}
    if name in ('fly-whisk',):
        return {'joint': 'palmNear', 'pivot': [w*.15, h*.5], 'width': 210, 'angle': -15, 'follow': 'forearmNear', 'layer': 82}
    if name == 'folding-fan':
        return {'joint': 'palmNear', 'pivot': [w*.5, h*.88], 'height': 110, 'angle': -35, 'follow': 'forearmNear', 'layer': 82}
    if name == 'book-scroll':
        return {'joint': 'palmNear', 'pivot': [w*.5, h*.5], 'width': 95, 'angle': 0, 'follow': 'forearmNear', 'layer': 82}
    return {'joint': 'palmNear', 'pivot': [w*.5, h*.2], 'height': 85, 'angle': 0, 'follow': 'forearmNear', 'layer': 82}


def bake():
    DEST.mkdir(parents=True, exist_ok=True)
    spec = json.loads((SOURCE/'cast-spec.json').read_text(encoding='utf-8'))
    result = {'version': 1, 'expected_skins': len(spec), 'expected_parts': sum(16+len(c['accessories']) for c in spec), 'rig': json.loads((ROOT/'unity/paper-rig.json').read_text(encoding='utf-8')), 'skins': {}}
    count = 0
    for character in spec:
        cid = character['id']
        directory = SOURCE/cid
        source = json.loads((directory/'parts-manifest.json').read_text(encoding='utf-8'))
        assert digest(directory/'parts-sheet.png') == source['source_sha256']
        images = {p['id']: Image.open(directory/p['file']).convert('RGBA') for p in source['parts']}
        atlas, positions = pack(images)
        skin = {'id': cid, 'description': character['description'], 'atlas': cid, 'size': list(atlas.size), 'scale': .77 if 'child' in cid else .96 if 'elder' in cid else 1, 'parts': {}}
        for part in source['parts']:
            name, image = part['id'], images[part['id']]
            image.info['source'] = f'{cid}/{part["file"]}'
            x, y = positions[name]
            data = {'uv': [x/atlas.width, 1-(y+image.height)/atlas.height, image.width/atlas.width, image.height/atlas.height], 'size': list(image.size), 'source': f'{cid}/{part["file"]}', 'sha256': digest(directory/part['file'])}
            candidates = rivets(image)
            if name == 'head-neck':
                data.update(joint='neck', pivot=anchor(image, candidates, (.4, .86)), height=190 if 'child' in cid else 160, layer=60)
            elif name == 'torso':
                data.update(bone='torso', pivot=anchor(image, candidates, (.5, .87)), tip=anchor(image, candidates, (.5, .08)), layer=40)
                data['shoulderNear'] = anchor(image, candidates, (.84, .25))
                data['shoulderFar'] = anchor(image, candidates, (.16, .25))
            elif name in ('front-skirt', 'back-skirt'):
                data.update(joint='hip', pivot=anchor(image, candidates, (.5, .09)), height=151, layer=50 if name.startswith('front') else 5)
            elif name.endswith(('upper-arm', 'forearm', 'thigh', 'shin')):
                suffix = 'Near' if name.startswith('near') else 'Far'
                bone = 'upper' if name.endswith('upper-arm') else 'forearm' if name.endswith('forearm') else 'thigh' if name.endswith('thigh') else 'shin'
                data.update(bone=bone+suffix, pivot=anchor(image, candidates, (.55, .12)), tip=anchor(image, candidates, (.5, .89)), layer={'upperFar': 10, 'forearmFar': 12, 'thighFar': 16, 'shinFar': 17, 'thighNear': 25, 'shinNear': 26, 'upperNear': 72, 'forearmNear': 75}[bone+suffix])
            elif name.endswith(('hand', 'foot')):
                near = name.startswith('near')
                hand = name.endswith('hand')
                data.update(joint=('wrist' if hand else 'ankle')+('Near' if near else 'Far'), pivot=anchor(image, candidates, (.5 if hand else .28, .16)), height=62 if hand else 49, layer=(80 if near else 14) if hand else (29 if near else 20), follow=('forearm' if hand else '')+('Near' if near else 'Far'))
            else:
                data.update(accessory_binding(name, image.size))
            if 'bone' in data:
                assert math.dist(data['pivot'], data['tip']) > 20
            skin['parts'][name] = data
            # The packed atlas contains exactly the original independent PNG pixels.
            assert atlas.crop((x, y, x+image.width, y+image.height)).tobytes() == image.tobytes()
        torso = skin['parts']['torso']
        a, b = torso['pivot'], torso['tip']
        angle = -math.pi/2-math.atan2(b[1]-a[1], b[0]-a[0])
        scale = result['rig']['lengths']['spine']/math.dist(a, b)
        def body_point(p):
            dx, dy = p[0]-a[0], p[1]-a[1]
            return [round((dx*math.cos(angle)-dy*math.sin(angle))*scale, 4), round((dx*math.sin(angle)+dy*math.cos(angle))*scale, 4)]
        skin['shoulders'] = {side: body_point(torso['shoulder'+side]) for side in ('Near', 'Far')}
        atlas_path = DEST/(cid+'.png')
        atlas.save(atlas_path, optimize=True)
        texture_meta(atlas_path)
        skin['atlas_sha256'] = digest(atlas_path)
        result['skins'][cid] = skin
        count += len(skin['parts'])
    assert len(result['skins']) == result['expected_skins'] and count == result['expected_parts']
    if (ROOT/'art/rig-reference/dressed-parts.png').exists():
        from bake_reference_puppet import bake_reference
        bake_reference(result)
    if (ROOT/'art/rig-bamboo/source.png').exists():
        from bake_bamboo_puppet import bake_bamboo
        bake_bamboo(result)
    if (ROOT/'art/rig-plain/source.png').exists():
        from bake_plain_puppet import bake_plain
        bake_plain(result)
    (DEST/'index.json').write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({'skins': len(result['skins']), 'parts': sum(len(s['parts']) for s in result['skins'].values()), 'clips': len(result['rig']['clips']), 'runtime': str(DEST)}, ensure_ascii=False))


if __name__ == '__main__':
    bake()
