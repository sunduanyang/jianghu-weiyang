"""Read-only injury/stance evidence from the user supplied APK extraction."""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path

import numpy as np
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator


def inspect(args):
    env = UnityPy.load(str(args.serialized))
    prefab = next(f for f in env.files.values() if f.name == '00af04d95140a4b44ac8453d91c032ea')
    generator = TypeTreeGenerator('6000.4.0f1')
    generator.load_local_dll_folder(str(args.dlls))
    components = []
    for obj in prefab.objects.values():
        if obj.type.name != 'MonoBehaviour':
            continue
        script = obj.read(check_read=False).m_Script.read()
        if script.m_ClassName in {'CharacterMover', 'BodyPart'}:
            data = obj.read_typetree(nodes=generator.get_nodes_up(script.m_AssemblyName, script.m_ClassName))
            components.append((script.m_ClassName, data))
    mover = next(data for name, data in components if name == 'CharacterMover')

    def world(identifier):
        t = prefab.objects[identifier].read_typetree()
        q = t['m_LocalRotation']
        x, y, z, w = (q[k] for k in ('x', 'y', 'z', 'w'))
        matrix = np.eye(4)
        matrix[:3, :3] = np.array([
            [1-2*y*y-2*z*z, 2*x*y-2*z*w, 2*x*z+2*y*w],
            [2*x*y+2*z*w, 1-2*x*x-2*z*z, 2*y*z-2*x*w],
            [2*x*z-2*y*w, 2*y*z+2*x*w, 1-2*x*x-2*y*y],
        ]) @ np.diag([t['m_LocalScale'][k] for k in ('x', 'y', 'z')])
        matrix[:3, 3] = [t['m_LocalPosition'][k] for k in ('x', 'y', 'z')]
        parent = t['m_Father']['m_PathID']
        return world(parent) @ matrix if parent else matrix

    points = {}
    for key in ('pelvisPoint', 'pelvisWhenAnyLegSeveredPoint'):
        points[key] = world(mover[key]['m_PathID'])[:3, 3].tolist()
    parts = []
    for name, data in components:
        if name != 'BodyPart':
            continue
        obj = prefab.objects[data['m_GameObject']['m_PathID']].read_typetree()
        parts.append(dict(name=obj['m_Name'], type=data['bodyPartType'],
                          triggers_any_leg_severed=bool(data['triggersAnyLegSevered'])))
    with args.apk.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    with zipfile.ZipFile(args.apk) as archive:
        binary_hash = hashlib.sha256(archive.read('lib/arm64-v8a/libil2cpp.so')).hexdigest()
    local_hash = hashlib.sha256(args.binary.read_bytes()).hexdigest()
    if binary_hash != local_hash:
        raise ValueError('Inspected native binary differs from supplied APK')
    evidence = dict(apk_sha256=digest, binary_sha256=binary_hash, points=points,
                    settings={k: mover[k] for k in ('movementSpeed', 'movementSpeedWhenDowned', 'maxVelocityX')},
                    parts=parts,
                    reviewed_native_methods={
                        'CharacterMover.clm': 'FILE 0x34310d4: anyLegSevered selects pelvisWhenAnyLegSeveredPoint before MovePosition.',
                        'BodyPart.cdx': 'FILE 0x3416914: triggersAnyLegSevered sets flag; pelvis COM mass 5, downed movement speed, max velocity 1.75.',
                        'BodyPart.cdy': 'FILE 0x3416a54: same downed transition, detached driver deactivation and gravity restoration.',
                        'CharacterMover.StartKick': 'FILE 0x3432098: either leg severed rejects kick.',
                    },
                    limits=['Modified APK; not original C# source.', 'Static evidence does not prove identical runtime trajectories.'])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(evidence, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ('apk', 'serialized', 'dlls', 'binary', 'output'):
        parser.add_argument('--' + key, type=Path, required=True)
    inspect(parser.parse_args())
