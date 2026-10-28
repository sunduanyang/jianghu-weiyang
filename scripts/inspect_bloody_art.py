"""Static art-pipeline study of the user-supplied Bloody Bastards APK.

Requires UnityPy, Pillow and numpy. Uses the serialized cache produced by
inspect_bloody_reference.py; never installs the APK or changes game assets.
Third-party decoded textures and diagrams stay in the ignored output folder.
"""

import argparse
import collections
import hashlib
import json
import re
from pathlib import Path

import numpy as np
import UnityPy
from PIL import Image, ImageDraw, ImageFont
from UnityPy.classes.PPtr import PPtr
from UnityPy.helpers.MeshHelper import MeshHandler
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator


PREFAB = "00af04d95140a4b44ac8453d91c032ea"


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def font(size):
    return ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", size)


def checker(size, step=16):
    result = Image.new("RGBA", size, "#e8e7e3")
    draw = ImageDraw.Draw(result)
    for y in range(0, size[1], step):
        for x in range(0, size[0], step):
            if (x // step + y // step) % 2:
                draw.rectangle((x, y, x + step - 1, y + step - 1), fill="#d4d3cf")
    return result


def texture_panel(texture, size):
    image = texture.convert("RGBA")
    scale = min(size[0] / image.width, size[1] / image.height)
    image = image.resize((round(image.width * scale), round(image.height * scale)), Image.Resampling.NEAREST)
    panel = checker(size)
    panel.alpha_composite(image, ((size[0] - image.width) // 2, (size[1] - image.height) // 2))
    return panel


def mesh_islands(vertices, weights, indices, triangles, bones):
    """Topology components by vertex index; UV seam duplicates stay separate."""
    parents = list(range(len(vertices)))

    def root(index):
        while parents[index] != index:
            parents[index] = parents[parents[index]]
            index = parents[index]
        return index

    for tri in triangles:
        for index in tri[1:]:
            parents[root(int(index))] = root(int(tri[0]))
    groups = collections.defaultdict(list)
    for index in range(len(vertices)):
        groups[root(index)].append(index)
    result = []
    for group in sorted(groups.values(), key=len, reverse=True):
        active_bones = sorted(set(indices[group][weights[group] > 1e-5].tolist()))
        result.append({"vertex_count": len(group),
                       "mixed_weight_vertices": int(((weights[group] > 1e-5).sum(axis=1) > 1).sum()),
                       "bones": [bones[index] for index in active_bones]})
    return result


def main(args):
    args.output.mkdir(parents=True, exist_ok=True)
    samples = args.output / "texture-samples"
    samples.mkdir(exist_ok=True)
    env = UnityPy.load(str(args.serialized))
    files = {obj.assets_file.name: obj.assets_file for obj in env.objects}
    prefab = files[PREFAB]
    generator = TypeTreeGenerator("6000.4.0f1")
    generator.load_local_dll_folder(str(args.dlls))

    def decode_mono(obj):
        base = obj.read(check_read=False)
        script = base.m_Script.read()
        full_name = (script.m_Namespace + "." if script.m_Namespace else "") + script.m_ClassName
        nodes = generator.get_nodes_up(script.m_AssemblyName, full_name)
        tree = obj.read_typetree(nodes=nodes)
        # Generated type trees have different base-header byte alignment.
        # User data is valid, but copy the authoritative Unity base header.
        header = obj.read_typetree(check_read=False)
        for key in ("m_GameObject", "m_Enabled", "m_Script", "m_Name"):
            tree[key] = header[key]
        return {"id": obj.path_id, "owner": base.m_GameObject.read().m_Name,
                "script": script.m_ClassName, "data": tree}

    config = decode_mono(prefab.objects[882])
    assert config["script"] == "CharacterConfigApplier"
    paints = []
    for key, ptr in config["data"].items():
        if "PaintableTexture" in key and ptr.get("m_PathID"):
            record = decode_mono(PPtr(**ptr, assetsfile=prefab).deref())
            record["config_field"] = key
            paints.append(record)
    write_json(args.output / "paint-textures.json", paints)
    # Repair headers in the earlier exploratory evidence too.
    previous = args.output / "paint-and-config-evidence.json"
    if previous.exists():
        write_json(previous, [decode_mono(prefab.objects[row["id"]])
                              for row in json.loads(previous.read_text(encoding="utf-8"))])

    textures = {}
    texture_objects = {}
    for obj in env.objects:
        if obj.type.name == "Texture2D":
            data = obj.read()
            texture_objects.setdefault(data.m_Name, (obj, data))
    exported = {}

    def export_texture(obj, data, relation):
        name = data.m_Name
        if name not in exported:
            image = data.image.convert("RGBA")
            safe_name = re.sub(r"[^\w.\-]", "_", name)
            path = samples / (safe_name + ".png")
            image.save(path)
            alpha = np.asarray(image)[:, :, 3]
            textures[name] = image
            exported[name] = {"name": name, "file": obj.assets_file.name, "id": obj.path_id,
                              "width": data.m_Width, "height": data.m_Height,
                              "format": str(data.m_TextureFormat), "mip_count": data.m_MipCount,
                              "filter_mode": data.m_TextureSettings.m_FilterMode,
                              "alpha_zero_pixels": int((alpha == 0).sum()),
                              "alpha_partial_pixels": int(((alpha > 0) & (alpha < 255)).sum()),
                              "png": str(path.relative_to(args.output)), "relations": []}
        exported[name]["relations"].append(relation)
        return name

    for record in paints:
        ptr = PPtr(**record["data"]["texture"], assetsfile=prefab)
        if ptr.path_id:
            obj = ptr.deref()
            record["source_texture_name"] = export_texture(obj, obj.read(), record["config_field"])
    write_json(args.output / "paint-textures.json", paints)

    chosen = [
        ("SourceTexture_Head_6", "脸型底图｜独立五官叠加"),
        ("SourceTexture_Eyes_18", "眼睛｜独立选择"),
        ("SourceTexture_SourceTexture_Nose_5", "鼻子｜独立选择"),
        ("SourceTexture_SourceTexture_Mouth_2", "嘴巴｜独立选择"),
        ("SourceTexture_Hair_5", "头发｜单独网格"),
        ("SourceTexture_Beard_10", "胡须｜单独网格"),
        ("JS_TopL1_Kimono_Longer_Black", "上衣 UV｜躯干与双袖共图"),
        ("SourceTexture_TopL4_Cloak_4", "披风 UV｜附加服装层"),
    ]
    for name, _ in chosen:
        obj, data = texture_objects[name]
        export_texture(obj, data, "representative library sample; not a runtime outfit assertion")

    renderers = []
    geometry = {}
    for obj in prefab.objects.values():
        if obj.type.name != "SkinnedMeshRenderer":
            continue
        renderer = obj.read()
        owner = renderer.m_GameObject.read().m_Name
        mesh = renderer.m_Mesh.read()
        helper = MeshHandler(mesh)
        helper.process()
        bones = [ptr.read().m_GameObject.read().m_Name for ptr in renderer.m_Bones]
        record = {"renderer_id": obj.path_id, "owner": owner, "mesh_name": mesh.m_Name,
                  "vertex_count": len(helper.m_Vertices), "bones": bones,
                  "materials": [ptr.read().m_Name for ptr in renderer.m_Materials]}
        if owner in {"Top", "TopL1", "TopL2", "TopL3", "Top L4", "Hair", "Beard", "Gloves"}:
            vertices = np.asarray(helper.m_Vertices)
            weights = np.asarray(helper.m_BoneWeights)
            indices = np.asarray(helper.m_BoneIndices)
            uv = np.asarray(helper.m_UV0)
            triangles = np.asarray([tri for submesh in helper.get_triangles() for tri in submesh])
            assert vertices.shape[0] == weights.shape[0] == uv.shape[0]
            assert np.isfinite(vertices).all() and np.isfinite(uv).all()
            assert triangles.min() >= 0 and triangles.max() < len(vertices)
            assert np.allclose(weights.sum(axis=1), 1.0, atol=1e-5)
            assert indices[weights > 1e-5].max() < len(bones)
            record.update(triangle_count=len(triangles),
                          mixed_weight_vertices=int(((weights > 1e-5).sum(axis=1) > 1).sum()),
                          max_weights_per_vertex=int((weights > 1e-5).sum(axis=1).max()),
                          weight_sum_max_error=float(abs(weights.sum(axis=1) - 1).max()),
                          local_axis_ranges=np.ptp(vertices, axis=0).tolist(),
                          topology_islands=mesh_islands(vertices, weights, indices, triangles, bones))
            geometry[owner] = (vertices, uv, weights, indices, triangles)
        renderers.append(record)

    inventory = json.loads((args.output / "texture-inventory.json").read_text(encoding="utf-8"))
    categories = collections.Counter()
    sizes = collections.defaultdict(collections.Counter)
    for item in inventory:
        name = item["name"]
        if name.startswith("SourceTexture_"):
            name = re.sub(r"^(SourceTexture_)+", "", name)
            category = name.split("_")[0]
            categories[category] += 1
            sizes[category][f'{item["width"]}x{item["height"]}'] += 1

    shaders = {}
    for short_name in ("_UniversalCharacter", "Character_TopL1"):
        parsed = json.loads((args.output / f"shader-{short_name}.json").read_text())["m_ParsedForm"]
        sub = parsed["m_SubShaders"][0]
        state = sub["m_Passes"][0]["m_State"]
        shaders[short_name] = {"name": parsed["m_Name"], "properties": parsed["m_PropInfo"],
                               "subshader_tags": sub["m_Tags"],
                               "first_pass": {key: state[key] for key in
                                              ("m_Name", "rtBlend0", "zWrite", "zTest", "culling", "m_Tags")}}

    symbols_path = args.serialized.parent / "symbols.json"
    symbols = json.loads(symbols_path.read_text()) if symbols_path.exists() else []
    apply_methods = [row["name"] for row in symbols
                     if row.get("type") == "CharacterConfigApplier" and row["name"].startswith("Apply")]
    with args.apk.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    evidence = {
        "source_apk": str(args.apk), "source_bytes": args.apk.stat().st_size, "sha256": digest,
        "unity_version": "6000.4.0f1", "character_prefab": PREFAB,
        "scope": "Static serialized asset inspection; no APK installation or runtime capture.",
        "renderers": renderers, "config": config, "paint_components": paints,
        "representative_textures": list(exported.values()),
        "source_texture_categories": categories, "source_texture_sizes": dict(sizes),
        "apply_method_symbols": apply_methods, "shaders": shaders,
        "limitations": [
            "SourceTexture_ counts exclude regional JS/HRE/RI names and are not whole-game totals.",
            "20 renderer slots in this prefab does not imply 20 active renderers or 20 draw calls.",
            "Material saved properties include stale fields; only current shader properties/state are cited.",
            "Default shared material texture is not the configured final texture for every body region.",
            "Geometry validation is limited to explicitly checked meshes; other MeshHelper decoding can be inaccurate.",
            "PaintIn3D component wiring is observed; the complete hit-to-blood rendering path is not verified.",
            "The supplied APK is a modified distribution; findings describe this file.",
        ],
    }
    write_json(args.output / "art-study-evidence.json", evidence)

    # Nearest-neighbour enlargement and real alpha compositing reveal the
    # authored pixels without treating invisible RGB values as visible paint.
    sheet = Image.new("RGB", (1480, 1010), "#f6f3ec")
    draw = ImageDraw.Draw(sheet)
    draw.text((34, 22), "Bloody Bastards｜实际贴图样本", font=font(34), fill="#252a2e")
    draw.text((36, 72), "APK 静态解码 · 棋盘格表示透明 · 原始低分辨率贴图按最近邻放大", font=font(20), fill="#555b62")
    for index, (name, label) in enumerate(chosen):
        x, y = 34 + (index % 4) * 362, 126 + (index // 4) * 422
        sheet.paste(texture_panel(textures[name], (324, 300)).convert("RGB"), (x, y))
        draw.text((x, y + 310), label, font=font(19), fill="#252a2e")
        draw.text((x, y + 342), f'{textures[name].width} × {textures[name].height} px', font=font(17), fill="#61666c")
        # Names are precise evidence links; shorten long names for the card.
        short = re.sub(r"^(SourceTexture_)+", "", name)
        if len(short) > 32:
            short = short[:29] + "…"
        draw.text((x, y + 369), short, font=font(14), fill="#61666c")
    draw.text((34, 977), "这些是 UV 空间的贴图；并非已经摆好位置、能直接旋转绑定的肢体 PNG。", font=font(19), fill="#323b46")
    sheet.save(args.output / "texture-study.png")

    # Side-by-side actual UV sheet, UV triangles and bind-pose weights.
    diagram = Image.new("RGB", (1680, 860), "#f6f3ec")
    draw = ImageDraw.Draw(diagram)
    draw.text((30, 20), "一张衣服贴图，如何跟着多根骨骼动？", font=font(34), fill="#252a2e")
    draw.text((32, 73), "TopL1 实际网格：202 顶点 · 258 三角形 · 16 根骨骼 · 62 个顶点混合多个骨骼权重", font=font(22), fill="#555b62")
    image = textures["JS_TopL1_Kimono_Longer_Black"]
    panel_size = (500, 500)
    positions = [(32, 180), (587, 180), (1142, 180)]
    labels = ["① 美术在统一 UV 上画衣服", "② 同一 UV 上的三角网格", "③ 网格绑定到骨骼（静态绑定姿势）"]
    for pos, label in zip(positions, labels):
        draw.text((pos[0], 135), label, font=font(20), fill="#252a2e")
    diagram.paste(texture_panel(image, panel_size).convert("RGB"), positions[0])
    overlay = texture_panel(image, panel_size)
    od = ImageDraw.Draw(overlay)
    vertices, uv, weights, indices, triangles = geometry["TopL1"]
    uv_points = [(float(u) * 500, (1 - float(v)) * 500) for u, v in uv]
    for tri in triangles:
        od.line([uv_points[int(i)] for i in (*tri, tri[0])], fill="#05a7c7", width=1)
    diagram.paste(overlay.convert("RGB"), positions[1])
    panel = checker(panel_size).convert("RGB")
    pd = ImageDraw.Draw(panel)
    xz = vertices[:, [0, 2]].copy()
    xz[:, 1] *= -1
    lower, upper = xz.min(axis=0), xz.max(axis=0)
    scale = 450 / max(upper - lower)
    points = (xz - (lower + upper) / 2) * scale + 250
    colors = ["#c85b5b", "#dc8b3e", "#cfb637", "#82aa49", "#45a37b", "#4a9fa8", "#4f83bf", "#796cbd",
              "#a56ab6", "#c36e99", "#846a50", "#7792b5", "#bba368", "#6a9c96", "#9d7e9d", "#70915a"]
    for tri in triangles:
        bone = int(indices[int(tri[0]), int(np.argmax(weights[int(tri[0])]))])
        pd.polygon([tuple(points[int(i)]) for i in tri], fill=colors[bone], outline="#eae6dd")
    mixed = (weights > 1e-5).sum(axis=1) > 1
    for point in points[mixed]:
        px, py = point
        pd.ellipse((px - 3, py - 3, px + 3, py + 3), fill="#f9faf3", outline="#343c41")
    diagram.paste(panel, positions[2])
    draw.text((32, 702), "256 × 256 像素；示例款式", font=font(20), fill="#555b62")
    draw.text((587, 702), "固定 UV 模板，换纹样即可换衣服", font=font(20), fill="#555b62")
    draw.text((1142, 702), "颜色＝主权重骨骼；白点＝混合权重", font=font(18), fill="#555b62")
    draw.text((32, 759), "刚柔混合：上臂、前臂片区大多跟随单骨；混合权重集中在衣摆，保留硬片感。", font=font(24), fill="#252a2e")
    draw.text((32, 807), "本图为 APK 数据可视化，不是游戏运行画面。第③图单独归一化了网格坐标。", font=font(18), fill="#61666c")
    diagram.save(args.output / "uv-and-weights.png")
    print(json.dumps({"sha256": digest, "renderer_slots": len(renderers),
                      "paint_components": len(paints), "exported_texture_samples": len(exported),
                      "geometry_checks": {row["owner"]: row.get("mixed_weight_vertices") for row in renderers
                                          if "mixed_weight_vertices" in row},
                      "output": str(args.output)}, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("--serialized", type=Path, default=Path("output/bloody-reference-20261028/serialized"))
    parser.add_argument("--dlls", type=Path, default=Path("output/bloody-reference-20261028/dummy-attributes"))
    parser.add_argument("--output", type=Path, default=Path("output/bloody-art-study-20261028"))
    main(parser.parse_args())
