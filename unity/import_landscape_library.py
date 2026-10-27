"""Archive supplied PNGs, deduplicate sources and calibrate native terrain exports."""
from pathlib import Path
import hashlib
import json
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu import city_gardens
from jianghu.city_regions import NAMES


def main():
    source=Path(r'D:\Desktop\美术');archive=ROOT/'art/landscape-library-20261027'
    originals=archive/'source';originals.mkdir(parents=True,exist_ok=True)
    runtime=ROOT/'unity/JianghuUnity/Assets/Resources/CityBiomes'
    old=json.loads((runtime/'art-pack.json').read_text(encoding='utf-8'))
    entries=[e for e in old['assets'] if e['id'] in city_gardens.city_biomes.ASSETS]
    old_hashes={e['source_sha256']:e['id'] for e in entries};seen={};sources=[]
    for i,file in enumerate(sorted(source.glob('*.png')),1):
        data=file.read_bytes();digest=hashlib.sha256(data).hexdigest();im=Image.open(file)
        shutil.copy2(file,originals/file.name)
        record=dict(id=i,file=file.name,width=im.width,height=im.height,sha256=digest)
        if digest in seen:record['duplicate_of']=seen[digest]
        if digest in old_hashes:record['terrain_pack_equivalent']=old_hashes[digest]
        seen.setdefault(digest,i);sources.append(record)
    for key,label,extent,source_id in city_gardens.NEW_ASSETS:
        record=sources[source_id-1];im=Image.open(originals/record['file']).convert('RGBA')
        pixels=np.asarray(im).copy();alpha=pixels[:,:,3].astype(float)
        # Remove the supplied export's diffuse halo while keeping edge coverage.
        pixels[:,:,3]=np.clip((alpha-160)*255/94,0,255).astype('uint8');im=Image.fromarray(pixels)
        solid=pixels[:,:,3]>=160;ys,xs=np.nonzero(solid)
        scans=[]
        for y in range(int(im.height*.43),int(im.height*.85)):
            line=np.flatnonzero(solid[y]);scans.append((int(line[-1]-line[0]) if len(line) else 0,y))
        width,pivot_y=max(scans);bottom=int(ys.max());pivot_x=(int(xs.min())+int(xs.max()))/2
        sx,sy=508/width,128/(bottom-pivot_y)
        transform=(1/sx,0,pivot_x-256/sx,0,1/sy,pivot_y-342/sy)
        exported=im.transform((512,512),Image.Transform.AFFINE,transform,Image.Resampling.BICUBIC)
        exported.save(runtime/(key+'.png'),optimize=True)
        entries.append(dict(id=key,label=label,size=extent,source_id=source_id,source_file=record['file'],
                            source_sha256=record['sha256'],runtime_sha256=hashlib.sha256((runtime/(key+'.png')).read_bytes()).hexdigest(),
                            source_pivot=[pivot_x,pivot_y],source_bottom=bottom,canvas=[512,512],pivot=[256,342],diamond=[508,256],
                            alpha_cleanup_floor=160,top_margin=round(342+(int(ys.min())-pivot_y)*sy,1)))
    catalog=[]
    for i,e in enumerate(entries):
        label=e['label'];catalog.append(dict(id=e['id'],label=label,size=e['size'],cells=e['size']**2,selected=e['id'] in city_gardens.SELECTED,
                                            reason='地面简洁，缩小后仍清晰' if e['size']==1 else '细节与主体较丰富，整片占四格'))
    (archive/'source-manifest.json').write_text(json.dumps(dict(source=str(source),count=len(sources),unique_count=len(seen),files=sources),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (archive/'catalog.json').write_text(json.dumps(dict(pack=city_gardens.PACK,assets=catalog),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    manifest=dict(pack=city_gardens.PACK,source_count=len(entries),original_terrain_count=32,additional_count=len(city_gardens.NEW_ASSETS),
                  original_archive='terrain-biomes-v3-20261026.zip',placement_version=7,legacy_assets=False,assets=entries)
    (runtime/'art-pack.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    font=ImageFont.truetype(r'C:\Windows\Fonts\msyh.ttc',16)
    sheet=Image.new('RGB',(1400,((len(entries)+6)//7)*210),(224,226,210));draw=ImageDraw.Draw(sheet)
    for i,e in enumerate(entries):
        image=Image.open(runtime/(e['id']+'.png'));image.thumbnail((192,176));x=i%7*200;y=i//7*210
        sheet.paste(image,(x+(200-image.width)//2,y),image)
        draw.text((x+5,y+176),e['label']+' · '+str(e['size']**2)+'格',font=font,fill=(20,35,25))
    sheet.save(archive/'runtime-catalog.jpg',quality=94)
    print(json.dumps(dict(archived=len(sources),unique=len(seen),runtime=len(entries),selected=len(city_gardens.SELECTED),clipped_peaks=[e['id'] for e in entries if e.get('top_margin',0)<-1]),ensure_ascii=False))


if __name__=='__main__':main()
