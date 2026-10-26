"""Bind the three user supplied sheets without redrawing their pixels.

Crop rectangles and rivet centres are measured in the original 1024x1536
canvases. The assembled reference supplies proportions, not animation frames.
"""
import json
import math
from pathlib import Path
from PIL import Image

from bake_paper_cast import ROOT, DEST, digest, pack, texture_meta

SOURCE = ROOT / 'art/rig-reference'

# name, source rectangle, pivot, optional distal rivet (all source coordinates)
DRESSED = [
    ('head-neck', (0, 12, 330, 420), (205, 320), None),
    ('torso', (345, 40, 530, 421), (445, 295), (431, 68)),
    ('front-skirt', (527, 20, 790, 456), (658, 44), None),
    ('back-skirt', (790, 30, 1024, 455), (914, 62), None),
    ('near-upper-arm', (43, 465, 198, 768), (158, 500), (106, 737)),
    ('far-upper-arm', (345, 465, 497, 768), (389, 500), (429, 737)),
    ('near-forearm', (578, 464, 720, 766), (628, 499), (682, 723)),
    ('far-forearm', (821, 465, 951, 766), (899, 499), (894, 729)),
    ('near-hand', (74, 835, 176, 1036), (130, 865), None),
    ('far-hand', (329, 835, 438, 1036), (360, 866), None),
    ('near-thigh', (569, 767, 719, 1091), (629, 802), (666, 1058)),
    ('far-thigh', (824, 767, 965, 1091), (900, 802), (886, 1059)),
    ('near-shin', (65, 1070, 183, 1503), (133, 1101), (119, 1423)),
    ('far-shin', (337, 1068, 453, 1503), (388, 1101), (387, 1423)),
    ('near-foot', (533, 1263, 739, 1491), (592, 1292), None),
    ('far-foot', (810, 1263, 1012, 1491), (868, 1294), None),
]
BARE = [
    ('head-neck', (108, 68, 231, 370), (166, 345), None),
    ('torso', (442, 32, 584, 411), (513, 371), (509, 70)),
    ('pelvis', (791, 136, 920, 303), (855, 161), None),
    ('near-upper-arm', (91, 443, 163, 734), (119, 474), (140, 705)),
    ('far-upper-arm', (348, 443, 420, 734), (376, 474), (397, 705)),
    ('near-forearm', (610, 470, 670, 708), (640, 498), (644, 681)),
    ('far-forearm', (867, 470, 925, 708), (896, 498), (894, 681)),
    ('near-hand', (93, 874, 164, 1024), (117, 899), None),
    ('far-hand', (350, 874, 422, 1024), (374, 899), None),
    ('near-thigh', (591, 771, 690, 1131), (641, 803), (644, 1092)),
    ('far-thigh', (848, 771, 947, 1131), (898, 803), (898, 1092)),
    ('near-shin', (92, 1165, 164, 1497), (133, 1198), (129, 1464)),
    ('far-shin', (349, 1165, 421, 1497), (390, 1198), (386, 1464)),
    ('near-foot', (549, 1275, 733, 1385), (586, 1306), None),
    ('far-foot', (806, 1275, 990, 1385), (843, 1306), None),
]


def bake_reference(result):
    import numpy as np
    calibration = json.loads((SOURCE/'calibration.json').read_text(encoding='utf-8'))
    origin = np.array(calibration['origin'], float)
    joints = {k: np.array(v, float)-origin for k, v in calibration['landmarks'].items()}
    down_angle = lambda v: math.degrees(math.atan2(-v[0], v[1]))
    bind_lean = math.degrees(math.atan2(joints['neck'][0], -joints['neck'][1]))
    def unrotate(p):
        a = math.radians(-bind_lean)
        return [p[0]*math.cos(a)-p[1]*math.sin(a), p[0]*math.sin(a)+p[1]*math.cos(a)]
    for cid, file, rows, dressed in (
        ('reference-jade', 'dressed-parts.png', DRESSED, True),
        ('reference-body', 'body-parts.png', BARE, False),
    ):
        sheet = Image.open(SOURCE/file).convert('RGBA')
        images = {name: sheet.crop(rect) for name, rect, _, _ in rows}
        atlas, positions = pack(images)
        lengths = dict(spine=float(np.linalg.norm(joints['neck'])), upper_arm=215, forearm=198, thigh=449, shin=361)
        for side in ('Near', 'Far'):
            for kind, a, b in [('upper_arm', 'shoulder', 'elbow'), ('forearm', 'elbow', 'wrist'), ('thigh', 'hip', 'knee'), ('shin', 'knee', 'ankle')]:
                lengths[kind+side] = float(np.linalg.norm(joints[b+side]-joints[a+side]))
        skin = dict(id=cid, description='青玉女侠 · 用户参考素材' if dressed else '素身关节标定 · 用户参考素材',
                    atlas=cid, size=list(atlas.size), scale=1, lengths=lengths, reference=True,
                    canvas=calibration['canvas'], canvas_origin=calibration['origin'], bind_lean=bind_lean,
                    reference_landmarks={k: v.tolist() for k, v in joints.items()},
                    shoulders={side: unrotate(joints['shoulder'+side]) for side in ('Near', 'Far')},
                    hips={side: unrotate(joints['hip'+side]) for side in ('Near', 'Far')},
                    feet={side: joints['ankle'+side].tolist() for side in ('Near', 'Far')},
                    knee_poles={'Near': 1, 'Far': -1}, angle_offsets={}, leg_bind_angles={}, parts={})
        for side, prefix, rest_upper, rest_elbow in [('Near', 'near', -12, 8), ('Far', 'far', 10, -8)]:
            upper = down_angle(joints['elbow'+side]-joints['shoulder'+side])
            forearm = down_angle(joints['wrist'+side]-joints['elbow'+side])
            skin['angle_offsets'][prefix+'_upper'] = upper-bind_lean-rest_upper
            skin['angle_offsets'][prefix+'_elbow'] = forearm-upper-rest_elbow
            skin['leg_bind_angles'][side] = down_angle(joints['knee'+side]-joints['hip'+side])
        skin['head_angle_offset'] = -bind_lean
        skin['hand_angle_offsets'] = {'Near': -3, 'Far': 47.5}
        for name, rect, pivot, tip in rows:
            image = images[name]
            x, y = positions[name]
            local = lambda p: [p[0]-rect[0], p[1]-rect[1]]
            data = dict(size=list(image.size), pivot=local(pivot),
                        uv=[x/atlas.width, 1-(y+image.height)/atlas.height, image.width/atlas.width, image.height/atlas.height],
                        source='rig-reference/'+file, source_rect=list(rect), source_sha256=digest(SOURCE/file))
            near = name.startswith('near')
            side = 'Near' if near else 'Far'
            if name == 'torso':
                data.update(bone='torso', tip=local(tip), layer=40)
                # A fixed bind matrix calibrates width separately from length.
                # It never changes during animation and keeps both bone pins exact.
                a, b = data['pivot'], data['tip']
                if dressed:
                    se = np.array(b, float)-a; se /= np.linalg.norm(se)
                    sp = np.array([se[1], -se[0]])
                    te = joints['neck']/np.linalg.norm(joints['neck']); tp = np.array([te[1], -te[0]])
                    source_shoulder = np.array([377,137])-pivot
                    target_shoulder = joints['shoulderNear']
                    scale = lengths['spine']/math.dist(a,b)
                    across = float(source_shoulder@sp)
                    data['breadth'] = float(target_shoulder@tp)/across/scale
                    data['shear'] = (float(target_shoulder@te)/scale-float(source_shoulder@se))/across
            elif tip is not None:
                kind = next(k for k in ('upper-arm', 'forearm', 'thigh', 'shin') if name.endswith(k))
                bone = ('upper' if kind == 'upper-arm' else kind)+side
                data.update(bone=bone, tip=local(tip), layer={'upperFar': 10, 'forearmFar': 12, 'thighFar': 16, 'shinFar': 17, 'thighNear': 25, 'shinNear': 26, 'upperNear': 72, 'forearmNear': 75}[bone])
                data['breadth'] = {'upperNear': .8, 'upperFar': .7, 'forearmNear': 1, 'forearmFar': 1.1, 'thighNear': .48, 'thighFar': .49, 'shinNear': .85, 'shinFar': .85}[bone] if dressed else .7
            elif name == 'head-neck':
                data.update(joint='neck', height=300, breadth=1.13 if dressed else 1, angle=0, follow='head', layer=60)
            elif name.endswith(('hand', 'foot')):
                hand = name.endswith('hand')
                data.update(joint=('wrist' if hand else 'ankle')+side, height=(150 if hand else 130) if dressed else (150 if hand else 110), breadth=.9 if hand else (1.25 if near else 1.35),
                            angle=0, follow=('hand' if hand else 'foot')+side, layer=(80 if near else 14) if hand else (29 if near else 20))
            elif name == 'pelvis':
                data.update(joint='hip', height=110, angle=0, layer=50)
            else:
                data.update(joint='hipNear' if name.startswith('front') else 'hipFar', height=630, angle=0 if name.startswith('front') else 20,
                            follow='skirtNear' if name.startswith('front') else 'skirtFar', layer=50 if name.startswith('front') else 5)
            skin['parts'][name] = data
            assert atlas.crop((x, y, x+image.width, y+image.height)).tobytes() == image.tobytes()
        path = DEST/(cid+'.png')
        atlas.save(path, optimize=True)
        texture_meta(path)
        skin['atlas_sha256'] = digest(path)
        skin['ground'] = max(skin['feet'][side][1]+skin['parts'][('near' if side=='Near' else 'far')+'-foot']['height']*(1-skin['parts'][('near' if side=='Near' else 'far')+'-foot']['pivot'][1]/skin['parts'][('near' if side=='Near' else 'far')+'-foot']['size'][1]) for side in ('Near','Far'))
        result['skins'][cid] = skin
    # This is the user's unmodified assembled reference, used only in the studio.
    import shutil
    shutil.copy2(SOURCE/'assembled.png', DEST/'reference-assembled.png')
    texture_meta(DEST/'reference-assembled.png')
    result['expected_skins'] += 2
    result['expected_parts'] += len(DRESSED)+len(BARE)


if __name__ == '__main__':
    path = DEST/'index.json'
    result = json.loads(path.read_text(encoding='utf-8'))
    for key in ('reference-jade', 'reference-body'):
        old = result['skins'].pop(key, None)
        if old:
            result['expected_skins'] -= 1
            result['expected_parts'] -= len(old['parts'])
    bake_reference(result)
    path.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print('Reference sheets bound: 16 dressed parts + 15 body parts; original RGBA preserved.')
