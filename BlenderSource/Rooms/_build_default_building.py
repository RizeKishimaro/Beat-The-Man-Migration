"""
Ticket 117 / 122 / 179 — Default Building master atom.

Grid-corner snap: root empty sits at the exterior min corner (0,0,0).

Ticket 179 — shell IS the placement tile (supersedes the 2026-09-10 8×8 lock):
  6.0 m  exterior TOTAL footprint (x/y 0..6)
  0.3 m  walls
  5.4 m  interior clear space     (x/y 0.3..5.7)
  3.0 m  tall
  1.5 m  door (2×0.75 m leaves), centred on min-Y (Unity -Z)

Corner trim: Front/Back own the 0.3×0.3 corner columns. Left/Right are
trimmed to INT_MIN..INT_MAX on Y so adjacent boxes meet at an edge (no
overlapping volume). Overlapping boxes + Cull Back punched holes into
the hollow wall cavity.

Front wall is the MIN-Y side.

EXPORT / AXIS — measured in Unity, do not re-derive from memory.

Export NATIVE Blender Z-up and let Unity bake the conversion:

    axis_forward='Y', axis_up='Z', bake_space_transform=False
    Unity importer: bakeAxisConversion=True, importCameras/Lights=False,
                    animationType=None        (matches CafeRoom.fbx)

That yields an IDENTITY root in Unity, the building at X[0, 6]
Z[0, 6] Y[0, 3], and the door facing -Z. No yaw compensation needed.

This matters beyond placement: ShopDoorLeaf rotates the hinge about its LOCAL
Y and uses transform.forward as the door normal, so the hinge must import with
Unity axes (up=(0,1,0), forward horizontal). Exporting with axis_up='Y'
instead leaves a (270,0,0) or (90,0,0) root, the hinges keep Blender axes, and
the doors then tip like a drawbridge and never detect the player crossing.

bake_space_transform=True is NOT a shortcut — it detaches the parented door
leaves from their hinges.

Run (from repo):
  /Applications/Blender.app/Contents/MacOS/Blender --background \\
    --python BlenderSource/Rooms/_build_default_building.py -- --export-fbx
"""
import math
import os
import sys

import bpy
import mathutils

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_REPO_ROOT = os.path.abspath(os.path.join(_SCRIPT_DIR, "..", ".."))
BLEND_PATH = os.path.join(_SCRIPT_DIR, "DefaultBuilding.blend")
FBX_PATH = os.path.join(_REPO_ROOT, "Assets", "Art", "Buildings", "DefaultBuilding.fbx")
EXPORT_FBX = "--export-fbx" in sys.argv or os.environ.get("DEFAULT_BUILDING_EXPORT_FBX") == "1"

# --- Footprint (meters, Blender Z-up, X right, Y forward) ---
EXT = 6.0
INT_MIN, INT_MAX = 0.3, 5.7
WALL_T = 0.3
W_MIN, W_MAX = 0.0, EXT  # exterior IS the tile: 0 .. 6
WALL_H = 3.0
FLOOR_T = 0.05
CEIL_T = 0.05

# Ticket 179: 1.5 m centred (2×0.75 m leaves).
DOOR_W = 1.5
DOOR_H = 2.2
DOOR_X0 = EXT * 0.5 - DOOR_W * 0.5
DOOR_X1 = EXT * 0.5 + DOOR_W * 0.5
LEAF_T = 0.06

F_OUT, F_IN = W_MIN, INT_MIN  # front wall (min Y -> Unity -Z): 0.0 .. 0.3
B_IN, B_OUT = INT_MAX, W_MAX  # back wall:                      5.7 .. 6.0
DOOR_Y = (F_OUT + F_IN) * 0.5  # 0.15 — door plane centred in wall thickness
LEAF_Y0 = DOOR_Y - LEAF_T * 0.5
LEAF_Y1 = DOOR_Y + LEAF_T * 0.5


def clear_scene():
    """Wipe objects/meshes/materials. Deliberately NOT read_factory_settings():
    that unregisters the BlenderMCP addon when driven over MCP."""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for collection in (bpy.data.meshes, bpy.data.materials):
        for item in list(collection):
            collection.remove(item)


def make_mat(name, rgba, roughness=0.7):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Roughness"].default_value = roughness
    return mat


def make_box(name, x0, x1, y0, y1, z0, z1, mat, parent):
    """Bounds are LOCAL to `parent`.

    matrix_parent_inverse is left at identity so the object's location stays a
    true local offset from the parent. Setting it to the parent's inverse (as an
    earlier pass did) pins the child to wherever it was created, which is how the
    door leaves ended up stranded at the world origin.
    """
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.mesh.primitive_cube_add(
        size=1.0,
        location=((x0 + x1) * 0.5, (y0 + y1) * 0.5, (z0 + z1) * 0.5),
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (x1 - x0, y1 - y0, z1 - z0)
    bpy.ops.object.transform_apply(scale=True)
    obj.data.materials.append(mat)
    obj.parent = parent
    return obj


def make_empty(name, location, parent):
    obj = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(obj)
    obj.empty_display_type = "ARROWS"
    obj.empty_display_size = 0.4
    obj.location = location
    obj.parent = parent
    return obj


def world_aabb(name):
    obj = bpy.data.objects[name]
    corners = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    return (
        round(min(c.x for c in corners), 3), round(max(c.x for c in corners), 3),
        round(min(c.y for c in corners), 3), round(max(c.y for c in corners), 3),
        round(min(c.z for c in corners), 3), round(max(c.z for c in corners), 3),
    )


EXPECTED = {
    "RoomFloor":         (INT_MIN, INT_MAX, INT_MIN, INT_MAX, 0.0, FLOOR_T),
    "RoomCeiling":       (INT_MIN, INT_MAX, INT_MIN, INT_MAX, WALL_H - CEIL_T, WALL_H),
    # Front/Back own corners (full 6.0 m). Left/Right trimmed on Y (5.4 m).
    "Wall_Back":         (W_MIN, W_MAX, B_IN, B_OUT, 0.0, WALL_H),
    "Wall_Left":         (W_MIN, INT_MIN, INT_MIN, INT_MAX, 0.0, WALL_H),
    "Wall_Right":        (INT_MAX, W_MAX, INT_MIN, INT_MAX, 0.0, WALL_H),
    "Wall_Front_Left":   (W_MIN, DOOR_X0, F_OUT, F_IN, 0.0, WALL_H),
    "Wall_Front_Right":  (DOOR_X1, W_MAX, F_OUT, F_IN, 0.0, WALL_H),
    "Wall_Front_Lintel": (DOOR_X0, DOOR_X1, F_OUT, F_IN, DOOR_H, WALL_H),
    "DoorLeaf_Left":     (DOOR_X0, DOOR_X0 + DOOR_W * 0.5, LEAF_Y0, LEAF_Y1, 0.0, DOOR_H),
    "DoorLeaf_Right":    (DOOR_X0 + DOOR_W * 0.5, DOOR_X1, LEAF_Y0, LEAF_Y1, 0.0, DOOR_H),
}


def verify():
    """Assert against expected world bounds. Printing bounds and eyeballing them
    is how a previous pass 'verified' door leaves that were 4 m out of place."""
    failures = []
    for name, exp in EXPECTED.items():
        act = world_aabb(name)
        ok = all(abs(act[i] - exp[i]) < 0.002 for i in range(6))
        print(("PASS " if ok else "FAIL ") + name, act, "" if ok else "expected " + str(exp))
        if not ok:
            failures.append(name)

    for name, exp in (("DoorHinge_Left", (DOOR_X0, DOOR_Y, 0.0)),
                      ("DoorHinge_Right", (DOOR_X1, DOOR_Y, 0.0))):
        w = bpy.data.objects[name].matrix_world.translation
        act = (round(w.x, 3), round(w.y, 3), round(w.z, 3))
        ok = all(abs(act[i] - exp[i]) < 0.002 for i in range(3))
        print(("PASS " if ok else "FAIL ") + name, act)
        if not ok:
            failures.append(name)

    # Opening must contain no wall geometry below the lintel.
    open_lo = DOOR_X0 + 0.001
    open_hi = DOOR_X1 - 0.001
    for wall in ("Wall_Back", "Wall_Left", "Wall_Right",
                 "Wall_Front_Left", "Wall_Front_Right", "Wall_Front_Lintel"):
        b = world_aabb(wall)
        if b[0] < open_hi and b[1] > open_lo and b[2] < (F_IN - 0.001) and b[3] > (F_OUT + 0.001) and b[4] < (DOOR_H - 0.001):
            print("FAIL door opening blocked by", wall)
            failures.append(wall)

    # Outer picture-frame is the 6.0 m tile (Back X, Front span). Left/Right Y = 5.4 m.
    ring_len = round(W_MAX - W_MIN, 3)
    side_len = round(INT_MAX - INT_MIN, 3)
    for name, axis, exp in (("Wall_Back", "x", ring_len),
                            ("Wall_Left", "y", side_len),
                            ("Wall_Right", "y", side_len)):
        b = world_aabb(name)
        length = round((b[1] - b[0]) if axis == "x" else (b[3] - b[2]), 3)
        ok = abs(length - exp) < 0.002
        print(("PASS " if ok else "FAIL ") + name + "_length", length, "expected", exp)
        if not ok:
            failures.append(name + "_length")

    front_l = world_aabb("Wall_Front_Left")
    front_r = world_aabb("Wall_Front_Right")
    front_span = round(front_r[1] - front_l[0], 3)
    ok = abs(front_span - ring_len) < 0.002
    print(("PASS " if ok else "FAIL ") + "front_span", front_span, "expected", ring_len)
    if not ok:
        failures.append("front_span")

    walls = ("Wall_Back", "Wall_Left", "Wall_Right",
             "Wall_Front_Left", "Wall_Front_Right", "Wall_Front_Lintel")
    boxes = {n: world_aabb(n) for n in walls}
    env = (
        min(b[0] for b in boxes.values()), max(b[1] for b in boxes.values()),
        min(b[2] for b in boxes.values()), max(b[3] for b in boxes.values()),
    )
    ok = all(abs(env[i] - (W_MIN, W_MAX, W_MIN, W_MAX)[i]) < 0.002 for i in range(4))
    print(("PASS " if ok else "FAIL ") + "envelope_xy", env, "expected", (W_MIN, W_MAX, W_MIN, W_MAX))
    if not ok:
        failures.append("envelope_xy")

    # Shared faces are OK; a 0.3×0.3 overlapping column is not.
    eps = 0.001
    overlap_pairs = (
        ("Wall_Left", "Wall_Front_Left"),
        ("Wall_Left", "Wall_Back"),
        ("Wall_Right", "Wall_Front_Right"),
        ("Wall_Right", "Wall_Back"),
    )
    for a_name, b_name in overlap_pairs:
        a, b = boxes[a_name], boxes[b_name]
        overlap = (
            a[0] < b[1] - eps and a[1] > b[0] + eps and
            a[2] < b[3] - eps and a[3] > b[2] + eps and
            a[4] < b[5] - eps and a[5] > b[4] + eps
        )
        print(("PASS " if not overlap else "FAIL ") + "no_overlap " + a_name + "×" + b_name)
        if overlap:
            failures.append("overlap_" + a_name + "_" + b_name)

    return failures


def export_fbx():
    os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    root = bpy.data.objects["DefaultBuilding"]
    root.select_set(True)
    for obj in root.children_recursive:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="Y",
        axis_up="Z",
        apply_unit_scale=True,
        bake_space_transform=False,
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        path_mode="AUTO",
    )
    print("SAVED_FBX", FBX_PATH)


def main():
    if "DefaultBuilding" not in os.path.basename(BLEND_PATH):
        print("REFUSING unexpected blend path:", BLEND_PATH)
        sys.exit(1)
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)

    clear_scene()

    m_wall = make_mat("Wall_Mat", (0.72, 0.70, 0.66, 1.0))
    m_floor = make_mat("Floor_Mat", (0.55, 0.48, 0.40, 1.0))
    m_ceil = make_mat("Ceiling_Mat", (0.62, 0.62, 0.65, 1.0))
    m_door = make_mat("Door_Mat", (0.45, 0.26, 0.13, 1.0))

    root = make_empty("DefaultBuilding", (0.0, 0.0, 0.0), None)

    make_box("RoomFloor", INT_MIN, INT_MAX, INT_MIN, INT_MAX, 0.0, FLOOR_T, m_floor, root)
    make_box("RoomCeiling", INT_MIN, INT_MAX, INT_MIN, INT_MAX,
             WALL_H - CEIL_T, WALL_H, m_ceil, root)

    # Picture-frame ring: Front/Back span W_MIN..W_MAX and own the corners.
    # Left/Right stop at INT_MIN..INT_MAX on Y (no overlapping corner columns).
    make_box("Wall_Back", W_MIN, W_MAX, B_IN, B_OUT, 0.0, WALL_H, m_wall, root)
    make_box("Wall_Left", W_MIN, INT_MIN, INT_MIN, INT_MAX, 0.0, WALL_H, m_wall, root)
    make_box("Wall_Right", INT_MAX, W_MAX, INT_MIN, INT_MAX, 0.0, WALL_H, m_wall, root)

    make_box("Wall_Front_Left", W_MIN, DOOR_X0, F_OUT, F_IN, 0.0, WALL_H, m_wall, root)
    make_box("Wall_Front_Right", DOOR_X1, W_MAX, F_OUT, F_IN, 0.0, WALL_H, m_wall, root)
    make_box("Wall_Front_Lintel", DOOR_X0, DOOR_X1, F_OUT, F_IN, DOOR_H, WALL_H, m_wall, root)

    hinge_l = make_empty("DoorHinge_Left", (DOOR_X0, DOOR_Y, 0.0), root)
    hinge_r = make_empty("DoorHinge_Right", (DOOR_X1, DOOR_Y, 0.0), root)

    # Hinge_Right is yawed 180 so BOTH hinges have their local +X pointing at
    # their own leaf. ShopDoorLeaf.IsInDoorway() measures the doorway volume as
    # x in (-0.15, doorwayWidth) along transform.right — if right pointed away
    # from the leaf, that volume would sit inside the wall and the door would
    # never trigger. Each leaf is therefore built on local +X.
    hinge_r.rotation_euler = (0.0, 0.0, math.pi)

    make_box("DoorLeaf_Left", 0.0, DOOR_W * 0.5,
             -LEAF_T * 0.5, LEAF_T * 0.5, 0.0, DOOR_H, m_door, hinge_l)
    make_box("DoorLeaf_Right", 0.0, DOOR_W * 0.5,
             -LEAF_T * 0.5, LEAF_T * 0.5, 0.0, DOOR_H, m_door, hinge_r)

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.update()

    print("=== DefaultBuilding blockout verification (corner trim, no overlap) ===")
    print("BLEND_PATH", BLEND_PATH)
    print("FBX_PATH", FBX_PATH)
    print("DOOR_W", DOOR_W)
    failures = verify()
    if failures:
        print("VERIFICATION FAILED:", failures)
        sys.exit(1)
    print("ALL CHECKS PASSED —", len(bpy.context.scene.objects), "objects")

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print("SAVED_BLEND", BLEND_PATH)
    if EXPORT_FBX:
        export_fbx()
    else:
        print("SKIP_FBX (pass --export-fbx or DEFAULT_BUILDING_EXPORT_FBX=1 to export)")
    print("DONE")


if __name__ == "__main__":
    main()
