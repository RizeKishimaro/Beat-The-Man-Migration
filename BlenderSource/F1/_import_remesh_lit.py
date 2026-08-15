"""
Ticket 130 — Wheels (https://trello.com/c/oBQ8790I).
Bring Meshy remesh wheel into the studio lighting, shrink hub 15%,
widen the rubber band (same radial remap as the high-poly pass).
"""
import bpy
import math
import os
import shutil
from mathutils import Vector

SRC = "/Users/sammy/Downloads/Meshy_AI_Fusion_XP_3000_Tire_0814103109_generate.blend"
SRC_COPY = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/Meshy_AI_Fusion_XP_3000_Tire_0814103109_generate.blend"
LIT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/Wheel_FusionXP_LowPoly.blend"
PREVIEW = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_remesh_lit_preview.png"
HUB_SCALE = 0.85
HUB_R_FRAC = 0.63  # rim lip ~63% of outer radius (from histogram)


def add_area(scene, name, loc, target, size, energy, color):
    light = bpy.data.lights.new(name=name, type="AREA")
    light.shape = "RECTANGLE"
    light.size = size[0]
    light.size_y = size[1]
    light.energy = energy
    light.color = color
    o = bpy.data.objects.new(name, light)
    scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (target - o.location).to_track_quat("-Z", "Y").to_euler()
    return o


def setup_lights(scene, target):
    world = bpy.data.worlds.get("World") or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Color"].default_value = (0.28, 0.30, 0.33, 1)
    bg.inputs["Strength"].default_value = 1.1
    out = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(bg.outputs["Background"], out.inputs["Surface"])

    cx, cy, cz = target
    t = Vector(target)
    add_area(scene, "Key", (cx + 1.6, cy - 2.2, cz + 1.8), t, (1.8, 1.3), 900, (1.0, 0.97, 0.92))
    add_area(scene, "Fill", (cx - 2.2, cy - 1.5, cz + 1.0), t, (2.2, 1.6), 280, (0.72, 0.80, 1.0))
    add_area(scene, "Rim", (cx - 0.1, cy + 2.4, cz + 1.3), t, (1.5, 1.0), 560, (1.0, 0.95, 0.88))
    add_area(scene, "Top", (cx, cy - 0.4, cz + 2.4), t, (2.2, 1.0), 180, (1.0, 1.0, 1.0))
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 2.4
    sun.angle = math.radians(25)
    so = bpy.data.objects.new("Sun", sun)
    scene.collection.objects.link(so)
    so.location = (cx + 3, cy - 3, cz + 4.5)
    so.rotation_euler = (t - so.location).to_track_quat("-Z", "Y").to_euler()

    cam_data = bpy.data.cameras.new("ViewCam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("ViewCam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (cx + 1.6, cy - 2.1, cz + 0.85)
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam


def simple_mat(name, base, metallic, roughness):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*base, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


def main():
    shutil.copy2(SRC, SRC_COPY)
    bpy.ops.wm.open_mainfile(filepath=SRC)

    obj = bpy.data.objects["mesh_node"]
    obj.name = "Wheel_FusionXP"
    mw = obj.matrix_world
    imw = mw.inverted()
    n = len(obj.data.vertices)
    worlds = [mw @ v.co for v in obj.data.vertices]
    cx = sum(p.x for p in worlds) / n
    cy = sum(p.y for p in worlds) / n
    cz = sum(p.z for p in worlds) / n
    rs = [math.hypot(p.x - cx, p.z - cz) for p in worlds]
    r_max = max(rs)
    hub_r = r_max * HUB_R_FRAC
    hub_new = hub_r * HUB_SCALE

    # Origin at axle center
    from mathutils import Matrix

    local = imw @ Vector((cx, cy, cz))
    obj.data.transform(Matrix.Translation(-local))
    obj.location = (0, 0, 0)
    obj.rotation_euler = (0, 0, 0)
    obj.scale = (1, 1, 1)

    # Recompute after origin
    worlds = [obj.matrix_world @ v.co for v in obj.data.vertices]
    rs = [math.hypot(p.x, p.z) for p in worlds]

    for i, p in enumerate(worlds):
        r = rs[i]
        if r < 1e-8:
            continue
        if r <= hub_r:
            new_r = r * HUB_SCALE
        else:
            t = (r - hub_r) / (r_max - hub_r)
            new_r = hub_new + t * (r_max - hub_new)
        s = new_r / r
        p.x *= s
        p.z *= s
        obj.data.vertices[i].co = obj.matrix_world.inverted() @ p
    obj.data.update()

    rubber = simple_mat("Wheel_Rubber", (0.02, 0.02, 0.022), 0.0, 0.62)
    metal = simple_mat("Wheel_Rim", (0.72, 0.74, 0.76), 1.0, 0.28)
    obj.data.materials.clear()
    obj.data.materials.append(rubber)
    obj.data.materials.append(metal)
    hub_f = tire_f = 0
    for poly in obj.data.polygons:
        c = poly.center
        r = math.hypot(c.x, c.z)
        if r < hub_new + 0.02:
            poly.material_index = 1
            hub_f += 1
        else:
            poly.material_index = 0
            tire_f += 1

    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_auto_smooth(angle=math.radians(35))

    # Drop any leftover studio from the Meshy file, keep only the wheel
    for o in list(bpy.data.objects):
        if o != obj:
            bpy.data.objects.remove(o, do_unlink=True)

    scene = bpy.context.scene
    setup_lights(scene, (0.0, 0.0, 0.0))

    for ws in bpy.data.workspaces:
        for sc in ws.screens:
            for area in sc.areas:
                if area.type == "VIEW_3D":
                    for sp in area.spaces:
                        if sp.type == "VIEW_3D":
                            sp.shading.type = "RENDERED"
                            sp.shading.use_scene_lights = True
                            sp.shading.use_scene_world = True

    try:
        scene.render.engine = "BLENDER_EEVEE"
    except Exception:
        pass
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.filepath = PREVIEW
    scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)

    bpy.ops.wm.save_as_mainfile(filepath=LIT)
    print(
        f"v={len(obj.data.vertices)} f={len(obj.data.polygons)} "
        f"hub_r {hub_r:.4f}->{hub_new:.4f} r_max={r_max:.4f} "
        f"hub_faces={hub_f} tire_faces={tire_f} saved={LIT}"
    )


if __name__ == "__main__":
    main()
