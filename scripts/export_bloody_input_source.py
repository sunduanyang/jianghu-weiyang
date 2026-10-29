"""Extract the actually mounted touch controls and their runtime canvas rules."""
import json
from pathlib import Path
import re
import struct
from elftools.elf.elffile import ELFFile

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'


def main():
    text = (ASSETS/'Game/Scenes/MainScene.unity').read_text(encoding='utf-8-sig')
    blocks = {m.group(1): m.group(2) for m in re.finditer(
        r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S)}

    def value(block, key):
        return re.search(r'^  '+re.escape(key)+r': ([^\n]+)', block, re.M).group(1)

    def ref(block, key):
        return re.search(r'fileID: (\d+)', value(block, key)).group(1)

    def vec(block, key):
        return {k: float(v) for k, v in re.findall(r'(\w+): ([^,}]+)', value(block, key))}

    def path(component):
        block = blocks[component]
        go = blocks[ref(block, 'm_GameObject')]
        name = value(go, 'm_Name')
        transform = blocks[re.search(r'component: \{fileID: (\d+)\}', go).group(1)]
        parent = ref(transform, 'm_Father')
        return (path(parent)+'/' if parent != '0' else '')+name

    def node(component):
        return dict(id=int(component), path=path(component))

    touch = blocks['19964']
    assert 'bd25a9ae058da07f996d12af9a20bb1d' in touch
    views = {}
    for screen_side, component, nub_field in [('screen_left', '18123', 'NubRight'),
                                               ('screen_right', '18121', 'NubLeft')]:
        block = blocks[component]
        assert 'c513580a514bcde96305d1334cba883c' in block
        background, handle = ref(block, 'background'), ref(block, 'handle')
        assert ref(touch, nub_field) == handle
        views[screen_side] = dict(source=node(component), native_nub_field=nub_field,
                                  background=node(background), handle=node(handle),
                                  background_size=vec(blocks[background], 'm_SizeDelta'),
                                  handle_range=float(value(block, 'handleRange')),
                                  dead_zone=float(value(block, 'deadZone')),
                                  axis_options=int(value(block, 'axisOptions')),
                                  snap_x=bool(int(value(block, 'snapX'))), snap_y=bool(int(value(block, 'snapY'))),
                                  move_threshold=float(value(block, 'moveThreshold')))
    scaler, tablet = blocks['21039'], blocks['18789']
    constants = {}
    with (ROOT/'output/bloody-reference-20261028/libil2cpp.so').open('rb') as stream:
        elf = ELFFile(stream)
        segments = [s for s in elf.iter_segments() if s['p_type'] == 'PT_LOAD']
        for key, address in [('pc_width', 0x120a7c0), ('tablet_width', 0x120b2f8), ('aspect_threshold', 0x120aff0)]:
            segment = next(s for s in segments if s['p_vaddr'] <= address < s['p_vaddr']+s['p_filesz'])
            stream.seek(segment['p_offset']+address-segment['p_vaddr'])
            constants[key] = struct.unpack('<f', stream.read(4))[0]
    options = (ASSETS/'MonoBehaviour/OptionsData.asset').read_text(encoding='utf-8-sig')
    limits = {float(value((ASSETS/f'GameObject/Character_Player_{index}.prefab').read_text(encoding='utf-8-sig'),
                          'maxNubMagnitude')) for index in (1, 2)}
    assert limits == {155.0}, limits
    result = dict(source_scene='Game/Scenes/MainScene.unity', controls=node('19964'), views=views,
                  canvas=dict(source=node('10937'), scaler=node('21039'),
                              scale_mode=int(value(scaler, 'm_UiScaleMode')),
                              reference_resolution=vec(scaler, 'm_ReferenceResolution'),
                              screen_match_mode=int(value(scaler, 'm_ScreenMatchMode')),
                              match_width_or_height=float(value(scaler, 'm_MatchWidthOrHeight'))),
                  tablet_canvas=dict(source=node('18789'), pc_mode=bool(int(value(tablet, 'pcModeOn'))),
                                     horizontal_layout_group=node(ref(tablet, 'horizontalLayoutGroup')),
                                     bottom_offset_elements=[node(c) for c in re.findall(r'- \{fileID: (\d+)\}', tablet)],
                                     **constants, standard_width=1920,
                                     pc_padding=dict(left=-750, right=-750, top=100, bottom=100, offset_y=250),
                                     tablet_padding=dict(left=-250, right=-250, top=120, bottom=150, offset_y=0),
                                     standard_padding=dict(left=0, right=0, top=0, bottom=0, offset_y=50),
                                     native_apply_va='0x36aefb8'),
                  options=dict(visible=bool(int(value(options, 'VisibleJoysticks'))),
                               fixed=bool(int(value(options, 'FixedJoysticks'))), fixed_threshold=1000),
                  network=dict(nub_quantization=1024, maximum_magnitude=next(iter(limits))))
    destination = ROOT/'recovery/bloody/Runtime/Resources/OriginalInputSource.json'
    destination.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print('Exported mounted two-joystick controls, source bindings, options, canvas and TabletModeCanvas rules')


if __name__ == '__main__':
    main()
