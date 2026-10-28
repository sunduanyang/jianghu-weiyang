"""Read-only, reproducible inspection of a user-supplied Unity APK.

Requires UnityPy; writes local evidence, never installs or runs the APK.
Serialized defaults are evidence of authoring, not verified runtime behavior.
"""
import argparse
import collections
import hashlib
import json
import re
import zipfile
from pathlib import Path

import UnityPy


def inspect(apk, output, dlls=None):
    output.mkdir(parents=True, exist_ok=True)
    data_dir = output / "serialized"
    data_dir.mkdir(exist_ok=True)
    with zipfile.ZipFile(apk) as archive:
        names = archive.namelist()
        for name in names:
            if name.startswith("assets/bin/Data/") and re.fullmatch(r"[0-9a-f]{32}", name.rsplit("/", 1)[-1]):
                (data_dir / name.rsplit("/", 1)[-1]).write_bytes(archive.read(name))
        for base in ("globalgamemanagers", "globalgamemanagers.assets", "level0", "sharedassets0.assets"):
            path = "assets/bin/Data/" + base
            if path in names:
                data = archive.read(path)
            else:
                splits = sorted((n for n in names if n.startswith(path + ".split")), key=lambda n: int(n.rsplit("split", 1)[1]))
                data = b"".join(archive.read(n) for n in splits)
            (data_dir / base).write_bytes(data)
        metadata = archive.read("assets/bin/Data/Managed/Metadata/global-metadata.dat")
        (output / "global-metadata.dat").write_bytes(metadata)
    env = UnityPy.load(str(data_dir))
    generator = None
    if dlls:
        from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
        generator = TypeTreeGenerator("6000.4.0f1")
        generator.load_local_dll_folder(str(dlls))
    counts = collections.Counter(obj.type.name for obj in env.objects)
    records = []
    errors = []
    scripts = {}
    for obj in env.objects:
        if obj.type.name == "MonoScript":
            tree = obj.read_typetree()
            scripts[(obj.assets_file.name, obj.path_id)] = tree
    wanted = {"Physics2DSettings", "TimeManager", "PhysicsMaterial2D", "Rigidbody2D", "HingeJoint2D", "FixedJoint2D", "SpringJoint2D", "DistanceJoint2D", "TargetJoint2D", "MonoBehaviour"}
    for obj in env.objects:
        if obj.type.name not in wanted:
            continue
        try:
            owner = ""
            script = ""
            nodes = None
            if generator and obj.type.name == "MonoBehaviour":
                base = obj.read(check_read=False)
                mono = base.m_Script.read()
                script = mono.m_ClassName
                if script not in {"CharacterMover", "CharacterVitality", "StaminaDrainer", "CharacterDamageCalculator", "BodyPart", "Joystick", "DynamicJoystick", "PlayerInputHandler", "SourcePrefab_HandItem", "CharacterEnemyAI", "AIHandsInputRelayer", "CharacterConfigApplier"}:
                    continue
                nodes = generator.get_nodes_up(mono.m_AssemblyName, (mono.m_Namespace + "." if mono.m_Namespace else "") + script)
            tree = obj.read_typetree(nodes=nodes)
            if nodes:
                # The generator's base UInt8 alignment differs from Unity's
                # shipped base tree. User fields start after the aligned name;
                # retain the authoritative engine header from its own tree.
                header = obj.read_typetree(check_read=False)
                for key in ("m_GameObject", "m_Enabled", "m_Script", "m_Name"):
                    tree[key] = header[key]
            if "m_GameObject" in tree:
                ptr = obj.read(check_read=False).m_GameObject
                if ptr.path_id:
                    owner = ptr.read().m_Name
            if obj.type.name == "MonoBehaviour":
                try:
                    script = obj.read(check_read=False).m_Script.read().m_ClassName
                except Exception:
                    pass
            records.append({"file": obj.assets_file.name, "id": obj.path_id, "type": obj.type.name, "owner": owner, "script": script, "data": tree})
        except Exception as exc:
            errors.append({"file": obj.assets_file.name, "id": obj.path_id, "type": obj.type.name, "error": str(exc)[:160]})
    report = {"apk": str(apk), "sha256": hashlib.sha256(apk.read_bytes()).hexdigest(), "counts": counts, "records": records, "errors": errors, "scripts": list(scripts.values())}
    (output / ("decoded-evidence.json" if dlls else "serialized-evidence.json")).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"sha256": report["sha256"], "counts": counts, "records": len(records), "errors": len(errors)}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("apk", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--dlls", type=Path)
    args = parser.parse_args()
    inspect(args.apk, args.output, args.dlls)
