"""Export original mounted TransitionController ownership and timing data."""
import hashlib
import json
from pathlib import Path
import re
import yaml

ROOT=Path(__file__).resolve().parents[1];ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
OUTPUT=ROOT/'recovery/bloody/Runtime/Resources/OriginalArenaTransitionSource.json'


def main():
    scene=ASSETS/'Game/Scenes/MainScene.unity';body=scene.read_text(encoding='utf-8-sig')
    blocks={m.group(1):m.group(2)for m in re.finditer(r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)',body,re.M|re.S)}
    def field(block,key):return re.search(r'^  '+re.escape(key)+r': ([^\n]+)',block,re.M).group(1)
    def ref(block,key):return re.search(r'fileID: (\d+)',field(block,key)).group(1)
    def path(component):
        block=blocks[str(component)];go=block if block.startswith('GameObject:')else blocks[ref(block,'m_GameObject')]
        tr=blocks[re.search(r'component: \{fileID: (\d+)\}',go).group(1)];parent=ref(tr,'m_Father')
        return(path(parent)+'/'if parent!='0'else'')+field(go,'m_Name')
    component,block=next((i,b)for i,b in blocks.items()if'\n  menuObjects:'in b and'\n  arenaObjects:'in b)
    data=next(iter(yaml.safe_load(block).values()))
    result=dict(source_apk_sha256='c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba',source_scene=scene.relative_to(ASSETS).as_posix(),source_scene_sha256=hashlib.sha256(scene.read_bytes()).hexdigest(),
                source_component=int(component),path=path(component),data=data,cover_delay=data['coverDelay'],network_controller_spawn_timeout=data['ngcSpawnTimeout'],
                menu_objects=[dict(reference=r,path=path(r['fileID']))for r in data['menuObjects']],arena_objects=[dict(reference=r,path=path(r['fileID']))for r in data['arenaObjects']],
                enum_notes='No Loading/Preparing value exists in original LocalGameState or NetworkedGameState. The intermediate lifecycle is cover -> runner teardown/event -> context switch -> new runner state events.',
                source_ctor_defaults=dict(cover_delay=.25,network_controller_spawn_timeout=15),
                source_native=dict(arena_move_next='0x368a3f8',menu_move_next='0x368a9a8',arena_context='0x368a0c4',menu_context='0x368a1d4',interstitial_predicate='0x368a364',runner_destroyed_ui='0x3416da8'))
    assert result['cover_delay']==.35 and result['network_controller_spawn_timeout']==15
    OUTPUT.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print(json.dumps({k:result[k]for k in('path','cover_delay','network_controller_spawn_timeout','menu_objects','arena_objects')},ensure_ascii=False))


if __name__=='__main__':main()
