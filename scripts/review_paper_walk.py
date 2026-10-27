"""Audit real split-panel sockets and compare Unity samples with offline assembly.

Run after bake_paper_cast.py and the isolated native PaperRenderSmoke pass.
Produces contact sheets from actual Unity screenshots, not generated mockups.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'unity'))
from bake_split_cast import pose, rotation


def matrix(skin, name, part, rig, joints, angles):
    pivot = np.array(part['pivot'])
    if 'bone' in part:
        start, end = rig['bones'][part['bone']]
        a, b = joints[start], joints[end]
        if part.get('freeze_to_body'):
            delta=np.array(skin['bind_joints'][end])-np.array(skin['bind_joints'][start])
            b=a+rotation(angles['body']-skin['bind_lean'])@delta
        local = np.array(part['tip']) - pivot
        e = local / np.linalg.norm(local)
        scale = np.linalg.norm(b-a) / np.linalg.norm(local)
        angle = math.degrees(math.atan2(*(b-a)[::-1])-math.atan2(*local[::-1]))
    else:
        a = joints[part['joint']]
        e = np.array([0., -1.])
        scale = part['height'] / (pivot[1] if name == 'head-neck' else part['size'][1])
        angle = part['angle'] + angles[part['follow']]
    q = np.array([e[1], -e[0]])
    linear = rotation(angle) @ (np.outer(e, e)+part.get('breadth', 1)*np.outer(q, q))*scale
    return linear, a-linear @ pivot


def connections():
    yield 'neck', 'torso', 'head-neck'
    yield 'hip', 'torso', 'pelvis'
    for side, prefix in [('Near', 'near'), ('Far', 'far')]:
        for joint, parent, child in [
            ('shoulder', 'torso', 'upper-arm'), ('elbow', 'upper-arm', 'forearm'),
            ('wrist', 'forearm', 'hand'), ('hip', 'pelvis', 'thigh'),
            ('knee', 'thigh', 'shin'), ('ankle', 'shin', 'foot'),
        ]:
            yield joint+side, parent if parent in ('torso', 'pelvis') else prefix+'-'+parent, prefix+'-'+child


def audit(index):
    checks = []
    for cid, skin in index['skins'].items():
        masks = {name: Image.open(ROOT/'art/puppet-modern-24'/p['source']).getchannel('A') for name, p in skin['parts'].items()}
        for phase in [None] + [x/12 for x in range(12)]:
            joints, angles = pose(skin, phase or 0, phase is not None)
            matrices = {n: matrix(skin, n, p, index['rig'], joints, angles) for n, p in skin['parts'].items()}
            for joint, parent, child in connections():
                # Inspect actual transformed alpha within 12 world units of the
                # hinge. Bone endpoint equality alone cannot catch art gaps.
                center = joints[joint];step = .5;radius = 12;size = 49
                start = center-radius;visible = []
                yy, xx = np.mgrid[:size, :size]
                disk = (xx-24)**2+(yy-24)**2 <= 24**2
                for name in (parent, child):
                    linear, offset = matrices[name];inverse = np.linalg.inv(linear)
                    affine = np.column_stack([inverse*step, inverse@(start-offset)])
                    mask = masks[name].transform((size, size), Image.Transform.AFFINE, tuple(affine.flatten()), Image.Resampling.BILINEAR)
                    visible.append((np.asarray(mask)>100)&disk)
                a, b = visible
                gap = float(ndimage.distance_transform_edt(~a)[b].min()*step) if a.any() and b.any() else None
                checks.append(dict(skin=cid, phase='bind' if phase is None else phase, joint=joint,
                                   gap_world_units=gap, overlap_pixels=int((a&b).sum())))
    return checks


def contact_sheets(native, output, index):
    font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 22)
    files = []
    for batch in range(3):
        sheet = Image.new('RGB', (1920, 650), '#eae4d9');draw = ImageDraw.Draw(sheet)
        for i, (cid, skin) in enumerate(list(index['skins'].items())[batch*8:batch*8+8]):
            viewport = json.loads((native/f'comparison-{cid}.json').read_text())
            screenshot = Image.open(native/f'comparison-{cid}.png')
            a, b = viewport['reference'], viewport['rig']
            crop = screenshot.crop((round(a['x']), round(a['y']), round(b['x']+b['width']), round(b['y']+b['height'])))
            crop.thumbnail((474, 510), Image.Resampling.LANCZOS)
            x, y = (i%4)*480, (i//4)*325
            sheet.paste(crop, (x+(480-crop.width)//2, y+48))
            draw.text((x+12, y+8), skin['description'].split('，')[0]+'：成品图 / 绑定', font=font, fill='#243c3c')
        path = output/f'comparison-{batch+1}.jpg';sheet.save(path, quality=95);files.append(str(path.relative_to(ROOT)))
    return files


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--resources', type=Path, default=ROOT/'unity/JianghuUnity/Assets/Resources')
    parser.add_argument('--native', type=Path, default=ROOT/'output/rig-rewrite/native-final')
    parser.add_argument('--output', type=Path, default=ROOT/'output/rig-rewrite')
    args = parser.parse_args();output=args.output.resolve();output.mkdir(parents=True,exist_ok=True)
    index = json.loads((args.resources/'PaperCast/index.json').read_text(encoding='utf-8'))
    checks = audit(index)
    (output/'socket-contact-audit.json').write_text(json.dumps(checks, indent=2)+'\n')
    gaps = [c for c in checks if c['gap_world_units'] is None or c['gap_world_units']>2]
    report = dict(socket_cases=len(checks), contacts_with_gap_above_2_world_units=gaps)
    if (args.native/'render-regression.json').exists():
        native=json.loads((args.native/'render-regression.json').read_text())
        error=0;count=0
        for sample in native['checks'][0]['facts']['sampled_joints']:
            expected,_=pose(index['skins'][sample['skin']],sample['phase'],sample['pose']=='walk')
            for joint,value in expected.items():
                error=max(error,float(np.linalg.norm(value-np.array(sample['joints'][joint]))));count+=1
        report.update(native_passed=native['passed'],offline_native_joint_samples=count,max_offline_native_joint_error=error,
                      comparison_sheets=contact_sheets(args.native,output,index))
    (output/'review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('contacts_with_gap_above_2_world_units',)},ensure_ascii=False))
    print('Socket gaps requiring visual review:',len(gaps))
    for row in gaps[:20]:print(row)
    if gaps or report.get('native_passed') is False or report.get('max_offline_native_joint_error',0)>.002:raise SystemExit(1)


if __name__=='__main__':main()
