"""Export the original painted 4x4 city atlas into anchored Unity map sprites.

This only crops, resizes and pads existing pixels. To regenerate runtime assets:
    python unity/export_city_art.py
The previous procedural art remains the fallback when source art is unavailable.
"""
import hashlib
import json
from pathlib import Path
import numpy as np
from scipy import ndimage
from PIL import Image
from bake_ui import ROOT, save

SOURCE=ROOT/'art/city-map/painted-atlas.png'
OUT=ROOT/'unity/JianghuUnity/Assets/Resources/City'
KINDS=('gate','government','palace','market','inn','clinic','workshop','training',
       'school','residence','granary','garden','temple','dock','faction','forest')


def export():
    from export_song_city_art import export as export_song
    if export_song():
        return
    if not SOURCE.exists():
        print('Painted atlas unavailable; keeping procedural buildings.')
        return
    atlas=Image.open(SOURCE).convert('RGBA')
    if atlas.getchannel('A').getextrema()[0]==255:
        raise ValueError('Atlas needs actual transparency, not an opaque background')
    # Generated sheets need not obey exact cell edges. Connected silhouettes preserve
    # pagoda tips and foliage extending into gutters, rather than slicing a fixed grid.
    alpha=np.asarray(atlas)[:,:,3]
    labels,_=ndimage.label(alpha>12)
    sizes=np.bincount(labels.ravel())
    bodies=[int(i) for i in np.flatnonzero(sizes>atlas.width*atlas.height*.012) if i]
    if len(bodies)!=16:raise ValueError('Expected 16 isolated sprite bodies, got '+str(len(bodies)))
    centres={i:ndimage.center_of_mass(alpha,labels,i) for i in bodies}
    by_y=sorted(bodies,key=lambda i:centres[i][0])
    ordered=[]
    for row in range(4):ordered.extend(sorted(by_y[row*4:row*4+4],key=lambda i:centres[i][1]))
    groups=np.zeros(alpha.shape,dtype=np.uint8)
    for i,body in enumerate(ordered):groups[labels==body]=i+1
    nearest=ndimage.distance_transform_edt(groups==0,return_distances=False,return_indices=True)
    assigned=groups[tuple(nearest)]
    records=[]
    for i,kind in enumerate(KINDS):
        cell=atlas.copy()
        cell.putalpha(Image.fromarray(np.where(assigned==i+1,alpha,0).astype('uint8')))
        bbox=cell.getchannel('A').getbbox()
        if bbox is None:raise ValueError('Empty atlas cell: '+kind)
        sprite=cell.crop(bbox)
        # All footprints sit over the tile centre (128,171); courtyard tips extend below it.
        ratio=min(236/sprite.width,210/sprite.height)
        sprite=sprite.resize((round(sprite.width*ratio),round(sprite.height*ratio)),Image.Resampling.LANCZOS)
        target=Image.new('RGBA',(256,256))
        target.alpha_composite(sprite,((256-sprite.width)//2,211-sprite.height))
        names=['prop-forest-'+str(v) for v in range(3)] if kind=='forest' else ['building-'+kind]
        for name in names:save(target,OUT/(name+'.png'))
        records.append({'kind':kind,'alpha_bounds':list(bbox),'outputs':names,'anchor':[128,171]})
    manifest={'source':'painted-atlas.png','sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'layout':[4,4],'extraction':'16 connected silhouettes with nearest-body alpha fringe assignment','source_size':list(atlas.size),'sprites':records}
    (SOURCE.parent/'export-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':export()
