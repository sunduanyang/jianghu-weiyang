"""Read-only kick/AI evidence from a user-supplied Unity APK extraction.

Prepare --serialized and --dlls using inspect_bloody_reference.py / Cpp2IL.
This exports local inspection data only, never installs or executes the APK.
Requires UnityPy. Optional native listings also require capstone.
"""
import argparse
import hashlib
import json
from pathlib import Path

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator


PREFAB = '00af04d95140a4b44ac8453d91c032ea'


def inspect(args):
    args.output.mkdir(parents=True, exist_ok=True)
    env = UnityPy.load(str(args.serialized))
    prefab = next(f for f in env.files.values() if f.name == PREFAB)
    generator = TypeTreeGenerator('6000.4.0f1')
    generator.load_local_dll_folder(str(args.dlls))
    components = []
    for obj in prefab.objects.values():
        if obj.type.name != 'MonoBehaviour':
            continue
        script = obj.read(check_read=False).m_Script.read()
        if script.m_ClassName not in {'CharacterMover', 'KickReceiver'}:
            continue
        data = obj.read_typetree(nodes=generator.get_nodes_up(script.m_AssemblyName, script.m_ClassName))
        header = obj.read_typetree(check_read=False)
        for key in ('m_GameObject', 'm_Enabled', 'm_Script', 'm_Name'):
            data[key] = header[key]
        components.append(dict(id=obj.path_id, script=script.m_ClassName, data=data))
    mover = next(c['data'] for c in components if c['script'] == 'CharacterMover')
    drivers = []
    for key in ('handLeftCOMRigidbody2D', 'handRightCOMRigidbody2D',
                'footLeftCOMRigidbody2D', 'footRightCOMRigidbody2D'):
        body = prefab.objects[mover[key]['m_PathID']].read_typetree()
        go = prefab.objects[body['m_GameObject']['m_PathID']].read_typetree()
        shapes = [prefab.objects[c['component']['m_PathID']].read_typetree()
                  for c in go['m_Component']
                  if 'Collider2D' in prefab.objects[c['component']['m_PathID']].type.name]
        drivers.append(dict(key=key, name=go['m_Name'], layer=go['m_Layer'], body=body, shapes=shapes))
    effector = prefab.objects[mover['kickEffector']['m_PathID']].read_typetree()
    effector_shapes = [prefab.objects[c['component']['m_PathID']].read_typetree()
                       for c in effector['m_Component']
                       if 'Collider2D' in prefab.objects[c['component']['m_PathID']].type.name]
    animation = prefab.objects[mover['kickAnimation']['m_PathID']].read().m_Animation.read()
    clip = animation.object_reader.read_typetree()
    curves = [dict(path=c['path'], keys=[dict(time=k['time'], value=k['value'])
               for k in c['curve']['m_Curve']]) for c in clip.get('m_PositionCurves', [])]
    with args.apk.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    physics = next(o.read_typetree() for o in env.objects if o.type.name == 'Physics2DSettings')
    evidence = dict(sha256=digest, prefab=PREFAB, components=components, drivers=drivers,
                    driver_layer_collision_mask=physics['m_LayerCollisionMatrix'][13],
                    kick_effector=dict(name=effector['m_Name'], shapes=effector_shapes),
                    pose_inspection=dict(name=clip['m_Name'], position_curves=curves),
                    limits=['Serialized defaults can be overridden at runtime.',
                            'The supplied APK is a modified distribution.',
                            'Native listings require manual control-flow interpretation.',
                            'Source clips and native code are inspection material, not build inputs.'])
    (args.output / 'mechanics-evidence.json').write_text(
        json.dumps(evidence, ensure_ascii=False, indent=2), encoding='utf-8')
    if args.symbols and args.binary:
        import capstone
        symbols = json.loads(args.symbols.read_text(encoding='utf-8-sig'))
        names = {int(s['RVA'], 16): s['type'] + '.' + s['name'] for s in symbols if 'RVA' in s}
        raw = args.binary.read_bytes()
        decoder = capstone.Cs(capstone.CS_ARCH_ARM64, capstone.CS_MODE_LITTLE_ENDIAN)
        wanted = {'StartKick', 'FixedUpdateNetwork', 'clz', 'cma'}
        lines = []
        for symbol in symbols:
            if not (symbol['type'] == 'KickReceiver' or
                    symbol['type'] == 'CharacterMover' and symbol['name'] in wanted):
                continue
            address, length = int(symbol['RVA'], 16), int(symbol['Length'], 16)
            if int(symbol['Offset'], 16) != address:
                raise ValueError('This listing expects matching ELF RVA/file offsets')
            lines.append('\n### ' + symbol['type'] + '.' + symbol['name'])
            for op in decoder.disasm(raw[address:address + length], address):
                note = names.get(int(op.op_str[1:], 16), '') if op.mnemonic in ('bl', 'b') and op.op_str.startswith('#') else ''
                lines.append(f'{op.address:x}: {op.mnemonic} {op.op_str} {note}')
        (args.output / 'kick-control-flow.asm').write_text('\n'.join(lines), encoding='utf-8')
    print(json.dumps(dict(sha256=digest, components=len(components),
                         effector_shapes=len(effector_shapes), pose_curves=len(curves))))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ('apk', 'serialized', 'dlls', 'output'):
        parser.add_argument('--' + key, type=Path, required=True)
    parser.add_argument('--symbols', type=Path)
    parser.add_argument('--binary', type=Path)
    inspect(parser.parse_args())
