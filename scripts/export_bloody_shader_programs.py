"""Recover actual GLES programs; Unity 6 player programs are not m_SubPrograms.

AssetRipper's DummyShaderTextExporter and UnityPy's parsed-form export omit the
new player-program table. Preserve the compiled game's embedded GLSL directly
as analysis material, together with states and variant identities.
"""
import json
from pathlib import Path
import UnityPy
from UnityPy.export.ShaderConverter import ShaderProgram
from UnityPy.helpers import CompressionHelper
from UnityPy.streams import EndianBinaryReader


def main():
    root=Path(__file__).resolve().parents[1]
    env=UnityPy.load(str(root/'output/bloody-reference-20261028/serialized'))
    out=root/'output/bloody-full-port-20261028/shader-programs'
    out.mkdir(parents=True,exist_ok=True)
    ledger=[]
    for obj in env.objects:
        if obj.type.name!='Shader':continue
        shader=obj.read()
        if not shader.m_ParsedForm:continue
        name=shader.m_ParsedForm.m_Name
        if not name.startswith(('Shader Graphs/','BB/','Custom/','Hidden/PaintCore/')):continue
        directory=out/name.replace('/','_');directory.mkdir(exist_ok=True)
        structure=obj.read_typetree()
        structure.pop('compressedBlob',None)
        (directory/'structure.json').write_text(json.dumps(structure),encoding='utf-8')
        # Unity 6 mixes parameter blobs into this table. Only the parsed player
        # program references identify executable stages and their real keywords.
        references={}
        keyword_names=structure['m_ParsedForm'].get('m_KeywordNames',[])
        for subshader in structure['m_ParsedForm']['m_SubShaders']:
            for pi,spass in enumerate(subshader['m_Passes']):
                for stage in ['progVertex','progFragment']:
                    for group in spass[stage].get('m_PlayerSubPrograms',[]):
                        for entry in group:
                            references.setdefault(entry['m_BlobIndex'],[]).append(dict(pass_index=pi,stage=stage,keywords=[keyword_names[i] for i in entry['m_KeywordIndices']]))
        variants=[]
        for platform,offsets,lengths,sizes in zip(shader.platforms,shader.offsets,shader.compressedLengths,shader.decompressedLengths):
            if not isinstance(offsets,list):offsets,lengths,sizes=[offsets],[lengths],[sizes]
            for segment,(offset,length,size) in enumerate(zip(offsets,lengths,sizes)):
                raw=CompressionHelper.decompress_lz4(bytes(shader.compressedBlob)[offset:offset+length],size)
                program=ShaderProgram(EndianBinaryReader(raw,endian='<'),obj.version)
                for index,sub in enumerate(program.m_SubPrograms):
                    code=bytes(sub.m_ProgramCode)
                    file=f'{platform}-{segment}-{index}.glsl'
                    try:text=code.decode('utf-8')
                    except UnicodeDecodeError:file=file.removesuffix('.glsl')+'.bin';text=None
                    if text is None:(directory/file).write_bytes(code)
                    else:(directory/file).write_text(text,encoding='utf-8')
                    executable=text is not None and '#ifdef VERTEX' in text and '#ifdef FRAGMENT' in text
                    variants.append(dict(file=file,index=index,platform=platform,type=int(sub.m_ProgramType),
                                         keywords=references.get(index,[{}])[0].get('keywords',sub.m_Keywords if executable else []),
                                         references=references.get(index,[]),executable=executable,size=len(code)))
        (directory/'variants.json').write_text(json.dumps(variants,indent=2),encoding='utf-8')
        ledger.append(dict(shader=name,variants=len(variants),directory=str(directory.relative_to(root))))
    (out/'index.json').write_text(json.dumps(ledger,indent=2),encoding='utf-8')
    print(json.dumps(ledger))


if __name__=='__main__':main()
