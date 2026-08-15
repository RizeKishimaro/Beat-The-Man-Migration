"""Read-only: tire sidewall + UV dump for ChromeDrift. Does not save."""
import bpy
import math
import os
from mathutils import Vector

OUT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_tire_inspect.txt"
UV_PNG = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_tire_uv.png"
SIDE_PNG = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_sidewall.png"

obj = bpy.data.objects.get("Wheel_ChromeDrift_Tire")
rim = bpy.data.objects.get("Wheel_ChromeDrift_Rim")
me = obj.data
mw = obj.matrix_world
pts = [mw @ v.co for v in me.vertices]

def rad(p):
    return math.hypot(p.y, p.z)

rs = [rad(p) for p in pts]
xs = [p.x for p in pts]
lines = []
def log(s=""):
    print(s); lines.append(s)

log(f"tire verts={len(me.vertices)} faces={len(me.polygons)}")
log(f"r [{min(rs):.4f},{max(rs):.4f}] x [{min(xs):.4f},{max(xs):.4f}] width={max(xs)-min(xs):.4f} diam={2*max(rs):.4f}")
if rim:
    rpts = [rim.matrix_world @ v.co for v in rim.data.vertices]
    rr = [math.hypot(p.y, p.z) for p in rpts]
    log(f"rim r [{min(rr):.4f},{max(rr):.4f}] x [{min(p.x for p in rpts):.4f},{max(p.x for p in rpts):.4f}]")

# radial bins on tire
rmin, rmax = min(rs), max(rs)
nb = 10
log("=== tire radial bins (spin axis X) ===")
for i in range(nb):
    lo = rmin + (rmax-rmin)*i/nb
    hi = rmin + (rmax-rmin)*(i+1)/nb
    sel = [(p,r) for p,r in zip(pts,rs) if lo <= r < hi or (i==nb-1 and r<=hi)]
    if not sel:
        continue
    ax = [p.x for p,_ in sel]
    log(f"  r[{lo:.3f},{hi:.3f}) n={len(sel)} x_span={max(ax)-min(ax):.4f} |x|_mean={sum(abs(v) for v in ax)/len(ax):.4f}")

# Sidewall verts: high |x| relative to local max at that radius? Or simply faces pointing mostly along X
log("=== faces by |normal.x| (sidewall vs tread) ===")
side, tread, other = [], [], []
for p in me.polygons:
    n = mw.to_3x3() @ p.normal
    n.normalize()
    c = mw @ p.center
    r = rad(c)
    if abs(n.x) > 0.7:
        side.append((c, r, n.x))
    elif abs(n.x) < 0.35:
        tread.append((c, r, n.x))
    else:
        other.append((c, r, n.x))
for name, arr in (("sidewall", side), ("tread", tread), ("shoulder", other)):
    if not arr:
        continue
    fr = [t[1] for t in arr]
    fx = [t[0].x for t in arr]
    log(f"  {name} n={len(arr)} r[{min(fr):.3f},{max(fr):.3f}] mean_r={sum(fr)/len(fr):.3f} x[{min(fx):.3f},{max(fx):.3f}]")

# UV bounds
uv = me.uv_layers.active.data if me.uv_layers.active else None
if uv:
    us = [l.uv.x for l in uv]; vs = [l.uv.y for l in uv]
    log(f"UV u[{min(us):.3f},{max(us):.3f}] v[{min(vs):.3f},{max(vs):.3f}]")

# Sidewall-facing camera along +X
scene = bpy.context.scene
cam = bpy.data.objects.get("Studio_Cam")
if cam:
    cam.location = (1.15, 0.0, 0.0)
    cam.rotation_euler = (math.radians(90), 0, math.radians(90))
    scene.camera = cam
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except Exception:
        scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900
    scene.render.filepath = SIDE_PNG
    bpy.ops.render.render(write_still=True)
    log(f"sidewall_preview={SIDE_PNG}")

with open(OUT, "w") as f:
    f.write("\n".join(lines)+"\n")
print("wrote", OUT)
