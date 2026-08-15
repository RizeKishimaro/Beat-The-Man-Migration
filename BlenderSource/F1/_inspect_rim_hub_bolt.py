"""Read-only: bin ChromeDrift rim faces to find hub vs bolt. Does not save the blend."""
import bpy
import math
from mathutils import Vector

OUT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_rim_hub_bolt.txt"
DEBUG = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_rim_hub_bolt_debug.png"

obj = bpy.data.objects.get("Wheel_ChromeDrift_Rim")
me = obj.data
mw = obj.matrix_world

lines = []
def log(s=""):
    print(s)
    lines.append(s)

log(f"rim verts={len(me.vertices)} faces={len(me.polygons)}")

# Per-face stats
rows = []
for p in me.polygons:
    c = mw @ p.center
    r = math.hypot(c.y, c.z)
    nx = (mw.to_3x3() @ p.normal).x
    rows.append((r, c.x, abs(nx), p.index, nx))

rs = [t[0] for t in rows]
xs = [t[1] for t in rows]
log(f"face_r [{min(rs):.4f},{max(rs):.4f}] face_x [{min(xs):.4f},{max(xs):.4f}]")

# Fine radial bins
nbin = 24
rmin, rmax = min(rs), max(rs)
for i in range(nbin):
    lo = rmin + (rmax - rmin) * i / nbin
    hi = rmin + (rmax - rmin) * (i + 1) / nbin
    sel = [t for t in rows if lo <= t[0] < hi or (i == nbin - 1 and t[0] <= hi)]
    if not sel:
        continue
    fx = [t[1] for t in sel]
    fn = [t[2] for t in sel]
    log(f"  r[{lo:.3f},{hi:.3f}) n={len(sel):4d} x[{min(fx):.3f},{max(fx):.3f}] "
        f"mean_x={sum(fx)/len(fx):.3f} mean_|nx|={sum(fn)/len(fn):.2f}")

# Front-facing (visible hub) faces: +X normal, outer-ish x
front = [t for t in rows if t[4] > 0.4]
log(f"front +nx n={len(front)} r[{min(t[0] for t in front):.4f},{max(t[0] for t in front):.4f}]")

# Paint vertex colors by radius so we can see bands
if not me.color_attributes:
    me.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="CORNER")
col = me.color_attributes[0]
# 8 distinct bands
palette = [
    (1, 0, 0, 1), (1, 0.5, 0, 1), (1, 1, 0, 1), (0, 1, 0, 1),
    (0, 1, 1, 1), (0, 0.4, 1, 1), (0.6, 0, 1, 1), (1, 1, 1, 1),
]
for p in me.polygons:
    c = mw @ p.center
    r = math.hypot(c.y, c.z)
    t = (r - rmin) / max(1e-6, rmax - rmin)
    bi = min(7, int(t * 8))
    color = palette[bi]
    for li in p.loop_indices:
        col.data[li].color = color

# Temp material using vertex color
mat = bpy.data.materials.new("DEBUG_VC")
mat.use_nodes = True
nt = mat.node_tree
for n in list(nt.nodes):
    nt.nodes.remove(n)
out = nt.nodes.new("ShaderNodeOutputMaterial")
em = nt.nodes.new("ShaderNodeEmission")
vc = nt.nodes.new("ShaderNodeVertexColor")
vc.layer_name = col.name
nt.links.new(vc.outputs["Color"], em.inputs["Color"])
em.inputs["Strength"].default_value = 1.0
nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
me.materials.clear()
me.materials.append(mat)

tire = bpy.data.objects.get("Wheel_ChromeDrift_Tire")
if tire:
    tire.hide_render = True
    tire.hide_set(True)

scene = bpy.context.scene
cam = bpy.data.objects.get("Studio_Cam")
try:
    scene.render.engine = "BLENDER_EEVEE_NEXT"
except Exception:
    scene.render.engine = "BLENDER_EEVEE"
if cam:
    scene.camera = cam
    cam.location = (1.2, 0.0, 0.05)
    cam.rotation_euler = (math.radians(90), 0, math.radians(90))
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900
    scene.render.filepath = DEBUG
    bpy.ops.render.render(write_still=True)
    log(f"debug={DEBUG}")

with open(OUT, "w") as f:
    f.write("\n".join(lines) + "\n")
print("wrote", OUT)
