"""
Remove leftover embossed decals from the tire normal map.
Mask is rebuilt from the pre-strip albedo backup (red paint).
"""
import bpy
import os
import numpy as np

CURRENT = bpy.data.filepath
ROOT = os.path.abspath(os.path.join(os.path.dirname(CURRENT), "..", "..", ".."))
BACKUP = os.path.join(
    ROOT,
    "BlenderSource",
    "F1",
    "Meshy_AI_Fusion_XP_3000_Tire_0814081341_texture_BACKUP_pre_decal_strip.blend",
)
RENDER = os.path.join(os.path.dirname(CURRENT), "_tire_nodecal_preview.png")
REPORT = os.path.join(ROOT, "BlenderSource", "F1", "_decal_strip_report.txt")


def image_rgba(img):
    w, h = int(img.size[0]), int(img.size[1])
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4), w, h


def find_img(name_part):
    for im in bpy.data.images:
        if im.name and name_part in im.name.lower():
            return im
    return None


def red_mask(px):
    r, g, b = px[:, :, 0], px[:, :, 1], px[:, :, 2]
    red = (r > 0.45) & (r > g * 1.6) & (r > b * 1.6)
    dil = red.copy()
    for dy in range(-4, 5):
        for dx in range(-4, 5):
            dil |= np.roll(np.roll(red, dy, axis=0), dx, axis=1)
    return dil, int(red.sum())


def main():
    bpy.ops.wm.open_mainfile(filepath=BACKUP)
    albedo = find_img("base_color")
    if albedo is None:
        raise RuntimeError("backup has no base_color")
    alb, w, h = image_rgba(albedo)
    dil, red_n = red_mask(alb)

    bpy.ops.wm.open_mainfile(filepath=CURRENT)
    nrm = find_img("normal")
    if nrm is None:
        raise RuntimeError("no normal image")
    npx, nw, nh = image_rgba(nrm)
    if (nw, nh) != (w, h):
        raise RuntimeError(f"normal size {nw}x{nh} != albedo {w}x{h}")

    work = npx.copy()
    for _ in range(48):
        blur = (
            np.roll(work, 1, 0)
            + np.roll(work, -1, 0)
            + np.roll(work, 1, 1)
            + np.roll(work, -1, 1)
        ) * 0.25
        work[dil] = blur[dil]

    # keep as a normal map: xy around 0.5, z high
    png = os.path.join(os.path.dirname(CURRENT), "normal_nodecal.png")
    flat = np.ascontiguousarray(work.reshape(-1), dtype=np.float32)
    nrm.pixels.foreach_set(flat)
    nrm.update()
    nrm.filepath_raw = png
    nrm.file_format = "PNG"
    nrm.save()
    nrm.pack()

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
    scene.render.filepath = RENDER
    scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)

    text = (
        f"red_texels={red_n} dilated={int(dil.sum())}\n"
        f"normal_png={png}\n"
        f"render={RENDER}\n"
    )
    with open(REPORT, "a") as f:
        f.write(text)
    print(text)


if __name__ == "__main__":
    main()
