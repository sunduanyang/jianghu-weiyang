"""Export native combat component settings omitted from the initial rig view."""
import copy
import json
from pathlib import Path

import UnityPy
from UnityPy.classes import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeHelper import read_value, TypeTreeConfig
from UnityPy.streams.EndianBinaryReader import EndianBinaryReader

from prepare_bloody_recovery_project import resolve_rig
from build_bloody_item_catalog import graph_index


def main():
    root=Path(__file__).resolve().parents[1]
    deps=json.loads((root/'output/bloody-full-port-20261028/dependencies.json').read_text())
    env=UnityPy.load(str(root/'output/bloody-reference-20261028/serialized'))
    files={f.name:f for f in env.files.values()}
    generator=TypeTreeGenerator('6000.4.0f1')
    generator.load_local_dll_folder(str(root/'output/bloody-reference-20261028/dummy-attributes'))
    cache={}
    wanted={'bt','InternalOrgan','StaminaDrainer','KickReceiver','HandCannonSync',
            'BodyPart','CharacterVitality','CharacterDamageCalculator','CharacterConfigApplier',
            'WeaponSelfColliderInitializer','KienematicDynamicEnabler'}

    def typed(obj,script):
        key=(script.m_AssemblyName,script.m_ClassName)
        if key not in cache:
            nodes=generator.get_nodes_up(*key)
            for child in nodes.m_Children:
                if child.m_Name=='m_Enabled':child.m_MetaFlag|=0x4000
            for node in nodes.traverse():
                if (node.m_Type=='string' and node.m_Children and node.m_Children[0].m_Type=='Array'
                        and node.m_Children[0].m_Children[-1].m_Type=='string'):node.m_Type='vector'
            cache[key]=nodes
        reader=EndianBinaryReader(obj.get_raw_data(),endian=obj.reader.endian)
        data=read_value(cache[key],reader,TypeTreeConfig(True,obj.assets_file,False))
        if reader.Position!=obj.byte_size:raise ValueError(f'Incomplete decode {script.m_ClassName} {obj.path_id}')
        return data

    output={}
    for name,rig in deps['rigs'].items():
        controllers=copy.deepcopy(rig['controllers'])
        for obj in files[name].objects.values():
            if obj.type.name!='MonoBehaviour':continue
            pointer=obj.read(check_read=False).m_Script
            if not pointer.m_PathID:continue
            script=pointer.read()
            if script.m_ClassName in wanted:
                controllers.setdefault(script.m_ClassName,[])
                controllers[script.m_ClassName]=[v for v in controllers[script.m_ClassName] if v['id']!=obj.path_id]
                controllers[script.m_ClassName].append(dict(id=obj.path_id,data=typed(obj,script)))
        enriched=dict(rig,controllers=controllers)
        output[name]=resolve_rig(enriched)
    destination=root/'recovery/bloody/Runtime/Resources'
    destination.mkdir(parents=True,exist_ok=True)
    (destination/'OriginalCombatSource.json').write_text(json.dumps(output,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    armor_materials={}
    for item in deps['items']:
        if item['script']!='SourcePrefab_ArmorItem':continue
        source=item['source'];mapping={}
        for name,pointer in item['data'].items():
            if not name.startswith('SourceArmorPart_') or not pointer['m_PathID']:continue
            obj=PPtr(**pointer,assetsfile=files[source['file']]).deref()
            pointer=obj.read(check_read=False).m_Script
            if not pointer.m_PathID:continue
            script=pointer.read()
            if script.m_ClassName!='bt':raise ValueError('Armor source component is not bt')
            mapping[name.removeprefix('Source')]=typed(obj,script)['armorMaterial']
        armor_materials[f"{source['file']}:{source['id']}"]=mapping
    (destination/'OriginalArmorMaterials.json').write_text(json.dumps(armor_materials,separators=(',',':')),encoding='utf-8')
    catalog=json.loads((root/'output/bloody-full-port-20261028/original-item-catalog.json').read_text())
    # HandCannon sits beside SourcePrefab_HandItem and is not represented by
    # that item's public fields. Preserve its source geometry/settings too.
    extras={}
    original_rows={(i['source']['file'],i['source']['id']):i for i in deps['items']}
    for record in catalog['items']:
        filename=record['source']['file']
        goid=str(original_rows[(filename,record['source']['id'])]['data']['m_GameObject']['m_PathID'])
        for component in deps['item_graphs'][filename]['objects'][goid]['m_Component']:
            obj=files[filename].objects[component['component']['m_PathID']]
            if obj.type.name!='MonoBehaviour':continue
            script_pointer=obj.read(check_read=False).m_Script
            if not script_pointer.m_PathID:continue
            script=script_pointer.read()
            if script.m_ClassName not in {'HandCannon'}:continue
            data=typed(obj,script)
            extras[(filename,str(data['m_GameObject']['m_PathID']))]=data
    for record in catalog['items']:
        filename=record['source']['file']
        objects,transforms,go_to_t,component_go=graph_index(deps['item_graphs'][filename])
        root_go=str(original_rows[(filename,record['source']['id'])]['data']['m_GameObject']['m_PathID'])
        root_t=go_to_t[root_go]
        cache={root_t:''}
        def path(t):
            if t in cache:return cache[t]
            parent=str(transforms[t]['m_Father']['m_PathID'])
            if parent=='0':raise ValueError('Extra reference leaves original prefab')
            cache[t]=(path(parent)+'/'+objects[str(transforms[t]['m_GameObject']['m_PathID'])]['m_Name']).lstrip('/')
            return cache[t]
        def resolve(value):
            if isinstance(value,list):return [resolve(v) for v in value]
            if not isinstance(value,dict):return value
            if 'm_PathID' not in value:return {k:resolve(v) for k,v in value.items()}
            i=str(value['m_PathID']);r=dict(id=i,file=value['m_FileID'])
            if value['m_FileID']==0 and i!='0':
                owner=i if i in objects else component_go.get(i)
                if owner:r['path']=path(go_to_t[owner])
            return r
        if (filename,root_go) in extras:record['cannon']=resolve(extras[(filename,root_go)])
        # Exact component binding avoids confusing multiple colliders/joints
        # on one original GameObject.
        bindings={};kinds={str(p['id']):p['type'] for p in deps['item_graphs'][filename]['physics']}
        descendant_transforms=[]
        def walk(t):
            descendant_transforms.append(t)
            for child in transforms[t]['m_Children']:walk(str(child['m_PathID']))
        walk(root_t)
        for t in descendant_transforms:
            goid=str(transforms[t]['m_GameObject']['m_PathID']);go=objects[goid]
            counts={}
            local_path=path(t)
            for entry in go['m_Component']:
                i=str(entry['component']['m_PathID']);kind=kinds.get(i)
                if kind:
                    bindings[i]=dict(path=local_path,kind=kind,index=counts.get(kind,0));counts[kind]=counts.get(kind,0)+1
        record['component_bindings']=bindings
    (destination/'OriginalItemCatalog.json').write_text(json.dumps(catalog,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print(json.dumps({k:{n:len(v) for n,v in r['controllers'].items() if n in wanted} for k,r in output.items()}))


if __name__=='__main__':main()
