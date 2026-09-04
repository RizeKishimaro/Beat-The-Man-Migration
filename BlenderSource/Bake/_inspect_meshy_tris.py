"""Print mesh_count + tris for candidate FBXs. Blender --background --python ..."""
from __future__ import annotations

import json
import os
import sys

import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CANDIDATES = [
    "Temp/bake163/second_src/orange/Meshy_AI_Orange_Office_Chair_0826075021_texture_fbx/Meshy_AI_Orange_Office_Chair_0826075021_texture.fbx",
    "Temp/bake163/second_src/cream1618/Meshy_AI_Cream_Cushioned_Woode_0901111618_texture_fbx/Meshy_AI_Cream_Cushioned_Woode_0901111618_texture.fbx",
    "Temp/bake163/second_src/mystery/Meshy_AI__0901083831_texture_fbx/Meshy_AI__0901083831_texture.fbx",
    "_Spike163/RawSource/Meshy_AI_Cream_Cushioned_Woode_0901123805_texture_fbx/Meshy_AI_Cream_Cushioned_Woode_0901123805_texture.fbx",
]


def tri_count():
    n = 0
    for o in bpy.context.scene.objects:
        if o.type != "MESH" or o.data is None:
            continue
        o.data.calc_loop_triangles()
        n += len(o.data.loop_triangles)
    return n


def main():
    rows = []
    for rel in CANDIDATES:
        path = os.path.join(ROOT, rel)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        if not os.path.isfile(path):
            rows.append({"path": rel, "missing": True})
            continue
        bpy.ops.import_scene.fbx(filepath=path)
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        rows.append({
            "path": rel,
            "bytes": os.path.getsize(path),
            "mesh_count": len(meshes),
            "names": [o.name for o in meshes][:8],
            "tris": tri_count(),
            "uv_layers": sum(len(o.data.uv_layers) for o in meshes if o.data),
        })
    print(json.dumps(rows, indent=2))


if __name__ == "__main__":
    main()
