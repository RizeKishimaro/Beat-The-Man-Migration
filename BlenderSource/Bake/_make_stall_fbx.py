"""Single-object mesh whose Collapse floor stays above 2x target.

Thousands of 3-face edges (non-manifold). Fill Holes cannot cap those.
Used only to prove 163 stall → _Review halt. Not a Meshy drop.

Blender --background --python BlenderSource/Bake/_make_stall_fbx.py
"""
from __future__ import annotations

import os
import sys

import bmesh
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
import _bake_fbx as bake  # noqa: E402

OUT = os.path.join(ROOT, "Temp", "bake163", "stall", "dirty_join.fbx")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.unit_settings.length_unit = "METERS"

    mesh = bpy.data.meshes.new("Ticket163Stall")
    obj = bpy.data.objects.new("Ticket163Stall", mesh)
    scene.collection.objects.link(obj)

    bm = bmesh.new()
    # 8000 fans × 3 tris = 24k tris. Verts 2cm apart so merge 0.1mm cannot weld.
    for i in range(8000):
        x = (i % 100) * 0.02
        y = (i // 100) * 0.02
        v0 = bm.verts.new((x, y, 0.0))
        v1 = bm.verts.new((x, y + 0.01, 0.0))
        v2 = bm.verts.new((x, y + 0.005, 0.01))
        v3 = bm.verts.new((x + 0.005, y + 0.005, 0.01))
        v4 = bm.verts.new((x - 0.005, y + 0.005, 0.01))
        bm.faces.new((v0, v1, v2))
        bm.faces.new((v0, v1, v3))
        bm.faces.new((v0, v1, v4))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bake.select_meshes([obj])
    bpy.ops.export_scene.fbx(filepath=OUT, **bake.FBX_EXPORT)
    obj.data.calc_loop_triangles()
    print(
        "STALL_MESH",
        OUT,
        "tris",
        len(obj.data.loop_triangles),
        "topo",
        bake.edge_topology([obj]),
    )
    sys.exit(0)


if __name__ == "__main__":
    main()
