"""Render the art templates (人台 / 拆件纸样 / 头部模板) and their spec.json.

    python prototypes/shadow-puppets/pipeline/make_templates.py

Outputs prototypes/shadow-puppets/templates/<body>/*.png and templates/spec.json.
The PNGs are references to upload to the image model together with the prompts in
ART_PIPELINE.md; spec.json is what cut_sheet.py / assemble.py use.
"""
import json
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

import rig

OUT = Path(__file__).resolve().parents[1] / 'templates'
FONT = '/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'
BG = (246, 243, 236, 255)
FILL = (216, 212, 204, 255)
EDGE = (150, 144, 134, 255)
PIVOT = (224, 33, 138, 255)     # magenta: the joint this part rotates about
TIP = (31, 163, 196, 255)       # cyan: the next joint (bone end) / child joints
GUIDE = (170, 164, 154, 255)
LABEL = (120, 114, 104, 255)


def font(size):
    try:
        return ImageFont.truetype(FONT, size)
    except OSError:
        return ImageFont.load_default()


def dot(d, p, color, r=7):
    d.ellipse((p[0] - r, p[1] - r, p[0] + r, p[1] + r), outline=color, width=3)
    d.line((p[0] - r - 5, p[1], p[0] + r + 5, p[1]), fill=color, width=1)
    d.line((p[0], p[1] - r - 5, p[0], p[1] + r + 5), fill=color, width=1)


def dashed_rect(d, r, color, dash=8):
    x, y, w, h = r
    for a, b in (((x, y), (x + w, y)), ((x + w, y), (x + w, y + h)), ((x + w, y + h), (x, y + h)), ((x, y + h), (x, y))):
        length = math.dist(a, b)
        for s in range(0, int(length), dash * 2):
            t0, t1 = s / length, min(1, (s + dash) / length)
            d.line((a[0] + (b[0] - a[0]) * t0, a[1] + (b[1] - a[1]) * t0, a[0] + (b[0] - a[0]) * t1, a[1] + (b[1] - a[1]) * t1), fill=color, width=1)


def capsule(d, a, b, wa, wb, fill=FILL, edge=EDGE):
    """Tapered limb from a (width wa) to b (width wb), rounded ends (the overlap caps)."""
    ang = math.atan2(b[1] - a[1], b[0] - a[0])
    nx, ny = -math.sin(ang), math.cos(ang)
    poly = [(a[0] + nx * wa / 2, a[1] + ny * wa / 2), (b[0] + nx * wb / 2, b[1] + ny * wb / 2),
            (b[0] - nx * wb / 2, b[1] - ny * wb / 2), (a[0] - nx * wa / 2, a[1] - ny * wa / 2)]
    d.polygon(poly, fill=fill)
    d.ellipse((a[0] - wa / 2, a[1] - wa / 2, a[0] + wa / 2, a[1] + wa / 2), fill=fill)
    d.ellipse((b[0] - wb / 2, b[1] - wb / 2, b[0] + wb / 2, b[1] + wb / 2), fill=fill)
    d.line([poly[0], poly[1]], fill=edge, width=2)
    d.line([poly[2], poly[3]], fill=edge, width=2)


def head_polygon(origin_px, scale_px):
    """Face guide outline: head space → pixels, origin_px = pixel position of the neck anchor."""
    n = rig.ANCHORS['neck']
    return [(origin_px[0] + (x - n[0]) * scale_px, origin_px[1] + (y - n[1]) * scale_px) for x, y in rig.smooth(rig.FACE_OUTLINE)]


def head_point(origin_px, scale_px, hp):
    n = rig.ANCHORS['neck']
    return (origin_px[0] + (hp[0] - n[0]) * scale_px, origin_px[1] + (hp[1] - n[1]) * scale_px)


def foot_poly(ankle, s, length=rig.FOOT_LENGTH):
    x, y = ankle
    return [(x - 6 * s, y - 4 * s), (x + 5 * s, y - 4 * s), (x + 9 * s, y + 7 * s), (x + length * s, y + 11 * s),
            (x + length * s, y + 15 * s), (x - 8 * s, y + 15 * s)]


def hand_poly(wrist, s, angle=0, length=rig.HAND_LENGTH):
    pts = [(-6, 0), (6, 0), (8, length * .45), (6, length), (-1, length * 1.02), (-7, length * .6)]
    out = []
    for p in pts:
        q = rig.rotate((p[0] * s, p[1] * s), angle)
        out.append((wrist[0] + q[0], wrist[1] + q[1]))
    return out


# ------------------------------------------------------------------ 人台 (dressed-on reference)
def body_template(body):
    b = rig.BODIES[body]
    W, H = 1024, 1536
    s = 2.8
    p, ang = rig.pose(body)
    origin = (500, 795)
    px = lambda u: (origin[0] + u[0] * s, origin[1] + u[1] * s)
    img = Image.new('RGBA', (W, H), BG)
    d = ImageDraw.Draw(img)
    w = b['limb_width']
    # far side first (drawn slightly darker), then torso, head, near side
    far_fill, far_edge = (200, 196, 188, 255), (140, 134, 124, 255)
    capsule(d, px(p['hipFar']), px(p['kneeFar']), w['thigh'][0] * s, w['thigh'][1] * s, far_fill, far_edge)
    capsule(d, px(p['kneeFar']), px(p['ankleFar']), w['shin'][0] * s, w['shin'][1] * s, far_fill, far_edge)
    d.polygon(foot_poly(px(p['ankleFar']), s), fill=far_fill, outline=far_edge)
    capsule(d, px(p['shoulderFar']), px(p['elbowFar']), w['upper'][0] * s, w['upper'][1] * s, far_fill, far_edge)
    capsule(d, px(p['elbowFar']), px(p['wristFar']), w['forearm'][0] * s, w['forearm'][1] * s, far_fill, far_edge)
    d.polygon(hand_poly(px(p['wristFar']), s, ang['forearmFar']), fill=far_fill, outline=far_edge)
    d.polygon([px(q) for q in b['torso']], fill=FILL, outline=EDGE)
    neck_px = px(p['neck'])
    d.polygon(head_polygon(neck_px, b['head'] * s), fill=FILL, outline=EDGE)
    capsule(d, px(p['hipNear']), px(p['kneeNear']), w['thigh'][0] * s, w['thigh'][1] * s)
    capsule(d, px(p['kneeNear']), px(p['ankleNear']), w['shin'][0] * s, w['shin'][1] * s)
    d.polygon(foot_poly(px(p['ankleNear']), s), fill=FILL, outline=EDGE)
    capsule(d, px(p['shoulderNear']), px(p['elbowNear']), w['upper'][0] * s, w['upper'][1] * s)
    capsule(d, px(p['elbowNear']), px(p['wristNear']), w['forearm'][0] * s, w['forearm'][1] * s)
    d.polygon(hand_poly(px(p['wristNear']), s, ang['forearmNear']), fill=FILL, outline=EDGE)
    for name, pt in p.items():
        dot(d, px(pt), PIVOT, 6)
    for key in ('eye', 'brow', 'mouth', 'ear', 'crown'):
        dot(d, head_point(neck_px, b['head'] * s, rig.ANCHORS[key]), TIP, 4)
    f = font(22)
    d.text((40, 40), f'{b["label"]} · 人台（纯侧面朝右，静立）', font=font(30), fill=LABEL)
    d.text((40, 82), '洋红圈 = 关节；青色点 = 头部锚点（冠、眉、眼、嘴、耳）。仅供参考，成图中不要出现。', font=f, fill=LABEL)
    return img


# ------------------------------------------------------------------ 套装拆件纸样
def outfit_template(body):
    b = rig.BODIES[body]
    s = rig.OUTFIT_SCALE
    img = Image.new('RGBA', (1024, 1536), BG)
    d = ImageDraw.Draw(img)
    cells = rig.outfit_cells()
    spec = {}
    w = b['limb_width']
    p0, _ = rig.pose(body)
    for sid, c in cells.items():
        x, y, cw, ch = c['rect']
        dashed_rect(d, (x + 6, y + 6, cw - 12, ch - 12), GUIDE)
        d.text((x + 12, y + 10), c['name'], font=font(16), fill=LABEL)
        cx = x + cw / 2
        entry = {'rect': c['rect'], 'kind': c['kind'], 'layer': c.get('layer'), 'scale': s}
        if sid == 'torso':
            ts = rig.TORSO_SCALE   # the torso is long: drawn a bit smaller so collar and jacket hem fit the cell
            entry['scale'] = ts
            neck = (cx, y + 40)
            hip = (cx, y + 40 + rig.L['spine'] * ts)
            poly = [(hip[0] + u[0] * ts, hip[1] + u[1] * ts) for u in b['torso']]
            d.polygon(poly, fill=FILL, outline=EDGE)
            sh = {k: (hip[0] + v[0] * ts, hip[1] + v[1] * ts) for k, v in b['shoulders'].items()}
            for v in sh.values():
                dot(d, v, TIP, 5)
            dot(d, neck, TIP)
            dot(d, hip, PIVOT)
            entry.update(bone='torso', pivot=hip, tip=neck, shoulderNear=sh['Near'], shoulderFar=sh['Far'])
        elif c['kind'] == 'bone':
            bone = c['target'][:-4] if c['target'].endswith('Near') else c['target'][:-3]
            length = rig.BONE_LENGTH[bone] * s
            a, tip = (cx, y + 52), (cx, y + 52 + length)
            wa, wb = w[bone][0] * s, w[bone][1] * s
            capsule(d, a, tip, wa, wb)
            dot(d, a, PIVOT)
            dot(d, tip, TIP)
            entry.update(bone=c['target'], pivot=a, tip=tip)
        elif sid in ('front-skirt', 'back-skirt'):
            top = (cx, y + 46)
            drop = rig.SKIRT_DROP * s
            d.polygon([(cx - 26 * s, top[1] - 4), (cx + 26 * s, top[1] - 4), (cx + 36 * s, top[1] + drop), (cx - 36 * s, top[1] + drop)], fill=FILL, outline=EDGE)
            dot(d, top, PIVOT)
            entry.update(joint='hip', pivot=top, height=rig.SKIRT_DROP)
        elif sid == 'waist':
            hip = (cx, y + 150)
            d.rectangle((cx - 30 * s, hip[1] - 9 * s, cx + 30 * s, hip[1] + 3 * s), fill=FILL, outline=EDGE)
            dot(d, hip, PIVOT)
            entry.update(joint='hip', pivot=hip, height=None)
        elif sid.endswith('hand'):
            wrist = (cx, y + 90)
            d.polygon(hand_poly(wrist, s * 1.0), fill=FILL, outline=EDGE)
            dot(d, wrist, PIVOT)
            entry.update(joint=c['target'], pivot=wrist, height=rig.HAND_LENGTH, follow=('forearmNear' if 'near' in sid else 'forearmFar'))
        elif sid.endswith('foot'):
            ankle = (cx - 20, y + 120)
            d.polygon(foot_poly(ankle, s), fill=FILL, outline=EDGE)
            dot(d, ankle, PIVOT)
            entry.update(joint=c['target'], pivot=ankle, height=rig.FOOT_LENGTH)
        else:  # accessories
            entry.update(note='兵器、披挂、背篓等独立配件横放此行，每件完整、互不重叠，挂点在 assemble/bake 时按名称指定')
        spec[sid] = {k: ([round(v[0], 2), round(v[1], 2)] if isinstance(v, tuple) else v) for k, v in entry.items()}
    return img, spec


# ------------------------------------------------------------------ head sheets
def head_cell(d, origin, scale, labels=True, fill=FILL, show=('eye', 'brow', 'mouth', 'ear', 'crown', 'neck')):
    d.polygon(head_polygon(origin, scale), fill=fill, outline=EDGE)
    names = {'eye': '眼', 'brow': '眉', 'mouth': '嘴', 'ear': '耳', 'crown': '冠', 'neck': '颈', 'earring': '坠', 'brow_band': '额'}
    pts = {}
    for k in show:
        q = head_point(origin, scale, rig.ANCHORS[k])
        pts[k] = q
        dot(d, q, PIVOT if k == 'neck' else TIP, 6)
        if labels:
            d.text((q[0] + 10, q[1] - 22), names[k], font=font(18), fill=LABEL)
    return pts


def face_template(body):
    b = rig.BODIES[body]
    scale = b['head'] * rig.HEAD_SCALE
    img = Image.new('RGBA', (1536, 1024), BG)
    d = ImageDraw.Draw(img)
    cells = []
    for i in range(6):
        col, row = i % 3, i // 3
        x, y = col * 512, row * 512
        dashed_rect(d, (x + 6, y + 6, 500, 500), GUIDE)
        d.text((x + 14, y + 12), f'脸型 {i + 1}', font=font(18), fill=LABEL)
        origin = (x + 250, y + 470)
        pts = head_cell(d, origin, scale)
        cells.append({'type': i + 1, 'rect': [x, y, 512, 512], 'neck': [round(origin[0], 2), round(origin[1], 2)],
                      'anchors': {k: [round(v[0], 2), round(v[1], 2)] for k, v in pts.items()}, 'scale': rig.HEAD_SCALE})
    return img, cells


def feature_template(body, kind):
    b = rig.BODIES[body]
    scale = b['head'] * rig.FEATURE_SCALE
    anchor_key = {'eyes': 'eye', 'brows': 'brow', 'mouth': 'mouth'}[kind]
    img = Image.new('RGBA', (1536, 1024), BG)
    cells = rig.feature_cells(kind)
    expr_names = {'calm': '平静', 'angry': '怒', 'sad': '悲'}
    for c in cells:
        x, y, cw, ch = c['rect']
        cell = Image.new('RGBA', (cw, ch), BG)
        cd = ImageDraw.Draw(cell)
        # face fragment positioned so the feature anchor sits at the cell centre
        a = rig.ANCHORS[anchor_key]
        n = rig.ANCHORS['neck']
        origin = (cw / 2 - (a[0] - n[0]) * scale, ch / 2 - (a[1] - n[1]) * scale)
        cd.polygon(head_polygon(origin, scale), fill=(228, 224, 216, 255), outline=EDGE)
        dot(cd, (cw / 2, ch / 2), TIP, 8)
        dashed_rect(cd, (4, 4, cw - 8, ch - 8), GUIDE)
        cd.text((10, 8), f'{c["type"]} · {expr_names[c["expression"]]}', font=font(16), fill=LABEL)
        img.alpha_composite(cell, (x, y))
        c['scale'] = rig.FEATURE_SCALE
    return img, cells


def hair_template(body):
    b = rig.BODIES[body]
    scale = b['head'] * rig.HEAD_SCALE
    img = Image.new('RGBA', (1536, 1024), BG)
    d = ImageDraw.Draw(img)
    cells = []
    for i, label in enumerate(('常规发型', '压发版（戴帽巾时用）')):
        x = i * 768
        dashed_rect(d, (x + 6, 6, 756, 1012), GUIDE)
        d.text((x + 16, 14), label, font=font(20), fill=LABEL)
        origin = (x + 384, 70 + 1.30 * scale)
        pts = head_cell(d, origin, scale, show=('crown', 'ear', 'brow', 'neck'))
        cells.append({'variant': 'normal' if i == 0 else 'under-hat', 'rect': [x, 0, 768, 1024], 'neck': [round(origin[0], 2), round(origin[1], 2)], 'scale': rig.HEAD_SCALE})
    return img, cells


def headwear_template(body):
    b = rig.BODIES[body]
    scale = b['head'] * rig.HEAD_SCALE
    img = Image.new('RGBA', (1024, 1024), BG)
    d = ImageDraw.Draw(img)
    dashed_rect(d, (6, 6, 1012, 1012), GUIDE)
    d.text((16, 14), '头饰（冠、帽、巾、簪、抹额等，随套装）', font=font(20), fill=LABEL)
    origin = (500, 380 + 1.30 * scale)
    head_cell(d, origin, scale, show=('crown', 'brow_band', 'ear', 'earring', 'neck'))
    # dashed outline of a typical bun so crowns can be placed against it
    crown = head_point(origin, scale, rig.ANCHORS['crown'])
    bun = (crown[0] - 70, crown[1] - 95, crown[0] + 40, crown[1] + 10)
    d.ellipse(bun, outline=GUIDE, width=2)
    d.text((bun[2] + 8, bun[1]), '常见发髻位置', font=font(16), fill=LABEL)
    return img, {'rect': [0, 0, 1024, 1024], 'neck': [round(origin[0], 2), round(origin[1], 2)], 'scale': rig.HEAD_SCALE}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    spec = {'version': 1, 'units': 'paper-rig units (hip origin, y down, facing right)', 'rig': 'unity/paper-rig.json',
            'scales': {'outfit': rig.OUTFIT_SCALE, 'head': rig.HEAD_SCALE, 'feature': rig.FEATURE_SCALE},
            'head_anchors': rig.ANCHORS, 'head_layers': rig.HEAD_LAYERS, 'bodies': {}}
    for body in rig.BODIES:
        folder = OUT / body
        folder.mkdir(exist_ok=True)
        body_template(body).save(folder / 'body.png')
        outfit_img, outfit = outfit_template(body)
        outfit_img.save(folder / 'outfit-sheet.png')
        face_img, faces = face_template(body)
        face_img.save(folder / 'face-sheet.png')
        sheets = {'outfit': outfit, 'face': faces}
        for kind in ('eyes', 'brows', 'mouth'):
            im, cells = feature_template(body, kind)
            im.save(folder / f'{kind}-sheet.png')
            sheets[kind] = cells
        hair_img, hair = hair_template(body)
        hair_img.save(folder / 'hair-sheet.png')
        hw_img, hw = headwear_template(body)
        hw_img.save(folder / 'headwear-sheet.png')
        sheets['hair'], sheets['headwear'] = hair, hw
        b = rig.BODIES[body]
        spec['bodies'][body] = {'label': b['label'], 'head': b['head'], 'shoulders': b['shoulders'], 'sheets': sheets}
        print('templates', body, sorted(p.name for p in folder.glob('*.png')))
    (OUT / 'spec.json').write_text(json.dumps(spec, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
