"""Package the verified independent exports and the exact Unity rig resources."""
import hashlib,json,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/cast24-binding-20261029'

def main():
    assets=json.loads((OUT/'asset-validation.json').read_text(encoding='utf-8'))
    native=json.loads((OUT/'native-verified/character-cast24-native.json').read_text(encoding='utf-8'))
    assert assets['pass'] and native['pass']
    destination=ROOT/'output/imagegen/cast-24-bound-parts-20261029.zip'
    resource=ROOT/'unity/JianghuUnity/Assets/Resources/Characters/Cast24'
    entries=[]
    for path in sorted((OUT/'parts').rglob('*.png')):entries.append((path,'complete-parts/'+path.relative_to(OUT/'parts').as_posix()))
    for path in sorted(resource.rglob('*')):
        if path.is_file():entries.append((path,'unity-resources/Characters/Cast24/'+path.relative_to(resource).as_posix()))
    for name in ['binding-manifest.json','asset-validation.json']:entries.append((OUT/name,name))
    entries.extend([(ROOT/'docs/validation/CAST24_BINDING_VALIDATION.md','README.md'),(OUT/'native-verified/character-cast24-native.json','validation/character-cast24-native.json')])
    with zipfile.ZipFile(destination,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for path,name in entries:z.write(path,name)
    with zipfile.ZipFile(destination) as z:
        assert z.testzip() is None
        complete=sum(n.startswith('complete-parts/') and n.endswith('.png') for n in z.namelist())
        runtime=sum(n.startswith('unity-resources/') and n.endswith('.png') for n in z.namelist())
        assert complete==runtime==408
    result=dict(file=str(destination),bytes=destination.stat().st_size,entries=len(entries),complete_parts=complete,runtime_parts=runtime,sha256=hashlib.sha256(destination.read_bytes()).hexdigest())
    (OUT/'package-validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(result,ensure_ascii=False))

if __name__=='__main__':main()
