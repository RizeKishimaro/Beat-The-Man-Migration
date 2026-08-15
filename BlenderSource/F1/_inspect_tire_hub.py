"""Read-only inspect of the Fusion XP tire: materials, islands, radial bins, render."""
import bpy
import bmesh
import math
import os
from collections import defaultdict
from mathutils import Vector

OUT_DIR = bpy.path.abspath("//")
REPORT = os.path.join(OUT_DIR, "_tire_hub_inspect.txt")


def world_co(mw, v):
    return mw @ v.co


def main():
    obj = bpy.data.objects.get("mesh_node")
    if obj is None:
        obj = next(o for o in bpy.data.objects if o.type == "MESH" and o.name != "Studio_Backdrop")

    me = obj.data
    mw = obj.matrix_world
    lines = [f"object={obj.name} mesh={me.name} verts={len(me.vertices)} faces={len(me.polygons)}"]
    lines.append(f"materials={len(me.materials)}")
    for i, mat in enumerate(me.materials):
        n = mat.name if mat else "None"
        lines.append(f"  slot {i}: {n}")
        if mat and mat.use_nodes:
            for node in mat.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    lines.append(f"    tex={node.image.name} size={node.image.size[:]}")

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()

    world = [world_co(mw, v) for v in bm.verts]
    cx = sum(v.x for v in world) / len(world)
    cy = sum(v.y for v in world) / len(world)
    cz = sum(v.z for v in world) / len(world)
    xs = [v.x for v in world]
    ys = [v.y for v in world]
    zs = [v.z for v in world]
    spans = [(max(xs) - min(xs), "x"), (max(ys) - min(ys), "y"), (max(zs) - min(zs), "z")]
    spans.sort()
    axis = spans[0][1]
    lines.append(f"center=({cx:.4f},{cy:.4f},{cz:.4f}) axis={axis} spans={[(round(s, 4), a) for s, a in spans]}")

    def radial(v):
        if axis == "x":
            return math.hypot(v.y - cy, v.z - cz)
        if axis == "y":
            return math.hypot(v.x - cx, v.z - cz)
        return math.hypot(v.x - cx, v.y - cy)

    def axial(v):
        if axis == "x":
            return v.x - cx
        if axis == "y":
            return v.y - cy
        return v.z - cz

    rs = [radial(v) for v in world]
    ax = [axial(v) for v in world]
    r_max = max(rs)
    lines.append(f"r min={min(rs):.4f} max={r_max:.4f} ax min={min(ax):.4f} max={max(ax):.4f}")

    # Radial histogram
    bins = 20
    hist = [0] * bins
    for r in rs:
        i = min(bins - 1, int(r / r_max * bins))
        hist[i] += 1
    lines.append("radial_hist (inner->outer):")
    for i, c in enumerate(hist):
        lo = r_max * i / bins
        hi = r_max * (i + 1) / bins
        lines.append(f"  [{lo:.3f},{hi:.3f}) n={c}")

    # Connected components
    parent = list(range(len(bm.verts)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    for e in bm.edges:
        union(e.verts[0].index, e.verts[1].index)

    comps = defaultdict(list)
    for v in bm.verts:
        comps[find(v.index)].append(v)

    sized = sorted(((len(vs), k) for k, vs in comps.items()), reverse=True)
    lines.append(f"components={len(comps)} top={[s for s, _ in sized[:15]]}")

    for sz, k in sized[:12]:
        vs = comps[k]
        cc = sum((world_co(mw, v) for v in vs), Vector()) / len(vs)
        rcs = [radial(world_co(mw, v)) for v in vs]
        acs = [axial(world_co(mw, v)) for v in vs]
        lines.append(
            f"  comp n={sz} c=({cc.x:.3f},{cc.y:.3f},{cc.z:.3f}) "
            f"r=[{min(rcs):.3f},{max(rcs):.3f}] mean_r={sum(rcs)/len(rcs):.3f} "
            f"ax=[{min(acs):.3f},{max(acs):.3f}]"
        )

    # Material face counts + radial range
    mat_faces = defaultdict(list)
    for f in bm.faces:
        mat_faces[f.material_index].append(f)
    lines.append("material_faces:")
    for mi, faces in sorted(mat_faces.items()):
        name = me.materials[mi].name if mi < len(me.materials) and me.materials[mi] else "?"
        verts = {v for f in faces for v in f.verts}
        rcs = [radial(world_co(mw, v)) for v in verts]
        lines.append(
            f"  mat {mi} {name} faces={len(faces)} verts={len(verts)} "
            f"r=[{min(rcs):.3f},{max(rcs):.3f}] mean_r={sum(rcs)/len(rcs):.3f}"
        )

    bm.free()

    # Render from studio cam
    cam = bpy.data.objects.get("Studio_Cam")
    scene = bpy.context.scene
    if cam:
        scene.camera = cam
    scene.render.engine = "BLENDER_EEVEE_NEXT" if hasattr(bpy.types, "EEVEE_NEXT") else scene.render.engine
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except Exception:
        pass
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.filepath = os.path.join(OUT_DIR, "_tire_hub_preview.png")
    scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)
    lines.append(f"render={scene.render.filepath}")

    text = "\n".join(lines)
    with open(REPORT, "w") as f:
        f.write(text)
    print(text)


if __name__ == "__main__":
    main()
