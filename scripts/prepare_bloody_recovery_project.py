"""Prepare an isolated, editable recovery project from the supplied APK export.

The raw AssetRipper export is retained. Empty Cpp2IL script bodies and Android
plugin stubs are deliberately not copied into the executable recovery project.
Numeric controller references are resolved to original hierarchy paths instead.
This preparation step does not certify recovered gameplay or equivalence.
"""
import argparse
import collections
import hashlib
import json
import re
import shutil
from pathlib import Path


def strip_behaviours(text):
    docs = re.split(r'(?=^--- !u!)', text, flags=re.M)
    removed = set()
    kept = []
    for doc in docs:
        match = re.match(r'--- !u!114 &(-?\d+)', doc)
        if match:
            removed.add(match[1])
        else:
            kept.append(doc)
    text = ''.join(kept)
    return re.sub(r'^  - component: \{fileID: (-?\d+)\}\r?\n',
                  lambda m: '' if m[1] in removed else m[0], text, flags=re.M)


def resolve_rig(rig):
    objects = rig['objects']
    transforms = rig['transforms']
    go_to_transform = {str(t['m_GameObject']['m_PathID']): k for k, t in transforms.items()}
    component_to_go = {}
    for go_id, go in objects.items():
        for item in go['m_Component']:
            component_to_go[str(item['component']['m_PathID'])] = go_id
    cache = {}

    def path(transform_id):
        if transform_id in cache:
            return cache[transform_id]
        t = transforms[transform_id]
        parent = str(t['m_Father']['m_PathID'])
        name = objects[str(t['m_GameObject']['m_PathID'])]['m_Name']
        result = (path(parent) + '/' + name).lstrip('/') if parent != '0' else ''
        cache[transform_id] = result
        return result

    for transform_id in transforms:
        path(transform_id)
    duplicates = [p for p, n in collections.Counter(cache.values()).items() if n > 1]
    if duplicates:
        raise ValueError('Ambiguous original hierarchy paths: ' + repr(duplicates))
    physics_by_id={str(component['id']):component['type'] for component in rig['physics']}
    bindings={}
    for go_id,go in objects.items():
        counts=collections.Counter()
        for entry in go['m_Component']:
            component_id=str(entry['component']['m_PathID'])
            kind=physics_by_id.get(component_id)
            if kind is None:continue
            bindings[component_id]=dict(path=cache[go_to_transform[go_id]],kind=kind,index=counts[kind])
            counts[kind]+=1

    def value(data):
        if isinstance(data, list):
            return [value(v) for v in data]
        if not isinstance(data, dict):
            return data
        if 'm_PathID' not in data:
            return {k: value(v) for k, v in data.items()}
        source_id = str(data['m_PathID'])
        result = dict(id=source_id, file=data['m_FileID'])
        if source_id != '0' and data['m_FileID'] == 0:
            go_id = source_id if source_id in objects else component_to_go.get(source_id)
            if go_id is None:
                raise ValueError('Unresolved local reference ' + source_id)
            result['path'] = cache[go_to_transform[go_id]]
        return result

    return dict(source=rig['source'], controllers=value(rig['controllers']),component_bindings=bindings,
                physics_counts=dict(collections.Counter(p['type'] for p in rig['physics'])))


def main(args):
    repo = args.repo.resolve()
    raw = repo / 'output/bloody-full-port-20261028/Recovered/ExportedProject'
    project = repo / 'unity/BloodyBastardsRecovery'
    source = repo / 'recovery/bloody/Runtime'
    if not (raw / 'Assets/GameObject/Character_Player_1.prefab').is_file():
        raise FileNotFoundError('Export the original APK with AssetRipper first')
    assets = project / 'Assets'
    assets.mkdir(parents=True, exist_ok=True)
    copied = 0
    for item in (raw / 'Assets').iterdir():
        if item.name in {'Scripts', 'Scripts.meta', 'Plugins', 'Plugins.meta',
                         'MonoBehaviour', 'MonoBehaviour.meta'}:
            continue
        target = assets / item.name
        if item.is_dir():
            shutil.copytree(item, target, dirs_exist_ok=True)
        else:
            shutil.copy2(item, target)
    for path in assets.rglob('*'):
        if path.suffix in {'.prefab', '.unity'}:
            text = path.read_text(encoding='utf-8-sig')
            path.write_text(strip_behaviours(text), encoding='utf-8', newline='\n')
            copied += 1
    shutil.copytree(raw / 'ProjectSettings', project / 'ProjectSettings', dirs_exist_ok=True)
    (project / 'Packages').mkdir(exist_ok=True)
    modules = ['animation', 'audio', 'imageconversion', 'imgui', 'jsonserialize',
               'particlesystem', 'physics2d', 'screencapture', 'ui']
    package_source=repo/'recovery/bloody/Packages/manifest.json'
    if package_source.exists():
        shutil.copy2(package_source,project/'Packages/manifest.json')
    else:
        (project / 'Packages/manifest.json').write_text(json.dumps(
            dict(dependencies={'com.unity.modules.' + n: '1.0.0' for n in modules}),
            indent=2), encoding='utf-8')
    shutil.copytree(source, assets / 'Recovery', dirs_exist_ok=True)
    shaders=repo/'recovery/bloody/Shaders'
    if shaders.exists():
        for shader in shaders.glob('*.shader'):
            shutil.copy2(shader, assets/'Shader'/shader.name)
    shutil.copy2(repo / 'unity/JianghuUnity/Assets/Scripts/Json.cs', assets / 'Recovery/Json.cs')
    data = json.loads((repo / 'output/bloody-full-port-20261028/dependencies.json').read_text())
    rigs = {name: resolve_rig(rig) for name, rig in data['rigs'].items()}
    resources = assets / 'Recovery/Resources'
    resources.mkdir(parents=True, exist_ok=True)
    (resources / 'OriginalRigReferences.json').write_text(
        json.dumps(rigs, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    status = dict(project=str(project), source_apk_sha256=data['source_apk_sha256'],
                  source_unity='6000.4.0f1', copied_scene_and_prefab_files=copied,
                  recovered_resources=True, gameplay_complete=False,
                  runtime_parity_verified=False,
                  explanation='Original geometry/resources; reconstructed behaviours require native review and runtime comparison')
    (project / 'RECOVERY_STATUS.json').write_text(json.dumps(status, indent=2), encoding='utf-8')
    print(json.dumps(status, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[1])
    main(parser.parse_args())
