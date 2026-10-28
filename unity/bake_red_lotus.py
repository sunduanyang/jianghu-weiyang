"""Calibrate the user's original atlas through UVs; never repaint/resample it."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path
import numpy as np
from PIL import Image
from scipy import ndimage

ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'art/red-lotus'
OUT=ROOT/'unity/JianghuUnity/Assets/Resources/Characters/RedLotus'


def grid(rect, exclusions=(), nx=6, ny=10):
    x,y,w,h=rect
    xs=sorted(set([i*w/nx for i in range(nx+1)]+[v-x for r in exclusions for v in (r[0],r[2]) if x<v<x+w]))
    ys=sorted(set([i*h/ny for i in range(ny+1)]+[v-y for r in exclusions for v in (r[1],r[3]) if y<v<y+h]))
    vertices=[[u,v] for v in ys for u in xs];uv=[[(x+u)/1536,1-(y+v)/1024] for u,v in vertices];tri=[]
    for iy in range(len(ys)-1):
        for ix in range(len(xs)-1):
            cx=x+(xs[ix]+xs[ix+1])/2;cy=y+(ys[iy]+ys[iy+1])/2
            if any(a<=cx<=c and b<=cy<=d for a,b,c,d in exclusions):continue
            i=iy*len(xs)+ix;n=len(xs);tri.extend([i,i+1,i+n,i+1,i+n+1,i+n])
    return dict(vertices=vertices,uv=uv,triangles=tri)


def bake(source):
    ART.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
    original=ART/'source.png'
    if source and source.resolve()!=original.resolve():shutil.copyfile(source,original)
    if not original.exists():raise FileNotFoundError(original)
    shutil.copyfile(original,OUT/'sheet.png')
    # A data-only sprite ownership map separates nearby parts, the left-hand
    # assembled reference, and number labels without changing original RGBA.
    rgba=np.asarray(Image.open(original).convert('RGBA'));labels,count=ndimage.label(rgba[:,:,3]>50)
    objects=ndimage.find_objects(labels);owners=np.zeros(count+1,dtype=np.uint8)
    bounds={1:(452,68,707,354),2:(732,161,848,315),3:(885,98,1103,384),4:(1114,156,1326,378),5:(1327,155,1525,360),
            6:(514,397,675,621),7:(682,447,843,606),8:(866,501,992,574),9:(1032,412,1196,622),10:(1201,460,1373,595),11:(1396,506,1512,575),
            12:(504,642,642,975),13:(685,699,773,961),14:(825,838,990,975),15:(1028,646,1167,970),16:(1212,692,1312,964),17:(1353,836,1514,961)}
    for i,r in enumerate(objects,1):
        if r is None:continue
        box=(r[1].start,r[0].start,r[1].stop,r[0].stop)
        for number,bound in bounds.items():
            if box==bound:owners[i]=number
    assert len(owners[owners>0])==17
    ownership=owners[labels]
    for i in np.flatnonzero(owners):
        inside=ndimage.binary_fill_holes(labels==i);ownership[inside]=owners[i]
    distance,nearest=ndimage.distance_transform_edt(labels==0,return_indices=True)
    edge=(rgba[:,:,3]>0)&(rgba[:,:,3]<=50)&(distance<=3)
    ownership[edge]=ownership[nearest[0][edge],nearest[1][edge]]
    data_map=np.zeros(rgba.shape,dtype=np.uint8);data_map[:,:,:3]=ownership[:,:,None];data_map[:,:,3]=255
    Image.fromarray(data_map).save(OUT/'part-map.png')
    # Source coordinates are authored against the uploaded 1536 x 1024 PNG.
    # Label holes are omitted from mesh triangles; original RGBA pixels remain intact.
    specs=[
      ('torso','spine',40,[883,96,222,290],[993,363],[993,141],1,3,[]),
      ('back-skirt','pelvis',19,[1325,153,202,209],[1435,179],[1435,264],1,5,[]),
      ('upperFar','upperFar',20,[1024,407,181,223],[1068,451],[1160,588],1,9,[(992,416,1030,449)]),
      ('forearmFar','forearmFar',21,[1196,460,180,146],[1236,494],[1344,558],1,10,[]),
      ('handFar','handFar',22,[1384,502,152,89],[1436,542],[1498,543],1,11,[]),
      ('thighFar','thighFar',11,[1024,644,155,213],[1100,666],[1092,844],1,15,[(991,679,1032,712)]),
      ('shinFar','shinFar',12,[1211,703,110,221],[1257,725],[1262,913],1,16,[]),
      ('footFar','footFar',13,[1348,833,188,146],[1391,877],[1510,945],1,17,[]),
      ('thighNear','thighNear',30,[500,641,146,215],[575,661],[566,844],1,12,[(483,646,521,675)]),
      ('shinNear','shinNear',31,[681,701,98,224],[725,726],[725,914],1,13,[]),
      ('footNear','footNear',32,[820,832,181,154],[863,876],[979,952],1,14,[]),
      ('front-skirt','pelvis',34,[1110,148,226,244],[1227,180],[1227,265],1,4,[]),
      ('upperNear','upperNear',50,[510,394,176,236],[550,441],[638,587],1,6,[]),
      ('forearmNear','forearmNear',51,[675,447,176,169],[710,478],[811,565],1,7,[]),
      ('handNear','handNear',52,[859,498,144,88],[915,537],[975,541],1,8,[]),
    ]
    meshes=[]
    for name,bone,layer,rect,pivot,tip,breadth,number,exclusions in specs:
        x,y,w,h=rect
        part=dict(id=name,bone=bone,layer=layer,size=[w,h],pivot=[pivot[0]-x,pivot[1]-y],tip=[tip[0]-x,tip[1]-y],breadth=breadth,texture='sheet',source_number=number,source_rect=rect)
        if bone.startswith('foot'):
            heel=[1358,954] if bone.endswith('Far') else [831,959]
            part['heel_pivot']=[heel[0]-x,heel[1]-y]
        m=grid(rect,exclusions,nx=6 if bone in ('spine','pelvis') else 2,ny=10 if bone in ('spine','pelvis') else 3)
        if bone=='spine':
            bones=['pelvis','belly','torso'];weights=[]
            for u,v in m['vertices']:
                height=1-v/h;weights.append([max(0,1-2*height),1-abs(2*height-1),max(0,2*height-1)])
        elif bone=='pelvis':
            bones=['pelvis','thighFar','thighNear'];weights=[]
            for u,v in m['vertices']:
                t=max(0,min(1,(v-45)/(h-45)));t=t*t*(3-2*t)*.45
                weights.append([1-t,t*(1-u/w),t*u/w])
        else:bones=[bone];weights=[[1] for _ in m['vertices']]
        assert all(abs(sum(v)-1)<1e-6 for v in weights)
        meshes.append(dict(part=part,**m,weights=weights,bones=bones,skin=bone in ('spine','pelvis')))
    head_rect=[450,66,259,290];head_mesh=grid(head_rect,(),10,14)
    head=dict(id='red-lotus-original',texture='sheet',source_number=1,size=head_rect[2:],pivot=[604-450,278-66],tip=[604-450,97-66],
              uv=[450/1536,1-356/1024,259/1536,290/1024],mesh=head_mesh,
              neck_center=604-450,neck_start=303-66,neck_end=290,neck_width=48,
              face_rects=dict(eyes=[209,150,34,13],brows=[207,136,37,9],lips=[245,214,12,6]),baked_features=True)
    data=dict(version=1,profile='bloody-reference-v1',preserve_source_colour=True,single_head=True,
              body_meshes=meshes,heads=[head],head_layer=35,collar_layer=40,
              source_sha256=hashlib.sha256(original.read_bytes()).hexdigest(),
              source_size=[1536,1024],bound_source_numbers=[1]+[s[7] for s in specs],
              spare_source_numbers=[2],notes=['02 retained in source as spare shoulder/cuff transition; not forced into anatomy.',
              '12/15 thigh UV windows exclude their duplicate lower boot; 13/16 stop at ankle; 14/17 are independent feet.'])
    (OUT/'index.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
    (ART/'binding.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(source_sha256=data['source_sha256'],body_pieces=len(meshes),head_pieces=1,spare=[2],source_pixels='unchanged')))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('source',nargs='?',type=Path);bake(p.parse_args().source)
