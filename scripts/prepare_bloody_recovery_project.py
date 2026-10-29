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
from bloody_ui_recovery import bindings as ui_bindings,remap_document,copy_font_assets,restore_tmp_shaders,remap_animation_bindings
from bloody_spriteshape_recovery import bindings as sprite_bindings,copy_assets as copy_sprite_assets
from bloody_shader_aliases import install as install_shaders


def strip_behaviours(text,ui_mapping=None):
    docs = re.split(r'(?=^--- !u!)', text, flags=re.M)
    removed = set()
    kept = []
    for doc in docs:
        match = re.match(r'--- !u!114 &(-?\d+)', doc)
        if match:
            converted=remap_document(doc,ui_mapping or {})
            if converted is not None:kept.append(converted)
            else:removed.add(match[1])
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
    rules={}
    for rule in (raw/'Assets/MonoBehaviour').glob('MatchRules_*.asset'):
        text=rule.read_text(encoding='utf-8-sig');data={}
        for key in ['gameMode','roundsToWin','isEndless','cinematicIntroMode','UsesCountdown','maxRoundDurationSeconds']:
            value=re.search(r'^  '+key+r': (.+)$',text,re.M)
            if value:data[key]=float(value[1])
        rules[rule.stem]=dict(data=data,source=rule.relative_to(raw).as_posix())
    (source/'Resources/OriginalMatchRules.json').write_text(json.dumps(rules,separators=(',',':')),encoding='utf-8')
    if not (raw / 'Assets/GameObject/Character_Player_1.prefab').is_file():
        raise FileNotFoundError('Export the original APK with AssetRipper first')
    assets = project / 'Assets'
    assets.mkdir(parents=True, exist_ok=True)
    ui_package=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui')
    ui_mapping=ui_bindings(raw/'Assets',ui_package)
    sprite_package=repo/'output/bloody-full-port-20261028/packages/com.unity.2d.spriteshape-14.0.1/package'
    if sprite_package.exists():ui_mapping.update(sprite_bindings(raw/'Assets',sprite_package))
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
            path.write_text(strip_behaviours(text,ui_mapping), encoding='utf-8', newline='\n')
            copied += 1
    shutil.copytree(raw / 'ProjectSettings', project / 'ProjectSettings', dirs_exist_ok=True)
    (project / 'Packages').mkdir(exist_ok=True)
    modules = ['animation', 'audio', 'imageconversion', 'imgui', 'jsonserialize',
               'particlesystem', 'physics2d', 'screencapture', 'ui']
    package_source=repo/'recovery/bloody/Packages/manifest.json'
    if package_source.exists():
        manifest=json.loads(package_source.read_text(encoding='utf-8-sig'))
        # Resolve from this exact editor's bundled packages. This avoids a
        # registry wait and never substitutes packages from another editor.
        bundled=ui_package.parent
        pending=list(manifest['dependencies'])
        seen=set()
        while pending:
            name=pending.pop()
            if name in seen:continue
            seen.add(name)
            package=bundled/name/'package.json'
            if not package.exists():continue
            data=json.loads(package.read_text(encoding='utf-8-sig'))
            manifest['dependencies'][name]='file:'+(bundled/name).as_posix()
            for dep,version in data.get('dependencies',{}).items():
                manifest['dependencies'].setdefault(dep,version);pending.append(dep)
        resolved=repo/'output/bloody-full-port-20261028/tools/unity-packages/resolved-packages.json'
        if resolved.exists():
            for name,path in json.loads(resolved.read_text(encoding='utf-8-sig')).items():
                if not (Path(path)/'package.json').exists():raise FileNotFoundError(path)
                manifest['dependencies'][name]='file:'+Path(path).as_posix()
        for name,version in [('com.unity.2d.spriteshape','14.0.1'),('com.unity.2d.common','13.0.1')]:
            path=repo/f'output/bloody-full-port-20261028/packages/{name}-{version}/package'
            if path.exists():manifest['dependencies'][name]='file:'+path.as_posix()
        (project/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    else:
        (project / 'Packages/manifest.json').write_text(json.dumps(
            dict(dependencies={'com.unity.modules.' + n: '1.0.0' for n in modules}),
            indent=2), encoding='utf-8')
    shutil.copytree(source, assets / 'Recovery', dirs_exist_ok=True)
    shaders=repo/'recovery/bloody/Shaders'
    if shaders.exists():
        shader_aliases=install_shaders(shaders,assets)
        (project/'SHADER_ALIAS_RECOVERY.json').write_text(json.dumps(shader_aliases,indent=2),encoding='utf-8')
    fonts=copy_font_assets(raw/'Assets',assets,ui_mapping)
    sprite_profiles=copy_sprite_assets(raw/'Assets',assets,ui_mapping)
    tmp_shaders=restore_tmp_shaders(assets,ui_package)
    animation_mapping=dict(ui_mapping)
    # Light2D components are rebuilt with their source fields by the scene
    # builder; only animation bindings are remapped here to avoid duplicates.
    animation_mapping[('796348501','841024f847a9ee126ccc5cbf660ee0d4')]=dict(guid='073797afb82c5a1438f328866b10b3f0',name='UnityEngine.Rendering.Universal.Light2D')
    animation_remaps=remap_animation_bindings(assets,animation_mapping)
    (project/'ANIMATION_BINDING_RECOVERY.json').write_text(json.dumps(animation_remaps,indent=2),encoding='utf-8')
    shutil.copy2(repo / 'unity/JianghuUnity/Assets/Scripts/Json.cs', assets / 'Recovery/Json.cs')
    data = json.loads((repo / 'output/bloody-full-port-20261028/dependencies.json').read_text())
    rigs = {name: resolve_rig(rig) for name, rig in data['rigs'].items()}
    resources = assets / 'Recovery/Resources'
    resources.mkdir(parents=True, exist_ok=True)
    (resources / 'OriginalRigReferences.json').write_text(
        json.dumps(rigs, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    ui=repo/'output/bloody-full-port-20261028/original-ui-source.json'
    if ui.exists():(resources/'OriginalUISource.json').write_text(json.dumps(dict(records=json.loads(ui.read_text(encoding='utf-8-sig'))['records']),ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    status = dict(project=str(project), source_apk_sha256=data['source_apk_sha256'],
                  source_unity='6000.4.0f1', copied_scene_and_prefab_files=copied,
                  recovered_resources=True, gameplay_complete=False,
                  runtime_parity_verified=False,
                  ui_script_bindings=len(ui_mapping),original_tmp_assets=fonts,official_tmp_shaders=tmp_shaders,
                  original_sprite_profiles=sprite_profiles,
                  explanation='Original geometry/resources; reconstructed behaviours require native review and runtime comparison')
    (project / 'RECOVERY_STATUS.json').write_text(json.dumps(status, indent=2), encoding='utf-8')
    print(json.dumps(status, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[1])
    main(parser.parse_args())
