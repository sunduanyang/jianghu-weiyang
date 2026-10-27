"""Import the user's terrain ZIP, preserving originals and calibrating runtime tiles.

Only the 32 selected tiles are imported, never references, drafts or legacy art.
Runtime export scales existing pixels; it does not paint substitute terrain.
"""
from pathlib import Path
import hashlib
import json
import zipfile

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
PACK = 'terrain-biomes-v3-20261026'
SOURCE = ROOT / 'art' / 'terrain-biomes-v3'
UNITY = ROOT / 'unity/JianghuUnity/Assets/Resources/CityBiomes'


def main():
    archive = ROOT / 'output/imagegen' / (PACK+'.zip')
    for folder in (SOURCE, UNITY):
        folder.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as z:
        manifest = json.loads(z.read(PACK+'/manifest.json'))
        (SOURCE/'source-manifest.json').write_bytes(z.read(PACK+'/manifest.json'))
        for asset in manifest['assets']:
            data = z.read(PACK+'/'+asset['file'])
            assert hashlib.sha256(data).hexdigest() == asset['sha256']
            (SOURCE/Path(asset['file']).name).write_bytes(data)
    exports = []
    for asset in manifest['assets']:
        source = SOURCE/Path(asset['file']).name
        im = Image.open(source).convert('RGBA')
        alpha = np.asarray(im.getchannel('A'))
        solid = alpha >= 160
        widths = []
        # The widest scan line crosses the two side corners of the ground plane.
        for y in range(int(im.height*.53), int(im.height*.75)):
            xs = np.flatnonzero(solid[y])
            widths.append((int(xs[-1]-xs[0]) if len(xs) else 0, y))
        width, pivot_y = max(widths)
        ys, xs = np.nonzero(solid)
        bottom = int(ys.max())
        pivot_x = (int(xs.min())+int(xs.max()))/2
        scale_x, scale_y = 508/width, 128/(bottom-pivot_y)
        # A common 512 canvas, centred at (256,342); visible base is exactly 2:1.
        transform = (1/scale_x, 0, pivot_x-256/scale_x,
                     0, 1/scale_y, pivot_y-342/scale_y)
        runtime = im.transform((512,512), Image.Transform.AFFINE, transform,
                               Image.Resampling.BICUBIC)
        runtime.save(UNITY/(asset['id']+'.png'), optimize=True)
        exports.append(dict(id=asset['id'], label=asset['label'],
                            size=asset['footprintCells'][0],
                            source_sha256=asset['sha256'],
                            runtime_sha256=hashlib.sha256((UNITY/(asset['id']+'.png')).read_bytes()).hexdigest(),
                            source_pivot=[pivot_x,pivot_y], source_bottom=bottom,
                            canvas=[512,512], pivot=[256,342], diamond=[508,256]))
    result = dict(pack=PACK, source_count=32, archive_sha256=hashlib.sha256(archive.read_bytes()).hexdigest(),
                  placement_version=3, legacy_assets=False, assets=exports)
    (UNITY/'art-pack.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(pack=PACK, tiles=len(exports), unity=str(UNITY)),ensure_ascii=False))


if __name__ == '__main__':
    main()
