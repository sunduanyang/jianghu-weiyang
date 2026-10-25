"""Bake the dark ink-and-lamplight UI skin and the silk-scroll atlas textures for the Unity client.

All textures are generated procedurally and deterministically (fixed seeds), so the
script can be re-run after tweaking colours.  Requires Pillow, NumPy and SciPy.

    python unity/bake_ui.py            # UI skin + atlas
    python unity/bake_ui.py --ui-only  # skip the (slower) atlas
"""
import argparse
import json
import math
import re
import uuid
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'unity' / 'JianghuUnity' / 'Assets'
UI = ASSETS / 'Resources' / 'UI'
MAP = ASSETS / 'Resources' / 'Map'

INK = (43, 35, 28)
PAPER = (239, 230, 208)
VERMILION = (142, 49, 34)
GOLD = (176, 135, 70)
WOOD = (52, 33, 22)


# ---------------------------------------------------------------- helpers
def rgba(c, a=255):
    return (c[0], c[1], c[2], a)


def tile_noise(size, sigma, seed):
    """Seamless (wrap-around) smooth noise in [0,1]."""
    rng = np.random.default_rng(seed)
    n = ndimage.gaussian_filter(rng.random((size, size)), sigma, mode='wrap')
    n -= n.min()
    return n / max(n.max(), 1e-6)


def noise(shape, sigma, seed):
    rng = np.random.default_rng(seed)
    n = ndimage.gaussian_filter(rng.random(shape), sigma)
    n -= n.min()
    return n / max(n.max(), 1e-6)


def from_array(rgb, alpha=None):
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    if alpha is None:
        return Image.fromarray(rgb, 'RGB').convert('RGBA')
    a = np.clip(alpha, 0, 255).astype(np.uint8)
    return Image.fromarray(np.dstack([rgb, a]), 'RGBA')


def paper_field(size, base, seed, fibre=True, wrap=True):
    """Rice-paper texture: mottling + fine grain + fibres."""
    h, w = (size, size) if isinstance(size, int) else size
    if wrap and h == w:
        low, mid, grain = tile_noise(h, 40, seed), tile_noise(h, 6, seed + 1), tile_noise(h, 0.8, seed + 2)
    else:
        low, mid, grain = noise((h, w), 40, seed), noise((h, w), 6, seed + 1), noise((h, w), 0.8, seed + 2)
    shade = (low - .5) * 14 + (mid - .5) * 7 + (grain - .5) * 10
    rgb = np.dstack([np.full((h, w), c, float) for c in base]) + shade[..., None]
    img = from_array(rgb)
    if fibre:
        rng = np.random.default_rng(seed + 3)
        layer = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        for _ in range(int(w * h / 900)):
            x, y = rng.uniform(0, w), rng.uniform(0, h)
            ang, length = rng.uniform(0, math.pi), rng.uniform(4, 22)
            bend = rng.uniform(-3, 3)
            pts = []
            for t in np.linspace(0, 1, 6):
                px = x + math.cos(ang) * length * t + math.sin(ang) * bend * math.sin(t * math.pi)
                py = y + math.sin(ang) * length * t - math.cos(ang) * bend * math.sin(t * math.pi)
                pts.append((px, py))
            dark = rng.random() < .55
            col = (120, 100, 70, int(rng.uniform(18, 42))) if dark else (255, 250, 236, int(rng.uniform(30, 60)))
            offsets = [(0, 0)]
            if wrap:
                offsets = [(dx, dy) for dx in (-w, 0, w) for dy in (-h, 0, h)]
            for dx, dy in offsets:
                d.line([(px + dx, py + dy) for px, py in pts], fill=col, width=1)
        img = Image.alpha_composite(img, layer)
    return img


def save(img, path, wrap=False, mipmaps=False, compress=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True)
    write_texture_meta(path, wrap=wrap, mipmaps=mipmaps, compress=compress)
    print('baked', path.relative_to(ROOT), img.size)


META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: {mip}
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: {wrap}
    wrapV: {wrap}
    wrapW: {wrap}
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationMethod: 0
  spriteTessellationDetail: -1
  spriteGeometrySubdivision: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: {comp}
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def stable_guid(path):
    """Deterministic GUID per asset path, so re-baking never breaks references."""
    rel = path.relative_to(ASSETS).as_posix()
    return uuid.uuid5(uuid.NAMESPACE_URL, 'jianghu-unity/' + rel).hex


def write_texture_meta(path, wrap, mipmaps, compress):
    meta = Path(str(path) + '.meta')
    if meta.exists():
        # keep the GUID of an existing asset (e.g. the territory masks)
        found = re.search(r'guid: ([0-9a-f]{32})', meta.read_text(encoding='utf-8'))
        guid = found.group(1) if found else stable_guid(path)
    else:
        guid = stable_guid(path)
    meta.write_text(META_TEMPLATE.format(guid=guid, mip=1 if mipmaps else 0, wrap=0 if wrap else 1,
                                         comp=1 if compress else 0), encoding='utf-8', newline='\n')


def write_folder_meta(folder):
    meta = Path(str(folder) + '.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n'
                        '  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % stable_guid(folder),
                        encoding='utf-8', newline='\n')


# ---------------------------------------------------------------- UI skin
def rough_rect(d, box, color, width, seed, wobble=.6):
    """Hand-ruled rectangle: slight wobble so lines read as brush/print, not vector."""
    rng = np.random.default_rng(seed)
    x0, y0, x1, y1 = box
    def edge(a, b, steps=24):
        pts = []
        for t in np.linspace(0, 1, steps):
            pts.append((a[0] + (b[0] - a[0]) * t + rng.normal(0, wobble * .4),
                        a[1] + (b[1] - a[1]) * t + rng.normal(0, wobble * .4)))
        return pts
    for a, b in (((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))):
        d.line(edge(a, b), fill=color, width=width, joint='curve')


BG = (24, 30, 30)
PANEL = (30, 37, 36)
LINE = (88, 102, 98)
TEXT = (226, 217, 196)
JADE = (62, 143, 123)
BRASS = (201, 163, 90)
RED = (142, 42, 32)


def cloud_swirls(d, w, h, rng, count, col, wrap=True):
    for _ in range(count):
        cx, cy, r = rng.uniform(0, w), rng.uniform(0, h), rng.uniform(10, 34)
        offs = [(dx, dy) for dx in (-w, 0, w) for dy in (-h, 0, h)] if wrap else [(0, 0)]
        for dx, dy in offs:
            x, y = cx + dx, cy + dy
            d.arc((x - r, y - r, x + r, y + r), 160, 470, fill=col, width=2)
            d.arc((x - r * .5, y - r * .5, x + r * .5, y + r * .5), 0, 300, fill=col, width=2)
            d.line((x + r, y, x + r * 2.6, y), fill=col, width=2)


def dark_field(w, h, base, seed, swirls=0, wrap=True):
    if wrap and w == h:
        low, grain = tile_noise(w, 46, seed), tile_noise(w, .9, seed + 1)
    else:
        low, grain = noise((h, w), 46, seed), noise((h, w), .9, seed + 1)
    shade = (low - .5) * 12 + (grain - .5) * 5
    img = from_array(np.dstack([np.full((h, w), c, float) for c in base]) + shade[..., None])
    if swirls:
        layer = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        cloud_swirls(ImageDraw.Draw(layer), w, h, np.random.default_rng(seed + 5), swirls, (255, 255, 255, 9), wrap)
        img = Image.alpha_composite(img, layer)
    return img


def bake_ui():
    UI.mkdir(parents=True, exist_ok=True)
    write_folder_meta(UI)

    # backgrounds -------------------------------------------------------------
    save(dark_field(512, 512, BG, 201, swirls=18), UI / 'bg.png', wrap=True, mipmaps=True)
    save(paper_field(512, PAPER, 11), UI / 'paper.png', wrap=True, mipmaps=True)
    save(paper_field(512, (246, 240, 225), 12), UI / 'paper_light.png', wrap=True, mipmaps=True)

    s = 512
    grain = tile_noise(s, 1.2, 21)
    streak = ndimage.gaussian_filter(np.random.default_rng(22).random((s, s)), (40, 0.7), mode='wrap')
    streak = (streak - streak.min()) / (streak.max() - streak.min())
    shade = (streak - .5) * 26 + (grain - .5) * 8 + (tile_noise(s, 50, 23) - .5) * 16
    save(from_array(np.dstack([np.full((s, s), c, float) for c in WOOD]) + shade[..., None]), UI / 'stage.png', wrap=True, mipmaps=True)

    s = 256
    yy, xx = np.mgrid[0:s, 0:s] / (s - 1)
    t = np.clip(np.sqrt(((xx - .5) / .62) ** 2 + ((yy - .42) / .7) ** 2), 0, 1) ** 1.6
    c0, c1 = np.array([250, 234, 200.]), np.array([186, 132, 72.])
    glow = c0[None, None] * (1 - t[..., None]) + c1[None, None] * t[..., None] + (noise((s, s), .7, 31)[..., None] - .5) * 6
    save(from_array(glow), UI / 'screen.png')

    # screen vignette (drawn over the full-screen map) -------------------------
    s = 256
    yy, xx = np.mgrid[0:s, 0:s] / (s - 1)
    r = np.sqrt(((xx - .5) / .5) ** 2 + ((yy - .5) / .5) ** 2)
    a = np.clip((r - .62) / .55, 0, 1) ** 1.5 * 215
    save(from_array(np.zeros((s, s, 3)) + np.array(BG)[None, None] * .5, a), UI / 'vignette.png')

    # panels ------------------------------------------------------------------
    def panel(name, fill, alpha, border, inner=True, corners=True, size=64):
        img = dark_field(size, size, fill, 211 + len(name), wrap=False)
        img.putalpha(alpha)
        d = ImageDraw.Draw(img)
        d.rectangle((0, 0, size - 1, size - 1), outline=rgba(border, 255), width=1)
        if inner:
            d.rectangle((3, 3, size - 4, size - 4), outline=rgba(border, 90), width=1)
        if corners:
            for cx, cy in ((3, 3), (size - 4, 3), (3, size - 4), (size - 4, size - 4)):
                d.polygon([(cx, cy - 3), (cx + 3, cy), (cx, cy + 3), (cx - 3, cy)], fill=rgba(BRASS, 200))
        save(img, UI / name)
    panel('panel.png', PANEL, 236, LINE)
    panel('card.png', (40, 49, 47), 215, (74, 88, 84), inner=False, corners=False)
    panel('panel_solid.png', (27, 33, 33), 255, LINE)

    # thin light double rule with transparent centre (map frame, sections)
    img = Image.new('RGBA', (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle((2, 2, 125, 125), outline=rgba(LINE, 230), width=2)
    d.rectangle((7, 7, 120, 120), outline=rgba(LINE, 120), width=1)
    for cx, cy in ((7, 7), (120, 7), (7, 120), (120, 120)):
        d.polygon([(cx, cy - 4), (cx + 4, cy), (cx, cy + 4), (cx - 4, cy)], fill=rgba(BRASS, 220))
    save(img, UI / 'frame_book.png')

    s, band = 128, 12
    wood_tile = Image.open(UI / 'stage.png').crop((0, 0, s, s))
    mask = Image.new('L', (s, s), 0)
    ImageDraw.Draw(mask).rectangle((0, 0, s - 1, s - 1), fill=255)
    ImageDraw.Draw(mask).rectangle((band, band, s - 1 - band, s - 1 - band), fill=0)
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    img.paste(wood_tile, (0, 0), mask)
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, s - 1, s - 1), outline=(14, 10, 8, 255), width=2)
    d.rectangle((band - 3, band - 3, s - band + 2, s - band + 2), outline=rgba(BRASS, 230), width=1)
    save(img, UI / 'frame_stage.png')

    # buttons -------------------------------------------------------------------
    def button(name, fill, border, top=None, bottom=None, alpha=255):
        b = dark_field(64, 64, fill, 231 + len(name), wrap=False)
        b.putalpha(alpha)
        d = ImageDraw.Draw(b)
        d.rectangle((0, 0, 63, 63), outline=rgba(border, 255), width=1)
        if top:
            d.line((1, 1, 62, 1), fill=top, width=1)
        if bottom:
            d.line((1, 62, 62, 62), fill=bottom, width=1)
        save(b, UI / name)
    button('btn.png', (42, 51, 49), (86, 101, 96), top=(120, 134, 128, 90), bottom=(14, 18, 18, 255))
    button('btn_hover.png', (55, 66, 63), (150, 140, 110), top=(170, 170, 150, 110), bottom=(14, 18, 18, 255))
    button('btn_active.png', (28, 34, 33), (150, 140, 110), top=(10, 12, 12, 255))
    button('btn_on.png', RED, (90, 22, 16), top=rgba(BRASS, 170), bottom=(70, 18, 12, 255))
    button('btn_off.png', (34, 40, 39), (60, 70, 67), alpha=200)

    img = Image.new('RGBA', (64, 64), (0, 0, 0, 0))
    ImageDraw.Draw(img).rectangle((6, 59, 57, 60), fill=rgba(BRASS, 170))
    save(img, UI / 'tab_hover.png')

    # vermilion brush swash for the selected tab (ragged ends, dry-brush streaks)
    w, h = 256, 64
    m = np.zeros((h, w))
    yy, xx = np.mgrid[0:h, 0:w]
    rng = np.random.default_rng(241)
    top = 10 + noise((1, w), 6, 242)[0] * 6
    bot = 54 - noise((1, w), 6, 243)[0] * 6
    left = 6 + noise((h, 1), 3, 244)[:, 0] * 22
    right = w - 6 - noise((h, 1), 3, 245)[:, 0] * 22
    m = ((yy > top[None, :]) & (yy < bot[None, :]) & (xx > left[:, None]) & (xx < right[:, None])).astype(float)
    streaks = noise((h, w), (0.6), 246)
    streaks = ndimage.gaussian_filter(np.random.default_rng(247).random((h, w)), (0.5, 18))
    streaks = (streaks - streaks.min()) / (streaks.max() - streaks.min())
    m *= np.where(streaks > .78, .55, 1)
    m = ndimage.gaussian_filter(m, .7)
    col = np.dstack([np.full((h, w), c, float) for c in (150, 40, 30)]) + (noise((h, w), 4, 248)[..., None] - .5) * 40
    save(from_array(col, m * 255), UI / 'tab_brush.png')

    # list rows ------------------------------------------------------------------
    img = Image.new('RGBA', (64, 64), (0, 0, 0, 0))
    ImageDraw.Draw(img).line((2, 63, 61, 63), fill=rgba(LINE, 120), width=1)
    save(img, UI / 'row.png')
    img = Image.new('RGBA', (64, 64), (255, 255, 255, 14))
    ImageDraw.Draw(img).line((2, 63, 61, 63), fill=rgba(LINE, 160), width=1)
    save(img, UI / 'row_hover.png')
    img = Image.new('RGBA', (64, 64), (120, 36, 28, 120))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 3, 63), fill=rgba(BRASS, 255))
    d.line((4, 63, 63, 63), fill=rgba(LINE, 160), width=1)
    save(img, UI / 'row_on.png')

    img = Image.new('RGBA', (64, 64), (18, 23, 23, 240))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 63, 63), outline=rgba(LINE, 255), width=1)
    d.line((0, 62, 63, 62), fill=rgba(BRASS, 150), width=1)
    save(img, UI / 'field.png')

    # seal -------------------------------------------------------------------------
    s = 128
    m = np.zeros((s, s))
    m[6:s - 6, 6:s - 6] = 1
    edge = ndimage.distance_transform_edt(m)
    m = np.where(edge < 5, (noise((s, s), 1.4, 71) > .45 - edge * .06).astype(float), m)
    m *= np.where(noise((s, s), 2.5, 72) > .86, .55, 1)
    m = ndimage.gaussian_filter(m, .6)
    rgb = np.dstack([np.full((s, s), c, float) for c in (152, 46, 32)]) + (noise((s, s), 3, 73)[..., None] - .5) * 30
    save(from_array(rgb, m * 255), UI / 'seal.png')

    # scrollbars, sliders, toggles ---------------------------------------------------
    img = Image.new('RGBA', (16, 64), (0, 0, 0, 0))
    ImageDraw.Draw(img).rounded_rectangle((5, 2, 10, 61), radius=3, fill=rgba(BRASS, 150))
    save(img, UI / 'scroll_thumb.png')
    img = Image.new('RGBA', (16, 64), (0, 0, 0, 0))
    ImageDraw.Draw(img).line((7, 0, 7, 63), fill=rgba(LINE, 120), width=1)
    save(img, UI / 'scroll_track.png')
    img = Image.new('RGBA', (64, 16), (0, 0, 0, 0))
    ImageDraw.Draw(img).line((0, 8, 63, 8), fill=rgba(LINE, 220), width=2)
    save(img, UI / 'slider_track.png')
    img = Image.new('RGBA', (32, 32), (0, 0, 0, 0))
    ImageDraw.Draw(img).ellipse((6, 6, 25, 25), fill=rgba(RED), outline=rgba(BRASS))
    save(img, UI / 'slider_thumb.png')
    for name, on in (('toggle.png', False), ('toggle_on.png', True)):
        img = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        d.rectangle((1, 1, 13, 13), fill=(18, 23, 23, 255), outline=rgba(LINE, 255), width=1)
        if on:
            d.rectangle((4, 4, 10, 10), fill=rgba(BRASS))
        save(img, UI / name)

    # curtain + ornament ---------------------------------------------------------------
    w, h = 128, 256
    folds = .5 + .5 * np.cos(np.arange(w) / w * math.pi * 6)
    shade = folds[None, :] * 36 - 18 + (noise((h, w), 1, 81) - .5) * 6
    save(from_array(np.dstack([np.full((h, w), c, float) for c in (96, 28, 22)]) + shade[..., None]), UI / 'curtain.png', wrap=True)
    img = Image.new('RGBA', (64, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.arc((4, 6, 24, 26), 200, 520, fill=rgba(BRASS, 230), width=2)
    d.arc((10, 12, 20, 22), 0, 300, fill=rgba(BRASS, 230), width=2)
    d.line((24, 16, 60, 16), fill=rgba(BRASS, 200), width=1)
    save(img, UI / 'ornament.png')

    # top bar: dark band, brass rule and a row of small diamonds at the bottom edge ---------
    w, h = 512, 64
    bar = dark_field(w, h, (17, 22, 22), 261, wrap=False)
    g = np.linspace(1, .55, h)[:, None]
    arr = np.array(bar).astype(float)
    arr[..., 3] = 255 * np.clip(g * 1.0 + .2, 0, 1)
    bar = from_array(arr[..., :3], arr[..., 3])
    d = ImageDraw.Draw(bar)
    d.line((0, h - 6, w, h - 6), fill=rgba(BRASS, 160), width=1)
    d.line((0, h - 3, w, h - 3), fill=rgba(LINE, 120), width=1)
    save(bar, UI / 'topbar.png', wrap=True)

    # bottom bar: dark band with a raised central arch for the month dial -----------------
    W2, H2 = 2048, 192
    yy, xx = np.mgrid[0:H2, 0:W2]
    arch = 64 - 40 * np.exp(-((xx - W2 / 2) / 330.0) ** 2)          # top edge of the band
    wing = np.where(np.abs(xx - W2 / 2) < 900, 0, 0)
    inside = yy > arch + wing
    base = np.array(dark_field(W2, H2, (21, 27, 27), 271, swirls=60, wrap=False).convert('RGB'), float)
    alpha = np.where(inside, 248, 0).astype(float)
    alpha = ndimage.gaussian_filter(alpha, .8)
    img = from_array(base, alpha)
    d = ImageDraw.Draw(img)
    pts = [(x, float(64 - 40 * math.exp(-((x - W2 / 2) / 330.0) ** 2))) for x in range(0, W2, 4)]
    d.line(pts, fill=rgba(BRASS, 200), width=2)
    d.line([(x, y + 6) for x, y in pts], fill=rgba(LINE, 140), width=1)
    save(img, UI / 'bottombar.png')

    # month dial: brass disc with a dark rim and an outer ornament ring -------------------
    s = 256
    yy, xx = np.mgrid[0:s, 0:s] - (s - 1) / 2
    r = np.sqrt(xx ** 2 + yy ** 2) / (s / 2)
    light = np.clip(1 - np.sqrt((xx + 30) ** 2 + (yy + 40) ** 2) / (s * .55), 0, 1)
    disc = np.array([168, 120, 44.])[None, None] + (np.array([238, 206, 120.]) - np.array([168, 120, 44.]))[None, None] * light[..., None]
    disc += (noise((s, s), 2.2, 281)[..., None] - .5) * 26
    col = np.where((r < .80)[..., None], disc, np.array([22, 27, 27.])[None, None])
    col = np.where(((r > .80) & (r < .84))[..., None], np.array(BRASS, float)[None, None], col)
    col = np.where(((r > .93) & (r < .96))[..., None], np.array(BRASS, float)[None, None] * .8, col)
    a = np.where(r < .98, 255, 0).astype(float)
    save(from_array(col, ndimage.gaussian_filter(a, .7)), UI / 'moon.png')

    # icon medallion and the big world/city toggle --------------------------------------
    def medal(name, s, rim, fill, ring2=True):
        yy, xx = np.mgrid[0:s, 0:s] - (s - 1) / 2
        r = np.sqrt(xx ** 2 + yy ** 2) / (s / 2)
        shade = np.clip(1.15 - r * .6 - yy / s * .5, .4, 1.2)
        col = np.array(fill, float)[None, None] * shade[..., None]
        col = np.where(((r > .86) & (r < .95))[..., None], np.array(rim, float)[None, None], col)
        if ring2:
            col = np.where(((r > .74) & (r < .77))[..., None], np.array(rim, float)[None, None] * .7, col)
        a = np.where(r < .96, 245, 0).astype(float)
        save(from_array(col, ndimage.gaussian_filter(a, .7)), UI / name)
    medal('medallion.png', 128, BRASS, (40, 50, 48))
    medal('medallion_on.png', 128, BRASS, (120, 36, 28))
    medal('globe.png', 256, BRASS, (34, 44, 42))

    # portrait ring: jade ring, outside filled with the screen background colour ---------
    s = 256
    yy, xx = np.mgrid[0:s, 0:s] - (s - 1) / 2
    r = np.sqrt(xx ** 2 + yy ** 2) / (s / 2)
    col = np.zeros((s, s, 3)) + np.array(BG, float)[None, None]
    col = np.where(((r > .86) & (r < .93))[..., None], np.array(JADE, float)[None, None], col)
    col = np.where(((r > .93) & (r < .95))[..., None], np.array(BRASS, float)[None, None] * .8, col)
    a = np.where(r > .86, 255, 0).astype(float)
    save(from_array(col, ndimage.gaussian_filter(a, .6)), UI / 'ring_portrait.png')

    # close button (crossed brushes) -------------------------------------------------
    img = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for a1, a2 in (((40, 40), (216, 216)), ((216, 40), (40, 216))):
        d.line((a1, a2), fill=rgba(TEXT, 235), width=14)
    for cx, cy in ((128, 30), (128, 226), (30, 128), (226, 128)):
        d.polygon([(cx, cy - 14), (cx + 14, cy), (cx, cy + 14), (cx - 14, cy)], fill=rgba(TEXT, 200))
    save(img.resize((64, 64), Image.LANCZOS), UI / 'close.png')

    # map label plate --------------------------------------------------------------------
    img = Image.new('RGBA', (32, 32), (18, 23, 23, 200))
    ImageDraw.Draw(img).rectangle((0, 0, 31, 31), outline=rgba(LINE, 220), width=1)
    save(img, UI / 'tag.png')
    img = Image.new('RGBA', (32, 32), (120, 36, 28, 230))
    ImageDraw.Draw(img).rectangle((0, 0, 31, 31), outline=rgba(BRASS, 255), width=1)
    save(img, UI / 'tag_on.png')


# ---------------------------------------------------------------- atlas
def lonlat(lon, lat):
    return 25 + (lon - 73) * 16.5, 25 + (54 - lat) * 18.5


# Generalised mountain ranges (lon, lat) for a painted map; not survey data.
RANGES = [
    ('喜马拉雅', [(75, 34.5), (79, 31.0), (83, 29.2), (88, 27.9), (92, 27.6), (95, 28.6)], 1.25),
    ('昆仑', [(76, 36.6), (81, 36.0), (86, 36.1), (91, 36.0), (96, 35.6), (99, 35.2)], 1.1),
    ('冈底斯 · 唐古拉', [(81, 31.6), (86, 31.1), (91, 32.5), (95, 33.0)], .9),
    ('天山', [(75, 41.0), (79, 42.1), (83, 43.0), (87, 43.4), (91, 43.2), (94, 43.0)], 1.0),
    ('阿尔泰', [(86, 49.0), (89, 47.8), (92, 46.8), (95, 45.6)], .85),
    ('祁连', [(95, 39.4), (98, 38.8), (101, 37.9), (103, 37.0)], .9),
    ('横断', [(98.5, 32.5), (99.5, 29.5), (100, 27.0), (100.5, 25.0)], .95),
    ('秦岭', [(104, 34.0), (106.5, 33.9), (109, 33.9), (111.5, 33.6)], .85),
    ('大巴', [(106, 32.3), (108.5, 31.9), (110.5, 31.6)], .7),
    ('太行', [(113.2, 39.5), (113.6, 38.0), (113.6, 36.5), (113.0, 35.2)], .8),
    ('阴山', [(106.5, 41.4), (109, 41.2), (111.5, 41.3), (113.5, 41.4)], .75),
    ('大兴安岭', [(120.5, 51.0), (121.5, 48.5), (121.0, 46.5), (119.6, 44.2)], .8),
    ('长白', [(126.5, 43.5), (128.0, 42.2), (129.0, 41.6)], .75),
    ('南岭', [(110.0, 25.5), (112.0, 25.3), (114.0, 25.0), (116.0, 25.0)], .65),
    ('武夷', [(116.5, 26.6), (117.5, 27.4), (118.5, 28.2)], .65),
    ('太白 · 小白', [(127.0, 39.6), (128.4, 37.8), (128.6, 36.4)], .6),
    ('日本中央', [(136.0, 35.6), (137.4, 36.0), (138.2, 35.8), (139.3, 36.6), (140.3, 38.5)], .6),
]
PLATEAU = [(77, 35.5), (80, 33.0), (83, 30.0), (88, 28.6), (93, 28.8), (98, 29.0), (102, 31.0), (102.5, 34.5),
           (99, 37.0), (93, 37.6), (86, 37.0), (79, 36.6)]
DESERTS = [[(78, 38.0), (82, 37.4), (87, 37.6), (89.5, 39.4), (87, 40.8), (82, 41.0), (78.5, 40.0)],
           [(96, 41.2), (101, 40.4), (106, 41.0), (110, 42.4), (111, 44.5), (105, 45.0), (99, 44.2), (96, 43.0)]]
STEPPE = [(98, 46.0), (104, 47.5), (110, 47.4), (116, 47.6), (118, 45.2), (114, 43.6), (108, 43.4), (101, 44.6)]
FOREST = [(122, 47.5), (126, 49.5), (131, 48.0), (133, 45.5), (130, 43.6), (126, 44.2), (123, 45.5)]


def polygons(path):
    for section in re.split('[Mm]', path):
        nums = [float(n) for n in re.findall(r'-?\d+(?:\.\d+)?', section)]
        if len(nums) >= 6:
            yield list(zip(nums[::2], nums[1::2]))


def polylines(path):
    for section in re.split('[Mm]', path):
        nums = [float(n) for n in re.findall(r'-?\d+(?:\.\d+)?', section)]
        if len(nums) >= 4:
            yield list(zip(nums[::2], nums[1::2]))


def catmull(points, steps=10):
    if len(points) < 3:
        return points
    pts = [points[0]] + points + [points[-1]]
    out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        for t in np.linspace(0, 1, steps, endpoint=False):
            t2, t3 = t * t, t * t * t
            out.append(tuple(.5 * ((2 * p1[k]) + (-p0[k] + p2[k]) * t + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t2
                                   + (-p0[k] + 3 * p1[k] - 3 * p2[k] + p3[k]) * t3) for k in (0, 1)))
    out.append(points[-1])
    return out


def bake_atlas():
    geo = json.loads((ROOT / 'static' / 'geography-compact.json').read_text(encoding='utf-8'))
    W, H = 2048, 1216          # drawn at 1.6x of the 1280x760 map space, saved at 2048x1024
    k = W / 1280
    S = lambda p: (p[0] * k, p[1] * k)

    land = Image.new('L', (W, H), 0)
    dl = ImageDraw.Draw(land)
    for poly in polygons(geo['land']):
        dl.polygon([S(p) for p in poly], fill=255)
    land_np = np.array(land) > 127

    # ---- base: sea wash + land paper ------------------------------------------------
    sea_dist = ndimage.distance_transform_edt(~land_np)
    land_dist = ndimage.distance_transform_edt(land_np)
    paper = np.array(paper_field((H, W), (236, 226, 202), 101, fibre=False, wrap=False).convert('RGB'), float)
    sea = np.array(paper_field((H, W), (206, 214, 200), 102, fibre=False, wrap=False).convert('RGB'), float)
    # sea gets slightly deeper away from the coast, like a graded wash
    deep = np.clip(sea_dist / 260, 0, 1)[..., None]
    sea = sea * (1 - deep * .06) + np.array([150, 170, 165.])[None, None] * deep * .06
    # land edge: faint darker band just inside the coast (pigment pools at the edge)
    rim = np.exp(-land_dist / 10)[..., None]
    paper = paper * (1 - rim * .10) + np.array([170, 150, 112.])[None, None] * rim * .10
    base = np.where(land_np[..., None], paper, sea)
    # aged vignette
    yy, xx = np.mgrid[0:H, 0:W]
    v = np.clip(np.sqrt(((xx - W / 2) / (W * .62)) ** 2 + ((yy - H / 2) / (H * .62)) ** 2) - .6, 0, 1)
    base = base * (1 - v[..., None] * .12)
    base_img = from_array(base)

    # plateau and terrain washes (on the base so territory washes sit on top)
    terr = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    dt = ImageDraw.Draw(terr)
    dt.polygon([S(lonlat(*p)) for p in catmull(PLATEAU, 6)], fill=(196, 170, 120, 55))
    for poly in DESERTS:
        dt.polygon([S(lonlat(*p)) for p in catmull(poly, 6)], fill=(214, 186, 128, 60))
    terr = terr.filter(ImageFilter.GaussianBlur(14))
    terr.putalpha(ImageChops.multiply(terr.getchannel('A'), land))
    base_img = Image.alpha_composite(base_img, terr)

    # ---- ink overlay: wave rings, coast, rivers, mountains, deserts, steppe -----------
    ink = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    di = ImageDraw.Draw(ink)

    # concentric wave rings offshore (水纹)
    rings = np.zeros((H, W))
    for dist, alpha in ((9, 62), (21, 40), (36, 22)):
        band = (np.abs(sea_dist - dist) < .9) & ~land_np
        rings = np.maximum(rings, band * alpha)
    wob = noise((H, W), 3, 111) > .32           # break the rings into strokes
    rings *= wob
    ring_img = Image.fromarray(rings.astype(np.uint8), 'L').filter(ImageFilter.GaussianBlur(.5))
    ink = Image.alpha_composite(ink, Image.merge('RGBA', [Image.new('L', (W, H), c) for c in (70, 98, 100)] + [ring_img]))
    di = ImageDraw.Draw(ink)

    # coastline: vector ink line along every land polygon
    for poly in polygons(geo['land']):
        pts = [S(p) for p in poly]
        if len(pts) > 2:
            di.line(pts + [pts[0]], fill=rgba(INK, 215), width=2, joint='curve')

    rng = np.random.default_rng(121)

    # desert stipple (sparse, soft)
    for poly in DESERTS:
        pts = [S(lonlat(*p)) for p in catmull(poly, 6)]
        mask = Image.new('L', (W, H), 0)
        ImageDraw.Draw(mask).polygon(pts, fill=255)
        m = ndimage.gaussian_filter((np.array(mask) > 0).astype(float), 18)
        ys, xs = np.nonzero((m > .05) & land_np)
        for i in rng.choice(len(xs), size=min(len(xs), 1600), replace=False):
            if rng.random() > m[ys[i], xs[i]]:
                continue
            r = rng.uniform(.6, 1.2)
            di.ellipse((xs[i] - r, ys[i] - r, xs[i] + r, ys[i] + r), fill=(150, 112, 60, 95))

    # steppe: soft green wash, a few grass tufts
    mask = Image.new('L', (W, H), 0)
    ImageDraw.Draw(mask).polygon([S(lonlat(*p)) for p in catmull(STEPPE, 6)], fill=255)
    wash = Image.new('RGBA', (W, H), (128, 148, 98, 0))
    wash.putalpha(ImageChops.multiply(mask.filter(ImageFilter.GaussianBlur(26)).point(lambda v: v * 50 // 255), land))
    ink = Image.alpha_composite(ink, wash)
    di = ImageDraw.Draw(ink)
    ys, xs = np.nonzero((np.array(mask) > 0) & land_np)
    for i in rng.choice(len(xs), size=min(len(xs), 120), replace=False):
        x, y = xs[i], ys[i]
        for dx, top in ((-4, -5), (0, -8), (4, -5)):
            di.line((x, y, x + dx, y + top), fill=(80, 100, 64, 150), width=1)

    # forest: sparse little pines
    mask = Image.new('L', (W, H), 0)
    ImageDraw.Draw(mask).polygon([S(lonlat(*p)) for p in catmull(FOREST, 6)], fill=255)
    ys, xs = np.nonzero((np.array(mask) > 0) & land_np)
    trees = sorted((ys[i], xs[i]) for i in rng.choice(len(xs), size=min(len(xs), 110), replace=False))
    for y, x in trees:
        h = rng.uniform(10, 15)
        di.polygon([(x, y - h), (x - h * .38, y - h * .35), (x + h * .38, y - h * .35)], fill=(78, 110, 84, 200), outline=(46, 60, 46, 200))
        di.polygon([(x, y - h * .62), (x - h * .48, y), (x + h * .48, y)], fill=(70, 100, 78, 200), outline=(46, 60, 46, 200))
        di.line((x, y, x, y + 3), fill=(46, 60, 46, 200), width=1)

    # mountains: 青绿 peaks (mineral blue-green tops fading to ochre), ink contour on the ridge only
    top_c, mid_c, base_c = np.array([66, 112, 118.]), np.array([116, 146, 110.]), np.array([216, 198, 152.])
    def peak(x, y, size, seed, fade=1.0):
        r = np.random.default_rng(seed)
        w = size * r.uniform(.95, 1.35)
        h = size * r.uniform(1.15, 1.65)
        lean = r.uniform(-.25, .25) * w
        iw, ih = int(2 * w + 6), int(h + 6)
        bl, br = (3, h + 3), (2 * w + 3, h + 3)
        top = (w + 3 + lean, 3)
        s1 = (w * r.uniform(.4, .6) + 3, h * r.uniform(.42, .6) + 3)
        s2 = (w * r.uniform(1.4, 1.65) + 3, h * r.uniform(.38, .55) + 3)
        ridge = catmull([bl, s1, top, s2, br], 6)
        mask = Image.new('L', (iw, ih), 0)
        ImageDraw.Draw(mask).polygon(ridge, fill=255)
        t = (np.arange(ih) / max(ih - 1, 1))[:, None]
        col = np.where(t < .45, top_c * (1 - t / .45) + mid_c * (t / .45), mid_c * (1 - (t - .45) / .55) + base_c * ((t - .45) / .55))
        col = np.repeat(col[:, None, :], iw, axis=1) if col.ndim == 2 else col
        col = np.broadcast_to(col, (ih, iw, 3)).copy()
        xs_ = (np.arange(iw) / iw)[None, :, None]
        col *= np.where(xs_ > (top[0] / iw), .86, 1.0)        # shadow side
        a = np.array(mask, float) * (.92 * fade)
        a *= np.clip(1.25 - t, 0, 1)                            # base melts into the paper
        img = from_array(col, a)
        d2 = ImageDraw.Draw(img)
        d2.line(ridge[:-1], fill=rgba(INK, int(200 * fade)), width=2, joint='curve')
        for j in range(r.integers(1, 3)):                       # 皴 strokes
            sx = top[0] + r.uniform(-.35, .35) * w
            d2.line([(sx, top[1] + h * .2), (sx + r.uniform(-.1, .1) * w, top[1] + h * r.uniform(.45, .7))], fill=rgba(INK, int(90 * fade)), width=1)
        ink.alpha_composite(img, (int(x - w - 3), int(y - h - 3)))

    seed = 1000
    for name, pts, scale in RANGES:
        line = [S(lonlat(*p)) for p in catmull(pts, 14)]
        total = sum(math.dist(a, b) for a, b in zip(line, line[1:]))
        step = 20 * scale
        dist_acc, cursor, idx = 0, 0.0, 0
        positions = []
        while cursor < total and idx < len(line) - 1:
            a, b = line[idx], line[idx + 1]
            seg = math.dist(a, b)
            if dist_acc + seg >= cursor:
                t = (cursor - dist_acc) / seg if seg else 0
                positions.append(((a[0] + (b[0] - a[0]) * t), (a[1] + (b[1] - a[1]) * t), cursor / total))
                cursor += step * rng.uniform(.8, 1.15)
            else:
                dist_acc += seg
                idx += 1
        marks = []
        for x, y, f in positions:
            size = (10 + 9 * math.sin(f * math.pi)) * scale
            for row, keep in ((-1, .55), (0, 1.0), (1, .35)):
                if rng.random() > keep:
                    continue
                ox, oy = rng.normal(0, size * .35), row * size * .7 + rng.normal(0, 2)
                xi, yi = int(x + ox), int(y + oy)
                if 0 <= xi < W and 0 <= yi < H and land_np[yi, xi]:
                    marks.append((y + oy, x + ox, size * (1 - (row == -1) * .2 - (row == 1) * .3), .75 if row == -1 else 1.0))
        for y, x, size, fade in sorted(marks):
            seed += 1
            peak(x, y, size, seed, fade)

    # rivers: tapered ink-blue strokes, thicker downstream
    for river in geo['rivers']:
        for line in polylines(river['path']):
            pts = [S(p) for p in catmull(line, 6)]
            n = len(pts)
            for i, (a, b) in enumerate(zip(pts, pts[1:])):
                width = 1 + 2.4 * (i / max(1, n - 1))
                di.line([a, b], fill=(52, 92, 104, 220), width=max(1, int(round(width))))
            di.line(pts, fill=(52, 92, 104, 120), width=1)

    # final resample to power-of-two height (non-uniform; drawn back into 1280x760 space)
    base_img = base_img.resize((2048, 1024), Image.LANCZOS)
    ink = ink.resize((2048, 1024), Image.LANCZOS)
    # feather the sheet edges so the scroll fades into the surrounding paper at any zoom
    fy, fx = np.mgrid[0:1024, 0:2048]
    edge = np.minimum.reduce([fx / 90, (2047 - fx) / 90, fy / 60, (1023 - fy) / 60])
    feather = np.clip(edge, 0, 1) ** 1.4
    base_img.putalpha(Image.fromarray((feather * 255).astype(np.uint8), 'L'))
    ink.putalpha(ImageChops.multiply(ink.getchannel('A'), Image.fromarray((feather * 255).astype(np.uint8), 'L')))
    save(base_img, MAP / 'atlas_base.png', mipmaps=True, compress=False)
    save(ink, MAP / 'atlas_ink.png', mipmaps=True, compress=False)

    # ---- territory washes: pigment pools at the edges of each cell ------------------
    land_small = Image.new('L', (1280, 760), 0)
    for poly in polygons(geo['land']):
        ImageDraw.Draw(land_small).polygon(poly, fill=255)
    territories = {}
    for territory in geo['territories']:
        m = Image.new('L', (1280, 760), 0)
        for poly in polygons(territory['path']):
            ImageDraw.Draw(m).polygon(poly, fill=255)
        territories[territory['country']] = m
    gran = noise((760, 1280), 1.2, 131)
    for cell in geo['cells']:
        m = Image.new('L', (1280, 760), 0)
        for poly in polygons(cell['path']):
            ImageDraw.Draw(m).polygon(poly, fill=255)
        m = ImageChops.multiply(ImageChops.multiply(m, territories[cell['native_country']]), land_small)
        a = np.array(m) > 127
        inside = ndimage.distance_transform_edt(a)
        alpha = np.where(a, .42 + .58 * np.exp(-np.maximum(inside - 1, 0) / 5.0), 0)
        alpha *= .88 + .24 * (gran - .5)
        alpha = ndimage.gaussian_filter(alpha, .5)
        img = Image.new('RGBA', (1280, 760), (255, 255, 255, 0))
        img.putalpha(Image.fromarray(np.clip(alpha * 255, 0, 255).astype(np.uint8), 'L'))
        path = MAP / f"cell-{cell['place']}.png"
        img.save(path, optimize=True)
    print('baked territory washes', len(geo['cells']))

    # ---- markers ----------------------------------------------------------------------
    def marker(size, draw_fn, name):
        img = Image.new('RGBA', (size * 4, size * 4), (0, 0, 0, 0))
        draw_fn(ImageDraw.Draw(img), size * 4)
        save(img.resize((size, size), Image.LANCZOS), MAP / name)

    def city(d, s):          # walled-town glyph: double square
        d.rectangle((s * .18, s * .18, s * .82, s * .82), fill=(246, 238, 218, 255), outline=rgba(INK), width=int(s * .07))
        d.rectangle((s * .36, s * .36, s * .64, s * .64), fill=rgba(INK, 230))
    marker(32, city, 'city.png')

    def capital(d, s):       # capital: vermilion seal with paper inner square
        d.rectangle((s * .1, s * .1, s * .9, s * .9), fill=rgba(VERMILION), outline=(80, 24, 16, 255), width=int(s * .05))
        d.rectangle((s * .3, s * .3, s * .7, s * .7), outline=(246, 238, 218, 255), width=int(s * .07))
    marker(32, capital, 'capital.png')

    def ring(d, s):
        d.ellipse((s * .06, s * .06, s * .94, s * .94), outline=rgba(VERMILION, 235), width=int(s * .07))
        d.ellipse((s * .2, s * .2, s * .8, s * .8), outline=rgba(VERMILION, 140), width=int(s * .03))
    marker(64, ring, 'ring.png')

    def pennant(d, s):       # hero banner
        d.line((s * .3, s * .08, s * .3, s * .95), fill=rgba(INK), width=int(s * .06))
        d.polygon([(s * .33, s * .1), (s * .9, s * .22), (s * .33, s * .42)], fill=rgba(VERMILION), outline=(80, 24, 16, 255))
    marker(48, pennant, 'pennant.png')

    def compass(d, s):       # 指北: 8-point rose in ink and vermilion
        c = s / 2
        d.ellipse((c - s * .40, c - s * .40, c + s * .40, c + s * .40), outline=rgba(INK, 200), width=int(s * .012))
        d.ellipse((c - s * .34, c - s * .34, c + s * .34, c + s * .34), outline=rgba(INK, 120), width=int(s * .006))
        for i in range(8):
            ang = i * math.pi / 4
            long = s * (.38 if i % 2 == 0 else .22)
            tip = (c + math.sin(ang) * long, c - math.cos(ang) * long)
            l = (c + math.sin(ang - math.pi / 2) * s * .05, c - math.cos(ang - math.pi / 2) * s * .05)
            r = (c + math.sin(ang + math.pi / 2) * s * .05, c - math.cos(ang + math.pi / 2) * s * .05)
            col = rgba(VERMILION, 240) if i == 0 else rgba(INK, 220 if i % 2 == 0 else 160)
            d.polygon([tip, l, (c, c)], fill=col)
            d.polygon([tip, r, (c, c)], fill=(246, 238, 218, 255), outline=col)
        d.ellipse((c - s * .03, c - s * .03, c + s * .03, c + s * .03), fill=rgba(INK))
    marker(128, compass, 'compass.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ui-only', action='store_true')
    args = parser.parse_args()
    bake_ui()
    if not args.ui_only:
        bake_atlas()


if __name__ == '__main__':
    main()
