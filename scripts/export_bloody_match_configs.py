"""Retain actual ScriptableObject profiles and campaign equipment pools."""
import json
import re
from pathlib import Path
import yaml


def documents(path):
    text=path.read_text(encoding='utf-8-sig')
    text=re.sub(r'^%[^\n]*\n','',text,flags=re.M)
    text=re.sub(r'^--- !u!\d+ &(-?\d+)(?: stripped)?\s*$',r'---\n__file_id: "\1"',text,flags=re.M)
    return list(yaml.load_all(text,Loader=yaml.CSafeLoader))


def main():
    root=Path(__file__).resolve().parents[1]
    assets=root/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
    profiles={}
    for path in assets.rglob('CharacterConfigDataSO_*.asset'):
        data=documents(path)[0]['MonoBehaviour']
        profiles[data['m_Name']]=dict(path=str(path.relative_to(assets)).replace('\\','/'),data=data['Data'])
    # Only prefab references are needed for the original bulk slot arrays.
    by_guid={}
    for path in assets.rglob('*.prefab.meta'):
        match=re.search(r'^guid: (\w+)',path.read_text(),re.M)
        if match:by_guid[match[1]]=Path(str(path)[:-5])
    prefab_cache={}
    def prefab(pointer):
        guid=pointer.get('guid')
        if not guid:return dict(item_id=0,name='None')
        if guid in prefab_cache:return prefab_cache[guid]
        path=by_guid[guid];docs=documents(path)
        root_go=next(v['Transform']['m_GameObject']['fileID']for v in docs
            if 'Transform'in v and v['Transform']['m_Father']['fileID']==0)
        data=next(v['MonoBehaviour']for v in docs if 'MonoBehaviour'in v and 'itemId'in v['MonoBehaviour']
            and v['MonoBehaviour']['m_GameObject']['fileID']==root_go)
        result=dict(item_id=data['itemId'],name=path.stem,path=str(path.relative_to(assets)).replace('\\','/'))
        prefab_cache[guid]=result;return result
    bulk={}
    for path in (assets/'MonoBehaviour').glob('EnemyBulkConfig_*.asset'):
        data=documents(path)[0]['MonoBehaviour']
        clean={k:v for k,v in data.items()if not k.startswith('m_')}
        for key,value in list(clean.items()):
            if isinstance(value,list)and(key.endswith('Items')or key.startswith('SingleHandedItems')):clean[key]=[prefab(p)for p in value]
        bulk[data['m_Name']]=dict(path=str(path.relative_to(assets)).replace('\\','/'),data=clean)
    destination=root/'recovery/bloody/Runtime/Resources/OriginalMatchConfigs.json'
    destination.write_text(json.dumps(dict(profiles=profiles,enemy_bulk=bulk),ensure_ascii=False,separators=(',',':')),encoding='utf8')
    print(json.dumps(dict(profiles=len(profiles),enemy_bulk=len(bulk),prefab_references=len(prefab_cache))))


if __name__=='__main__':main()
