"""
Ticket 130 — add shrinkwrapped sidewall decal rings to ChromeDrift.

Original demo: SPEED FREAKS strip on a thin sidewall ring.
Does NOT overwrite Wheel_ChromeDrift.blend (file may be open).
Writes a preview blend + render instead.
"""
import bpy
import bmesh
import math
import os
from mathutils import Vector, Matrix

SRC = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/Wheel_ChromeDrift.blend"
OUT_BLEND = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_decal_preview.blend"
PNG = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/Decals/Soft Tyre.png"
RENDER = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_decal_preview.png"
SIDE_RENDER = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_decal_side.png"
SEAM_RENDER = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_decal_seam.png"
REAR_RENDER = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_chromedrift_decal_rear.png"

# Sidewall band from inspect: faces r 0.198–0.338 mean 0.274, |x| ~ 0.145
R_INNER = 0.238
R_OUTER = 0.312
SEGS = 96
RINGS = 6
OFFSET = 0.0022


def annulus(name, x_sign):
    """Flat ring in YZ, sitting just outside the +X or -X sidewall."""
    bm = bmesh.new()
    x = x_sign * (0.1498 + OFFSET)
    for i in range(RINGS):
        t = i / (RINGS - 1)
        r = R_INNER + t * (R_OUTER - R_INNER)
        for j in range(SEGS):
            a = 2 * math.pi * j / SEGS
            bm.verts.new((x, r * math.cos(a), r * math.sin(a)))
    bm.verts.ensure_lookup_table()
    for i in range(RINGS - 1):
        for j in range(SEGS):
            j2 = (j + 1) % SEGS
            v00 = bm.verts[i * SEGS + j]
            v10 = bm.verts[(i + 1) * SEGS + j]
            v11 = bm.verts[(i + 1) * SEGS + j2]
            v01 = bm.verts[i * SEGS + j2]
            if x_sign > 0:
                bm.faces.new((v00, v01, v11, v10))
            else:
                bm.faces.new((v00, v10, v11, v01))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()

    # UV: U = angle, V = radial.
    # Faces that cross the atan2 wrap (0/1) must not interpolate across the
    # whole strip — that is the squished-letter seam. Bump the low U by +1
    # so the closing quad is a short span and REPEAT wraps cleanly.
    me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    for p in me.polygons:
        loops = list(p.loop_indices)
        us, vs = [], []
        for li in loops:
            co = me.vertices[me.loops[li].vertex_index].co
            u = (math.atan2(co.z, co.y) / (2 * math.pi)) % 1.0
            r = math.hypot(co.y, co.z)
            v = (r - R_INNER) / (R_OUTER - R_INNER)
            us.append(u)
            vs.append(v)
        if max(us) - min(us) > 0.5:
            us = [u + 1.0 if u < 0.5 else u for u in us]
        for li, u, v in zip(loops, us, vs):
            uv[li].uv = (u, v)

    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


# Hub is the front octagon; bolt is the recessed inner disc.
BOLT_R = 0.038
HUB_R = 0.062


def _set_in(node, name, val):
    if name in node.inputs:
        node.inputs[name].default_value = val


def hub_white_mat():
    mat = bpy.data.materials.new("Wheel_HubWhiteChrome")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    # Satin white chrome: stays bright in studio light, with a chrome sheen.
    _set_in(bsdf, "Base Color", (0.90, 0.91, 0.94, 1.0))
    _set_in(bsdf, "Metallic", 0.42)
    _set_in(bsdf, "Roughness", 0.14)
    _set_in(bsdf, "Specular IOR Level", 0.6)
    _set_in(bsdf, "Coat Weight", 0.55)
    _set_in(bsdf, "Coat Roughness", 0.06)
    return mat


def _delete_faces_by_radius(me, keep_if):
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    drop = [f for f in bm.faces if not keep_if(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(me)
    bm.free()
    me.update()


def split_bolt(rim):
    """Peel the center-lock geo off the hub into its own object."""
    me = rim.data
    bolt_me = me.copy()
    bolt_me.name = "Wheel_ChromeDrift_Bolt"
    bolt = bpy.data.objects.new("Wheel_ChromeDrift_Bolt", bolt_me)
    bpy.context.scene.collection.objects.link(bolt)
    _delete_faces_by_radius(
        bolt_me, lambda c: math.hypot(c.y, c.z) < BOLT_R
    )
    _delete_faces_by_radius(
        me, lambda c: math.hypot(c.y, c.z) >= BOLT_R
    )
    gloss = bpy.data.materials.get("Wheel_GlossBlack")
    bolt_me.materials.clear()
    if gloss:
        bolt_me.materials.append(gloss)
    bolt.parent = rim

    white = hub_white_mat()
    me.materials.clear()
    me.materials.append(white)
    for p in me.polygons:
        p.material_index = 0
    return bolt


def decal_mat():
    mat = bpy.data.materials.new("Tire_Decal_SpeedFreaks")
    mat.use_nodes = True
    mat.blend_method = "BLEND"
    try:
        mat.shadow_method = "NONE"
    except Exception:
        pass
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    mix = nt.nodes.new("ShaderNodeMixShader")
    trans = nt.nodes.new("ShaderNodeBsdfTransparent")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    img = bpy.data.images.load(PNG)
    tex.image = img
    tex.interpolation = "Linear"
    tex.extension = "REPEAT"
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.45
    try:
        bsdf.inputs["Specular IOR Level"].default_value = 0.15
    except Exception:
        pass
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], mix.inputs["Fac"])
    nt.links.new(trans.outputs["BSDF"], mix.inputs[1])
    nt.links.new(bsdf.outputs["BSDF"], mix.inputs[2])
    nt.links.new(mix.outputs["Shader"], out.inputs["Surface"])
    return mat


def main():
    tire = bpy.data.objects.get("Wheel_ChromeDrift_Tire")
    if tire is None:
        raise RuntimeError("Wheel_ChromeDrift_Tire not found")
    rim = bpy.data.objects.get("Wheel_ChromeDrift_Rim")
    if rim is None:
        raise RuntimeError("Wheel_ChromeDrift_Rim not found")
    bolt = split_bolt(rim)

    mat = decal_mat()
    rings = []
    # Front sidewall only — no lettering on the rear.
    for sign, name in ((-1.0, "Decal_Sidewall_Out"),):
        obj = annulus(name, sign)
        obj.data.materials.append(mat)
        sw = obj.modifiers.new("SW", "SHRINKWRAP")
        sw.wrap_method = "PROJECT"
        sw.wrap_mode = "OUTSIDE_SURFACE"
        sw.target = tire
        sw.use_project_x = True
        sw.use_project_y = False
        sw.use_project_z = False
        sw.use_negative_direction = True
        sw.use_positive_direction = True
        sw.offset = 0.0016
        obj.parent = tire
        rings.append(obj)

    scene = bpy.context.scene
    cam = bpy.data.objects.get("Studio_Cam")
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except Exception:
        scene.render.engine = "BLENDER_EEVEE"
    eevee = getattr(scene, "eevee", None)
    if eevee:
        for attr, val in (("use_raytracing", True), ("use_ssr", True)):
            if hasattr(eevee, attr):
                setattr(eevee, attr, val)
    scene.render.film_transparent = False
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 1000
    if cam:
        scene.camera = cam
        scene.render.filepath = RENDER
        bpy.ops.render.render(write_still=True)
        cam.location = (-1.2, 0.0, 0.05)
        cam.rotation_euler = (math.radians(90), 0, math.radians(-90))
        scene.render.filepath = SIDE_RENDER
        bpy.ops.render.render(write_still=True)
        # UV wrap is on +Y; shoot that to confirm the pinch is gone.
        cam.location = (0.12, 1.05, 0.02)
        cam.rotation_euler = (math.radians(90), 0, math.radians(180))
        scene.render.filepath = SEAM_RENDER
        bpy.ops.render.render(write_still=True)
        cam.location = (1.2, 0.0, 0.05)
        cam.rotation_euler = (math.radians(90), 0, math.radians(90))
        scene.render.filepath = REAR_RENDER
        bpy.ops.render.render(write_still=True)

    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
    print("RINGS", [o.name for o in rings])
    print("BOLT", bolt.name, "verts", len(bolt.data.vertices))
    print("SAVED", OUT_BLEND)
    print("RENDER", RENDER)
    print("SIDE", SIDE_RENDER)
    print("SEAM", SEAM_RENDER)
    print("REAR", REAR_RENDER)


main()
