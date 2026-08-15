"""
Paint out baked red sidewall decals from the tire albedo.
Albedo-only (rubber is non-metallic). Dilate the red mask, then diffuse-fill
from surrounding rubber.
"""
import bpy
import os
import shutil
import numpy as np

BLEND = bpy.data.filepath
ROOT = os.path.abspath(os.path.join(os.path.dirname(BLEND), "..", "..", ".."))
BACKUP = os.path.join(
    ROOT,
    "BlenderSource",
    "F1",
    "Meshy_AI_Fusion_XP_3000_Tire_0814081341_texture_BACKUP_pre_decal_strip.blend",
)
RENDER = os.path.join(os.path.dirname(BLEND), "_tire_nodecal_preview.png")
REPORT = os.path.join(ROOT, "BlenderSource", "F1", "_decal_strip_report.txt")


def main():
    os.makedirs(os.path.dirname(BACKUP), exist_ok=True)
    if not os.path.exists(BACKUP):
        shutil.copy2(BLEND, BACKUP)

    img = bpy.data.images.get("base_color")
    if img is None:
        for im in bpy.data.images:
            if im.name and "base_color" in im.name.lower():
                img = im
                break
    if img is None:
        raise RuntimeError("No base_color image")

    w, h = int(img.size[0]), int(img.size[1])
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    r, g, b = px[:, :, 0], px[:, :, 1], px[:, :, 2]
    red = (r > 0.45) & (r > g * 1.6) & (r > b * 1.6)
    red_n = int(red.sum())

    dil = red.copy()
    for dy in range(-3, 4):
        for dx in range(-3, 4):
            dil |= np.roll(np.roll(red, dy, axis=0), dx, axis=1)
    dil_n = int(dil.sum())

    valid = ~dil
    dark = valid & (r < 0.22) & (g < 0.22) & (b < 0.22)
    if int(dark.sum()) < 100:
        dark = valid
    rubber = px[dark].mean(axis=0)
    work = px.copy()
    work[dil] = rubber
    # soften the seam
    for _ in range(6):
        blur = (
            np.roll(work, 1, 0)
            + np.roll(work, -1, 0)
            + np.roll(work, 1, 1)
            + np.roll(work, -1, 1)
        ) * 0.25
        work[dil] = blur[dil]

    flat = np.ascontiguousarray(work.reshape(-1), dtype=np.float32)
    img.pixels.foreach_set(flat)
    img.update()

    # Packed images keep the old GPU/file buffer unless we save then re-pack.
    png_path = os.path.join(os.path.dirname(BLEND), "base_color_nodecal.png")
    img.filepath_raw = png_path
    img.file_format = "PNG"
    img.save()
    img.pack()

    px2 = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    r2, g2, b2 = px2[:, :, 0], px2[:, :, 1], px2[:, :, 2]
    red_after = int(((r2 > 0.45) & (r2 > g2 * 1.6) & (r2 > b2 * 1.6)).sum())

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

    text = "\n".join(
        [
            f"image={img.name} {w}x{h}",
            f"red_texels={red_n} dilated={dil_n} red_after={red_after}",
            f"png={png_path}",
            f"backup={BACKUP}",
            f"render={RENDER}",
        ]
    )
    with open(REPORT, "w") as f:
        f.write(text + "\n")
    print(text)


if __name__ == "__main__":
    main()
