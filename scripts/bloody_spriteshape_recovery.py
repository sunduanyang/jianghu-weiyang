"""Bind original SpriteShape splines/profiles to the Unity 6000.4 package.

Preserve every source control point, tangent, sprite reference and UV setting;
the official package regenerates the native SpriteShapeRenderer geometry.
"""
import re
import shutil
from bloody_ui_recovery import md4,guid,SCRIPT,remap_document


def bindings(raw_assets,package):
    dll=guid(raw_assets/'Plugins/Unity.2D.SpriteShape.Runtime.dll.meta')
    result={}
    for source in (package/'Runtime').glob('*.cs'):
        meta=source.with_suffix('.cs.meta')
        if not meta.exists():continue
        text=source.read_text(encoding='utf-8-sig');ns=re.search(r'\bnamespace\s+([\w.]+)',text);name=source.stem
        if not ns or not re.search(r'\b(?:class|struct)\s+'+re.escape(name)+r'\b',text):continue
        file_id=int.from_bytes(md4(('s\0\0\0'+ns[1]+name).encode())[:4],'little',signed=True)
        result[(str(file_id),dll)]=dict(guid=guid(meta),name=ns[1]+'.'+name)
    return result


def copy_assets(raw_assets,assets,mapping):
    copied=0
    for source in (raw_assets/'MonoBehaviour').glob('*.asset'):
        text=source.read_text(encoding='utf-8-sig');match=SCRIPT.search(text)
        if not match:continue
        entry=mapping.get((match[1],match[2]))
        if entry is None or not entry['name'].startswith('UnityEngine.U2D.'):continue
        target=assets/source.relative_to(raw_assets);target.parent.mkdir(parents=True,exist_ok=True)
        target.write_text(remap_document(text,mapping),encoding='utf-8')
        shutil.copy2(source.with_suffix('.asset.meta'),target.with_suffix('.asset.meta'));copied+=1
    return copied
