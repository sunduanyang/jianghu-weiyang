"""Export the mounted CountdownView and UISFX dependency configuration."""
import json
from pathlib import Path
import re
import shutil
import yaml

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
TARGET=ROOT/'recovery/bloody/Runtime/Resources'


def main():
    text=(ASSETS/'Game/Scenes/MainScene.unity').read_text(encoding='utf-8-sig')
    blocks={m.group(1):m.group(2)for m in re.finditer(r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)',text,re.M|re.S)}
    scripts={re.search(r'^guid: (\w+)',f.read_text(),re.M).group(1):f.name[:-8]for f in(ASSETS/'Scripts/Assembly-CSharp').glob('*.cs.meta')}
    cache={}
    def raw(ident):
        ident=str(ident)
        if ident not in cache:cache[ident]=next(iter(yaml.safe_load(blocks[ident]).values()))
        return cache[ident]
    def path(ident):
        obj=raw(ident)
        if'm_GameObject'in obj:obj=raw(obj['m_GameObject']['fileID'])
        transform=raw(obj['m_Component'][0]['component']['fileID']);parent=transform['m_Father']['fileID']
        return(path(parent)+'/'if parent else'')+obj['m_Name']
    index={}
    for folder in('AudioClip','MonoBehaviour'):
        for meta in(ASSETS/folder).glob('*.meta'):index[re.search(r'^guid: (\w+)',meta.read_text(),re.M).group(1)]=meta.with_suffix('')
    audio={};objects={};records={};destination=TARGET/'OriginalCountdown';destination.mkdir(exist_ok=True)
    def collect(guid):
        source=index[guid]
        if source.suffix=='.asset':
            doc=next(iter(yaml.safe_load('\n'.join(source.read_text(encoding='utf-8').splitlines()[3:])).values()))
            assert scripts.get(doc['m_Script']['guid'])=='AudioObject'
            if guid not in objects:objects[guid]=resolve(doc)
            return dict(kind='audio_object',key=guid)
        resource='OriginalCountdown/'+guid
        audio[guid]=dict(name=source.stem,resource=resource,source_path=source.relative_to(ASSETS).as_posix())
        shutil.copy2(source,destination/(guid+source.suffix));return dict(kind='audio',key=guid)
    def resolve(value):
        if isinstance(value,list):return[resolve(x)for x in value]
        if not isinstance(value,dict):return value
        if'fileID'in value:
            if not value['fileID']:return dict(kind='null')
            if'guid'in value:return collect(value['guid'])if value['guid']in index else value
            ident=str(value['fileID']);kind=blocks[ident].split(':',1)[0];result=dict(kind='scene',id=int(ident),path=path(ident),type=kind)
            if kind!='GameObject':
                go=raw(raw(ident)['m_GameObject']['fileID']);same=[str(c['component']['fileID'])for c in go['m_Component']if blocks[str(c['component']['fileID'])].startswith(kind+':')];result['ordinal']=same.index(ident)
            return result
        return{k:resolve(v)if k!='m_Script'else v for k,v in value.items()}
    for ident,block in blocks.items():
        if not block.startswith('MonoBehaviour:'):continue
        match=re.search(r'm_Script: \{fileID: -?\d+, guid: (\w+)',block);kind=scripts.get(match.group(1))if match else None
        if kind in('CountdownView','UISFX'):
            assert kind not in records;records[kind]=dict(path=path(ident),data=resolve(raw(ident)))
    assert len(records)==2
    result=dict(records=records,audio=audio,audio_objects=objects)
    (TARGET/'OriginalCountdownSource.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(audio=len(audio),audio_objects=len(objects),records=list(records))))


if __name__=='__main__':main()
