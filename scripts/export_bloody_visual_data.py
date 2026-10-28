"""Resolve original appearance, paint-texture and environment dependencies.

This exports source data only. A resolved texture or prefab is not evidence of
runtime rendering parity. Private Unity fields use the recovered attributes.
"""
import collections
import hashlib
import json
import re
import struct
from pathlib import Path

import UnityPy
from UnityPy.classes.PPtr import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeHelper import read_value, TypeTreeConfig
from UnityPy.streams.EndianBinaryReader import EndianBinaryReader


def main():
    root=Path(__file__).resolve().parents[1]
    folder=root/'output/bloody-full-port-20261028'
    original=root/'output/bloody-reference-20261028'
    project=root/'unity/BloodyBastardsRecovery'
    env=UnityPy.load(str(original/'serialized'))
    files={f.name:f for f in env.files.values()}
    generator=TypeTreeGenerator('6000.4.0f1');generator.load_local_dll_folder(str(original/'dummy-attributes'))
    nodes={}
    dependencies=json.loads((folder/'dependencies.json').read_text())
    all_asset_paths=collections.defaultdict(list)
    for p in (project/'Assets').rglob('*'):
        if p.is_file() and p.suffix not in ['.meta','.cs','.dll','.json']:
            all_asset_paths[p.stem].append(p.relative_to(project).as_posix())

    def typed(obj):
        header=obj.read_typetree(check_read=False)
        script=obj.read(check_read=False).m_Script.read()
        name=(script.m_Namespace+'.' if script.m_Namespace else '')+script.m_ClassName
        key=(script.m_AssemblyName,name)
        if key not in nodes:
            node=generator.get_nodes_up(*key)
            for child in node.m_Children:
                if child.m_Name=='m_Enabled':child.m_MetaFlag|=0x4000
            for child in node.traverse():
                if child.m_Type=='string' and child.m_Children and child.m_Children[0].m_Type=='Array' and child.m_Children[0].m_Children[-1].m_Type=='string':child.m_Type='vector'
            nodes[key]=node
        reader=EndianBinaryReader(obj.get_raw_data(),endian=obj.reader.endian)
        data=read_value(nodes[key],reader,TypeTreeConfig(True,obj.assets_file,False))
        if reader.Position!=obj.byte_size:raise ValueError(f'Bad source schema {name}: {reader.Position}/{obj.byte_size}')
        for k in ('m_GameObject','m_Enabled','m_Script','m_Name'):
            if k in header:data[k]=header[k]
        return script.m_ClassName,data

    scene_paths={}
    for file in files.values():
        gos={str(o.path_id):o.read_typetree() for o in file.objects.values() if o.type.name=='GameObject'}
        ts={str(o.path_id):o.read_typetree() for o in file.objects.values() if o.type.name in ['Transform','RectTransform']}
        by_go={str(t['m_GameObject']['m_PathID']):k for k,t in ts.items()}
        memo={}
        def scene_path(t):
            if t in memo:return memo[t]
            tr=ts[t];p=str(tr['m_Father']['m_PathID']);n=gos[str(tr['m_GameObject']['m_PathID'])]['m_Name']
            memo[t]=(scene_path(p)+'/'+n) if p in ts else n
            return memo[t]
        for goid,go in gos.items():
            if goid in by_go:
                value=scene_path(by_go[goid])
                scene_paths[file.name+':'+goid]=value
                for c in go['m_Component']:scene_paths[file.name+':'+str(c['component']['m_PathID'])]=value

    asset_refs={}
    def asset(file,ref):
        if not ref.get('m_PathID'):return None
        obj=PPtr(m_FileID=ref['m_FileID'],m_PathID=ref['m_PathID'],assetsfile=file).deref()
        identity=obj.assets_file.name+':'+str(obj.path_id)
        if identity in asset_refs:return identity
        record=dict(source=dict(file=obj.assets_file.name,id=obj.path_id),type=obj.type.name)
        if identity in scene_paths:record['scene_path']=scene_paths[identity]
        if obj.type.name in ['Texture2D','Material','Mesh','Sprite','GameObject','AnimationClip','PhysicsMaterial2D']:
            data=obj.read_typetree();name=data.get('m_Name','')
            record['name']=name
            candidates=all_asset_paths.get(name,[])
            extension={'Texture2D':'.png','Material':'.mat','Mesh':'.asset','Sprite':'.asset','GameObject':'.prefab','AnimationClip':'.anim','PhysicsMaterial2D':'.physicsMaterial2D'}[obj.type.name]
            candidates=[p for p in candidates if p.endswith(extension)]
            record['asset_paths']=candidates
            if obj.type.name=='Texture2D':record.update(width=data['m_Width'],height=data['m_Height'],filter=data['m_TextureSettings']['m_FilterMode'])
        asset_refs[identity]=record
        return identity

    def fvalue(data):
        raw=bytearray(struct.pack('<I',data['hiddenValue']&0xffffffff));raw[1],raw[2]=raw[2],raw[1]
        return struct.unpack('<f',struct.pack('<I',int.from_bytes(raw,'little')^(data['currentCryptoKey']&0xffffffff)))[0]

    armor=[]
    for item in dependencies['items']:
        if item['script']!='SourcePrefab_ArmorItem':continue
        file=files[item['source']['file']];data=item['data']
        go=file.objects[data['m_GameObject']['m_PathID']].read_typetree()
        record=dict(source=item['source'],name=go['m_Name'],item_id=data['itemId'],type=data['Type'],label=data['Name'],empty=bool(data['EmptyShopSlot']),
                    texture=asset(file,data['Texture']),tucked_beard_texture=asset(file,data['TuckedBeardTexture']),
                    tucked_top_texture=asset(file,data['TuckedTopL1Texture']),tint=data['Tint'],
                    defense=fvalue(data['obs_Defense']),weight=fvalue(data['obs_Weight']),damage=fvalue(data['obs_Damage']),
                    flags={k:data[k] for k in ['NotTintable','ErasesHair','TucksHair','SqueezesHair','ErasesBeard','TucksBeard','CoversChest','CoversL1Arms','HasL3GiantShoulders']},
                    parameters={k:data[k] for k in ['HatBoneFrequency_Top','HatBoneFrequency_Bottom'] if k in data},
                    source_parts={k:v for k,v in data.items() if k.startswith('SourceArmorPart_')})
        armor.append(record)

    rig_records={}
    wanted={'CwPaintableMeshTexture','CwPaintableMesh','CwMaterialCloner','CharacterConfigApplier','SkirtBonesAnimator'}
    for name,rig in dependencies['rigs'].items():
        file=files[name];objects=rig['objects'];transforms=rig['transforms']
        go_to_t={str(t['m_GameObject']['m_PathID']):k for k,t in transforms.items()}
        def path(t):
            data=transforms[t];parent=str(data['m_Father']['m_PathID'])
            return (path(parent)+'/'+objects[str(data['m_GameObject']['m_PathID'])]['m_Name']).lstrip('/') if parent!='0' else ''
        records=[]
        for obj in file.objects.values():
            if obj.type.name!='MonoBehaviour':continue
            s=obj.read(check_read=False).m_Script
            if not s.m_PathID or s.read().m_ClassName not in wanted:continue
            cls,data=typed(obj)
            record=dict(id=obj.path_id,script=cls,path=path(go_to_t[str(data['m_GameObject']['m_PathID'])]),data=data)
            if cls=='CwPaintableMeshTexture':record['texture']=asset(file,data['texture'])
            if cls=='CharacterConfigApplier':
                record['textures']={k:asset(file,v) for k,v in data.items() if isinstance(v,dict) and 'm_PathID'in v and ('Texture' in k or 'texture' in k) and v['m_FileID']!=0}
            records.append(record)
        rig_records[name]=records

    scene_records=[]
    wanted={'EnvironmentCustomizer','TimeOfDayController','GameWorld','WeatherManager','CustomizeEnviroProp','RegionConfiguration','TopRimRendererFeature',
            'UniversalRenderPipelineAsset','Renderer2DData','Light2D'}
    for file in files.values():
        for obj in file.objects.values():
            if obj.type.name!='MonoBehaviour':continue
            p=obj.read(check_read=False).m_Script
            if not p.m_PathID or p.read().m_ClassName not in wanted:continue
            cls,data=typed(obj)
            bindings={}
            def visit(value,key):
                if isinstance(value,dict):
                    if 'm_PathID'in value:
                        bindings[key]=asset(file,value)
                    else:
                        for k,v in value.items():visit(v,key+'.'+k if key else k)
                elif isinstance(value,list):
                    for i,v in enumerate(value):visit(v,key+f'[{i}]')
            visit(data,'')
            record=dict(source=dict(file=file.name,id=obj.path_id),script=cls,data=data,assets=bindings)
            identity=file.name+':'+str(obj.path_id)
            if identity in scene_paths:record['scene_path']=scene_paths[identity]
            scene_records.append(record)
    for file in files.values():
        for obj in file.objects.values():
            if obj.type.name=='PhysicsMaterial2D':asset(file,dict(m_FileID=0,m_PathID=obj.path_id))
    result=dict(source_apk_sha256=dependencies['source_apk_sha256'],armor=armor,rigs=rig_records,scene=scene_records,assets=asset_refs,
                runtime_parity_verified=False)
    target=folder/'original-visual-catalog.json';target.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(armor=len(armor),rigs={k:len(v) for k,v in rig_records.items()},scene=len(scene_records),assets=len(asset_refs),unresolved_assets=sum(not x.get('asset_paths') for x in asset_refs.values() if x['type']in ['Texture2D','Material','Mesh','Sprite','GameObject','AnimationClip']))))


if __name__=='__main__':main()
