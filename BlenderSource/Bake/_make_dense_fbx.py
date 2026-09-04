"""Ticket 163 — write two dirty high-tri FBXs at spike scale and 3x.

Blender --background --python BlenderSource/Bake/_make_dense_fbx.py
Outputs Temp/bake163/dense/spike_scale.fbx and Temp/bake163/dense/triple_scale.fbx
"""
from __future__ import annotations

import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUTDIR = os.path.join(ROOT, "Temp", "bake163", "dense")

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


def longest_aabb(obj):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    xs = [c.x for c in corners]
    ys = [c.y for c in corners]
    zs = [c.z for c in corners]
    return max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))


def poke_holes(obj, n_faces=40):
    bm = bmesh.new()
    try:
        bm.from_mesh(obj.data)
        bm.faces.ensure_lookup_table()
        kill = list(bm.faces)[: min(n_faces, len(bm.faces))]
        bmesh.ops.delete(bm, geom=kill, context="FACES")
        bm.to_mesh(obj.data)
    finally:
        bm.free()
    obj.data.update()


def export_obj(obj, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=path, **FBX_EXPORT)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.unit_settings.length_unit = "METERS"

    # 0.81 x 0.4 x 0.15 m slab (longest = spike 0.81). Subdivide so raw tris
    # exceed target, then punch faces so remesh has dirty topology to clean.
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.0, 0.0, 0.075))
    obj = bpy.context.active_object
    obj.name = "Ticket163Dense"
    obj.dimensions = (0.81, 0.4, 0.15)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.subdivide(number_cuts=24)
    bpy.ops.object.mode_set(mode="OBJECT")
    poke_holes(obj, 40)

    spike_path = os.path.join(OUTDIR, "spike_scale.fbx")
    export_obj(obj, spike_path)
    spike_long = longest_aabb(obj)

    obj.scale = (3.0, 3.0, 3.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    triple_path = os.path.join(OUTDIR, "triple_scale.fbx")
    export_obj(obj, triple_path)
    triple_long = longest_aabb(obj)

    print("DENSE_OK", spike_path, "longest", round(spike_long, 4))
    print("DENSE_OK", triple_path, "longest", round(triple_long, 4))
    sys.exit(0)


if __name__ == "__main__":
    main()
