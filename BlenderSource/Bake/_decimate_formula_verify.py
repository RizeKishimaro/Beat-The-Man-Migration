"""Ticket 163 — voxel formula check. Run with python3 (no Blender)."""
from __future__ import annotations

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import _decimate_math as m  # noqa: E402


def main():
    cfg = m.load_decimate_config()
    spike = m.voxel_size_for_longest(0.81)
    scaled = m.voxel_size_for_longest(0.81 * 3.0)
    report = {
        "ticket": 163,
        "config": cfg,
        "spike_longest_0.81_voxel": spike,
        "spike_expected": 0.0012,
        "spike_ok": abs(spike - 0.0012) < 1e-9,
        "3x_longest_2.43_voxel": scaled,
        "3x_expected": 0.0036,
        "3x_ok": abs(scaled - 0.0036) < 1e-9,
        "target_tris": cfg["target_tris"],
        "resolve_global": m.resolve_target_tris(None, None),
        "resolve_pickable": m.resolve_target_tris("Pickable", None),
        "resolve_nodes": m.resolve_target_tris("Nodes", None),
        "resolve_cli_override": m.resolve_target_tris("Nodes", 800),
        "flag": "target_tris is JSON (now 1500). Per-category keys: target_tris_pickable / target_tris_nodes.",
    }
    report["resolve_ok"] = (
        report["resolve_global"] == 1500
        and report["resolve_pickable"] == 1500
        and report["resolve_nodes"] == 1500
        and report["resolve_cli_override"] == 800
    )
    report["ok"] = report["spike_ok"] and report["3x_ok"] and report["resolve_ok"]
    out = os.path.join(HERE, "_decimate_formula_report.json")
    with open(out, "w") as f:
        json.dump(report, f, indent=2)
        f.write("\n")
    print(json.dumps(report, indent=2))
    sys.exit(0 if report["ok"] else 1)


if __name__ == "__main__":
    main()
