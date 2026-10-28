"""Bake authored images into fixed UV slots and a reusable weighted mesh template."""
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'art/puppet-modular'
OUT = ROOT / 'unity/JianghuUnity/Assets/Resources/ModularPuppet'


def bake():
    OUT.mkdir(parents=True, exist_ok=True)
    original = ROOT/'unity/JianghuUnity/Assets/Resources/PuppetTrial'
    index = json.loads((original/'index.json').read_text(encoding='utf-8'))
    # Source painting stays unchanged. Size normalization is a texture-import
    # operation; every outfit uses exactly the same UV and mesh definitions.
    Image.open(original/'atlas.png').save(OUT/'cloud.png')
    Image.open(ART/'jade-source.png').resize((1024,1024),Image.Resampling.LANCZOS).save(OUT/'jade.png')
    feature = Image.open(ART/'features-source.png').convert('RGBA')
    feature.save(OUT/'features.png')
    faces = {}
    for row, kind in enumerate(('eyes','brows','lips')):
        faces[kind] = []
        for col in range(3):
            box = (round(col*feature.width/3),round(row*feature.height/3),
                   round((col+1)*feature.width/3),round((row+1)*feature.height/3))
            crop = feature.crop(box)
            bound = crop.getchannel('A').point(lambda a:255 if a>70 else 0).getbbox()
            assert bound is not None
            x0,y0,x1,y1 = bound
            x0=max(0,x0-3)+box[0];x1=min(crop.width,x1+3)+box[0]
            y0=max(0,y0-3)+box[1];y1=min(crop.height,y1+3)+box[1]
            faces[kind].append([x0/feature.width,1-y1/feature.height,
                                (x1-x0)/feature.width,(y1-y0)/feature.height])

    mask=Image.new('RGBA',(1024,1024),'white');md=ImageDraw.Draw(mask)
    meshes=[];svg=['<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">',
                   '<rect width="1024" height="1024" fill="#f7f2e7"/>']
    for p in index['parts']:
        p=dict(p)
        # The neck root belongs beneath the coat collar. The face sits above
        # the collar in space; its separate feature meshes keep their layers.
        if p['id']=='head-neck':p['layer']=35
        nx,ny = (10,12) if p['id']=='pelvis' else (6,8) if p['id']=='torso' else (10,14) if p['id']=='head-neck' else (1,1)
        vertices=[];uv=[];weights=[];tris=[]
        for y in range(ny+1):
            for x in range(nx+1):
                u,v=x/nx,y/ny
                vertices.append([u*p['size'][0],v*p['size'][1]])
                uv.append([p['uv'][0]+u*p['uv'][2],p['uv'][1]+(1-v)*p['uv'][3]])
                if p['id']=='pelvis':
                    t=max(0,min(1,(v*p['size'][1]-55)/(p['size'][1]-55)))
                    t=t*t*(3-2*t)*.72
                    weights.append([1-t,t*(1-u),t*u])
                elif p['id']=='torso':
                    h=1-v
                    weights.append([max(0,1-2*h),1-abs(2*h-1),max(0,2*h-1)])
                elif p['id']=='head-neck':
                    px,py=u*p['size'][0],v*p['size'][1]
                    clamp=lambda t:max(0,min(1,t))
                    # Face and hair stay rigid. Only the skin below the jaw
                    # transitions onto neck/chest, avoiding an exposed gap.
                    t=clamp((py-210)/65)*clamp((px-145)/22)*clamp((235-px)/22)
                    weights.append([1-t,t*.25,t*.75])
                else: weights.append([1,0,0])
        for y in range(ny):
            for x in range(nx):
                i=y*(nx+1)+x;tris.extend([i,i+1,i+nx+1,i+1,i+nx+2,i+nx+1])
        assert all(abs(sum(w)-1)<1e-6 for w in weights)
        meshes.append(dict(part=p,vertices=vertices,uv=uv,weights=weights,triangles=tris,
                           bones=['pelvis','thighFar','thighNear'] if p['id']=='pelvis' else
                                 ['pelvis','belly','torso'] if p['id']=='torso' else
                                 ['head','neck','torso'] if p['id']=='head-neck' else [p['bone']]))
        ax,ay=round(p['uv'][0]*1024),round((1-p['uv'][1]-p['uv'][3])*1024)
        for left,top,right,bottom in p.get('socket_masks',[]):
            r=p['runtime_rect']
            left,top=max(left,r[0]),max(top,r[1]);right,bottom=min(right,r[2]),min(bottom,r[3])
            if right>left and bottom>top:md.rectangle((ax+left-r[0],ay+top-r[1],ax+right-r[0]-1,ay+bottom-r[1]-1),fill=(0,0,0,0))
        svg.append(f'<g stroke="#397b77" fill="none"><rect x="{ax}" y="{ay}" width="{p["size"][0]}" height="{p["size"][1]}"/>')
        for tri in zip(*[iter(tris)]*3):
            points=' '.join(f'{ax+vertices[i][0]:.1f},{ay+vertices[i][1]:.1f}' for i in tri)
            svg.append(f'<polygon points="{points}" stroke-width="0.4"/>')
        svg.append('</g>')
        svg.append(f'<text x="{ax+4}" y="{ay+17}" font-size="12" fill="#873d30">{p["id"]}</text>')
        for point in [p['pivot'],p['tip']]:svg.append(f'<circle cx="{ax+point[0]}" cy="{ay+point[1]}" r="3" fill="#d97442"/>')
    svg.append('</svg>');(ART/'uv-template.svg').write_text('\n'.join(svg),encoding='utf-8')
    mask.save(OUT/'socket-mask.png')
    manifest=dict(version=1,profile=index['profile'],atlas_size=[1024,1024],
                  outfits=[dict(id='cloud',name='丹青云纹'),dict(id='jade',name='素锦青竹')],
                  head_texture='jade',features=faces,meshes=meshes,
                  facial_rects=dict(eyes=[220,143,37,15],brows=[218,133,35,7],lips=[248,173,11,5]),
                  source_hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in ART.glob('*-source.png')})
    (OUT/'index.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'Baked 2 fixed-UV outfits, 9 independent features, {len(meshes)} meshes; '
          f'{sum(len(m["vertices"]) for m in meshes)} vertices')


if __name__=='__main__':bake()
