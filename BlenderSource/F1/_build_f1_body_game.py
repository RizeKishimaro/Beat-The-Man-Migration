"""
Ticket 125 — game-res F1 body from the Aero Kart sculpt.

Does not overwrite the open Aero Kart blend.
Writes BlenderSource/F1/F1_Body_Game.blend only.
"""
import bpy
import math
import os

SRC = "/Users/sammy/Documents/GitHub/BeatTheMan/Assets/Objects/F1 Car/Meshy_AI_Aero_Kart_Prototype_0809085417_generate.blend"
OUT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/F1_Body_Game.blend"
PREVIEW = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_f1_body_game_preview.png"
REPORT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_f1_body_game_report.txt"
TARGET_FACES = 6000
TARGET_TOL = 1500


def eval_faces(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    me = ev.to_mesh()
    n = len(me.polygons)
    ev.to_mesh_clear()
    return n


def main():
    bpy.ops.wm.open_mainfile(filepath=SRC)
    src = bpy.data.objects.get("F1_Body")
    if src is None:
        raise RuntimeError("F1_Body not found")

    src.name = "F1_Body_HIGH"
    src.hide_render = True
    src.hide_set(True)

    game = src.copy()
    game.data = src.data.copy()
    game.name = "F1_Body_Game"
    bpy.context.scene.collection.objects.link(game)
    game.hide_set(False)
    game.hide_render = False

    bpy.ops.object.select_all(action="DESELECT")
    game.select_set(True)
    bpy.context.view_layer.objects.active = game
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    before_v = len(game.data.vertices)
    before_f = len(game.data.polygons)

    mod = game.modifiers.new(name="GameDecimate", type="DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.use_collapse_triangulate = True

    lo, hi = 0.002, 0.08
    best_ratio, best_faces = 0.01, None
    for _ in range(12):
        mid = (lo + hi) * 0.5
        mod.ratio = mid
        faces = eval_faces(game)
        best_ratio, best_faces = mid, faces
        if abs(faces - TARGET_FACES) <= TARGET_TOL:
            break
        if faces > TARGET_FACES:
            hi = mid
        else:
            lo = mid

    bpy.ops.object.modifier_apply(modifier=mod.name)
    try:
        bpy.ops.object.shade_auto_smooth(angle=math.radians(40.0))
    except Exception:
        bpy.ops.object.shade_smooth()

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.002)
    bpy.ops.object.mode_set(mode="OBJECT")

    mat = bpy.data.materials.new("F1_Body_Game")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (0.12, 0.13, 0.15, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.15
    bsdf.inputs["Roughness"].default_value = 0.42
    game.data.materials.clear()
    game.data.materials.append(mat)

    keep = {"F1_Body_Game", "Studio_Camera", "Studio_Key", "Studio_Fill", "Studio_Top", "Studio_Bounce", "Studio_Cyclorama"}
    for o in list(bpy.data.objects):
        if o.name not in keep:
            bpy.data.objects.remove(o, do_unlink=True)

    after_v = len(game.data.vertices)
    after_f = len(game.data.polygons)

    scene = bpy.context.scene
    cam = bpy.data.objects.get("Studio_Camera")
    if cam:
        scene.camera = cam
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except Exception:
        scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 800
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)

    bpy.ops.wm.save_as_mainfile(filepath=OUT)
    text = "\n".join([
        f"src={SRC}",
        f"before v={before_v} f={before_f}",
        f"decimate_ratio={best_ratio:.6f} eval_faces={best_faces}",
        f"after v={after_v} f={after_f}",
        f"out={OUT}",
        f"preview={PREVIEW}",
    ])
    with open(REPORT, "w") as f:
        f.write(text + "\n")
    print(text)


main()
