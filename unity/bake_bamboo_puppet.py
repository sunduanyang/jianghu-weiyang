"""Mechanically isolate the user's bamboo swordsman and bind hidden hinges.

Body parts keep source aspect ratios and a common source-pixel scale. Sleeve
hems are cloth silhouettes, not elbow/wrist endpoints. No artwork is redrawn.
"""
import json
import math
from pathlib import Path
import numpy as np
from PIL import Image
from scipy import ndimage
from bake_paper_cast import ROOT, DEST, digest, pack, texture_meta

SOURCE = ROOT/'art/rig-bamboo'
ROWS = [
    ('head-neck', 1, (118, 236), None, True),
    ('torso', 2, (389, 265), (397, 61), True),
    ('front-skirt', 4, (642, 59), None, True),
    ('back-skirt', 3, (870, 60), None, True),
    ('near-upper-arm', 11, (155, 492), (137, 663), True),
    ('far-upper-arm', 12, (358, 491), (377, 663), True),
    ('near-forearm', 18, (553, 541), (623, 686), True),
    ('far-forearm', 17, (903, 539), (880, 690), True),
    ('near-hand', 36, (128, 861), None, True),
    ('far-hand', 34, (358, 861), None, True),
    ('near-thigh', 35, (589, 851), (616, 1120), True),
    ('far-thigh', 37, (924, 858), (885, 1120), True),
    ('near-shin', 39, (124, 1056), (128, 1335), True),
    ('far-shin', 40, (367, 1055), (369, 1335), True),
    ('near-foot', 41, (631, 1183), None, True),
    ('far-foot', 42, (881, 1183), None, False),
    ('sheathed-sword', 43, (304, 1411), None, False),
]


def bake_bamboo(result):
    sheet = Image.open(SOURCE/'source.png').convert('RGBA')
    rgba = np.array(sheet)
    labels, _ = ndimage.label(rgba[:, :, 3] > 48)
    regions = ndimage.find_objects(labels)
    images, rows = {}, []
    for name, component, pivot, tip, mirror in ROWS:
        ys, xs = regions[component-1]
        rect = [max(0,xs.start-4), max(0,ys.start-4), min(sheet.width,xs.stop+4), min(sheet.height,ys.stop+4)]
        x0,y0,x1,y1 = rect
        mask = ndimage.binary_dilation(labels[y0:y1,x0:x1] == component, iterations=2)
        crop = rgba[y0:y1,x0:x1].copy()
        crop[~mask] = 0
        image = Image.fromarray(crop)
        assert np.array_equal(crop[mask],rgba[y0:y1,x0:x1][mask])
        local = lambda p: [p[0]-x0,p[1]-y0]
        images[name] = image
        rows.append(dict(id=name, source_rect=rect, pivot=local(pivot), tip=local(tip) if tip else None, mirror=mirror, retained_pixels=int((crop[:,:,3]>0).sum())))
    atlas, positions = pack(images)
    torso = next(p for p in rows if p['id']=='torso')
    pivot,tip = np.array(torso['pivot']), np.array(torso['tip'])
    v = tip-pivot; v[0] *= -1
    angle = -math.pi/2-math.atan2(v[1],v[0])
    def body_point(p):
        d = np.array(p,float)-np.array([389,265]);d[0]*=-1
        return [float(d[0]*math.cos(angle)-d[1]*math.sin(angle)),float(d[0]*math.sin(angle)+d[1]*math.cos(angle))]
    lengths = {'spine':float(np.linalg.norm(v))}
    skin = dict(id='reference-bamboo', description='竹纹长衫剑客 · 用户新素材试装', atlas='reference-bamboo', size=list(atlas.size), scale=1, reference=True,
                source_sheet='bamboo-source', source_facing='left', body_source_scale=1, parts={},
                shoulders={'Near':body_point((474,126)), 'Far':body_point((380,136))},
                hips={'Near':body_point((407,284)), 'Far':body_point((364,285))}, lengths=lengths,
                canvas=[1200,1380], canvas_origin=[600,470], knee_poles={'Near':1,'Far':1})
    part_dir = SOURCE/'parts';part_dir.mkdir(exist_ok=True)
    for i,row in enumerate(rows,1):
        name=row['id'];image=images[name];x,y=positions[name]
        file=f'{i:02d}-{name}.png';image.save(part_dir/file)
        data=dict(size=list(image.size),uv=[x/atlas.width,1-(y+image.height)/atlas.height,image.width/atlas.width,image.height/atlas.height],pivot=row['pivot'],mirror=row['mirror'],source='rig-bamboo/parts/'+file,sha256=digest(part_dir/file))
        side='Near' if name.startswith('near') else 'Far'
        if name=='torso':data.update(bone='torso',tip=row['tip'],layer=40)
        elif row['tip'] is not None:
            kind=next(k for k in ('upper-arm','forearm','thigh','shin') if name.endswith(k))
            bone=('upper' if kind=='upper-arm' else kind)+side
            lengths[('upper_arm' if kind=='upper-arm' else kind)+side]=math.dist(row['pivot'],row['tip'])
            data.update(bone=bone,tip=row['tip'],layer={'upperFar':10,'forearmFar':12,'thighFar':16,'shinFar':17,'thighNear':25,'shinNear':26,'upperNear':72,'forearmNear':75}[bone])
        elif name=='head-neck':data.update(joint='neck',height=row['pivot'][1],angle=0,follow='head',layer=60)
        elif name.endswith(('hand','foot')):
            hand=name.endswith('hand')
            data.update(joint=('wrist' if hand else 'ankle')+side,height=image.height,angle=0,follow=('hand' if hand else 'foot')+side,layer=(80 if side=='Near' else 14) if hand else (29 if side=='Near' else 20))
        elif name in ('front-skirt','back-skirt'):
            data.update(joint='hipNear' if name.startswith('front') else 'hipFar',height=image.height,angle=0 if name.startswith('front') else 8,follow='skirtNear' if name.startswith('front') else 'skirtFar',layer=50 if name.startswith('front') else 5)
        else:data.update(joint='hipNear',width=600,angle=100,layer=19,trial_weapon=True)
        skin['parts'][name]=data
        assert atlas.crop((x,y,x+image.width,y+image.height)).tobytes()==image.tobytes()
    for key in ('upper_arm','forearm','thigh','shin'):lengths[key]=(lengths[key+'Near']+lengths[key+'Far'])/2
    skin['ground']=max(skin['hips'][side][1]+lengths['thigh'+side]+lengths['shin'+side]+images[('near' if side=='Near' else 'far')+'-foot'].height-skin['parts'][('near' if side=='Near' else 'far')+'-foot']['pivot'][1] for side in ('Near','Far'))-18
    path=DEST/'reference-bamboo.png';atlas.save(path,optimize=True);texture_meta(path);skin['atlas_sha256']=digest(path)
    import shutil
    shutil.copy2(SOURCE/'source.png',DEST/'bamboo-source.png');texture_meta(DEST/'bamboo-source.png')
    old=result['skins'].get(skin['id'])
    result['expected_parts']+=len(rows)-(len(old['parts']) if old else 0)
    result['expected_skins']+=0 if old else 1
    result['skins'][skin['id']]=skin
    (SOURCE/'binding.json').write_text(json.dumps(dict(source_sha256=digest(SOURCE/'source.png'),method='Connected-component isolation with two-pixel antialias fringe. Body transforms use uniform source scale; hidden sleeve hinges are manually marked. Sword display scale is separate.',parts=rows),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':
    path=DEST/'index.json';result=json.loads(path.read_text(encoding='utf-8'));bake_bamboo(result);path.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Bamboo swordsman: 16 body pieces + 1 sheathed sword; body aspect ratios preserved.')
