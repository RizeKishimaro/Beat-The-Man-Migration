"""Ticket 163 — voxel-size math, no bpy. Used by _bake_fbx.py and formula verify."""
from __future__ import annotations

import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))

# Spike table: 0.0012 local voxel on ~0.78 x 0.81 x 0.54 m (longest 0.81).
SPIKE_LONGEST_M = 0.81
SPIKE_VOXEL_SIZE = 0.0012
DEFAULT_FRAC = SPIKE_VOXEL_SIZE / SPIKE_LONGEST_M


def load_decimate_config():
    path = os.path.join(HERE, "_decimate_config.json")
    cfg = {
        "target_tris": 1500,
        "target_tris_pickable": 1500,
        "target_tris_nodes": 1500,
        "stall_multiple": 2.0,
        "voxel_frac_of_longest": DEFAULT_FRAC,
        "voxel_min": 0.0002,
        "voxel_max": 0.05,
    }
    if os.path.isfile(path):
        with open(path) as f:
            loaded = json.load(f)
        for key in cfg:
            if key in loaded:
                cfg[key] = loaded[key]
    return cfg


def voxel_size_for_longest(longest_m, frac=None, vmin=None, vmax=None):
    cfg = load_decimate_config()
    if frac is None:
        frac = cfg["voxel_frac_of_longest"]
    if vmin is None:
        vmin = cfg["voxel_min"]
    if vmax is None:
        vmax = cfg["voxel_max"]
    if longest_m is None or longest_m <= 0:
        return vmin
    return max(vmin, min(vmax, longest_m * frac))


def stall_limit_tris(target, multiple=None):
    """Halt to _Review if collapse lands above this many tris."""
    cfg = load_decimate_config()
    if multiple is None:
        multiple = float(cfg.get("stall_multiple", 2.0))
    target = int(target)
    if target <= 0:
        return 0, multiple
    return int(target * multiple), multiple


def resolve_target_tris(kind=None, override=None):
    """CLI --target-tris wins. Else Pickable/Nodes JSON key. Else target_tris."""
    cfg = load_decimate_config()
    if override is not None:
        return int(override)
    key = None
    k = (kind or "").strip().lower()
    if k.startswith("node"):
        key = "target_tris_nodes"
    elif k.startswith("pick"):
        key = "target_tris_pickable"
    if key and key in cfg:
        return int(cfg[key])
    return int(cfg["target_tris"])
