"""
Ticket 152 — production headless FBX bake.

Import a raw FBX, halt if the safety net trips, otherwise floor-origin the
mesh and re-export with the confirmed Unity-safe flags. Sibling PNG maps
are copied next to the output (not via an .fbm folder).

Run (no GUI, not BlenderMCP):
  blender --background --python BlenderSource/Bake/_bake_fbx.py -- \\
    --input <fbx> --output <fbx> [--decimate] [--kind Pickable|Nodes] [--target-tris N]

Ticket 163: --decimate is opt-in (Drop/Decimate/{Pickable,Nodes} only).
Light cleanup (merge / fill holes / recalc normals) then Collapse to
target_tris. Voxel Remesh is parked — it destroys UVs.
Normal Drop/{Pickable,Nodes} must omit --decimate — same bake as pre-163.

Exit codes:
  0  baked
  1  error (missing input, no meshes, export failed)
  2  safety net: keyword, mesh_count > 1, or 163 Collapse stall
     (tris_after > target_tris * stall_multiple). Copies unmodified source
     to Assets/Objects/_Review/<stem>/ and writes NEEDS_REVIEW.json — no baked FBX

Safety net HALTs only on moving-part keywords or mesh_object_count > 1.
Island count is not a trigger (Meshy static props routinely have 10+ islands).
"""
from __future__ import annotations

import json
import math
import os
import re
import shutil
import sys

import bpy
import bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import _decimate_math as decimate_math

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_REVIEW_ROOT = os.path.join(ROOT, "Assets", "Objects", "_Review")

# Confirmed 2026-08-26 (tickets 151/152, sttvR8QE export flags).
# path_mode=STRIP: textures are copied as sibling files, not via .fbm/COPY.
FBX_EXPORT = dict(
    use_selection=True,
    axis_forward="-Z",
    axis_up="Y",
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    bake_space_transform=True,
    mesh_smooth_type="FACE",
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    path_mode="STRIP",
    embed_textures=False,
)

# Whole-token match on path / folders / object+mesh names.
# Underscore, dash, and CamelCase are separators. Not substrings:
# "indoor" must not trip "door". window/wheel intentionally omitted.
MOVING_PART_KEYWORDS = (
    "door",
    "hinge",
    "drawer",
    "lid",
    "oven",
    "cooker",
    "stove",
    "fridge",
    "freezer",
    "cabinet",
    "cupboard",
    "wardrobe",
    "chest",
    "gate",
    "hatch",
    "shutter",
    "flap",
)

_CAMEL_SPLIT = re.compile(r"[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z]+|[A-Z]+|[0-9]+")

# Sibling maps in the FBX's folder only (not recursive, not .fbm).
_MAP_PATTERNS = (
    re.compile(r"(?i)^meshy_basecolor\.png$"),
    re.compile(r"(?i).+_texture\.png$"),
    re.compile(r"(?i)^meshy_normal\.png$"),
    re.compile(r"(?i).+_normal\.png$"),
    re.compile(r"(?i).+_roughness\.png$"),
    re.compile(r"(?i)^meshy_metallic_smoothness\.png$"),
    re.compile(r"(?i).+_metallic\.png$"),
    re.compile(r"(?i)^meshy_emission\.png$"),
    re.compile(r"(?i).+_emission\.png$"),
)


def argv_after_double_dash():
    if "--" not in sys.argv:
        return []
    return sys.argv[sys.argv.index("--") + 1 :]


def parse_args():
    args = argv_after_double_dash()
    inp = None
    out = None
    review_root = DEFAULT_REVIEW_ROOT
    decimate = False
    target_tris = None
    kind = None
    i = 0
    while i < len(args):
        if args[i] == "--input" and i + 1 < len(args):
            inp = args[i + 1]
            i += 2
            continue
        if args[i] == "--output" and i + 1 < len(args):
            out = args[i + 1]
            i += 2
            continue
        if args[i] == "--review-root" and i + 1 < len(args):
            review_root = args[i + 1]
            i += 2
            continue
        if args[i] == "--decimate":
            decimate = True
            i += 1
            continue
        if args[i] == "--target-tris" and i + 1 < len(args):
            target_tris = int(args[i + 1])
            i += 2
            continue
        if args[i] == "--kind" and i + 1 < len(args):
            kind = args[i + 1]
            i += 2
            continue
        i += 1
    if not inp or not out:
        print("BAKE_FAIL usage: --input <fbx> --output <fbx> [--review-root <dir>] [--decimate] [--kind Pickable|Nodes] [--target-tris N]")
        sys.exit(1)
    if not os.path.isabs(inp):
        inp = os.path.join(ROOT, inp)
    if not os.path.isabs(out):
        out = os.path.join(ROOT, out)
    if not os.path.isabs(review_root):
        review_root = os.path.join(ROOT, review_root)
    return (
        os.path.abspath(inp),
        os.path.abspath(out),
        os.path.abspath(review_root),
        decimate,
        target_tris,
        kind,
    )


def rel(path):
    try:
        return os.path.relpath(path, ROOT)
    except ValueError:
        return path


def deg(euler):
    return [round(math.degrees(v), 4) for v in euler]


def tokenize(text):
    tokens = set()
    for chunk in re.split(r"[^A-Za-z0-9]+", text):
        if not chunk:
            continue
        tokens.add(chunk.lower())
        for part in _CAMEL_SPLIT.findall(chunk):
            p = part.lower()
            if p:
                tokens.add(p)
    return tokens


def collect_name_tokens(src, meshes):
    tokens = set()
    tokens |= tokenize(rel(src))
    tokens |= tokenize(os.path.basename(src))
    parent = os.path.dirname(src)
    while parent and parent != ROOT and os.path.abspath(parent) != os.path.abspath(ROOT):
        tokens |= tokenize(os.path.basename(parent))
        nxt = os.path.dirname(parent)
        if nxt == parent:
            break
        parent = nxt
    for obj in meshes:
        tokens |= tokenize(obj.name)
        if obj.data is not None:
            tokens |= tokenize(obj.data.name)
    return tokens


def keyword_hits(tokens):
    return sorted(k for k in MOVING_PART_KEYWORDS if k in tokens)


def mesh_world_bounds(objs):
    xs, ys, zs = [], [], []
    for obj in objs:
        if obj.type != "MESH" or obj.data is None:
            continue
        for v in obj.data.vertices:
            w = obj.matrix_world @ v.co
            xs.append(w.x)
            ys.append(w.y)
            zs.append(w.z)
    if not xs:
        return None
    return {
        "min": [round(min(xs), 6), round(min(ys), 6), round(min(zs), 6)],
        "max": [round(max(xs), 6), round(max(ys), 6), round(max(zs), 6)],
        "size": [
            round(max(xs) - min(xs), 6),
            round(max(ys) - min(ys), 6),
            round(max(zs) - min(zs), 6),
        ],
    }


def snapshot(label, meshes):
    rows = []
    for obj in meshes:
        loc = obj.matrix_world.translation
        rows.append(
            {
                "name": obj.name,
                "type": obj.type,
                "location": [round(loc.x, 6), round(loc.y, 6), round(loc.z, 6)],
                "rotation_euler_deg": deg(obj.rotation_euler),
                "scale": [
                    round(obj.scale.x, 6),
                    round(obj.scale.y, 6),
                    round(obj.scale.z, 6),
                ],
            }
        )
    return {"label": label, "objects": rows, "world_bounds": mesh_world_bounds(meshes)}


def select_meshes(meshes):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]


def apply_floor_origin(meshes):
    """Origin at AABB bottom-center (Blender Z-up floor). Then Apply All.

    After bake_space_transform, Unity Renderer.bounds.min.y should be ~0.
    Does not rescale. Ticket 150 overrides scale/pivot in Unity later.
    """
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    select_meshes(meshes)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    bounds = mesh_world_bounds(meshes)
    if not bounds:
        return bounds
    mn, mx = bounds["min"], bounds["max"]
    cursor = Vector(((mn[0] + mx[0]) * 0.5, (mn[1] + mx[1]) * 0.5, mn[2]))
    bpy.context.scene.cursor.location = cursor
    select_meshes(meshes)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    for obj in meshes:
        obj.location = (0.0, 0.0, 0.0)
    select_meshes(meshes)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return mesh_world_bounds(meshes)


def list_sibling_maps(src_fbx):
    folder = os.path.dirname(src_fbx)
    found = []
    try:
        names = os.listdir(folder)
    except OSError:
        return found
    for name in names:
        if not name.lower().endswith(".png"):
            continue
        if any(p.search(name) for p in _MAP_PATTERNS):
            found.append(os.path.join(folder, name))
    found.sort()
    return found


def copy_files(paths, dest_dir):
    os.makedirs(dest_dir, exist_ok=True)
    copied = []
    for src in paths:
        dest = os.path.join(dest_dir, os.path.basename(src))
        shutil.copy2(src, dest)
        copied.append(rel(dest))
    return copied


def write_json(path, payload):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        json.dump(payload, f, indent=2)
        f.write("\n")


def tri_count(meshes):
    total = 0
    for obj in meshes:
        if obj.type != "MESH" or obj.data is None:
            continue
        obj.data.calc_loop_triangles()
        total += len(obj.data.loop_triangles)
    return total


def vert_count(meshes):
    return sum(len(o.data.vertices) for o in meshes if o.type == "MESH" and o.data)


def edge_topology(meshes):
    boundary = 0
    nonmanifold = 0
    for obj in meshes:
        if obj.type != "MESH" or obj.data is None:
            continue
        bm = bmesh.new()
        try:
            bm.from_mesh(obj.data)
            bm.edges.ensure_lookup_table()
            for e in bm.edges:
                if e.is_boundary:
                    boundary += 1
                if not e.is_manifold:
                    nonmanifold += 1
        finally:
            bm.free()
    return {"boundary_edges": boundary, "nonmanifold_edges": nonmanifold}


def uv_layer_count(meshes):
    n = 0
    for obj in meshes:
        if obj.type == "MESH" and obj.data is not None:
            n += len(obj.data.uv_layers)
    return n


def apply_light_cleanup(meshes, merge_dist, report):
    """Merge by Distance + Fill Holes + consistent normals. Keeps UVs."""
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    merge_op = "merge_by_distance"
    for obj in meshes:
        if obj.type != "MESH":
            continue
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        try:
            bpy.ops.mesh.merge_by_distance(threshold=merge_dist)
        except Exception:
            bpy.ops.mesh.remove_doubles(threshold=merge_dist)
            merge_op = "remove_doubles"
        bpy.ops.mesh.fill_holes(sides=0)
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode="OBJECT")
    report["decimate"]["light_cleanup"] = {
        "merge_op": merge_op,
        "merge_dist": merge_dist,
        "fill_holes": True,
        "normals_make_consistent": True,
    }


def apply_voxel_remesh(meshes, voxel_size, report):
    """Parked. Rebuilds topology and wipes UVs. Do not call from 163."""
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for obj in meshes:
        if obj.type != "MESH":
            continue
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        mod = obj.modifiers.new(name="Ticket163_VoxelRemesh", type="REMESH")
        mod.mode = "VOXEL"
        mod.voxel_size = voxel_size
        if hasattr(mod, "adaptivity"):
            mod.adaptivity = 0.0
        bpy.ops.object.modifier_apply(modifier=mod.name)
        report["modifiers"].append(
            {"object": obj.name, "type": "REMESH", "mode": "VOXEL", "voxel_size": voxel_size}
        )


def apply_decimate_to_target(meshes, target_tris, report):
    current = tri_count(meshes)
    report["decimate_before_tris"] = current
    if current <= 0:
        report["decimate_skipped"] = "no triangles"
        return
    if current <= target_tris:
        report["decimate_skipped"] = "already at or below target"
        report["decimate_ratio"] = 1.0
        return
    ratio = max(0.0001, min(1.0, float(target_tris) / float(current)))
    report["decimate_ratio"] = ratio
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for obj in meshes:
        if obj.type != "MESH":
            continue
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        mod = obj.modifiers.new(name="Ticket163_Decimate", type="DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        mod.use_collapse_triangulate = False
        bpy.ops.object.modifier_apply(modifier=mod.name)
        report["modifiers"].append(
            {
                "object": obj.name,
                "type": "DECIMATE",
                "mode": "COLLAPSE",
                "ratio": ratio,
                "target_tris": target_tris,
            }
        )


def run_decimate_stage(meshes, report, target_tris_override, kind=None):
    cfg = decimate_math.load_decimate_config()
    target = decimate_math.resolve_target_tris(kind, target_tris_override)
    bounds = mesh_world_bounds(meshes)
    size = bounds["size"] if bounds else [0, 0, 0]
    longest = max(size) if size else 0.0
    report["decimate"] = {
        "enabled": True,
        "recipe": "light_cleanup_then_collapse",
        "voxel_remesh": False,
        "voxel_remesh_note": (
            "Parked: Voxel Remesh destroys UVs. 2026-09-02 probe on the 3.05M "
            "Meshy drop: Collapse alone hit 2899 tris with UVs intact "
            "(topology was 2 boundary / 3 non-manifold, not the spike's 6303/3837)."
        ),
        "target_tris": target,
        "target_tris_kind": kind,
        "target_tris_flagged_for_sam": False,
        "target_tris_note": (
            "JSON target_tris / target_tris_pickable / target_tris_nodes "
            "(Drop folder chooses category). Override with --target-tris."
        ),
        "longest_aabb_m": longest,
        "topology_before": edge_topology(meshes),
        "tris_raw": tri_count(meshes),
        "verts_raw": vert_count(meshes),
        "uv_layers_raw": uv_layer_count(meshes),
    }
    topo = report["decimate"]["topology_before"]
    raw_tris = report["decimate"]["tris_raw"]
    already_simple = (
        raw_tris <= target
        and topo.get("boundary_edges", 0) == 0
        and topo.get("nonmanifold_edges", 0) == 0
    )
    if already_simple:
        report["decimate"]["skipped"] = "already <= target and manifold"
        report["decimate"]["tris_after_decimate"] = raw_tris
        report["steps"].append(
            "163 skip remesh/decimate (tris=%d <= target=%d, manifold)"
            % (raw_tris, target)
        )
        return

    report["modifiers"] = []
    apply_light_cleanup(meshes, 0.0001, report)
    report["decimate"]["topology_after_cleanup"] = edge_topology(meshes)
    report["decimate"]["tris_after_cleanup"] = tri_count(meshes)
    report["decimate"]["uv_layers_after_cleanup"] = uv_layer_count(meshes)
    report["steps"].append(
        "163 light cleanup (merge 0.0001, fill holes, recalc normals) → %d tris, uv_layers=%d"
        % (
            report["decimate"]["tris_after_cleanup"],
            report["decimate"]["uv_layers_after_cleanup"],
        )
    )
    apply_decimate_to_target(meshes, target, report)
    report["decimate"]["tris_after_decimate"] = tri_count(meshes)
    report["decimate"]["verts_after_decimate"] = vert_count(meshes)
    report["decimate"]["topology_after_decimate"] = edge_topology(meshes)
    report["decimate"]["uv_layers_after_decimate"] = uv_layer_count(meshes)
    report["steps"].append(
        "163 decimate COLLAPSE target=%d ratio=%s → %d tris uv_layers=%d"
        % (
            target,
            report.get("decimate_ratio"),
            report["decimate"]["tris_after_decimate"],
            report["decimate"]["uv_layers_after_decimate"],
        )
    )
    if "decimate_ratio" in report:
        report["decimate"]["ratio"] = report["decimate_ratio"]
    after = report["decimate"]["tris_after_decimate"]
    limit, multiple = decimate_math.stall_limit_tris(target)
    report["decimate"]["stall_multiple"] = multiple
    report["decimate"]["stall_limit_tris"] = limit
    if after > limit:
        report["decimate"]["stalled"] = True
        report["reasons"].append(
            "decimate_stall tris_after=%d > %d (target=%d × %.1fx)"
            % (after, limit, target, multiple)
        )
        report["steps"].append(
            "163 STALL halt _Review (%d tris > %d limit)" % (after, limit)
        )


def halt_for_review(src, review_root, report):
    stem = os.path.splitext(os.path.basename(src))[0]
    dest_dir = os.path.join(review_root, stem)
    os.makedirs(dest_dir, exist_ok=True)
    maps = list_sibling_maps(src)
    copied = copy_files([src] + maps, dest_dir)
    payload = {
        "ticket": 152,
        "status": "needs_review",
        "input": rel(src),
        "review_dir": rel(dest_dir),
        "reasons": report["reasons"],
        "keywords_matched": report["keywords_matched"],
        "mesh_count": report["mesh_count"],
        "mesh_names": report["mesh_names"],
        "world_bounds": report.get("world_bounds"),
        "copied": copied,
        "baked_fbx": False,
        "halted": True,
        "decimate": report.get("decimate"),
        "tris": report.get("tris"),
        "tris_after_decimate": (report.get("decimate") or {}).get("tris_after_decimate"),
    }
    write_json(os.path.join(dest_dir, "NEEDS_REVIEW.json"), payload)
    print("BAKE_REVIEW", rel(dest_dir))
    print(json.dumps(payload, indent=2))
    sys.exit(2)


def main():
    src, dst, review_root, do_decimate, target_tris, kind = parse_args()
    report = {
        "ticket": 152,
        "blender": bpy.app.version_string,
        "input": rel(src),
        "output": rel(dst),
        "gui": False,
        "background": True,
        "decimate_requested": do_decimate,
        "decimate_kind": kind,
        "steps": [],
        "reasons": [],
        "keywords_matched": [],
        "ok": False,
    }

    if not os.path.isfile(src):
        report["error"] = "input FBX missing: " + rel(src)
        write_json(os.path.join(HERE, "_last_error.json"), report)
        print("BAKE_FAIL", report["error"])
        sys.exit(1)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.unit_settings.length_unit = "METERS"
    report["steps"].append("units: METRIC scale_length=1.0 METERS")

    bpy.ops.import_scene.fbx(filepath=src)
    report["steps"].append("imported FBX")

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    others = [o.name + ":" + o.type for o in bpy.context.scene.objects if o.type != "MESH"]
    report["mesh_count"] = len(meshes)
    report["non_mesh"] = others
    report["mesh_names"] = [o.name for o in meshes]

    if not meshes:
        report["error"] = "no mesh objects after FBX import"
        write_json(os.path.join(HERE, "_last_error.json"), report)
        print("BAKE_FAIL", report["error"])
        sys.exit(1)

    report["world_bounds"] = mesh_world_bounds(meshes)
    report["tris"] = tri_count(meshes)
    report["keywords_matched"] = keyword_hits(collect_name_tokens(src, meshes))

    # Safety net (2026-08-26): HALT on keyword match or mesh_object_count > 1.
    # Island count is not a trigger — Meshy static props have 10+ islands
    # from generation, not moving-part separation.
    if report["mesh_count"] > 1:
        report["reasons"].append(
            "mesh_count=%d (more than one mesh object)" % report["mesh_count"]
        )
    for kw in report["keywords_matched"]:
        report["reasons"].append("keyword:%s" % kw)

    if report["reasons"]:
        halt_for_review(src, review_root, report)

    # Ticket 163 — opt-in only. Safety net above is unchanged and still
    # fires on mesh_count > 1 before any remesh/decimate.
    if do_decimate:
        run_decimate_stage(meshes, report, target_tris, kind)
        if report["reasons"]:
            halt_for_review(src, review_root, report)

    report["before_floor_origin"] = snapshot("before_floor_origin", meshes)
    after_bounds = apply_floor_origin(meshes)
    report["steps"].append(
        "floor-origin: AABB bottom-center (Blender min-Z=0), then Apply All Transforms"
    )
    report["after_floor_origin"] = snapshot("after_floor_origin", meshes)
    report["after_floor_origin_bounds"] = after_bounds
    report["tris_after_bake"] = tri_count(meshes)

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    select_meshes(meshes)
    bpy.ops.export_scene.fbx(filepath=dst, **FBX_EXPORT)
    report["steps"].append(
        "export_scene.fbx axis_forward=-Z axis_up=Y global_scale=1 apply_unit_scale "
        "FBX_SCALE_ALL bake_space_transform path_mode=STRIP"
    )
    report["output_exists"] = os.path.isfile(dst)
    report["output_bytes"] = os.path.getsize(dst) if report["output_exists"] else 0

    maps = list_sibling_maps(src)
    report["textures_copied"] = copy_files(maps, os.path.dirname(dst))
    report["steps"].append("copied %d sibling PNG maps (not .fbm)" % len(maps))

    report["ok"] = bool(report["output_exists"] and report["output_bytes"] > 0)
    report["baked_fbx"] = report["ok"]
    report["note_scale"] = (
        "1 Blender unit = 1m = 1 Unity unit as authored. This script does not "
        "rescale. Ticket 150 scale multiplier + pivot offset are Unity-side."
    )

    bake_json = os.path.splitext(dst)[0] + ".bake.json"
    write_json(bake_json, report)
    print("BAKE_OK", rel(dst))
    print(json.dumps(report, indent=2))
    if not report["ok"]:
        sys.exit(1)


if __name__ == "__main__":
    main()
