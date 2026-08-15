"""
Ticket 132 step 1 — export the chassis out of the master Aero Kart file.

Chassis parts (confirmed by Sam): the Middle body pod tub, plus the rear
support, rear block, rear light, both side body pods and the seat.

Every part's origin goes to the master file's world origin (0,0,0) so the
chassis, the body and every later part share one coordinate frame and
reassemble in Unity with no offset maths. Wheels are the documented exception —
their pivots go to their own axle centres for the spin animation (ticket 133).

Parts stay as separate objects parented under one root, matching how the
ChromeDrift wheel exports tire/rim/bolt/decal in a single FBX.

The master file is a reference only: it is opened, never saved.
"""
import bpy
import os

SRC = "/Users/sammy/Documents/GitHub/BeatTheMan/Assets/Objects/F1 Car/Meshy_AI_Aero_Kart_Prototype_0809085417_generate.blend"
GAME_BLEND = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/F1_Chassis_Game.blend"
OUT_DIR = "/Users/sammy/Documents/GitHub/BeatTheMan/Assets/Objects/F1 Car/F1_Chassis"
FBX_PATH = os.path.join(OUT_DIR, "F1_Chassis.fbx")
REPORT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_f1_chassis_report.txt"

ROOT_SRC = "Middle body pod"
ROOT_NAME = "F1_Chassis"

# source name in the master -> exported name
PARTS = {
    "Middle body pod": ROOT_NAME,
    "left body pod": "Chassis_BodyPod_L",
    "right body pod": "Chassis_BodyPod_R",
    "Seat": "Chassis_Seat",
    "Rear block": "Chassis_RearBlock",
    "Rear support": "Chassis_RearSupport",
    "Rear Light": "Chassis_RearLight",
}


def world_bounds(obj):
    vs = [obj.matrix_world @ v.co for v in obj.data.vertices]
    xs = [v.x for v in vs]
    ys = [v.y for v in vs]
    zs = [v.z for v in vs]
    return (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))


def fmt(t):
    return "(" + ", ".join(f"{c:.5f}" for c in t) + ")"


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=SRC)

    missing = [n for n in PARTS if bpy.data.objects.get(n) is None]
    if missing:
        raise RuntimeError("missing objects: " + ", ".join(missing))

    rows = []
    objs = []
    final_bounds = []

    # cursor snapped precisely to the attachment point
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.context.scene.cursor.rotation_euler = (0.0, 0.0, 0.0)

    for src_name, out_name in PARTS.items():
        obj = bpy.data.objects[src_name]
        before = (tuple(obj.location), tuple(obj.rotation_euler), tuple(obj.scale))
        before_bounds = world_bounds(obj)

        # both body pods ship hidden in the master; select_set silently no-ops
        # on a hidden object, which would quietly drop them from the export.
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.hide_render = False

        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        if not obj.select_get():
            raise RuntimeError(f"{src_name}: could not be selected")

        # 1. origin to 3D cursor  2. Ctrl+A -> All Transforms
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

        after = (tuple(obj.location), tuple(obj.rotation_euler), tuple(obj.scale))
        after_bounds = world_bounds(obj)

        identity = (
            max(abs(c) for c in after[0]) < 1e-6
            and max(abs(c) for c in after[1]) < 1e-6
            and max(abs(c - 1.0) for c in after[2]) < 1e-6
        )
        drift = max(abs(a - b) for a, b in zip(before_bounds, after_bounds))
        if not identity:
            raise RuntimeError(f"{src_name}: transforms not identity after apply: {after}")
        if drift > 1e-5:
            raise RuntimeError(f"{src_name}: mesh shifted in world space by {drift}")

        obj.name = out_name
        obj.data.name = out_name
        objs.append(obj)
        final_bounds.append(after_bounds)
        rows.append(
            f"  '{src_name}' -> '{out_name}'\n"
            f"      verts={len(obj.data.vertices)} faces={len(obj.data.polygons)} "
            f"mods={[m.type for m in obj.modifiers]} mats={[m.name if m else None for m in obj.data.materials]}\n"
            f"      before loc={fmt(before[0])} rot={fmt(before[1])} scale={fmt(before[2])}\n"
            f"      after  loc={fmt(after[0])} rot={fmt(after[1])} scale={fmt(after[2])}\n"
            f"      world bounds={fmt(after_bounds)} drift={drift:.9f}"
        )

    # single root so the chassis drops onto the plinth as one GameObject.
    # every origin is already at (0,0,0), so parenting is a no-op transform-wise.
    # set directly rather than via bpy.ops.object.parent_set, which no-ops in
    # background mode.
    root = bpy.data.objects[ROOT_NAME]
    for obj in objs:
        if obj is not root:
            obj.parent = root
            obj.matrix_parent_inverse = root.matrix_world.inverted()
    bpy.context.view_layer.update()

    for obj, expected in zip(objs, final_bounds):
        if obj is not root and obj.parent is not root:
            raise RuntimeError(f"{obj.name} failed to parent to {ROOT_NAME}")
        drift = max(abs(a - b) for a, b in zip(world_bounds(obj), expected))
        if drift > 1e-5:
            raise RuntimeError(f"{obj.name}: parenting shifted the mesh by {drift}")

    # 3. selection-only FBX, chassis parts alone
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objs:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root

    unselected = [o.name for o in objs if not o.select_get()]
    if unselected:
        raise RuntimeError("not selected at export time: " + ", ".join(unselected))
    selected = {o.name for o in bpy.context.view_layer.objects if o.select_get()}
    if selected != set(PARTS.values()):
        raise RuntimeError(f"export selection is {sorted(selected)}, expected {sorted(PARTS.values())}")

    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        bake_space_transform=True,
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        path_mode="COPY",
        embed_textures=False,
    )

    # working blend: chassis only, keeps the studio rig for later look checks
    keep = set(PARTS.values()) | {
        "Studio_Camera", "Studio_Key", "Studio_Fill", "Studio_Top", "Studio_Bounce", "Studio_Cyclorama",
    }
    for o in list(bpy.data.objects):
        if o.name not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.wm.save_as_mainfile(filepath=GAME_BLEND)

    total_v = sum(len(o.data.vertices) for o in objs)
    total_f = sum(len(o.data.polygons) for o in objs)
    text = "\n".join(
        [
            f"src={SRC} (opened read-only, never saved)",
            f"root='{ROOT_NAME}', {len(objs)} parts, totals verts={total_v} faces={total_f}",
            "origin: world (0,0,0) via ORIGIN_CURSOR, then All Transforms applied",
            "",
        ]
        + rows
        + ["", f"fbx={FBX_PATH}", f"blend={GAME_BLEND}"]
    )
    with open(REPORT, "w") as f:
        f.write(text + "\n")
    print(text)


main()
