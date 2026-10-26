"""Original, deterministic miniature landscape art for the native city map.

No game screenshots or third-party game assets are sampled. Ground tiles and
depth-sorted scenery share a 256px canvas, with ground centre at (128, 171).
Run: python unity/bake_city.py
"""
import math
import io
import random
from pathlib import Path

import numpy as np
from scipy.ndimage import gaussian_filter
from PIL import Image, ImageDraw, ImageFilter
from bake_ui import write_texture_meta

OUT = Path(__file__).resolve().parent / 'JianghuUnity/Assets/Resources/City'
S = 2
INK = (45, 58, 49)


def save(image, path):
    """Replace whole city textures atomically, including during a full rebake."""
    path.parent.mkdir(parents=True, exist_ok=True)
    buffer = io.BytesIO()
    image.save(buffer, format='PNG', optimize=True)
    temporary = path.with_suffix('.png.tmp')
    temporary.write_bytes(buffer.getvalue())
    temporary.replace(path)
    write_texture_meta(path, wrap=False, mipmaps=False, compress=False)


class Art:
    def __init__(self, seed=0):
        self.im = Image.new('RGBA', (256*S, 256*S))
        self.d = ImageDraw.Draw(self.im)
        self.rng = random.Random(seed)

    def poly(self, pts, color, edge=None, width=1):
        pts = [(round(x*S), round(y*S)) for x, y in pts]
        self.d.polygon(pts, fill=color)
        if edge:
            self.d.line(pts+[pts[0]], fill=edge, width=width*S, joint='curve')

    def line(self, pts, color, width=1):
        self.d.line([(round(x*S), round(y*S)) for x, y in pts], fill=color, width=max(1, round(width*S)), joint='curve')

    def oval(self, box, color, edge=None):
        self.d.ellipse(tuple(round(x*S) for x in box), fill=color, outline=edge, width=S)

    def finish(self, name):
        im = self.im.resize((256, 256), Image.Resampling.LANCZOS)
        save(im, OUT / (name+'.png'))


def iso(u, v, z=0):
    return 128+(u-v)*.88, 171+(u+v)*.44-z


def patch(a, u, v, w, d, color, z=0, edge=None):
    a.poly([iso(u-w,v-d,z),iso(u+w,v-d,z),iso(u+w,v+d,z),iso(u-w,v+d,z)], color, edge)


def ground(kind, variant):
    a = Art(1200+variant*17+ord(kind))
    colors = {'.':(147,158,119), '+':(147,158,119), '~':(66,118,119),
              '^':(126,146,117),'t':(122,147,106),',':(164,159,103),'#':(144,151,125)}
    base = colors[kind]
    # A continuous landscape: exact touching diamonds, no dark extrusion on each cell.
    mask=Image.new('L',a.im.size);md=ImageDraw.Draw(mask)
    md.polygon([(128*S,107*S),(256*S,171*S),(128*S,235*S),(0,171*S)],fill=255)
    rng=np.random.default_rng(1200+variant*17+ord(kind))
    cloud=gaussian_filter(rng.normal(size=(256*S,256*S)),14*S)
    cloud=cloud/max(float(cloud.std()),.0001)
    grain=rng.normal(0,1.4,size=cloud.shape)
    rgb=np.clip(np.array(base)[None,None,:]+(cloud*3+grain)[...,None],0,255).astype('uint8')
    a.im=Image.fromarray(rgb).convert('RGBA');a.im.putalpha(mask);a.d=ImageDraw.Draw(a.im)
    for _ in range(45):
        u,v = a.rng.uniform(-66,66),a.rng.uniform(-66,66)
        x,y=iso(u,v)
        c=tuple(max(0,min(255,k+a.rng.randint(-8,6))) for k in base)
        a.line([(x,y),(x+a.rng.uniform(2,7),y-1)], c, .5)
    if kind=='~':
        for _ in range(24):
            x,y=iso(a.rng.uniform(-58,58),a.rng.uniform(-58,58))
            a.line([(x-6,y),(x,y+1),(x+7,y-1)], (153,191,171,140), 1)
        if variant%2==0:
            for u,v in [(-26,29),(-15,34),(20,-18)]:
                x,y=iso(u,v);a.oval((x-4,y-2,x+4,y+2),(102,157,100));a.line([(x,y),(x+3,y-1)],(190,197,132))
    elif kind==',':
        patch(a,0,0,51,51,(124,135,71),edge=(180,180,111))
        for v in range(-44,48,12):
            a.line([iso(-48,v),iso(48,v)],(202,188,108),3)
            a.line([iso(-48,v+3),iso(48,v+3)],(86,117,65),2)
            for u in range(-44,48,12):
                x,y=iso(u,v);a.line([(x,y),(x,y-5)],(207,191,104))
    elif kind not in '+#^':
        for _ in range(6):
            x,y=iso(a.rng.uniform(-58,58),a.rng.uniform(-58,58))
            a.line([(x-2,y),(x-3,y-3),(x,y),(x+2,y-4)],(111,136,89))
    a.finish({'~':'water','^':'mountain','t':'forest',',':'field','#':'wall','+':'street','.':'grass'}[kind]+f'-{variant}')


def road(mask, bridge=False):
    a=Art(mask+700)
    ends=[(192,139),(192,203),(64,203),(64,139)]
    for bit,end in enumerate(ends):
        if not(mask & (1<<bit)): continue
        a.line([(128,171),end],(130,139,105),33)
        a.line([(128,171),end],(183,177,143) if not bridge else (155,126,84),26)
        dx,dy=end[0]-128,end[1]-171
        for i in range(1,7):
            t=i/8;x=128+dx*t;y=171+dy*t
            a.line([(x-dy*.17,y+dx*.17),(x+dy*.17,y-dx*.17)],(158,157,125) if not bridge else (85,72,54),.6)
        if bridge:
            for sign in (-1,1):
                q=(sign*-dy*.25,sign*dx*.25)
                a.line([(128+q[0],171+q[1]-7),(end[0]+q[0],end[1]+q[1]-7)],(211,193,148),2)
                for t in (.25,.65,1):
                    x=128+dx*t+q[0];y=171+dy*t+q[1]
                    a.line([(x,y),(x,y-12)],(92,83,60),2)
    a.finish(('bridge-' if bridge else 'road-')+str(mask))


def tree(a,u,v,size=1,pine=False):
    x,y=iso(u,v)
    a.oval((x-15*size,y-5*size,x+20*size,y+6*size),(39,68,47,65))
    a.line([(x,y),(x-2*size,y-36*size)],(74,76,49),3*size)
    if pine:
        for i in range(3):
            h=23+i*10;a.poly([(x,y-(h+19)*size),(x-(19-i*3)*size,y-h*size),(x+(19-i*3)*size,y-(h-2)*size)],(64+i*8,102+i*8,72+i*6),(49,77,56))
    else:
        for i in range(10):
            ang=i*2.4;rx=math.cos(ang)*12*size;ry=math.sin(ang)*8*size
            c=(76+i%3*12,119+i%4*9,75+i%3*9)
            a.oval((x+rx-12*size,y+ry-49*size,x+rx+13*size,y+ry-27*size),c,(59,91,58))
            a.line([(x+rx-6*size,y+ry-42*size),(x+rx,y+ry-44*size)],(155,166,102,150))


def mountain(a,u,v,size=1):
    x,y=iso(u,v)
    a.poly([(x-49*size,y+3),(x-26*size,y-38*size),(x-14*size,y-78*size),(x+4*size,y-88*size),(x+21*size,y-40*size),(x+48*size,y+5),(x+9,y+17)],(64,99,89),INK)
    a.poly([(x-14*size,y-78*size),(x+4*size,y-88*size),(x+7*size,y-26*size),(x-1,y+9),(x-26*size,y+13)],(115,145,121))
    a.poly([(x+4*size,y-88*size),(x+21*size,y-40*size),(x+48*size,y+5),(x+12,y+11),(x+7*size,y-26*size)],(46,78,76))
    a.line([(x-10*size,y-70*size),(x-13*size,y-45*size),(x-22*size,y-29*size)],(168,181,142),2)
    a.line([(x+10*size,y-55*size),(x+15*size,y-24*size),(x+30*size,y-5*size)],(91,122,103),2)
    for _ in range(8):
        xx=x+a.rng.uniform(-35,38)*size;yy=y+a.rng.uniform(-2,10)
        a.oval((xx-6,yy-3,xx+7,yy+3),(91,117,76))


def house(a,u=0,v=0,w=35,d=26,h=38,roof=(64,88,82),walls=(211,197,152)):
    patch(a,u+5,v+5,w+9,d+9,(38,52,40,75),edge=None)
    a.poly([iso(u-w,v+d),iso(u+w,v+d),iso(u+w,v+d,h),iso(u-w,v+d,h)],walls,INK)
    a.poly([iso(u+w,v-d),iso(u+w,v+d),iso(u+w,v+d,h),iso(u+w,v-d,h)],tuple(int(k*.72) for k in walls),INK)
    for i in (-.7,0,.7):
        a.line([iso(u+w*i,v+d,1),iso(u+w*i,v+d,h)],(103,85,57),2)
    for du in (-w*.48,w*.48):
        a.poly([iso(u+du-5,v+d,h*.42),iso(u+du+5,v+d,h*.42),iso(u+du+5,v+d,h*.72),iso(u+du-5,v+d,h*.72)],(77,89,66),INK)
        a.line([iso(u+du,v+d,h*.43),iso(u+du,v+d,h*.72)],(186,163,102))
    a.poly([iso(u-5,v+d,0),iso(u+5,v+d,0),iso(u+5,v+d,h*.65),iso(u-5,v+d,h*.65)],(77,67,45),INK)
    # Hipped roofs with raised ridges, extended eaves and fine tile courses.
    ridgeA=iso(u-w*.65,v,h+22);ridgeB=iso(u+w*.65,v,h+22)
    e=[iso(u-w-9,v-d-9,h+3),iso(u+w+9,v-d-9,h+3),iso(u+w+9,v+d+9,h+3),iso(u-w-9,v+d+9,h+3)]
    a.poly([e[3],e[2],ridgeB,ridgeA],roof,INK,2)
    a.poly([e[2],e[1],ridgeB],tuple(max(0,k-18) for k in roof),INK)
    a.poly([e[0],e[1],ridgeB,ridgeA],tuple(min(255,k+15) for k in roof),INK)
    for t in (.2,.4,.6,.8):
        mix=lambda p,q:(p[0]*(1-t)+q[0]*t,p[1]*(1-t)+q[1]*t)
        a.line([mix(e[3],ridgeA),mix(e[2],ridgeB)],tuple(min(255,k+28) for k in roof))
    for i in range(11):
        t=i/10;a.line([(e[3][0]*(1-t)+e[2][0]*t,e[3][1]*(1-t)+e[2][1]*t),(ridgeA[0]*(1-t)+ridgeB[0]*t,ridgeA[1]*(1-t)+ridgeB[1]*t)],(*INK,80),.5)
    a.line([e[3],e[2]],(168,156,112),2)
    a.line([ridgeA,ridgeB],(191,178,129),2)
    for p in e:
        a.line([(p[0]-4,p[1]-4),p,(p[0]+4,p[1]-3)],(90,103,81),2)


def building(kind):
    a=Art(sum(map(ord,kind)))
    patch(a,0,0,52,48,(178,169,124),edge=(104,113,82))
    for i in (-30,0,30): a.line([iso(-48,i),iso(48,i)],(151,149,111))
    if kind in ('government','palace','temple','faction','school'):
        roofs={'government':(131,68,51),'palace':(156,132,64),'temple':(157,134,69),'faction':(69,102,95),'school':(74,99,91)}
        house(a,0,-17,40,24,42,roofs[kind])
        if kind in ('palace','temple'):
            house(a,0,-17,29,17,79,roofs[kind])
        for u in (-42,42): house(a,u,28,12,18,22,roofs[kind])
        if kind=='faction':
            p=iso(42,-35);a.line([p,(p[0],p[1]-76)],(189,168,113),2);a.poly([(p[0],p[1]-74),(p[0]+25,p[1]-68),(p[0]+20,p[1]-43),(p[0],p[1]-48)],(151,65,44),INK)
        if kind=='temple':
            x,y=iso(0,31);a.oval((x-10,y-6,x+10,y+2),(90,94,69));a.line([(x,y-6),(x,y-22)],(182,164,119),3)
    elif kind=='gate':
        for u in (-42,42): house(a,u,0,17,27,35,(76,92,85),(158,155,123))
        # Open gate between two watchtowers.
        a.line([iso(-24,0,29),iso(24,0,29)],(167,155,116),12)
        house(a,0,0,35,19,64,(83,106,91))
        a.poly([iso(-14,22),iso(14,22),iso(14,22,33),iso(-14,22,33)],(55,67,49),INK)
        a.line([iso(-12,22,2),iso(-12,22,30),iso(12,22,30),iso(12,22,2)],(180,161,113),2)
    elif kind=='market':
        for u,v,col in [(-29,-15,(174,94,61)),(24,-24,(182,160,87)),(0,28,(103,137,111))]:
            for du in (-18,18):a.line([iso(u+du,v+12),iso(u+du,v+12,30)],(94,83,53),2)
            patch(a,u,v,22,15,col,z=32,edge=INK);patch(a,u,v+9,17,5,(155,132,80),z=12,edge=INK)
            for i in (-10,0,10):
                x,y=iso(u+i,v+9,16);a.oval((x-4,y-3,x+4,y+2),(198,166,80))
    elif kind=='training':
        patch(a,0,0,43,39,(189,170,111),edge=(109,111,77))
        x,y=iso(0,0);a.oval((x-24,y-13,x+24,y+13),(173,153,93),(211,195,133))
        for u in (-35,35):
            a.line([iso(u,-27),iso(u,-27,37)],(101,80,46),3)
            a.line([iso(u-11,-27,24),iso(u+11,-27,24)],(120,97,56),3)
            for du in (-7,0,7):a.line([iso(u+du,-27,3),iso(u+du,-27,44)],(64,80,69),2)
    elif kind=='garden':
        tree(a,-30,-12,.9);tree(a,30,-28,.7,True)
        house(a,0,25,18,18,27,(93,122,97));mountain(a,37,18,.32)
    elif kind=='dock':
        patch(a,0,17,47,24,(128,107,70),edge=INK)
        for u in range(-42,46,9):a.line([iso(u,-5),iso(u,41)],(184,157,103),2)
        house(a,-20,-25,24,17,30,(87,109,96))
        a.poly([iso(34,22),iso(59,28),iso(72,10),iso(50,7)],(120,81,45),INK)
        a.line([iso(54,16),iso(54,16,47)],(205,185,131),2)
        a.poly([iso(54,16,47),iso(54,16,11),iso(74,16,16)],(217,209,164),INK)
    elif kind=='granary':
        for u,v in [(-24,-9),(22,-12)]:
            house(a,u,v,20,23,36,(147,133,79),(191,173,109))
        for u in (-30,-14,9,25):
            x,y=iso(u,32);a.oval((x-7,y-11,x+7,y+2),(193,173,109),(125,114,72))
    else:
        h=59 if kind=='inn' else 37
        roof=(116,81,59) if kind=='inn' else (69,100,93)
        house(a,0,-8,38,27,h,roof)
        if kind=='residence':house(a,-35,26,19,17,24,(105,118,89))
        if kind=='clinic':
            for u in (-25,-7,11,29):
                x,y=iso(u,34);a.oval((x-5,y-4,x+5,y+2),(125,105,63));a.line([(x,y-3),(x,y-13)],(86,129,71),3)
        if kind in ('inn','clinic','workshop'):
            p=iso(35,23,35);a.line([p,(p[0]+24,p[1])],(182,155,99),2);a.poly([(p[0]+13,p[1]),(p[0]+28,p[1]),(p[0]+26,p[1]+23),(p[0]+13,p[1]+23)],(159,64,44),INK)
        if kind=='workshop':
            for u,v in [(26,35),(40,28)]:
                x,y=iso(u,v);a.oval((x-9,y-5,x+9,y+5),(113,104,74),INK);a.line([(x-8,y),(x+8,y)],(179,156,104),2)
    a.finish('building-'+kind)


def scenery(kind, variant):
    a=Art(820+variant)
    if kind=='forest':
        for u,v,s in [(-27,-24,.8),(23,-28,.7),(-17,25,.8),(32,18,.65)]:tree(a,u,v,s,variant%2==0)
    elif kind=='mountain':
        mountain(a,13,-19,.83);mountain(a,-24,28,.68);tree(a,45,26,.48,True)
    elif kind=='wall':
        house(a,0,0,54,12,30,(102,115,96),(151,155,128))
        for u in range(-48,54,17):patch(a,u,0,6,13,(173,175,143),z=43,edge=INK)
    elif kind=='grass':
        tree(a,30,-12,.52,variant%2==0)
        for u,v in [(-34,27),(-25,29)]:
            x,y=iso(u,v);a.oval((x-5,y-3,x+6,y+2),(157,161,121),INK)
    a.finish('prop-'+kind+f'-{variant}')


def backdrop():
    rng_np=np.random.default_rng(243)
    yy,xx=np.mgrid[:960,:1680]
    light=np.exp(-((xx-810)/900)**2-((yy-410)/520)**2)
    grain=rng_np.normal(0,.75,(960,1680))
    rgb=np.stack([28+light*29+grain,46+light*30+grain,43+light*22+grain],axis=-1)
    im=Image.fromarray(np.clip(rgb,0,255).astype('uint8'));d=ImageDraw.Draw(im)
    rng=random.Random(243)
    for layer in range(4):
        pts=[(-100,960)]
        for x in range(-100,1800,38):
            y=280+layer*155+math.sin(x*.007+layer)*65+rng.uniform(-50,30)
            pts.append((x,y))
        pts.append((1800,960));d.polygon(pts,fill=(34+layer*3,53+layer*4,49+layer*3))
    im=im.filter(ImageFilter.GaussianBlur(14))
    save(im,OUT/'backdrop.png')


def overlays():
    a=Art();a.poly([(128,108),(254,171),(128,234),(2,171)],(173,209,121,45));a.finish('reachable')
    a=Art();a.oval((91,153,165,189),(255,238,172));a.oval((105,160,151,182),(255,250,208));a.finish('route-dot')
    for name,col in [('selection',(235,198,103)),('hero-ring',(250,219,140))]:
        a=Art();a.oval((18,119,238,223),(*col,32));a.oval((26,123,230,219),(0,0,0,0),(*col,210));
        a.line([(128,118),(139,124),(128,130),(117,124),(128,118)],(*col,230),2)
        a.line([(128,212),(139,218),(128,224),(117,218),(128,212)],(*col,230),2)
        a.finish(name)
    shadow=Image.new('RGBA',(1024,560));d=ImageDraw.Draw(shadow)
    d.polygon([(512,18),(1006,265),(512,518),(18,265)],fill=(7,20,17,160))
    shadow=shadow.filter(ImageFilter.GaussianBlur(15));save(shadow,OUT/'island-shadow.png')


def main():
    for kind in '.+~^t,#':
        for v in range(3):ground(kind,v)
    for mask in range(16):road(mask);road(mask,True)
    for kind in ('grass','forest','mountain','wall'):
        for v in range(3):scenery(kind,v)
    for kind in ('gate','government','palace','market','inn','clinic','workshop','training','school','residence','granary','garden','temple','dock','faction'):building(kind)
    backdrop()
    overlays()
    # Generated production painting is kept as source art, with deterministic crop/anchor export.
    from export_city_art import export
    export()


if __name__=='__main__':main()
