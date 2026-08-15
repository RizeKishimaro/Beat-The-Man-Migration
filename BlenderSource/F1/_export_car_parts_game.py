"""
Ticket 135 — export the remaining car parts (body + 4 wheels) out of the master
Aero Kart file, at the size they actually are IN the assembled car.

This is the point of the spike: every part leaves the master at true 1 Blender
unit = 1 metre, with the master's own transforms baked in, so the parts are
inherently consistent with each other and nothing needs resizing in Unity.

Origin convention (see also _export_chassis_game.py):
  - body  -> origin at the master world origin (0,0,0), all transforms applied,
             so it drops into Unity at local position zero and lines up with
             the chassis automatically.
  - wheels -> origin at each wheel's own AXLE CENTRE so it spins correctly, and
             its POSITION is preserved rather than zeroed, so the four wheels
             land in the right places without being positioned by hand.

Note the ticket-130 standalone wheel FBX is 0.720m (real F1 tyre size) while the
wheel fitted to this car is 0.331m — a 2.18x mismatch, because that one was
exported from a different blend rather than from the master. This script takes
the fitted size, which is the one consistent with the chassis and body.

The master file is a reference only: it is opened, never saved.
"""
import bpy
import os

SRC = "/Users/sammy/Documents/GitHub/BeatTheMan/Assets/Objects/F1 Car/Meshy_AI_Aero_Kart_Prototype_0809085417_generate.blend"
OBJ_DIR = "/Users/sammy/Documents/GitHub/BeatTheMan/Assets/Objects/F1 Car"
BODY_DIR = os.path.join(OBJ_DIR, "F1_Body")
WHEELS_DIR = os.path.join(OBJ_DIR, "F1_Wheels")
BODY_FBX = os.path.join(BODY_DIR, "F1_Body.fbx")
WHEELS_FBX = os.path.join(WHEELS_DIR, "F1_Wheels.fbx")
GAME_BLEND = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/F1_CarParts_Game.blend"
REPORT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_f1_car_parts_report.txt"

BODY_SRC = "F1_Body_Game"
BODY_OUT = "F1_Body"
WHEELS = [("Wheel_FL", "Wheel_FL"), ("Wheel_FR", "Wheel_FR"),
          ("Wheel_RL", "Wheel_RL"), ("Wheel_RR", "Wheel_RR")]

FBX_ARGS = dict(
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

rows = []


def show(obj):
    """Several parts ship hidden in the master; hidden objects cannot be selected."""
    obj.hide_set(False)
    obj.hide_viewport = False
    obj.hide_render = False


def select_only(objs, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        show(o)
        o.select_set(True)
        if not o.select_get():
            raise RuntimeError(f"{o.name}: could not be selected")
    bpy.context.view_layer.objects.active = active if active is not None else objs[0]


def bounds(objs):
    vs = []
    for o in objs:
        vs += [o.matrix_world @ v.co for v in o.data.vertices]
    xs = [v.x for v in vs]
    ys = [v.y for v in vs]
    zs = [v.z for v in vs]
    return (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))


def size(b):
    return (b[1] - b[0], b[3] - b[2], b[5] - b[4])


def descend_meshes(root):
    out = []
    stack = list(root.children)
    while stack:
        o = stack.pop()
        stack += list(o.children)
        if o.type == "MESH" and len(o.data.vertices) > 0:
            out.append(o)
    return out


def export_body():
    obj = bpy.data.objects.get(BODY_SRC)
    if obj is None:
        raise RuntimeError(f"'{BODY_SRC}' not found")
    before = size(bounds([obj]))

    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    select_only([obj])
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.name = BODY_OUT
    obj.data.name = BODY_OUT

    after = size(bounds([obj]))
    if max(abs(a - b) for a, b in zip(before, after)) > 1e-5:
        raise RuntimeError(f"body changed size: {before} -> {after}")
    if max(abs(c) for c in obj.location) > 1e-6:
        raise RuntimeError(f"body origin not at world origin: {obj.location}")

    os.makedirs(BODY_DIR, exist_ok=True)
    select_only([obj])
    bpy.ops.export_scene.fbx(filepath=BODY_FBX, **FBX_ARGS)
    rows.append(f"BODY '{BODY_SRC}' -> '{BODY_OUT}' verts={len(obj.data.vertices)} "
                f"size={after[0]:.3f} x {after[1]:.3f} x {after[2]:.3f} origin=(0,0,0)")
    return obj


def export_wheels():
    made = []
    for empty_name, out_name in WHEELS:
        empty = bpy.data.objects.get(empty_name)
        if empty is None:
            raise RuntimeError(f"'{empty_name}' not found")
        axle = empty.matrix_world.translation.copy()
        meshes = descend_meshes(empty)
        if not meshes:
            raise RuntimeError(f"{empty_name}: no child meshes")
        before = size(bounds(meshes))

        # Unparent, keeping world transform, so the empty's fitted scale ends up
        # on the meshes themselves and can be baked in.
        for m in meshes:
            show(m)
            if m.data.users > 1:
                m.data = m.data.copy()
            mw = m.matrix_world.copy()
            m.parent = None
            m.matrix_world = mw
        bpy.context.view_layer.update()

        # Bake the fitted rotation/scale into the mesh; keep positions.
        select_only(meshes)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

        select_only(meshes, active=meshes[0])
        if len(meshes) > 1:
            bpy.ops.object.join()
        wheel = bpy.context.view_layer.objects.active
        wheel.name = out_name
        wheel.data.name = out_name

        # Pivot at the axle centre — the documented wheel exception — while the
        # wheel stays exactly where the master put it.
        bpy.context.scene.cursor.location = axle
        select_only([wheel])
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

        after = size(bounds([wheel]))
        if max(abs(a - b) for a, b in zip(before, after)) > 1e-4:
            raise RuntimeError(f"{out_name} changed size: {before} -> {after}")
        off = (wheel.matrix_world.translation - axle).length
        if off > 1e-5:
            raise RuntimeError(f"{out_name} origin is {off} from the axle centre")

        made.append(wheel)
        rows.append(f"WHEEL '{empty_name}' -> '{out_name}' verts={len(wheel.data.vertices)} "
                    f"size={after[0]:.3f} x {after[1]:.3f} x {after[2]:.3f} "
                    f"axle=({axle.x:.4f}, {axle.y:.4f}, {axle.z:.4f})")

    os.makedirs(WHEELS_DIR, exist_ok=True)
    select_only(made)
    bpy.ops.export_scene.fbx(filepath=WHEELS_FBX, **FBX_ARGS)
    return made


def main():
    bpy.ops.wm.open_mainfile(filepath=SRC)
    body = export_body()
    wheels = export_wheels()

    whole = size(bounds([body] + wheels))
    rows.append("")
    rows.append(f"BODY+WHEELS combined = {whole[0]:.3f} x {whole[1]:.3f} x {whole[2]:.3f}")
    rows.append(f"body={BODY_FBX}")
    rows.append(f"wheels={WHEELS_FBX}")

    keep = {BODY_OUT} | {n for _, n in WHEELS} | {
        "Studio_Camera", "Studio_Key", "Studio_Fill", "Studio_Top", "Studio_Bounce", "Studio_Cyclorama"}
    for o in list(bpy.data.objects):
        if o.name not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.wm.save_as_mainfile(filepath=GAME_BLEND)
    rows.append(f"blend={GAME_BLEND}")

    text = "\n".join([f"src={SRC} (opened read-only, never saved)", ""] + rows)
    with open(REPORT, "w") as f:
        f.write(text + "\n")
    print(text)


main()
