"""
Ticket 130 — append game-ready ChromeDrift wheels into the Aero Kart
chassis+body blend for scale fitting.

Does not touch Wheel_ChromeDrift.blend or the preview file.
"""
import bpy
from mathutils import Vector

CAR = "/Users/sammy/Documents/GitHub/BeatTheMan/Assets/Objects/F1 Car/Meshy_AI_Aero_Kart_Prototype_0809085417_generate.blend"
WHEEL = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/Wheel_ChromeDrift_Game.blend"

KEEP = [
    "Wheel_ChromeDrift_Tire",
    "Wheel_ChromeDrift_Rim",
    "Wheel_ChromeDrift_Bolt",
    "Decal_Sidewall_Out",
]

# Hub at each suspension center (car: X=right-left, Y=front-back, Z=up).
SLOTS = [
    ("Wheel_FL", Vector((0.153, -0.389, -0.174))),
    ("Wheel_FR", Vector((-0.153, -0.389, -0.174))),
    ("Wheel_RL", Vector((0.213, 0.346, -0.105))),
    ("Wheel_RR", Vector((-0.213, 0.347, -0.105))),
]


def main():
    if bpy.data.filepath != CAR:
        bpy.ops.wm.open_mainfile(filepath=CAR)

    with bpy.data.libraries.load(WHEEL, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in KEEP]

    appended = [o for o in dst.objects if o is not None]
    if len(appended) != 4:
        raise RuntimeError("expected 4 wheel objects, got " + str([o.name for o in appended]))

    for o in appended:
        if o.name not in bpy.context.scene.collection.objects:
            bpy.context.scene.collection.objects.link(o)

    tire = bpy.data.objects["Wheel_ChromeDrift_Tire"]
    rim = bpy.data.objects["Wheel_ChromeDrift_Rim"]

    def make_rig(name, loc):
        bpy.ops.object.empty_add(type="PLAIN_AXES", location=loc)
        empty = bpy.context.active_object
        empty.name = name
        empty.empty_display_size = 0.08
        return empty

    # First copy uses the appended objects.
    root0 = make_rig(SLOTS[0][0], SLOTS[0][1])
    tire.parent = root0
    rim.parent = root0
    tire.location = (0.0, 0.0, 0.0)
    rim.location = (0.0, 0.0, 0.0)
    # Bolt/decal keep their existing parents (rim/tire).

    bpy.ops.object.select_all(action="DESELECT")
    root0.select_set(True)
    for c in root0.children_recursive:
        c.select_set(True)
    bpy.context.view_layer.objects.active = root0

    for name, loc in SLOTS[1:]:
        bpy.ops.object.duplicate()
        new_root = bpy.context.active_object
        new_root.name = name
        new_root.location = loc
        bpy.ops.object.select_all(action="DESELECT")
        new_root.select_set(True)
        for c in new_root.children_recursive:
            c.select_set(True)
        bpy.context.view_layer.objects.active = new_root

    bpy.ops.wm.save_mainfile()
    print("SAVED", bpy.data.filepath)
    print("WHEELS", [n for n, _ in SLOTS])


main()
