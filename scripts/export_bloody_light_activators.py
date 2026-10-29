"""Retain source LightActivator settings; only Clear pair is currently wired."""
import json
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeHelper import read_value,TypeTreeConfig
from UnityPy.streams.EndianBinaryReader import EndianBinaryReader
ROOT=Path(__file__).resolve().parents[1]
def main():
    visual=json.loads((ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json').read_text(encoding='utf-8'))
    env=UnityPy.load(str(ROOT/'output/bloody-reference-20261028/serialized'))
    generator=TypeTreeGenerator('6000.4.0f1');generator.load_local_dll_folder(str(ROOT/'output/bloody-reference-20261028/dummy-attributes'))
    node=generator.get_nodes_up('Assembly-CSharp','LightActivator')
    for c in node.m_Children:
        if c.m_Name=='m_Enabled':c.m_MetaFlag|=0x4000
    records=[];script_cache={};transform_cache={}
    def hierarchy(file,goid):
        if file.name not in transform_cache:
            ts={o.path_id:o.read_typetree() for o in file.objects.values() if o.type.name in ('Transform','RectTransform')}
            transform_cache[file.name]=(ts,{t['m_GameObject']['m_PathID']:key for key,t in ts.items()})
        ts,by_go=transform_cache[file.name];current=by_go[goid];indices=[];names=[]
        while True:
            t=ts[current];go=file.objects[t['m_GameObject']['m_PathID']].read_typetree();names.append(go['m_Name']);parent=t['m_Father']['m_PathID']
            if parent not in ts:break
            indices.append([p['m_PathID'] for p in ts[parent]['m_Children']].index(current));current=parent
        return dict(root=names[-1],indices=list(reversed(indices)),names=list(reversed(names[:-1])))
    for obj in env.objects:
        if obj.type.name!='MonoBehaviour':continue
        ptr=obj.read(check_read=False).m_Script
        if not ptr.m_PathID:continue
        script=ptr.deref();key=(script.assets_file.name,script.path_id)
        if key not in script_cache:script_cache[key]=script.read().m_ClassName
        if script_cache[key]!='LightActivator':continue
        reader=EndianBinaryReader(obj.get_raw_data(),endian=obj.reader.endian);data=read_value(node,reader,TypeTreeConfig(True,obj.assets_file,False))
        if reader.Position!=obj.byte_size:raise ValueError('Incomplete source LightActivator schema')
        goid=data['m_GameObject']['m_PathID'];identity=obj.assets_file.name+':'+str(goid)
        go=obj.assets_file.objects[goid].read_typetree();h=visual['hierarchy'].get(identity) or hierarchy(obj.assets_file,goid)
        root=h['root'] if h else None
        records.append(dict(source_identity=obj.assets_file.name+':'+str(obj.path_id),node_identity=identity,
            name=go['m_Name'],source_active_self=go['m_IsActive'],hierarchy=h,catalog_identity_available=identity in visual['hierarchy'],
            scope='clear' if obj.assets_file.name=='level0' and goid in (673,2329) else 'retained_unwired',
            data=dict(enabled=data['m_Enabled'],HoursActive=data['HoursActive'],Inverted=data['Inverted'])))
    clear=[r for r in records if r['scope']=='clear'];assert len(clear)==2 and all(r['hierarchy'] for r in clear)
    result=dict(source_apk_sha256=visual['source_apk_sha256'],native_on_enable_va='0x3444ad4',native_ctor_va='0x3444c60',
        semantics='OnEnable only: Enumerable.Range(native_int(min), unchecked(native_int(max)-native_int(min))).Contains(byte hour); Inverted true keeps contains, false negates; SetActive(self)',
        initial_clear_parent_active_self=False,records=records,wired_scope='Only the two Clear ambience nodes; no time-change event subscription')
    target=ROOT/'recovery/bloody/Runtime/Resources/OriginalLightActivatorSource.json';target.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')
    audit=ROOT/'output/bloody-full-port-20261028/environment-check/light-activator-source-audit.json';audit.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(total_source_records=len(records),clear_pair=clear,other_records_with_catalog_identity=sum(r['scope']!='clear' and r['catalog_identity_available'] for r in records)),ensure_ascii=False))
if __name__=='__main__':main()
