"""Bind recovered UGUI/TMP components to the matching official Unity package.

DLL MonoScript file identifiers use Unity's MD4 naming convention. Mapping to
the same version's package script GUID retains the original serialized UI;
empty IL2CPP DLL stubs are not needed by the recovered project.
"""
import re
import shutil
import struct
import tarfile
from pathlib import Path


def md4(data):
    length=len(data)*8
    data+=b'\x80';data+=b'\0'*((56-len(data))%64)+struct.pack('<Q',length)
    state=[0x67452301,0xefcdab89,0x98badcfe,0x10325476]
    for offset in range(0,len(data),64):
        words=struct.unpack('<16I',data[offset:offset+64]);a,b,c,d=state
        for i in range(48):
            round,index=divmod(i,16)
            if round==0:f=(b&c)|(~b&d);word=index;shift=[3,7,11,19][index%4];constant=0
            elif round==1:f=(b&c)|(b&d)|(c&d);word=(index%4)*4+index//4;shift=[3,5,9,13][index%4];constant=0x5a827999
            else:f=b^c^d;word=[0,8,4,12,2,10,6,14,1,9,5,13,3,11,7,15][index];shift=[3,9,11,15][index%4];constant=0x6ed9eba1
            v=(a+f+words[word]+constant)&0xffffffff;v=((v<<shift)|(v>>(32-shift)))&0xffffffff
            a,b,c,d=d,v,b,c
        state=[(x+y)&0xffffffff for x,y in zip(state,[a,b,c,d])]
    return struct.pack('<4I',*state)


SCRIPT=re.compile(r'm_Script: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: \d+\}')
CURVE_SCRIPT=re.compile(r'(\bscript:\s*)\{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: \d+\}')


def guid(path):
    return re.search(r'^guid: ([0-9a-f]{32})',path.read_text(encoding='utf-8-sig'),re.M)[1]


def bindings(raw_assets,package):
    dlls={namespace:guid(raw_assets/'Plugins'/filename) for namespace,filename in
          [('UnityEngine.UI','UnityEngine.UI.dll.meta'),('UnityEngine.EventSystems','UnityEngine.UI.dll.meta'),('TMPro','Unity.TextMeshPro.dll.meta')]}
    result={}
    for source in (package/'Runtime').rglob('*.cs'):
        meta=source.with_suffix('.cs.meta')
        if not meta.exists():continue
        text=source.read_text(encoding='utf-8-sig');ns=re.search(r'\bnamespace\s+([\w.]+)',text)
        if not ns or ns[1] not in dlls:continue
        name=source.stem
        if not re.search(r'\b(?:class|struct)\s+'+re.escape(name)+r'\b',text):continue
        file_id=int.from_bytes(md4(('s\0\0\0'+ns[1]+name).encode())[:4],'little',signed=True)
        result[(str(file_id),dlls[ns[1]])]=dict(guid=guid(meta),name=ns[1]+'.'+name)
    return result


def remap_document(document,mapping):
    match=SCRIPT.search(document)
    if not match:return None
    target=mapping.get((match[1],match[2]))
    if target is None:return None
    return document[:match.start()]+f'm_Script: {{fileID: 11500000, guid: {target["guid"]}, type: 3}}'+document[match.end():]


def remap_animation_bindings(assets,mapping):
    changed={}
    for path in assets.rglob('*.anim'):
        text=path.read_text(encoding='utf-8-sig');count=0
        def replace(match):
            nonlocal count
            target=mapping.get((match[2],match[3]))
            if target is None:return match[0]
            count+=1
            return match[1]+f'{{fileID: 11500000, guid: {target["guid"]}, type: 3}}'
        updated=CURVE_SCRIPT.sub(replace,text)
        if count:path.write_text(updated,encoding='utf-8',newline='\n');changed[path.relative_to(assets).as_posix()]=count
    return changed


def copy_font_assets(raw_assets,assets,mapping):
    count=0
    for source in raw_assets.rglob('*.asset'):
        original=source.read_text(encoding='utf-8-sig')
        match=SCRIPT.search(original)
        if not match or not mapping.get((match[1],match[2]),{}).get('name','').startswith('TMPro.'):continue
        converted=remap_document(original,mapping)
        target=assets/source.relative_to(raw_assets);target.parent.mkdir(parents=True,exist_ok=True)
        target.write_text(converted,encoding='utf-8')
        if source.with_suffix('.asset.meta').exists():shutil.copy2(source.with_suffix('.asset.meta'),target.with_suffix('.asset.meta'))
        count+=1
    return count


def restore_tmp_shaders(assets,package):
    shaders={}
    for source in package.rglob('*.shader'):
        name=re.search(r'\bShader\s+"([^"]+)"',source.read_text(encoding='utf-8-sig'))
        if name and name[1].startswith('TextMeshPro/'):shaders[name[1]]=source
    archive=package/'Package Resources/TMP Essential Resources.unitypackage'
    extracted={}
    if archive.exists():
        with tarfile.open(archive,'r:gz') as tar:
            members={m.name:m for m in tar.getmembers() if m.isfile()}
            for key,member in members.items():
                if not key.endswith('/pathname'):continue
                name=tar.extractfile(member).read().decode('utf-8').strip()
                payload=members.get(key.rsplit('/',1)[0]+'/asset')
                if payload and name.endswith(('.shader','.cginc','.hlsl')):
                    content=tar.extractfile(payload).read()
                    extracted[Path(name).name]=content
                    shader=re.search(rb'\bShader\s+"([^"]+)"',content)
                    if shader:shaders[shader[1].decode()]=content
    restored=0
    for target in (assets/'Shader').glob('*.shader'):
        text=target.read_text(encoding='utf-8-sig');name=re.search(r'\bShader\s+"([^"]+)"',text)
        if name and name[1] in shaders:
            source=shaders[name[1]]
            if isinstance(source,bytes):target.write_bytes(source)
            else:shutil.copy2(source,target)
            restored+=1
    for source in package.rglob('*.cginc'):
        shutil.copy2(source,assets/'Shader'/source.name)
    for name,content in extracted.items():
        if not name.endswith('.shader'):(assets/'Shader'/name).write_bytes(content)
    return restored
