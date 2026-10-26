"""Calibrate the 21 original Song city PNGs for the native city renderer.

Sources are preserved verbatim in art/city-map/song-20261026/source. Runtime
textures use a 512px canvas with a shared logical ground anchor (128,171).
Ground diamonds and connected roads are derived from the new ground paintings;
neither this exporter nor its manifest changes settlement or pathfinding data.
"""
import hashlib
import io
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageOps
from scipy import ndimage

from bake_ui import ROOT, write_texture_meta

PACK = ROOT / 'art/city-map/song-20261026'
SOURCE = PACK / 'source'
OUT = ROOT / 'unity/JianghuUnity/Assets/Resources/City'
SCALE = 2
CANVAS = (512, 512)
BUILDINGS = ('gate', 'government', 'palace', 'market', 'inn', 'clinic', 'workshop',
             'training', 'school', 'residence', 'granary', 'garden', 'temple', 'dock', 'faction')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(name):
    image = Image.open(SOURCE / (name + '.png')).convert('RGBA')
    if image.getchannel('A').getextrema()[0] != 0:
        raise ValueError('Expected transparent source: ' + name)
    return image


def silhouette(name, width, height, px=.5, py=.82, mirror=False):
    im = load(name)
    # Retain all generated edge pixels, including soft built-in shadows.
    bbox = im.getchannel('A').getbbox()
    sprite = im.crop(bbox)
    factor = min(width / sprite.width, height / sprite.height) * SCALE
    sprite = sprite.resize((round(sprite.width * factor), round(sprite.height * factor)), Image.Resampling.LANCZOS)
    if mirror:
        sprite = ImageOps.mirror(sprite)
        px = 1 - px
    x, y = round(256 - sprite.width * px), round(342 - sprite.height * py)
    if x < 0 or y < 0 or x + sprite.width > 512 or y + sprite.height > 512:
        raise ValueError('Clipped calibrated sprite: ' + name)
    target = Image.new('RGBA', CANVAS)
    target.alpha_composite(sprite, (x, y))
    return target, {'source_alpha_bounds': list(bbox), 'crop_pivot': [px, py],
                    'runtime_bounds': [x, y, x + sprite.width, y + sprite.height]}


def centered_building(kind):
    """Fit all visible source pixels inside a centred square in map projection."""
    image = load('building-' + kind)
    # ImageGen includes almost invisible alpha specks far outside some subjects.
    # Ignore only that <=3% opacity fringe when measuring the visible painting.
    alpha = np.asarray(image.getchannel('A')).copy()
    alpha[alpha <= 8] = 0
    image.putalpha(Image.fromarray(alpha))
    bbox = image.getchannel('A').getbbox()
    sprite = image.crop(bbox)
    ys, xs = np.nonzero(np.asarray(sprite.getchannel('A')) > 0)
    # Screen-space bounds of the complete painting, not an estimated ground pivot.
    # The same sprite is drawn at 1x or 2x for a one-cell or four-cell square.
    radius = np.max(np.abs(xs - (sprite.width - 1) / 2) / 128
                    + np.abs(ys - (sprite.height - 1) / 2) / 64)
    ratio = .83 / radius * SCALE
    sprite = sprite.resize((max(1, round(sprite.width * ratio)), max(1, round(sprite.height * ratio))), Image.Resampling.LANCZOS)
    alpha = np.asarray(sprite.getchannel('A')).copy()
    alpha[alpha <= 2] = 0
    sprite.putalpha(Image.fromarray(alpha))
    sprite = sprite.crop(sprite.getchannel('A').getbbox())
    x, y = round(256 - (sprite.width - 1) / 2), round(342 - (sprite.height - 1) / 2)
    target = Image.new('RGBA', CANVAS)
    target.alpha_composite(sprite, (x, y))
    bounds = target.getchannel('A').getbbox()
    centre_error = max(abs((bounds[0] + bounds[2] - 1) / 2 - 256),
                       abs((bounds[1] + bounds[3] - 1) / 2 - 342))
    if centre_error > .5:
        raise ValueError('Building is not centred: ' + kind)
    return target, {'source_alpha_bounds': list(bbox), 'alignment': 'visible image centre',
                    'runtime_centre': [256, 342], 'projected_lot_inset': .83,
                    'source_fringe_alpha_threshold': 8,
                    'centre_error_pixels': centre_error,
                    'runtime_bounds': [x, y, x + sprite.width, y + sprite.height]}


def diamond_mask():
    mask = Image.new('L', CANVAS)
    ImageDraw.Draw(mask).polygon([(256, 214), (512, 342), (256, 470), (0, 342)], fill=255)
    return mask


def ground(name):
    im = load(name)
    alpha = np.asarray(im.getchannel('A'))
    ys, xs = np.nonzero(alpha > 128)
    crop = im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))
    crop = crop.resize((512, 256), Image.Resampling.LANCZOS)
    pixels = np.array(crop)
    # Fill transparent shadow/edge gaps from the nearest actual painted texel,
    # then apply one exact 2:1 diamond. Adjacent ground cells touch without gaps.
    # Remove the source tile's dark outline as well as its transparent shadow.
    # Outlines belong to the optional grid layer, rather than every ground cell.
    interior = ndimage.distance_transform_edt(pixels[:, :, 3] > 128) > 5
    nearest = ndimage.distance_transform_edt(~interior, return_distances=False, return_indices=True)
    pixels[:, :, :3] = pixels[tuple(nearest)][:, :, :3]
    pixels[:, :, 3] = 255
    target = Image.new('RGBA', CANVAS)
    target.paste(Image.fromarray(pixels), (0, 214))
    target.putalpha(diamond_mask())
    return target


def export():
    if not SOURCE.is_dir():
        return False
    original = json.loads((PACK / 'manifest.json').read_text(encoding='utf-8'))
    for record in original['assets']:
        path = SOURCE / (record['id'] + '.png')
        if sha(path) != record['sha256']:
            raise ValueError('Source hash changed: ' + str(path))
    outputs, records = {}, []

    def emit(name, im):
        path = OUT / (name + '.png')
        buffer = io.BytesIO()
        im.save(buffer, format='PNG', optimize=True)
        data = buffer.getvalue()
        # Atomic replacement avoids truncating a texture while Windows indexes it.
        if not path.exists() or path.read_bytes() != data:
            temporary = path.with_suffix('.png.tmp')
            temporary.write_bytes(data)
            temporary.replace(path)
        write_texture_meta(path, wrap=False, mipmaps=False, compress=False)
        outputs[name] = hashlib.sha256(data).hexdigest()

    for kind in BUILDINGS:
        im, facts = centered_building(kind)
        emit('building-' + kind, im)
        records.append({'source': 'building-' + kind, **facts})
    for v in range(3):
        im, _ = silhouette('prop-tree', 194 - v * 7, 194, .5, .84, mirror=v == 1)
        emit('prop-forest-' + str(v), im)
        im, _ = silhouette('prop-tree', 100 + v * 5, 108, .5, .84, mirror=v == 1)
        emit('prop-grass-' + str(v), im)
    for axis in range(2):
        im, _ = silhouette('prop-wall', 174, 142, .5, .84, mirror=bool(axis))
        emit('wall-axis-' + str(axis), im)
        im, _ = silhouette('prop-bridge', 236, 150, .5, .77, mirror=bool(axis))
        emit('bridge-axis-' + str(axis), im)

    grass, stone, water = [ground('tile-' + k) for k in ('grass', 'stone', 'water')]
    parcel = ImageEnhance.Color(stone).enhance(.55)
    edge = ImageDraw.Draw(parcel)
    edge.line([(256, 217), (506, 342), (256, 467), (6, 342), (256, 217)], fill=(177, 157, 112, 255), width=3)
    emit('building-parcel', parcel)
    for v in range(3):
        for kind in ('grass', 'street', 'forest', 'mountain', 'wall'):
            im = ImageEnhance.Brightness(grass).enhance((.985, 1, 1.015)[v])
            if kind == 'forest':
                im = ImageEnhance.Brightness(im).enhance(.94)
            emit(kind + '-' + str(v), im)
        emit('water-' + str(v), ImageEnhance.Brightness(water).enhance((.99, 1, 1.01)[v]))
        field = grass.copy()
        d = ImageDraw.Draw(field)
        def iso(u, w):
            return round(256 + (u - w) * 1.76), round(342 + (u + w) * .88)
        d.polygon([iso(-52, -52), iso(52, -52), iso(52, 52), iso(-52, 52)], fill=(138, 149, 94, 255))
        for w in range(-46, 50, 12):
            d.line([iso(-48, w), iso(48, w)], fill=(201, 181, 124, 255), width=5)
            d.line([iso(-48, w + 4), iso(48, w + 4)], fill=(108, 132, 88, 255), width=3)
        emit('field-' + str(v), field)

    ends = ((384, 278), (384, 406), (128, 406), (128, 278))
    for mask in range(16):
        route = Image.new('L', CANVAS)
        border = Image.new('RGBA', CANVAS)
        md, bd = ImageDraw.Draw(route), ImageDraw.Draw(border)
        md.ellipse((224, 326, 288, 358), fill=255)
        for bit, end in enumerate(ends):
            if mask & (1 << bit):
                md.line([(256, 342), end], fill=255, width=60)
                bd.line([(256, 342), end], fill=(176, 151, 109, 255), width=68)
        surface = stone.copy()
        surface.putalpha(route)
        border.alpha_composite(surface)
        emit('road-' + str(mask), border)
        # A connected flat crossing remains beneath the arch, even at bends.
        emit('bridge-' + str(mask), border)

    manifest = {'pack': 'song-20261026', 'source_count': len(original['assets']),
                'placement_version': 2, 'building_alignment': 'visible_image_centre',
                'building_footprints': ['1x1', '2x2'],
                'source_hashes': {r['id']: r['sha256'] for r in original['assets']},
                'logical_canvas': [256, 256], 'runtime_canvas': list(CANVAS),
                'logical_anchor': [128, 171], 'ground_projection': '2:1',
                'buildings': records, 'outputs': outputs}
    (PACK / 'runtime-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    # Included in Resources so native acceptance can establish pack identity.
    (OUT / 'art-pack.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Song city art:', len(original['assets']), 'sources,', len(outputs), 'runtime textures')
    return True


if __name__ == '__main__':
    export()
