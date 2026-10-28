"""Package the approved fixed UV body and calibrated modular head library."""
import hashlib
import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT/'art/character-modular'
RES = ROOT/'unity/JianghuUnity/Assets/Resources'
OUT = RES/'Characters'


def bake():
    OUT.mkdir(parents=True, exist_ok=True)
    base=json.loads((RES/'ModularPuppet/index.json').read_text(encoding='utf-8'))
    for name in ('cloud','jade','features','socket-mask'):
        shutil.copy2(RES/'ModularPuppet'/f'{name}.png',OUT/f'{name}.png')
    head_files=('heads-life-stages','heads-roles')
    for name in head_files:
        shutil.copy2(ART/f'{name}.png',OUT/f'{name}.png')
        a=np.asarray(Image.open(OUT/f'{name}.png').convert('RGBA')).astype(float)/255
        rgb=a[:,:,:3];r,g,b=rgb[:,:,0],rgb[:,:,1],rgb[:,:,2];light=rgb.mean(2);visible=a[:,:,3]>.08
        skin=(r>g+.07)&(g>b+.08)&(light>.35)&visible
        skin_shadow=(r>g+.025)&(g>b+.035)&(light>.35)&visible
        hair=ndimage.binary_opening(visible&~skin_shadow&(light>.035)&(light<.85),iterations=2)
        tie=visible&(g>r*1.10)&(g>b*.95)&(light>.08)
        m=np.zeros(a.shape,dtype=np.uint8);m[:,:,0]=skin*255;m[:,:,1]=(hair&~tie)*255;m[:,:,2]=tie*255;m[:,:,3]=255
        Image.fromarray(m).save(OUT/f'{name}-mask.png')
    for name in ('cloud','jade'):
        a=np.asarray(Image.open(OUT/f'{name}.png').convert('RGBA')).astype(float)/255
        rgb=a[:,:,:3];r,g,b=rgb[:,:,0],rgb[:,:,1],rgb[:,:,2];light=rgb.mean(2);visible=a[:,:,3]>.05
        m=np.zeros(a.shape,dtype=np.uint8)
        cool=((b>r*1.15)|(g>r*1.08))&visible&(light>.12)
        other=visible&~cool&(light>.15)
        m[:,:,1]=cool*255;m[:,:,2]=other*255;m[:,:,3]=255
        for mesh in base['meshes']:
            part=mesh['part']
            if 'hand' not in part['id']:continue
            u,v,w,h=part['uv'];x,y=round(u*1024),round((1-v-h)*1024);pw,ph=part['size']
            warm=(r[y:y+ph,x:x+pw]>g[y:y+ph,x:x+pw]+.025)&(g[y:y+ph,x:x+pw]>b[y:y+ph,x:x+pw]+.04)&(light[y:y+ph,x:x+pw]>.45)&visible[y:y+ph,x:x+pw]
            region=m[y:y+ph,x:x+pw];region[warm,0]=255;region[warm,1:3]=0
        Image.fromarray(m).save(OUT/f'{name}-mask.png')
    # Logical source-pixel coordinates use 512x512 cells even when the generator
    # returns a larger atlas. Original RGBA outputs are never painted over.
    specs=[
      ('adult-m-short',0,0,0,[270,380],[270,133],[354,246,63,23],[348,228,65,11],[412,320,18,7]),
      ('adult-f-long',0,1,0,[278,380],[278,137],[378,265,55,21],[373,247,58,10],[427,327,17,7]),
      ('elder-m-beard',0,2,0,[245,386],[245,119],[352,239,61,22],[347,221,63,11],[405,316,17,7]),
      ('elder-f-bun',0,0,1,[250,389],[250,137],[337,248,58,21],[331,230,61,10],[398,330,17,7]),
      ('boy',0,1,1,[267,387],[267,169],[333,281,54,21],[329,264,56,10],[391,337,15,6]),
      ('girl',0,2,1,[282,390],[282,179],[350,278,56,21],[344,260,59,10],[412,338,15,6]),
      ('monk',1,0,0,[250,385],[250,125],[354,234,63,23],[350,216,64,11],[422,333,16,7]),
      ('adult-m-topknot',1,1,0,[235,390],[235,125],[354,246,61,23],[350,228,64,11],[423,343,16,7]),
      ('adult-f-short',1,0,1,[250,376],[250,140],[353,239,62,23],[349,221,64,11],[422,319,17,7]),
      ('elder-m-sparse',1,1,1,[235,385],[235,115],[359,228,63,23],[355,210,66,11],[422,314,17,7]),
    ]
    heads=[]
    for name,atlas,col,row,pivot,tip,eyes,brows,lips in specs:
        cols=3 if atlas==0 else 2;rows=2
        heads.append(dict(id=name,texture=head_files[atlas],size=[512,512],pivot=pivot,tip=tip,
                          uv=[col/cols,1-(row+1)/rows,1/cols,1/rows],
                          neck_center=pivot[0],neck_start=pivot[1],neck_end=500,
                          face_rects=dict(eyes=eyes,brows=brows,lips=lips)))
    data=dict(version=2,profile=base['profile'],body_meshes=[m for m in base['meshes'] if m['part']['id']!='head-neck'],
              features=base['features'],heads=heads,head_layer=35,collar_layer=40,
              source_hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in ART.glob('*.png')})
    (OUT/'index.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'Packaged {len(heads)} calibrated heads, 14 fixed-UV body slots, 9 independent facial feature sprites.')


if __name__=='__main__':bake()
