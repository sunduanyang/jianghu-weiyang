"""Verify the delivered crops against the user's immutable source archive."""
import hashlib,json,zipfile
from pathlib import Path
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/cast24-binding-20261029'
RES=ROOT/'unity/JianghuUnity/Assets/Resources/Characters/Cast24'

def main():
    m=json.loads((OUT/'binding-manifest.json').read_text(encoding='utf-8'))
    archive=Path(m['archive']);failures=[];rows=[]
    assert hashlib.sha256(archive.read_bytes()).hexdigest()==m['archive_sha256']
    with zipfile.ZipFile(archive) as z:
        source_manifest=json.loads(z.read('prompts-and-manifest.json'))
        for character in m['characters']:
            role=character['id'];src=next(c for c in source_manifest['characters'] if c['id']==role)
            source_data=z.read(src['file']);source=np.array(Image.open(z.open(src['file'])).convert('RGBA'))
            definition=json.loads((RES/role/'index.json').read_text(encoding='utf-8'))
            assert hashlib.sha256(source_data).hexdigest()==character['source_sha256'].lower()
            assert len(character['parts'])==17 and len(definition['body_meshes'])==16
            for part in character['parts']:
                full=np.array(Image.open(ROOT/part['export']).convert('RGBA'));runtime=np.array(Image.open(ROOT/part['runtime']).convert('RGBA'))
                x0,y0,x1,y1=part['source_box'];raw=source[y0:y1,x0:x1];visible=full[:,:,3]>0
                checks=dict(export_size=full.shape==raw.shape,visible_pixels_preserved=bool(np.array_equal(full[visible],raw[visible])),runtime_is_exact_export_window=bool(np.array_equal(runtime,full[:runtime.shape[0],:runtime.shape[1]])),nonempty=int(np.count_nonzero(visible))>200,has_alpha_clear=bool(np.any(full[:,:,3]==0)),runtime_alpha_clear=bool(np.any(runtime[:,:,3]==0)),finite_anchors=bool(np.isfinite(np.array(part['pivot']+part['tip'])).all()))
                if not all(checks.values()):failures.append(dict(role=role,number=part['number'],checks=checks))
                rows.append(dict(role=role,number=part['number'],checks=checks,export_sha256=hashlib.sha256((ROOT/part['export']).read_bytes()).hexdigest(),runtime_sha256=hashlib.sha256((ROOT/part['runtime']).read_bytes()).hexdigest()))
    result=dict(pass_=not failures,source_archive_sha256=m['archive_sha256'],characters=len(m['characters']),independent_full_source_crops=len(rows),independent_runtime_crops=len(rows),failures=failures,parts=rows)
    result['pass']=result.pop('pass_');(OUT/'asset-validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    assert len(rows)==408 and len(m['characters'])==24 and not failures,result
    print(json.dumps({k:v for k,v in result.items() if k!='parts'},ensure_ascii=False))

if __name__=='__main__':main()
