"""Import only the three supplied complete settlement tiles for Unity."""
from pathlib import Path
import hashlib
import json
import zipfile

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
PACK = 'settlement-tiles-1-4-9-20261026'
SOURCE = ROOT/'art/settlement-tiles-1-4-9'
OUT = ROOT/'unity/JianghuUnity/Assets/Resources/CitySettlements'


def main():
    archive = ROOT/'output/imagegen'/(PACK+'.zip')
    SOURCE.mkdir(parents=True,exist_ok=True); OUT.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(archive) as z:
        manifest = json.loads(z.read(PACK+'/manifest.json'))
        (SOURCE/'source-manifest.json').write_bytes(z.read(PACK+'/manifest.json'))
        for a in manifest['assets']:
            data = z.read(PACK+'/'+a['file'])
            assert hashlib.sha256(data).hexdigest()==a['sha256']
            (SOURCE/Path(a['file']).name).write_bytes(data)
    exports = []
    for a in manifest['assets']:
        with Image.open(SOURCE/Path(a['file']).name) as source:
            im = source.convert('RGBA')
        solid = np.asarray(im.getchannel('A'))>=160
        widths = []
        for y in range(round(im.height*.42),round(im.height*.80)):
            xs = np.flatnonzero(solid[y])
            widths.append((int(xs[-1]-xs[0]) if len(xs) else 0,y))
        width,cy = max(widths)
        ys,xs = np.nonzero(solid)
        cx = (int(xs.min())+int(xs.max()))/2; bottom = int(ys.max())
        sx,sy = 1016/width,256/(bottom-cy)
        runtime = im.transform((1024,1024),Image.Transform.AFFINE,
            (1/sx,0,cx-512/sx,0,1/sy,cy-684/sy),Image.Resampling.BICUBIC)
        destination = OUT/(a['id']+'.png')
        runtime.save(destination,optimize=True)
        exports.append(dict(id=a['id'],label=a['label'],size=a['footprintCells'][0],
                            source_sha256=a['sha256'],runtime_sha256=hashlib.sha256(destination.read_bytes()).hexdigest(),
                            source_pivot=[cx,cy],source_bottom=bottom,canvas=[1024,1024],pivot=[512,684],diamond=[1016,512]))
    result = dict(pack=PACK,source_count=3,legacy_assets=False,assets=exports,
                  archive_sha256=hashlib.sha256(archive.read_bytes()).hexdigest())
    (OUT/'art-pack.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(pack=PACK,tiles=len(exports),unity=str(OUT)),ensure_ascii=False))


if __name__=='__main__': main()
