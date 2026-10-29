"""Export mounted tutorial references, button callbacks and original texts.

Reads the untouched AssetRipper scene export. No dummy method body is used.
Source scene component IDs and paths remain attached to every binding.
"""
import hashlib
import json
from pathlib import Path
import re
import shutil
import yaml

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
OUTPUT=ROOT/'recovery/bloody/Runtime/Resources/OriginalTutorialSource.json'


def main():
    scene=ASSETS/'Game/Scenes/MainScene.unity';body=scene.read_text(encoding='utf-8-sig')
    blocks={m.group(1):m.group(2)for m in re.finditer(r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)',body,re.M|re.S)}
    def field(block,key):return re.search(r'^  '+re.escape(key)+r': ([^\n]+)',block,re.M).group(1)
    def reference(block,key):return re.search(r'fileID: (\d+)',field(block,key)).group(1)
    paths={}
    def path(component):
        component=str(component)
        if component=='0':return None
        if component in paths:return paths[component]
        block=blocks[component];go=block if block.startswith('GameObject:')else blocks[reference(block,'m_GameObject')]
        name=field(go,'m_Name');transform=blocks[re.search(r'component: \{fileID: (\d+)\}',go).group(1)];parent=reference(transform,'m_Father')
        paths[component]=(path(parent)+'/'if parent!='0'else'')+name;return paths[component]
    def parse(block):return next(iter(yaml.safe_load(block).values()))
    def resolve(value):
        if isinstance(value,list):return [resolve(v)for v in value]
        if not isinstance(value,dict):return value
        if'fileID'in value:return dict(value,path=path(value['fileID'])if'guid'not in value and str(value['fileID'])in blocks else None)
        return {k:resolve(v)for k,v in value.items()}
    controller_id,controller_block=next((i,b)for i,b in blocks.items()if b.startswith('MonoBehaviour:')and'\n  tutorialContainer:'in b)
    data=resolve(parse(controller_block));controller_path=path(controller_id)
    assert controller_path=='CanvasAndCamera/Canvas/MainContainer/Round/PracticeController',controller_path
    assert len(data['tutorialScreens'])==4 and len(data['tutorialHandsCanvasGroups'])==3
    script_names={}
    for meta in(ASSETS/'Scripts').rglob('*.cs.meta'):
        match=re.search(r'^guid: (\w+)',meta.read_text(),re.M)
        if match:script_names[match.group(1)]=meta.name[:-8]
    buttons=[];texts=[];localizers=[];extra=[]
    for component,block in blocks.items():
        if not block.startswith('MonoBehaviour:'):continue
        selected=path(component).startswith(controller_path+'/')
        is_button='\n  m_OnClick:'in block and re.search(r'm_Target: \{fileID: '+controller_id+r'\}',block)
        if not selected and not is_button:continue
        parsed=parse(block);p=path(component)
        if is_button:
            calls=parsed['m_OnClick']['m_PersistentCalls']['m_Calls']
            owned=[c for c in calls if str(c['m_Target']['fileID'])==controller_id]
            buttons.append(dict(source_component=int(component),path=p,interactable=bool(parsed['m_Interactable']),calls=resolve(owned),all_persistent_calls=resolve(calls)))
        if'm_text'in parsed:texts.append(dict(source_component=int(component),path=p,text=parsed['m_text']))
        if'mTerm'in parsed:localizers.append(dict(source_component=int(component),path=p,term=parsed['mTerm'],secondary_term=parsed.get('mTermSecondary')))
        script=parsed.get('m_Script',{});name=script_names.get(script.get('guid',''))
        if name and name not in('TutorialController','UITransitionAnimator','hz','Localize'):
            extra.append(dict(source_component=int(component),path=p,script=name,data=resolve(parsed)))
    table_path=ASSETS/'Resources/I2Languages.asset'
    table=next(iter(yaml.safe_load('\n'.join(table_path.read_text(encoding='utf-8-sig').splitlines()[3:])).values()))['mSource']
    terms={r['Term']:r for r in table['mTerms']};selected_terms={}
    for localizer in localizers:
        term=localizer['term']
        if not term:continue
        if term not in terms:raise ValueError('Original tutorial localization term missing: '+term)
        row=terms[term];assert len(row['Languages'])==len(table['mLanguages'])
        selected_terms[term]=row['Languages']
    groups=[]
    for ref in data['tutorialHandsCanvasGroups']:
        raw=parse(blocks[str(ref['fileID'])]);groups.append(dict(reference=ref,alpha=raw['m_Alpha'],blocks_raycasts=bool(raw['m_BlocksRaycasts']),interactable=bool(raw['m_Interactable'])))
    initial_active={path(i):bool(int(field(b,'m_IsActive')))for i,b in blocks.items()if b.startswith('GameObject:')and path(i).startswith(controller_path)}
    audio_index={}
    for folder in('AudioClip','MonoBehaviour'):
        for meta in(ASSETS/folder).glob('*.meta'):
            guid=re.search(r'^guid: (\w+)',meta.read_text(),re.M).group(1);audio_index[guid]=meta.with_suffix('')
    audio={};audio_objects={};popups=[];destination=OUTPUT.parent/'OriginalTutorial';destination.mkdir(exist_ok=True)
    def audio_reference(ref):
        if not ref.get('fileID'):return dict(kind='null')
        guid=ref['guid'];file=audio_index[guid]
        if file.suffix=='.asset':
            raw=parse('\n'.join(file.read_text(encoding='utf-8-sig').splitlines()[3:]));assert script_names[raw['m_Script']['guid']]=='AudioObject'
            if guid not in audio_objects:audio_objects[guid]={k:[audio_reference(v)for v in value]if k=='audioClip'else value for k,value in raw.items()}
            return dict(kind='audio_object',key=guid)
        assert file.suffix in('.ogg','.wav','.mp3','.aiff'),file
        audio[guid]=dict(name=file.stem,resource='OriginalTutorial/'+guid,source_path=file.relative_to(ASSETS).as_posix(),sha256=hashlib.sha256(file.read_bytes()).hexdigest());shutil.copy2(file,destination/(guid+file.suffix));return dict(kind='audio',key=guid)
    for record in extra:
        if record['script']=='PopupSFXRelayer':popups.append(dict(path=record['path'],source_component=record['source_component'],open=audio_reference(record['data']['openSFXType']),close=audio_reference(record['data']['closeSFXType'])))
    uisfx_guid=next(guid for guid,name in script_names.items()if name=='UISFX')
    uisfx=parse(next(b for b in blocks.values()if b.startswith('MonoBehaviour:')and'guid: '+uisfx_guid in b))
    source_ref=uisfx['audioSourceDefault'];audio_id=str(source_ref['fileID']);audio_data=parse(blocks[audio_id]);audio_go=parse(blocks[str(audio_data['m_GameObject']['fileID'])]);same=[str(c['component']['fileID'])for c in audio_go['m_Component']if blocks[str(c['component']['fileID'])].startswith('AudioSource:')]
    default_audio=dict(source_component=int(audio_id),path=path(audio_id),ordinal=same.index(audio_id))
    result=dict(source_scene=scene.relative_to(ASSETS).as_posix(),source_scene_sha256=hashlib.sha256(scene.read_bytes()).hexdigest(),controller=dict(source_component=int(controller_id),path=controller_path,data=data),buttons=buttons,hands_canvas_groups=groups,texts=texts,localizers=localizers,languages=table['mLanguages'],terms=selected_terms,localization_source_sha256=hashlib.sha256(table_path.read_bytes()).hexdigest(),initial_active=initial_active,auxiliary_behaviours=extra,popup_sfx=popups,audio=audio,audio_objects=audio_objects,uisfx_default=default_audio)
    OUTPUT.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(controller_id=int(controller_id),screens=[r['path']for r in data['tutorialScreens']],groups=len(groups),buttons=[dict(path=r['path'],methods=[c['m_MethodName']for c in r['calls']])for r in buttons],texts=len(texts),terms=len(selected_terms),audio_objects=len(audio_objects),audio=len(audio),auxiliary_scripts=sorted(set(r['script']for r in extra))),ensure_ascii=False))


if __name__=='__main__':main()
