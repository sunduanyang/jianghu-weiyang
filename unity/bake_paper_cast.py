"""Shared atlas/extraction helpers and the exploded-component binding entrypoint."""
import hashlib,re,json,os
from pathlib import Path
import numpy as np
from PIL import Image
from scipy import ndimage
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'art/puppet-modern-24'
RESOURCE_ROOT=Path(os.environ.get('JIANGHU_ART_OUTPUT',ROOT/'unity/JianghuUnity/Assets/Resources'))
DEST=RESOURCE_ROOT/'PaperCast'
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def pack(images):
 width,x,y,row=1024,8,8,0;positions={}
 for name,im in sorted(images.items(),key=lambda kv:(-kv[1].height,kv[0])):
  if x+im.width+8>width:x,y,row=8,y+row+16,0
  positions[name]=(x,y);x+=im.width+16;row=max(row,im.height)
 height=((y+row+8+255)//256)*256;assert height<=4096
 atlas=Image.new('RGBA',(width,height))
 for name,im in images.items():atlas.paste(im,positions[name])
 return atlas,positions

def texture_meta(path):
 template=(ROOT/'unity/paper-texture-importer.txt').read_text(encoding='utf-8')
 logical=ROOT/'unity/JianghuUnity/Assets/Resources'/path.relative_to(RESOURCE_ROOT)
 guid=hashlib.md5(str(logical.relative_to(ROOT)).replace('\\','/').encode()).hexdigest()
 path.with_name(path.name+'.meta').write_text(re.sub(r'guid: [0-9a-f]+','guid: '+guid,template,count=1),encoding='utf-8')

def isolate(sheet,box):
 rgb=np.asarray(sheet.crop(box).convert('RGB'))
 dark=(rgb.min(2)<178)|((rgb.max(2).astype(int)-rgb.min(2))>65)
 labels,_=ndimage.label(dark);areas=np.bincount(labels.ravel());areas[0]=0
 if areas.max()<100:raise ValueError(f'Empty character cell: {box}')
 main=labels==areas.argmax();nearby=ndimage.binary_dilation(main,iterations=10)
 selected=np.unique(labels[nearby & dark]);keep=np.isin(labels,[i for i in selected if i and areas[i]>25])
 solid=ndimage.binary_fill_holes(keep);mask=ndimage.binary_dilation(solid,iterations=1)
 paper=(rgb.min(2)>232)&((rgb.max(2).astype(int)-rgb.min(2))<26)
 alpha=np.where(mask,255,0).astype(np.uint8)
 # Tiny printed paper apertures are transparent; larger cream fabric is retained.
 plabels,_=ndimage.label(paper & solid);parea=np.bincount(plabels.ravel())
 alpha[np.isin(plabels,[i for i in range(1,len(parea)) if 3<=parea[i]<400])]=0
 edge=mask & ~solid;alpha[edge]=np.clip((245-rgb.min(2)[edge].astype(float))*5,0,255).astype(np.uint8)
 rgba=np.dstack([rgb,alpha]);rgba[alpha==0]=0;image=Image.fromarray(rgba);b=image.getbbox()
 b=(max(0,b[0]-3),max(0,b[1]-3),min(image.width,b[2]+3),min(image.height,b[3]+3))
 return image.crop(b),[box[0]+b[0],box[1]+b[1],box[0]+b[2],box[1]+b[3]]

def bake():
    from bake_split_cast import bake_split_cast
    bake_split_cast()

if __name__=='__main__':
    bake()
