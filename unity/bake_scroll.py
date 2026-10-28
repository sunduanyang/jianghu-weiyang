"""Bake the 舆图 / 长卷 textures for the city stage.

The 舆图 shows one town region as a node map on silk; the 长卷 unrolls one settlement as a
handscroll of its facilities. Buildings are drawn in a 界画 manner (ruled ink outlines, mineral
colours) so they sit beside the silk atlas rather than the photographic city tiles.
All drawing is procedural and seeded, so re-running the script reproduces the same files.

    python unity/bake_scroll.py
"""
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

from bake_ui import ASSETS, INK, VERMILION, catmull, cjk_font, from_array, noise, paper_field, rgba, save, write_folder_meta

OUT = ASSETS / 'Resources' / 'Scroll'
K = 3                                   # supersampling factor; every sprite is drawn at K× and reduced

SILK = (214, 194, 148)                  # the atlas land silk, so both maps read as one set
WALL = (226, 211, 174)
WALL_SHADE = (196, 178, 138)
ROOF = (74, 80, 82)
ROOF_LIGHT = (104, 110, 110)
PILLAR = (142, 49, 34)
WOOD = (122, 86, 54)
SHILV = (86, 128, 118)                  # 石绿
HUAQING = (62, 94, 112)                 # 花青
OCHRE = (176, 132, 76)


class Sprite:
    """A transparent canvas drawn at K× with helpers in final-pixel units."""

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.img = Image.new('RGBA', (w * K, h * K), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)

    def p(self, pts):
        return [(x * K, y * K) for x, y in pts]

    def poly(self, pts, fill, line=INK, width=1.2, alpha=235):
        self.d.polygon(self.p(pts), fill=fill if fill is None or len(fill) == 4 else rgba(fill))
        if line is not None:
            q = self.p(pts)
            self.d.line(q + [q[0]], fill=rgba(line, alpha), width=max(1, int(width * K)), joint='curve')

    def rect(self, x0, y0, x1, y1, fill, line=INK, width=1.2):
        self.poly([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], fill, line, width)

    def line(self, pts, col=INK, width=1.0, alpha=230):
        self.d.line(self.p(pts), fill=rgba(col, alpha), width=max(1, int(width * K)), joint='curve')

    def ellipse(self, x0, y0, x1, y1, fill, line=INK, width=1.0):
        self.d.ellipse((x0 * K, y0 * K, x1 * K, y1 * K), fill=None if fill is None else rgba(fill) if len(fill) == 3 else fill,
                       outline=None if line is None else rgba(line, 230), width=max(1, int(width * K)))

    def text(self, x, y, s, size, col, bold=True):
        self.d.text((x * K, y * K), s, font=cjk_font(int(size * K), bold), fill=rgba(col), anchor='mm')

    def paste(self, other, x, y):
        self.img.alpha_composite(other.img, (int(x * K), int(y * K)))

    def final(self, seed=0, scale=1.0):
        """Reduce, then let the pigment settle unevenly so flat fills read as a wash."""
        w, h = int(round(self.w * scale)), int(round(self.h * scale))
        img = self.img.resize((w, h), Image.LANCZOS)
        arr = np.array(img).astype(float)
        mottle = (noise((h, w), 6 * scale, 900 + seed) - .5) * .16 + (noise((h, w), .8, 950 + seed) - .5) * .06
        arr[..., :3] *= (1 + mottle)[..., None]
        return from_array(arr[..., :3], arr[..., 3])


# ---------------------------------------------------------------- building parts
def eave_roof(s, cx, top, w, rise, col=ROOF, lift=6, ridge=True):
    """Hip-and-gable roof with upturned eaves; `top` is where the roof meets the wall."""
    L, R = cx - w / 2, cx + w / 2
    inner = w * .16
    upper = top - rise
    left_curve = catmull([(L - 2, top - lift), (L + w * .05, top - 1), (L + inner, upper + rise * .25), (L + inner + 2, upper)], 6)
    right_curve = catmull([(R - inner - 2, upper), (R - inner, upper + rise * .25), (R - w * .05, top - 1), (R + 2, top - lift)], 6)
    outline = left_curve + right_curve + [(R - w * .03, top + 2), (L + w * .03, top + 2)]
    s.poly(outline, col, width=1.3)
    # tile courses: a few vertical strokes, darker eave band
    for i in range(1, 9):
        x = L + inner + (w - 2 * inner) * i / 9
        s.line([(x, upper + 2), (x + (x - cx) * .12, top - 1)], INK, .6, 70)
    s.line([(L + w * .03, top), (R - w * .03, top)], INK, 1.6, 200)
    if ridge:
        s.line([(L + inner, upper), (R - inner, upper)], INK, 2.4, 240)
        for x in (L + inner, R - inner):     # 鸱吻 ends
            s.line([(x, upper), (x + (2 if x < cx else -2), upper - 5)], INK, 2.0, 240)


def hall(s, cx, base, w, h, roof=ROOF, wall=WALL, pillars=True, door=True, rise=None, lift=6, ridge=True):
    L, R, top = cx - w / 2, cx + w / 2, base - h
    s.rect(L, top, R, base, wall, width=1.2)
    if pillars:
        n = max(2, int(round(w / 24)))
        for i in range(n + 1):
            x = L + 3 + (w - 6) * i / n
            s.line([(x, top + 2), (x, base)], PILLAR, 1.8, 230)
    if door:
        s.rect(cx - w * .09, base - h * .64, cx + w * .09, base, (88, 62, 44), width=1)
    s.line([(L + 2, top + 4), (R - 2, top + 4)], INK, .8, 150)        # lintel
    eave_roof(s, cx, top, w * 1.22, rise or max(12, h * .55), roof, lift, ridge)


def platform(s, cx, base, w, h=6, col=WALL_SHADE):
    s.rect(cx - w / 2, base - h, cx + w / 2, base, col, width=1)
    for i in range(1, 4):
        s.line([(cx - w / 2 + 2, base - h * i / 4), (cx + w / 2 - 2, base - h * i / 4)], INK, .5, 70)


def low_wall(s, x0, x1, base, h=14, col=WALL):
    s.rect(x0, base - h, x1, base, col, width=1)
    s.poly([(x0 - 3, base - h), (x0 + 2, base - h - 5), (x1 - 2, base - h - 5), (x1 + 3, base - h)], ROOF, width=1)


def tree(s, x, base, size=1.0, kind='round', seed=0):
    rng = np.random.default_rng(seed)
    h = 38 * size
    s.line([(x, base), (x + rng.uniform(-2, 2), base - h * .55)], (82, 60, 40), 2.2 * size, 230)
    if kind == 'pine':
        for i in range(4):
            y = base - h * (.42 + i * .16)
            w = (22 - i * 4) * size
            s.poly([(x - w, y), (x, y - 9 * size), (x + w, y)], (70, 104, 86), width=.9)
    elif kind == 'willow':
        crown = (x, base - h * .9)
        for i in range(13):
            ang = -math.pi * .9 + i * math.pi * .8 / 12
            a = (crown[0] + math.cos(ang) * 6 * size, crown[1] + math.sin(ang) * 4 * size)
            b = (a[0] + math.cos(ang) * 14 * size, a[1] + 34 * size)
            s.line(catmull([a, ((a[0] + b[0]) / 2 + 3, a[1] + 8), b], 5), (96, 130, 92), 1.0, 200)
    else:
        for _ in range(7):
            dx, dy, r = rng.uniform(-11, 11) * size, rng.uniform(-30, -12) * size, rng.uniform(7, 12) * size
            s.ellipse(x + dx - r, base + dy - h * .3 - r, x + dx + r, base + dy - h * .3 + r, (88, 118, 90, 200), line=None)
        s.ellipse(x - 14 * size, base - h - 2 * size, x + 14 * size, base - h * .45, None, line=(52, 70, 56), width=.6)


def banner(s, x, base, height, text, cloth=(232, 218, 182), ink=VERMILION):
    s.line([(x, base), (x, base - height)], INK, 1.6, 240)
    s.line([(x, base - height + 2), (x + 20, base - height + 2)], INK, 1.2, 230)
    s.rect(x + 2, base - height + 3, x + 19, base - height + 40, cloth, width=1)
    s.text(x + 10.5, base - height + 21, text, 13, ink)


def water(s, x0, x1, y0, y1):
    s.rect(x0, y0, x1, y1, (150, 172, 160), line=None)
    rng = np.random.default_rng(int(x0 + y0))
    for row in range(int((y1 - y0) / 7)):
        y = y0 + 4 + row * 7
        for x in np.arange(x0 + (row % 2) * 7, x1 - 10, 14):
            s.d.arc(((x) * K, (y - 3) * K, (x + 12) * K, (y + 4) * K), 200, 340, fill=rgba(HUAQING, 150), width=K)


# ---------------------------------------------------------------- one sprite per facility kind
W, H, GROUND = 340, 260, 238          # drawing units; GROUND is the street line inside the sprite
BUILDING_SCALE = 1.3                  # buildings are saved at 442×338 so the scroll shows them unscaled


def b_gate():
    s = Sprite(W, H)
    s.rect(18, 150, 322, GROUND, (206, 190, 150), width=1.3)                 # wall
    for x in range(18, 322, 12):
        s.rect(x, 142, x + 7, 150, (206, 190, 150), width=.8)                # crenels
    for y in range(160, GROUND, 11):
        s.line([(20, y), (320, y)], INK, .4, 60)
    s.poly(catmull([(146, GROUND), (146, 196), (170, 180), (194, 196), (194, GROUND)], 6), (60, 44, 34), width=1.2)
    hall(s, 170, 142, 130, 38, rise=20)
    hall(s, 170, 142 - 38 - 18, 86, 22, rise=16)
    return s


def b_palace():
    s = Sprite(W, H)
    s.rect(10, 196, 330, GROUND, PILLAR, width=1.2)                          # 宫墙
    s.poly([(6, 196), (12, 188), (328, 188), (334, 196)], ROOF, width=1)
    platform(s, 170, 188, 210, 14)
    hall(s, 170, 174, 170, 48, roof=(96, 86, 62), rise=24)
    hall(s, 170, 174 - 48 - 20, 110, 26, roof=(96, 86, 62), rise=20)
    hall(s, 54, 188, 62, 30, roof=(96, 86, 62), rise=14, pillars=False)
    hall(s, 286, 188, 62, 30, roof=(96, 86, 62), rise=14, pillars=False)
    return s


def b_government():
    s = Sprite(W, H)
    low_wall(s, 24, 316, GROUND, 18)
    hall(s, 170, 196, 160, 42, rise=22)
    hall(s, 170, GROUND, 70, 30, rise=14, pillars=False)                    # 仪门
    s.rect(150, GROUND - 46, 190, GROUND - 38, (40, 32, 26), width=1)
    s.text(170, GROUND - 42, '衙', 8, (232, 210, 150))
    for x in (34, 306):                                                     # 旗杆
        s.line([(x, GROUND), (x, GROUND - 130)], INK, 1.4, 240)
        d = 1 if x < 170 else -1
        s.poly([(x, GROUND - 128), (x + 24 * d, GROUND - 120), (x, GROUND - 110)], VERMILION, width=.8)
    s.ellipse(250, GROUND - 40, 274, GROUND - 16, (150, 92, 54))              # 鸣冤鼓
    s.line([(254, GROUND - 16), (250, GROUND)], WOOD, 1.4, 230)
    s.line([(270, GROUND - 16), (274, GROUND)], WOOD, 1.4, 230)
    return s


def b_market():
    s = Sprite(W, H)
    hall(s, 170, 186, 200, 34, rise=18, door=False)
    for i, x in enumerate((70, 170, 270)):
        cloth = ((196, 92, 62), (214, 182, 112), (96, 128, 118))[i]
        s.poly([(x - 44, GROUND - 46), (x + 44, GROUND - 46), (x + 52, GROUND - 30), (x - 52, GROUND - 30)], cloth, width=1)
        for px in (x - 46, x + 46):
            s.line([(px, GROUND - 46), (px, GROUND)], WOOD, 1.6, 240)
        s.rect(x - 40, GROUND - 16, x + 40, GROUND - 4, WOOD, width=.9)
        for gx in range(int(x - 34), int(x + 34), 13):
            s.ellipse(gx, GROUND - 24, gx + 10, GROUND - 15, (204, 160, 86) if i != 2 else (120, 150, 96), width=.6)
    banner(s, 318, GROUND, 120, '市')
    return s


def b_inn():
    s = Sprite(W, H)
    hall(s, 160, GROUND, 190, 52, rise=18, lift=5)
    s.rect(54, GROUND - 52 - 50, 266, GROUND - 52 - 8, WALL, width=1.2)       # second storey
    for x in range(60, 262, 12):
        s.line([(x, GROUND - 62), (x, GROUND - 52)], INK, .8, 200)            # railing
    s.line([(56, GROUND - 62), (264, GROUND - 62)], INK, 1.2, 230)
    for x in (96, 138, 182, 224):
        s.rect(x, GROUND - 96, x + 24, GROUND - 70, (238, 226, 196), width=.8)
    eave_roof(s, 160, GROUND - 102, 240, 26)
    banner(s, 296, GROUND, 150, '酒')
    return s


def b_dock():
    s = Sprite(W, H)
    water(s, 0, W, GROUND - 6, H)
    hall(s, 92, GROUND - 6, 110, 40, rise=16, pillars=False)                 # 货栈
    s.rect(150, GROUND - 12, 330, GROUND - 4, WOOD, width=1)                  # 栈桥
    for x in range(156, 330, 18):
        s.line([(x, GROUND - 4), (x, GROUND + 12)], WOOD, 1.6, 230)
    # junk with mat sail
    s.poly(catmull([(196, GROUND + 4), (230, GROUND + 14), (290, GROUND + 14), (318, GROUND + 2)], 6) + [(196, GROUND + 4)], (110, 78, 50), width=1.2)
    s.line([(258, GROUND + 4), (258, GROUND - 92)], INK, 1.6, 240)
    s.poly([(260, GROUND - 88), (300, GROUND - 78), (296, GROUND - 14), (260, GROUND - 18)], (214, 196, 150), width=1)
    for y in range(int(GROUND - 80), int(GROUND - 18), 9):
        s.line([(260, y), (298, y + 2)], INK, .6, 120)
    for x in (40, 70):
        s.ellipse(x, GROUND - 26, x + 22, GROUND - 6, (176, 140, 90))     # 货包
    return s


def b_clinic():
    s = Sprite(W, H)
    hall(s, 150, GROUND, 180, 50, rise=22)
    for i, x in enumerate((262, 300)):                                      # 晒药架
        s.line([(x - 16, GROUND), (x - 16, GROUND - 46)], WOOD, 1.4, 230)
        s.line([(x + 16, GROUND), (x + 16, GROUND - 46)], WOOD, 1.4, 230)
        for y in (GROUND - 44, GROUND - 26):
            s.ellipse(x - 18, y - 4, x + 18, y + 6, (176, 154, 96), width=.8)
            for dx in range(-12, 14, 6):
                s.ellipse(x + dx - 2, y - 1, x + dx + 2, y + 3, (110, 136, 80, 255), line=None)
    banner(s, 46, GROUND, 130, '药', cloth=(96, 128, 118), ink=(238, 226, 196))
    return s


def b_workshop():
    s = Sprite(W, H)
    hall(s, 140, GROUND, 170, 46, rise=18, door=False, wall=(206, 188, 150))
    s.rect(230, GROUND - 120, 252, GROUND - 40, (146, 120, 96), width=1.2)    # 烟囱
    for i in range(4):                                                      # smoke
        r = 9 + i * 4
        s.ellipse(236 + i * 9 - r, GROUND - 128 - i * 16 - r, 236 + i * 9 + r, GROUND - 128 - i * 16 + r, (120, 116, 108, 70 - i * 12), line=None)
    s.rect(120, GROUND - 22, 160, GROUND - 12, (60, 56, 54), width=1)        # 砧
    s.rect(132, GROUND - 12, 148, GROUND, (60, 56, 54), width=1)
    for y in range(int(GROUND - 30), int(GROUND), 7):                       # 柴堆
        s.line([(270, y), (318, y)], WOOD, 4, 230)
    return s


def b_training():
    s = Sprite(W, H)
    s.rect(14, GROUND - 8, 326, GROUND, (206, 186, 140), width=1)            # 演武台
    hall(s, 82, GROUND - 8, 92, 34, rise=18, pillars=True)                  # 亭
    for i, x in enumerate(range(176, 260, 14)):                             # 兵器架
        s.line([(x, GROUND - 8), (x, GROUND - 98)], WOOD, 1.6, 240)
        tip = [(x - 3, GROUND - 98), (x, GROUND - 110), (x + 3, GROUND - 98)] if i % 2 == 0 else [(x - 6, GROUND - 98), (x + 2, GROUND - 106), (x + 2, GROUND - 96)]
        s.poly(tip, (180, 184, 180), width=.8)
    s.line([(170, GROUND - 70), (264, GROUND - 70)], WOOD, 2, 240)
    s.line([(170, GROUND - 30), (264, GROUND - 30)], WOOD, 2, 240)
    for r, col in ((26, (232, 218, 182)), (18, VERMILION), (10, (232, 218, 182)), (4, VERMILION)):   # 靶
        s.ellipse(298 - r, GROUND - 64 - r, 298 + r, GROUND - 64 + r, col, width=.8)
    s.line([(298, GROUND - 38), (298, GROUND - 8)], WOOD, 2, 240)
    return s


def b_school():
    s = Sprite(W, H)
    low_wall(s, 20, 320, GROUND, 14)
    hall(s, 180, GROUND - 14, 170, 46, rise=22)
    s.rect(150, GROUND - 76, 210, GROUND - 66, (40, 32, 26), width=1)
    s.text(180, GROUND - 71, '书院', 7, (232, 210, 150))
    tree(s, 58, GROUND - 14, 1.5, 'pine', 3)
    return s


def b_temple():
    s = Sprite(W, H)
    hall(s, 110, GROUND, 150, 44, roof=(98, 84, 60), wall=(226, 196, 150), rise=22)
    base = GROUND
    for i in range(5):                                                      # 塔
        w, h = 52 - i * 7, 26 - i * 1.5
        s.rect(262 - w / 2 + 6, base - h, 262 + w / 2 - 6, base, (226, 206, 164), width=1)
        eave_roof(s, 262, base - h, w + 10, 9, (98, 84, 60), lift=4, ridge=False)
        base -= h + 9
    s.line([(262, base), (262, base - 22)], INK, 1.8, 240)
    for y in range(4):
        s.ellipse(258, base - 6 - y * 5, 266, base - 2 - y * 5, None, width=.6)
    return s


def b_sect():
    s = Sprite(W, H)
    for (x0, peak, x1), col in (((10, 50, 200), (130, 160, 140)), ((120, 14, 334), (96, 136, 124))):
        s.poly(catmull([(x0, GROUND - 30), ((x0 + x1) / 2 - 34, peak + 64), ((x0 + x1) / 2, peak), ((x0 + x1) / 2 + 42, peak + 72), (x1, GROUND - 30)], 8), col, width=1.2)
        s.line([((x0 + x1) / 2, peak + 8), ((x0 + x1) / 2 - 10, peak + 64)], INK, .8, 120)
    tree(s, 40, GROUND - 26, 1.0, 'pine', 21)
    tree(s, 304, GROUND - 26, 1.1, 'pine', 22)
    hall(s, 240, GROUND - 70, 90, 28, rise=14)                              # 山门殿 on the slope
    s.line([(196, GROUND - 70), (172, GROUND - 8)], INK, .8, 120)           # 石阶
    s.line([(284, GROUND - 70), (262, GROUND - 8)], INK, .8, 120)
    for y in range(int(GROUND - 64), int(GROUND - 8), 8):
        t = (y - (GROUND - 70)) / 62
        s.line([(196 - 24 * t, y), (284 - 22 * t, y)], INK, .5, 80)
    # 牌坊 in front: four posts, a lintel, a small roof and a gilt plaque
    s.rect(70, GROUND - 6, 210, GROUND, WALL_SHADE, width=1)
    for x in (82, 116, 164, 198):
        s.line([(x, GROUND - 6), (x, GROUND - 86)], PILLAR, 3.2, 245)
    s.rect(74, GROUND - 92, 206, GROUND - 80, (60, 50, 40), width=1)
    s.rect(120, GROUND - 78, 160, GROUND - 64, (214, 180, 96), width=.8)
    eave_roof(s, 140, GROUND - 92, 156, 14, lift=4)
    return s


def b_residence():
    s = Sprite(W, H)
    for i, (cx, base, w, h) in enumerate(((70, GROUND - 26, 90, 30), (190, GROUND - 34, 100, 32), (290, GROUND - 22, 80, 28),
                                          (120, GROUND, 96, 32), (240, GROUND, 104, 34))):
        hall(s, cx, base, w, h, rise=14, pillars=False, wall=(222, 204, 166) if i % 2 else (214, 196, 156))
    tree(s, 22, GROUND, 1.0, 'round', 5)
    tree(s, 326, GROUND - 8, .9, 'round', 6)
    return s


def b_granary():
    s = Sprite(W, H)
    for y in range(int(GROUND - 30), int(GROUND), 6):                        # 田
        s.line([(10, y), (120, y - 4)], (150, 160, 96), 1.2, 160)
    for cx, w, h in ((170, 56, 52), (240, 60, 60), (306, 48, 44)):            # 囷
        s.rect(cx - w / 2, GROUND - h, cx + w / 2, GROUND, (214, 190, 140), width=1.2)
        for yy in range(int(GROUND - h + 8), int(GROUND), 9):
            s.line([(cx - w / 2 + 2, yy), (cx + w / 2 - 2, yy)], INK, .5, 90)
        s.poly([(cx - w / 2 - 6, GROUND - h), (cx, GROUND - h - 30), (cx + w / 2 + 6, GROUND - h)], (176, 150, 92), width=1.1)
        for k in range(-2, 3):
            s.line([(cx, GROUND - h - 30), (cx + k * w * .25, GROUND - h)], INK, .5, 100)
    hall(s, 60, GROUND - 30, 76, 28, rise=12, pillars=False)
    return s


def b_garden():
    s = Sprite(W, H)
    s.poly(catmull([(40, GROUND - 4), (90, GROUND - 26), (200, GROUND - 28), (300, GROUND - 14), (320, GROUND + 2), (40, GROUND + 2)], 8), (150, 172, 160), width=1)
    for x in range(70, 300, 22):
        s.d.arc(((x) * K, (GROUND - 18) * K, (x + 14) * K, (GROUND - 10) * K), 200, 340, fill=rgba(HUAQING, 140), width=K)
    # 亭: round-roofed pavilion on the bank
    for x in (182, 222):
        s.line([(x, GROUND - 30), (x, GROUND - 82)], PILLAR, 2.2, 240)
    s.line([(176, GROUND - 46), (228, GROUND - 46)], INK, 1, 200)
    s.poly(catmull([(160, GROUND - 82), (182, GROUND - 92), (202, GROUND - 118), (222, GROUND - 92), (244, GROUND - 82)], 8), ROOF, width=1.2)
    s.line([(202, GROUND - 118), (202, GROUND - 130)], INK, 1.6, 240)
    tree(s, 92, GROUND - 22, 1.7, 'willow', 7)
    tree(s, 296, GROUND - 12, 1.3, 'willow', 8)
    s.d.arc((236 * K, (GROUND - 46) * K, 288 * K, (GROUND - 2) * K), 190, 350, fill=rgba(INK, 220), width=2 * K)   # 拱桥
    return s


def b_generic():
    s = Sprite(W, H)
    hall(s, 170, GROUND, 180, 48, rise=22)
    return s


BUILDINGS = dict(gate=b_gate, palace=b_palace, government=b_government, market=b_market, inn=b_inn, dock=b_dock,
                 clinic=b_clinic, workshop=b_workshop, training=b_training, school=b_school, temple=b_temple,
                 sect=b_sect, residence=b_residence, granary=b_granary, garden=b_garden, generic=b_generic)


# ---------------------------------------------------------------- scroll furniture
def bake_silk():
    img = paper_field(512, SILK, 701, fibre=True, wrap=True)
    arr = np.array(img.convert('RGB'), float)
    yy, xx = np.mgrid[0:512, 0:512]
    arr += ((np.sin(xx * 2.1) * np.sin(yy * 2.1)) * 2.0)[..., None]       # weave, period divides 512 closely enough at this scale
    save(from_array(arr), OUT / 'silk.png', wrap=True)


def bake_hills():
    """Far 青绿 ranges for the top of the scroll: tiles horizontally, fades to nothing at the base."""
    w, h = 1024, 200
    img = np.zeros((h, w, 4))
    xs = np.arange(w)
    for layer, (base, peak, col, alpha, seed) in enumerate(((190, 40, (96, 132, 136), .55, 711), (196, 92, (120, 150, 112), .62, 712))):
        ridge = np.full(w, float(base))
        rng = np.random.default_rng(seed)
        for _ in range(9):
            c, hgt, wid = rng.uniform(0, w), rng.uniform(.4, 1) * (base - peak), rng.uniform(70, 170)
            for shift in (-w, 0, w):
                ridge = np.minimum(ridge, base - hgt * np.exp(-((xs - c - shift) / wid) ** 2))
        ridge += (noise((1, w), 3, seed + 1)[0] - .5) * 5
        yy = np.arange(h)[:, None]
        inside = yy >= ridge[None, :]
        depth = np.clip((yy - ridge[None, :]) / 60, 0, 1)
        a = inside * alpha * (1 - depth * .85)
        rim = np.exp(-np.abs(yy - ridge[None, :]) / 1.2) * .55
        rgb = np.array(col, float)[None, None] * (1 - depth[..., None] * .15)
        old_a = img[..., 3:4]
        new_a = np.maximum(a, rim)[..., None]
        ink = np.array(INK, float)[None, None]
        layer_rgb = np.where((rim > a)[..., None], ink, rgb)
        img[..., :3] = layer_rgb * new_a + img[..., :3] * old_a * (1 - new_a)
        img[..., 3:4] = new_a + old_a * (1 - new_a)
        img[..., :3] /= np.maximum(img[..., 3:4], 1e-6)
    save(from_array(img[..., :3], img[..., 3] * 255), OUT / 'hills.png', wrap=True)


def bake_mist():
    w, h = 256, 96
    yy, xx = np.mgrid[0:h, 0:w]
    r = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2)
    a = np.clip(1 - r, 0, 1) ** 1.6 * 230
    save(from_array(np.zeros((h, w, 3)) + np.array(SILK, float) * 1.08, a), OUT / 'mist.png')


def bake_glyphs():
    """Pictograms: the scroll's foreground pine, and the post pavilion and boat that mark roads out of a region."""
    def glyph(name, w, h, fn):
        s = Sprite(w, h)
        fn(s, w, h)
        save(s.final(), OUT / name)

    def pine(s, w, h):
        tree(s, w / 2, h - 2, .62, 'pine', 11)
    glyph('pine.png', 32, 32, pine)

    def node_exit(s, w, h):                       # 驿亭: the post pavilion where a road leaves the region
        for x in (12, 36):
            s.line([(x, h - 4), (x, h - 24)], PILLAR, 2.2, 240)
        s.poly(catmull([(2, h - 24), (14, h - 28), (24, h - 40), (34, h - 28), (46, h - 24)], 6), ROOF, width=1.2)
    glyph('node_exit.png', 48, 48, node_exit)

    def node_boat(s, w, h):
        s.poly(catmull([(4, h - 16), (16, h - 6), (36, h - 6), (46, h - 18)], 5) + [(4, h - 16)], (110, 78, 50), width=1.2)
        s.line([(26, h - 12), (26, h - 44)], INK, 1.6, 240)
        s.poly([(27, h - 42), (42, h - 36), (40, h - 16), (27, h - 18)], (214, 196, 150), width=1)
    glyph('node_boat.png', 48, 48, node_boat)


# ---------------------------------------------------------------- 舆图 stamps in the atlas manner
# The region map follows the same conventions as the world atlas (bake_ui.bake_atlas): 青绿 peaks
# whose tops are mineral blue-green fading to ochre at the foot, ink-dot groves, and aged silk.
PEAK_TOP, PEAK_MID, PEAK_BASE = np.array([66, 112, 118.]), np.array([116, 146, 110.]), np.array([216, 198, 152.])


def qinglv_peak(size, seed, fade=1.0, k=3):
    """One 青绿 peak, drawn at k× and reduced; returns an RGBA image whose foot sits on the bottom edge."""
    r = np.random.default_rng(seed)
    size *= k
    w = size * r.uniform(.95, 1.35)
    h = size * r.uniform(1.15, 1.65)
    lean = r.uniform(-.25, .25) * w
    iw, ih = int(2 * w + 6 * k), int(h + 6 * k)
    bl, br = (3 * k, h + 3 * k), (2 * w + 3 * k, h + 3 * k)
    top = (w + 3 * k + lean, 3 * k)
    s1 = (w * r.uniform(.4, .6) + 3 * k, h * r.uniform(.42, .6) + 3 * k)
    s2 = (w * r.uniform(1.4, 1.65) + 3 * k, h * r.uniform(.38, .55) + 3 * k)
    ridge = catmull([bl, s1, top, s2, br], 8)
    mask = Image.new('L', (iw, ih), 0)
    ImageDraw.Draw(mask).polygon(ridge, fill=255)
    t = (np.arange(ih) / max(ih - 1, 1))[:, None]
    col = np.where(t < .45, PEAK_TOP * (1 - t / .45) + PEAK_MID * (t / .45), PEAK_MID * (1 - (t - .45) / .55) + PEAK_BASE * ((t - .45) / .55))
    col = np.broadcast_to(col[:, None, :], (ih, iw, 3)).copy()
    xs = (np.arange(iw) / iw)[None, :, None]
    col *= (1 - .14 / (1 + np.exp(-(xs - top[0] / iw) * 28)))       # shadow side, blended across the crest
    col *= (1 + (noise((ih, iw), 4 * k, seed + 7) - .5) * .10)[..., None]
    a = np.array(mask, float) * (.96 * fade) * np.clip(1.55 - t, 0, 1)
    img = from_array(col, a)
    d = ImageDraw.Draw(img)
    d.line(ridge[:-1], fill=rgba(INK, int(205 * fade)), width=2 * k, joint='curve')
    for _ in range(r.integers(1, 3)):                              # 皴 strokes
        sx = top[0] + r.uniform(-.35, .35) * w
        d.line([(sx, top[1] + h * .2), (sx + r.uniform(-.1, .1) * w, top[1] + h * r.uniform(.45, .7))], fill=rgba(INK, int(95 * fade)), width=k)
    return img.resize((max(1, iw // k), max(1, ih // k)), Image.LANCZOS)


def stamp(parts, w, h):
    """Compose peaks left-to-right, back-to-front, with their feet on a common base line."""
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    for peak, x, foot in parts:
        img.alpha_composite(peak, (int(x - peak.width / 2), int(foot - peak.height)))
    return img


def ink_grove(seed, w=44, h=34):
    """Song-map grove: three to five little trees, each a trunk stroke under a cone of ink dots."""
    s = Sprite(w, h)
    rng = np.random.default_rng(seed)
    for i in range(rng.integers(3, 6)):
        x, base = rng.uniform(7, w - 7), rng.uniform(h * .6, h - 3)
        th = rng.uniform(9, 14)
        s.line([(x, base), (x, base - th)], INK, .9, 210)
        for j in range(5):
            yy = base - th * (.45 + j * .13)
            ww = (4.2 - j * .7)
            s.line([(x - ww, yy + 1.5), (x, yy - .5), (x + ww, yy + 1.5)], (52, 74, 58), 1.0, 220)
    return s.final(seed)


def bake_map_stamps():
    for i in range(6):                                             # single peaks
        save(stamp([(qinglv_peak(17 + (i % 3) * 3, 3100 + i), 36, 58)], 72, 60), OUT / f'peak_{i}.png')
    for i in range(3):                                             # small ranges for dense mountain ground
        rng = np.random.default_rng(3200 + i)
        parts = [(qinglv_peak(rng.uniform(13, 17), 3210 + i * 10 + j, .9 if j % 2 else 1.0), 22 + j * 30 + rng.uniform(-4, 4), 70 - (j % 2) * 4) for j in range(3)]
        parts.sort(key=lambda p: p[2])
        save(stamp(parts, 112, 72), OUT / f'range_{i}.png')
    for i in range(3):
        save(ink_grove(3300 + i), OUT / f'grove_{i}.png')


def bake_aging():
    """Foxing, tide marks, fold creases and a darker mount, laid over the whole 舆图 sheet."""
    W, H = 1024, 576
    yy, xx = np.mgrid[0:H, 0:W]
    dark = np.zeros((H, W))
    for x0 in (W / 3, W * 2 / 3):
        dx = xx - x0 - (noise((H, 1), 30, int(x0))[:, 0][:, None] - .5) * 6
        dark += .10 * np.exp(-(dx / 1.3) ** 2)
    dy = yy - H / 2 - (noise((1, W), 40, 7)[0][None, :] - .5) * 6
    dark += .10 * np.exp(-(dy / 1.3) ** 2)
    rng = np.random.default_rng(3401)
    spots = np.zeros((H, W))
    for _ in range(140):
        x, y, r = rng.uniform(0, W), rng.uniform(0, H), rng.uniform(1.0, 3.8)
        x0, x1, y0, y1 = int(max(0, x - 3 * r)), int(min(W, x + 3 * r)), int(max(0, y - 3 * r)), int(min(H, y + 3 * r))
        spots[y0:y1, x0:x1] = np.maximum(spots[y0:y1, x0:x1], np.exp(-(((xx[y0:y1, x0:x1] - x) ** 2 + (yy[y0:y1, x0:x1] - y) ** 2) / (r * r))))
    tide = np.clip((noise((H, W), 50, 3402) - .70) * 4, 0, 1) * .10
    edge = np.clip(np.sqrt(((xx - W / 2) / (W * .56)) ** 2 + ((yy - H / 2) / (H * .56)) ** 2) - .62, 0, 1) * .55
    a = np.clip(dark + spots * .28 + tide + edge, 0, .8)
    col = np.where((spots > .2)[..., None], np.array([120, 80, 36.]), np.array([90, 66, 40.]))
    save(from_array(col, a * 255), OUT / 'aging.png')


def contact_sheet():
    """A preview of every building on silk, for reviewing the set at a glance (not used in game)."""
    names = list(BUILDINGS)
    w, h = int(W * BUILDING_SCALE), int(H * BUILDING_SCALE)
    sheet = paper_field((h * 4 + 40, w * 4 + 40), SILK, 709, fibre=False, wrap=False)
    for i, name in enumerate(names):
        img = Image.open(OUT / f'building_{name}.png')
        sheet.alpha_composite(img, (20 + (i % 4) * w, 20 + (i // 4) * h))
    return sheet


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    write_folder_meta(OUT)
    for name, fn in BUILDINGS.items():
        save(fn().final(len(name), BUILDING_SCALE), OUT / f'building_{name}.png')
    bake_silk()
    bake_hills()
    bake_mist()
    bake_glyphs()
    bake_map_stamps()
    bake_aging()
    return contact_sheet()


if __name__ == '__main__':
    import sys
    sheet = main()
    if len(sys.argv) > 1:
        sheet.save(sys.argv[1])
