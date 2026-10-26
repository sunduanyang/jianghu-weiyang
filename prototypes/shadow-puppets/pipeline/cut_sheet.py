"""Cut a finished art sheet (drawn on one of the templates) into parts with known pivots.

    python cut_sheet.py <sheet.png> --body female --kind outfit   --id hanfu-qing
    python cut_sheet.py <sheet.png> --body female --kind headwear --id hanfu-qing --hat
    python cut_sheet.py <sheet.png> --body female --kind face     --id set1
    python cut_sheet.py <sheet.png> --body female --kind eyes     --id set1   (also brows / mouth)
    python cut_sheet.py <sheet.png> --body female --kind hair     --id long-bun

Writes library/<body>/<kind>/<id>/*.png + manifest.json. Every part records
`scale` (px per rig unit) and `pivot` (px in the cut PNG), so no drawn rivet is needed.
The sheet may be transparent or on a flat light background (it is keyed out); it is
resized to the template size first, so the image model must keep the template framing.
"""
import argparse
import json
import math
import os
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

import rig

HERE = Path(__file__).resolve().parent
SPEC = HERE.parent / 'templates' / 'spec.json'
LIBRARY = Path(os.environ.get('PUPPET_LIBRARY', HERE.parent / 'library'))
GUIDE_COLORS = [(224, 33, 138), (31, 163, 196)]
GUIDE_HEAD_COLORS = [(216, 212, 204), (228, 224, 216)]
SHEET_SIZE = {'outfit': (1024, 1536), 'headwear': (1024, 1024), 'face': (1536, 1024), 'eyes': (1536, 1024),
              'brows': (1536, 1024), 'mouth': (1536, 1024), 'hair': (1536, 1024)}


def load_sheet(path, kind):
    im = Image.open(path).convert('RGBA')
    size = SHEET_SIZE[kind]
    if abs(im.width / im.height - size[0] / size[1]) > .02:
        raise SystemExit(f'{path}: aspect {im.width}x{im.height} does not match template {size[0]}x{size[1]} — regenerate with the template framing')
    if im.size != size:
        im = im.resize(size, Image.LANCZOS)
    a = np.asarray(im).astype(np.int16)
    alpha = a[:, :, 3].copy()
    if (alpha > 250).mean() > .97:          # opaque sheet → key out the flat paper background
        rgb = a[:, :, :3]
        border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
        bg = np.median(border, axis=0)
        dist = np.abs(rgb - bg).sum(axis=2)
        near = dist < 42
        labels, _ = ndimage.label(near)
        edge_labels = set(np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]]))) - {0}
        bgmask = np.isin(labels, list(edge_labels))
        alpha = np.where(bgmask, 0, 255).astype(np.int16)
        soft = (dist >= 42) & (dist < 70) & ndimage.binary_dilation(bgmask, iterations=1)
        alpha[soft] = ((dist[soft] - 42) * 255 / 28).astype(np.int16)
    for c in GUIDE_COLORS:                    # leftover template markers
        alpha[np.abs(a[:, :, :3] - np.array(c)).sum(axis=2) < 60] = 0
    if kind in ('eyes', 'brows', 'mouth', 'hair', 'headwear'):
        # the grey guide head must not end up inside a hair / feature layer
        for c in GUIDE_HEAD_COLORS:
            alpha[np.abs(a[:, :, :3] - np.array(c)).sum(axis=2) < 24] = 0
    a[:, :, 3] = alpha
    return Image.fromarray(a.astype(np.uint8))


LABEL_BAND = 40   # template cells print their name in the top band; leftovers there are not art


def drop_labels(alpha):
    labels, n = ndimage.label(alpha > 0)
    for i, sl in enumerate(ndimage.find_objects(labels), 1):
        if sl is not None and sl[0].stop <= LABEL_BAND:
            alpha[labels == i] = 0
    return alpha


def clean(cell, min_area=60):
    """Drop specks, kill near-transparent fringe, return (image, (x0, y0)) trimmed with padding."""
    a = np.asarray(cell).copy()
    alpha = a[:, :, 3]
    alpha[alpha < 16] = 0
    drop_labels(alpha)
    labels, n = ndimage.label(alpha > 0)
    if n:
        areas = ndimage.sum(np.ones_like(alpha), labels, range(1, n + 1))
        biggest = areas.max()
        for i, area in enumerate(areas, 1):
            if area < max(min_area, biggest * .002):
                alpha[labels == i] = 0
    a[:, :, 3] = alpha
    ys, xs = np.nonzero(alpha)
    if not len(xs):
        return None, None
    pad = 4
    x0, y0 = int(max(0, xs.min() - pad)), int(max(0, ys.min() - pad))
    x1, y1 = min(a.shape[1], xs.max() + pad + 1), min(a.shape[0], ys.max() + pad + 1)
    touches = xs.min() <= 1 or ys.min() <= 1 or xs.max() >= a.shape[1] - 2 or ys.max() >= a.shape[0] - 2
    return Image.fromarray(a[y0:y1, x0:x1]), (x0, y0, bool(touches))


def local(p, rect, origin):
    return [round(float(p[0] - rect[0] - origin[0]), 2), round(float(p[1] - rect[1] - origin[1]), 2)]


def save_part(out, name, cell, rect, points, meta, warnings):
    img, info = clean(cell)
    if img is None:
        return None
    x0, y0, touches = info
    if touches:
        warnings.append(f'{name}: 画面碰到格子边缘，可能被截断')
    img.save(out / f'{name}.png', optimize=True)
    entry = dict(meta, file=f'{name}.png', size=list(img.size))
    for key, p in points.items():
        entry[key] = local(p, rect, (x0, y0))
    return entry


def cut(sheet_path, body, kind, set_id, hat=False):
    spec = json.loads(SPEC.read_text(encoding='utf-8'))['bodies'][body]['sheets']
    sheet = load_sheet(sheet_path, kind)
    out = LIBRARY / body / kind / set_id
    out.mkdir(parents=True, exist_ok=True)
    for old in out.glob('*.png'):
        old.unlink()
    parts, warnings = {}, []

    def crop(rect):
        x, y, w, h = rect
        return sheet.crop((x, y, x + w, y + h))

    if kind == 'outfit':
        for sid, c in spec['outfit'].items():
            if sid == 'accessories':
                continue
            pts = {k: c[k] for k in ('pivot', 'tip', 'shoulderNear', 'shoulderFar') if k in c}
            meta = {k: c[k] for k in ('kind', 'layer', 'bone', 'follow') if c.get(k)}
            if c['kind'] == 'joint':
                meta['joint'] = {'waist': 'hip', 'front-skirt': 'hip', 'back-skirt': 'hip'}.get(sid) or rig.outfit_cells()[sid]['target']
            meta['scale'] = c['scale']
            e = save_part(out, sid, crop(c['rect']), c['rect'], pts, meta, warnings)
            if e:
                parts[sid] = e
            elif sid not in ('waist',):
                warnings.append(f'{sid}: 格子是空的')
        # accessories: every separate blob in the bottom row is one prop
        acc = spec['outfit']['accessories']['rect']
        region = drop_labels(np.asarray(crop(acc))[:, :, 3].copy()) > 16
        labels, n = ndimage.label(ndimage.binary_dilation(region, iterations=6))
        for i, sl in enumerate(ndimage.find_objects(labels), 1):
            ys, xs = sl
            if (xs.stop - xs.start) * (ys.stop - ys.start) < 900:
                continue
            r = [int(acc[0] + xs.start), int(acc[1] + ys.start), int(xs.stop - xs.start), int(ys.stop - ys.start)]
            mask = (labels[sl] == i)
            piece = np.asarray(crop(r)).copy()
            piece[:, :, 3] = piece[:, :, 3] * mask
            name = f'acc-{len([k for k in parts if k.startswith("acc-")]) + 1}'
            e = save_part(out, name, Image.fromarray(piece), r, {'center': (r[0] + r[2] / 2, r[1] + r[3] / 2)},
                          {'kind': 'accessory', 'scale': rig.OUTFIT_SCALE, 'joint': None, 'note': '挂点在 bake 时按名称指定'}, warnings)
            if e:
                parts[name] = e
    elif kind == 'face':
        for c in spec['face']:
            name = f'face-{c["type"]}'
            e = save_part(out, name, crop(c['rect']), c['rect'], {'pivot': c['neck']},
                          {'kind': 'head', 'attach': 'neck', 'layer': rig.HEAD_LAYERS['face'], 'scale': c['scale']}, warnings)
            if e:
                parts[name] = e
    elif kind in ('eyes', 'brows', 'mouth'):
        attach = {'eyes': 'eye', 'brows': 'brow', 'mouth': 'mouth'}[kind]
        for c in spec[kind]:
            name = f'{kind}-{c["type"]}-{c["expression"]}'
            e = save_part(out, name, crop(c['rect']), c['rect'], {'pivot': c['anchor']},
                          {'kind': 'head', 'attach': attach, 'type': c['type'], 'expression': c['expression'],
                           'layer': rig.HEAD_LAYERS[kind], 'scale': c['scale']}, warnings)
            if e:
                parts[name] = e
    elif kind == 'hair':
        for c in spec['hair']:
            name = f'hair-{c["variant"]}'
            e = save_part(out, name, crop(c['rect']), c['rect'], {'pivot': c['neck']},
                          {'kind': 'head', 'attach': 'neck', 'variant': c['variant'], 'layer': rig.HEAD_LAYERS['hair'], 'scale': c['scale']}, warnings)
            if e:
                parts[name] = e
        if 'hair-normal' not in parts:
            warnings.append('常规发型格是空的')
    elif kind == 'headwear':
        c = spec['headwear']
        e = save_part(out, 'headwear', crop(c['rect']), c['rect'], {'pivot': c['neck']},
                      {'kind': 'head', 'attach': 'neck', 'style': 'hat' if hat else 'crown', 'layer': rig.HEAD_LAYERS['headwear'], 'scale': c['scale']}, warnings)
        if e:
            parts['headwear'] = e
    manifest = {'version': 1, 'body': body, 'kind': kind, 'id': set_id, 'source': Path(sheet_path).name,
                'units': 'pivot in px of the part PNG; scale = px per paper-rig unit', 'parts': parts, 'warnings': warnings}
    (out / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print(f'{kind}/{set_id}: {len(parts)} parts → {out}')
    for w in warnings:
        print('  ⚠', w)
    return manifest


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('sheet')
    ap.add_argument('--body', choices=sorted(rig.BODIES), required=True)
    ap.add_argument('--kind', choices=sorted(SHEET_SIZE), required=True)
    ap.add_argument('--id', required=True, help='set name, e.g. hanfu-qing')
    ap.add_argument('--hat', action='store_true', help='headwear covers the hair (帽巾类) → uses the hair under-hat variant')
    a = ap.parse_args()
    cut(a.sheet, a.body, a.kind, a.id, a.hat)


if __name__ == '__main__':
    main()
