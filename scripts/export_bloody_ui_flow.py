"""Bind battle UI flow to original scene animators/controllers and all events.

Original controllers and clips are already imported unchanged by the recovery
project. This adds source identities and component modes; it does not generate
replacement curves or copy duplicate asset GUIDs into Resources.
"""
import hashlib
import json
from pathlib import Path
import re
import yaml

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
OUTPUT=ROOT/'recovery/bloody/Runtime/Resources/OriginalUIFlowSource.json'


def main():
    text=(ASSETS/'Game/Scenes/MainScene.unity').read_text(encoding='utf-8-sig')
    blocks={m.group(1):m.group(2)for m in re.finditer(r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)',text,re.M|re.S)}
    def field(block,key):return re.search(r'^  '+re.escape(key)+r': ([^\n]+)',block,re.M).group(1)
    def ref(block,key):return re.search(r'fileID: (\d+)',field(block,key)).group(1)
    def path(component):
        block=blocks[component];go=block if block.startswith('GameObject:')else blocks[ref(block,'m_GameObject')];name=field(go,'m_Name')
        transform=blocks[re.search(r'component: \{fileID: (\d+)\}',go).group(1)];parent=ref(transform,'m_Father')
        return(path(parent)+'/'if parent!='0'else'')+name
    roots=['CanvasAndCamera/Canvas/MainContainer/'+n for n in ['CinematicIntro','Countdown','Round','TopBar_InGame','RoundEnd','MatchEnd','PausePanel']]
    def selected(p):return any(p==r or p.startswith(r+'/')for r in roots)
    index={}
    for folder in ['AnimationClip','AnimatorController','AnimatorOverrideController']:
        for meta in(ASSETS/folder).glob('*.meta'):
            index[re.search(r'^guid: (\w+)',meta.read_text(),re.M).group(1)]=meta.with_suffix('')
    clips={};assets={};animators=[];animations=[];transitions=[];relays=[]
    def visit(guid):
        if guid in assets:return
        if guid not in index:raise ValueError(f'Original UI controller/clip missing: {guid}')
        file=index[guid];body=file.read_text(encoding='utf-8-sig')
        assets[guid]=dict(name=file.stem,path=file.relative_to(ASSETS).as_posix(),sha256=hashlib.sha256(file.read_bytes()).hexdigest())
        if file.suffix=='.anim':
            data=yaml.safe_load('\n'.join(body.splitlines()[3:]))['AnimationClip']
            clips[guid]=dict(name=data['m_Name'],events=data.get('m_Events',[]),legacy=bool(data['m_Legacy']))
        else:
            for child in re.findall(r'guid: (\w+)',body):visit(child)
    for component,block in blocks.items():
        if not block.startswith(('Animator:','Animation:','MonoBehaviour:')):continue
        p=path(component)
        if not selected(p):continue
        if block.startswith('Animator:'):
            controller=re.search(r'm_Controller: \{fileID: \d+, guid: (\w+)',block)
            if controller:visit(controller.group(1))
            animators.append(dict(id=int(component),path=p,enabled=bool(int(field(block,'m_Enabled'))),
                                  update_mode=int(field(block,'m_UpdateMode')),culling_mode=int(field(block,'m_CullingMode')),
                                  controller=controller.group(1)if controller else None))
        elif block.startswith('Animation:'):
            for guid in re.findall(r'guid: (\w+)',block):visit(guid)
            animations.append(dict(id=int(component),path=p,enabled=bool(int(field(block,'m_Enabled'))),
                                   play_automatically=bool(int(field(block,'m_PlayAutomatically'))),
                                   animate_physics=bool(int(field(block,'m_AnimatePhysics'))),
                                   update_mode=int(field(block,'m_UpdateMode'))))
        elif 'guid: 7e73fe3071f8527e8af27628d1688cab' in block:
            transitions.append(dict(id=int(component),path=p,enabled=bool(int(field(block,'m_Enabled'))),
                                    root_path=path(ref(block,'root')),animator_path=path(ref(block,'animator'))))
        elif 'guid: 3bbf2e95718d2eac197c90705ea09533' in block:
            relays.append(dict(id=int(component),path=p,enabled=bool(int(field(block,'m_Enabled')))))
    functions={e['functionName']for c in clips.values()for e in c['events']}
    assert functions=={'RelayTimeScale'},functions
    assert len(animators)>=55 and len(animations)==11,(len(animators),len(animations))
    result=dict(source_scene='Game/Scenes/MainScene.unity',scope_network_states=[2,3,4,5,6],scope_local_states=[0,10],
                animators=animators,legacy_animations=animations,transitions=transitions,time_relays=relays,
                assets=assets,clips=clips,event_functions=sorted(functions),
                enter_trigger='Enter',enter_delayed_trigger='EnterDelayed',exit_trigger='Exit')
    OUTPUT.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'Exported {len(animators)} Animators, {len(animations)} legacy Animations, {len(transitions)} transitions, {len(relays)} time relays, {len(clips)} source clips')


if __name__=='__main__':main()
