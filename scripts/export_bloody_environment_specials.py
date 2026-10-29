"""Export source-only seasonal activation, reverb and spooky references."""
import collections,json,re
from pathlib import Path
import UnityPy
from UnityPy.classes.PPtr import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeHelper import read_value,TypeTreeConfig
from UnityPy.streams.EndianBinaryReader import EndianBinaryReader
ROOT=Path(__file__).resolve().parents[1]


def main():
    visual=json.loads((ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json').read_text(encoding='utf-8'))
    scope=json.loads((ROOT/'output/bloody-full-port-20261028/environment-check/stage-scope-audit.json').read_text(encoding='utf-8'))
    roots={r['root']for r in scope['stages']}
    env=UnityPy.load(str(ROOT/'output/bloody-reference-20261028/serialized'))
    generator=TypeTreeGenerator('6000.4.0f1');generator.load_local_dll_folder(str(ROOT/'output/bloody-reference-20261028/dummy-attributes'))
    nodes={};transform_cache={};scripts={};records=[];refs={}
    def hierarchy(obj):
        file=obj.assets_file
        if file.name not in transform_cache:
            gos={o.path_id:o.read_typetree()for o in file.objects.values()if o.type.name=='GameObject'}
            ts={o.path_id:o.read_typetree()for o in file.objects.values()if o.type.name in ('Transform','RectTransform')}
            transform_cache[file.name]=(gos,ts,{t['m_GameObject']['m_PathID']:i for i,t in ts.items()})
        gos,ts,by_go=transform_cache[file.name]
        if obj.type.name=='GameObject':goid=obj.path_id
        else:
            h=obj.read_typetree(check_read=False)
            if 'm_GameObject'not in h:return None
            goid=h['m_GameObject']['m_PathID']
        if goid not in by_go:return None
        current=by_go[goid];indices=[];names=[];active=True
        while True:
            tr=ts[current];go=gos[tr['m_GameObject']['m_PathID']];names.append(go['m_Name']);active=active and bool(go['m_IsActive']);parent=tr['m_Father']['m_PathID']
            if parent not in ts:break
            indices.append([c['m_PathID']for c in ts[parent]['m_Children']].index(current));current=parent
        return dict(root=names[-1],indices=list(reversed(indices)),names=list(reversed(names[:-1])),active_in_hierarchy=active)
    def typed(obj,cls):
        if cls not in nodes:
            nodes[cls]=generator.get_nodes_up('Assembly-CSharp',cls)
            for c in nodes[cls].m_Children:
                if c.m_Name=='m_Enabled':c.m_MetaFlag|=0x4000
        reader=EndianBinaryReader(obj.get_raw_data(),endian=obj.reader.endian)
        data=read_value(nodes[cls],reader,TypeTreeConfig(True,obj.assets_file,False))
        if reader.Position!=obj.byte_size:raise ValueError('Incomplete special source schema: '+cls)
        return data
    def reference(file,ref):
        if not ref['m_PathID']:return None
        obj=PPtr(m_FileID=ref['m_FileID'],m_PathID=ref['m_PathID'],assetsfile=file).deref();identity=obj.assets_file.name+':'+str(obj.path_id)
        if identity not in refs:
            h=hierarchy(obj);row=dict(type=obj.type.name,hierarchy=h,catalog_asset_available=identity in visual['assets'])
            if obj.type.name in ('AudioSource','ParticleSystem'):
                header=obj.read_typetree();go=obj.assets_file.objects[header['m_GameObject']['m_PathID']].read_typetree()
                same=[c['component']['m_PathID']for c in go['m_Component']if obj.assets_file.objects[c['component']['m_PathID']].type.name==obj.type.name]
                row['component_index']=same.index(obj.path_id)
            if obj.type.name in ('AudioMixerGroupController','AudioMixerGroup'):
                source=obj.read_typetree();row['name']=source.get('m_Name');row['source_data']=source
                guid=''.join(format(source['m_GroupID']['data[%d]'%i],'08x')[::-1]for i in range(4));matches=[]
                for path in (ROOT/'unity/BloodyBastardsRecovery/Assets/AudioMixerController').glob('*.mixer'):
                    for block in re.split(r'(?m)^--- ',path.read_text(encoding='utf-8-sig')):
                        file_id=re.match(r'!u!243 &(\d+)',block)
                        if file_id and re.search(r'(?m)^  m_GroupID: '+guid+r'\s*$',block):
                            matches.append(dict(asset_path=path.relative_to(ROOT/'unity/BloodyBastardsRecovery').as_posix(),local_file_id=int(file_id[1]),source_group_guid=guid))
                if len(matches)!=1:raise ValueError('Original AudioMixerGroup identity is not unique: '+identity)
                row['binding']=matches[0]
            refs[identity]=row
        return identity
    candidates=[]
    for obj in env.objects:
        if obj.type.name!='MonoBehaviour':continue
        ptr=obj.read(check_read=False).m_Script
        if not ptr.m_PathID:continue
        script=ptr.deref();key=(script.assets_file.name,script.path_id)
        if key not in scripts:scripts[key]=script.read().m_ClassName
        cls=scripts[key]
        if cls not in ('ActivateGameObjectChristmasMode','ReverbSetter','SpookyEnviroExtras','SingleSFX'):continue
        h=hierarchy(obj)
        if not h:continue
        if cls in ('ActivateGameObjectChristmasMode','ReverbSetter') and h['root']not in roots:continue
        if cls in ('SpookyEnviroExtras','SingleSFX') and h['root']!='Environment':continue
        candidates.append((obj,cls,h))
    for obj,cls,h in candidates:
        data=typed(obj,cls);assets={}
        def visit(v,key):
            if isinstance(v,dict):
                if'm_PathID'in v:assets[key]=reference(obj.assets_file,v)
                else:
                    for k,x in v.items():visit(x,key+'.'+k if key else k)
            elif isinstance(v,list):
                for i,x in enumerate(v):visit(x,key+'['+str(i)+']')
        visit(data,'')
        go=obj.assets_file.objects[data['m_GameObject']['m_PathID']].read_typetree()
        order=[c['component']['m_PathID']for c in go['m_Component']].index(obj.path_id)
        records.append(dict(identity=obj.assets_file.name+':'+str(obj.path_id),script=cls,hierarchy=h,component_order=order,data=data,assets=assets))
    extras=[r for r in records if r['script']=='SpookyEnviroExtras'];assert len(extras)==1
    needed={extras[0]['assets']['spookyHowlsSfx'],extras[0]['assets']['spookyBatsSfx']}
    records=[r for r in records if r['script']!='SingleSFX'or r['identity']in needed]
    assert len([r for r in records if r['script']=='SingleSFX'])==2
    result=dict(source_apk_sha256=visual['source_apk_sha256'],records=records,references=refs,
        semantics='Explicit captured Context flags only; no calendar/date override; source hierarchy preserves duplicate names',native=dict(christmas_start='0x34448f0',reverb_awake='0x343d570',reverb_parameter='SFXReverb',spooky_refresh='0x3412adc',single_sfx_play='0x3412a20'))
    target=ROOT/'recovery/bloody/Runtime/Resources/OriginalEnvironmentSpecialSource.json';target.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')
    audit=ROOT/'output/bloody-full-port-20261028/environment-check/special-source-audit.json';audit.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(counts=dict(collections.Counter(r['script']for r in records)),spooky=extras[0],missing_non_node_catalog_assets=[k for k,r in refs.items()if not r['catalog_asset_available']and r['hierarchy']is None]),ensure_ascii=False))


if __name__=='__main__':main()
