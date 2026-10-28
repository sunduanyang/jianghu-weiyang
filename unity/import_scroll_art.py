"""Bring GPT-painted 长卷 buildings into the game.

Each source is a 3:2 picture of one building on a flat #FF00FF background, named after the
facility kind (building_inn.png, building_gate.png ...). The script keys out the magenta,
removes the pink fringe, scales the whole picture to the scroll's 442-pixel width (so the
relative sizes asked for in the prompts survive) and moves it so the foot of the building
stands on the scroll's street line, then writes it to Assets/Resources/Scroll/Art/ with a
Unity .meta. The game prefers these files over the baked ones; delete a file to fall back.

    python unity/import_scroll_art.py path/to/building_inn.png [more files ...]
    python unity/import_scroll_art.py --preview sheet.png path/to/*.png
"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

from bake_ui import ASSETS, save, write_folder_meta

OUT = ASSETS / 'Resources' / 'Scroll' / 'Art'
W, H, GROUND = 442, 338, 309            # sprite size and street line used by CityScroll.cs
KINDS = {'gate', 'palace', 'government', 'market', 'inn', 'dock', 'clinic', 'workshop', 'training',
         'school', 'temple', 'sect', 'residence', 'granary', 'garden', 'generic'}
# The landing stands in water: its picture already shows the bank, so keep its own composition.
KEEP_COMPOSITION = {'dock'}


def key_magenta(img):
    rgb = np.array(img.convert('RGB'), float)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    # distance from pure magenta; strong red+blue with little green is background
    magenta = np.clip((np.minimum(r, b) - g) / 255, 0, 1)
    alpha = 1 - np.clip((magenta - .25) / .35, 0, 1)
    alpha = ndimage.grey_erosion(alpha, size=(3, 3))                   # shrink one pixel: no pink rim
    # despill: pull leftover magenta out of the edge pixels
    spill = np.clip(np.minimum(r, b) - g, 0, None) * .6
    rgb[..., 0] -= spill * (r > g) * .5
    rgb[..., 2] -= spill * (b > g) * .5
    rgba = np.dstack([np.clip(rgb, 0, 255), alpha * 255]).astype(np.uint8)
    return Image.fromarray(rgba, 'RGBA')


def fit(img, kind):
    scale = W / img.width
    img = img.resize((W, max(1, round(img.height * scale))), Image.LANCZOS)
    a = np.array(img)[..., 3] > 40
    rows = np.nonzero(a.mean(axis=1) > .02)[0]
    if len(rows) == 0:
        raise SystemExit(f'{kind}: nothing left after removing the #FF00FF background')
    top, foot = rows[0], rows[-1]
    shift = (H - img.height) if kind in KEEP_COMPOSITION else GROUND - foot
    shift = max(shift, -top + 2)                                       # never push the roof off the top
    out = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    out.alpha_composite(img.crop((0, max(0, -shift), W, img.height)), (0, max(0, shift)))
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('files', nargs='+', type=Path)
    parser.add_argument('--preview', type=Path, help='also write a contact sheet of the imported buildings on silk')
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    write_folder_meta(OUT)
    done = []
    for path in args.files:
        kind = path.stem.removeprefix('building_')
        if kind not in KINDS:
            raise SystemExit(f'{path.name}: name it building_<kind>.png, kind one of {", ".join(sorted(KINDS))}')
        sprite = fit(key_magenta(Image.open(path)), kind)
        save(sprite, OUT / f'building_{kind}.png')
        done.append(sprite)
    if args.preview:
        from bake_ui import paper_field
        cols = 4
        sheet = paper_field((H * ((len(done) + cols - 1) // cols) + 20, W * cols + 20), (214, 194, 148), 709, fibre=False, wrap=False)
        for i, sprite in enumerate(done):
            sheet.alpha_composite(sprite, (10 + (i % cols) * W, 10 + (i // cols) * H))
            # the street line, so a misplaced foot is easy to see
            y = 10 + (i // cols) * H + GROUND
            sheet.paste((150, 60, 40, 255), (10 + (i % cols) * W, y, 10 + (i % cols + 1) * W, y + 1))
        sheet.convert('RGB').save(args.preview)


if __name__ == '__main__':
    main()
