"""Probe metallic vs radius so hub/tire/decal bands are known before edit."""
import bpy
import math
import os
from mathutils import Vector

obj = bpy.data.objects["mesh_node"]
me = obj.data
mw = obj.matrix_world
mat = me.materials[0]
img = None
for node in mat.node_tree.nodes:
    if node.type == "TEX_IMAGE" and node.image and "metallic" in node.image.name.lower():
        img = node.image
        break
    if node.type == "TEX_IMAGE" and node.image and "base_color" in node.image.name.lower():
        albedo = node.image

albedo = None
for node in mat.node_tree.nodes:
    n = node.image.name.lower() if node.type == "TEX_IMAGE" and node.image else ""
    if "metallic" in n:
        img = node.image
    if "base_color" in n or "basecolor" in n or n.endswith("albedo"):
        albedo = node.image

print("metallic_img", img.name if img else None, "albedo", albedo.name if albedo else None)
pixels = list(img.pixels) if img else None
w, h = img.size if img else (0, 0)
apix = list(albedo.pixels) if albedo else None
aw, ah = albedo.size if albedo else (0, 0)


def sample(pix, iw, ih, uv, ch):
    u = uv[0] % 1.0
    v = uv[1] % 1.0
    x = min(iw - 1, max(0, int(u * iw)))
    y = min(ih - 1, max(0, int(v * ih)))
    return pix[(y * iw + x) * 4 + ch]


uv_layer = me.uv_layers.active.data
# polygon-vertex UV: use loop
# We'll sample via loops then average per vertex — too heavy.
# Instead sample a subset of loops.

cx = cy = cz = 0.0
n = len(me.vertices)
for v in me.vertices:
    p = mw @ v.co
    cx += p.x
    cy += p.y
    cz += p.z
cx /= n
cy /= n
cz /= n

# axis is y (thinnest)
bins = 24
met_sum = [0.0] * bins
red_sum = [0.0] * bins
cnt = [0] * bins
r_max = 0.9586

for poly in me.polygons:
    for li in poly.loop_indices:
        loop = me.loops[li]
        uv = uv_layer[li].uv
        p = mw @ me.vertices[loop.vertex_index].co
        r = math.hypot(p.x - cx, p.z - cz)
        bi = min(bins - 1, int(r / r_max * bins))
        met = sample(pixels, w, h, uv, 2)  # B = metallic in glTF pack
        # also try G
        met_g = sample(pixels, w, h, uv, 1)
        # use max of B/G as metallic-ish
        met_sum[bi] += met
        if apix:
            cr = sample(apix, aw, ah, uv, 0)
            cg = sample(apix, aw, ah, uv, 1)
            cb = sample(apix, aw, ah, uv, 2)
            # red decal: high R, low G/B
            if cr > 0.45 and cr > cg * 1.6 and cr > cb * 1.6:
                red_sum[bi] += 1
        cnt[bi] += 1

print("bin r_lo r_hi n mean_metallic_B red_decal_loops")
for i in range(bins):
    lo = r_max * i / bins
    hi = r_max * (i + 1) / bins
    m = met_sum[i] / cnt[i] if cnt[i] else 0
    print(f"{i:02d} {lo:.3f}-{hi:.3f} n={cnt[i]:7d} metB={m:.3f} red={int(red_sum[i])}")

# Channel stats
s0 = s1 = s2 = 0.0
k = 0
for i in range(0, len(pixels), 4):
    s0 += pixels[i]
    s1 += pixels[i + 1]
    s2 += pixels[i + 2]
    k += 1
print(f"metallic_tex mean R={s0/k:.3f} G={s1/k:.3f} B={s2/k:.3f}")
