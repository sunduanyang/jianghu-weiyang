"""Slice the requested 24 source sheets; retain pixels and explicit rig calibration."""
from __future__ import annotations
import argparse, hashlib, json, zipfile
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from scipy.optimize import linear_sum_assignment

ROOT=Path(__file__).resolve().parents[1]
ARCHIVE=ROOT/'output/imagegen/cast-24-shadow-puppets-20261029.zip'
OUT=ROOT/'output/cast24-binding-20261029'
RES=ROOT/'unity/JianghuUnity/Assets/Resources/Characters/Cast24'
NAMES=['head','neck','torso','belly','pelvis','near-upper-arm','near-forearm','near-hand','far-upper-arm','far-forearm','far-hand','near-thigh','near-shin','near-foot','far-thigh','far-shin','far-foot']
TARGET=np.array([(575,210),(790,240),(995,230),(1220,260),(1430,260),(590,500),(760,530),(935,545),(1110,510),(1295,530),(1470,545),(580,805),(740,835),(910,910),(1110,805),(1260,835),(1440,910)])
LABELS=['农家青年','农家姑娘','中年农人','中年农妇','老翁','老妪','男童','女童','书生','书香女子','行商','女商人','镖师','女镖师','渔人','猎户','匠人','女药师','戏伶','道士','僧人','官员','乞丐','女侠']

def dump(path,obj):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(obj,ensure_ascii=False,indent=2),encoding='utf-8')

def sources():
    OUT.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(ARCHIVE) as z:
        manifest=json.loads(z.read('prompts-and-manifest.json'))
        for c in manifest['characters']:
            data=z.read(c['file']); sha=hashlib.sha256(data).hexdigest()
            if sha.lower()!=c['sha256'].lower():raise ValueError('Source SHA mismatch: '+c['id'])
            p=OUT/'raw'/c['file'];p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
            yield c,p,sha

def components(image):
    rgba=np.array(image);visible=rgba[:,:,3]>64
    # Two costume panels occasionally touch through a one-pixel raster bridge.
    # Divide only a wide fused group at its narrowest intervening column.
    for attempt in range(3):
        lab,n=ndimage.label(visible);areas=np.bincount(lab.ravel());boxes=ndimage.find_objects(lab)
        repaired=False
        for i,s in enumerate(boxes,1):
            if s is None or areas[i]<500:continue
            x0,x1=s[1].start,s[1].stop;y0,y1=s[0].start,s[0].stop
            if x0>1000 and x1-x0>340 and y1<420:
                profile=np.sum(lab[y0:y1,x0:x1]==i,axis=0);lo=int((x1-x0)*.3);hi=int((x1-x0)*.7)
                cut=x0+lo+int(np.argmin(profile[lo:hi]));visible[y0:y1,cut]=False;repaired=True;break
        if not repaired:break
    lab,n=ndimage.label(visible)
    areas=np.bincount(lab.ravel());boxes=ndimage.find_objects(lab)
    items=[]
    for i,s in enumerate(boxes,1):
        if s is None or areas[i]<500:continue
        box=[s[1].start,s[0].start,s[1].stop,s[0].stop]
        center=np.array([(box[0]+box[2])/2,(box[1]+box[3])/2])
        if center[0]<470:continue
        items.append(dict(component=i,box=box,area=int(areas[i]),center=center.tolist()))
    if len(items)<17:raise ValueError(f'Only {len(items)} isolated candidates')
    distances=np.array([np.sum(((np.array(v['center'])-TARGET)/[160,175])**2,axis=1) for v in items]).T
    rows,cols=linear_sum_assignment(distances)
    assigned=[None]*17
    for r,c in zip(rows,cols):assigned[r]=items[c]|dict(number=int(r+1),name=NAMES[r],cost=float(distances[r,c]))
    return lab,assigned,[v for j,v in enumerate(items) if j not in cols]

def inspect():
    records=[];contact=Image.new('RGB',(1536,24*200),'#eeeeea');draw=ImageDraw.Draw(contact)
    for c,p,sha in sources():
        im=Image.open(p).convert('RGBA');lab,parts,extras=components(im)
        preview=Image.new('RGB',im.size,'#e7e2d2');preview.paste(im,mask=im.getchannel('A'));d=ImageDraw.Draw(preview)
        for part in parts:
            b=part['box'];d.rectangle(b,outline='#00ff66',width=3);d.text((b[0]+2,b[1]+2),str(part['number']),fill='#ff0022',stroke_width=1,stroke_fill='white')
        preview.thumbnail((300,200));y=(c['number']-1)*200;contact.paste(preview,(0,y))
        for i,part in enumerate(parts):
            tile=im.crop(part['box']);tile.thumbnail((68,158));x=310+i*71;contact.paste(tile,(x,y+24),tile)
            draw.text((x,y+5),str(i+1),fill='black')
        draw.text((3,y+182),c['id'],fill='black')
        records.append(dict(id=c['id'],label=LABELS[c['number']-1],file=c['file'],sha256=sha,parts=parts,extras=extras))
        print(c['id'],len(parts),'extras',len(extras),'cost',round(max(v['cost'] for v in parts),3))
    dump(OUT/'segmentation.json',records)
    contact.save(OUT/'segmentation-contact.png')
    for start in range(0,24,4):contact.crop((0,start*200,1536,(start+4)*200)).save(OUT/f'segmentation-{start+1:02}.png')

BONES=['head','neck','torso','belly','pelvis','upperNear','forearmNear','handNear','upperFar','forearmFar','handFar','thighNear','shinNear','footNear','thighFar','shinFar','footFar']
LAYERS=[35,36,40,41,44,50,51,52,20,21,22,31,30,32,11,10,12]
LONG={'f-youth-farmer':(1.,.77),'f-middle-farmer':(.64,.74),'m-elder':(.64,.76),'f-elder':(.65,.74),'m-scholar':(.66,.77),'f-scholar':(.70,.77),'m-merchant':(.64,.74),'f-merchant':(.66,.74),'f-healer':(.68,.74),'m-performer':(.65,.65),'m-daoist':(1.,.77),'m-monk':(.64,.76),'m-official':(1.,.77),'f-swordswoman':(1.,.66)}

def anchors(image,number,role):
    w,h=image.size
    points={1:((.49,.92),(.49,.04)),2:((.48,.90),(.48,.10)),3:((.61,.91),(.61,.08)),4:((.50,.92),(.50,.08)),5:((.50,.06),(.50,.94)),6:((.33,.12),(.73,.88)),7:((.12,.16),(.88,.81)),8:((.10,.45),(.90,.45)),9:((.33,.12),(.73,.88)),10:((.12,.16),(.88,.81)),11:((.10,.45),(.90,.45)),12:((.49,.04),(.51,.96)),13:((.50,.06),(.50,.94)),14:((.27,.10),(.94,.84)),15:((.49,.04),(.51,.96)),16:((.50,.06),(.50,.94)),17:((.27,.10),(.94,.84))}
    a,b=points[number]
    if number==2:a,b=(.65,.69),(.50,.22)
    if number in {8,11}:
        rgba=np.array(image);r,g,bv=[rgba[:,:,i].astype(float) for i in range(3)]
        skin=(rgba[:,:,3]>128)&(r>100)&(r>g*1.12)&(g>bv*1.10)
        lab,n=ndimage.label(skin);areas=np.bincount(lab.ravel());areas[0]=0
        if areas.max()>40:
            ys,xs=np.nonzero(lab==int(areas.argmax()));cy=float(np.median(ys))
            if xs.max()-xs.min()>w*.18:a=((float(xs.min())+1)/w,cy/h);b=((float(xs.max())-1)/w,cy/h)
    if number==1:
        rgba=np.array(image);r,g,bv=[rgba[:,:,i].astype(float) for i in range(3)]
        skin=(rgba[:,:,3]>128)&(r>100)&(r>g*1.12)&(g>bv*1.10)
        skin[:,:int(w*.42)]=False;skin[int(h*.85):]=False
        lab,n=ndimage.label(skin);areas=np.bincount(lab.ravel());areas[0]=0
        if areas.max()>100:
            k=int(areas.argmax());ys,xs=np.nonzero(lab==k);x0,x1,y0,y1=xs.min(),xs.max(),ys.min(),ys.max();fw=x1-x0;fh=y1-y0
            px=float(x0+fw*.18);py=float(min(h-3,y1+fh*.14));ty=float(max(3,y0-fh*.48))
            if py-ty>h*.34:a=(px/w,py/h);b=(px/w,ty/h)
        if role in {'m-child','f-child'}:b=(a[0],a[1]-(a[1]-b[1])*.78)
    if number in {12,15} and role in LONG:
        # An independent long garment hangs from the waist, not from a knee.
        total=LONG[role][1];a=(.50,.03);b=(.50,.03+.3929377/total)
    if number in {14,17}:
        # Calibrate the ankle against actual painted sole pixels. Heel/toe
        # still determine one uniform similarity; there is no shoe flattening.
        heel=np.array([w*.08,h*.78]);tip=np.array([b[0]*w,b[1]*h]);axis=tip-heel
        factor=.24/float(np.dot(axis,axis));m10=-axis[1]*factor;m11=axis[0]*factor
        ys,xs=np.nonzero(np.array(image)[:,:,3]>=48);px=w*.27
        lowest=float(np.max(m10*(xs+.5)+m11*(ys+.5)))
        py=(lowest-.13-m10*px)/m11;a=(px/w,py/h)
    return [[round(a[0]*w,4),round(a[1]*h,4)],[round(b[0]*w,4),round(b[1]*h,4)]]

def grid(w,h):return [[0,0],[w,0],[0,h],[w,h]],[0,1,2,1,3,2]

def meta_texture(path):
    # Avoid Unity's lossy default compression on carved alpha and fine outlines.
    meta=path.with_suffix(path.suffix+'.meta')
    guid=hashlib.md5(str(path.relative_to(ROOT)).encode()).hexdigest()
    text=f'''fileFormatVersion: 2\nguid: {guid}\nTextureImporter:\n  serializedVersion: 13\n  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 0\n    sRGBTexture: 1\n  isReadable: 0\n  textureFormat: 1\n  maxTextureSize: 1024\n  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n  nPOTScale: 0\n  lightmap: 0\n  compressionQuality: 100\n  spriteMode: 0\n  alphaUsage: 1\n  alphaIsTransparency: 1\n  textureType: 0\n  textureShape: 1\n  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 1024\n    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: 0\n    compressionQuality: 100\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: 0\n'''
    if not meta.exists():meta.write_text(text,encoding='utf-8')

def build():
    catalog=[];evidence=[]
    for c,p,sha in sources():
        role=c['id'];im=Image.open(p).convert('RGBA');rgba=np.array(im);lab,parts,extras=components(im)
        distance,nearest=ndimage.distance_transform_edt(lab==0,return_indices=True)
        owned=lab[nearest[0],nearest[1]];owned[distance>2.25]=0
        folder=RES/role;folder.mkdir(parents=True,exist_ok=True);body=[];records=[];head=None
        for part in parts:
            num=part['number'];full=rgba.copy();full[owned!=part['component']]=0
            b=part['box'];x0=max(0,b[0]-3);y0=max(0,b[1]-3);x1=min(im.width,b[2]+3);y1=min(im.height,b[3]+3)
            image=Image.fromarray(full[y0:y1,x0:x1]);name=f'{num:02}-{NAMES[num-1]}';export=OUT/'parts'/role/(name+'.png');export.parent.mkdir(parents=True,exist_ok=True);image.save(export)
            clip=1.
            if num in {12,15}:clip=LONG[role][0] if role in LONG else .62
            if num in {13,16}:clip=.80
            # Keep the full source crop as an independently reusable export;
            # a separate runtime crop excludes painted duplicate boot segments.
            active=image.crop((0,0,image.width,max(8,round(image.height*clip))))
            path=folder/'parts'/(name+'.png');path.parent.mkdir(parents=True,exist_ok=True);active.save(path);meta_texture(path)
            w,h=active.size;pivot,tip=anchors(active,num,role);bone=BONES[num-1];layer=LAYERS[num-1]
            garment=num in {12,15} and role in LONG
            primary='pelvis' if garment else bone
            if garment:layer=43 if num==12 else 24
            vertices,triangles=grid(w,h);key='parts/'+name
            descriptor=dict(id=NAMES[num-1] if num!=1 else 'head-neck',bone=bone,layer=layer,size=[w,h],pivot=pivot,tip=tip,breadth=1,texture=key,source_number=0,source_rect=[x0,y0,x1-x0,y1-y0],source_component=num)
            if garment:descriptor['primary_bone']='pelvis'
            if num in {14,17}:descriptor['heel_pivot']=[round(w*.08,4),round(h*.78,4)]
            if num==1:
                # Facial marks are baked in the original head, not recoloured.
                face={'eyes':[w*.77,h*.39,max(5,w*.08),max(4,h*.045)],'brows':[w*.76,h*.34,max(5,w*.09),max(3,h*.035)],'lips':[w*.88,h*.56,max(4,w*.06),max(3,h*.025)]}
                head=dict(size=[w,h],pivot=pivot,tip=tip,texture=key,uv=[0,0,1,1],mesh=dict(vertices=vertices,triangles=triangles),neck_start=h+10,neck_center=w*.5,neck_end=h+20,neck_width=1,face_rects=face,baked_features=True,source_number=0,source_component=1)
            else:body.append(dict(part=descriptor,vertices=vertices,uv=[[0,1],[1,1],[0,0],[1,0]],triangles=triangles,bones=[primary],weights=[[1] for _ in vertices]))
            kept=np.array(active);retained=kept[:,:,3]>0;original=rgba[y0:y0+active.height,x0:x0+active.width]
            assert np.array_equal(kept[retained],original[retained]),(role,name)
            assert retained.sum()>200 and kept.shape[2]==4
            records.append(dict(number=num,name=name,source_box=[x0,y0,x1,y1],export=str(export.relative_to(ROOT)),runtime=str(path.relative_to(ROOT)),runtime_clip_fraction=clip,source_pixel_values_preserved=True,visible_pixels=int(retained.sum()),pivot=pivot,tip=tip,bone=bone,primary_bone=primary))
        index=dict(version=1,profile='cast24-rigid-parts-v1',preserve_source_colour=True,single_head=True,literal_source_parts=True,body_meshes=body,heads=[head],head_layer=35,collar_layer=40,source_sha256=sha,bound_source_numbers=list(range(1,18)),notes=['Source component labels and assembled example are excluded.','17 separate source crops are exported; runtime clips remove duplicate lower boot drawings.','Long garment panels hang rigidly from pelvis; no simulation or skeleton dimensions are modified.'])
        dump(folder/'index.json',index)
        entry=dict(id=role,asset='cast24-'+role,label=LABELS[c['number']-1],number=c['number'],parts=17,source_sha256=sha)
        catalog.append(entry);evidence.append(entry|dict(parts=records))
        print('Built',role,17,'parts',flush=True)
    dump(RES/'catalog.json',dict(version=1,characters=catalog))
    dump(OUT/'binding-manifest.json',dict(archive=str(ARCHIVE),archive_sha256=hashlib.sha256(ARCHIVE.read_bytes()).hexdigest(),characters=evidence,part_count=sum(len(c['parts']) for c in evidence)))
    preview(catalog)

def preview(catalog):
    # Independent static construction preview; production verification uses Unity.
    bones={'pelvis':((0,.883),(0,1.033)),'belly':((0,1.033),(0,1.183)),'torso':((0,1.183),(0,1.383)),'neck':((0,1.383),(0,1.473)),'head':((0,1.473),(0,1.773))}
    for side,x in [('Near',.06),('Far',-.06)]:
        bones['upper'+side]=((x,1.343),(x+.015,1.061));bones['forearm'+side]=(bones['upper'+side][1],(x+.068,.809));bones['hand'+side]=(bones['forearm'+side][1],(x+.089,.713))
        bones['thigh'+side]=((x,.913),(x+.043,.522));bones['shin'+side]=(bones['thigh'+side][1],(x,.13));bones['foot'+side]=((x-.08,.075),(x+.16,.075))
    contact=Image.new('RGB',(6*270,4*610),'#e5decb');draw=ImageDraw.Draw(contact)
    for i,c in enumerate(catalog):
        d=json.loads((RES/c['id']/'index.json').read_text(encoding='utf-8'));parts=[p['part'] for p in d['body_meshes']]+[d['heads'][0]|dict(bone='head',layer=35)]
        canvas=Image.new('RGBA',(768,900));unit=420
        for part in sorted(parts,key=lambda v:v['layer']):
            bone=part['bone'];a,b=np.array(bones[bone],float)
            if bone=='pelvis':a,b=b,a
            anchor=a.copy();heel=a.copy()
            if bone.startswith('foot'):anchor=np.array(bones['shin'+bone[4:]][1]);a=anchor
            a=np.array([a[0],-a[1]]);b=np.array([b[0],-b[1]]);heel=np.array([heel[0],-heel[1]])
            pivot=np.array(part['pivot']);tip=np.array(part['tip']);source=tip-pivot;delta=b-a
            if 'heel_pivot' in part:source=tip-np.array(part['heel_pivot']);delta=b-heel
            factor=complex(*delta)/complex(*source);m=np.array([[factor.real,-factor.imag],[factor.imag,factor.real]])*unit
            target=np.array([384,850])+a*unit;translation=target-m@pivot;inv=np.linalg.inv(m);shift=-inv@translation
            image=Image.open(RES/c['id']/(part['texture']+'.png')).convert('RGBA')
            warped=image.transform(canvas.size,Image.Transform.AFFINE,tuple([inv[0,0],inv[0,1],shift[0],inv[1,0],inv[1,1],shift[1]]),Image.Resampling.BICUBIC)
            canvas=Image.alpha_composite(canvas,warped)
        out=OUT/'assembly-preview'/f"{c['number']:02}-{c['id']}.png";out.parent.mkdir(parents=True,exist_ok=True);canvas.save(out)
        tile=canvas.copy();tile.thumbnail((260,560));x=(i%6)*270;y=(i//6)*610;contact.paste(tile,(x+(270-tile.width)//2,y),tile);draw.text((x+8,y+565),c['id'],fill='black')
    contact.save(OUT/'assembly-contact.png')
    for row in range(4):contact.crop((0,row*610,1620,(row+1)*610)).save(OUT/f'assembly-row-{row+1}.png')

def reanchor():
    manifest=json.loads((OUT/'binding-manifest.json').read_text(encoding='utf-8'))
    catalog=json.loads((RES/'catalog.json').read_text(encoding='utf-8'))['characters']
    for c in catalog:
        path=RES/c['id']/'index.json';d=json.loads(path.read_text(encoding='utf-8'))
        for mesh in d['body_meshes']:
            p=mesh['part'];image=Image.open(RES/c['id']/(p['texture']+'.png'))
            p['pivot'],p['tip']=anchors(image,p['source_component'],c['id'])
            if p['source_component'] in {12,15} and c['id'] in LONG:
                p['bone']=BONES[p['source_component']-1];p['primary_bone']='pelvis';mesh['bones']=['pelvis']
        dump(path,d)
        record=next(r for r in manifest['characters'] if r['id']==c['id'])
        for item in record['parts']:
            if item['number']!=1:
                p=next(m['part'] for m in d['body_meshes'] if m['part']['source_component']==item['number'])
                item['pivot'],item['tip']=p['pivot'],p['tip'];item['bone']=p['bone'];item['primary_bone']=p.get('primary_bone',p['bone'])
    dump(OUT/'binding-manifest.json',manifest);preview(catalog)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--inspect',action='store_true');parser.add_argument('--reanchor',action='store_true');args=parser.parse_args()
    if args.inspect:inspect()
    elif args.reanchor:reanchor()
    else:build()
