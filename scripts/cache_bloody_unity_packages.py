"""Cache required official Unity packages for reproducible local resolution."""
import hashlib
import json
import tarfile
import urllib.request
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
BUNDLED=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data/Resources/PackageManager/BuiltInPackages')
CACHE=ROOT/'output/bloody-full-port-20261028/tools/unity-packages'

def main():
    manifest=json.loads((ROOT/'recovery/bloody/Packages/manifest.json').read_text(encoding='utf-8-sig'))
    pending=list(manifest['dependencies'].items());selected={}
    # The SRP's declared package versions are sufficient for this editor.
    # Prefer declared versions for registry packages; bundled package versions
    # are pinned by this exact editor, including its built-in collections.
    while pending:
        name,version=pending.pop(0)
        if name in selected:continue
        folder=BUNDLED/name
        if not (folder/'package.json').exists():
            folder=CACHE/(name+'@'+version)
            if not (folder/'package.json').exists():
                metadata=json.load(urllib.request.urlopen('https://packages.unity.com/'+name,timeout=30))
                package=metadata['versions'][version];dist=package['dist']
                archive=urllib.request.urlopen(dist['tarball'],timeout=90).read()
                if dist.get('shasum') and hashlib.sha1(archive).hexdigest()!=dist['shasum']:raise ValueError('Package checksum '+name)
                CACHE.mkdir(parents=True,exist_ok=True);tgz=CACHE/(name+'-'+version+'.tgz');tgz.write_bytes(archive)
                import io
                with tarfile.open(fileobj=io.BytesIO(archive),mode='r:gz') as tar:
                    for member in tar.getmembers():
                        relative=Path(*Path(member.name).parts[1:])
                        target=folder/relative
                        if not target.resolve().is_relative_to(folder.resolve()):raise ValueError('Archive path')
                        if member.isfile():target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(tar.extractfile(member).read())
                print('CACHED',name,version,len(archive),flush=True)
        selected[name]=folder
        data=json.loads((folder/'package.json').read_text(encoding='utf-8-sig'))
        for dep,v in data.get('dependencies',{}).items():
            if dep not in selected:pending.append((dep,v))
    for name,path in selected.items():manifest['dependencies'][name]='file:'+path.as_posix()
    target=ROOT/'unity/BloodyBastardsRecovery/Packages/manifest.json';target.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    (CACHE/'resolved-packages.json').write_text(json.dumps({k:str(v)for k,v in selected.items()},indent=2),encoding='utf-8')
    print('RESOLVED',len(selected),flush=True)

if __name__=='__main__':main()
