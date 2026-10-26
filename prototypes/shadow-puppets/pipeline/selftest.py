"""End-to-end check without real art: paint fake sheets on the templates, cut, assemble.

    python selftest.py [out_dir]

Fake sheets are opaque on paper colour (exercises background keying), costume parts are
drawn larger than the guides (like real sleeves), and template markers are left in to
make sure the cutter strips them. Prints the bounding-box checks and writes previews.
"""
import json
import os
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

import rig

TEMPLATES = Path(__file__).resolve().parent.parent / 'templates'


def fake_from_guides(template, colors, grow=6):
    """Recolour the grey guide shapes of a template, grown by `grow` px, on flat paper."""
    a = np.asarray(Image.open(template).convert('RGB')).astype(int)
    guide = (np.abs(a - [216, 212, 204]).sum(axis=2) < 12) | (np.abs(a - [228, 224, 216]).sum(axis=2) < 12)
    labels, n = ndimage.label(guide)                       # drop antialiased label text, fill marker holes
    if n:
        areas = ndimage.sum(guide, labels, range(1, n + 1))
        guide = np.isin(labels, [i + 1 for i, a in enumerate(areas) if a > 300])
    guide = ndimage.binary_fill_holes(ndimage.binary_closing(guide, iterations=6))
    if grow:
        guide = ndimage.binary_dilation(guide, iterations=grow)
    labels, n = ndimage.label(guide)
    out = np.full_like(a, [244, 240, 230])
    for i in range(1, n + 1):
        out[labels == i] = colors[(i - 1) % len(colors)]
    keep = (np.abs(a - [224, 33, 138]).sum(axis=2) < 30)   # leave a few markers in on purpose
    out[keep] = [224, 33, 138]
    return Image.fromarray(out.astype(np.uint8))


def run(out_dir):
    out_dir = Path(out_dir)
    lib = out_dir / 'library'
    os.environ['PUPPET_LIBRARY'] = str(lib)
    import cut_sheet
    import assemble
    cut_sheet.LIBRARY = assemble.LIBRARY = lib
    spec = json.loads((TEMPLATES / 'spec.json').read_text(encoding='utf-8'))
    results = {}
    for body in rig.BODIES:
        t = TEMPLATES / body
        sheets = spec['bodies'][body]['sheets']
        robe = [(52, 96, 112), (178, 62, 58), (226, 196, 120), (62, 70, 120)]
        fake_from_guides(t / 'outfit-sheet.png', robe, grow=7).save(out_dir / f'{body}-outfit.png')
        # face sheet: neutral mid-grey skin (dyed by the engine)
        fake_from_guides(t / 'face-sheet.png', [(200, 200, 200)], grow=0).save(out_dir / f'{body}-face.png')
        for kind, color, shape in (('eyes', (30, 24, 28), 'eye'), ('brows', (180, 180, 180), 'brow'), ('mouth', (190, 60, 70), 'mouth')):
            im = Image.new('RGB', (1536, 1024), (244, 240, 230))
            d = ImageDraw.Draw(im)
            for c in sheets[kind]:
                x, y = c['anchor']
                k = {'calm': 0, 'angry': -6, 'sad': 6}[c['expression']]
                if shape == 'eye':
                    d.polygon([(x - 26, y + k), (x, y - 10), (x + 22, y - 2), (x + 4, y + 9)], fill=color)
                elif shape == 'brow':
                    d.line([(x - 34, y + 6 - k), (x - 4, y - 6), (x + 26, y + k)], fill=color, width=6)
                else:
                    d.polygon([(x - 12, y), (x + 4, y - 5), (x + 10, y + 1), (x + 2, y + 6)], fill=color)
            im.save(out_dir / f'{body}-{kind}.png')
        # hair: a grey (dyeable) cap + bun over the skull, two variants
        im = Image.new('RGB', (1536, 1024), (244, 240, 230))
        d = ImageDraw.Draw(im)
        for c in sheets['hair']:
            nx, ny = c['neck']
            sc = rig.BODIES[body]['head'] * c['scale']
            P = lambda hx, hy: (nx + (hx - rig.ANCHORS['neck'][0]) * sc, ny + (hy - rig.ANCHORS['neck'][1]) * sc)
            cap = [P(*q) for q in [(-0.10, -1.04), (0.20, -0.96), (0.33, -0.70), (0.18, -0.78), (0.0, -0.72), (-0.06, -0.50), (-0.30, -0.30), (-0.46, -0.10), (-0.44, -0.62), (-0.30, -0.92)]]
            d.polygon(cap, fill=(150, 150, 150))
            if c['variant'] == 'normal':
                bx, by = P(-0.12, -1.10)
                d.ellipse((bx - 45, by - 50, bx + 45, by + 40), fill=(150, 150, 150))
                d.polygon([P(-0.40, -0.40), P(-0.30, -0.30), P(-0.36, 0.60), P(-0.50, 0.55)], fill=(150, 150, 150))
        im.save(out_dir / f'{body}-hair.png')
        # headwear: gold crown on the bun
        im = Image.new('RGB', (1024, 1024), (244, 240, 230))
        d = ImageDraw.Draw(im)
        nx, ny = sheets['headwear']['neck']
        sc = rig.BODIES[body]['head'] * sheets['headwear']['scale']
        cx, cy = nx + (rig.ANCHORS['crown'][0] - rig.ANCHORS['neck'][0]) * sc, ny + (rig.ANCHORS['crown'][1] - rig.ANCHORS['neck'][1]) * sc
        d.polygon([(cx - 60, cy - 30), (cx - 30, cy - 110), (cx, cy - 70), (cx + 30, cy - 120), (cx + 50, cy - 30)], fill=(214, 168, 64))
        d.line([(cx + 30, cy - 80), (cx + 140, cy - 60)], fill=(214, 168, 64), width=8)
        im.save(out_dir / f'{body}-headwear.png')

        cut_sheet.cut(out_dir / f'{body}-outfit.png', body, 'outfit', 'test')
        cut_sheet.cut(out_dir / f'{body}-headwear.png', body, 'headwear', 'test')
        for kind in ('face', 'eyes', 'brows', 'mouth', 'hair'):
            cut_sheet.cut(out_dir / f'{body}-{kind}.png', body, kind, 'test')
        m = json.loads((lib / body / 'outfit' / 'test' / 'manifest.json').read_text(encoding='utf-8'))
        assert len([p for p in m['parts'] if not p.startswith('acc')]) == 16, m['parts'].keys()
        for name, part in m['parts'].items():
            img = Image.open(lib / body / 'outfit' / 'test' / part['file'])
            assert np.asarray(img)[:, :, 3].max() == 255
            assert not (np.abs(np.asarray(img)[:, :, :3].astype(int) - [224, 33, 138]).sum(axis=2) < 30)[np.asarray(img)[:, :, 3] > 0].any(), name
            px, py = part['pivot']
            assert -2 <= px <= img.width + 2 and -2 <= py <= img.height + 2, (name, part['pivot'], img.size)
        for expr in ('calm', 'angry'):
            img = assemble.compose(body, 'test', 'test', 'test:2', f'test:3:{expr}', 'test:3', 'test:1', 'test',
                                   skin='#f0d8c4', hair_color='#2a2230')
            bg = Image.new('RGBA', img.size, '#e9dcc0')
            bg.alpha_composite(img)
            bg.save(out_dir / f'{body}-preview-{expr}.png')
        pose = assemble.compose(body, 'test', 'test', 'test:4', 'test:1', 'test:1', 'test:1', 'test', skin='#e8c9ae',
                                hair_color='#3b2a20', pose_values={'near_upper': -70, 'near_elbow': -40, 'far_thigh': 30, 'near_thigh': -25})
        bg = Image.new('RGBA', pose.size, '#e9dcc0')
        bg.alpha_composite(pose)
        bg.save(out_dir / f'{body}-preview-pose.png')
        # the baked skin, drawn with the runtime's placement rules, must match the assembler
        import bake_library
        skin, atlas = bake_library.bake(f'test-{body}', body, out_dir / 'build', outfit='test', headwear='test', face='test:2',
                                        eyes='test:3', brows='test:3', mouth='test:1', hair='test', skin_color='#f0d8c4', hair_color='#2a2230')
        assert len(skin['expressions']) == 6, skin['expressions'].keys()
        ref = np.asarray(assemble.compose(body, 'test', 'test', 'test:2', 'test:3:calm', 'test:3', 'test:1', 'test', skin='#f0d8c4', hair_color='#2a2230'))[:, :, 3] > 128
        got = np.asarray(bake_library.render_like_runtime(skin, atlas, body))[:, :, 3] > 128
        iou = (ref & got).sum() / max(1, (ref | got).sum())
        assert iou > .98, iou
        results[body] = sorted(m['parts'])
        results[body + '_runtime_iou'] = round(float(iou), 4)
    print(json.dumps({'ok': True, 'checks': {k: (len(v) if isinstance(v, list) else v) for k, v in results.items()}, 'out': str(out_dir)}, ensure_ascii=False))


if __name__ == '__main__':
    run(sys.argv[1] if len(sys.argv) > 1 else tempfile.mkdtemp(prefix='puppet-selftest-'))
