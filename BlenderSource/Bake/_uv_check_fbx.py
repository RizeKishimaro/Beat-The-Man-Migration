"""UV stats for one FBX. blender --background --python ... -- --input <fbx>"""
from __future__ import annotations

import json
import os
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
path = args[args.index("--input") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
n_layers = n_loops = nz = 0
umin = vmin = 1e9
umax = vmax = -1e9
for o in bpy.context.scene.objects:
    if o.type != "MESH" or o.data is None:
        continue
    n_layers += len(o.data.uv_layers)
    if not o.data.uv_layers:
        continue
    uv = o.data.uv_layers.active.data
    n_loops += len(uv)
    for it in uv:
        x, y = it.uv.x, it.uv.y
        if x != 0.0 or y != 0.0:
            nz += 1
        umin = min(umin, x)
        umax = max(umax, x)
        vmin = min(vmin, y)
        vmax = max(vmax, y)
print(
    json.dumps(
        {
            "uv_layers": n_layers,
            "uv_loops": n_loops,
            "uv_nonzero": nz,
            "uv_bounds": [round(umin, 4), round(vmin, 4), round(umax, 4), round(vmax, 4)]
            if n_loops
            else None,
            "uv_intact": n_layers > 0 and nz > 0 and (umax - umin) > 0.01,
        }
    )
)
