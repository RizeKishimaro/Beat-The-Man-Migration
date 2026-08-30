"""
Build a chunky Love Heart tablet from the Figma heart + type SVGs and render a PNG.

Run:
  /Applications/Blender.app/Contents/MacOS/Blender --background --python \
    BlenderSource/LoveHeart/_build_love_heart.py
"""
from __future__ import annotations

import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from _svg_path import extract_fill_d, parse_path  # noqa: E402

HEART_SVG = os.path.join(HERE, "heart_asset.svg")
LOVE_SVG = os.path.join(HERE, "love.svg")
HOUSE_SVG = os.path.join(HERE, "house.svg")
FOR_THE_SVG = os.path.join(HERE, "for_the.svg")
OF_SVG = os.path.join(HERE, "of.svg")

BLEND_OUT = os.path.join(HERE, "LoveHeart.blend")
RENDER_OUT = os.path.join(HERE, "LoveHeart_render.png")
TRANSPARENT_OUT = os.path.join(HERE, "LoveHeart_render_transparent.png")
MASK_OUT = os.path.join(HERE, "LoveHeart_text_mask.png")
REPORT_OUT = os.path.join(HERE, "_love_heart_report.txt")

# Figma-relative offsets from LOVE's top-left (SVG y-down).
TEXT_OFFSETS = {
    "for_the": (42.676, -13.0),
    "love": (0.0, 0.0),
    "of": (56.676, 38.0),
    "house": (0.0, 50.0),
}
# LOVE top-left in heart SVG space (y-down). Tuned to sit in the heart body.
LOVE_ORIGIN = (49.88, 78.0)

HEART_WIDTH = 2.0
CURVE_EXTRUDE = 0.20
CURVE_BEVEL = 0.115
RIM_WIDTH = 0.155
BASIN_DEPTH = 0.04
DOME_HEIGHT = 0.07
TEXT_CUT_DEPTH = 0.07
ENGRAVE_BEVEL = 0.009
CREAM_SCALE = 0.76
CREAM_EXTRUDE = 0.016
CREAM_BEVEL = 0.007

PINK = (0.82, 0.42, 0.50, 1.0)
CREAM = (0.95, 0.90, 0.82, 1.0)


def set_sock(node, names, value):
    for n in names:
        sock = node.inputs.get(n)
        if sock is not None:
            sock.default_value = value
            return True
    return False


def make_chalk_mat(name, color):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nodes = nt.nodes
    links = nt.links
    nodes.clear()

    out = nodes.new("ShaderNodeOutputMaterial")
    out.location = (700, 0)
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (380, 0)
    texcoord = nodes.new("ShaderNodeTexCoord")
    texcoord.location = (-560, 40)
    mapping = nodes.new("ShaderNodeMapping")
    mapping.location = (-360, 40)
    noise = nodes.new("ShaderNodeTexNoise")
    noise.location = (-160, 80)
    noise.inputs["Scale"].default_value = 95.0
    noise.inputs["Detail"].default_value = 11.0
    noise.inputs["Roughness"].default_value = 0.62
    if "Distortion" in noise.inputs:
        noise.inputs["Distortion"].default_value = 0.35
    noise2 = nodes.new("ShaderNodeTexNoise")
    noise2.location = (-160, -160)
    noise2.inputs["Scale"].default_value = 28.0
    noise2.inputs["Detail"].default_value = 6.0
    noise2.inputs["Roughness"].default_value = 0.55
    mixn = nodes.new("ShaderNodeMix")
    mixn.location = (40, -40)
    if "Data Type" in mixn:
        mixn.data_type = "FLOAT"
    bump = nodes.new("ShaderNodeBump")
    bump.location = (200, -80)
    bump.inputs["Strength"].default_value = 0.18
    if "Distance" in bump.inputs:
        bump.inputs["Distance"].default_value = 0.015
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.location = (40, 200)
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (
        color[0] * 0.92,
        color[1] * 0.92,
        color[2] * 0.94,
        1.0,
    )
    ramp.color_ramp.elements[1].position = 0.72
    ramp.color_ramp.elements[1].color = (
        min(1.0, color[0] * 1.06),
        min(1.0, color[1] * 1.05),
        min(1.0, color[2] * 1.03),
        1.0,
    )

    set_sock(bsdf, ["Base Color"], color)
    set_sock(bsdf, ["Roughness"], 0.94)
    set_sock(bsdf, ["Metallic"], 0.0)
    set_sock(bsdf, ["Specular IOR Level", "Specular"], 0.12)
    set_sock(bsdf, ["IOR"], 1.32)
    set_sock(bsdf, ["Coat Weight", "Clearcoat"], 0.0)
    set_sock(bsdf, ["Sheen Weight", "Sheen"], 0.22)
    set_sock(bsdf, ["Sheen Roughness"], 0.85)
    set_sock(bsdf, ["Subsurface Weight", "Subsurface"], 0.08)
    set_sock(bsdf, ["Subsurface Radius"], (0.4, 0.2, 0.15))
    set_sock(bsdf, ["Subsurface Scale"], 0.04)

    links.new(texcoord.outputs["Object"], mapping.inputs["Vector"])
    links.new(mapping.outputs["Vector"], noise.inputs["Vector"])
    links.new(mapping.outputs["Vector"], noise2.inputs["Vector"])
    fac_out = noise.outputs.get("Fac") or noise.outputs[0]
    fac2 = noise2.outputs.get("Fac") or noise2.outputs[0]
    if "A" in mixn.inputs and mixn.inputs["A"].type == "VALUE":
        links.new(fac_out, mixn.inputs["A"])
        links.new(fac2, mixn.inputs["B"])
        mixn.inputs["Factor"].default_value = 0.35
        mixed = mixn.outputs.get("Result") or mixn.outputs[0]
    else:
        mixed = fac_out
    links.new(mixed, ramp.inputs["Fac"])
    links.new(mixed, bump.inputs["Height"])
    links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def contours_to_curve(name, contours, scale, flip_y=True):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "2D"
    cu.fill_mode = "BOTH"
    res = 24 if name == "Heart" else 10
    cu.resolution_u = res
    cu.render_resolution_u = res
    obj = bpy.data.objects.new(name, cu)
    bpy.context.scene.collection.objects.link(obj)

    for contour in contours:
        if len(contour) < 2:
            continue
        spl = cu.splines.new("BEZIER")
        spl.use_cyclic_u = True
        spl.resolution_u = res
        pts = spl.bezier_points
        pts.add(len(contour) - 1)
        for i, (x, y, hlx, hly, hrx, hry) in enumerate(contour):
            if flip_y:
                y, hly, hry = -y, -hly, -hry
            p = pts[i]
            p.co = (x * scale, y * scale, 0.0)
            p.handle_left_type = "FREE"
            p.handle_right_type = "FREE"
            p.handle_left = (hlx * scale, hly * scale, 0.0)
            p.handle_right = (hrx * scale, hry * scale, 0.0)
            p.handle_left_type = "FREE"
            p.handle_right_type = "FREE"
    return obj


def curve_bounds(obj):
    xs, ys = [], []
    for spl in obj.data.splines:
        for p in spl.bezier_points:
            xs.append(p.co.x)
            ys.append(p.co.y)
    return min(xs), max(xs), min(ys), max(ys)


def translate_curve(obj, dx, dy):
    for spl in obj.data.splines:
        for p in spl.bezier_points:
            p.co.x += dx
            p.co.y += dy
            p.handle_left.x += dx
            p.handle_left.y += dy
            p.handle_right.x += dx
            p.handle_right.y += dy


def scale_curve_xy(obj, s):
    for spl in obj.data.splines:
        for p in spl.bezier_points:
            p.co.x *= s
            p.co.y *= s
            p.handle_left.x *= s
            p.handle_left.y *= s
            p.handle_right.x *= s
            p.handle_right.y *= s


def shade_smooth(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_smooth()
    try:
        bpy.ops.object.shade_auto_smooth(angle=math.radians(50))
    except TypeError:
        if hasattr(obj.data, "use_auto_smooth"):
            obj.data.use_auto_smooth = True
            obj.data.auto_smooth_angle = math.radians(50)


def consistent_normals(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def assign_mat(obj, mat):
    mesh = obj.data
    while len(mesh.materials):
        mesh.materials.pop(index=0)
    mesh.materials.append(mat)
    for p in mesh.polygons:
        p.material_index = 0
        p.use_smooth = True


def build_xy_uv(obj, cx, cy, span):
    mesh = obj.data
    uv = mesh.uv_layers.get("DebossUV") or mesh.uv_layers.new(name="DebossUV")
    mesh.uv_layers.active = uv
    half = span * 0.5
    for loop in mesh.loops:
        co = mesh.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = (
            (co.x - (cx - half)) / span,
            (co.y - (cy - half)) / span,
        )


def make_cream_deboss_mat(name, cream_color, pink_color, image):
    mat = make_chalk_mat(name, cream_color)
    nt = mat.node_tree
    nodes = nt.nodes
    links = nt.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    img = nodes.new("ShaderNodeTexImage")
    img.location = (-720, 320)
    img.image = image
    img.interpolation = "Cubic"
    img.extension = "CLIP"
    uvmap = nodes.new("ShaderNodeUVMap")
    uvmap.location = (-920, 320)
    uvmap.uv_map = "DebossUV"
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.location = (-460, 320)
    ramp.color_ramp.interpolation = "EASE"
    ramp.color_ramp.elements[0].position = 0.22
    ramp.color_ramp.elements[0].color = (0, 0, 0, 1)
    ramp.color_ramp.elements[1].position = 0.72
    ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    mix = nodes.new("ShaderNodeMixRGB")
    mix.location = (80, 240)
    mix.blend_type = "MIX"
    bump = next((n for n in nodes if n.type == "BUMP"), None)
    links.new(uvmap.outputs["UV"], img.inputs["Vector"])
    links.new(img.outputs["Color"], ramp.inputs["Fac"])
    chalk_color_in = None
    for ln in list(links):
        if ln.to_socket == bsdf.inputs["Base Color"]:
            chalk_color_in = ln.from_socket
            links.remove(ln)
            break
    if chalk_color_in is not None:
        links.new(chalk_color_in, mix.inputs["Color1"])
    else:
        mix.inputs["Color1"].default_value = cream_color
    mix.inputs["Color2"].default_value = pink_color
    links.new(ramp.outputs["Color"], mix.inputs["Fac"])
    links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    if bump is not None:
        combine = nodes.new("ShaderNodeMath")
        combine.location = (40, -200)
        combine.operation = "SUBTRACT"
        combine.inputs[0].default_value = 0.0
        links.new(ramp.outputs["Color"], combine.inputs[1])
        if bump.inputs["Height"].is_linked:
            prev = bump.inputs["Height"].links[0].from_socket
            add = nodes.new("ShaderNodeMath")
            add.location = (200, -120)
            add.operation = "ADD"
            links.new(prev, add.inputs[0])
            links.new(combine.outputs["Value"], add.inputs[1])
            for ln in list(bump.inputs["Height"].links):
                links.remove(ln)
            links.new(add.outputs["Value"], bump.inputs["Height"])
        else:
            links.new(combine.outputs["Value"], bump.inputs["Height"])
        bump.inputs["Strength"].default_value = 0.5
    return mat


def render_text_mask(text_meshes, bounds):
    xmin, xmax, ymin, ymax, zmin, zmax = bounds
    cx = 0.5 * (xmin + xmax)
    cy = 0.5 * (ymin + ymax)
    span = max(xmax - xmin, ymax - ymin)

    emit = bpy.data.materials.new("MaskEmit")
    emit.use_nodes = True
    nt = emit.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (1, 1, 1, 1)
    em.inputs["Strength"].default_value = 8.0
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    for obj in text_meshes:
        assign_mat(obj, emit)
        obj.hide_render = False
        obj.hide_set(False)

    hide = [o for o in bpy.data.objects if o not in text_meshes]
    hidden_state = [(o, o.hide_render) for o in hide]
    for o in hide:
        o.hide_render = True

    cam_data = bpy.data.cameras.new("MaskCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = span
    cam = bpy.data.objects.new("MaskCam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = (cx, cy, zmax + 4.0)
    cam.rotation_euler = (0.0, 0.0, 0.0)

    scene = bpy.context.scene
    old_cam = scene.camera
    old_samples = scene.cycles.samples
    old_x = scene.render.resolution_x
    old_y = scene.render.resolution_y
    old_path = scene.render.filepath
    old_film = scene.render.film_transparent
    old_denoise = scene.cycles.use_denoising
    scene.camera = cam
    scene.cycles.samples = 24
    scene.cycles.use_denoising = False
    scene.render.resolution_x = 2048
    scene.render.resolution_y = 2048
    scene.render.film_transparent = False
    scene.render.filepath = MASK_OUT
    bg = scene.world.node_tree.nodes.get("Background") if scene.world and scene.world.use_nodes else None
    old_col = tuple(bg.inputs[0].default_value) if bg else None
    old_str = bg.inputs[1].default_value if bg else None
    if bg is not None:
        bg.inputs[0].default_value = (0, 0, 0, 1)
        bg.inputs[1].default_value = 0.0

    bpy.ops.render.render(write_still=True)

    scene.camera = old_cam
    scene.cycles.samples = old_samples
    scene.render.resolution_x = old_x
    scene.render.resolution_y = old_y
    scene.render.filepath = old_path
    scene.render.film_transparent = old_film
    scene.cycles.use_denoising = old_denoise
    if bg is not None and old_col is not None:
        bg.inputs[0].default_value = old_col
        bg.inputs[1].default_value = old_str
    for o, h in hidden_state:
        o.hide_render = h
    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.cameras.remove(cam_data)

    loaded = bpy.data.images.load(MASK_OUT)
    loaded.pack()
    return loaded, cx, cy, span


def convert_mesh(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target="MESH")
    return bpy.context.view_layer.objects.active


def object_bounds(obj):
    coords = [obj.matrix_world @ Vector(v.co) for v in obj.data.vertices]
    xs = [c.x for c in coords]
    ys = [c.y for c in coords]
    zs = [c.z for c in coords]
    return (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))


def apply_object(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def load_svg_contours(path):
    d = extract_fill_d(open(path, "r", encoding="utf-8").read())
    return parse_path(d)


def basin_and_materials(obj, pink, cream):
    mesh = obj.data
    while mesh.materials:
        mesh.materials.pop(index=0)
    mesh.materials.append(pink)
    mesh.materials.append(cream)

    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    bm.verts.ensure_lookup_table()

    zs = [v.co.z for v in bm.verts]
    z_max = max(zs)
    z_min = min(zs)
    z_cut = z_min + 0.72 * (z_max - z_min)

    fronts = [
        f
        for f in bm.faces
        if f.normal.z > 0.75 and f.calc_center_median().z > z_cut
    ]
    if not fronts:
        fronts = [max(bm.faces, key=lambda f: f.calc_center_median().z)]

    # Keep the largest connected front island
    fronts.sort(key=lambda f: f.calc_area(), reverse=True)
    island = {fronts[0]}
    stack = [fronts[0]]
    front_set = set(fronts)
    while stack:
        f = stack.pop()
        for e in f.edges:
            for nf in e.link_faces:
                if nf in front_set and nf not in island:
                    island.add(nf)
                    stack.append(nf)
    fronts = list(island)

    bmesh.ops.inset_region(
        bm,
        faces=fronts,
        thickness=RIM_WIDTH,
        depth=0.0,
        use_boundary=True,
        use_even_offset=True,
        use_interpolate=True,
        use_relative_offset=False,
    )
    # After inset, original faces are the inner basin
    geom = bmesh.ops.extrude_face_region(bm, geom=fronts)
    inner_verts = [g for g in geom["geom"] if isinstance(g, bmesh.types.BMVert)]
    inner_faces = [g for g in geom["geom"] if isinstance(g, bmesh.types.BMFace)]
    bmesh.ops.translate(bm, verts=inner_verts, vec=(0.0, 0.0, -BASIN_DEPTH))

    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        f.material_index = 0
    for f in inner_faces:
        f.material_index = 1
        f.smooth = True
    for f in bm.faces:
        f.smooth = True

    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return len(inner_faces)


def apply_dome(obj, height):
    mesh = obj.data
    xs = [v.co.x for v in mesh.vertices]
    ys = [v.co.y for v in mesh.vertices]
    zs = [v.co.z for v in mesh.vertices]
    cx = 0.5 * (min(xs) + max(xs))
    cy = 0.5 * (min(ys) + max(ys))
    radius = 0.52 * max(max(xs) - min(xs), max(ys) - min(ys))
    z_mid = 0.5 * (min(zs) + max(zs))
    for v in mesh.vertices:
        if v.co.z < z_mid:
            continue
        r = math.hypot(v.co.x - cx, v.co.y - cy)
        t = max(0.0, 1.0 - (r / max(radius, 1e-6)) ** 2)
        v.co.z += height * t
    mesh.update()


def boolean_difference(target, cutter):
    bpy.context.view_layer.update()
    mod = target.modifiers.new("Deboss", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cutter
    solvers = []
    if hasattr(mod, "solver"):
        solvers = ["FLOAT", "MANIFOLD", "EXACT"]
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    last_err = None
    if solvers:
        for s in solvers:
            try:
                mod.solver = s
            except TypeError:
                continue
            try:
                bpy.ops.object.modifier_apply(modifier=mod.name)
                print("BOOLEAN_OK", s)
                return
            except Exception as exc:
                last_err = exc
                print("BOOLEAN_FAIL", s, exc)
                # modifier may have been consumed; recreate
                if "Deboss" not in target.modifiers:
                    mod = target.modifiers.new("Deboss", "BOOLEAN")
                    mod.operation = "DIFFERENCE"
                    mod.object = cutter
        raise RuntimeError(f"boolean failed: {last_err}")
    bpy.ops.object.modifier_apply(modifier=mod.name)


def setup_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for col in (bpy.data.objects, bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(col):
            col.remove(item)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    preview = os.environ.get("LOVEHEART_PREVIEW", "1") == "1"
    scene.cycles.samples = 48 if preview else 384
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1280 if preview else 2560
    scene.render.resolution_y = 1280 if preview else 2560
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "16"
    scene.render.film_transparent = False
    scene.render.filepath = RENDER_OUT
    if hasattr(scene.view_settings, "view_transform"):
        try:
            scene.view_settings.view_transform = "AgX"
        except TypeError:
            scene.view_settings.view_transform = "Filmic"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.15
    scene.view_settings.gamma = 1.0

    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "METAL"
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        scene.cycles.device = "GPU"
    except Exception as exc:
        print("GPU setup skipped:", exc)
        scene.cycles.device = "CPU"

    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.90, 0.88, 0.85, 1.0)
        bg.inputs[1].default_value = 0.55


def setup_camera_lights(target):
    xmin, xmax, ymin, ymax, zmin, zmax = object_bounds(target)
    cx = 0.5 * (xmin + xmax)
    cy = 0.5 * (ymin + ymax)
    cz = 0.5 * (zmin + zmax)
    span = max(xmax - xmin, ymax - ymin)

    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = span * 1.42
    cam_data.clip_start = 0.01
    cam_data.clip_end = 100
    cam = bpy.data.objects.new("Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = (cx, cy - span * 0.16, cz + span * 2.6)
    direction = Vector((cx, cy, cz + span * 0.02)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam

    key = bpy.data.lights.new("Key", "AREA")
    key.shape = "DISK"
    key.size = span * 3.2
    key.energy = 220
    key.color = (1.0, 0.97, 0.93)
    key_obj = bpy.data.objects.new("Key", key)
    bpy.context.scene.collection.objects.link(key_obj)
    key_obj.location = (cx - span * 1.35, cy + span * 1.1, cz + span * 2.4)
    kd = Vector((cx, cy, cz)) - key_obj.location
    key_obj.rotation_euler = kd.to_track_quat("-Z", "Y").to_euler()

    fill = bpy.data.lights.new("Fill", "AREA")
    fill.shape = "DISK"
    fill.size = span * 4.0
    fill.energy = 35
    fill.color = (0.95, 0.96, 1.0)
    fill_obj = bpy.data.objects.new("Fill", fill)
    bpy.context.scene.collection.objects.link(fill_obj)
    fill_obj.location = (cx + span * 1.6, cy - span * 0.8, cz + span * 1.4)
    fd = Vector((cx, cy, cz)) - fill_obj.location
    fill_obj.rotation_euler = fd.to_track_quat("-Z", "Y").to_euler()

    rim = bpy.data.lights.new("Rim", "AREA")
    rim.shape = "DISK"
    rim.size = span * 2.0
    rim.energy = 18
    rim.color = (1.0, 0.95, 0.92)
    rim_obj = bpy.data.objects.new("Rim", rim)
    bpy.context.scene.collection.objects.link(rim_obj)
    rim_obj.location = (cx + span * 0.2, cy + span * 1.8, cz + span * 0.6)
    rd = Vector((cx, cy, cz)) - rim_obj.location
    rim_obj.rotation_euler = rd.to_track_quat("-Z", "Y").to_euler()

    plane = bpy.data.meshes.new("Catcher")
    plane_obj = bpy.data.objects.new("Catcher", plane)
    bpy.context.scene.collection.objects.link(plane_obj)
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=span * 6.0)
    bm.to_mesh(plane)
    bm.free()
    plane_obj.location = (cx, cy, zmin - 0.0008)
    ground = bpy.data.materials.new("Ground")
    ground.use_nodes = True
    bsdf = ground.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        set_sock(bsdf, ["Base Color"], (0.90, 0.88, 0.85, 1.0))
        set_sock(bsdf, ["Roughness"], 0.95)
        set_sock(bsdf, ["Specular IOR Level", "Specular"], 0.05)
    plane.materials.append(ground)
    return cam


def main():
    setup_scene()
    pink = make_chalk_mat("LoveHeart_Pink", PINK)
    cream = make_chalk_mat("LoveHeart_Cream", CREAM)

    heart_contours = load_svg_contours(HEART_SVG)
    xs = [p[0] for c in heart_contours for p in c]
    ys = [p[1] for c in heart_contours for p in c]
    raw_w = max(xs) - min(xs)
    scale = HEART_WIDTH / raw_w
    print("HEART_RAW", {"w": raw_w, "h": max(ys) - min(ys), "scale": scale, "contours": len(heart_contours)})

    def centered_heart(name):
        obj = contours_to_curve(name, heart_contours, scale)
        xmin, xmax, ymin, ymax = curve_bounds(obj)
        translate_curve(obj, -(xmin + xmax) * 0.5, -(ymin + ymax) * 0.5)
        return obj

    heart = centered_heart("Heart")
    heart.data.extrude = CURVE_EXTRUDE
    heart.data.bevel_depth = CURVE_BEVEL
    heart.data.bevel_resolution = 10
    heart.data.offset = 0.018

    cream_crv = centered_heart("CreamFace")
    scale_curve_xy(cream_crv, CREAM_SCALE)
    cream_crv.data.extrude = CREAM_EXTRUDE
    cream_crv.data.bevel_depth = CREAM_BEVEL
    cream_crv.data.bevel_resolution = 6
    cream_crv.data.offset = 0.0

    hx0, hx1 = min(xs) * scale, max(xs) * scale
    hy0, hy1 = -max(ys) * scale, -min(ys) * scale
    heart_cx = 0.5 * (hx0 + hx1)
    heart_cy = 0.5 * (hy0 + hy1)

    def place_text(name, svg_path, origin_xy):
        contours = load_svg_contours(svg_path)
        obj = contours_to_curve(name, contours, scale)
        ox, oy = origin_xy
        dx = ox * scale - heart_cx
        dy = -oy * scale - heart_cy
        translate_curve(obj, dx, dy)
        return obj

    love_xy = LOVE_ORIGIN
    texts = [
        place_text("Text_for_the", FOR_THE_SVG, (love_xy[0] + TEXT_OFFSETS["for_the"][0], love_xy[1] + TEXT_OFFSETS["for_the"][1])),
        place_text("Text_LOVE", LOVE_SVG, (love_xy[0] + TEXT_OFFSETS["love"][0], love_xy[1] + TEXT_OFFSETS["love"][1])),
        place_text("Text_of", OF_SVG, (love_xy[0] + TEXT_OFFSETS["of"][0], love_xy[1] + TEXT_OFFSETS["of"][1])),
        place_text("Text_HOUSE", HOUSE_SVG, (love_xy[0] + TEXT_OFFSETS["house"][0], love_xy[1] + TEXT_OFFSETS["house"][1])),
    ]
    for t in texts:
        t.data.extrude = 0.002
        t.data.bevel_depth = 0.0
        t.data.offset = 0.0012

    heart_mesh = convert_mesh(heart)
    heart_mesh.name = "LoveHeart_Pink"
    apply_object(heart_mesh)
    assign_mat(heart_mesh, pink)

    cream_mesh = convert_mesh(cream_crv)
    cream_mesh.name = "LoveHeart_Cream"
    apply_object(cream_mesh)

    # Dense even mesh so the stamp can displace cleanly.
    rem = cream_mesh.modifiers.new("PlateRemesh", "REMESH")
    rem.mode = "VOXEL"
    rem.voxel_size = 0.01
    if hasattr(rem, "use_smooth_shade"):
        rem.use_smooth_shade = True
    bpy.context.view_layer.objects.active = cream_mesh
    bpy.ops.object.modifier_apply(modifier=rem.name)

    _, _, _, _, hzmin, hzmax = object_bounds(heart_mesh)
    _, _, _, _, czmin, czmax = object_bounds(cream_mesh)
    cream_mesh.location.z += (hzmax - 0.004) - czmin
    apply_object(cream_mesh)

    apply_dome(heart_mesh, DOME_HEIGHT)
    apply_dome(cream_mesh, DOME_HEIGHT)
    shade_smooth(heart_mesh)

    mask_meshes = []
    for t in texts:
        m = convert_mesh(t)
        apply_object(m)
        mask_meshes.append(m)
    bpy.ops.object.select_all(action="DESELECT")
    for m in mask_meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = mask_meshes[0]
    bpy.ops.object.join()
    mask_obj = bpy.context.view_layer.objects.active
    mask_obj.name = "TextMask"

    mask_img, mx, my, mspan = render_text_mask([mask_obj], object_bounds(cream_mesh))
    mask_obj.hide_set(True)
    mask_obj.hide_render = True

    build_xy_uv(cream_mesh, mx, my, mspan)
    cream_mat = make_cream_deboss_mat("LoveHeart_CreamDeboss", CREAM, PINK, mask_img)
    assign_mat(cream_mesh, cream_mat)

    tex = bpy.data.textures.new("DebossTex", "IMAGE")
    tex.image = mask_img
    disp = cream_mesh.modifiers.new("Deboss", "DISPLACE")
    disp.texture = tex
    disp.texture_coords = "UV"
    disp.uv_layer = "DebossUV"
    disp.direction = "Z"
    disp.mid_level = 0.0
    disp.strength = -0.048
    bpy.context.view_layer.objects.active = cream_mesh
    bpy.ops.object.modifier_apply(modifier=disp.name)
    shade_smooth(cream_mesh)

    # Sit the pair on z=0
    hxmin, hxmax, hymin, hymax, hzmin, hzmax = object_bounds(heart_mesh)
    heart_mesh.location.z -= hzmin
    cream_mesh.location.z -= hzmin
    apply_object(heart_mesh)
    apply_object(cream_mesh)

    cam = setup_camera_lights(heart_mesh)

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    bpy.ops.render.render(write_still=True)

    preview = os.environ.get("LOVEHEART_PREVIEW", "1") == "1"
    if not preview:
        ground = bpy.data.objects.get("Catcher")
        if ground is not None:
            ground.is_shadow_catcher = True
        scene = bpy.context.scene
        scene.render.film_transparent = True
        scene.render.filepath = TRANSPARENT_OUT
        bpy.ops.render.render(write_still=True)
        scene.render.filepath = RENDER_OUT
        scene.render.film_transparent = False

    xmin, xmax, ymin, ymax, zmin, zmax = object_bounds(heart_mesh)
    report = {
        "pink_verts": len(heart_mesh.data.vertices),
        "cream_verts": len(cream_mesh.data.vertices),
        "cream_faces": len(cream_mesh.data.polygons),
        "size": (
            round(xmax - xmin, 4),
            round(ymax - ymin, 4),
            round(zmax - zmin, 4),
        ),
        "render": RENDER_OUT,
        "transparent": TRANSPARENT_OUT,
        "blend": BLEND_OUT,
        "camera": tuple(round(x, 4) for x in cam.location),
    }
    with open(REPORT_OUT, "w", encoding="utf-8") as f:
        f.write(repr(report) + "\n")
    print("LOVEHEART_DONE", report)


if __name__ == "__main__":
    main()
