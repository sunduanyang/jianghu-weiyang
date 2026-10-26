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


# ---------------------------------------------------------------- hand-made overrides
# The first skin above is built from perfect rectangles and 1px rules, which reads as
# "template UI".  These overrides keep the same file names and 9-slice borders but give
# every piece a mounted-silk / lacquered-wood / brushed-ink surface: ragged edges,
# pigment pooling at borders, cloud-head corners and an ink-mountain dock.
def wrap_noise_1d(n, sigma, seed):
    rng = np.random.default_rng(seed)
    v = ndimage.gaussian_filter1d(rng.random(n), sigma, mode='wrap')
    v -= v.min()
    return v / max(v.max(), 1e-6)


def ragged_mask(h, w, inset, rough, seed, soft=.7):
    """Rectangle whose edge wanders by up to `rough` px, like torn silk or a brushed border."""
    m = np.zeros((h, w))
    m[inset:h - inset, inset:w - inset] = 1
    dist = ndimage.distance_transform_edt(np.pad(m, 1))[1:-1, 1:-1]
    n = noise((h, w), 1.6, seed)
    keep = dist > (n * rough)
    return ndimage.gaussian_filter(keep.astype(float), soft)


def ink_wash(h, w, base, seed, stain=10, pool=0.0, pool_width=8):
    """Mottled wash with darker blooms; `pool` darkens the edge like pigment drying at the rim."""
    low = noise((h, w), max(h, w) / 5, seed)
    mid = noise((h, w), 4, seed + 1)
    grain = noise((h, w), .7, seed + 2)
    bloom = np.clip((noise((h, w), max(h, w) / 9, seed + 3) - .62) * 3, 0, 1)
    shade = (low - .5) * stain + (mid - .5) * stain * .5 + (grain - .5) * 6 - bloom * stain * .9
    rgb = np.dstack([np.full((h, w), c, float) for c in base]) + shade[..., None]
    if pool:
        m = np.zeros((h, w))
        m[1:-1, 1:-1] = 1
        d = ndimage.distance_transform_edt(m)
        rgb *= (1 - pool * np.exp(-d / pool_width))[..., None]
    return rgb


def wobble(d, a, b, col, width, rng, amp=.45, steps=None):
    steps = steps or max(6, int(math.hypot(b[0] - a[0], b[1] - a[1]) / 4))
    pts = []
    drift = 0.0
    for t in np.linspace(0, 1, steps):
        drift = drift * .7 + rng.normal(0, amp)
        nx, ny = -(b[1] - a[1]), b[0] - a[0]
        L = max(math.hypot(nx, ny), 1e-6)
        pts.append((a[0] + (b[0] - a[0]) * t + nx / L * drift, a[1] + (b[1] - a[1]) * t + ny / L * drift))
    d.line(pts, fill=col, width=width, joint='curve')


def cloud_head(size, col):
    """A 云头 corner bracket, drawn for the top-left corner (flip for the others)."""
    s = size * 4
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    w = max(3, s // 22)
    # the L bracket
    d.line((s * .08, s * .95, s * .08, s * .30), fill=col, width=w)
    d.line((s * .30, s * .08, s * .95, s * .08), fill=col, width=w)
    # spiral curl filling the corner
    cx, cy, r = s * .30, s * .30, s * .22
    d.arc((cx - r, cy - r, cx + r, cy + r), 90, 360, fill=col, width=w)
    r2 = r * .55
    d.arc((cx - r2 + r * .2, cy - r2, cx + r2 + r * .2, cy + r2), 180, 450, fill=col, width=w)
    d.ellipse((cx + r * .2 - w, cy - w, cx + r * .2 + w, cy + w), fill=col)
    # tails joining the bracket
    d.arc((s * .08 - r * .0, cy, s * .08 + r * 2 * .0 + r * .44, cy + r * .9), 90, 180, fill=col, width=w)
    return img.resize((size, size), Image.LANCZOS)


def paste_corners(img, corner, inset):
    w, h = img.size
    cw, ch = corner.size
    for flipx, flipy, x, y in ((False, False, inset, inset), (True, False, w - inset - cw, inset),
                              (False, True, inset, h - inset - ch), (True, True, w - inset - cw, h - inset - ch)):
        c = corner
        if flipx:
            c = c.transpose(Image.FLIP_LEFT_RIGHT)
        if flipy:
            c = c.transpose(Image.FLIP_TOP_BOTTOM)
        img.alpha_composite(c, (x, y))


def silk_panel(size, base, seed, alpha=238, rule=BRASS, corners=True, inner=True):
    rgb = ink_wash(size, size, base, seed, stain=12, pool=.35, pool_width=7)
    m = ragged_mask(size, size, 1, 2.2, seed + 9)
    img = from_array(rgb, m * alpha)
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(seed + 11)
    o = 5
    for a, b in (((o, o), (size - o, o)), ((size - o, o), (size - o, size - o)),
                 ((size - o, size - o), (o, size - o)), ((o, size - o), (o, o))):
        wobble(d, a, b, rgba(rule, 190), 1, rng, amp=.25)
    if inner:
        o = 9
        for a, b in (((o, o), (size - o, o)), ((size - o, o), (size - o, size - o)),
                     ((size - o, size - o), (o, size - o)), ((o, size - o), (o, o))):
            wobble(d, a, b, rgba(LINE, 80), 1, rng, amp=.2)
    if corners:
        paste_corners(img, cloud_head(18, rgba(rule, 225)), 2)
    return img


def plaque(w, h, base, seed, rule, alpha=245, grain_dir=0):
    """Lacquered wooden board: streaky grain, ragged edge, worn gilt frame."""
    rng = np.random.default_rng(seed)
    g = ndimage.gaussian_filter(rng.random((h, w)), (0.6, 14) if grain_dir == 0 else (14, .6))
    g = (g - g.min()) / max(g.max() - g.min(), 1e-6)
    rgb = ink_wash(h, w, base, seed + 1, stain=8, pool=.30, pool_width=5) + ((g - .5) * 22)[..., None]
    top = np.linspace(1.10, .88, h)[:, None, None]          # lamplight from above
    rgb *= top
    m = ragged_mask(h, w, 1, 1.6, seed + 2)
    img = from_array(rgb, m * alpha)
    d = ImageDraw.Draw(img)
    if rule:
        for o, a in ((3, 210), (6, 70)):
            for p, q in (((o, o), (w - o, o)), ((w - o, o), (w - o, h - o)), ((w - o, h - o), (o, h - o)), ((o, h - o), (o, o))):
                wobble(d, p, q, rgba(rule, a), 1, rng, amp=.2)
        # wear: break the gilt line here and there
        arr = np.array(img).astype(float)
        wear = noise((h, w), 1.2, seed + 3) > .70
        edge = np.zeros((h, w), bool)
        edge[:5, :] = edge[-5:, :] = edge[:, :5] = edge[:, -5:] = True
        arr[..., :3] = np.where((wear & edge)[..., None], rgb, arr[..., :3])
        img = from_array(arr[..., :3], arr[..., 3])
    return img


def brush_ring(s, r0, r1, seed, col, gap=.12):
    """Ring painted in one brush pass: thickness swells, a dry gap where the brush lifted."""
    yy, xx = np.mgrid[0:s, 0:s] - (s - 1) / 2
    r = np.sqrt(xx ** 2 + yy ** 2) / (s / 2)
    ang = (np.arctan2(yy, xx) + math.pi) / (2 * math.pi)
    k = np.arange(512)
    swell = wrap_noise_1d(512, 18, seed)[(ang * 511).astype(int)]
    thick = (r1 - r0) * (.65 + .7 * swell)
    mid = (r0 + r1) / 2
    # the stroke starts and ends at `lift`: taper both ends so the joint reads as one brush pass
    da = np.abs(((ang - .62 + .5) % 1) - .5)
    thick = thick * np.clip(da / max(gap, 1e-3), 0, 1) ** .6
    ring = np.abs(r - mid) < thick / 2
    a = ndimage.gaussian_filter(ring.astype(float), .6)
    return from_array(np.zeros((s, s, 3)) + np.array(col[:3], float)[None, None], a * (col[3] if len(col) > 3 else 255))


def ink_disc(name, s, fill, seed, rim=BRASS):
    yy, xx = np.mgrid[0:s, 0:s] - (s - 1) / 2
    r = np.sqrt(xx ** 2 + yy ** 2) / (s / 2)
    rgb = ink_wash(s, s, fill, seed, stain=14)
    rgb *= np.clip(1.12 - r * .35 - yy / s * .45, .55, 1.2)[..., None]
    edge = 0.90 + (wrap_noise_1d(512, 10, seed + 2)[((np.arctan2(yy, xx) + math.pi) / (2 * math.pi) * 511).astype(int)] - .5) * .03
    a = ndimage.gaussian_filter((r < edge).astype(float), .8) * 248
    img = from_array(rgb, a)
    # faint cloud scroll inside
    layer = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    cloud_swirls(ImageDraw.Draw(layer), s, s, np.random.default_rng(seed + 3), 3, rgba(rim, 18), wrap=False)
    layer.putalpha(ImageChops.multiply(layer.getchannel('A'), Image.fromarray((r < .7).astype(np.uint8) * 255)))
    img = Image.alpha_composite(img, layer)
    img = Image.alpha_composite(img, brush_ring(s, .82, .885, seed + 4, rgba(rim, 235), gap=.08))
    img = Image.alpha_composite(img, brush_ring(s, .70, .725, seed + 5, rgba(rim, 110), gap=.3))
    save(img, UI / name)


def ink_mountains(w, base_y, peak_y, seed, period, rough=6):
    """Ridge line (per column) for a range of soft ink peaks."""
    rng = np.random.default_rng(seed)
    x = np.arange(w)
    ridge = np.full(w, float(base_y))
    for _ in range(int(w / period)):
        c, hgt, wid = rng.uniform(0, w), rng.uniform(.35, 1) * (base_y - peak_y), rng.uniform(.5, 1.3) * period
        ridge = np.minimum(ridge, base_y - hgt * np.exp(-((x - c) / (wid * .42)) ** 2) ** .8)
    ridge += (noise((1, w), rough, seed + 1)[0] - .5) * rough * 1.6
    return ridge


def bake_handmade():
    # panels -----------------------------------------------------------------------
    save(silk_panel(96, PANEL, 401), UI / 'panel.png')
    save(silk_panel(96, (27, 33, 33), 402, alpha=255), UI / 'panel_solid.png')
    card = from_array(ink_wash(64, 64, (40, 49, 46), 403, stain=10, pool=.25, pool_width=5),
                      ragged_mask(64, 64, 1, 2.0, 404) * 212)
    save(card, UI / 'card.png')

    # buttons: small lacquered boards; the selected one is a vermilion stamp ----------
    save(plaque(64, 64, (42, 49, 45), 411, (110, 112, 96)), UI / 'btn.png')
    save(plaque(64, 64, (56, 62, 54), 412, BRASS), UI / 'btn_hover.png')
    save(plaque(64, 64, (30, 35, 33), 413, (120, 110, 84)), UI / 'btn_active.png')
    save(plaque(64, 64, (34, 40, 39), 414, (70, 78, 74), alpha=200), UI / 'btn_off.png')
    on = plaque(64, 64, (150, 44, 32), 415, (214, 170, 96))
    save(on, UI / 'btn_on.png')

    # name plaques for the map / city labels -----------------------------------------
    save(plaque(48, 48, (38, 30, 23), 421, BRASS, alpha=242), UI / 'plaque.png')
    save(plaque(48, 48, (128, 40, 28), 422, (222, 178, 100), alpha=248), UI / 'plaque_on.png')
    tag = from_array(ink_wash(32, 32, (24, 27, 25), 423, stain=8, pool=.3, pool_width=4), ragged_mask(32, 32, 1, 1.6, 424) * 215)
    save(tag, UI / 'tag.png')
    save(plaque(32, 32, (128, 40, 28), 425, (222, 178, 100)), UI / 'tag_on.png')

    # a horizontal dry-brush stroke for toolbars and the legend strip ----------------
    w, h = 256, 48
    yy, xx = np.mgrid[0:h, 0:w]
    top = 7 + noise((1, w), 5, 431)[0] * 5
    bot = h - 7 - noise((1, w), 5, 432)[0] * 5
    left = 4 + noise((h, 1), 2, 433)[:, 0] * 14
    right = w - 4 - noise((h, 1), 2, 434)[:, 0] * 14
    m = ((yy > top) & (yy < bot) & (xx > left[:, None]) & (xx < right[:, None])).astype(float)
    streak = ndimage.gaussian_filter(np.random.default_rng(435).random((h, w)), (.5, 20))
    streak = (streak - streak.min()) / (streak.max() - streak.min())
    m *= np.where(streak > .80, .55, 1) * np.where(streak < .12, .8, 1)
    m = ndimage.gaussian_filter(m, .7)
    save(from_array(ink_wash(h, w, (14, 20, 19), 436, stain=10), m * 225), UI / 'strip.png')

    # list rows: a wobbly hairline; selection is a brush wash that fades to the right --
    img = Image.new('RGBA', (64, 64), (0, 0, 0, 0))
    wobble(ImageDraw.Draw(img), (2, 62), (62, 62), rgba(LINE, 110), 1, np.random.default_rng(441), amp=.3)
    save(img, UI / 'row.png')
    img = from_array(ink_wash(64, 64, (60, 70, 64), 442, stain=8), np.full((64, 64), 26.))
    wobble(ImageDraw.Draw(img), (2, 62), (62, 62), rgba(BRASS, 120), 1, np.random.default_rng(443), amp=.3)
    save(img, UI / 'row_hover.png')
    fade = np.clip(1.15 - np.linspace(0, 1, 64), .35, 1)[None, :] * np.ones((64, 1))
    img = from_array(ink_wash(64, 64, (126, 36, 26), 444, stain=16), fade * 175 * ragged_mask(64, 64, 0, 1.2, 445))
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(446)
    for x in (1, 2, 3):
        wobble(d, (x, 2), (x, 62), rgba(BRASS, 230 - x * 30), 1, rng, amp=.2)
    save(img, UI / 'row_on.png')

    img = from_array(ink_wash(64, 64, (16, 20, 20), 451, stain=6, pool=.25), ragged_mask(64, 64, 1, 1.2, 452) * 240)
    d = ImageDraw.Draw(img)
    wobble(d, (2, 61), (62, 61), rgba(BRASS, 150), 1, np.random.default_rng(453), amp=.25)
    save(img, UI / 'field.png')

    # top bar: ink band with a brushed lower edge and a running 回纹 fret ---------------
    w, h = 512, 64                                         # drawn squeezed into the 58 px top bar
    edge = 59 + (wrap_noise_1d(w, 6, 461) - .5) * 6 + (wrap_noise_1d(w, 1.2, 462) - .5) * 2.5
    yy = np.mgrid[0:h, 0:w][0]
    alpha = np.clip((edge[None, :] - yy) / 2.0, 0, 1) * np.clip(1.05 - yy / h * .25, 0, 1) * 250
    # wrap-around noise (crop of a seamless square) so the bar tiles without a seam every 512 px
    shade = (tile_noise(w, 30, 463)[:h] - .5) * 10 + (tile_noise(w, 3, 464)[:h] - .5) * 5 + (tile_noise(w, .7, 465)[:h] - .5) * 5
    rgb = np.dstack([np.full((h, w), c, float) for c in (16, 21, 21)]) + shade[..., None]
    bar = from_array(rgb, alpha)
    d = ImageDraw.Draw(bar)
    fret = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    df = ImageDraw.Draw(fret)
    y0 = 50                                               # a low 回 fret just above the brushed edge, under the captions
    for x0 in range(0, w, 16):                            # one unit every 16 px, tiles at 512
        df.line([(x0, y0 + 6), (x0, y0), (x0 + 11, y0), (x0 + 11, y0 + 5), (x0 + 4, y0 + 5), (x0 + 4, y0 + 2), (x0 + 8, y0 + 2)],
                fill=rgba(BRASS, 34), width=1)
    bar = Image.alpha_composite(bar, fret)
    d = ImageDraw.Draw(bar)
    rng = np.random.default_rng(464)
    d.line([(x, edge[x] - 3 + (wrap_noise_1d(w, 3, 465)[x] - .5) * 1.5) for x in range(w)], fill=rgba(BRASS, 170), width=1)
    save(bar, UI / 'topbar.png', wrap=True)

    # bottom dock: misty ink mountains and rooflines rising behind a dark band -----------
    W2, H2 = 2048, 192
    yy, xx = np.mgrid[0:H2, 0:W2]
    img = Image.new('RGBA', (W2, H2), (0, 0, 0, 0))
    layers = [(ink_mountains(W2, 70, 0, 471, 260, 8), (52, 70, 66), 120),
              (ink_mountains(W2, 74, 18, 472, 170, 6), (34, 48, 45), 190),
              (ink_mountains(W2, 78, 40, 473, 120, 4), (22, 30, 29), 235)]
    for ridge, col, a in layers:
        inside = yy > ridge[None, :]
        mist = np.clip((yy - ridge[None, :]) / 46, 0, 1)            # peaks dark, feet fade into mist
        al = np.where(inside, a * (1 - .55 * mist), 0)
        al = ndimage.gaussian_filter(al, .8)
        img = Image.alpha_composite(img, from_array(ink_wash(H2, W2, col, int(a), stain=12), al))
    # a few pines on the nearest ridge
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(474)
    near = layers[2][0]
    for x in rng.choice(np.arange(60, W2 - 60), 22, replace=False):
        if abs(x - W2 / 2) < 220:
            continue
        y = near[x] + 3
        hgt = rng.uniform(10, 18)
        for k in range(3):
            yy0 = y - hgt * (k + 1) / 3.2
            wd = hgt * (.55 - k * .14)
            d.polygon([(x - wd, yy0 + hgt * .22), (x, yy0 - hgt * .12), (x + wd, yy0 + hgt * .22)], fill=(18, 25, 24, 235))
        d.line((x, y, x, y - hgt * .4), fill=(18, 25, 24, 235), width=1)
    # the dock band itself: arched under the month dial, brushed top edge
    edge = 72 - 44 * np.exp(-((xx[0] - W2 / 2) / 330.0) ** 2) + (noise((1, W2), 4, 475)[0] - .5) * 3
    band = np.clip((yy - edge[None, :]) / 1.5, 0, 1) * 250
    base = np.array(dark_field(W2, H2, (19, 25, 25), 476, swirls=60, wrap=False).convert('RGB'), float)
    img = Image.alpha_composite(img, from_array(base, band))
    d = ImageDraw.Draw(img)
    d.line([(x, edge[x]) for x in range(0, W2, 2)], fill=rgba(BRASS, 190), width=2)
    d.line([(x, edge[x] + 6 + (noise((1, W2), 9, 477)[0][x] - .5) * 2) for x in range(0, W2, 2)], fill=rgba(LINE, 120), width=1)
    save(img, UI / 'bottombar.png')

    # month dial: gold-leaf disc, squares of leaf, brushed dark rim, a cloud over its foot --
    s = 256
    yy, xx = np.mgrid[0:s, 0:s] - (s - 1) / 2
    r = np.sqrt(xx ** 2 + yy ** 2) / (s / 2)
    light = np.clip(1 - np.sqrt((xx + 30) ** 2 + (yy + 40) ** 2) / (s * .6), 0, 1)
    lo, hi = np.array([160, 114, 42.]), np.array([236, 202, 116.])
    disc = lo[None, None] + (hi - lo)[None, None] * light[..., None]
    rng = np.random.default_rng(481)
    leaf = np.zeros((s, s))
    for gy in range(0, s, 34):                                  # gold leaf laid in slightly uneven squares
        for gx in range(0, s, 34):
            leaf[gy:gy + 34, gx:gx + 34] = rng.normal(0, 3)
    leaf = ndimage.shift(leaf, (rng.uniform(0, 8), rng.uniform(0, 8)), mode='wrap')
    seams = (np.abs(((xx + s / 2 + 3) % 34) - 0) < .8) | (np.abs(((yy + s / 2 + 5) % 34) - 0) < .8)
    disc += leaf[..., None] + (noise((s, s), 2, 482)[..., None] - .5) * 22 - seams[..., None] * 5
    disc -= np.clip((noise((s, s), 9, 483) - .7) * 3, 0, 1)[..., None] * 40   # tarnish
    col = np.where((r < .80)[..., None], disc, np.array([20, 25, 25.])[None, None])
    a = np.where(r < .97, 255, 0).astype(float)
    img = from_array(col, ndimage.gaussian_filter(a, .7))
    img = Image.alpha_composite(img, brush_ring(s, .78, .83, 484, (60, 40, 20, 220), gap=.06))
    img = Image.alpha_composite(img, brush_ring(s, .91, .955, 485, rgba(BRASS, 220)))
    save(img, UI / 'moon.png')

    ink_disc('medallion.png', 128, (38, 48, 46), 491)
    ink_disc('medallion_on.png', 128, (122, 36, 26), 492)
    ink_disc('globe.png', 256, (32, 42, 40), 493)


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


# ---------------------------------------------------------------- ancient-map furniture
# The atlas follows the conventions of Song–Ming Chinese maps (《华夷图》《禹迹图》《大明混一图》):
# aged silk, a sea filled with scale waves, double-banked rivers with the Yellow River in yellow,
# the Great Wall as a crenellated line, a faint 计里画方 grid, a ruled frame and a title cartouche.
FONT_BOLD = ASSETS / 'Resources' / 'Fonts' / 'NotoSerifSC-Bold.ttf'
FONT_REGULAR = ASSETS / 'Resources' / 'Fonts' / 'NotoSerifSC-Regular.ttf'
MAP_FRAME = (58, 42)          # x / y inset of the ruled frame on the 2048x1216 sheet
GREAT_WALL = [(98.2, 39.8), (100.4, 38.9), (102.6, 37.9), (104.2, 37.4), (106.2, 38.6), (107.6, 38.1),
              (109.7, 38.4), (111.4, 39.4), (113.3, 40.2), (114.9, 40.6), (116.0, 40.4), (117.6, 40.3), (119.8, 40.0)]


def cjk_font(size, bold=True):
    from PIL import ImageFont
    return ImageFont.truetype(str(FONT_BOLD if bold else FONT_REGULAR), size)


def age_silk(base, W, H):
    """Silk weave, fold creases and foxing on the painted base (float RGB array)."""
    yy, xx = np.mgrid[0:H, 0:W]
    weave = (np.sin(xx * 2.1) * np.sin(yy * 2.1)) * 2.2
    base = base + weave[..., None]
    # creases where the scroll was folded: soft dark line with a lighter shoulder
    for x0 in (W * .25, W * .5, W * .75):
        dx = xx - x0 - (noise((H, 1), 30, int(x0))[:, 0][:, None] - .5) * 6
        base = base * (1 - .07 * np.exp(-(dx / 1.4) ** 2))[..., None] + (5 * np.exp(-((dx - 4) / 3) ** 2))[..., None]
    dy = yy - H * .5 - (noise((1, W), 40, 7)[0][None, :] - .5) * 6
    base = base * (1 - .07 * np.exp(-(dy / 1.4) ** 2))[..., None] + (5 * np.exp(-((dy - 4) / 3) ** 2))[..., None]
    # foxing: a scatter of small rust-brown spots and a few larger tide marks
    rng = np.random.default_rng(141)
    spots = np.zeros((H, W))
    for _ in range(220):
        x, y, r = rng.uniform(0, W), rng.uniform(0, H), rng.uniform(1.2, 4.5)
        x0, x1, y0, y1 = int(max(0, x - 3 * r)), int(min(W, x + 3 * r)), int(max(0, y - 3 * r)), int(min(H, y + 3 * r))
        spots[y0:y1, x0:x1] = np.maximum(spots[y0:y1, x0:x1], np.exp(-(((xx[y0:y1, x0:x1] - x) ** 2 + (yy[y0:y1, x0:x1] - y) ** 2) / (r * r))))
    tide = np.clip((noise((H, W), 60, 142) - .72) * 5, 0, 1)
    stain = np.array([120, 84, 40.])[None, None]
    base = base * (1 - spots[..., None] * .22) + stain * spots[..., None] * .10
    base = base * (1 - tide[..., None] * .09)
    # mounting margin outside the ruled frame: slightly darker, plainer silk
    fx, fy = MAP_FRAME
    outside = (xx < fx - 8) | (xx > W - fx + 8) | (yy < fy - 8) | (yy > H - fy + 8)
    base = np.where(outside[..., None], base * .80 + np.array([150, 128, 88.])[None, None] * .14, base)
    return base


def fish_scale_sea(W, H, sea_dist, land_np):
    """Rows of overlapping scale-arcs (鱼鳞纹) filling the sea; a quiet band hugs the coast."""
    layer = Image.new('L', (W, H), 0)
    d = ImageDraw.Draw(layer)
    rng = np.random.default_rng(151)
    r, dx, dy = 9.0, 16.0, 7.0
    row = 0
    y = -r
    while y < H + r:
        off = (row % 2) * dx / 2
        x = -r + off
        while x < W + r:
            jx, jy = rng.normal(0, .6), rng.normal(0, .5)
            d.arc((x - r + jx, y - r + jy, x + r + jx, y + r + jy), 200, 340, fill=150, width=1)
            if rng.random() < .22:                          # an inner crest here and there
                rr = r * .55
                d.arc((x - rr + jx, y - rr + jy + 2, x + rr + jx, y + rr + jy + 2), 215, 325, fill=110, width=1)
            x += dx
        y += dy
        row += 1
    a = np.array(layer, float)
    keep = np.clip((sea_dist - 5) / 6, 0, 1) * (~land_np)          # leave the coast line clear
    a *= keep * (.75 + .25 * noise((H, W), 25, 152))
    fx, fy = MAP_FRAME
    a[:fy, :] = a[H - fy:, :] = 0
    a[:, :fx] = a[:, W - fx:] = 0
    # a second, sparse layer of curling breakers (卷浪) further out to sea
    curls = Image.new('L', (W, H), 0)
    dc = ImageDraw.Draw(curls)
    ys, xs = np.nonzero((sea_dist > 40) & ~land_np)
    for i in rng.choice(len(xs), size=min(len(xs), 36), replace=False):
        x, y = xs[i], ys[i]
        if not (fx + 20 < x < W - fx - 20 and fy + 20 < y < H - fy - 20):
            continue
        dc.arc((x - 9, y - 9, x + 9, y + 9), 160, 430, fill=230, width=2)
        dc.arc((x - 4, y - 4, x + 4, y + 4), 0, 300, fill=230, width=2)
        dc.line((x + 9, y, x + 22, y + 4), fill=230, width=2)
    a = np.maximum(a, np.array(curls, float) * .8)
    return Image.merge('RGBA', [Image.new('L', (W, H), c) for c in (52, 82, 80)] + [Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))])


def draw_great_wall(d, pts):
    line = catmull(pts, 10)
    # wall body: ink line with a grey fill, crenels on the north side every few pixels
    d.line(line, fill=rgba(INK, 230), width=6, joint='curve')
    d.line(line, fill=(176, 166, 140, 255), width=3, joint='curve')
    acc = 0.0
    for a, b in zip(line, line[1:]):
        seg = math.dist(a, b)
        if seg < 1e-6:
            continue
        ux, uy = (b[0] - a[0]) / seg, (b[1] - a[1]) / seg
        nx, ny = uy, -ux                                       # left normal points north for a west→east wall
        if ny > 0:
            nx, ny = -nx, -ny
        t = (6 - acc % 6) % 6
        while t < seg:
            cx, cy = a[0] + ux * t + nx * 4, a[1] + uy * t + ny * 4
            d.rectangle((cx - 1.6, cy - 1.6, cx + 1.6, cy + 1.6), fill=rgba(INK, 230))
            t += 6
        acc += seg
    for x, y in (line[0], line[-1]):                          # gate towers at both ends
        d.rectangle((x - 6, y - 6, x + 6, y + 6), fill=(176, 166, 140, 255), outline=rgba(INK, 240), width=2)
        d.line((x - 8, y - 6, x, y - 11, x + 8, y - 6), fill=rgba(INK, 240), width=2)


def ruled_grid(W, H, step):
    """计里画方: a faint square grid, broken like a ruled line drawn in diluted ink."""
    fx, fy = MAP_FRAME
    layer = Image.new('L', (W, H), 0)
    d = ImageDraw.Draw(layer)
    for x in np.arange(fx, W - fx, step):
        d.line((x, fy, x, H - fy), fill=255, width=1)
    for y in np.arange(fy, H - fy, step):
        d.line((fx, y, W - fx, y), fill=255, width=1)
    a = np.array(layer, float) * (.55 + .45 * (noise((H, W), 6, 161) > .35)) * .11
    return Image.merge('RGBA', [Image.new('L', (W, H), c) for c in (110, 80, 44)] + [Image.fromarray(a.astype(np.uint8))])


def draw_frame(d, W, H):
    fx, fy = MAP_FRAME
    d.rectangle((fx - 8, fy - 8, W - fx + 8, H - fy + 8), outline=rgba(INK, 220), width=3)
    d.rectangle((fx, fy, W - fx, H - fy), outline=rgba(INK, 170), width=1)


def vertical_text(d, x, y, text, font, fill, gap=1.06):
    size = font.size
    for i, ch in enumerate(text):
        d.text((x, y + i * size * gap), ch, font=font, fill=fill, anchor='mt')
    return y + len(text) * size * gap


def title_cartouche(ink, land_np, k):
    """Vertical title in a ruled cartouche with a vermilion seal, placed in open sea (lower right first)."""
    H, W = land_np.shape
    bw, bh = 118, 360
    near = ndimage.binary_dilation(land_np, iterations=14)
    fx, fy = MAP_FRAME
    spot = None
    for x0 in range(W - fx - bw - 30, W // 2, -16):
        for y0 in range(H - fy - bh - 30, fy + 30, -16):
            if not near[y0:y0 + bh, x0:x0 + bw].any():
                spot = (x0, y0)
                break
        if spot:
            break
    if spot is None:
        return ink
    x0, y0 = spot
    card = Image.new('RGBA', (bw, bh), (0, 0, 0, 0))
    paper = paper_field((bh, bw), (226, 208, 162), 171, fibre=False, wrap=False)
    card.paste(paper, (0, 0))
    d = ImageDraw.Draw(card)
    d.rectangle((2, 2, bw - 3, bh - 3), outline=rgba(INK, 235), width=3)
    d.rectangle((9, 9, bw - 10, bh - 10), outline=rgba(INK, 160), width=1)
    title = cjk_font(40)
    vertical_text(d, bw * .60, 26, '九州山河图', title, rgba(INK, 240), gap=1.12)
    small = cjk_font(17, bold=False)
    vertical_text(d, bw * .24, 34, '宋金对峙', small, rgba(INK, 210))
    vertical_text(d, bw * .24, 34 + 17 * 5.2, '列国十二', small, rgba(INK, 210))
    ink.alpha_composite(card, (x0, y0))
    # collector's seal (白文: paper characters cut into vermilion)
    s = 46
    seal = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    ds = ImageDraw.Draw(seal)
    ds.rectangle((0, 0, s - 1, s - 1), fill=(160, 44, 30, 235))
    f = cjk_font(19)
    for i, ch in enumerate('江湖未央'):
        ds.text((s * (.73 if i < 2 else .27), s * (.28 if i % 2 == 0 else .72)), ch, font=f, fill=(236, 222, 190, 255), anchor='mm')
    m = (noise((s, s), 1.1, 172) > .18).astype(np.uint8) * 255   # worn stamp
    seal.putalpha(ImageChops.multiply(seal.getchannel('A'), Image.fromarray(m)))
    ink.alpha_composite(seal, (x0 - s - 10, y0 + bh - s - 4))   # stamped beside the cartouche, inside the frame
    return ink


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


# ---------------------------------------------------------------- map content: lakes, sea names, roads
LAKES = [('洞庭湖', 112.9, 29.25, 26, 14), ('鄱阳湖', 116.3, 29.1, 13, 20), ('太湖', 120.2, 31.2, 13, 11),
         ('青海湖', 100.2, 36.9, 22, 12), ('洱海', 100.2, 25.8, 5, 12), ('滇池', 102.7, 24.85, 7, 11),
         ('巢湖', 117.6, 31.6, 9, 6), ('呼伦湖', 117.4, 49.0, 10, 14), ('兴凯湖', 132.5, 45.0, 16, 12)]
SEA_NAMES = [('东海', 125.0, 28.8, 56), ('南海', 114.5, 18.8, 56), ('渤海', 120.0, 38.9, 30)]


def mask_from(paths, W, H, k):
    m = Image.new('L', (W, H), 0)
    d = ImageDraw.Draw(m)
    for path in paths:
        for poly in polygons(path):
            d.polygon([(x * k, y * k) for x, y in poly], fill=255)
    return np.array(m) > 127


def ranges_mask(W, H, k, width):
    m = Image.new('L', (W, H), 0)
    d = ImageDraw.Draw(m)
    for _, pts, scale in RANGES:
        d.line([(x * k, y * k) for x, y in (lonlat(*p) for p in catmull(pts, 10))], fill=255, width=int(width * scale))
    return np.array(m) > 127


def paint_lakes(di, k, rng):
    for _, lon, lat, rx, ry in LAKES:
        x, y = lonlat(lon, lat)
        x, y = x * k, y * k
        pts = []
        for a in np.linspace(0, 2 * math.pi, 28, endpoint=False):
            r = 1 + rng.normal(0, .12)
            pts.append((x + math.cos(a) * rx * r, y + math.sin(a) * ry * r))
        pts = catmull(pts + pts[:3], 4)[:-8]
        di.polygon(pts, fill=(150, 176, 160, 255), outline=rgba(INK, 220))
        for j in range(-1, 2):                             # a couple of ripple strokes
            yy = y + j * ry * .35
            di.arc((x - rx * .5, yy - 3, x - rx * .1, yy + 3), 200, 340, fill=(52, 82, 80, 160), width=1)
            di.arc((x + rx * .05, yy - 3, x + rx * .45, yy + 3), 200, 340, fill=(52, 82, 80, 160), width=1)


def paint_region_names(ink, k):
    layer = Image.new('RGBA', ink.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    # land is labelled live with each power's name; the sheet only names the seas
    for names, col in ((SEA_NAMES, (40, 74, 72, 130)),):
        for name, lon, lat, size in names:
            x, y = lonlat(lon, lat)
            f = cjk_font(size)
            step = size * 1.9                               # wide letter spacing, like a painted map caption
            x0 = x * k - step * (len(name) - 1) / 2
            for i, ch in enumerate(name):
                d.text((x0 + i * step, y * k), ch, font=f, fill=col, anchor='mm')
    return Image.alpha_composite(ink, layer.filter(ImageFilter.GaussianBlur(.4)))


def paint_roads(di, geo, k):
    """驿道 as vermilion dotted lines and 航路 as long ink-teal dashes, painted into the sheet."""
    for road in geo.get('roads', []):
        for line in polylines(road['path']):
            pts = [(x * k, y * k) for x, y in line]
            dash, gap, col, width = (14, 10, (52, 92, 100, 170), 2) if road['sea'] else (5, 5, (150, 44, 30, 215), 3)
            phase = 0.0
            for a, b in zip(pts, pts[1:]):
                seg = math.dist(a, b)
                t = 0.0
                while t < seg:
                    on = (phase % (dash + gap)) < dash
                    step = min(seg - t, (dash - phase % (dash + gap)) if on else (dash + gap - phase % (dash + gap)))
                    step = max(step, .5)
                    if on:
                        u0, u1 = t / seg, min(1, (t + step) / seg)
                        di.line((a[0] + (b[0] - a[0]) * u0, a[1] + (b[1] - a[1]) * u0,
                                 a[0] + (b[0] - a[0]) * u1, a[1] + (b[1] - a[1]) * u1), fill=col, width=width)
                    t += step
                    phase += step


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
    paper = np.array(paper_field((H, W), (214, 194, 148), 101, fibre=False, wrap=False).convert('RGB'), float)   # aged silk
    sea = np.array(paper_field((H, W), (166, 180, 154), 102, fibre=False, wrap=False).convert('RGB'), float)     # 石青 wash
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
    base = base * (1 - v[..., None] * .22)
    base = age_silk(base, W, H)
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

    # 鱼鳞水纹: the whole sea is filled with rows of scale-shaped wave arcs, as on 《华夷图》《大明混一图》
    ink = Image.alpha_composite(ink, fish_scale_sea(W, H, sea_dist, land_np))
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
    # (grass tufts, pine forests, fields and villages were dropped: the map stays clean around the towns)

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

    # rivers: drawn as two ink banks with colour between (黄河 in ochre-yellow, others in pale 石青)
    for river in geo['rivers']:
        fill = (198, 162, 70, 255) if river['name'] == '黄河' else (132, 168, 160, 255)
        for line in polylines(river['path']):
            pts = [S(p) for p in catmull(line, 6)]
            n = len(pts)
            for i, (a, b) in enumerate(zip(pts, pts[1:])):
                w = 3 + 5 * (i / max(1, n - 1))
                di.line([a, b], fill=rgba(INK, 225), width=int(round(w + 3)))
            for i, (a, b) in enumerate(zip(pts, pts[1:])):
                w = 3 + 5 * (i / max(1, n - 1))
                di.line([a, b], fill=fill, width=int(round(w)))

    # winding post roads and sea lanes between the towns (static; the game only highlights them)
    paint_roads(di, geo, k)

    # 长城: crenellated wall along the northern frontier, as drawn on the Song 《华夷图》 (1136)
    draw_great_wall(di, [S(lonlat(*p)) for p in GREAT_WALL])

    # 计里画方 grid, a double frame and a title cartouche with a collector's seal
    ink = Image.alpha_composite(ink, ruled_grid(W, H, 64))
    di = ImageDraw.Draw(ink)
    draw_frame(di, W, H)
    ink = paint_region_names(ink, k)
    ink = title_cartouche(ink, land_np, k)
    di = ImageDraw.Draw(ink)

    # nothing is painted on the mounting margin outside the ruled frame
    fx, fy = MAP_FRAME
    ia = np.array(ink)
    ia[:fy - 10, :, 3] = 0
    ia[H - fy + 10:, :, 3] = 0
    ia[:, :fx - 10, 3] = 0
    ia[:, W - fx + 10:, 3] = 0
    ink = Image.fromarray(ia, 'RGBA')

    # final resample to power-of-two height (non-uniform; drawn back into 1280x760 space)
    base_img = base_img.resize((2048, 1024), Image.LANCZOS)
    ink = ink.resize((2048, 1024), Image.LANCZOS)
    # feather the sheet edges so the scroll fades into the surrounding paper at any zoom
    fy, fx = np.mgrid[0:1024, 0:2048]
    edge = np.minimum.reduce([fx / 22, (2047 - fx) / 22, fy / 14, (1023 - fy) / 14])   # the mounting margin is the edge now
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
    frame_inside = np.zeros((760, 1280))
    frame_inside[int(MAP_FRAME[1] / 1.6):760 - int(MAP_FRAME[1] / 1.6), int(MAP_FRAME[0] / 1.6):1280 - int(MAP_FRAME[0] / 1.6)] = 1
    for cell in geo['cells']:
        m = Image.new('L', (1280, 760), 0)
        for poly in polygons(cell['path']):
            ImageDraw.Draw(m).polygon(poly, fill=255)
        m = ImageChops.multiply(ImageChops.multiply(m, territories[cell['native_country']]), land_small)
        a = np.array(m) > 127
        inside = ndimage.distance_transform_edt(a)
        alpha = np.where(a, .42 + .58 * np.exp(-np.maximum(inside - 1, 0) / 5.0), 0)
        alpha *= .88 + .24 * (gran - .5)
        alpha *= frame_inside
        alpha = ndimage.gaussian_filter(alpha, .5)
        img = Image.new('RGBA', (1280, 760), (255, 255, 255, 0))
        img.putalpha(Image.fromarray(np.clip(alpha * 255, 0, 255).astype(np.uint8), 'L'))
        path = MAP / f"cell-{cell['place']}.png"
        img.save(path, optimize=True)
        if not Path(str(path) + '.meta').exists():          # new region: same importer settings as the others
            write_texture_meta(path, wrap=False, mipmaps=True, compress=True)
    print('baked territory washes', len(geo['cells']))

    # ---- markers ----------------------------------------------------------------------
    def marker(size, draw_fn, name):
        img = Image.new('RGBA', (size * 4, size * 4), (0, 0, 0, 0))
        draw_fn(ImageDraw.Draw(img), size * 4)
        save(img.resize((size, size), Image.LANCZOS), MAP / name)

    def city(d, s):          # 府州: a ruled square on silk, as on Song maps
        d.rectangle((s * .16, s * .16, s * .84, s * .84), fill=(232, 216, 176, 255), outline=rgba(INK), width=int(s * .08))
        d.rectangle((s * .34, s * .34, s * .66, s * .66), outline=rgba(INK, 200), width=int(s * .05))
    marker(32, city, 'city.png')

    def capital(d, s):       # 都城: vermilion double square
        d.rectangle((s * .06, s * .06, s * .94, s * .94), fill=rgba(VERMILION), outline=(70, 20, 14, 255), width=int(s * .06))
        d.rectangle((s * .24, s * .24, s * .76, s * .76), outline=(240, 226, 192, 255), width=int(s * .06))
        d.rectangle((s * .40, s * .40, s * .60, s * .60), fill=(240, 226, 192, 255))
    marker(32, capital, 'capital.png')

    # vertical name slips (9-sliced, border 6): silk slip with ruled ink edge; capitals on vermilion
    def slip(name, fill, line, seed):
        w, h = 32, 64
        img = from_array(np.array(paper_field((h, w), fill, seed, fibre=False, wrap=False).convert('RGB'), float),
                         ragged_mask(h, w, 0, 1.0, seed + 1) * 240)
        d = ImageDraw.Draw(img)
        d.rectangle((1, 1, w - 2, h - 2), outline=line, width=2)
        d.rectangle((4, 4, w - 5, h - 5), outline=line[:3] + (110,), width=1)
        save(img, MAP / name)
    slip('label.png', (230, 214, 172), rgba(INK, 225), 181)
    slip('label_cap.png', (150, 48, 32), (236, 214, 160, 230), 183)

    # minor places that belong to a town region (not playable stops): small pictograms
    def site_city(d, s):     # 县: a ruled circle, smaller and quieter than a town square
        d.ellipse((s * .2, s * .2, s * .8, s * .8), fill=(232, 216, 176, 255), outline=rgba(INK, 235), width=int(s * .07))
        d.ellipse((s * .4, s * .4, s * .6, s * .6), fill=rgba(INK, 200))
    marker(24, site_city, 'site_city.png')

    def site_town(d, s):     # 镇集: two roofs
        for cx, cy, w in ((.36, .62, .2), (.64, .56, .22)):
            d.rectangle((s * (cx - w * .8), s * (cy - w * .2), s * (cx + w * .8), s * (cy + w * .9)), fill=(232, 216, 176, 255), outline=rgba(INK, 235), width=int(s * .035))
            d.polygon([(s * (cx - w * 1.15), s * (cy - w * .1)), (s * (cx - w * .55), s * (cy - w * .95)), (s * (cx + w * .55), s * (cy - w * .95)), (s * (cx + w * 1.15), s * (cy - w * .1))],
                      fill=(78, 82, 82, 255), outline=rgba(INK, 240))
    marker(24, site_town, 'site_town.png')

    def site_mountain(d, s):  # 山门: a blue-green peak with a small vermilion gate at its foot
        d.polygon([(s * .08, s * .86), (s * .34, s * .30), (s * .48, s * .46), (s * .62, s * .14), (s * .92, s * .86)], fill=(86, 128, 118, 255), outline=rgba(INK, 240))
        d.line((s * .62, s * .14, s * .62, s * .5), fill=rgba(INK, 140), width=int(s * .03))
        d.rectangle((s * .40, s * .64, s * .60, s * .86), fill=rgba(VERMILION), outline=rgba(INK, 240))
        d.line((s * .34, s * .64, s * .66, s * .64), fill=rgba(INK, 250), width=int(s * .06))
    marker(24, site_mountain, 'site_mountain.png')

    def site_port(d, s):     # 津渡: a junk under sail on two wave strokes
        d.polygon([(s * .18, s * .62), (s * .82, s * .62), (s * .70, s * .76), (s * .30, s * .76)], fill=(120, 84, 52, 255), outline=rgba(INK, 240))
        d.polygon([(s * .50, s * .14), (s * .50, s * .60), (s * .22, s * .58)], fill=(232, 216, 176, 255), outline=rgba(INK, 240))
        d.line((s * .5, s * .12, s * .5, s * .62), fill=rgba(INK, 250), width=int(s * .04))
        d.arc((s * .1, s * .74, s * .5, s * .94), 200, 340, fill=(52, 82, 80, 230), width=int(s * .04))
        d.arc((s * .5, s * .74, s * .9, s * .94), 200, 340, fill=(52, 82, 80, 230), width=int(s * .04))
    marker(24, site_port, 'site_port.png')

    def ring(d, s):
        d.ellipse((s * .06, s * .06, s * .94, s * .94), outline=rgba(VERMILION, 235), width=int(s * .07))
        d.ellipse((s * .2, s * .2, s * .8, s * .8), outline=rgba(VERMILION, 140), width=int(s * .03))
    marker(64, ring, 'ring.png')

    def pennant(d, s):       # hero banner
        d.line((s * .3, s * .08, s * .3, s * .95), fill=rgba(INK), width=int(s * .06))
        d.polygon([(s * .33, s * .1), (s * .9, s * .22), (s * .33, s * .42)], fill=rgba(VERMILION), outline=(80, 24, 16, 255))
    marker(48, pennant, 'pennant.png')

    def compass(d, s):       # 方位: a silk disc with the four quarters written in, 北 in vermilion
        c = s / 2
        d.ellipse((c - s * .46, c - s * .46, c + s * .46, c + s * .46), fill=(226, 208, 162, 235), outline=rgba(INK, 230), width=int(s * .02))
        d.ellipse((c - s * .39, c - s * .39, c + s * .39, c + s * .39), outline=rgba(INK, 150), width=int(s * .008))
        d.line((c, c - s * .22, c, c + s * .22), fill=rgba(INK, 160), width=int(s * .01))
        d.line((c - s * .22, c, c + s * .22, c), fill=rgba(INK, 160), width=int(s * .01))
        d.ellipse((c - s * .05, c - s * .05, c + s * .05, c + s * .05), fill=rgba(VERMILION, 230))
        f = cjk_font(int(s * .17))
        for ch, (x, y), col in (('北', (c, c - s * .31), rgba(VERMILION, 250)), ('南', (c, c + s * .31), rgba(INK, 235)),
                                ('东', (c + s * .31, c), rgba(INK, 235)), ('西', (c - s * .31, c), rgba(INK, 235))):
            d.text((x, y), ch, font=f, fill=col, anchor='mm')
    marker(128, compass, 'compass.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ui-only', action='store_true')
    args = parser.parse_args()
    bake_ui()
    bake_handmade()
    if not args.ui_only:
        bake_atlas()


if __name__ == '__main__':
    main()
