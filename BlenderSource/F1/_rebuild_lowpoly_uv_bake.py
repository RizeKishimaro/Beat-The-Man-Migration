"""
Rebuild game wheel shading: keep 3k geometry, ignore Meshy UVs,
cylinder-unwrap tire + smart-unwrap hub, bake normal/AO from HIGH,
simple rubber + metal materials.
"""
import bpy
import bmesh
import math
import os
from mathutils import Vector, Euler

GAME = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/Wheel_FusionXP_Game.blend"
OUT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/Wheel_FusionXP_LowPoly.blend"
PREVIEW = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_lowpoly_rebake_preview.png"
REPORT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_lowpoly_rebake_report.txt"
TEX_DIR = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1"
HUB_R = 0.54
BAKE_RES = 2048


def face_radius(f):
    c = f.calc_center_median()
    return math.hypot(c.x, c.z)  # axle = Y


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


def setup_world_and_lights(scene, target):
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
    add_area(scene, "Key", (cx + 2.2, cy - 3.0, cz + 2.4), t, (2.4, 1.8), 1400, (1.0, 0.97, 0.92))
    add_area(scene, "Fill", (cx - 3.0, cy - 2.0, cz + 1.4), t, (3.0, 2.2), 420, (0.72, 0.80, 1.0))
    add_area(scene, "Rim", (cx - 0.2, cy + 3.2, cz + 1.8), t, (2.0, 1.4), 900, (1.0, 0.95, 0.88))
    add_area(scene, "Top", (cx, cy - 0.6, cz + 3.4), t, (3.2, 1.4), 280, (1.0, 1.0, 1.0))
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 2.4
    sun.angle = math.radians(25)
    so = bpy.data.objects.new("Sun", sun)
    scene.collection.objects.link(so)
    so.location = (cx + 4, cy - 4, cz + 6)
    so.rotation_euler = (t - so.location).to_track_quat("-Z", "Y").to_euler()

    cam_data = bpy.data.cameras.new("ViewCam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("ViewCam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (cx + 2.8, cy - 3.6, cz + 1.35)
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam


def make_image(name, data=True):
    img = bpy.data.images.get(name)
    if img:
        bpy.data.images.remove(img)
    img = bpy.data.images.new(name, BAKE_RES, BAKE_RES, alpha=False, float_buffer=False, is_data=data)
    img.colorspace_settings.name = "Non-Color" if data else "sRGB"
    return img


def principled_with_maps(name, base, metallic, roughness, nrm_img, ao_img):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (700, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (350, 0)
    bsdf.inputs["Base Color"].default_value = (*base, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    tex_n = nt.nodes.new("ShaderNodeTexImage")
    tex_n.image = nrm_img
    tex_n.location = (-400, 200)
    nrm = nt.nodes.new("ShaderNodeNormalMap")
    nrm.location = (-80, 200)
    nrm.inputs["Strength"].default_value = 1.0
    tex_ao = nt.nodes.new("ShaderNodeTexImage")
    tex_ao.image = ao_img
    tex_ao.location = (-400, -80)
    mix = nt.nodes.new("ShaderNodeMixRGB") if "ShaderNodeMixRGB" in dir(bpy.types) else nt.nodes.new("ShaderNodeMix")
    mix.location = (80, -40)
    # Blender 4+/5 Mix node
    if mix.type == "MIX":
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 0.85
        mix.inputs["A"].default_value = (*base, 1)
        nt.links.new(tex_ao.outputs["Color"], mix.inputs["B"])
        nt.links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])
    else:
        mix.blend_type = "MULTIPLY"
        mix.inputs["Fac"].default_value = 0.85
        mix.inputs["Color1"].default_value = (*base, 1)
        nt.links.new(tex_ao.outputs["Color"], mix.inputs["Color2"])
        nt.links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex_n.outputs["Color"], nrm.inputs["Color"])
    nt.links.new(nrm.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def ensure_bake_node(mat, img):
    nt = mat.node_tree
    node = nt.nodes.new("ShaderNodeTexImage")
    node.image = img
    node.select = True
    nt.nodes.active = node
    return node


def bake(low, high, img, bake_type, samples, cage):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    try:
        scene.cycles.device = "CPU"
    except Exception:
        pass

    for mat in low.data.materials:
        if mat:
            ensure_bake_node(mat, img)

    bpy.ops.object.select_all(action="DESELECT")
    high.hide_set(False)
    high.hide_render = False
    high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.bake(
        type=bake_type,
        use_selected_to_active=True,
        cage_extrusion=cage,
        max_ray_distance=0.35,
        margin=4,
        normal_space="TANGENT",
    )


def main():
    bpy.ops.wm.open_mainfile(filepath=GAME)
    low = bpy.data.objects["Wheel_FusionXP"]
    high = bpy.data.objects["Wheel_FusionXP_HIGH"]
    scene = bpy.context.scene

    # Drop leftover Meshy materials on the low mesh
    low.data.materials.clear()

    bpy.context.view_layer.objects.active = low
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bm = bmesh.from_edit_mesh(low.data)
    bm.faces.ensure_lookup_table()
    hub_n = tire_n = 0
    for f in bm.faces:
        if face_radius(f) < HUB_R:
            f.material_index = 1
            hub_n += 1
        else:
            f.material_index = 0
            tire_n += 1
    bmesh.update_edit_mesh(low.data)

    # Fresh UV
    if low.data.uv_layers:
        while low.data.uv_layers:
            low.data.uv_layers.remove(low.data.uv_layers[0])
    low.data.uv_layers.new(name="UVMap")

    # Tire: cylinder around Y
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.mesh.select_mode(type="FACE")
    bm = bmesh.from_edit_mesh(low.data)
    for f in bm.faces:
        f.select = face_radius(f) >= HUB_R
    bmesh.update_edit_mesh(low.data)
    bpy.ops.uv.cylinder_project(
        direction="ALIGN_TO_OBJECT",
        align="POLAR_ZX",
        correct_aspect=True,
        clip_to_bounds=False,
        scale_to_bounds=True,
    )

    # Hub: smart project
    bpy.ops.mesh.select_all(action="DESELECT")
    bm = bmesh.from_edit_mesh(low.data)
    for f in bm.faces:
        f.select = face_radius(f) < HUB_R
    bmesh.update_edit_mesh(low.data)
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02, scale_to_bounds=True)

    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(margin=0.008)
    bpy.ops.object.mode_set(mode="OBJECT")

    nrm_img = make_image("Wheel_Normal", data=True)
    ao_img = make_image("Wheel_AO", data=True)

    # Placeholder mats so bake has a target image node
    rubber = bpy.data.materials.new("Rubber_tmp")
    rubber.use_nodes = True
    metal = bpy.data.materials.new("Metal_tmp")
    metal.use_nodes = True
    low.data.materials.append(rubber)
    low.data.materials.append(metal)

    bake(low, high, nrm_img, "NORMAL", 16, 0.045)
    bake(low, high, ao_img, "AO", 32, 0.045)

    nrm_path = os.path.join(TEX_DIR, "Wheel_FusionXP_normal.png")
    ao_path = os.path.join(TEX_DIR, "Wheel_FusionXP_ao.png")
    nrm_img.filepath_raw = nrm_path
    nrm_img.file_format = "PNG"
    nrm_img.save()
    nrm_img.pack()
    ao_img.filepath_raw = ao_path
    ao_img.file_format = "PNG"
    ao_img.save()
    ao_img.pack()

    # Real materials
    low.data.materials.clear()
    rubber_m = principled_with_maps("Wheel_Rubber", (0.025, 0.025, 0.027), 0.0, 0.58, nrm_img, ao_img)
    metal_m = principled_with_maps("Wheel_Rim", (0.62, 0.64, 0.66), 1.0, 0.32, nrm_img, ao_img)
    low.data.materials.append(rubber_m)
    low.data.materials.append(metal_m)

    # Remove high + old studio so the file is only the game wheel
    for o in list(bpy.data.objects):
        if o.name != "Wheel_FusionXP":
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.outliner.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)

    pts = [low.matrix_world @ v.co for v in low.data.vertices]
    cx = sum(p.x for p in pts) / len(pts)
    cy = sum(p.y for p in pts) / len(pts)
    cz = sum(p.z for p in pts) / len(pts)
    setup_world_and_lights(scene, (cx, cy, cz))

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

    bpy.ops.wm.save_as_mainfile(filepath=OUT)
    text = "\n".join(
        [
            f"hub_faces={hub_n} tire_faces={tire_n}",
            f"low v={len(low.data.vertices)} f={len(low.data.polygons)}",
            f"normal={nrm_path}",
            f"ao={ao_path}",
            f"out={OUT}",
            f"preview={PREVIEW}",
        ]
    )
    with open(REPORT, "w") as f:
        f.write(text + "\n")
    print(text)


if __name__ == "__main__":
    main()
