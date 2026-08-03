"""
Build Item_Crate_Base only. Must never touch Character_Base paths.
Run: Blender --background --python this_script.py
"""
import bpy
import bmesh
import os
import sys

BLEND_PATH = "/Users/sammy/BeatTheMan/BlenderSource/Items/Item_Crate_Base.blend"
FBX_PATH = "/Users/sammy/BeatTheMan/Assets/Art/Items/Item_Crate_Base.fbx"

PROTECTED = (
    "/Users/sammy/BeatTheMan/Assets/Art/Characters/Character_Base.fbx",
    "/Users/sammy/BeatTheMan/Assets/Characters/Character_Base/Character_Base.fbx",
    "/Users/sammy/BeatTheMan/BlenderSource/Characters/Character_Base.blend",
)

for p in (BLEND_PATH, FBX_PATH):
    if "Character_Base" in p:
        print("REFUSING path that mentions Character_Base:", p)
        sys.exit(1)
    if "Item_Crate_Base" not in os.path.basename(p):
        print("REFUSING unexpected basename:", p)
        sys.exit(1)

for p in PROTECTED:
    if os.path.abspath(p) in (os.path.abspath(BLEND_PATH), os.path.abspath(FBX_PATH)):
        print("REFUSING: target collides with protected Character_Base path")
        sys.exit(1)

os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)

for collection in (bpy.data.objects, bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
    for item in list(collection):
        collection.remove(item)

mesh = bpy.data.meshes.new("Item_Crate_Base")
obj = bpy.data.objects.new("Item_Crate_Base", mesh)
bpy.context.scene.collection.objects.link(obj)

bm = bmesh.new()
bmesh.ops.create_cube(bm, size=0.7)
for v in bm.verts:
    v.co.z += 0.35  # bottom on Z=0, origin at bottom center
bm.to_mesh(mesh)
bm.free()

obj.location = (0.0, 0.0, 0.0)

mat = bpy.data.materials.new(name="Item_Crate_Base_Mat")
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get("Principled BSDF")
if bsdf:
    bsdf.inputs["Base Color"].default_value = (0.45, 0.45, 0.48, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.75
mesh.materials.append(mat)

coords = [obj.matrix_world @ v.co for v in mesh.vertices]
zs = [c.z for c in coords]
xs = [c.x for c in coords]
ys = [c.y for c in coords]
print(
    "CRATE_INFO",
    {
        "name": obj.name,
        "z_min": round(min(zs), 4),
        "z_max": round(max(zs), 4),
        "size": (
            round(max(xs) - min(xs), 4),
            round(max(ys) - min(ys), 4),
            round(max(zs) - min(zs), 4),
        ),
        "origin": tuple(obj.location),
        "verts": len(mesh.vertices),
    },
)

bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
print("SAVED_BLEND", BLEND_PATH)

# Select only the crate for export
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj

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
    path_mode="AUTO",
)
print("SAVED_FBX", FBX_PATH)

# Final safety check: Character_Base files unchanged (mtime compare printed by shell after)
print("OBJECTS_IN_FILE", [o.name for o in bpy.data.objects])
print("DONE")
