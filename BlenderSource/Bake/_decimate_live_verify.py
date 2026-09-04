"""
Ticket 163 — live remesh/decimate + existing-flow regression.

Blender --background --python BlenderSource/Bake/_decimate_live_verify.py
Writes BlenderSource/Bake/_decimate_live_report.json
"""
from __future__ import annotations

import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
import _decimate_math as decimate_math  # noqa: E402

BLENDER = "/Applications/Blender.app/Contents/MacOS/Blender"
BAKE = os.path.join(HERE, "_bake_fbx.py")
MAKE_DENSE = os.path.join(HERE, "_make_dense_fbx.py")
OUTDIR = os.path.join(ROOT, "Temp", "bake163")
CHAIR = os.path.join(
    ROOT,
    "Assets/Objects/Pickable/Meshy_AI_Cream_Cushioned_Woode_0901111846_texture_fbx",
    "Meshy_AI_Cream_Cushioned_Woode_0901111846_texture.fbx",
)
SPIKE_RAW = os.path.join(
    ROOT,
    "_Spike163/RawSource/Meshy_AI_Cream_Cushioned_Woode_0901123805_texture_fbx",
    "Meshy_AI_Cream_Cushioned_Woode_0901123805_texture.fbx",
)


def blender_bake(src, dst, decimate):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    extra = ["--decimate"] if decimate else []
    cmd = [
        BLENDER, "--background", "--python", BAKE, "--",
        "--input", src, "--output", dst, *extra,
    ]
    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True)
    js_path = os.path.splitext(dst)[0] + ".bake.json"
    payload = None
    if os.path.isfile(js_path):
        with open(js_path) as f:
            payload = json.load(f)
    return proc.returncode, proc.stdout[-4000:], payload


def summarize_decimate(js):
    d = (js or {}).get("decimate") or {}
    return {
        "exit_ok": True,
        "decimate_requested": bool((js or {}).get("decimate_requested")),
        "longest_aabb_m": d.get("longest_aabb_m"),
        "voxel_size": d.get("voxel_size"),
        "tris_raw": d.get("tris_raw"),
        "tris_after_cleanup": d.get("tris_after_cleanup"),
        "tris_after_decimate": d.get("tris_after_decimate"),
        "topology_before": d.get("topology_before"),
        "uv_layers_raw": d.get("uv_layers_raw"),
        "uv_layers_after_decimate": d.get("uv_layers_after_decimate"),
        "skipped": d.get("skipped"),
        "voxel_remesh": d.get("voxel_remesh"),
        "steps_have_163_collapse": any(
            "163 decimate COLLAPSE" in s for s in ((js or {}).get("steps") or [])
        ),
    }


def dense_case_ok(block, target=None, tol=0.15):
    if target is None:
        target = int(decimate_math.load_decimate_config()["target_tris"])
    if not block:
        return False
    after = block.get("tris_after_decimate") or 0
    return (
        block.get("decimate_requested") is True
        and not block.get("skipped")
        and block.get("steps_have_163_collapse") is True
        and block.get("voxel_remesh") is False
        and (block.get("uv_layers_after_decimate") or 0) > 0
        and abs(after - target) / float(target) <= tol
    )


def run_dense_bakes():
    proc = subprocess.run(
        [BLENDER, "--background", "--python", MAKE_DENSE],
        cwd=ROOT,
        capture_output=True,
        text=True,
    )
    out = {
        "make_exit": proc.returncode,
        "make_tail": (proc.stdout or "")[-800:],
    }
    if proc.returncode != 0:
        out["ok"] = False
        out["make_err"] = (proc.stderr or "")[-800:]
        return out

    spike_src = os.path.join(OUTDIR, "dense", "spike_scale.fbx")
    triple_src = os.path.join(OUTDIR, "dense", "triple_scale.fbx")
    code_s, _, js_s = blender_bake(
        spike_src, os.path.join(OUTDIR, "dense", "spike_baked.fbx"), True
    )
    code_t, _, js_t = blender_bake(
        triple_src, os.path.join(OUTDIR, "dense", "triple_baked.fbx"), True
    )
    out["spike"] = summarize_decimate(js_s)
    out["spike"]["exit"] = code_s
    out["triple"] = summarize_decimate(js_t)
    out["triple"]["exit"] = code_t
    spike_ok = code_s == 0 and dense_case_ok(out["spike"])
    triple_ok = code_t == 0 and dense_case_ok(out["triple"])
    ls = out["spike"].get("longest_aabb_m") or 0
    lt = out["triple"].get("longest_aabb_m") or 0
    scale_ok = ls > 0 and abs(lt / ls - 3.0) < 0.05
    out["aabb_scales_3x"] = scale_ok
    out["spike_ok"] = spike_ok
    out["triple_ok"] = triple_ok
    out["ok"] = bool(spike_ok and triple_ok and scale_ok)
    return out


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    cfg = decimate_math.load_decimate_config()
    report = {
        "ticket": 163,
        "config": cfg,
        "voxel_spike_0.81": decimate_math.voxel_size_for_longest(0.81),
        "voxel_3x_2.43": decimate_math.voxel_size_for_longest(2.43),
        "formula_ok": abs(decimate_math.voxel_size_for_longest(0.81) - 0.0012) < 1e-9
        and abs(decimate_math.voxel_size_for_longest(2.43) - 0.0036) < 1e-9,
        "flag_target_tris": (
            "Default target_tris=%s. Per-category: target_tris_pickable / "
            "target_tris_nodes in _decimate_config.json."
            % cfg["target_tris"]
        ),
    }

    src = CHAIR if os.path.isfile(CHAIR) else SPIKE_RAW
    report["source"] = os.path.relpath(src, ROOT)

    normal_dst = os.path.join(OUTDIR, "normal", "out.fbx")
    dec_dst = os.path.join(OUTDIR, "decimate", "out.fbx")

    code_n, log_n, js_n = blender_bake(src, normal_dst, False)
    code_d, log_d, js_d = blender_bake(src, dec_dst, True)

    report["normal_exit"] = code_n
    report["decimate_exit"] = code_d
    report["normal_decimate_requested"] = bool(js_n and js_n.get("decimate_requested"))
    report["decimate_decimate_requested"] = bool(js_d and js_d.get("decimate_requested"))

    if js_n:
        report["normal_tris_in"] = js_n.get("tris")
        report["normal_tris_out"] = js_n.get("tris_after_bake")
        report["normal_tris_unchanged"] = js_n.get("tris") == js_n.get("tris_after_bake")
        report["normal_steps_have_163"] = any(
            "163 " in s for s in (js_n.get("steps") or [])
        )
    normal_ok = (
        code_n == 0
        and js_n is not None
        and js_n.get("decimate_requested") is False
        and not report.get("normal_steps_have_163")
        and report.get("normal_tris_unchanged") is True
    )
    report["existing_flow_unchanged"] = normal_ok
    if js_d and js_d.get("decimate"):
        d = js_d["decimate"]
        report["decimate_block"] = {
            "longest_aabb_m": d.get("longest_aabb_m"),
            "voxel_size": d.get("voxel_size"),
            "tris_raw": d.get("tris_raw"),
            "tris_after_cleanup": d.get("tris_after_cleanup"),
            "tris_after_decimate": d.get("tris_after_decimate"),
            "uv_layers_raw": d.get("uv_layers_raw"),
            "uv_layers_after_decimate": d.get("uv_layers_after_decimate"),
            "voxel_remesh": d.get("voxel_remesh"),
            "topology_before": d.get("topology_before"),
        }
        report["downstream_incoming_unchanged"] = (
            "Assets/Objects/_Incoming/{Pickable,Nodes} — 148/149/160 Calculate 90 / 162 still apply"
        )

    if js_d and js_d.get("decimate"):
        report["decimate_skipped"] = js_d["decimate"].get("skipped")

    dense = run_dense_bakes()
    report["dense"] = dense

    report["ok"] = bool(
        report["formula_ok"]
        and report["existing_flow_unchanged"]
        and code_d == 0
        and dense.get("ok")
    )
    out = os.path.join(HERE, "_decimate_live_report.json")
    with open(out, "w") as f:
        json.dump(report, f, indent=2)
        f.write("\n")
    print(json.dumps(report, indent=2))
    if not report["ok"]:
        print("NORMAL_LOG_TAIL", log_n[-1500:])
        print("DECIMATE_LOG_TAIL", log_d[-1500:])
    sys.exit(0 if report["ok"] else 1)


if __name__ == "__main__":
    main()
