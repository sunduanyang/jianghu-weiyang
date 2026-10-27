"""Re-extract final illustrations, removing paper in enclosed limb/prop gaps."""
import hashlib
import numpy as np
from PIL import Image
from bake_paper_cast import SOURCE,isolate

def extract_references(manifest,calibration):
 for c in manifest['characters']:
  row=calibration[c['id']];rect=row['source_rect'];sheet=Image.open(SOURCE/c['file']).convert('RGB')
  im,actual=isolate(sheet,rect);reference=Image.new('RGBA',(rect[2]-rect[0],rect[3]-rect[1]));reference.paste(im,(actual[0]-rect[0],actual[1]-rect[1]));a=np.array(reference)
  y,x=np.mgrid[:a.shape[0],:a.shape[1]];gx=x+rect[0];gy=y+rect[1]
  exclusions=[]
  if c['n']==17:exclusions=[(0,0,160,165)]
  if c['n']==23:exclusions=[(0,0,290,150),(430,140,1536,148)]
  for x0,y0,x1,y1 in exclusions:a[(gx>=x0)&(gx<x1)&(gy>=y0)&(gy<y1)]=0
  rgb=np.array(sheet);samples=rgb[rect[1]:rect[3],max(0,rect[0]-12):rect[0],:3].reshape(-1,3)
  bright=samples.min(1)>200
  bg=np.median(samples[bright] if bright.any() else samples,axis=0)
  paper=np.abs(a[:,:,:3].astype(float)-bg).max(2)<9
  a[paper]=0
  p=SOURCE/c['id']/'assembly-reference.png';Image.fromarray(a).save(p)
  row['reference_sha256']=hashlib.sha256(p.read_bytes()).hexdigest();row['background_rgb']=bg.tolist();row['alpha_recipe']='remove exterior paper and all enclosed regions matching measured paper color'
 return calibration
