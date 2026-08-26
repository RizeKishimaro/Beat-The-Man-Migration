"""
Ticket 151 spike — headless Blender bake of a RAW Meshy FBX.

Reproduces the documented apply-transforms / export recipe only
(GH5cYKPv + sttvR8QE). Does not guess pivot, per-item scale-by-eye,
or mesh-splitting. Those are flagged in the report, not automated.

Run (no GUI):
  Blender --background --python BlenderSource/Spike151/_bake_meshy_fbx.py

Optional args after -- :
  --input <fbx> --output <fbx>
"""
import json
import math
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

DEFAULT_INPUT = os.path.join(
    ROOT,
    "Assets/Objects/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture_fbx",
    "Meshy_AI_Cream_Cushioned_Woode_0805155347_texture.fbx",
)
DEFAULT_OUTPUT = os.path.join(
    ROOT,
    "Assets/_Spike151/CreamChair_HeadlessBake/CreamChair_HeadlessBake.fbx",
)
REPORT_PATH = os.path.join(HERE, "_spike151_blender_report.json")

# sttvR8QE / crate / F1 export recipe — do not drift these flags.
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
    path_mode="COPY",
    embed_textures=False,
)


def argv_after_double_dash():
    if "--" not in sys.argv:
        return []
    return sys.argv[sys.argv.index("--") + 1 :]


def parse_args():
    args = argv_after_double_dash()
    inp = DEFAULT_INPUT
    out = DEFAULT_OUTPUT
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
        i += 1
    if not os.path.isabs(inp):
        inp = os.path.join(ROOT, inp)
    if not os.path.isabs(out):
        out = os.path.join(ROOT, out)
    return os.path.abspath(inp), os.path.abspath(out)


def deg(euler):
    return [round(math.degrees(v), 4) for v in euler]


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
                "scale": [round(obj.scale.x, 6), round(obj.scale.y, 6), round(obj.scale.z, 6)],
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


def origin_vs_bounds_notes(bounds):
    if not bounds:
        return ["no mesh bounds"]
    notes = []
    mn, mx, size = bounds["min"], bounds["max"], bounds["size"]
    # After Apply All Transforms, object origin is world 0,0,0.
    # Floor-sitting props usually want min-Z ~= 0 and XY centered on origin.
    if abs(mn[2]) > 0.02:
        notes.append(
            "PIVOT/FLOOR: origin is not at the mesh floor (world min-Z = %.4f, not ~0). "
            "Manual recipe 'position it' is a judgment call — not automated."
            % mn[2]
        )
    cx = (mn[0] + mx[0]) * 0.5
    cy = (mn[1] + mx[1]) * 0.5
    if abs(cx) > 0.05 or abs(cy) > 0.05:
        notes.append(
            "PIVOT/CENTER: mesh is not XY-centered on origin (center=%.4f,%.4f). "
            "Centering is a judgment call — not automated."
            % (cx, cy)
        )
    height = size[2]
    if height > 3.0 or height < 0.3:
        notes.append(
            "SCALE-BY-EYE: mesh height is %.4f Blender units. Ticket 135 wants 1 unit = 1 metre "
            "(a chair is ~0.9–1.1 m). sttvR8QE says per-item scale is decided by eye in Unity. "
            "This script does NOT rescale."
            % height
        )
    return notes


def main():
    src, dst = parse_args()
    report = {
        "ticket": 151,
        "blender": bpy.app.version_string,
        "input": os.path.relpath(src, ROOT),
        "output": os.path.relpath(dst, ROOT),
        "gui": False,
        "background": True,
        "steps": [],
        "flags": [],
        "ok": False,
    }

    if not os.path.isfile(src):
        report["error"] = "input FBX missing: " + src
        with open(REPORT_PATH, "w") as f:
            json.dump(report, f, indent=2)
        print("SPIKE151_FAIL", report["error"])
        sys.exit(1)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.unit_settings.length_unit = "METERS"
    report["steps"].append("units: METRIC scale_length=1.0 METERS")

    bpy.ops.import_scene.fbx(filepath=src)
    report["steps"].append("imported raw Meshy FBX")

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    others = [o.name + ":" + o.type for o in bpy.context.scene.objects if o.type != "MESH"]
    report["mesh_count"] = len(meshes)
    report["non_mesh"] = others
    if len(meshes) != 1:
        report["flags"].append(
            "MESH-COUNT: imported %d mesh objects (plus %s). Meshy Smart-mode parts are often "
            "one mesh with disconnected islands (ticket 103) — splitting/joining is judgment, "
            "not automated. Exporting all meshes as selected."
            % (len(meshes), others)
        )

    if not meshes:
        report["error"] = "no mesh objects after FBX import"
        with open(REPORT_PATH, "w") as f:
            json.dump(report, f, indent=2)
        print("SPIKE151_FAIL", report["error"])
        sys.exit(1)

    report["before_apply"] = snapshot("before_apply_all_transforms", meshes)

    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    select_meshes(meshes)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    report["steps"].append("Object → Apply → All Transforms (location+rotation+scale)")
    report["after_apply"] = snapshot("after_apply_all_transforms", meshes)
    report["flags"].extend(origin_vs_bounds_notes(report["after_apply"]["world_bounds"]))

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    select_meshes(meshes)
    bpy.ops.export_scene.fbx(filepath=dst, **FBX_EXPORT)
    report["steps"].append(
        "export_scene.fbx axis_forward=-Z axis_up=Y global_scale=1 apply_unit_scale "
        "FBX_SCALE_ALL bake_space_transform"
    )
    report["output_exists"] = os.path.isfile(dst)
    report["output_bytes"] = os.path.getsize(dst) if report["output_exists"] else 0
    report["ok"] = bool(report["output_exists"] and report["output_bytes"] > 0)

    with open(REPORT_PATH, "w") as f:
        json.dump(report, f, indent=2)
    print("SPIKE151_REPORT", REPORT_PATH)
    print(json.dumps(report, indent=2))
    if not report["ok"]:
        sys.exit(1)


if __name__ == "__main__":
    main()
