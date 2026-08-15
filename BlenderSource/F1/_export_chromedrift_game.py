"""
Ticket 130 — export the approved ChromeDrift wheel into Unity.

Reads _chromedrift_decal_preview.blend (does not overwrite it).
Applies the sidewall shrinkwrap, then writes FBX + the decal PNG.
"""
import bpy
import os
import shutil

PREVIEW = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_decal_preview.blend"
GAME_BLEND = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/Wheel_ChromeDrift_Game.blend"
ART_DIR = "/Users/sammy/Documents/GitHub/BeatTheMan/Assets/Art/Vehicles/Wheel_ChromeDrift"
OBJECTS_FBX = "/Users/sammy/Documents/GitHub/BeatTheMan/Assets/Objects/F1 Car/Wheel_ChromeDrift.fbx"
DECAL_SRC = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/Decals/Soft Tyre.png"
FBX_PATH = os.path.join(ART_DIR, "Wheel_ChromeDrift.fbx")
DECAL_DST = os.path.join(ART_DIR, "SoftTyre.png")

KEEP = {
    "Wheel_ChromeDrift_Tire",
    "Wheel_ChromeDrift_Rim",
    "Wheel_ChromeDrift_Bolt",
    "Decal_Sidewall_Out",
}


def apply_mods(obj):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="OBJECT")
    for mod in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)


def main():
    os.makedirs(ART_DIR, exist_ok=True)
    if os.path.abspath(bpy.data.filepath) != os.path.abspath(PREVIEW):
        bpy.ops.wm.open_mainfile(filepath=PREVIEW)

    missing = [n for n in KEEP if bpy.data.objects.get(n) is None]
    if missing:
        raise RuntimeError("missing objects: " + ", ".join(missing))

    decal = bpy.data.objects["Decal_Sidewall_Out"]
    apply_mods(decal)

    for obj in list(bpy.data.objects):
        if obj.name not in KEEP and obj.type == "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)

    bpy.ops.object.select_all(action="DESELECT")
    for name in KEEP:
        bpy.data.objects[name].select_set(True)
    bpy.context.view_layer.objects.active = bpy.data.objects["Wheel_ChromeDrift_Rim"]

    bpy.ops.wm.save_as_mainfile(filepath=GAME_BLEND)

    shutil.copy2(DECAL_SRC, DECAL_DST)

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
    shutil.copy2(FBX_PATH, OBJECTS_FBX)
    print("FBX", FBX_PATH)
    print("OBJECTS", OBJECTS_FBX)
    print("DECAL", DECAL_DST)
    print("BLEND", GAME_BLEND)


main()
