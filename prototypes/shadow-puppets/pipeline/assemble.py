"""Put cut parts back on the rig to check a combination (换装 / 捏脸 preview).

    python assemble.py --body female --outfit hanfu-qing --headwear hanfu-qing \
        --face set1:3 --eyes set1:2:calm --brows set1:5 --mouth set1:1 --hair long-bun \
        --skin "#f1d9c4" --hair-color "#2b2230" --out preview.png [--pose '{"near_upper": -40}']

Library ids refer to library/<body>/<kind>/<id>/ written by cut_sheet.py. The outfit and
its headwear are a set (same id by convention); face / eyes / brows / mouth / hair are the
player's choice. Hats (style "hat") automatically use the hair's under-hat variant.
"""
import argparse
import json
import math
import os
from pathlib import Path

import numpy as np
from PIL import Image

import rig

LIBRARY = Path(os.environ.get('PUPPET_LIBRARY', Path(__file__).resolve().parent.parent / 'library'))
HEAD_BASE = 60


def manifest(body, kind, set_id):
    path = LIBRARY / body / kind / set_id / 'manifest.json'
    if not path.exists():
        raise SystemExit(f'missing {path} — run cut_sheet.py first')
    m = json.loads(path.read_text(encoding='utf-8'))
    m['dir'] = path.parent
    return m


def tint(img, hex_color, strength=1.0):
    """Dye a neutral-drawn layer: keep its luminance, replace the hue (skin / hair colour)."""
    if not hex_color:
        return img
    c = np.array([int(hex_color.lstrip('#')[i:i + 2], 16) for i in (0, 2, 4)], float)
    a = np.asarray(img).astype(float)
    lum = (a[:, :, :3] @ [.299, .587, .114]) / 255.0
    # map mid-grey to the target colour, keep shading and dark ink lines
    dyed = np.clip(c[None, None, :] * (lum[:, :, None] / .78), 0, 255)
    a[:, :, :3] = a[:, :, :3] * (1 - strength) + dyed * strength
    return Image.fromarray(a.astype(np.uint8))


def affine(img, pivot, target, angle, scale, canvas_size):
    """Image placed so `pivot` lands on `target`, rotated by `angle` degrees, scaled by `scale`."""
    t = math.radians(angle)
    cs, sn = math.cos(t), math.sin(t)
    tx, ty = target
    data = (cs / scale, sn / scale, pivot[0] - (cs * tx + sn * ty) / scale,
            -sn / scale, cs / scale, pivot[1] - (-sn * tx + cs * ty) / scale)
    return img.transform(canvas_size, Image.AFFINE, data, resample=Image.BICUBIC)


def compose(body, outfit=None, headwear=None, face=None, eyes=None, brows=None, mouth=None, hair=None,
            skin=None, hair_color=None, pose_values=None, px=2.6, size=(900, 1400)):
    b = rig.BODIES[body]
    joints, ang = rig.pose(body, pose_values)
    origin = (size[0] * .46, size[1] * .57)
    to_px = lambda u: (origin[0] + u[0] * px, origin[1] + u[1] * px)
    layers = []  # (layer, image-on-canvas)

    def open_part(m, name):
        part = m['parts'][name]
        return Image.open(m['dir'] / part['file']).convert('RGBA'), part

    if outfit:
        m = manifest(body, 'outfit', outfit)
        bone_ends = {'torso': ('hip', 'neck')}
        for side in ('Near', 'Far'):
            bone_ends.update({'upper' + side: ('shoulder' + side, 'elbow' + side), 'forearm' + side: ('elbow' + side, 'wrist' + side),
                              'thigh' + side: ('hip' + side, 'knee' + side), 'shin' + side: ('knee' + side, 'ankle' + side)})
        for name, part in m['parts'].items():
            if part['kind'] == 'accessory':
                continue
            img = Image.open(m['dir'] / part['file']).convert('RGBA')
            if part['kind'] == 'bone':
                a, z = bone_ends[part['bone']]
                src = (part['tip'][0] - part['pivot'][0], part['tip'][1] - part['pivot'][1])
                dst = (joints[z][0] - joints[a][0], joints[z][1] - joints[a][1])
                angle = math.degrees(math.atan2(dst[1], dst[0]) - math.atan2(src[1], src[0]))
                scale = math.hypot(*dst) * px / math.hypot(*src)
                layers.append((part['layer'], affine(img, part['pivot'], to_px(joints[a]), angle, scale, size)))
            else:
                angle = ang.get(part.get('follow') or '', 0) if 'hand' in name else rig.REST.get('lean', 0) if name.endswith('skirt') or name == 'waist' else 0
                layers.append((part['layer'], affine(img, part['pivot'], to_px(joints[part['joint']]), angle, px / part['scale'], size)))

    neck = to_px(joints['neck'])
    head = b['head']

    def head_layer(m, name, attach, layer, color=None):
        img, part = open_part(m, name)
        img = tint(img, color)
        target = rig.head_to_body(body, rig.ANCHORS[attach], (0, 0))
        target = (neck[0] + target[0] * px, neck[1] + target[1] * px)
        layers.append((HEAD_BASE + layer, affine(img, part['pivot'], target, 0, px / part['scale'], size)))

    hw_style = None
    if headwear:
        m = manifest(body, 'headwear', headwear)
        hw_style = m['parts']['headwear'].get('style')
        head_layer(m, 'headwear', 'neck', rig.HEAD_LAYERS['headwear'])
    if face:
        set_id, n = face.split(':')
        head_layer(manifest(body, 'face', set_id), f'face-{n}', 'neck', rig.HEAD_LAYERS['face'], skin)
    for kind, choice, attach in (('eyes', eyes, 'eye'), ('brows', brows, 'brow'), ('mouth', mouth, 'mouth')):
        if not choice:
            continue
        bits = choice.split(':') + ['calm']
        set_id, n, expr = bits[0], bits[1], bits[2]
        m = manifest(body, kind, set_id)
        name = f'{kind}-{n}-{expr}' if f'{kind}-{n}-{expr}' in m['parts'] else f'{kind}-{n}-calm'
        head_layer(m, name, attach, rig.HEAD_LAYERS[kind], hair_color if kind == 'brows' else None)
    if hair:
        m = manifest(body, 'hair', hair)
        name = 'hair-under-hat' if hw_style == 'hat' and 'hair-under-hat' in m['parts'] else 'hair-normal'
        head_layer(m, name, 'neck', rig.HEAD_LAYERS['hair'], hair_color)

    canvas = Image.new('RGBA', size, (0, 0, 0, 0))
    for _, im in sorted(layers, key=lambda x: x[0]):
        canvas.alpha_composite(im)
    return canvas


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--body', choices=sorted(rig.BODIES), required=True)
    for k in ('outfit', 'headwear', 'face', 'eyes', 'brows', 'mouth', 'hair', 'skin', 'hair-color', 'pose'):
        ap.add_argument('--' + k)
    ap.add_argument('--bg', default='#e9dcc0', help='backdrop colour (screen paper); "none" for transparent')
    ap.add_argument('--out', default='preview.png')
    a = ap.parse_args()
    img = compose(a.body, a.outfit, a.headwear, a.face, a.eyes, a.brows, a.mouth, a.hair, a.skin, a.hair_color,
                  json.loads(a.pose) if a.pose else None)
    if a.bg != 'none':
        bg = Image.new('RGBA', img.size, a.bg)
        bg.alpha_composite(img)
        img = bg
    img.save(a.out)
    print('wrote', a.out)


if __name__ == '__main__':
    main()
