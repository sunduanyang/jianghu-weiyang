"""Finish the runtime copy without editing its original project."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COPY = (ROOT / "standalone/PhysicsCombatCopy").resolve()
PROJECT = COPY / "UnityProject"
ASSETS = PROJECT / "Assets"


def label(text):
    for old, new in (("Bastardly", "Battle"), ("bastardly", "battle"),
                     ("Bastards", "Fighters"), ("bastards", "fighters"),
                     ("Bastard", "Fighter"), ("bastard", "fighter"),
                     ("Bloody", "Blood"), ("bloody", "blood")):
        text = text.replace(old, new)
    return text


def clean(value):
    if isinstance(value, dict):
        return {k: clean(v) for k, v in value.items()
                if not (k == "source" and isinstance(v, str) and v.startswith("output/"))}
    if isinstance(value, list):
        return [clean(v) for v in value]
    return value


def main():
    if not (ASSETS / "Runtime/ArenaPreview.cs").is_file():
        raise SystemExit("Expected runtime copy does not exist")
    changed = []
    for base in (ASSETS, PROJECT / "ProjectSettings"):
        for path in base.rglob("*"):
            if not path.is_file() or path.suffix.lower() not in {".cs", ".json", ".unity", ".prefab", ".asset", ".mat", ".anim", ".controller", ".overridecontroller", ".shader", ".meta", ".txt"}:
                continue
            if "AssetRipperPatches" in path.parts:
                continue
            try:
                old = path.read_text(encoding="utf-8-sig")
            except UnicodeDecodeError:
                continue
            new = old
            if path.suffix == ".json" and path.is_relative_to(ASSETS / "Runtime/Resources"):
                new = json.dumps(clean(json.loads(new)), ensure_ascii=False, separators=(",", ":"))
            new = label(new)
            if path.name == "ArenaPreview.Match.cs":
                new = new.replace("if(!BeginArenaInputFrame()){TraceArenaMouseFrame(pos,down,held,up);return;}", "if(!BeginArenaInputFrame())return;")
                new = new.replace("        TraceArenaMouseFrame(pos,down,held,up);\n", "")
            if path.name == "ArenaPreview.Context.cs":
                new = new.replace('        if(Array.IndexOf(args,"-validateArenaMatch")>=0)Progress.GameMode=5;\n', "")
            if path.name == "ProjectSettings.asset":
                new = re.sub(r"(?m)^  switchApplicationID:.*$", "  switchApplicationID: 0x0", new)
                new = re.sub(r"com\.[A-Za-z0-9_.]*(?:PhysicsCombat|bloodfighters)[A-Za-z0-9_.]*", "local.physicscombat.standalone", new, flags=re.I)
            if new != old:
                path.write_text(new, encoding="utf-8", newline="\n")
                changed.append(path.relative_to(COPY).as_posix())
    # Preserve .meta GUIDs while renaming any matching asset filenames.
    for path in sorted(ASSETS.rglob("*"), key=lambda p: len(p.parts), reverse=True):
        new_name = label(path.name)
        if path.name != new_name:
            destination = path.with_name(new_name).resolve()
            if not destination.is_relative_to(COPY):
                raise RuntimeError("Rename escaped runtime copy")
            if destination.exists():
                raise RuntimeError(f"Rename collision: {destination}")
            path.rename(destination)
    audit = ROOT / "output/standalone-runtime-copy-20261030/finalize-audit.json"
    audit.write_text(json.dumps({"changed_files": changed, "original_project_edited": False}, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"changed_files": len(changed)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
