"""Ticket 163 UV blocker — option 1 probe (NOT wired into the drop pipeline).

Lighter cleanup (Merge by Distance, Fill Holes, Recalc Normals) then
Decimate Collapse, vs a no-cleanup Collapse baseline.

Source: today's Drop/Decimate Nodes zip (3.05M tris, 2 boundary / 3
non-manifold). Compare stall vs the spike's ~31k no-cleanup floor.

Run:
  blender --background --python BlenderSource/Bake/_uv_cleanup_probe.py
"""
from __future__ import annotations

import json
import os
import sys
import time

import bpy
import bmesh

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import _bake_fbx as bake  # noqa: E402

SRC = os.path.join(
    ROOT,
    "Temp/bake163/uv_src/Meshy_AI_Cream_Cushioned_Woode_0902130031_texture_fbx",
    "Meshy_AI_Cream_Cushioned_Woode_0902130031_texture.fbx",
)
OUTDIR = os.path.join(ROOT, "Temp", "bake163", "uv_probe")
TARGET = 2900
MERGE_DIST = 0.0001  # 0.1 mm — small, as requested
SPIKE_STALL_BASELINE = 31000


def uv_stats(meshes):
    n_layers = 0
    n_loops = 0
    nonzero = 0
    umin = vmin = 1e9
    umax = vmax = -1e9
    for obj in meshes:
        if obj.type != "MESH" or obj.data is None:
            continue
        n_layers += len(obj.data.uv_layers)
        if not obj.data.uv_layers:
            continue
        uv = obj.data.uv_layers.active.data
        n_loops += len(uv)
        for item in uv:
            x, y = item.uv.x, item.uv.y
            if x != 0.0 or y != 0.0:
                nonzero += 1
            if x < umin:
                umin = x
            if x > umax:
                umax = x
            if y < vmin:
                vmin = y
            if y > vmax:
                vmax = y
    intact = n_layers > 0 and n_loops > 0 and nonzero > 0 and (umax - umin) > 0.01
    return {
        "uv_layers": n_layers,
        "uv_loops": n_loops,
        "uv_nonzero": nonzero,
        "uv_bounds": None
        if n_loops == 0
        else [round(umin, 4), round(vmin, 4), round(umax, 4), round(vmax, 4)],
        "uv_intact": intact,
    }


def snapshot(meshes):
    out = {
        "tris": bake.tri_count(meshes),
        "verts": bake.vert_count(meshes),
        "topology": bake.edge_topology(meshes),
    }
    out.update(uv_stats(meshes))
    return out


def duplicate_mesh(obj):
    dup = obj.copy()
    dup.data = obj.data.copy()
    bpy.context.scene.collection.objects.link(dup)
    return dup


def light_cleanup(obj, merge_dist):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if obj.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    # Blender 5: merge_by_distance. Fallback to remove_doubles.
    try:
        bpy.ops.mesh.merge_by_distance(threshold=merge_dist)
        merge_op = "merge_by_distance"
    except Exception:
        bpy.ops.mesh.remove_doubles(threshold=merge_dist)
        merge_op = "remove_doubles"
    bpy.ops.mesh.fill_holes(sides=0)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return merge_op


def decimate_to_target(meshes, target):
    report = {"modifiers": []}
    bake.apply_decimate_to_target(meshes, target, report)
    return report


def run_path(label, src_obj, do_cleanup):
    t0 = time.time()
    obj = duplicate_mesh(src_obj)
    meshes = [obj]
    steps = []
    after_cleanup = None
    merge_op = None
    if do_cleanup:
        merge_op = light_cleanup(obj, MERGE_DIST)
        after_cleanup = snapshot(meshes)
        steps.append(
            "light cleanup merge=%s dist=%s fill_holes normals_make_consistent"
            % (merge_op, MERGE_DIST)
        )
    dec_report = decimate_to_target(meshes, TARGET)
    after = snapshot(meshes)
    elapsed = round(time.time() - t0, 2)
    os.makedirs(OUTDIR, exist_ok=True)
    dst = os.path.join(OUTDIR, label + ".fbx")
    bake.select_meshes(meshes)
    bpy.ops.export_scene.fbx(filepath=dst, **bake.FBX_EXPORT)
    bpy.data.objects.remove(obj, do_unlink=True)
    return {
        "label": label,
        "cleanup": do_cleanup,
        "merge_op": merge_op,
        "merge_dist": MERGE_DIST if do_cleanup else None,
        "after_cleanup": after_cleanup,
        "after_decimate": after,
        "decimate_ratio": dec_report.get("decimate_ratio"),
        "decimate_skipped": dec_report.get("decimate_skipped"),
        "elapsed_sec": elapsed,
        "exported": bake.rel(dst),
        "steps": steps,
        "hit_target": after["tris"] <= TARGET * 1.15,
        "stalled_near_31k": abs(after["tris"] - SPIKE_STALL_BASELINE) / float(SPIKE_STALL_BASELINE)
        < 0.35
        if after["tris"] > TARGET * 2
        else False,
        "uv_intact": after["uv_intact"],
    }


def main():
    if not os.path.isfile(SRC):
        print("UV_PROBE_FAIL missing", SRC)
        sys.exit(1)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.unit_settings.length_unit = "METERS"
    bpy.ops.import_scene.fbx(filepath=SRC)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if len(meshes) != 1:
        print("UV_PROBE_FAIL want 1 mesh, got", len(meshes))
        sys.exit(1)
    src_obj = meshes[0]
    src_obj.name = "UVProbeRaw"

    raw = snapshot([src_obj])
    payload = {
        "ticket": 163,
        "probe": "option1_light_cleanup_then_collapse",
        "source": bake.rel(SRC),
        "target_tris": TARGET,
        "spike_no_cleanup_stall": SPIKE_STALL_BASELINE,
        "raw": raw,
        "current_pipeline_on_this_drop": {
            "note": "already ran via Drop/Decimate — voxel remesh then collapse",
            "tris_raw": 3050441,
            "tris_after_remesh": 3714296,
            "tris_after_decimate": 2900,
            "uv_intact": False,
        },
    }

    payload["baseline_no_cleanup"] = run_path("baseline_collapse", src_obj, False)
    payload["option1_light_cleanup"] = run_path("option1_cleanup_collapse", src_obj, True)

    a = payload["baseline_no_cleanup"]["after_decimate"]["tris"]
    b = payload["option1_light_cleanup"]["after_decimate"]["tris"]
    payload["vs_31k_stall"] = {
        "baseline_tris": a,
        "option1_tris": b,
        "baseline_closer_than_31k": a < SPIKE_STALL_BASELINE * 0.9,
        "option1_closer_than_31k": b < SPIKE_STALL_BASELINE * 0.9,
        "option1_reached_target": payload["option1_light_cleanup"]["hit_target"],
        "option1_uv_intact": payload["option1_light_cleanup"]["uv_intact"],
        "baseline_uv_intact": payload["baseline_no_cleanup"]["uv_intact"],
    }
    payload["recommendation"] = (
        "option1_enough_skip_option2"
        if payload["option1_light_cleanup"]["hit_target"]
        and payload["option1_light_cleanup"]["uv_intact"]
        else "option1_not_enough_need_uv_rebake"
    )

    out = os.path.join(HERE, "_uv_cleanup_probe_report.json")
    bake.write_json(out, payload)
    print(json.dumps(payload, indent=2))
    print("UV_PROBE", payload["recommendation"])
    sys.exit(0)


if __name__ == "__main__":
    main()
