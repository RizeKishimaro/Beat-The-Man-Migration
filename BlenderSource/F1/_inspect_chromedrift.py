"""Read-only inspect of Wheel_ChromeDrift.blend. Does not save."""
import bpy
import math
import os
from mathutils import Vector

OUT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_inspect.txt"
PREVIEW = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_inspect.png"

lines = []
def log(s=""):
    print(s)
    lines.append(s)

log(f"filepath={bpy.data.filepath}")
log("=== objects ===")
for o in bpy.data.objects:
    if o.type == "MESH":
        me = o.data
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        xs = [p.x for p in bb]; ys = [p.y for p in bb]; zs = [p.z for p in bb]
        log(f"MESH {o.name} verts={len(me.vertices)} faces={len(me.polygons)} "
            f"loc={tuple(round(v,4) for v in o.location)} "
            f"rot={tuple(round(math.degrees(v),2) for v in o.rotation_euler)} "
            f"scale={tuple(round(v,4) for v in o.scale)}")
        log(f"  world dims xyz=({max(xs)-min(xs):.4f}, {max(ys)-min(ys):.4f}, {max(zs)-min(zs):.4f})")
        log(f"  uv={[uv.name for uv in me.uv_layers]} mats={[m.name if m else None for m in me.materials]}")
        for i, mat in enumerate(me.materials):
            nfaces = sum(1 for p in me.polygons if p.material_index == i)
            log(f"  slot{i} {mat.name if mat else None} faces={nfaces}")
    else:
        log(f"{o.type} {o.name}")

log("=== images ===")
for img in bpy.data.images:
    log(f"  {img.name} size={tuple(img.size)} packed={img.packed_file is not None} path={img.filepath}")

log("=== materials ===")
for mat in bpy.data.materials:
    log(f"  {mat.name} nodes={mat.use_nodes}")
    if mat.use_nodes:
        for n in mat.node_tree.nodes:
            extra = ""
            if n.type == "BSDF_PRINCIPLED":
                bc = n.inputs["Base Color"].default_value
                extra = f" color={tuple(round(c,3) for c in bc)} metal={n.inputs['Metallic'].default_value:.2f} rough={n.inputs['Roughness'].default_value:.2f}"
            if n.type == "TEX_IMAGE" and n.image:
                extra = f" img={n.image.name}"
            log(f"    {n.type} {n.name}{extra}")

# Sidewall / radius analysis on the largest mesh
meshes = [o for o in bpy.data.objects if o.type == "MESH" and "Studio" not in o.name and "Backdrop" not in o.name]
if not meshes:
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
obj = max(meshes, key=lambda o: len(o.data.vertices)) if meshes else None
if obj:
    mw = obj.matrix_world
    pts = [mw @ v.co for v in obj.data.vertices]
    # Guess spin axis = longest of the two similar dims? ChromeDrift was meant to spin on X in blender then FBX.
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    spans = [("x", max(xs)-min(xs)), ("y", max(ys)-min(ys)), ("z", max(zs)-min(zs))]
    spans.sort(key=lambda t: t[1])
    # thinnest span is likely wheel width / spin axis
    axis = spans[0][0]
    log(f"=== radius (guess spin axis={axis}, spans={spans}) ===")
    def rad(p):
        if axis == "x":
            return math.hypot(p.y, p.z)
        if axis == "y":
            return math.hypot(p.x, p.z)
        return math.hypot(p.x, p.y)
    def axv(p):
        return getattr(p, axis)
    rs = [rad(p) for p in pts]
    axs = [axv(p) for p in pts]
    rmin, rmax = min(rs), max(rs)
    log(f"r [{rmin:.4f}, {rmax:.4f}] axis [{min(axs):.4f}, {max(axs):.4f}]")
    # bin by radius
    nb = 12
    for i in range(nb):
        lo = rmin + (rmax-rmin)*i/nb
        hi = rmin + (rmax-rmin)*(i+1)/nb
        sel = [axv(p) for p,r in zip(pts, rs) if lo <= r < hi or (i==nb-1 and r<=hi)]
        if not sel:
            continue
        log(f"  r[{lo:.3f},{hi:.3f}) n={len(sel)} ax_span={max(sel)-min(sel):.4f} ax_mean={sum(sel)/len(sel):.4f}")

    # faces by material + mean radius
    me = obj.data
    me.calc_loop_triangles()
    for mi, mat in enumerate(me.materials):
        frs = []
        for p in me.polygons:
            if p.material_index != mi:
                continue
            c = mw @ p.center
            frs.append(rad(c))
        if frs:
            log(f"  mat {mat.name if mat else mi} face_r mean={sum(frs)/len(frs):.4f} min={min(frs):.4f} max={max(frs):.4f} n={len(frs)}")

# preview render if camera exists
scene = bpy.context.scene
cam = bpy.data.objects.get("Studio_Cam") or scene.camera
if cam:
    scene.camera = cam
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except Exception:
        try:
            scene.render.engine = "BLENDER_EEVEE_NEXT"
        except Exception:
            pass
    scene.render.resolution_x = 800
    scene.render.resolution_y = 800
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
    log(f"preview={PREVIEW}")

with open(OUT, "w") as f:
    f.write("\n".join(lines) + "\n")
log(f"wrote {OUT}")
