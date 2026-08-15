"""
Ticket 130 — Wheels (https://trello.com/c/oBQ8790I).
Build a game-ready Fusion XP wheel from the high-poly Meshy sculpt.

- Copy is processed; high-poly source is not overwritten
- Collapse decimate, UVs kept, hub weighted so spokes survive
- Origin at axle center, studio stripped from export
- FBX uses the repo's -Z forward / Y up settings
"""
import bpy
import math
import os
import shutil

HIGHPOLY = "/Users/sammy/Documents/GitHub/BeatTheMan/Assets/Objects/F1 Car/Meshy_AI_Fusion_XP_3000_Tire_0814081341_texture.blend"
GAME_BLEND = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/Wheel_FusionXP_Game.blend"
OUT_DIR = "/Users/sammy/Documents/GitHub/BeatTheMan/Assets/Art/Vehicles/Wheel_FusionXP"
FBX_PATH = os.path.join(OUT_DIR, "Wheel_FusionXP.fbx")
REPORT = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_wheel_game_report.txt"
RENDER = "/Users/sammy/Documents/GitHub/BeatTheMan/BlenderSource/F1/_wheel_game_preview.png"
TARGET_FACES = 2800
TARGET_TOL = 600


def eval_faces(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    me = ev.to_mesh()
    n = len(me.polygons)
    ev.to_mesh_clear()
    return n


def world_center_and_axis(obj):
    mw = obj.matrix_world
    pts = [mw @ v.co for v in obj.data.vertices]
    n = len(pts)
    cx = sum(p.x for p in pts) / n
    cy = sum(p.y for p in pts) / n
    cz = sum(p.z for p in pts) / n
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    spans = [(max(xs) - min(xs), "x"), (max(ys) - min(ys), "y"), (max(zs) - min(zs), "z")]
    axis = min(spans)[1]
    return cx, cy, cz, axis, spans


def paint_keep_group(obj, cx, cy, cz, axis):
    vg = obj.vertex_groups.new(name="keep_hub")
    mw = obj.matrix_world
    imw = mw.inverted()
    # hub after 15% shrink ~0.50; tire to ~0.96
    for v in obj.data.vertices:
        p = mw @ v.co
        if axis == "y":
            r = math.hypot(p.x - cx, p.z - cz)
        elif axis == "x":
            r = math.hypot(p.y - cy, p.z - cz)
        else:
            r = math.hypot(p.x - cx, p.y - cy)
        if r < 0.50:
            w = 1.0
        elif r < 0.62:
            w = 0.55
        elif r < 0.80:
            w = 0.22
        else:
            w = 0.08
        vg.add([v.index], w, "REPLACE")
    return vg


def set_origin(obj, cx, cy, cz):
    from mathutils import Vector, Matrix

    mw = obj.matrix_world
    cursor = Vector((cx, cy, cz))
    local = mw.inverted() @ cursor
    obj.data.transform(Matrix.Translation(-local))
    obj.matrix_world.translation = cursor
    # snap object to world origin so the axle is (0,0,0)
    obj.location = (0.0, 0.0, 0.0)


def extract_textures(out_dir):
    saved = []
    for img in bpy.data.images:
        if not img.size[0]:
            continue
        if img.name in ("Render Result", "Viewer Node"):
            continue
        safe = "".join(c if c.isalnum() or c in "-_" else "_" for c in img.name)
        path = os.path.join(out_dir, safe + ".png")
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        saved.append(path)
    return saved


def render_preview(path):
    scene = bpy.context.scene
    cam = bpy.data.objects.get("Studio_Cam")
    if cam:
        scene.camera = cam
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except Exception:
        pass
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.filepath = path
    scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(GAME_BLEND), exist_ok=True)
    if os.path.abspath(bpy.data.filepath) != os.path.abspath(HIGHPOLY):
        bpy.ops.wm.open_mainfile(filepath=HIGHPOLY)

    src = bpy.data.objects.get("mesh_node")
    if src is None:
        src = next(o for o in bpy.data.objects if o.type == "MESH" and "Studio" not in o.name)

    src.name = "Wheel_FusionXP_HIGH"
    src.hide_render = True
    src.hide_set(True)

    game = src.copy()
    game.data = src.data.copy()
    game.name = "Wheel_FusionXP"
    bpy.context.scene.collection.objects.link(game)
    game.hide_set(False)
    game.hide_render = False

    cx, cy, cz, axis, spans = world_center_and_axis(game)
    before_v = len(game.data.vertices)
    before_f = len(game.data.polygons)

    set_origin(game, cx, cy, cz)
    paint_keep_group(game, 0.0, 0.0, 0.0, axis)

    mod = game.modifiers.new(name="GameDecimate", type="DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.use_collapse_triangulate = True
    mod.vertex_group = "keep_hub"
    mod.vertex_group_factor = 0.82

    lo, hi = 0.0004, 0.02
    best_ratio = 0.002
    best_faces = None
    for _ in range(10):
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

    bpy.context.view_layer.objects.active = game
    game.select_set(True)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.shade_auto_smooth(angle=math.radians(32.0))

    after_v = len(game.data.vertices)
    after_f = len(game.data.polygons)

    # drop studio from the game object set used for export
    bpy.ops.object.select_all(action="DESELECT")
    game.select_set(True)
    bpy.context.view_layer.objects.active = game

    texs = extract_textures(OUT_DIR)

    bpy.ops.wm.save_as_mainfile(filepath=GAME_BLEND)

    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        bake_space_transform=True,
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        path_mode="COPY",
        embed_textures=False,
    )

    # unhide high for the preview comparison? render game only
    render_preview(RENDER)

    lines = [
        f"axis={axis} spans={spans}",
        f"before v={before_v} f={before_f}",
        f"decimate_ratio={best_ratio:.6f} eval_faces={best_faces}",
        f"after v={after_v} f={after_f}",
        f"blend={GAME_BLEND}",
        f"fbx={FBX_PATH}",
        f"textures={texs}",
        f"render={RENDER}",
    ]
    text = "\n".join(lines)
    with open(REPORT, "w") as f:
        f.write(text + "\n")
    print(text)


if __name__ == "__main__":
    main()
