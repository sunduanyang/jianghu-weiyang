"""Build recolouring masks; original split artwork and anchors stay intact."""
import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / 'unity/JianghuUnity/Assets/Resources'


def bake():
    catalog = json.loads((RESOURCES / 'PaperCast/index.json').read_text(encoding='utf-8'))
    output = RESOURCES / 'Appearance/Masks'
    output.mkdir(parents=True, exist_ok=True)
    for name, skin in catalog['skins'].items():
        atlas = np.asarray(Image.open(RESOURCES / f'PaperCast/{name}.png').convert('RGBA'))
        mask = np.zeros_like(atlas)
        for key, part in skin['parts'].items():
            u, v, du, dv = part['uv']
            x, y = round(u * atlas.shape[1]), round((1-v-dv) * atlas.shape[0])
            width, height = part['size']
            pixels = atlas[y:y+height, x:x+width].astype(float) / 255
            rgb, alpha = pixels[:, :, :3], pixels[:, :, 3]
            red, green, blue = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
            light = rgb.mean(axis=2)
            visible = alpha > .05
            warm_skin = (red > green + .025) & (green > blue + .04) & (light > .48)
            region = mask[y:y+height, x:x+width]
            if key == 'head-neck':
                region[:, :, 0] = (warm_skin & visible) * 255
                # Broad dark regions are hair; thin facial outlines remain ink.
                dark = (light < .26) & (light > .035) & visible
                core = ndimage.binary_opening(dark, iterations=2)
                hair = ndimage.binary_dilation(core, iterations=1) & dark
                region[:, :, 2] = hair * 255
            elif 'hand' in key:
                region[:, :, 0] = (warm_skin & visible) * 255
            elif 'prop' not in key and 'equipment' not in key:
                cloth = visible & (light > .15)
                exposed = np.zeros_like(cloth)
                if 'forearm' in key:
                    exposed[int(height*.7):] = warm_skin[int(height*.7):]
                cloth &= ~exposed
                channel = 3 if any(word in key for word in ('thigh', 'shin', 'pelvis', 'skirt', 'foot')) else 1
                region[:, :, channel] = cloth * 235
                region[:, :, 0] = (exposed & visible) * 255
        Image.fromarray(mask).save(output / f'{name}.png')
    print(f'Built {len(catalog["skins"])} appearance masks')


if __name__ == '__main__':
    bake()
