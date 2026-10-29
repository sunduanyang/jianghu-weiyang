"""Export the complete numeric source hierarchy for the formal PhysicsDuel loader.

Copies no renderers, textures, empty IL2CPP scripts, or executable APK code.
All original records remain inspectable; added material_key resolves file refs.
"""
import argparse
import copy
import hashlib
import json
import math
import re
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'output/bloody-full-port-20261028/source-combat-data.json'
SETTINGS = ROOT / 'unity/BloodyBastardsRecovery/ProjectSettings'
DEST = ROOT / 'unity/JianghuUnity/Assets/Resources/CombatSource/rig.json'
PLAYER = '00af04d95140a4b44ac8453d91c032ea'
ENEMY = 'c0b154b0ebaf72f4fa139b4e3cf363e9'


def build():
    source = json.loads(SOURCE.read_text(encoding='utf-8'))
    physics_text = (SETTINGS / 'Physics2DSettings.asset').read_text(encoding='utf-8')
    matrix_hex = re.search(r'm_LayerCollisionMatrix: ([0-9a-fA-F]+)', physics_text)[1]
    matrix = list(struct.unpack('<32I', bytes.fromhex(matrix_hex)))
    tag_text = (SETTINGS / 'TagManager.asset').read_text(encoding='utf-8')
    names = re.search(r'  layers:\n(.*?)  m_SortingLayers:', tag_text, re.S)[1]
    layers = [line[4:].strip() for line in names.splitlines() if line.startswith('  - ')]
    materials = copy.deepcopy(source['materials'])
    unity_files = None

    def material_key(rig, ref):
        nonlocal unity_files
        path_id, file_id = ref['m_PathID'], ref['m_FileID']
        if path_id == 0:
            return ''
        filename = rig['source'] if file_id == 0 else rig['externals'][file_id - 1].replace('\\', '/').split('/')[-1]
        key = f'{filename}:{path_id}'
        if key not in materials:
            if unity_files is None:
                import UnityPy
                env = UnityPy.load(str(ROOT / 'output/bloody-reference-20261028/serialized'))
                unity_files = {f.name: f for f in env.files.values()}
            obj = unity_files[filename].objects[path_id]
            if obj.type.name != 'PhysicsMaterial2D':
                raise ValueError(f'Expected physics material: {key} is {obj.type.name}')
            raw = obj.read_typetree(check_read=False)
            materials[key] = {k: raw[k] for k in ('m_Name','friction','bounciness','m_FrictionCombine','m_BounceCombine')}
        return key

    def enrich(value, rig, field=''):
        if isinstance(value, list):
            return [enrich(v, rig, field) for v in value]
        if not isinstance(value, dict):
            if isinstance(value, float) and not math.isfinite(value):
                return '+Infinity' if value > 0 else '-Infinity' if value < 0 else 'NaN'
            return value
        result = {k: enrich(v, rig, k) for k, v in value.items()}
        if 'm_PathID' in value and ('material' in field.lower() or field.startswith('HandItem_')):
            result['material_key'] = material_key(rig, value)
        return result

    rigs = {key: enrich(source['rigs'][key], source['rigs'][key]) for key in (PLAYER, ENEMY)}
    for key, rig in rigs.items():
        roots = [int(k) for k, t in rig['transforms'].items() if t['m_Father']['m_PathID'] == 0]
        assert len(roots) == 1
        rig['root_transform_id'] = roots[0]
        ids = {p['id'] for p in rig['physics']}
        assert len(ids) == len(rig['physics'])
        for p in rig['physics']:
            ref = p['data'].get('m_ConnectedRigidBody')
            if ref and ref['m_PathID']:
                assert ref['m_FileID'] == 0 and ref['m_PathID'] in ids
        assert len(rig['physics']) == (165 if key == PLAYER else 162)
    return dict(schema=1, source_apk_sha256=source['source_apk_sha256'],
        source_unity=source['source_unity'], source_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        player_source=PLAYER, enemy_source=ENEMY, collision_matrix=matrix, layer_names=layers,
        physics_settings_yaml=physics_text, materials=materials, rigs=rigs,
        scope='Numeric physical graph and controller data only; formal runtime integration is separate.')


def audit(result):
    """Compare EVERY retained leaf and exact connectedBody against owning input."""
    source = json.loads(SOURCE.read_text(encoding='utf-8'))
    counts = dict(leaves=0, numeric_leaves=0, references=0, connected_bodies=0, polygon_points=0)

    def compare(original, exported, path):
        if isinstance(original, dict):
            for key, value in original.items():
                assert key in exported, f'Missing {path}.{key}'
                compare(value, exported[key], f'{path}.{key}')
            if 'm_PathID' in original:
                counts['references'] += 1
        elif isinstance(original, list):
            assert len(original) == len(exported), f'List length {path}'
            for index, value in enumerate(original):
                compare(value, exported[index], f'{path}[{index}]')
        else:
            counts['leaves'] += 1
            if isinstance(original, (int, float)) and not isinstance(original, bool):
                counts['numeric_leaves'] += 1
            if isinstance(original, float) and not math.isfinite(original):
                original = '+Infinity' if original > 0 else '-Infinity' if original < 0 else 'NaN'
            assert original == exported, f'Changed source value {path}: {original!r} != {exported!r}'

    rigs = {}
    for key, rig in result['rigs'].items():
        compare(source['rigs'][key], rig, key)
        transforms, objects = rig['transforms'], rig['objects']
        physics = {p['id']: p for p in rig['physics']}
        pending = []
        for tid, transform in transforms.items():
            go = transform['m_GameObject']['m_PathID']
            assert str(go) in objects
            assert any(c['component']['m_PathID'] == int(tid) for c in objects[str(go)]['m_Component'])
            parent = transform['m_Father']['m_PathID']
            if parent:
                assert str(parent) in transforms
                assert any(c['m_PathID'] == int(tid) for c in transforms[str(parent)]['m_Children'])
            for child in transform['m_Children']:
                assert transforms[str(child['m_PathID'])]['m_Father']['m_PathID'] == int(tid)
        for pid, component in physics.items():
            d = component['data']
            owner = str(d['m_GameObject']['m_PathID'])
            assert owner in objects
            assert any(c['component']['m_PathID'] == pid for c in objects[owner]['m_Component'])
            connected = d.get('m_ConnectedRigidBody')
            if connected:
                counts['connected_bodies'] += 1
                cid = connected['m_PathID']
                if cid:
                    assert connected['m_FileID'] == 0 and physics[cid]['type'] == 'Rigidbody2D'
                    if physics[cid]['data']['m_GameObject'] == d['m_GameObject']:
                        pending.append(pid)
            if component['type'] == 'PolygonCollider2D':
                counts['polygon_points'] += sum(len(path) for path in d['m_Points']['m_Paths'])
            if 'm_IncludeLayers' in d:
                # Existing input has no includes beyond its source matrix;
                # fail if that changes rather than silently blocking overrides.
                layer = objects[owner]['m_Layer']
                assert d['m_IncludeLayers']['m_Bits'] & ~result['collision_matrix'][layer] == 0
        rigs[key] = dict(objects=len(objects), transforms=len(transforms), physics=len(physics),
                         bodies=sum(p['type'] == 'Rigidbody2D' for p in physics.values()), pending_self_joints=pending)
    assert result['collision_matrix'][18] == 1 << 16
    return dict(verified=True, counts=counts, rigs=rigs, materials=len(result['materials']),
                native_runtime_checked=False, note='Source JSON preservation/reference audit; Unity loader must run ValidateSource before caller mutations.')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    result = build()
    report = audit(result)
    text = json.dumps(result, ensure_ascii=False, separators=(',', ':'), allow_nan=False) + '\n'
    if args.check:
        if DEST.read_text(encoding='utf-8') != text:
            raise SystemExit('CombatSource/rig.json differs; run scripts/build_duel_source_rig.py')
    else:
        DEST.write_text(text, encoding='utf-8')
    output = ROOT / 'output/source-rig-compile-20261028'
    output.mkdir(parents=True, exist_ok=True)
    (output / 'source-audit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(dict(rigs={k: dict(objects=len(v['objects']), transforms=len(v['transforms']), physics=len(v['physics'])) for k, v in result['rigs'].items()}, materials=len(result['materials']))))


if __name__ == '__main__':
    main()
