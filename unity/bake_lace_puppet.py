"""Extract the designated split artwork, preserve RGBA, and pack its trial atlas."""
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'art/puppet-lace-blue'
OUTPUT = ROOT / 'unity/JianghuUnity/Assets/Resources/PuppetTrial'


def bake():
    data = json.loads((ART/'calibration.json').read_text(encoding='utf-8'))
    source = ART/data['source']
    sheet = Image.open(source).convert('RGBA')
    assert list(sheet.size) == data['source_size']
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (ART/'parts').mkdir(exist_ok=True)
    atlas = Image.new('RGBA', (1024,1024))
    x, y, row_height = 8, 8, 0
    result = []
    for p in data['parts']:
        box = p['rect']
        full = sheet.crop(box)
        full.save(ART/'parts'/f'{p["id"]}.png')
        # The design sheet repeats its lower robe in torso and pelvis. Keep the
        # complete original above, but only the upper coat is bound to the spine.
        actual_box = list(box)
        actual_box[3] = p.get('runtime_bottom', actual_box[3])
        image = sheet.crop(actual_box)
        # Socket tabs are drawing-board helpers, not anatomy. Preserve them in
        # the complete originals, and mask only their authored rectangles here.
        alpha=image.getchannel('A')
        mask=ImageDraw.Draw(alpha)
        for left,top,right,bottom in p.get('socket_masks',[]):
            left,top=max(left,actual_box[0]),max(top,actual_box[1])
            right,bottom=min(right,actual_box[2]),min(bottom,actual_box[3])
            if right>left and bottom>top:
                mask.rectangle((left-actual_box[0],top-actual_box[1],right-actual_box[0]-1,bottom-actual_box[1]-1),fill=0)
        image.putalpha(alpha)
        if x+image.width+8 > atlas.width:
            x, y, row_height = 8, y+row_height+8, 0
        assert y+image.height+8 <= atlas.height
        atlas.paste(image,(x,y))  # no mask/blend: source RGBA bytes stay unchanged
        local = lambda point: [point[0]-actual_box[0],point[1]-actual_box[1]]
        part = {**p, 'source_rect':box, 'runtime_rect':actual_box,
                'source_region':'right-hand detached component artwork',
                'pivot':local(p['a']), 'tip':local(p['b']),
                'size':list(image.size), 'uv':[x/1024,1-(y+image.height)/1024,image.width/1024,image.height/1024],
                'rgba_sha256':hashlib.sha256(image.tobytes()).hexdigest()}
        if 'heel' in p:
            part['heel_pivot'] = local(p['heel'])
        assert all(0 <= v < limit for v,limit in zip(part['pivot'],image.size))
        assert all(0 <= v < limit for v,limit in zip(part['tip'],image.size))
        result.append(part)
        row_height=max(row_height,image.height)
        x+=image.width+8
    atlas.save(OUTPUT/'atlas.png')
    index=dict(version=1,profile=data['profile'],binding_basis=data['binding_basis'],
               source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
               atlas_sha256=hashlib.sha256((OUTPUT/'atlas.png').read_bytes()).hexdigest(),
               atlas_size=list(atlas.size),skin_color=data['skin_color'],parts=result)
    (OUTPUT/'index.json').write_text(json.dumps(index,ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'Packed {len(result)} original RGBA components into {atlas.width}x{atlas.height}')


if __name__ == '__main__':
    bake()
