"""
Remove suspension bars / protrusions welded onto selected wheel meshes.

Usage (in the F1 kart .blend):
  1. Select the 2 wheel objects
  2. Scripting workspace → Open this file → Run Script
  OR: Blender → Scripting → paste & run

Strategy:
  - Guess wheel axis = thinnest bbox axis
  - Estimate tire radius from a mid-plane slice
  - If mesh has separate loose parts: keep the large wheel body, drop small outliers
  - If welded into one piece: delete verts outside the tire radius capsule
    (and hub-side axial spikes)

Does not touch non-selected objects. Saves the file when done.
"""
import bpy
import bmesh
import math
from mathutils import Vector


def _world_co(mw, v):
    return mw @ v.co


def clean_wheel(obj):
    report = [f"=== {obj.name} ==="]
    mw = obj.matrix_world

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()

    world = [_world_co(mw, v) for v in bm.verts]
    cx = sum(v.x for v in world) / len(world)
    cy = sum(v.y for v in world) / len(world)
    cz = sum(v.z for v in world) / len(world)

    xs = [v.x for v in world]
    ys = [v.y for v in world]
    zs = [v.z for v in world]
    spans = [
        (max(xs) - min(xs), "x"),
        (max(ys) - min(ys), "y"),
        (max(zs) - min(zs), "z"),
    ]
    spans.sort()
    axis = spans[0][1]
    report.append(f"axis={axis} spans={[(round(s,4), a) for s,a in spans]}")

    def radial(v):
        if axis == "x":
            return math.hypot(v.y - cy, v.z - cz)
        if axis == "y":
            return math.hypot(v.x - cx, v.z - cz)
        return math.hypot(v.x - cx, v.y - cy)

    def axial(v):
        if axis == "x":
            return v.x - cx
        if axis == "y":
            return v.y - cy
        return v.z - cz

    # Union-find components
    parent = list(range(len(bm.verts)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    for e in bm.edges:
        union(e.verts[0].index, e.verts[1].index)

    comps = {}
    for v in bm.verts:
        comps.setdefault(find(v.index), []).append(v)

    sizes = sorted(((len(vs), k) for k, vs in comps.items()), reverse=True)
    report.append(f"components={len(comps)} sizes={[s for s,_ in sizes[:10]]}")
    main_size, main_k = sizes[0]
    before = len(bm.verts)

    # Tire radius from mid axial slice of the main (or whole) mesh
    sample_verts = comps[main_k]
    ax_vals = [axial(_world_co(mw, v)) for v in sample_verts]
    ax_lo = sorted(ax_vals)[int(len(ax_vals) * 0.02)]
    ax_hi = sorted(ax_vals)[int(len(ax_vals) * 0.98)]
    mid = 0.5 * (ax_lo + ax_hi)
    band = max(1e-4, (ax_hi - ax_lo) * 0.15)
    slice_r = [
        radial(_world_co(mw, v))
        for v in sample_verts
        if abs(axial(_world_co(mw, v)) - mid) <= band
    ]
    slice_r.sort()
    tire_r = slice_r[int(len(slice_r) * 0.92)] if slice_r else sorted(
        radial(_world_co(mw, v)) for v in sample_verts
    )[int(main_size * 0.95)]
    report.append(f"tire_r={tire_r:.5f} ax=[{ax_lo:.4f},{ax_hi:.4f}]")

    deleted = 0

    if len(comps) >= 2 and sizes[1][0] < main_size * 0.5:
        # Loose parts: keep main + any large in-radius chunks; drop outliers
        keep = set(comps[main_k])
        ax_lim = max(abs(ax_lo), abs(ax_hi)) * 1.25
        for sz, k in sizes[1:]:
            vs = comps[k]
            cc = sum((_world_co(mw, v) for v in vs), Vector()) / len(vs)
            rad = radial(cc)
            ax = abs(axial(cc))
            if sz >= main_size * 0.2 and rad <= tire_r * 1.05:
                keep.update(vs)
                report.append(f"keep_comp size={sz}")
            elif rad > tire_r * 0.88 or ax > ax_lim:
                report.append(f"drop_comp size={sz} rad={rad:.3f} ax={ax:.3f}")
            elif sz < max(80, main_size * 0.03):
                report.append(f"drop_small size={sz}")
            else:
                keep.update(vs)
        del_verts = [v for v in bm.verts if v not in keep]
        deleted = len(del_verts)
        if del_verts:
            bmesh.ops.delete(bm, geom=del_verts, context="VERTS")
    else:
        # Welded: clip verts clearly outside tire radius; also hub axial spikes
        ax_pad = (ax_hi - ax_lo) * 0.1
        del_verts = []
        for v in list(bm.verts):
            w = _world_co(mw, v)
            r = radial(w)
            a = axial(w)
            if r > tire_r * 1.035:
                del_verts.append(v)
            elif a < ax_lo - ax_pad or a > ax_hi + ax_pad:
                if r < tire_r * 0.78:
                    del_verts.append(v)
        deleted = len(del_verts)
        report.append(f"clip_candidates={deleted}")
        if del_verts:
            bmesh.ops.delete(bm, geom=del_verts, context="VERTS")

    # Remove tiny leftover islands after clip
    bm.verts.ensure_lookup_table()
    if bm.verts:
        parent = list(range(len(bm.verts)))
        idx = {v: i for i, v in enumerate(bm.verts)}

        def find2(a):
            while parent[a] != a:
                parent[a] = parent[parent[a]]
                a = parent[a]
            return a

        def union2(a, b):
            ra, rb = find2(a), find2(b)
            if ra != rb:
                parent[rb] = ra

        for e in bm.edges:
            union2(idx[e.verts[0]], idx[e.verts[1]])
        comps2 = {}
        for v in bm.verts:
            comps2.setdefault(find2(idx[v]), []).append(v)
        main2 = max(comps2.values(), key=len)
        thresh = max(40, int(len(main2) * 0.02))
        drop = []
        for vs in comps2.values():
            if len(vs) < thresh and vs is not main2 and len(vs) != len(main2):
                drop.extend(vs)
        if drop:
            report.append(f"drop_islands_verts={len(drop)}")
            bmesh.ops.delete(bm, geom=drop, context="VERTS")

    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.to_mesh(obj.data)
    obj.data.update()
    bm.free()
    report.append(f"verts {before} -> {len(obj.data.vertices)} (deleted~{deleted})")
    return "\n".join(report)


def main():
    sel = [o for o in bpy.context.selected_objects if o.type == "MESH"]
    if len(sel) < 1:
        raise RuntimeError("Select the wheel mesh object(s) first.")

    # Duplicate originals as backup (hidden)
    backups = []
    for o in sel:
        dup = o.copy()
        dup.data = o.data.copy()
        dup.name = o.name + "_BACKUP_pre_clean"
        bpy.context.collection.objects.link(dup)
        dup.hide_set(True)
        dup.hide_render = True
        backups.append(dup.name)

    reports = [f"backups={backups}"]
    for o in sel:
        reports.append(clean_wheel(o))

    bpy.ops.wm.save_mainfile()
    text = "\n\n".join(reports)
    print(text)
    # Also write report next to project if possible
    try:
        out = bpy.path.abspath("//wheel_clean_report.txt")
        with open(out, "w") as f:
            f.write(text)
    except Exception:
        pass
    return text


if __name__ == "__main__":
    main()
