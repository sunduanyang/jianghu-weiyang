"""Export mounted TransitionView and byte-identical clips/audio resources."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import yaml

ROOT=Path(__file__).resolve().parents[1];ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
OUTPUT=ROOT/'recovery/bloody/Runtime/Resources/OriginalTransitionViewSource.json'


def main():
    text=(ASSETS/'Game/Scenes/MainScene.unity').read_text(encoding='utf-8-sig');blocks={m.group(1):m.group(2)for m in re.finditer(r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)',text,re.M|re.S)}
    def field(block,key):return re.search(r'^  '+re.escape(key)+r': ([^\n]+)',block,re.M).group(1)
    def ref(block,key):return re.search(r'fileID: (\d+)',field(block,key)).group(1)
    def path(component):
        block=blocks[str(component)];go=block if block.startswith('GameObject:')else blocks[ref(block,'m_GameObject')];tr=blocks[re.search(r'component: \{fileID: (\d+)\}',go).group(1)];parent=ref(tr,'m_Father')
        return(path(parent)+'/'if parent!='0'else'')+field(go,'m_Name')
    def parse(block):return next(iter(yaml.safe_load(block).values()))
    def resolve(value):
        if isinstance(value,list):return[resolve(v)for v in value]
        if not isinstance(value,dict):return value
        if'fileID'in value:return dict(value,path=path(value['fileID'])if'guid'not in value and str(value['fileID'])in blocks else None)
        return{k:resolve(v)for k,v in value.items()}
    cid,block=next((i,b)for i,b in blocks.items()if'\n  panelsSlideIn:'in b);data=parse(block);host=blocks[str(data['m_GameObject']['fileID'])];hostpath=path(cid)
    index={}
    for folder in('AnimationClip','AudioClip','MonoBehaviour'):
        for meta in(ASSETS/folder).glob('*.meta'):
            g=re.search(r'^guid: (\w+)',meta.read_text(),re.M).group(1);index[g]=meta.with_suffix('')
    destination=OUTPUT.parent/'OriginalTransitionView';destination.mkdir(exist_ok=True)
    clips={};audio={};objects={}
    def copy(file):
        target=destination/file.name;shutil.copyfile(file,target);assert file.read_bytes()==target.read_bytes()
        return dict(name=file.stem,source=file.relative_to(ASSETS).as_posix(),resource='OriginalTransitionView/'+file.stem,sha256=hashlib.sha256(file.read_bytes()).hexdigest())
    for key in('initialize','slideIn','slideOut'):
        file=index[data[key]['guid']];record=copy(file);raw=parse('\n'.join(file.read_text(encoding='utf-8-sig').splitlines()[3:]))
        record.update(events=raw.get('m_Events',[]),duration=raw['m_AnimationClipSettings']['m_StopTime'],legacy=bool(raw['m_Legacy']),curves=[dict(path=c.get('path'),attribute=c['attribute'],class_id=c['classID'],script=c.get('script'),keys=c['curve']['m_Curve'])for c in raw.get('m_FloatCurves',[])])
        clips[key]=record
    for key in('panelsSlideIn','panelsUnlock','panelsSlideOut','panelsContact'):
        file=index[data[key]['guid']];raw=parse('\n'.join(file.read_text(encoding='utf-8-sig').splitlines()[3:]));row={k:raw[k]for k in('m_Name','loop','pitch','volume')};row['clips']=[]
        for reference in raw['audioClip']:
            guid=reference['guid'];audio.setdefault(guid,copy(index[guid]));row['clips'].append(guid)
        row['source']=file.relative_to(ASSETS).as_posix();objects[key]=row
    animancer=resolve(parse(blocks[str(data['animancer']['fileID'])]));animator=resolve(parse(blocks[str(animancer['_Animator']['fileID'])]));components=[]
    for r in parse(host)['m_Component']:
        component=str(r['component']['fileID']);components.append(dict(id=int(component),kind=blocks[component].split(':',1)[0],data=resolve(parse(blocks[component]))))
    node_paths=[path(i)for i,b in blocks.items()if b.startswith('GameObject:')and(path(i)==hostpath or path(i).startswith(hostpath+'/'))]
    result=dict(source_apk_sha256='c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba',source_component=int(cid),path=hostpath,source_active=bool(int(field(host,'m_IsActive'))),data=resolve(data),animancer=animancer,animator=animator,host_components=components,source_node_paths=node_paths,clips=clips,audio=audio,audio_objects=objects,
                initialize_usage='Retained source field and clip. None of the nine source methods plays initialize; Attach does not fabricate that call.',audio_rng_order=['pitch_float_range','clip_int_range','volume_float_range','PlayOneShot_volumeScale'],source_play_fade=dict(duration=0,mode=2))
    assert result['path']=='CanvasAndCamera/Canvas/MainContainer/TransitionView'and result['source_active']
    assert animator['m_UpdateMode']==2 and len(audio)==6
    assert {e['functionName']for r in clips.values()for e in r['events']}=={'PlaySlideIn','PlayContact','PlaySlideOut'}
    OUTPUT.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print(json.dumps(dict(path=hostpath,active=result['source_active'],clips={k:dict(name=v['name'],duration=v['duration'],events=[e['functionName']for e in v['events']])for k,v in clips.items()},audio_clips=len(audio)),ensure_ascii=False))


if __name__=='__main__':main()
