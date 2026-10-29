"""Synchronize one stable source snapshot, aliases and engine curve bindings."""
import hashlib
import json
from pathlib import Path
import shutil
import time
from bloody_shader_aliases import install
from bloody_ui_recovery import bindings,remap_animation_bindings
from bloody_spriteshape_recovery import bindings as sprite_bindings

ROOT=Path(__file__).resolve().parents[1]

def inventory(folder):
    return {p.relative_to(folder).as_posix():hashlib.sha256(p.read_bytes()).hexdigest()for p in folder.rglob('*')if p.is_file()}

def main():
    source=ROOT/'recovery/bloody';project=ROOT/'unity/BloodyBastardsRecovery';assets=project/'Assets'
    snapshots=ROOT/'output/bloody-full-port-20261028/runtime-snapshots';snapshots.mkdir(exist_ok=True)
    snapshot=snapshots/str(time.time_ns());snapshot.mkdir()
    for attempt in range(4):
        attempt_folder=snapshot/str(attempt);attempt_folder.mkdir()
        before={name:inventory(source/name)for name in ['Runtime','Shaders']}
        for name in before:shutil.copytree(source/name,attempt_folder/name)
        after={name:inventory(source/name)for name in before}
        if before==after:break
    else:raise RuntimeError('Source group is changing; synchronize after the author finishes')
    shutil.copytree(attempt_folder/'Runtime',assets/'Recovery',dirs_exist_ok=True)
    aliases=install(attempt_folder/'Shaders',assets)
    raw=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
    package=Path('D:/Unity/Editors/6000.4.0f1/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui')
    mapping=bindings(raw,package)
    sprites=ROOT/'output/bloody-full-port-20261028/packages/com.unity.2d.spriteshape-14.0.1/package'
    if sprites.exists():mapping.update(sprite_bindings(raw,sprites))
    mapping[('796348501','841024f847a9ee126ccc5cbf660ee0d4')]=dict(guid='073797afb82c5a1438f328866b10b3f0',name='UnityEngine.Rendering.Universal.Light2D')
    remaps=remap_animation_bindings(assets,mapping)
    (project/'SHADER_ALIAS_RECOVERY.json').write_text(json.dumps(aliases,indent=2),encoding='utf-8')
    (project/'ANIMATION_BINDING_RECOVERY.json').write_text(json.dumps(remaps,indent=2),encoding='utf-8')
    (snapshot/'snapshot.json').write_text(json.dumps(dict(files=before,shader_aliases=aliases,animation_binding_remaps=remaps),indent=2),encoding='utf-8')
    print(json.dumps(dict(snapshot=str(snapshot),runtime_files=len(before['Runtime']),shader_names=len(aliases),animation_files_rebound=len(remaps))))

if __name__=='__main__':main()
