"""Verify runtime-copy provenance cleanup and preservation of functional data."""
import hashlib
import json
import re
from pathlib import Path

import create_standalone_runtime_copy as copying
from finalize_standalone_runtime_copy import clean, label

ROOT = Path(__file__).resolve().parents[1]
AUDIT = ROOT / "output/standalone-runtime-copy-20261030"
PROJECT = ROOT / "standalone/PhysicsCombatCopy/UnityProject"
mapping = json.loads((AUDIT / "identifier-map.json").read_text(encoding="utf-8"))
tokens = re.compile(r"\b(?:" + "|".join(re.escape(k) for k in sorted(mapping, key=len, reverse=True)) + r")\b")


def transform(text):
    text = tokens.sub(lambda m: mapping[m.group(0)], text)
    text = text.replace("Assets/Recovery/", "Assets/Runtime/")
    text = text.replace("Bloody Bastards", "Physics Combat").replace("BloodyBastards", "PhysicsCombat")
    text = text.replace("bloody-local-", "arena-local-").replace("bloody-playtest-", "arena-playtest-")
    text = text.replace("BLOODY_", "ARENA_")
    text = re.sub(r"\bOriginal\b", "Arena", text)
    text = re.sub(r"\boriginal\b", "runtime", text)
    text = text.replace("recoveryControlsOverlay", "controlsOverlay")
    text = text.replace(',"source_apk_sha256",source["source_apk_sha256"].S', "")
    return label(text)


def main():
    before = json.loads((AUDIT / "source-before.json").read_text(encoding="utf-8"))
    changed_source = []
    for relative, expected in before.items():
        path = copying.SOURCE / relative
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            changed_source.append(relative)

    code_checks = []
    for path in (copying.SOURCE / "Assets/Recovery").rglob("*.cs"):
        if copying.omitted(path.relative_to(copying.SOURCE)) or path.name == "OriginalPreview.cs":
            continue
        expected = transform(copying.strip_comments(path.read_text(encoding="utf-8-sig")))
        relative = path.relative_to(copying.SOURCE).as_posix()
        destination = PROJECT / transform(relative)
        if path.name == "OriginalPreview.Match.cs":
            expected = expected.replace("if(!BeginArenaInputFrame()){TraceArenaMouseFrame(pos,down,held,up);return;}", "if(!BeginArenaInputFrame())return;")
            expected = expected.replace("        TraceArenaMouseFrame(pos,down,held,up);\n", "")
        if path.name == "OriginalPreview.Context.cs":
            expected = expected.replace('        if(Array.IndexOf(args,"-validateArenaMatch")>=0)Progress.GameMode=5;\n', "")
        actual = destination.read_text(encoding="utf-8-sig") if destination.is_file() else ""
        code_checks.append({"file": str(destination.relative_to(PROJECT)), "pass": expected == actual})

    data_checks = []
    for path in (copying.SOURCE / "Assets/Recovery/Resources").glob("*.json"):
        if copying.omitted(path.relative_to(copying.SOURCE)):
            continue
        expected = copying.metadata_clean(json.loads(path.read_text(encoding="utf-8-sig")))
        expected = clean(json.loads(transform(json.dumps(expected, ensure_ascii=False, separators=(",", ":")))))
        destination = PROJECT / transform(path.relative_to(copying.SOURCE).as_posix())
        actual = json.loads(destination.read_text(encoding="utf-8")) if destination.is_file() else None
        data_checks.append({"file": str(destination.relative_to(PROJECT)), "pass": expected == actual})

    marker = re.compile(r"\bAPK\b|\bARM64\b|Ghidra|Cpp2IL|decompil|source_apk_sha256|apk_sha256|bloody|bastards|native methods", re.I)
    markers = []
    for path in (PROJECT / "Assets").rglob("*"):
        if not path.is_file() or path.suffix.lower() not in {".cs", ".json", ".unity", ".prefab", ".asset", ".shader", ".txt"}:
            continue
        try:
            text = path.read_text(encoding="utf-8-sig")
        except UnicodeDecodeError:
            continue
        hits = sorted(set(m.group(0) for m in marker.finditer(text)))
        if hits:
            markers.append({"file": str(path.relative_to(PROJECT)), "matches": hits})

    checks = {
        "original_source_unchanged": not changed_source,
        "functional_code_preserved": all(x["pass"] for x in code_checks),
        "functional_json_preserved": all(x["pass"] for x in data_checks),
        "scanned_explicit_markers_absent": not markers,
        "third_party_importer_attribution_retained": (PROJECT / "Assets/Editor/AssetRipperPatches/AudioMixerPostprocessor.cs").is_file(),
    }
    runtime_keys = set()
    for source in (PROJECT / "Assets/Runtime").glob("*.cs"):
        runtime_keys.update(re.findall(r'\[\s*"([^"\n]+)"\s*\]', source.read_text(encoding="utf-8-sig")))
    unsafe_keys = sorted(key for key in runtime_keys if copying.METADATA_KEYS.match(key))
    checks["removed_metadata_keys_not_read_by_runtime"] = not unsafe_keys
    report = {"pass": all(checks.values()), "checks": checks, "source_files_checked": len(before),
              "source_changed_paths": changed_source, "code_checks": code_checks,
              "data_checks": data_checks, "explicit_markers": markers, "unsafe_metadata_keys": unsafe_keys,
              "scope": "Source/data comparison excludes the rewritten startup diagnostic host, removed research tools and metadata. Runtime binding identifiers, original assets, physics values, behavior, license files and importer attribution remain; this is not a no-provenance claim."}
    (AUDIT / "static-validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"pass": report["pass"], "checks": checks, "code_files": len(code_checks), "data_files": len(data_checks),
                      "failures": [x["file"] for x in code_checks + data_checks if not x["pass"]], "explicit_markers": markers}, ensure_ascii=False))


if __name__ == "__main__":
    main()
