"""Import the user's printed 15-piece sheet with one rigid pelvis.

The input is RGB on paper, so mechanical extraction supplies alpha. Piece
aspect ratios are preserved; scalar bind sizes are calibrated against the
assembled figure, rather than trusting the page's same-scale caption.
"""
import json
import math
import shutil
import numpy as np
from PIL import Image
from scipy import ndimage
from bake_paper_cast import ROOT, DEST, digest, pack, texture_meta

SOURCE=ROOT/'art/rig-plain'
# slot, cell interior, pivot, distal hinge, fixed isotropic attachment scale
ROWS=[
 ('head-neck',(478,183,660,408),(576,374),None,1.06),
 ('torso',(704,194,863,382),(784,371),(790,240),None),
 ('pelvis',(895,204,1090,410),(994,236),None,1.33),
 ('near-upper-arm',(530,455,636,627),(594,480),(580,600),None),
 ('far-upper-arm',(743,455,842,627),(783,480),(802,600),None),
 ('near-forearm',(940,465,1045,648),(1004,485),(982,620),None),
 ('far-forearm',(1167,465,1265,648),(1196,483),(1232,617),None),
 ('near-hand',(522,726,592,852),(565,741),None,1),
 ('far-hand',(757,726,825,852),(779,741),None,.9),
 ('near-thigh',(962,691,1059,877),(1015,708),(1004,866),None),
 ('far-thigh',(1169,691,1258,877),(1214,708),(1221,866),None),
 ('near-shin',(530,913,618,1110),(577,922),(573,1100),None),
 ('far-shin',(748,913,829,1110),(782,924),(788,1100),None),
 ('near-foot',(923,978,1074,1080),(961,1005),None,1.1),
 ('far-foot',(1148,978,1290,1080),(1183,999),None,1.05),
]
LANDMARKS={
 'hip':[260,477],'neck':[236,325],
 'shoulderNear':[177,360],'shoulderFar':[249,367],
 'elbowNear':[137,486],'elbowFar':[327,548],
 'wristNear':[116,618],'wristFar':[354,618],
 'hipNear':[247,665],'hipFar':[219,655],
 'kneeNear':[266,829],'kneeFar':[212,797],
 'ankleNear':[278,1063],'ankleFar':[154,1035],
}
FRAME=[70,120,402,1140]


def isolate(sheet,box):
    rgb=np.array(sheet.crop(box).convert('RGB'))
    ink=(rgb.min(2)<180)|((rgb.max(2).astype(int)-rgb.min(2))>65)
    labels,_=ndimage.label(ink);areas=np.bincount(labels.ravel());areas[0]=0
    main=labels==areas.argmax();solid=ndimage.binary_fill_holes(main)
    mask=ndimage.binary_dilation(solid,iterations=1)
    alpha=np.where(mask,255,0).astype(np.uint8)
    # Enclosed paper holes remain transparent; cream embroidery is retained.
    paper=(rgb.min(2)>239)&((rgb.max(2).astype(int)-rgb.min(2))<18)
    alpha[paper]=0
    edge=mask & ~ndimage.binary_erosion(solid,iterations=1)
    alpha[edge]=np.minimum(alpha[edge],np.clip((255-rgb.min(2)[edge].astype(float))*5,0,255).astype(np.uint8))
    rgba=np.dstack([rgb,alpha]);rgba[alpha==0]=0
    image=Image.fromarray(rgba)
    bounds=image.getbbox();bounds=(max(0,bounds[0]-3),max(0,bounds[1]-3),min(image.width,bounds[2]+3),min(image.height,bounds[3]+3))
    x,y=box[:2]
    return image.crop(bounds),[x+bounds[0],y+bounds[1],x+bounds[2],y+bounds[3]]


def bake_plain(result):
    sheet=Image.open(SOURCE/'source.png').convert('RGB')
    images,records={},[]
    for name,box,pivot,tip,scale in ROWS:
        image,rect=isolate(sheet,box);images[name]=image
        local=lambda p:[p[0]-rect[0],p[1]-rect[1]]
        records.append(dict(id=name,source_rect=rect,pivot=local(pivot),tip=local(tip) if tip else None,scale=scale))
    atlas,positions=pack(images)
    origin=np.array(LANDMARKS['hip'],float)
    joints={k:np.array(v,float)-origin for k,v in LANDMARKS.items()}
    lean=math.degrees(math.atan2(joints['neck'][0],-joints['neck'][1]))
    def unrotate(p):
        a=math.radians(-lean);return [float(p[0]*math.cos(a)-p[1]*math.sin(a)),float(p[0]*math.sin(a)+p[1]*math.cos(a))]
    down=lambda p:math.degrees(math.atan2(-p[0],p[1]))
    lengths={'spine':float(np.linalg.norm(joints['neck']))}
    skin=dict(id='reference-plain',description='素发女侠 · 十五件单一腰胯',atlas='reference-plain',size=list(atlas.size),scale=1,reference=True,
        source_sheet='plain-source',reference_image='plain-assembled',assembly_label='十五件 · 单一腰胯',bind_lean=lean,
        canvas=[FRAME[2]-FRAME[0],FRAME[3]-FRAME[1]],canvas_origin=[LANDMARKS['hip'][0]-FRAME[0],LANDMARKS['hip'][1]-FRAME[1]],
        shoulders={s:unrotate(joints['shoulder'+s]) for s in ('Near','Far')},hips={s:unrotate(joints['hip'+s]) for s in ('Near','Far')},
        feet={s:joints['ankle'+s].tolist() for s in ('Near','Far')},knee_poles={'Near':1,'Far':1},
        angle_offsets={},hand_angle_offsets={'Near':-11,'Far':10},head_angle_offset=-lean,parts={},lengths=lengths,
        reference_landmarks={k:v.tolist() for k,v in joints.items()},
        notes='各片保持原长宽比，以左侧整身图校准连接与覆盖区；腰胯作为单一部件随腰骨旋转。')
    for side,prefix,ru,re in [('Near','near',-12,8),('Far','far',10,-8)]:
        upper=down(joints['elbow'+side]-joints['shoulder'+side]);lower=down(joints['wrist'+side]-joints['elbow'+side])
        skin['angle_offsets'][prefix+'_upper']=upper-lean-ru;skin['angle_offsets'][prefix+'_elbow']=lower-upper-re
        for kind,a,b in [('upper_arm','shoulder','elbow'),('forearm','elbow','wrist'),('thigh','hip','knee'),('shin','knee','ankle')]:lengths[kind+side]=float(np.linalg.norm(joints[b+side]-joints[a+side]))
    for kind in ('upper_arm','forearm','thigh','shin'):lengths[kind]=(lengths[kind+'Near']+lengths[kind+'Far'])/2
    partdir=SOURCE/'parts';partdir.mkdir(exist_ok=True)
    for i,row in enumerate(records,1):
        name=row['id'];image=images[name];x,y=positions[name];file=f'{i:02d}-{name}.png';image.save(partdir/file)
        p=dict(size=list(image.size),uv=[x/atlas.width,1-(y+image.height)/atlas.height,image.width/atlas.width,image.height/atlas.height],pivot=row['pivot'],source='rig-plain/parts/'+file,sha256=digest(partdir/file))
        side='Near' if name.startswith('near') else 'Far'
        if row['tip']:
            if name=='torso':bone='torso';layer=40
            else:
                kind=next(k for k in ('upper-arm','forearm','thigh','shin') if name.endswith(k));bone=('upper' if kind=='upper-arm' else kind)+side
                layer={'upperFar':10,'forearmFar':12,'thighFar':16,'shinFar':17,'thighNear':25,'shinNear':26,'upperNear':72,'forearmNear':75}[bone]
            p.update(bone=bone,tip=row['tip'],layer=layer)
        elif name=='head-neck':p.update(joint='neck',height=row['pivot'][1]*row['scale'],angle=0,follow='head',layer=30)
        elif name=='pelvis':p.update(joint='hip',height=image.height*row['scale'],angle=-lean,layer=50)
        else:
            hand=name.endswith('hand');p.update(joint=('wrist' if hand else 'ankle')+side,height=image.height*row['scale'],angle=0,follow=('hand' if hand else 'foot')+side,layer=(80 if side=='Near' else 14) if hand else (29 if side=='Near' else 20))
        skin['parts'][name]=p
        assert atlas.crop((x,y,x+image.width,y+image.height)).tobytes()==image.tobytes()
    skin['ground']=max(skin['feet'][s][1]+skin['parts'][('near' if s=='Near' else 'far')+'-foot']['height']*(1-skin['parts'][('near' if s=='Near' else 'far')+'-foot']['pivot'][1]/skin['parts'][('near' if s=='Near' else 'far')+'-foot']['size'][1]) for s in ('Near','Far'))
    path=DEST/'reference-plain.png';atlas.save(path,optimize=True);texture_meta(path);skin['atlas_sha256']=digest(path)
    ref,rect=isolate(sheet,FRAME);reference=Image.new('RGBA',(FRAME[2]-FRAME[0],FRAME[3]-FRAME[1]));reference.paste(ref,(rect[0]-FRAME[0],rect[1]-FRAME[1]));reference.save(DEST/'plain-assembled.png');texture_meta(DEST/'plain-assembled.png')
    shutil.copy2(SOURCE/'source.png',DEST/'plain-source.png');texture_meta(DEST/'plain-source.png')
    old=result['skins'].get(skin['id']);result['expected_parts']+=15-(len(old['parts']) if old else 0);result['expected_skins']+=0 if old else 1;result['skins'][skin['id']]=skin
    (SOURCE/'binding.json').write_text(json.dumps(dict(source_sha256=digest(SOURCE/'source.png'),method='Cell isolation, paper-background alpha matting, original interior RGB retained. Fixed isotropic bind sizes; no breadth or shear. Single pelvis.',reference_frame=FRAME,landmarks=LANDMARKS,parts=records),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':
    path=DEST/'index.json';result=json.loads(path.read_text(encoding='utf-8'));bake_plain(result);path.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print('Plain swordswoman: 15 pieces, one pelvis; assembled reference imported.')
