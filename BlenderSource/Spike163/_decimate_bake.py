"""
SPIKE (pre-163) — throwaway: decimate one Meshy mesh, then bake it the way
ticket 152 bakes production drops.

Reuses 152's recipe verbatim (units, floor-origin, FBX export flags, sibling
PNG copy). The only addition is a Collapse-mode Decimate modifier applied
before the floor-origin step. Nothing here is wired into the automated
drop-folder flow (148/152/153/149/150) — this file is run by hand.

Run:
  Blender --background --python BlenderSource/Spike163/_decimate_bake.py -- \
    --input <fbx> --output <fbx> --ratio 0.5

Reports BEFORE/AFTER triangle counts to stdout and to <output>.decimate.json.
"""
from __future__ import annotations

import json
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "BlenderSource", "Bake"))

# 152's own helpers — do not re-implement the recipe here.
import _bake_fbx as bake  # noqa: E402


def parse_args():
    args = bake.argv_after_double_dash()
    inp = out = None
    ratio = 0.5
    i = 0
    while i < len(args):
        if args[i] == "--input" and i + 1 < len(args):
            inp, i = args[i + 1], i + 2
            continue
        if args[i] == "--output" and i + 1 < len(args):
            out, i = args[i + 1], i + 2
            continue
        if args[i] == "--ratio" and i + 1 < len(args):
            ratio, i = float(args[i + 1]), i + 2
            continue
        i += 1
    if not inp or not out:
        print("SPIKE163_FAIL usage: --input <fbx> --output <fbx> [--ratio 0.5]")
        sys.exit(1)
    if not os.path.isabs(inp):
        inp = os.path.join(ROOT, inp)
    if not os.path.isabs(out):
        out = os.path.join(ROOT, out)
    return os.path.abspath(inp), os.path.abspath(out), ratio


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


def decimate(meshes, ratio, report):
    for obj in meshes:
        if obj.type != "MESH":
            continue
        bpy.context.view_layer.objects.active = obj
        mod = obj.modifiers.new(name="Spike163_Decimate", type="DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        mod.use_collapse_triangulate = False
        bpy.ops.object.modifier_apply(modifier=mod.name)
        report["modifiers"].append(
            {"object": obj.name, "type": "DECIMATE", "mode": "COLLAPSE", "ratio": ratio}
        )


def main():
    src, dst, ratio = parse_args()
    report = {
        "spike": "pre-163 manual decimation",
        "blender": bpy.app.version_string,
        "input": bake.rel(src),
        "output": bake.rel(dst),
        "decimate": {"modifier": "DECIMATE", "mode": "COLLAPSE", "ratio": ratio},
        "modifiers": [],
        "steps": [],
        "ok": False,
    }

    if not os.path.isfile(src):
        print("SPIKE163_FAIL input FBX missing:", bake.rel(src))
        sys.exit(1)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.unit_settings.length_unit = "METERS"

    bpy.ops.import_scene.fbx(filepath=src)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        print("SPIKE163_FAIL no mesh objects after FBX import")
        sys.exit(1)

    report["mesh_count"] = len(meshes)
    report["mesh_names"] = [o.name for o in meshes]
    report["before"] = {"tris": tri_count(meshes), "verts": vert_count(meshes)}
    report["before_bounds"] = bake.mesh_world_bounds(meshes)
    report["steps"].append("imported raw FBX (%d tris)" % report["before"]["tris"])

    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bake.select_meshes(meshes)
    decimate(meshes, ratio, report)
    report["after"] = {"tris": tri_count(meshes), "verts": vert_count(meshes)}
    report["steps"].append(
        "Decimate COLLAPSE ratio=%.2f applied (%d tris)" % (ratio, report["after"]["tris"])
    )
    report["actual_ratio"] = round(
        report["after"]["tris"] / max(1, report["before"]["tris"]), 4
    )

    report["after_floor_origin_bounds"] = bake.apply_floor_origin(meshes)
    report["steps"].append("152 floor-origin: AABB bottom-center, then Apply All Transforms")

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    bake.select_meshes(meshes)
    bpy.ops.export_scene.fbx(filepath=dst, **bake.FBX_EXPORT)
    report["steps"].append("152 FBX_EXPORT flags (bake_space_transform, path_mode=STRIP)")
    report["output_exists"] = os.path.isfile(dst)
    report["output_bytes"] = os.path.getsize(dst) if report["output_exists"] else 0

    report["textures_copied"] = bake.copy_files(
        bake.list_sibling_maps(src), os.path.dirname(dst)
    )
    report["ok"] = bool(report["output_exists"] and report["output_bytes"] > 0)

    bake.write_json(os.path.splitext(dst)[0] + ".decimate.json", report)
    print("SPIKE163_TRIS before=%d after=%d ratio_requested=%.2f ratio_actual=%.4f"
          % (report["before"]["tris"], report["after"]["tris"], ratio, report["actual_ratio"]))
    print(json.dumps(report, indent=2))
    if not report["ok"]:
        sys.exit(1)


if __name__ == "__main__":
    main()
