"""Extract the original UI animation events that control combat time scale.

This retains event order, including different values at the same source time.
It does not substitute a guessed pause or move AI's scaled startup invokes.
"""
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
OUTPUT = ROOT / 'recovery/bloody/Runtime/Resources/OriginalTimeScaleSource.json'


def main():
    scene = (ASSETS / 'Game/Scenes/MainScene.unity').read_text(encoding='utf-8-sig')
    blocks = {m.group(1): m.group(2) for m in re.finditer(
        r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)', scene, re.M | re.S)}
    views = {}
    for go_id in ['382', '450', '168', '291', '479']:
        go = blocks[go_id]
        name = re.search(r'^  m_Name: (.*)$', go, re.M).group(1)
        components = re.findall(r'component: \{fileID: (\d+)\}', go)
        animator_id = next(c for c in components if blocks[c].startswith('Animator:'))
        animator = blocks[animator_id]
        relay_id = next(c for c in components
                        if 'guid: 3bbf2e95718d2eac197c90705ea09533' in blocks[c])
        views[name] = dict(game_object_id=int(go_id), animator_id=int(animator_id),
                           update_mode=int(re.search(r'm_UpdateMode: (\d+)', animator).group(1)),
                           time_relay_id=int(relay_id))
    clips = {}
    for source in sorted((ASSETS / 'AnimationClip').glob('*.anim')):
        text = source.read_text(encoding='utf-8-sig')
        if 'functionName: RelayTimeScale' not in text:
            continue
        events = []
        for event in text.split('  - time: ')[1:]:
            if 'functionName: RelayTimeScale' in event:
                events.append(dict(time=float(event.splitlines()[0]),
                                   value=float(re.search(r'floatParameter: ([^\n]+)', event).group(1))))
        clips[source.stem] = dict(source=str(source.relative_to(ROOT)).replace('\\', '/'), events=events)
    assert len(clips) == 7
    assert views['CinematicIntro']['update_mode'] == 2
    assert views['Countdown']['update_mode'] == 0
    result = dict(source_scene='Game/Scenes/MainScene.unity',
                  source_time_controller=dict(type='hy', on_enable_va='0x3443788',
                                              phase_handler_va='0x34434f4', set_time_scale_va='0x3442fb4'),
                  source_time_relay=dict(type='hz', relay_time_scale_va='0x3443ba0'),
                  phase_resets=dict(states=[3, 4], value=1), views=views, clips=clips)
    OUTPUT.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'Exported {len(clips)} time-scale clip timelines and {len(views)} source view bindings')


if __name__ == '__main__':
    main()
