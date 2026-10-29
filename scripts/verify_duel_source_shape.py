"""Execute original ARM64 shape methods, compare production C# shape formulas.

Hooks only Unity property setters. Native clamps, branches and arithmetic run
unchanged under Unicorn. This is numerical validation, not Unity acceptance.
Uses the same task-local Unicorn dependency as verify_bloody_ai_native.py.
"""
import json
from pathlib import Path
import struct
import subprocess

from verify_bloody_ai_native import NativeAI, UC_HOOK_CODE
from verify_bloody_ai_native import UC_ARM64_REG_X0, UC_ARM64_REG_X1
from verify_bloody_ai_native import UC_ARM64_REG_PC, UC_ARM64_REG_LR

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/source-body-shape-check'
SCRIPTS = ROOT / 'unity/JianghuUnity/Assets/Scripts'
METHODS = dict(Weight=0x3420dac, Height=0x3420f30, Shoulders=0x34210ac,
               Waist=0x34211ac, Hips=0x34213a8)
SETTERS = {0x6adba64: ('localPosition', 3),
           0x6add09c: ('localScale', 3),
           0x6b5bfcc: ('connectedAnchor', 2),
           0x6a977bc: ('blend', 1)}


def original_cases():
    native = NativeAI()
    types = json.loads((ROOT / 'output/bloody-code-audit-20261028/game-metadata.json').read_text())['types']
    fields = next(t for t in types if t['type'] == 'CharacterConfigApplier')['fields']
    names = {}
    for index, field in enumerate(fields):
        if field['type'] not in ('UnityEngine.Transform', 'UnityEngine.HingeJoint2D',
                                 'UnityEngine.SkinnedMeshRenderer'):
            continue
        pointer = 0x10016000 + index * 16
        offset = int(field['offset']['Offset'], 16)
        native.cpu.mem_write(native.SELF + offset, struct.pack('<Q', pointer))
        names[pointer] = field['name']
    native.cpu.mem_write(native.SELF + 0x20, struct.pack('<Q', native.PROFILE))
    calls = []

    def hook(cpu, address, size, user_data):
        if address not in SETTERS:
            return
        property_name, count = SETTERS[address]
        pointer = cpu.reg_read(UC_ARM64_REG_X0)
        calls.append(dict(field=names[pointer], property=property_name,
                          index=cpu.reg_read(UC_ARM64_REG_X1) if property_name == 'blend' else None,
                          value=[native.sf(i) for i in range(count)]))
        cpu.reg_write(UC_ARM64_REG_PC, cpu.reg_read(UC_ARM64_REG_LR))

    native.cpu.hook_add(UC_HOOK_CODE, hook)
    cases = []
    for gender in (0, 1):
        for values in ((0, 0, .125, 0, 0), (-1, -1, -1, -1, -1),
                       (1, 1, 1, 1, 1), (.2, -.2, .13, -.3, .02)):
            config = dict(zip(('Gender', 'Height', 'Weight', 'ShouldersWidth',
                               'WaistWidth', 'HipsWidth'), (gender, *values)))
            native.cpu.mem_write(native.PROFILE + 0x18, struct.pack('<i5f', gender, *values))
            calls.clear()
            for method in METHODS.values():
                native.call(method, native.SELF)
            normalized = dict(zip(config, struct.unpack('<i5f', native.cpu.mem_read(native.PROFILE + 0x18, 24))))
            assert len(calls) == 34, calls
            cases.append(dict(input=config, config=normalized, calls=list(calls)))
    return cases


def main():
    OUT.mkdir(exist_ok=True, parents=True)
    cases = original_cases()
    (OUT / 'native-oracle.json').write_text(json.dumps(cases, indent=2) + '\n')
    graph = json.loads((SCRIPTS.parent / 'Resources/CombatSource/rig.json').read_text())
    neutral = next(t for t in cases if t['input']['Gender'] == 1 and t['input']['Height'] == 0)
    differences = {}
    for source_id, rig in graph['rigs'].items():
        config = rig['controllers']['CharacterConfigApplier'][0]['data']
        physics = {str(p['id']): p['data'] for p in rig['physics']}
        rows = []
        for call in neutral['calls']:
            if call['property'] == 'blend':
                continue
            identity = str(config[call['field']]['m_PathID'])
            prop = {'localPosition': 'm_LocalPosition', 'localScale': 'm_LocalScale',
                    'connectedAnchor': 'm_ConnectedAnchor'}[call['property']]
            before = (physics if prop == 'm_ConnectedAnchor' else rig['transforms'])[identity][prop]
            rows.append(dict(field=call['field'], id=int(identity), property=call['property'],
                             before=before, after=call['value'], changed=list(before.values()) != call['value']))
        differences[source_id] = rows
    (OUT / 'neutral-prefab-differences.json').write_text(json.dumps(differences, indent=2) + '\n')
    source = (SCRIPTS / 'PhysicsCombat/DuelSourceBodyShape.cs').read_text()
    (OUT / 'Shape.cs').write_text(source.split('public sealed partial class PhysicsDuel')[0])
    (OUT / 'Json.cs').write_text((SCRIPTS / 'Json.cs').read_text())
    (OUT / 'Program.cs').write_text((ROOT / 'tests/probes/DuelSourceShapeProbe.cs').read_text())
    (OUT / 'ShapeCheck.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
        '<NuGetAudit>false</NuGetAudit></PropertyGroup></Project>')
    (OUT / 'Stub.cs').write_text('namespace UnityEngine {'
        'public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}}'
        'public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}'
        'public static class Mathf {public static float Clamp(float v,float a,float b)=>v<a?a:v>b?b:v;}}')
    result = subprocess.run(['dotnet', 'run', '--project', 'ShapeCheck.csproj', '--ignore-failed-sources'],
                            cwd=OUT, check=True, capture_output=True, text=True)
    report = dict(native_cases=len(cases), native_setter_calls=sum(len(t['calls']) for t in cases),
                  methods={key: hex(value) for key, value in METHODS.items()},
                  production_csharp_checks=result.stdout.strip(),
                  scope='Original ARM64 numerical setter outputs; no Unity build or dynamic physics acceptance')
    (OUT / 'result.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
