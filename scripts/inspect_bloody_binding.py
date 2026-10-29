"""Reproducible static binding study of the user-provided Bloody Bastards APK.

This script never installs/runs the APK and writes only ignored study output.
It deliberately exports numerical evidence and geometry diagrams, not textures
or engine-ready third-party assets. Requires UnityPy, numpy and Pillow.
"""

import argparse
import collections
import hashlib
import json
import math
import zipfile
from pathlib import Path

import numpy as np
import UnityPy
from PIL import Image, ImageDraw, ImageFont
from UnityPy.helpers.MeshHelper import MeshHandler


PREFAB = "00af04d95140a4b44ac8453d91c032ea"
EPS = 1e-5


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")


def vector(v):
    return np.array([v.x, v.y, v.z], dtype=float)


def matrix(m):
    return np.array([[getattr(m, f"e{i}{j}") for j in range(4)] for i in range(4)])


def transform_matrix(t):
    q = t.m_LocalRotation
    x, y, z, w = q.x, q.y, q.z, q.w
    rotation = np.array([
        [1 - 2 * (y*y + z*z), 2 * (x*y - z*w), 2 * (x*z + y*w)],
        [2 * (x*y + z*w), 1 - 2 * (x*x + z*z), 2 * (y*z - x*w)],
        [2 * (x*z - y*w), 2 * (y*z + x*w), 1 - 2 * (x*x + y*y)],
    ])
    result = np.eye(4)
    result[:3, :3] = rotation @ np.diag(vector(t.m_LocalScale))
    result[:3, 3] = vector(t.m_LocalPosition)
    return result


def world_matrix(ptr):
    t = ptr.read()
    local = transform_matrix(t)
    return world_matrix(t.m_Father) @ local if t.m_Father.path_id else local


def transform_ptr(game_object):
    return next(c.component for c in game_object.m_Component
                if c.component.deref().type.name in {"Transform", "RectTransform"})


def transform_chain(ptr):
    t = ptr.read()
    q = t.m_LocalRotation
    record = {"id": ptr.path_id, "owner": t.m_GameObject.read().m_Name,
              "local_position": vector(t.m_LocalPosition).tolist(),
              "local_rotation_xyzw": [q.x, q.y, q.z, q.w],
              "local_scale": vector(t.m_LocalScale).tolist()}
    return (transform_chain(t.m_Father) if t.m_Father.path_id else []) + [record]


def active_chain(game_object):
    t = transform_ptr(game_object).read()
    record = {"name": game_object.m_Name, "active_self": game_object.m_IsActive}
    return (active_chain(t.m_Father.read().m_GameObject.read()) if t.m_Father.path_id else []) + [record]


def skin(vertices, weights, indices, bone_matrices, bind_matrices):
    homogeneous = np.c_[vertices, np.ones(len(vertices))]
    matrices = bone_matrices @ bind_matrices
    result = np.zeros_like(homogeneous)
    for slot in range(weights.shape[1]):
        result += weights[:, slot, None] * np.einsum(
            "nij,nj->ni", matrices[indices[:, slot]], homogeneous)
    return result[:, :3]


def connected_components(vertex_count, triangles):
    parents = list(range(vertex_count))

    def root(i):
        while parents[i] != i:
            parents[i] = parents[parents[i]]
            i = parents[i]
        return i

    for tri in triangles:
        for i in tri[1:]:
            parents[root(int(i))] = root(int(tri[0]))
    groups = collections.defaultdict(list)
    for i in range(vertex_count):
        groups[root(i)].append(i)
    return sorted(groups.values(), key=lambda g: (-len(g), min(g)))


def triangle_area(points):
    return np.linalg.norm(np.cross(points[:, 1] - points[:, 0],
                                   points[:, 2] - points[:, 0]), axis=1) / 2


def signed_area(poly):
    return sum(np.cross(a, b) for a, b in zip(poly, np.roll(poly, -1, axis=0))) / 2


def convex_intersection(subject, clip):
    """Sutherland-Hodgman; used only for projected triangle intersections."""
    clip = clip if signed_area(clip) >= 0 else clip[::-1]
    result = list(subject)
    for a, b in zip(clip, np.roll(clip, -1, axis=0)):
        source, result = result, []
        if not source:
            break
        previous = source[-1]
        prev_distance = np.cross(b - a, previous - a)
        for point in source:
            distance = np.cross(b - a, point - a)
            if (distance >= -1e-12) != (prev_distance >= -1e-12):
                alpha = prev_distance / (prev_distance - distance)
                result.append(previous + alpha * (point - previous))
            if distance >= -1e-12:
                result.append(point)
            previous, prev_distance = point, distance
    return np.asarray(result)


def projected_overlap(a, b):
    total = 0.0
    for ta in a:
        for tb in b:
            overlap = convex_intersection(ta, tb)
            if len(overlap) > 2:
                total += abs(float(signed_area(overlap)))
    return total


def verify_source(archive, cache, name):
    path = "assets/bin/Data/" + name
    names = archive.namelist()
    if path in names:
        paths = [path]
    else:
        paths = sorted((n for n in names if n.startswith(path + ".split")),
                       key=lambda p: int(p.rsplit("split", 1)[1]))
    assert paths, name
    source = b"".join(archive.read(p) for p in paths)
    cached = (cache / name).read_bytes()
    assert source == cached, f"Cache differs from APK: {name}"
    return {"cache_file": name, "apk_entries": paths, "bytes": len(source),
            "sha256": hashlib.sha256(source).hexdigest(), "byte_identical": True}


def pose_delta(pivot, degrees):
    # Source body mesh plane is XZ. Rotate about Y, never stretch an axis.
    angle = math.radians(degrees)
    c, s = math.cos(angle), math.sin(angle)
    result = np.eye(4)
    result[:3, :3] = [[c, 0, -s], [0, 1, 0], [s, 0, c]]
    result[:3, 3] = pivot - result[:3, :3] @ pivot
    return result


def deformation_stats(before, after, triangles, rigid):
    """Principal in-plane stretches per triangle via thin SVD, no projection."""
    output = []
    for mask in (rigid, ~rigid):
        stretches = []
        for tri in triangles[mask]:
            old = np.stack([before[tri[1]] - before[tri[0]],
                            before[tri[2]] - before[tri[0]]], axis=1)
            new = np.stack([after[tri[1]] - after[tri[0]],
                            after[tri[2]] - after[tri[0]]], axis=1)
            u, s, vt = np.linalg.svd(old, full_matrices=False)
            if s[-1] < 1e-10:
                continue
            jacobian = new @ vt.T @ np.diag(1 / s)
            stretches.append(np.linalg.svd(jacobian, compute_uv=False))
        a = np.asarray(stretches)
        output.append({"triangle_count": len(a),
                       "min_principal_stretch": float(a.min()) if len(a) else None,
                       "max_principal_stretch": float(a.max()) if len(a) else None,
                       "max_abs_stretch_error": float(abs(a - 1).max()) if len(a) else None})
    return {"strict_single_bone_triangles": output[0], "other_triangles": output[1]}


def draw_geometry(output, owner, geometry, poses):
    vertices, uv, weights, indices, triangles, rigid, names, bind, worlds = geometry
    font = lambda n: ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", n)
    image = Image.new("RGB", (1720, 820), "#f5f2eb")
    draw = ImageDraw.Draw(image)
    draw.text((30, 18), f"{owner}：固定 UV、分开的刚性肢体、局部柔性过渡", font=font(30), fill="#253240")
    draw.text((30, 63), "APK 静态几何与人为数学试姿；不含贴图，不代表游戏实际动画或碰撞结果。", font=font(19), fill="#65707b")
    original = vertices[:, [0, 2]].copy()
    original[:, 1] *= -1
    lo, hi = original.min(0), original.max(0)
    scale = 430 / max(hi - lo)
    center = (lo + hi) / 2
    colors = ["#d08062", "#aaa05f", "#75a385", "#649ab4", "#8f7bb8", "#b57f9c"]
    entries = [("静态 UV（三角形不随姿势改写）", uv, True),
               ("原始绑定坐标 XZ", vertices, False),
               ("人为屈肘 110°；硬片保持原形", poses["forearm_110"], False)]
    for panel, (title, positions, is_uv) in enumerate(entries):
        x, y = 30 + panel * 565, 150
        draw.text((x, 112), title, font=font(19), fill="#253240")
        draw.rectangle((x, y, x + 530, y + 530), fill="#e6e6e0")
        points = positions[:, :2].copy() if is_uv else positions[:, [0, 2]].copy()
        if is_uv:
            points[:, 1] = 1 - points[:, 1]
            points = points * 490 + 20
        else:
            points[:, 1] *= -1
            points = (points - center) * scale + 265
        for tri, strict in zip(triangles, rigid):
            dominants = indices[tri, np.argmax(weights[tri], axis=1)]
            color = colors[int(dominants[0]) % len(colors)] if strict else "#adb6bf"
            xy = [(x + float(points[i, 0]), y + float(points[i, 1])) for i in tri]
            draw.polygon(xy, fill=color, outline="#f4f2ed")
        mixed = (weights > EPS).sum(1) > 1
        for point in points[mixed]:
            px, py = x + point[0], y + point[1]
            draw.ellipse((px-2.5, py-2.5, px+2.5, py+2.5), fill="#fffdf6", outline="#35424b")
    draw.text((30, 709), "有色三角形：三个顶点均100%受同一骨骼控制；灰色：跨骨或混合，可发生变形。", font=font(21), fill="#253240")
    draw.text((30, 751), "白点：混合权重顶点。单骨顶点之间仍可能存在跨骨三角形，所以不能只统计混合顶点。", font=font(20), fill="#4f5c68")
    image.save(output / f"{owner.lower()}-binding-geometry.png")


def main(args):
    args.output.mkdir(parents=True, exist_ok=True)
    with args.apk.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    with zipfile.ZipFile(args.apk) as archive:
        verified_files = [verify_source(archive, args.serialized, name)
                          for name in (PREFAB, "sharedassets0.assets")]
    env = UnityPy.load(str(args.serialized))
    prefab = next(o.assets_file for o in env.objects if o.assets_file.name == PREFAB)
    records, geometry, auxiliary = [], {}, []
    for obj in prefab.objects.values():
        if obj.type.name in {"MeshRenderer", "SpriteRenderer", "ParticleSystemRenderer"}:
            r = obj.read()
            owner = r.m_GameObject.read()
            chain = active_chain(owner)
            item = {"id": obj.path_id, "type": obj.type.name, "owner": owner.m_Name,
                    "enabled": r.m_Enabled, "active_chain": chain,
                    "active_in_hierarchy": all(row["active_self"] for row in chain),
                    "materials": [p.read().m_Name for p in r.m_Materials if p.path_id]}
            if obj.type.name == "SpriteRenderer":
                item["sprite"] = r.m_Sprite.read().m_Name if r.m_Sprite.path_id else None
            auxiliary.append(item)
        if obj.type.name != "SkinnedMeshRenderer":
            continue
        renderer = obj.read()
        owner = renderer.m_GameObject.read().m_Name
        mesh = renderer.m_Mesh.read()
        helper = MeshHandler(mesh)
        helper.process()
        vertices = np.asarray(helper.m_Vertices, dtype=float)
        uv = np.asarray(helper.m_UV0, dtype=float)
        indices = np.asarray(helper.m_BoneIndices, dtype=int)
        channels = mesh.m_VertexData.m_Channels
        implicit = helper.m_BoneWeights is None
        if implicit:
            # Unity's optimized one-influence stream stores only one index.
            # Require that exact source channel pattern before supplying 1.0.
            assert indices.shape == (len(vertices), 1)
            assert channels[12].dimension == 0 and channels[13].dimension == 1
            weights = np.ones(indices.shape)
        else:
            weights = np.asarray(helper.m_BoneWeights, dtype=float)
        triangles = np.asarray([tri for sub in helper.get_triangles() for tri in sub], dtype=int)
        names = [ptr.read().m_GameObject.read().m_Name for ptr in renderer.m_Bones]
        bind = np.array([matrix(m) for m in mesh.m_BindPose])
        worlds = np.array([world_matrix(ptr) for ptr in renderer.m_Bones])
        assert len(bind) == len(names)
        assert len(vertices) == len(uv) == len(weights) == len(indices)
        assert np.isfinite(vertices).all() and np.isfinite(uv).all()
        assert triangles.min() >= 0 and triangles.max() < len(vertices)
        assert abs(weights.sum(1) - 1).max() < EPS
        assert indices[weights > EPS].min() >= 0
        assert indices[weights > EPS].max() < len(names)
        hard = (weights > EPS).sum(1) == 1
        dominant = indices[np.arange(len(indices)), weights.argmax(1)]
        rigid = hard[triangles].all(1) & (dominant[triangles].min(1) == dominant[triangles].max(1))
        islands = []
        for group in connected_components(len(vertices), triangles):
            group = np.asarray(group)
            mask = np.isin(triangles[:, 0], group)
            influences = sorted(set(indices[group][weights[group] > EPS].tolist()))
            islands.append({"vertex_indices": group.tolist(), "vertex_count": len(group),
                            "triangle_count": int(mask.sum()),
                            "bones": [names[i] for i in influences],
                            "mixed_vertices": int((~hard[group]).sum()),
                            "strict_single_bone_triangles": int(rigid[mask].sum()),
                            "uv_min": uv[group].min(0).tolist(), "uv_max": uv[group].max(0).tolist(),
                            "mesh_min": vertices[group].min(0).tolist(), "mesh_max": vertices[group].max(0).tolist()})
        posed = skin(vertices, weights, indices, worlds, bind)
        singular = np.linalg.svd(bind[:, :3, :3], compute_uv=False)
        uv2area = np.abs(np.cross(uv[triangles[:, 1]] - uv[triangles[:, 0]],
                                 uv[triangles[:, 2]] - uv[triangles[:, 0]]))
        bone_records = []
        for i, ptr in enumerate(renderer.m_Bones):
            t = ptr.read()
            bone_records.append({"name": names[i], "transform_id": ptr.path_id,
                                 "bind_pose": bind[i].tolist(), "world_matrix_serialized_pose": worlds[i].tolist(),
                                 "bind_singular_values": singular[i].tolist(),
                                 "bind_determinant": float(np.linalg.det(bind[i, :3, :3])),
                                 "local_scale": vector(t.m_LocalScale).tolist(),
                                 "parent": t.m_Father.read().m_GameObject.read().m_Name if t.m_Father.path_id else None})
        record = {"owner": owner, "renderer_id": obj.path_id,
                  "mesh_file": mesh.object_reader.assets_file.name, "mesh_id": mesh.object_reader.path_id,
                  "mesh_name": mesh.m_Name, "vertex_count": len(vertices), "triangle_count": len(triangles),
                  "implicit_single_weight_stream": implicit, "mixed_weight_vertices": int((~hard).sum()),
                  "strict_single_bone_triangles": int(rigid.sum()), "potentially_deforming_triangles": int((~rigid).sum()),
                  "weight_sum_max_error": float(abs(weights.sum(1) - 1).max()),
                  "max_weights_per_vertex": int((weights > EPS).sum(1).max()),
                  "degenerate_uv_triangles": int((uv2area < 1e-12).sum()),
                  "degenerate_geometry_triangles": int((triangle_area(vertices[triangles]) < 1e-12).sum()),
                  "uv_sha256": hashlib.sha256(uv.astype("<f8").tobytes()).hexdigest(),
                  "raw_mesh_axis_ranges": np.ptp(vertices, axis=0).tolist(),
                  "serialized_pose_world_axis_ranges": np.ptp(posed, axis=0).tolist(),
                  "renderer_transform_chain": transform_chain(transform_ptr(renderer.m_GameObject.read())),
                  "renderer_sorting_order": renderer.m_SortingOrder,
                  "renderer_sorting_layer_id": renderer.m_SortingLayerID,
                  "blend_shape_channel_names": [s.name for s in mesh.m_Shapes.channels],
                  "serialized_blend_shape_weights": renderer.m_BlendShapeWeights,
                  "bones": bone_records, "topology_islands": islands}
        records.append(record)
        geometry[owner] = (vertices, uv, weights, indices, triangles, rigid, names, bind, worlds)

    # A controlled mathematical test isolates skinning from game physics.
    # Construct bind transforms from inverse bind matrices, then rotate whole
    # descendant frames. This is not a claim about source runtime motion.
    synthetic = []
    for owner in ("Top", "TopL1", "Bottom", "Hair", "Beard"):
        vertices, uv, weights, indices, triangles, rigid, names, bind, worlds = geometry[owner]
        bind_worlds = np.linalg.inv(bind)
        roundtrip = skin(vertices, weights, indices, bind_worlds, bind)
        assert np.max(abs(roundtrip - vertices)) < 1e-6
        poses = {}
        for label, target, degrees in (("forearm_110", "Bone_ForearmR", 110),
                                       ("calf_110", "Bone_CalfR", 110),
                                       ("skirt_65", "Bone_Skirt_CalfR", 65)):
            if target not in names:
                continue
            target_index = names.index(target)
            pivot = bind_worlds[target_index, :3, 3]
            delta = pose_delta(pivot, degrees)
            posed_worlds = bind_worlds.copy()
            # Apply to target + descendants present in this renderer.
            target_ptr = next(o.read().m_Bones for o in prefab.objects.values()
                              if o.type.name == "SkinnedMeshRenderer" and
                              o.read().m_GameObject.read().m_Name == owner)[target_index]
            for index, bone_record in enumerate(next(r for r in records if r["owner"] == owner)["bones"]):
                ptr = prefab.objects[bone_record["transform_id"]].read().m_Father
                ids = [bone_record["transform_id"]]
                while ptr.path_id:
                    ids.append(ptr.path_id)
                    ptr = ptr.read().m_Father
                if target_ptr.path_id in ids:
                    posed_worlds[index] = delta @ bind_worlds[index]
            after = skin(vertices, weights, indices, posed_worlds, bind)
            poses[label] = after
            stats = deformation_stats(vertices, after, triangles, rigid)
            assert stats["strict_single_bone_triangles"]["max_abs_stretch_error"] < 1e-5
            synthetic.append({"owner": owner, "pose": label, "target_bone": target,
                              "degrees": degrees, "uv_changed": False,
                              "bind_roundtrip_max_vertex_error": float(abs(roundtrip - vertices).max()),
                              **stats})
        if owner == "TopL1":
            draw_geometry(args.output, owner, geometry[owner], poses)

    overlaps, bridges = [], []
    for owner in ("Top", "TopL1", "Bottom"):
        vertices, uv, weights, indices, triangles, rigid, names, bind, worlds = geometry[owner]
        posed = skin(vertices, weights, indices, worlds, bind)
        dominant = indices[np.arange(len(indices)), weights.argmax(1)]
        for a, b in (("Bone_ArmR", "Bone_ForearmR"), ("Bone_ArmL", "Bone_ForearmL"),
                     ("Bone_ThighR", "Bone_CalfR"), ("Bone_ThighL", "Bone_CalfL")):
            if a not in names or b not in names:
                continue
            pair_indices = [names.index(a), names.index(b)]
            triangle_bone_sets = [set(indices[t][weights[t] > EPS].tolist()) for t in triangles]
            bridges.append({"owner": owner, "bones": [a, b],
                            "triangles_influenced_by_both": sum(all(i in s for i in pair_indices)
                                                                for s in triangle_bone_sets)})
            selected = []
            for bone in (a, b):
                selected.append(triangles[rigid & (dominant[triangles[:, 0]] == names.index(bone))])
            if any(len(ts) == 0 for ts in selected):
                continue
            area = projected_overlap(*(posed[ts][:, :, :2] for ts in selected))
            overlaps.append({"owner": owner, "bones": [a, b],
                             "projected_triangle_overlap_area_world_xy": area,
                             "piece_world_depth_mean": [float(posed[np.unique(ts), 2].mean()) for ts in selected],
                             "caveat": "Projected mesh coverage in serialized pose only; transparent pixels may not cover this area."})

    evidence = {"source_apk": str(args.apk), "source_sha256": digest,
                "verified_cache_files": verified_files, "prefab": PREFAB,
                "scope": "Static serialized geometry only; APK never installed or executed.",
                "skinning_equation_world": "p_world = sum_i w_i * B_i_world(t) * bind_i * p_mesh; UV remains vertex attribute",
                "strict_rigidity_definition": "All three vertices have exactly one active weight, to the same bone; weights sum to 1. Scale-free pose delta preserves triangle lengths.",
                "renderers": records, "auxiliary_renderers": auxiliary,
                "limb_joint_bridges": bridges, "synthetic_pose_tests": synthetic,
                "projected_joint_overlaps": overlaps,
                "limitations": [
                    "Only one character prefab in this modified distribution is inspected; no whole-game frequency claim.",
                    "A topology component is based on shared vertex indices, not visible alpha silhouettes.",
                    "Default serialized transforms are an authored pose; raw mesh coordinates need not equal that pose or renderer axes.",
                    "Bind matrices contain importer scale, reflections and axis changes. Do not normalize each part independently.",
                    "Source Eyes mesh contains large authored depth coordinates and a compensating anisotropic bind; no whole-eye rest-quality conclusion is made.",
                    "Synthetic tests are mathematical examples, not source animations, ragdoll captures, or an in-engine validation.",
                    "Skin weights alone cannot prove no visible gaps. Texture alpha, depth, transparency and pose coverage also matter.",
                    "Blend shapes exist and can intentionally deform vertices independently of bone motion; defaults and channel names only are recorded.",
                    "Auxiliary renderers are serialized evidence only. No claim is made that runtime code never creates other renderers.",
                ]}
    write_json(args.output / "binding-evidence.json", evidence)
    write_report(args.output, evidence)
    print(json.dumps({"output": str(args.output), "sha256": digest,
                      "renderers": len(records), "synthetic_tests": len(synthetic),
                      "source_cache_files_identical": len(verified_files),
                      "maximum_rigid_triangle_stretch_error": max(
                          r["strict_single_bone_triangles"]["max_abs_stretch_error"] for r in synthetic)}, ensure_ascii=False))


def write_report(output, evidence):
    lines = ["# Bloody Bastards 骨骼贴图静态核查", "",
             "本报告由 `scripts/inspect_bloody_binding.py` 直接读取用户提供 APK 的序列化资产生成，未安装或执行 APK。",
             f"源 SHA-256：`{evidence['source_sha256']}`。角色 prefab 与 sharedassets0.assets 均重新与 APK 内原始字节比对一致。", "",
             "## 结论", "",
             "它使用带 UV 的 SkinnedMeshRenderer。四肢分成相互断开的几何片区，整个上臂或前臂片区由同一根骨骼控制；骨骼旋转使整片转动，因此能保持纹样比例。头、手、脚也大量使用这种方式。衣摆、胸腹连接和头发尾端保留跨骨/混合权重，因此会有受控变形。", "",
             "不能把“一个顶点只跟一根骨骼”直接写成“整个三角形不变形”。一个三角形的三个顶点分别跟随不同骨骼时，三角形仍会伸缩。也不能把 `SkinnedMeshRenderer` 解释成全身软皮。", "",
             "## 逐网格源数据", "",
             "| 网格 | 顶点 | 混合权重顶点 | 三角形 | 严格单骨三角形 | 潜在变形三角形 | 拓扑岛 |",
             "|---|---:|---:|---:|---:|---:|---:|"]
    for r in evidence["renderers"]:
        lines.append(f"| {r['owner']} | {r['vertex_count']} | {r['mixed_weight_vertices']} | {r['triangle_count']} | {r['strict_single_bone_triangles']} | {r['potentially_deforming_triangles']} | {len(r['topology_islands'])} |")
    lines += ["", "严格单骨三角形的三个顶点均100%跟随同一骨骼；表中其他三角形只表示可以变形，未表示每个动作一定变形。优化的单影响网格只有 BoneIndex 通道，脚本仅在源通道12为空、13维度为1时补上隐含权重1。", "",
              "## 上衣 TopL1 的拆片", ""]
    top = next(r for r in evidence["renderers"] if r["owner"] == "TopL1")
    for i, island in enumerate(top["topology_islands"]):
        lines.append(f"- 岛 {i + 1}：{island['vertex_count']} 顶点、{island['triangle_count']} 三角形、{island['mixed_vertices']} 混合顶点；骨骼：{', '.join(island['bones'])}。")
    lines += ["", "上下臂的岛均为独立单骨片，而不是跨肘焊接的一张长网格。几何片存在重叠，纹理的透明部分决定最终可见边缘；这为关节遮盖提供空间，但静态网格不能证明所有极端姿态下都没有缝。", "",
              "## 数学试姿（不是游戏运行画面）", "",
              "从每根骨骼的 bind inverse 重建绑定坐标，在指定关节处施加纯旋转，保持 UV 不变。对每个三角形计算两个面内主伸长率。1表示原长；该计算不把深度投影缩短误当拉伸。", "",
              "| 网格 / 人为姿势 | 严格单骨三角形最大长度误差 | 其他三角形主伸长范围 |",
              "|---|---:|---:|"]
    for r in evidence["synthetic_pose_tests"]:
        s, other = r["strict_single_bone_triangles"], r["other_triangles"]
        lines.append(f"| {r['owner']} / {r['pose']} | {s['max_abs_stretch_error']:.3g} | {other['min_principal_stretch']:.4f}–{other['max_principal_stretch']:.4f} |")
    lines += ["", "## 绑定矩阵、缩放与坐标", "",
              "世界坐标计算为 `p_world = Σ w_i · B_i_world(t) · bind_i · p_mesh`。如果自行算 renderer 局部坐标，左侧还要乘 renderer.worldToLocalMatrix。UV 是独立顶点属性，不应根据当前骨长或姿势重新拉伸。", "",
              "保存的 prefab 不是原始网格坐标的恒等姿势：多数身体网格原始平面是 XZ，而骨架最终位于 XY。源文件确有导入转换、负尺度与层级尺度。例如 Bone_ArmR 的 localScale 为 (-1,-1,-1)，Smeg 为 (1,1,2.45)，Top L4 的 bind 线性部分奇异值约为39.380722。Eyes 还具有很大的原始深度坐标和补偿的非等比 bind。不能独立把每片网格归一化、丢掉 bind，或宣称源数据所有 scale 都是1。", "",
              "原创实现可在导入时把各部件统一换算到同一个米制/像素制绑定空间并烘焙静态转换；之后动画层只更新平移和旋转。镜像与深度层单独管理。若需改变身材，作为明确的角色构建步骤调整网格和 bind，避免每帧按关节间距拉长美术。", "",
              "## 用于自己的游戏", "",
              "1. 使用原创分件作为绑定源，给每片保存明确的近端关节、远端关节、原始像素比例及层级。不要按透明包围框独立缩放每片。",
              "2. 上臂/前臂、大腿/小腿各用独立网格岛。硬片内所有顶点100%给同一骨骼，并检查每个三角形的三个骨索引相同。",
              "3. 肘、膝、肩、髋给出有形状的关节覆盖；让美术边缘跨过旋转轴，按姿势验证覆盖面积。单纯把截断直边贴在关节点会露缝。源几何重叠是证据，具体覆盖形状和像素尺寸仍须按原创图设计。",
              "4. 将少量柔性过渡集中在衣摆、披风、袖口和胸腹连接；重要条纹/刺绣放在刚性片区。跨骨区域用足够细分三角形，并限制转角及相对位移，防止长三角形拉扯。",
              "5. 物理骨架负责关节位置与角度；可见片只复制一个骨架帧的刚体变换。禁止用当前两关节距离设置片区的长轴缩放。物理约束应承担骨长误差，不能靠纹理伸长隐藏。",
              "6. 固定 UV，给纹理边缘留相同颜色的扩边，设置合适采样与 mip/图集 padding。扩边解决采样漏色，不能修复几何拉伸。",
              "7. 验收必须包括原地转身、肘膝大幅折叠、双臂交叉、倒地、受击和复位；记录硬片三角形边长/面积变化、最大关节缝隙、柔性区伸长率，以及遮挡顺序。", "",
              "## 证据边界", ""]
    lines.extend(f"- {s}" for s in evidence["limitations"])
    lines += ["", "完整矩阵、骨名、对象ID、岛的顶点索引、UV范围、投影关节重叠面积及数学试姿结果见 `binding-evidence.json`。示意图 `topl1-binding-geometry.png` 只展示几何，不含商业贴图。", ""]
    (output / "BINDING_STUDY.md").write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("--serialized", type=Path, default=Path("output/bloody-reference-20261028/serialized"))
    parser.add_argument("--output", type=Path, default=Path("output/bloody-binding-study-20261029"))
    main(parser.parse_args())
