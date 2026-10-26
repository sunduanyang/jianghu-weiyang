"""Shared geometry for the 美型 paper-puppet art pipeline.

Everything is expressed in the units of unity/paper-rig.json (hip at the origin, y down,
figure faces +x / right). The bone lengths, hip offsets and rest angles are read from that
file, so templates, cut parts and the Unity runtime all agree on one skeleton.

What changes against the 24-skin cast is the head: a 美型 head is HEAD units from crown to
chin (about 7.7 heads tall in total) and is split into layers:

    face shape · eyes · brows · mouth · hair · headwear (headwear belongs to an outfit set)

Feature anchors are fixed in head space, so any eye fits any face shape of the same body.
"""
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
RIG = json.loads((ROOT / 'unity' / 'paper-rig.json').read_text(encoding='utf-8'))
L = RIG['lengths']
REST = RIG['rest']

# ------------------------------------------------------------------ body templates
# Shoulder positions are per body (traditional spread-shoulder torso, near side in front).
BODIES = {
    'female': {
        'label': '成年女子', 'head': 60.0, 'shoulders': {'Near': (13, -140), 'Far': (-13, -140)},
        # torso outline (hip-relative units), drawn front (+x) first, clockwise
        'torso': [(-11, -163), (12, -163), (19, -150), (27, -120), (31, -100), (24, -80), (20, -45), (25, -10), (22, 6),
                  (-22, 6), (-24, -20), (-20, -55), (-22, -95), (-25, -130), (-19, -152)],
        'limb_width': {'upper': (17, 13), 'forearm': (13, 10), 'thigh': (27, 18), 'shin': (17, 11)},
    },
    'male': {
        'label': '成年男子', 'head': 62.0, 'shoulders': {'Near': (17, -140), 'Far': (-17, -140)},
        'torso': [(-12, -163), (13, -163), (23, -150), (32, -125), (33, -95), (28, -60), (26, -30), (27, -5), (24, 7),
                  (-25, 7), (-27, -25), (-26, -60), (-28, -100), (-31, -132), (-22, -153)],
        'limb_width': {'upper': (21, 16), 'forearm': (16, 12), 'thigh': (30, 21), 'shin': (20, 13)},
    },
}


def rotate(p, degrees):
    a = math.radians(degrees)
    return (p[0] * math.cos(a) - p[1] * math.sin(a), p[0] * math.sin(a) + p[1] * math.cos(a))


def add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def pose(body, values=None):
    """Forward kinematics identical to PaperRig.Evaluate (Unity)."""
    v = dict(REST)
    v.update(values or {})
    lean = v['lean']
    b = BODIES[body]
    p = {'hip': (v['root_x'], v['root_y'])}
    p['neck'] = add(p['hip'], rotate((0, -L['spine']), lean))
    angles = {}
    for side in ('Far', 'Near'):
        prefix = 'near' if side == 'Near' else 'far'
        p['shoulder' + side] = add(p['hip'], rotate(b['shoulders'][side], lean))
        upper = v[prefix + '_upper'] + lean
        forearm = upper + v[prefix + '_elbow']
        p['elbow' + side] = add(p['shoulder' + side], rotate((0, L['upper_arm']), upper))
        p['wrist' + side] = add(p['elbow' + side], rotate((0, L['forearm']), forearm))
        p['hip' + side] = add(p['hip'], (9 if side == 'Near' else -9, 0))
        thigh = v[prefix + '_thigh']
        shin = thigh + v[prefix + '_knee']
        p['knee' + side] = add(p['hip' + side], rotate((0, L['thigh']), thigh))
        p['ankle' + side] = add(p['knee' + side], rotate((0, L['shin']), shin))
        angles.update({'upper' + side: upper, 'forearm' + side: forearm, 'thigh' + side: thigh, 'shin' + side: shin})
    return p, angles


# ------------------------------------------------------------------ head space
# Head space: unit = head length (crown→chin); origin at the chin; y down; facing right.
FACE_OUTLINE = [  # a neutral 美型 profile used only as a guide; real face shapes are drawn by the artist
    (-0.06, -1.00), (0.10, -0.96), (0.22, -0.88), (0.29, -0.76), (0.315, -0.64), (0.30, -0.56),   # crown → brow → nasion
    (0.34, -0.46), (0.385, -0.37), (0.405, -0.33), (0.375, -0.30), (0.345, -0.29),                  # nose
    (0.355, -0.24), (0.36, -0.205), (0.33, -0.175), (0.345, -0.14), (0.325, -0.10),                  # lips
    (0.30, -0.075), (0.305, -0.04), (0.285, -0.005), (0.22, 0.015),                                   # chin
    (0.14, 0.03), (0.105, 0.08), (0.10, 0.36),                                                         # throat
    (-0.13, 0.36), (-0.15, 0.10), (-0.20, -0.08), (-0.33, -0.28), (-0.40, -0.50), (-0.38, -0.72), (-0.27, -0.90),  # nape → skull
]


def smooth(points, rounds=2):
    """Chaikin corner cutting for closed outlines."""
    for _ in range(rounds):
        out = []
        for i, a in enumerate(points):
            b = points[(i + 1) % len(points)]
            out += [(a[0] * .75 + b[0] * .25, a[1] * .75 + b[1] * .25), (a[0] * .25 + b[0] * .75, a[1] * .25 + b[1] * .75)]
        points = out
    return points
ANCHORS = {   # head space
    'neck': (0.00, 0.30),     # joint to the torso (rig 'neck')
    'eye': (0.22, -0.50),
    'brow': (0.21, -0.61),
    'mouth': (0.315, -0.165),
    'ear': (-0.10, -0.46),
    'earring': (-0.10, -0.33),
    'crown': (-0.06, -1.00),  # headwear reference point (top of skull)
    'brow_band': (0.12, -0.80),  # forehead band / 抹额 line
}


def head_to_body(body, hp, neck_point):
    """Head-space point → body units, given the rig neck joint position."""
    h = BODIES[body]['head']
    n = ANCHORS['neck']
    return (neck_point[0] + (hp[0] - n[0]) * h, neck_point[1] + (hp[1] - n[1]) * h)


# ------------------------------------------------------------------ sheets
# Every sheet is a fixed grid; each cell knows its scale (px per rig unit) and where its
# pivot / anchor sits, so cutting and binding never depend on detecting drawn rivets.
OUTFIT_SCALE = 1.5          # px per rig unit on the outfit parts sheet (1024×1536, 4×5 cells)
TORSO_SCALE = 1.2           # torso cell only (the spine is long)
HEAD_SCALE = 5.5            # face / hair / headwear sheets
FEATURE_SCALE = 9.0         # eye / brow / mouth sheets

OUTFIT_SLOTS = [  # id, 名称, kind, bone or joint, layer (same layers as bake_paper_cast.py)
    ('waist', '腰饰/腰带（可空）', 'joint', 'hip', 52),
    ('torso', '上身躯干', 'bone', 'torso', 40),
    ('front-skirt', '前裙片/前裤腰片', 'joint', 'hip', 50),
    ('back-skirt', '后裙片/后裤腰片', 'joint', 'hip', 5),
    ('near-upper-arm', '近侧上臂', 'bone', 'upperNear', 72),
    ('far-upper-arm', '远侧上臂', 'bone', 'upperFar', 10),
    ('near-forearm', '近侧前臂（含袖口）', 'bone', 'forearmNear', 75),
    ('far-forearm', '远侧前臂（含袖口）', 'bone', 'forearmFar', 12),
    ('near-hand', '近侧手', 'joint', 'wristNear', 80),
    ('far-hand', '远侧手', 'joint', 'wristFar', 14),
    ('near-thigh', '近侧大腿', 'bone', 'thighNear', 25),
    ('far-thigh', '远侧大腿', 'bone', 'thighFar', 16),
    ('near-shin', '近侧小腿', 'bone', 'shinNear', 26),
    ('far-shin', '远侧小腿', 'bone', 'shinFar', 17),
    ('near-foot', '近侧脚/鞋', 'joint', 'ankleNear', 29),
    ('far-foot', '远侧脚/鞋', 'joint', 'ankleFar', 20),
]
BONE_LENGTH = {'torso': L['spine'], 'upper': L['upper_arm'], 'forearm': L['forearm'], 'thigh': L['thigh'], 'shin': L['shin']}
SKIRT_DROP = 128      # hip → hem of the skirt pieces, rig units
HAND_LENGTH, FOOT_LENGTH = 32, 34

HEAD_LAYERS = {  # z order inside the head (added to the head base layer 60)
    'face': 0, 'mouth': 1, 'eyes': 2, 'brows': 3, 'hair': 4, 'headwear': 5,
}


def outfit_cells():
    """Cells of the outfit sheet: 4 columns × 5 rows on 1024×1536 (row 5 = accessories)."""
    cells = {}
    for i, (sid, name, kind, target, layer) in enumerate(OUTFIT_SLOTS):
        col, row = i % 4, i // 4
        cells[sid] = {'name': name, 'kind': kind, 'target': target, 'layer': layer,
                      'rect': [col * 256, row * 300, 256, 300]}
    cells['accessories'] = {'name': '独立配件（兵器、披挂等）', 'kind': 'free', 'rect': [0, 1200, 1024, 336]}
    return cells


def feature_cells(kind):
    """Eye/brow sheets: 8 types × 3 expressions (6 cols × 4 rows of 256×256 on 1536×1024).
    Mouth sheet: 4 types × 3 expressions (6 cols × 2 rows; rows 3–4 left empty)."""
    types = 8 if kind in ('eyes', 'brows') else 4
    cells = []
    for t in range(types):
        for e, expr in enumerate(('calm', 'angry', 'sad')):
            i = t * 3 + e
            col, row = i % 6, i // 6
            cells.append({'type': t + 1, 'expression': expr, 'rect': [col * 256, row * 256, 256, 256], 'anchor': [col * 256 + 128, row * 256 + 128]})
    return cells
