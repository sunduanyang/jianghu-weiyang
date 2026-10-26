"""Turn one library combination (outfit set + face choices) into a PaperCast skin.

    python bake_library.py --body female --id npc-0042 --outfit hanfu-qing --headwear hanfu-qing \
        --face set1:3 --eyes set1:2 --brows set1:5 --mouth set1:1 --hair long-bun \
        --skin "#f1d9c4" --hair-color "#2b2230" [--out build/]

Output: <out>/<id>.png (atlas) + <out>/<id>.json, in the same skin format bake_paper_cast.py
writes into Resources/PaperCast/index.json. The current runtime (PaperCast.cs) can draw it
unchanged: every head layer becomes a `joint: neck` part whose pivot is the neck point
expressed in that layer's pixels, so the layered head moves as one with the rig.
Expressions swap the eyes/brows/mouth entries; dyeing is baked here (later: runtime tint).
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

from PIL import Image

import assemble
import rig

ROOT = rig.ROOT
sys.path.insert(0, str(ROOT / 'unity'))
from bake_paper_cast import pack  # noqa: E402  (same shelf packer as the 24-skin cast)

HEAD_BASE = 60


def gather(body, outfit, headwear, face, eyes, brows, mouth, hair, skin_color=None, hair_color=None):
    """Return {name: (image, part-dict)} with pivots in each image's own pixels."""
    parts = {}
    head = rig.BODIES[body]['head']
    if outfit:
        m = assemble.manifest(body, 'outfit', outfit)
        for name, p in m['parts'].items():
            if p['kind'] == 'accessory':
                continue
            img = Image.open(m['dir'] / p['file']).convert('RGBA')
            d = {'layer': p['layer'], 'pivot': p['pivot']}
            if p['kind'] == 'bone':
                d.update(bone=p['bone'], tip=p['tip'])
                if name == 'torso':
                    d.update(shoulderNear=p['shoulderNear'], shoulderFar=p['shoulderFar'])
            else:
                d.update(joint=p['joint'], height=round(img.height / p['scale'], 3), angle=0, follow=p.get('follow', ''))
            parts[name] = (img, d)

    def head_part(name, m, key, attach, layer, color=None):
        p = m['parts'][key]
        img = assemble.tint(Image.open(m['dir'] / p['file']).convert('RGBA'), color)
        a, n = rig.ANCHORS[attach], rig.ANCHORS['neck']
        s = p['scale'] * head          # px per head unit
        pivot = [round(p['pivot'][0] + (n[0] - a[0]) * s, 2), round(p['pivot'][1] + (n[1] - a[1]) * s, 2)]
        parts[name] = (img, {'joint': 'neck', 'pivot': pivot, 'height': round(img.height / p['scale'], 3), 'angle': 0,
                             'follow': '', 'layer': HEAD_BASE + layer})

    hat = False
    if headwear:
        m = assemble.manifest(body, 'headwear', headwear)
        hat = m['parts']['headwear'].get('style') == 'hat'
        head_part('head-wear', m, 'headwear', 'neck', rig.HEAD_LAYERS['headwear'])
    if face:
        sid, n = face.split(':')
        head_part('head-face', assemble.manifest(body, 'face', sid), f'face-{n}', 'neck', rig.HEAD_LAYERS['face'], skin_color)
    for kind, choice, attach in (('eyes', eyes, 'eye'), ('brows', brows, 'brow'), ('mouth', mouth, 'mouth')):
        if not choice:
            continue
        sid, n = choice.split(':')[:2]
        m = assemble.manifest(body, kind, sid)
        for expr in ('calm', 'angry', 'sad'):
            key = f'{kind}-{n}-{expr}'
            if key in m['parts']:
                head_part(f'head-{kind}' if expr == 'calm' else f'head-{kind}@{expr}', m, key, attach,
                          rig.HEAD_LAYERS[kind], hair_color if kind == 'brows' else None)
    if hair:
        m = assemble.manifest(body, 'hair', hair)
        key = 'hair-under-hat' if hat and 'hair-under-hat' in m['parts'] else 'hair-normal'
        head_part('head-hair', m, key, 'neck', rig.HEAD_LAYERS['hair'], hair_color)
    return parts


def bake(skin_id, body, out, description='', **choices):
    parts = gather(body, **choices)
    drawn = {k: v for k, v in parts.items() if '@' not in k}
    expressions = {k: v for k, v in parts.items() if '@' in k}
    atlas, positions = pack({k: img for k, (img, _) in parts.items()})
    out.mkdir(parents=True, exist_ok=True)
    atlas_path = out / f'{skin_id}.png'
    atlas.save(atlas_path, optimize=True)
    skin = {'id': skin_id, 'description': description or f'{rig.BODIES[body]["label"]}（拆件库组合）', 'atlas': skin_id,
            'size': list(atlas.size), 'scale': 1, 'body': body, 'choices': choices, 'parts': {}, 'expressions': {},
            'shoulders': {k: list(v) for k, v in rig.BODIES[body]['shoulders'].items()}}
    for group, items in (('parts', drawn), ('expressions', expressions)):
        for name, (img, d) in items.items():
            x, y = positions[name]
            d = dict(d, uv=[x / atlas.width, 1 - (y + img.height) / atlas.height, img.width / atlas.width, img.height / atlas.height],
                     size=list(img.size))
            skin[group][name] = d
    skin['atlas_sha256'] = hashlib.sha256(atlas_path.read_bytes()).hexdigest()
    (out / f'{skin_id}.json').write_text(json.dumps(skin, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    return skin, atlas


def render_like_runtime(skin, atlas, body, px=2.6, size=(900, 1400), pose_values=None):
    """Mirror of PaperCast.RenderPaperFigure (right-facing) for checking a baked skin."""
    import math
    joints, ang = rig.pose(body, pose_values)
    origin = (size[0] * .46, size[1] * .57)
    to_px = lambda u: (origin[0] + u[0] * px, origin[1] + u[1] * px)
    ends = {'torso': ('hip', 'neck')}
    for s in ('Near', 'Far'):
        ends.update({'upper' + s: ('shoulder' + s, 'elbow' + s), 'forearm' + s: ('elbow' + s, 'wrist' + s),
                     'thigh' + s: ('hip' + s, 'knee' + s), 'shin' + s: ('knee' + s, 'ankle' + s)})
    layers = []
    for name, p in skin['parts'].items():
        u = p['uv']
        x0 = round(u[0] * atlas.width)
        y0 = round((1 - u[1] - u[3]) * atlas.height)
        img = atlas.crop((x0, y0, x0 + p['size'][0], y0 + p['size'][1]))
        if 'bone' in p:
            a, z = ends[p['bone']]
            src = (p['tip'][0] - p['pivot'][0], p['tip'][1] - p['pivot'][1])
            dst = (joints[z][0] - joints[a][0], joints[z][1] - joints[a][1])
            angle = math.degrees(math.atan2(dst[1], dst[0]) - math.atan2(src[1], src[0]))
            layers.append((p['layer'], assemble.affine(img, p['pivot'], to_px(joints[a]), angle, math.hypot(*dst) * px / math.hypot(*src), size)))
        else:
            angle = p['angle'] + ang.get(p.get('follow') or '', 0)
            layers.append((p['layer'], assemble.affine(img, p['pivot'], to_px(joints[p['joint']]), angle, p['height'] / p['size'][1] * px, size)))
    canvas = Image.new('RGBA', size, (0, 0, 0, 0))
    for _, im in sorted(layers, key=lambda t: t[0]):
        canvas.alpha_composite(im)
    return canvas


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--body', choices=sorted(rig.BODIES), required=True)
    ap.add_argument('--id', required=True)
    for k in ('outfit', 'headwear', 'face', 'eyes', 'brows', 'mouth', 'hair', 'skin', 'hair-color', 'description'):
        ap.add_argument('--' + k)
    ap.add_argument('--out', default=str(ROOT / 'prototypes/shadow-puppets/build'))
    a = ap.parse_args()
    skin, _ = bake(a.id, a.body, Path(a.out), a.description or '', outfit=a.outfit, headwear=a.headwear, face=a.face,
                   eyes=a.eyes, brows=a.brows, mouth=a.mouth, hair=a.hair, skin_color=a.skin, hair_color=a.hair_color)
    print(json.dumps({'skin': skin['id'], 'parts': len(skin['parts']), 'expressions': len(skin['expressions']), 'out': a.out}, ensure_ascii=False))


if __name__ == '__main__':
    main()
