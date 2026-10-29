"""Export the ten original verdict Animancer hosts and their dependencies."""
import json
from pathlib import Path
import re
import shutil
import yaml

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
TARGET=ROOT/'recovery/bloody/Runtime/Resources'
TYPES={'LostPopup','LostPopupRanked','WonPopupRandom','WonPopupCampaign','WonPopupRanked','WonPopupSurvival'}


def main():
    text=(ASSETS/'Game/Scenes/MainScene.unity').read_text(encoding='utf-8-sig')
    blocks={m.group(1):m.group(2)for m in re.finditer(r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)',text,re.M|re.S)}
    scripts={re.search(r'^guid: (\w+)',f.read_text(),re.M).group(1):f.name[:-8]for f in(ASSETS/'Scripts/Assembly-CSharp').glob('*.cs.meta')}
    def parse(block):return next(iter(yaml.safe_load(block).values()))
    def object_path(component):
        block=blocks[str(component)];go=block if block.startswith('GameObject:')else blocks[str(parse(block)['m_GameObject']['fileID'])]
        data=parse(go);name=data['m_Name'];transform=parse(blocks[str(data['m_Component'][0]['component']['fileID'])]);p=transform['m_Father']['fileID']
        return(object_path(p)+'/'if p else'')+name
    index={}
    for folder in ['AnimationClip','AudioClip','MonoBehaviour']:
        for meta in(ASSETS/folder).glob('*.meta'):index[re.search(r'^guid: (\w+)',meta.read_text(),re.M).group(1)]=meta.with_suffix('')
    clips={};audio={};audio_objects={};records=[];views={}
    destination=TARGET/'OriginalUIPopups';destination.mkdir(exist_ok=True)
    def collect(guid):
        file=index[guid];resource='OriginalUIPopups/'+guid
        if file.suffix=='.anim':
            data=parse('\n'.join(file.read_text().splitlines()[3:]));clips[guid]=dict(name=data['m_Name'],resource=resource,events=data.get('m_Events',[]),source_path=file.relative_to(ASSETS).as_posix())
            shutil.copy2(file,destination/(guid+file.suffix));return dict(kind='clip',key=guid)
        if file.suffix in ('.ogg','.wav','.mp3','.aiff'):
            audio[guid]=dict(name=file.stem,resource=resource,source_path=file.relative_to(ASSETS).as_posix());shutil.copy2(file,destination/(guid+file.suffix));return dict(kind='audio',key=guid)
        if file.suffix=='.asset':
            data=parse('\n'.join(file.read_text().splitlines()[3:]));name=scripts.get(data.get('m_Script',{}).get('guid'))
            if name=='AudioObject':
                if guid not in audio_objects:audio_objects[guid]=resolve(data)
                return dict(kind='audio_object',key=guid)
        return dict(kind='external',guid=guid)
    def resolve(v):
        if isinstance(v,list):return[resolve(x)for x in v]
        if not isinstance(v,dict):return v
        if'fileID'in v:
            if not v['fileID']:return dict(kind='null')
            if'guid'in v:return collect(v['guid'])if v['guid']in index else v
            component=str(v['fileID']);block=blocks[component];type_name=block.split(':',1)[0]
            if type_name=='MonoBehaviour'and '_Animator:'in block:
                raw=parse(block);animator=resolve(raw['_Animator']);animator['update_mode']=parse(blocks[str(raw['_Animator']['fileID'])])['m_UpdateMode']
                return dict(kind='animancer',id=int(component),path=object_path(component),animator=animator,action_on_disable=raw['_ActionOnDisable'])
            result=dict(kind='scene',id=int(component),path=object_path(component),type=type_name)
            if type_name!='GameObject':
                go=parse(blocks[str(parse(block)['m_GameObject']['fileID'])]);same=[str(c['component']['fileID'])for c in go['m_Component']if blocks[str(c['component']['fileID'])].startswith(type_name+':')];result['ordinal']=same.index(component)
            return result
        return{k:resolve(x)if k!='m_Script'else x for k,x in v.items()}
    for component,block in blocks.items():
        if not block.startswith('MonoBehaviour:'):continue
        match=re.search(r'm_Script: \{fileID: -?\d+, guid: (\w+)',block)
        script=scripts.get(match.group(1))if match else None
        if script in('RoundEndView','MatchEndView'):
            views[script]=resolve(parse(block));continue
        if script not in TYPES:continue
        raw=parse(block);record=dict(id=int(component),path=object_path(component),script=script,data=resolve(raw))
        go=parse(blocks[str(raw['m_GameObject']['fileID'])]);enable_events=[]
        for sibling in go['m_Component']:
            sibling_block=blocks[str(sibling['component']['fileID'])]
            if not sibling_block.startswith('MonoBehaviour:'):continue
            sibling_raw=parse(sibling_block)
            if scripts.get(sibling_raw.get('m_Script',{}).get('guid'))!='FireEventOnEnable' or not sibling_raw.get('m_Enabled'):continue
            for call in sibling_raw['FireOnEnable']['m_PersistentCalls']['m_Calls']:
                assert call['m_MethodName']=='SetActive'and call['m_Mode']==6 and call['m_CallState']==2,call
                enable_events.append(dict(target=resolve(call['m_Target']),active=call['m_Arguments']['m_BoolArgument']))
            assert not sibling_raw['FireOnDisable']['m_PersistentCalls']['m_Calls']
        record['enable_events']=enable_events;records.append(record)
    assert len(records)==10,len(records)
    event_functions=sorted({e['functionName']for c in clips.values()for e in c['events']})
    refs=[dict(clip=c['name'],event=e)for c in clips.values()for e in c['events']if e.get('objectReferenceParameter',{}).get('fileID',0)]
    result=dict(records=records,views=views,clips=clips,audio=audio,audio_objects=audio_objects,event_functions=event_functions,nonzero_event_object_references=refs)
    (TARGET/'OriginalUIPopupSource.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(popups=len(records),clips=len(clips),audio=len(audio),audio_objects=len(audio_objects),events=event_functions,object_references=refs),ensure_ascii=False))


if __name__=='__main__':main()
