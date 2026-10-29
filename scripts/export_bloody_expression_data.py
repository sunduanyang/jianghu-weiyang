"""Extract original expression/voice controllers, clips and AudioObject weights."""
import copy
import json
from pathlib import Path
import shutil
import UnityPy
from UnityPy.classes import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeHelper import read_value,TypeTreeConfig
from UnityPy.streams.EndianBinaryReader import EndianBinaryReader
from prepare_bloody_recovery_project import resolve_rig

ROOT=Path(__file__).resolve().parents[1]

def main():
    deps=json.loads((ROOT/'output/bloody-full-port-20261028/dependencies.json').read_text(encoding='utf-8-sig'))
    env=UnityPy.load(str(ROOT/'output/bloody-reference-20261028/serialized'))
    files={f.name:f for f in env.files.values()}
    generator=TypeTreeGenerator('6000.4.0f1');generator.load_local_dll_folder(str(ROOT/'output/bloody-reference-20261028/dummy-attributes'))
    cache={};audio={};clips={};records={}
    def typed(obj,script):
        key=(script.m_AssemblyName,(script.m_Namespace+'.' if script.m_Namespace else '')+script.m_ClassName)
        if key not in cache:
            nodes=generator.get_nodes_up(*key)
            for node in nodes.m_Children:
                if node.m_Name=='m_Enabled':node.m_MetaFlag|=0x4000
            cache[key]=nodes
        reader=EndianBinaryReader(obj.get_raw_data(),endian=obj.reader.endian)
        result=read_value(cache[key],reader,TypeTreeConfig(True,obj.assets_file,False))
        if reader.Position!=obj.byte_size:raise ValueError(f'Incomplete {script.m_ClassName}: {reader.Position}/{obj.byte_size}')
        return result
    def asset(file,ref):
        if not ref.get('m_PathID'):return None
        obj=PPtr(**ref,assetsfile=file).deref();key=f'{obj.assets_file.name}:{obj.path_id}'
        if obj.type.name=='AnimationClip':
            data=obj.read_typetree();clips[key]=dict(name=data['m_Name'],events=data.get('m_Events',[]),settings=data.get('m_MuscleClip',{}).get('m_Clip',{}),source=dict(file=obj.assets_file.name,id=obj.path_id))
            return dict(kind='clip',key=key,name=data['m_Name'])
        if obj.type.name=='AudioClip':
            name=obj.read().m_Name;audio[key]=dict(name=name,source=dict(file=obj.assets_file.name,id=obj.path_id))
            return dict(kind='audio',key=key,name=name)
        if obj.type.name=='MonoBehaviour':
            script=obj.read(check_read=False).m_Script.read()
            if script.m_ClassName=='AudioObject':
                if key not in records:
                    records[key]={k:resolve_assets(obj.assets_file,v) for k,v in typed(obj,script).items()}
                return dict(kind='audio_object',key=key)
        return None
    def resolve_assets(file,value):
        if isinstance(value,list):return [resolve_assets(file,v) for v in value]
        if not isinstance(value,dict):return value
        if 'm_PathID' in value:return asset(file,value) or value
        return {k:resolve_assets(file,v) for k,v in value.items()}
    output={}
    for name,rig in deps['rigs'].items():
        controllers=copy.deepcopy(rig['controllers']);wanted={'CharacterExpressions','VoiceSFX','AnimancerComponent','CharacterImpactFeed','CharacterAnimationController','KienematicDynamicEnabler'}
        raw={}
        for obj in files[name].objects.values():
            if obj.type.name!='MonoBehaviour':continue
            p=obj.read(check_read=False).m_Script
            if not p.m_PathID:continue
            script=p.read()
            if script.m_ClassName not in wanted:continue
            data=typed(obj,script)
            controllers.setdefault(script.m_ClassName,[])
            controllers[script.m_ClassName]=[row for row in controllers[script.m_ClassName]if row['id']!=obj.path_id]
            controllers[script.m_ClassName].append(dict(id=obj.path_id,data=data));raw[obj.path_id]=(obj,data)
        resolved=resolve_rig(dict(rig,controllers=controllers))
        output[name]={k:v for k,v in resolved['controllers'].items() if k in wanted}
        for kind,rows in output[name].items():
            for row in rows:
                obj,data=raw[row['id']]
                for field,value in data.items():
                    if isinstance(value,dict) and 'm_PathID' in value:
                        result=asset(obj.assets_file,value)
                        if result:row['data'][field]=result
    target=ROOT/'recovery/bloody/Runtime/Resources';dest=target/'OriginalExpressions';dest.mkdir(parents=True,exist_ok=True)
    exported=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
    for entries,folder,ext in ((clips,'AnimationClip','.anim'),(audio,'AudioClip',None)):
        for key,entry in entries.items():
            name=entry['name'];matches=[p for p in (exported/folder).iterdir() if p.stem==name and p.suffix!='.meta' and (ext is None or p.suffix==ext)]
            if len(matches)!=1:raise ValueError(f'Asset {key} {name}: {matches}')
            # Source identity, not a potentially colliding asset name, is the resource key.
            resource=key.replace(':','_');shutil.copy2(matches[0],dest/(resource+matches[0].suffix));entry['resource']='OriginalExpressions/'+resource
    result=dict(rigs=output,clips=clips,audio=audio,audio_objects=records)
    (target/'OriginalExpressionSource.json').write_text(json.dumps(result,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print(json.dumps(dict(rigs=len(output),expression_clips=len(clips),audio_clips=len(audio),audio_objects=len(records))))

if __name__=='__main__':main()
