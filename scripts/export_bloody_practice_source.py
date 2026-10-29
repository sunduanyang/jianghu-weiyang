"""Normalize references from the source Dummy prefab; preserve source values."""
from pathlib import Path
import json
import re
import struct
from export_bloody_match_configs import documents

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'


def main():
    docs=documents(ASSETS/'GameObject/Dummy.prefab');by={d['__file_id']:d for d in docs}
    kinds={k:next(n for n in d if n!='__file_id')for k,d in by.items()}
    values={k:d[kinds[k]]for k,d in by.items()}
    gos={k:v for k,v in values.items()if kinds[k]=='GameObject'}
    transforms={k:v for k,v in values.items()if kinds[k]=='Transform'}
    go_t={str(v['m_GameObject']['fileID']):k for k,v in transforms.items()}
    def path(t):
        data=transforms[t];parent=str(data['m_Father']['fileID'])
        return (path(parent)+'/'+gos[str(data['m_GameObject']['fileID'])]['m_Name']).lstrip('/')if parent!='0'else''
    bindings={}
    for goid,go in gos.items():
        component_counts={};go_path=path(go_t[goid]);bindings[goid]=dict(path=go_path,kind='GameObject',index=0)
        for component in go['m_Component']:
            key=str(component['component']['fileID']);kind=kinds[key];index=component_counts.get(kind,0);component_counts[kind]=index+1
            bindings[key]=dict(path=go_path,kind=kind,index=index)
    scripts={};assets_by_guid={}
    for meta in ASSETS.rglob('*.meta'):
        match=re.search(r'^guid: (\w+)',meta.read_text(encoding='utf-8-sig'),re.M)
        if not match:continue
        asset=Path(str(meta)[:-5]);assets_by_guid[match[1]]=asset
        if asset.suffix=='.cs':scripts[match[1]]=asset.stem
    def clean(v):
        if isinstance(v,list):return[clean(x)for x in v]
        if isinstance(v,dict):
            if 'fileID'in v:
                key=str(v['fileID'])
                if v.get('guid'):return dict(guid=v['guid'],asset=str(assets_by_guid[v['guid']].relative_to(ASSETS)).replace('\\','/'))
                return dict(id=key,**bindings.get(key,{}))
            return {k:clean(x)for k,x in v.items()}
        return v
    visual=json.loads((ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json').read_text(encoding='utf-8'))
    audio_by_name={r['data']['m_Name']:r for r in visual['scene']if r['script']=='AudioObject'}
    controllers=[];items=[]
    catalog=json.loads((ROOT/'recovery/bloody/Runtime/Resources/OriginalItemCatalog.json').read_text(encoding='utf-8'))
    for key,data in values.items():
        if kinds[key]!='MonoBehaviour':continue
        script=scripts.get(data['m_Script'].get('guid'),'')
        if script in ['DummyInitializer','DummyBodyPart','ImpactSFX','ShadowMover']:
            row=dict(id=key,path=bindings[key]['path'],script=script,data=clean(data))
            if script=='ImpactSFX':
                sounds={}
                for field,ref in data.items():
                    if not isinstance(ref,dict)or not ref.get('guid'):continue
                    asset=assets_by_guid[ref['guid']]
                    if asset.suffix!='.asset':continue
                    name=documents(asset)[0]['MonoBehaviour']['m_Name']
                    if name in audio_by_name:sounds[field]=audio_by_name[name]
                row['sounds']=sounds
            controllers.append(row)
        if script=='SourcePrefab_HandItem':
            source=next(r for r in catalog['items']if r['item_id']==data['itemId']and r['source']['file']=='a87a17d047355e740a4963b8664f84ac')
            items.append(dict(path=bindings[key]['path'],record=source))
    binary=(ROOT/'output/bloody-reference-20261028/libil2cpp.so').read_bytes()
    constants={name:struct.unpack_from('<f',binary,va)[0]for name,va in [('feedback_base',0x120aa20),('shake_base',0x120b2ac),('cannon_dust_radius',0x120a758),('direction_epsilon',0x120ae8c)]}
    result=dict(source_apk_sha256=catalog['source_apk_sha256'],network_guid='a87a17d047355e740a4963b8664f84ac',controllers=controllers,items=items,constants=constants,source_bindings=bindings,spooky_implemented=False)
    dest=ROOT/'recovery/bloody/Runtime/Resources/OriginalPracticeSource.json';dest.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print(json.dumps(dict(controllers=len(controllers),bodyparts=sum(r['script']=='DummyBodyPart'for r in controllers),items=len(items),audio_objects=len(next(r for r in controllers if r['script']=='ImpactSFX')['sounds']))))


if __name__=='__main__':main()
