"""Keep the full transparent source pieces separate from rig-only masks."""
import argparse,json,shutil,sys,zipfile
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'unity'))
from bake_split_cast import source_rect
from bake_paper_cast import digest

def main():
    parser=argparse.ArgumentParser();parser.add_argument('output',type=Path);args=parser.parse_args()
    output=args.output.resolve();output.mkdir(parents=True,exist_ok=True);source=ROOT/'art/puppet-modern-24'
    cast=json.loads((source/'manifest.json').read_text(encoding='utf-8'));font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',15)
    core=['head-neck','torso','pelvis']+[side+'-'+part for side in ('near','far') for part in ('upper-arm','forearm','hand','thigh','shin','foot')]
    manifest={'role':'Full transparent source pieces before any rig masks, crops or joint clipping. Runtime masks are stored separately.','characters':{},'parts':0}
    for character in cast['characters']:
        cid=character['id'];folder=output/cid;folder.mkdir(exist_ok=True);sheet=Image.open(source/character['file']);items=[]
        names=core+(['prop'] if (source/cid/'prop.png').exists() else [])
        preview=Image.new('RGB',(1200,900),'#e6e3dd');draw=ImageDraw.Draw(preview)
        for row in range(0,900,12):
            for col in range(0,1200,12):
                if (row//12+col//12)%2:draw.rectangle((col,row,col+11,row+11),fill='#c9c6c0')
        for i,name in enumerate(names):
            original=source/cid/(name+'.png');im=Image.open(original).convert('RGBA');a=np.asarray(im)
            if not (a[:,:,3]==0).any():raise ValueError(cid+'/'+name+' lacks a transparent background')
            rect,mirror=source_rect(sheet,im,cid,name);target=folder/(name+'.png');shutil.copy2(original,target)
            items.append(dict(part=name,file=str(target.relative_to(output)).replace('\\','/'),sha256=digest(target),source_sheet=character['file'],source_rect=rect,mirror=mirror,size=list(im.size),transparent_pixels=int((a[:,:,3]==0).sum())))
            im.thumbnail((225,185));x=(i%4)*300;y=(i//4)*225;preview.paste(im,(x+(300-im.width)//2,y+28),im);draw.text((x+6,y+3),name,font=font,fill='#172d31')
        preview.save(output/(cid+'-checkerboard.jpg'),quality=95)
        manifest['characters'][cid]=items;manifest['parts']+=len(items)
    (output/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    archive=output.with_suffix('.zip')
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
        for path in sorted(output.rglob('*')):
            if path.is_file():z.write(path,path.relative_to(output))
    print('Full transparent pieces:',manifest['parts'],'characters:',len(manifest['characters']),'archive:',archive)

if __name__=='__main__':main()
