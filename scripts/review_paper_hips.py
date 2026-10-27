"""Compare the repaired hip silhouette using actual native Unity screenshots."""
import argparse,json
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[1]

def main():
    parser=argparse.ArgumentParser();parser.add_argument('output',type=Path);parser.add_argument('--native',type=Path);args=parser.parse_args();out=args.output
    native=args.native or out/'native';before=ROOT/'output/rig-rewrite/native-final'
    index=json.loads((out/'prepared/PaperCast/index.json').read_text(encoding='utf-8'))
    old=json.loads((out/'baseline-index.json').read_text(encoding='utf-8'))
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',21)
    def crop(image,view,skin):
        fit=min(view['width']/620,view['height']/700)
        x=view['x']+view['width']/2+(skin['canvas_origin'][0]-310)*fit
        return image.crop((round(x-190*fit),round(view['y']+12),round(x+190*fit),round(view['y']+view['height']-6)))
    def paste(canvas,image,cell):
        x,y,w,h=cell;image.thumbnail((w,h),Image.Resampling.LANCZOS);canvas.paste(image,(round(x+(w-image.width)/2),round(y+h-image.height)))
    for batch in range(3):
        canvas=Image.new('RGB',(1920,760),'#eae4d9');draw=ImageDraw.Draw(canvas)
        for i,(cid,skin) in enumerate(list(index['skins'].items())[batch*8:batch*8+8]):
            nv=json.loads((native/f'comparison-{cid}.json').read_text());ov=json.loads((before/f'comparison-{cid}.json').read_text())
            new=Image.open(native/f'comparison-{cid}.png');previous=Image.open(before/f'comparison-{cid}.png')
            images=[crop(new,nv['reference'],skin),crop(previous,ov['rig'],old['skins'][cid]),crop(new,nv['rig'],skin)]
            x,y=(i%4)*480,(i//4)*380;draw.text((x+8,y+4),skin['description'].split('，')[0],font=font,fill='#233638')
            for col,image in enumerate(images):
                draw.text((x+col*160+12,y+35),['成品图','修复前','修复后'][col],font=font,fill='#233638');paste(canvas,image,(x+col*160,y+65,158,300))
        canvas.save(out/f'native-before-after-{batch+1}.jpg',quality=95)
    ids=['m-youth-farmer','m-middle-farmer','m-child','m-escort','f-escort','m-fisher','m-hunter','m-artisan','m-beggar']
    for batch in range(3):
        chosen=ids[batch*3:batch*3+3];canvas=Image.new('RGB',(1600,410*len(chosen)),'#eae4d9');draw=ImageDraw.Draw(canvas)
        for row,cid in enumerate(chosen):
            view=json.loads((native/'hip-phases'/f'{cid}.json').read_text())['rig']
            for i in range(8):
                image=crop(Image.open(native/'hip-phases'/f'{cid}-{i}.png'),view,index['skins'][cid]);paste(canvas,image,(i*200,row*410+25,198,380))
            draw.text((8,row*410+2),index['skins'][cid]['description'].split('，')[0],font=font,fill='#233638')
        canvas.save(out/f'native-walk-phases-{batch+1}.jpg',quality=95)
    nv=json.loads((native/'frame-viewports.json').read_text());ov=json.loads((before/'frame-viewports.json').read_text());cid='m-youth-farmer';frames=[]
    for i in range(24):
        image=Image.open(native/'frames'/f'{i:04}.png');previous=Image.open(before/'frames'/f'{i:04}.png')
        images=[crop(image,nv['reference'],index['skins'][cid]),crop(previous,ov['rig'],old['skins'][cid]),crop(image,nv['rig'],index['skins'][cid])]
        frame=Image.new('RGB',(900,500),'#eae4d9');draw=ImageDraw.Draw(frame)
        for col,part in enumerate(images):
            draw.text((col*300+95,4),['成品图','修复前','修复后'][col],font=font,fill='#233638');paste(frame,part,(col*300,35,298,460))
        frames.append(frame.quantize(colors=192))
    frames[0].save(out/'hip-walk-before-after.gif',save_all=True,append_images=frames[1:],duration=50,loop=0,optimize=True)
    print('Saved native before/after sheets, 72 walk phase comparisons and GIF:',out)

if __name__=='__main__':main()
