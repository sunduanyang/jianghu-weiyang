"""Read-only art-interface inventory and vector UV templates for the runtime copy."""
from __future__ import annotations

import html
import json
import math
import re
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "standalone/PhysicsCombatCopy/UnityProject"
ASSETS = PROJECT / "Assets"
OUT = ROOT / "standalone/PhysicsCombatCopy/ArtGuide"
FORMAT_SIZE = {0: 4, 1: 2, 2: 1, 3: 1, 4: 2, 5: 2, 6: 1, 7: 1, 8: 2, 9: 2, 10: 4, 11: 4}


def blocks(text):
    return {m.group(2): (int(m.group(1)), m.group(3)) for m in re.finditer(
        r"^--- !u!(\d+) &(-?\d+)\n(.*?)(?=^--- !u!|\Z)", text, re.M | re.S)}


def mesh_data(path):
    text = path.read_text(encoding="utf-8-sig")
    data = text.split("  m_VertexData:\n", 1)[1].split("  m_CompressedMesh:", 1)[0]
    count = int(re.search(r"m_VertexCount: (\d+)", data)[1])
    channels = [dict(zip(("stream", "offset", "format", "dimension"), map(int, m)))
                for m in re.findall(r"- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)", data)]
    blob = bytes.fromhex(re.search(r"_typelessdata: ([0-9a-fA-F]+)", data)[1])
    strides = {}
    for channel in channels:
        if channel["dimension"]:
            strides[channel["stream"]] = max(strides.get(channel["stream"], 0), channel["offset"] + FORMAT_SIZE[channel["format"]] * channel["dimension"])
    starts, cursor = {}, 0
    for stream, stride in sorted(strides.items()):
        cursor = (cursor + 15) & ~15
        starts[stream] = cursor
        cursor += stride * count
    if cursor > len(blob):
        raise ValueError(f"Invalid vertex streams in {path.name}: {cursor} > {len(blob)}")
    uv = channels[4]
    if uv["format"] not in (0, 1) or uv["dimension"] < 2:
        raise ValueError(f"Unsupported UV encoding in {path.name}: {uv}")
    fmt = "<2f" if uv["format"] == 0 else "<2e"
    coords = [struct.unpack_from(fmt, blob, starts[uv["stream"]] + i * strides[uv["stream"]] + uv["offset"]) for i in range(count)]
    if not all(math.isfinite(v) for row in coords for v in row):
        raise ValueError(f"Nonfinite UV in {path.name}")
    index_format = int(re.search(r"m_IndexFormat: (\d+)", text)[1])
    index_blob = bytes.fromhex(re.search(r"m_IndexBuffer: ([0-9a-fA-F]+)", text)[1])
    unit = 2 if index_format == 0 else 4
    indices = struct.unpack("<" + ("H" if unit == 2 else "I") * (len(index_blob) // unit), index_blob)
    submesh_block = text.split("  m_SubMeshes:\n", 1)[1].split("  m_Shapes:", 1)[0]
    faces = []
    for match in re.finditer(r"firstByte: (\d+)\s+indexCount: (\d+)\s+topology: (\d+)\s+baseVertex: (\d+)", submesh_block):
        first, length, topology, base = map(int, match.groups())
        group = 4 if topology == 2 else 3 if topology == 0 else 2 if topology == 3 else None
        if group is None:
            raise ValueError(f"Unsupported topology in {path.name}: {topology}")
        values = [i + base for i in indices[first // unit:first // unit + length]]
        if length % group or any(i >= count for i in values):
            raise ValueError(f"Invalid indices in {path.name}")
        faces.extend(values[i:i + group] for i in range(0, length, group))
    shape_block = re.search(r"    channels:\n(.*?)(?=^    fullWeights:)", text, re.M | re.S)
    shapes = re.findall(r"^    - name: (.*)$", shape_block[1], re.M) if shape_block else []
    bind_block = text.split("  m_BindPose:", 1)[1].split("  m_BoneNameHashes:", 1)[0]
    return {"vertices": count, "faces": len(faces), "bindposes": len(re.findall(r"^  - e00:", bind_block, re.M)),
            "blendshapes": shapes, "uv0_min": [min(v[i] for v in coords) for i in range(2)],
            "uv0_max": [max(v[i] for v in coords) for i in range(2)]}, coords, faces


def uv_svg(title, coords, faces, size=1024):
    edges = set()
    for face in faces:
        for i, a in enumerate(face):
            b = face[(i + 1) % len(face)]
            edges.add(tuple(sorted((a, b))))
    paths = []
    for a, b in sorted(edges):
        u, v = coords[a]
        x, y = coords[b]
        paths.append(f"M{u*size:.3f},{(1-v)*size:.3f}L{x*size:.3f},{(1-y)*size:.3f}")
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}">'
            f'<title>{html.escape(title)} — UV0, v increases upwards</title>'
            f'<rect x=".5" y=".5" width="{size-1}" height="{size-1}" fill="none" stroke="#999"/>'
            f'<path d="{"".join(paths)}" fill="none" stroke="#25716e" stroke-width="1"/>'
            '</svg>')


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "UV").mkdir(exist_ok=True)
    visual = json.loads((ASSETS / "Runtime/Resources/ArenaVisualData.json").read_text(encoding="utf-8"))
    mesh_paths = {}
    for meta in (ASSETS / "Mesh").glob("*.asset.meta"):
        guid = re.search(r"guid: ([0-9a-f]+)", meta.read_text(encoding="utf-8"))[1]
        mesh_paths[guid] = meta.with_suffix("")
    all_renderers, templates, checks = {}, {}, []
    for prefab_name in ("Character_Player_1.prefab", "Character_Enemy.prefab"):
        prefab = ASSETS / "GameObject" / prefab_name
        records = blocks(prefab.read_text(encoding="utf-8-sig"))
        names = {key: re.search(r"^  m_Name: (.*)$", body, re.M)[1] for key, (kind, body) in records.items() if kind == 1}
        transforms = {key: names.get(re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)[1], "") for key, (kind, body) in records.items() if kind == 4}
        renderers = []
        for key, (kind, body) in records.items():
            if kind != 137:
                continue
            owner = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)[1]
            mesh_guid = re.search(r"m_Mesh: \{fileID: \d+, guid: ([0-9a-f]+)", body)[1]
            mesh_path = mesh_paths[mesh_guid]
            bone_block = re.search(r"  m_Bones:\n(.*?)(?=^  [A-Za-z_]|\Z)", body, re.M | re.S)
            bone_ids = re.findall(r"fileID: (-?\d+)", bone_block[1]) if bone_block else []
            info, coords, faces = mesh_data(mesh_path)
            name = names[owner]
            entry = {"slot": name, "mesh": mesh_path.relative_to(PROJECT).as_posix(), "bones": [transforms.get(i, i) for i in bone_ids], **info}
            checks.append({"check": prefab_name + "/" + name + " bindposes", "pass": len(bone_ids) == info["bindposes"]})
            renderers.append(entry)
            if prefab_name == "Character_Player_1.prefab":
                template_name = re.sub(r"[^A-Za-z0-9_-]", "_", name) + ".svg"
                (OUT / "UV" / template_name).write_text(uv_svg(name, coords, faces), encoding="utf-8")
                templates[name] = template_name
        all_renderers[prefab_name] = renderers

    paint_layers, body_textures = [], []
    player_rig = next(iter(visual["rigs"].values()))
    appearance = next(r for r in player_rig if r.get("script") == "CharacterConfigApplier")
    for row in player_rig:
        if row.get("script") != "CwPaintableMeshTexture":
            continue
        data = row["data"]
        paint_layers.append({"slot": row["path"].split("/")[-1], "material_slot": data["slot"],
                             "uv_channel": data["coord"], "fallback_width": data["width"], "fallback_height": data["height"],
                             "texture_assets": visual["assets"].get(row.get("texture"), {}).get("asset_paths", [])})
    for name, key in appearance["textures"].items():
        if not name.startswith(("top", "bottom", "normal")):
            continue
        for relative in visual["assets"].get(key, {}).get("asset_paths", []):
            path = PROJECT / relative
            if path.suffix == ".png" and path.is_file():
                width, height = struct.unpack(">II", path.read_bytes()[16:24])
                body_textures.append({"slot": name, "texture": relative, "width": width, "height": height})
    fallback_bytes = sum(r["fallback_width"] * r["fallback_height"] * 4 for r in paint_layers)
    expressions = json.loads((ASSETS / "Runtime/Resources/ArenaExpressionData.json").read_text(encoding="utf-8"))
    attributes = set()
    for clip in expressions["clips"].values():
        path = ASSETS / "Runtime/Resources" / (clip["resource"] + ".anim")
        if path.is_file():
            attributes.update(re.findall(r"^    attribute: (.*)$", path.read_text(encoding="utf-8-sig"), re.M))

    report = {"scope": "Static art-replacement research; no runtime code, texture, material, mesh, prefab or physics configuration was modified.",
              "renderers": all_renderers, "paint_layers": paint_layers, "body_textures": body_textures,
              "expression_curve_attributes": sorted(attributes), "checks": checks,
              "fallback_argb32_bytes_per_actor": fallback_bytes,
              "all_18_layers_at_1024_argb32_bytes_per_actor": 18 * 1024 * 1024 * 4,
              "memory_scope": "Paint RenderTexture base levels only; excludes original textures, mipmaps, temporary buffers, other materials and the scene.",
              "uv_templates": templates, "pass": all(c["pass"] for c in checks)}
    (OUT / "art-inventory.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    panels = []
    for index, renderer in enumerate(all_renderers["Character_Player_1.prefab"]):
        x, y = (index % 4) * 300 + 20, (index // 4) * 332 + 80
        name = renderer["slot"]
        _, coords, faces = mesh_data(PROJECT / renderer["mesh"])
        paths = re.search(r'<path d="([^"]+)"', uv_svg(name, coords, faces, 256))[1]
        panels.append(f'<g transform="translate({x},{y})"><text x="0" y="0" font-size="18">{html.escape(name)}</text>'
                      f'<text x="0" y="20" font-size="12" fill="#555">{renderer["vertices"]} vertices · {len(renderer["bones"])} bones · {len(renderer["blendshapes"])} shapes</text>'
                      f'<g transform="translate(0,34)"><rect width="256" height="256" fill="#fff" stroke="#aaa"/><path d="{paths}" fill="none" stroke="#25716e" stroke-width=".7"/></g></g>')
    overview = '<svg xmlns="http://www.w3.org/2000/svg" width="1220" height="1740" viewBox="0 0 1220 1740"><rect width="1220" height="1740" fill="#f5f4ef"/><g font-family="Arial,sans-serif" fill="#203633"><text x="20" y="32" font-size="24">Character art — 20 mesh UV0 templates</text><text x="20" y="54" font-size="13">Static mesh coordinates; not a generated character image. Individual transparent SVGs are in UV/.</text>' + ''.join(panels) + '</g></svg>'
    (OUT / "UV-overview.svg").write_text(overview, encoding="utf-8")
    print(json.dumps({"pass": report["pass"], "player_renderers": len(all_renderers["Character_Player_1.prefab"]),
                      "enemy_renderers": len(all_renderers["Character_Enemy.prefab"]), "paint_layers": len(paint_layers),
                      "uv_templates": len(templates), "bindpose_checks": len(checks), "fallback_rt_mib_per_actor": fallback_bytes / 1024**2,
                      "output": str(OUT)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
