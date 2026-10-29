"""Verify original popup assets, scene references and curve/event bindings."""
import json
from pathlib import Path
import re
import yaml

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
RESOURCES=ROOT/'recovery/bloody/Runtime/Resources'


def main():
    data=json.loads((RESOURCES/'OriginalUIPopupSource.json').read_text(encoding='utf-8'))
    text=(ASSETS/'Game/Scenes/MainScene.unity').read_text(encoding='utf-8-sig')
    blocks={m.group(1):m.group(2)for m in re.finditer(r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)',text,re.M|re.S)}
    cache={};paths={}
    def raw(ident):
        ident=str(ident)
        if ident not in cache:cache[ident]=next(iter(yaml.safe_load(blocks[ident]).values()))
        return cache[ident]
    def path(ident):
        ident=str(ident)
        if ident in paths:return paths[ident]
        obj=raw(ident)
        if'm_GameObject'in obj:obj=raw(obj['m_GameObject']['fileID'])
        transform=raw(obj['m_Component'][0]['component']['fileID']);parent=transform['m_Father']['fileID']
        value=(path(parent)+'/'if parent else'')+obj['m_Name'];paths[ident]=value;return value
    scene_paths={path(i)for i,b in blocks.items()if b.startswith('GameObject:')}
    checks=0;curve_bindings=0;scene_refs=0;events=0;unbound=[]
    def check(condition,message):
        nonlocal checks
        checks+=1
        if not condition:raise AssertionError(message)
    def visit(value):
        nonlocal scene_refs
        if isinstance(value,list):
            for x in value:visit(x)
        elif isinstance(value,dict):
            if value.get('kind')=='scene':
                scene_refs+=1;check(value['path']in scene_paths,'Scene reference missing '+value['path']);check(value['path']==path(value['id']),'Scene reference changed')
            for x in value.values():visit(x)
    visit(data['records'])
    visit(data['views'])
    driver=(ROOT/'recovery/bloody/Runtime/OriginalUIPopupDriver.cs').read_text(encoding='utf-8')
    for key,clip in data['clips'].items():
        source=ASSETS/clip['source_path'];export=RESOURCES/(clip['resource']+'.anim')
        check(source.read_bytes()==export.read_bytes(),'Original clip bytes changed: '+clip['name'])
        doc=next(iter(yaml.safe_load('\n'.join(export.read_text(encoding='utf-8').splitlines()[3:])).values()))
        owners=[r['path']for r in data['records']if any(v==dict(kind='clip',key=key)for v in r['data'].values())]
        check(bool(owners),'Clip has no original owner: '+clip['name'])
        for group in ('m_RotationCurves','m_CompressedRotationCurves','m_EulerCurves','m_PositionCurves','m_ScaleCurves','m_FloatCurves','m_PPtrCurves'):
            for curve in doc.get(group,[]):
                for owner in owners:
                    binding=owner+('/'+curve['path']if curve.get('path')else'');curve_bindings+=1
                    if binding not in scene_paths:unbound.append(dict(clip=clip['name'],group=group,path=binding))
        for event in doc.get('m_Events',[]):
            events+=1;check(event['objectReferenceParameter']['fileID']==0,'Stale event object reference')
            check('public void '+event['functionName']+'('in driver,'Event receiver missing '+event['functionName'])
    for key,audio in data['audio'].items():
        source=ASSETS/audio['source_path'];copied=next(RESOURCES.glob(audio['resource']+'.*'));check(source.read_bytes()==copied.read_bytes(),'Original audio bytes changed')
    check(len(data['records'])==10,'Expected ten mounted hosts');check(len(data['clips'])==11,'Expected eleven original clips')
    result=dict(checks=checks,popup_hosts=len(data['records']),source_clips=len(data['clips']),source_audio=len(data['audio']),scene_references=scene_refs,curve_bindings=curve_bindings,source_unbound_curve_bindings=unbound,animation_events=events,failures=0)
    report=ROOT/'output/bloody-full-port-20261028/ai-check/ui/popup-assets-verification.json';report.write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result))


if __name__=='__main__':main()
