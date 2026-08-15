"""
Shrink Fusion XP 3000 wheel hub by 15% (radial, wheel plane only).
Remap the rubber sidewall so red decals stay on the new mid-circumference.

Backup: <same dir>/Meshy_AI_Fusion_XP_3000_Tire_0814081341_texture_BACKUP_pre_hub15.blend
"""
import bpy
import math
import os
import shutil

HUB_SCALE = 0.85  # reduce size 15%
BLEND_PATH = bpy.data.filepath
BACKUP_PATH = os.path.splitext(BLEND_PATH)[0] + "_BACKUP_pre_hub15.blend"
REPORT_PATH = os.path.join(os.path.dirname(BLEND_PATH), "_hub15_report.txt")
RENDER_PATH = os.path.join(os.path.dirname(BLEND_PATH), "_tire_hub_after.png")


def sample_rgba(pixels, iw, ih, uv, ch):
    u = uv[0] % 1.0
    v = uv[1] % 1.0
    x = min(iw - 1, max(0, int(u * iw)))
    y = min(ih - 1, max(0, int(v * ih)))
    return pixels[(y * iw + x) * 4 + ch]


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def main():
    if not os.path.exists(BACKUP_PATH):
        shutil.copy2(BLEND_PATH, BACKUP_PATH)

    obj = bpy.data.objects["mesh_node"]
    me = obj.data
    mw = obj.matrix_world
    imw = mw.inverted()
    nverts = len(me.vertices)

    mat = me.materials[0]
    met_img = None
    albedo = None
    for node in mat.node_tree.nodes:
        if node.type != "TEX_IMAGE" or node.image is None:
            continue
        name = node.image.name.lower()
        if "metallic" in name:
            met_img = node.image
        if "base_color" in name:
            albedo = node.image
    met_pix = list(met_img.pixels)
    mw_, mh = met_img.size
    alb_pix = list(albedo.pixels) if albedo else None
    aw, ah = albedo.size if albedo else (0, 0)

    cx = cy = cz = 0.0
    worlds = [mw @ v.co for v in me.vertices]
    for p in worlds:
        cx += p.x
        cy += p.y
        cz += p.z
    cx /= nverts
    cy /= nverts
    cz /= nverts

    uv_layer = me.uv_layers.active.data
    uvs = [None] * nverts
    for i, loop in enumerate(me.loops):
        vi = loop.vertex_index
        if uvs[vi] is None:
            uvs[vi] = (uv_layer[i].uv[0], uv_layer[i].uv[1])

    metallic = [0.0] * nverts
    is_red = [False] * nverts
    rs = [0.0] * nverts
    for i, p in enumerate(worlds):
        rs[i] = math.hypot(p.x - cx, p.z - cz)
        uv = uvs[i] or (0.0, 0.0)
        metallic[i] = sample_rgba(met_pix, mw_, mh, uv, 2)
        if alb_pix:
            cr = sample_rgba(alb_pix, aw, ah, uv, 0)
            cg = sample_rgba(alb_pix, aw, ah, uv, 1)
            cb = sample_rgba(alb_pix, aw, ah, uv, 2)
            is_red[i] = cr > 0.45 and cr > cg * 1.6 and cr > cb * 1.6

    hub_rs = [rs[i] for i in range(nverts) if metallic[i] > 0.5]
    hub_rs.sort()
    hub_r = hub_rs[int(len(hub_rs) * 0.97)]
    r_max = max(rs)
    hub_new = hub_r * HUB_SCALE

    red_before = [rs[i] for i in range(nverts) if is_red[i]]
    red_before.sort()
    red_mean_before = sum(red_before) / len(red_before) if red_before else 0.0

    lines = [
        f"center=({cx:.4f},{cy:.4f},{cz:.4f})",
        f"hub_r={hub_r:.5f} hub_new={hub_new:.5f} r_max={r_max:.5f} scale={HUB_SCALE}",
        f"red_decal_verts={len(red_before)} mean_r_before={red_mean_before:.5f}",
        f"old_sidewall_mid={(hub_r + r_max) * 0.5:.5f} new_sidewall_mid={(hub_new + r_max) * 0.5:.5f}",
        f"backup={BACKUP_PATH}",
    ]

    for i, p in enumerate(worlds):
        r = rs[i]
        if r < 1e-8:
            continue
        if r <= hub_r:
            rubber_r = r * HUB_SCALE
        else:
            t = (r - hub_r) / (r_max - hub_r)
            rubber_r = hub_new + t * (r_max - hub_new)
        hubbed_r = r * HUB_SCALE
        w = smoothstep((metallic[i] - 0.25) / 0.5)
        new_r = rubber_r * (1.0 - w) + hubbed_r * w
        s = new_r / r
        p.x = cx + (p.x - cx) * s
        p.z = cz + (p.z - cz) * s
        me.vertices[i].co = imw @ p

    me.update()

    red_after = []
    hub_after = []
    for i, v in enumerate(me.vertices):
        p = mw @ v.co
        r = math.hypot(p.x - cx, p.z - cz)
        if is_red[i]:
            red_after.append(r)
        if metallic[i] > 0.5:
            hub_after.append(r)
    red_mean_after = sum(red_after) / len(red_after) if red_after else 0.0
    hub_r_after = sorted(hub_after)[int(len(hub_after) * 0.97)] if hub_after else 0.0
    lines.append(f"hub_r_after={hub_r_after:.5f} (want ~{hub_new:.5f})")
    lines.append(f"red_mean_r_after={red_mean_after:.5f} (want ~{(hub_new + r_max) * 0.5:.5f})")
    lines.append(f"hub_ratio={hub_r_after / hub_r:.4f}")

    bpy.ops.wm.save_mainfile()

    cam = bpy.data.objects.get("Studio_Cam")
    scene = bpy.context.scene
    if cam:
        scene.camera = cam
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except Exception:
        pass
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.filepath = RENDER_PATH
    scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)
    lines.append(f"render={RENDER_PATH}")

    text = "\n".join(lines)
    with open(REPORT_PATH, "w") as f:
        f.write(text + "\n")
    print(text)


if __name__ == "__main__":
    main()
