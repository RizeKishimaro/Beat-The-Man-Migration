using System;
using System.Collections.Generic;
using UnityEngine;

public enum ObjectKind
{
    Pickable = 0,
    Node = 1
}

public enum ObjectTriState
{
    Unset = 0,
    Yes = 1,
    No = 2
}

public enum ObjectSizeTier
{
    Unset = 0,
    Small = 1,
    Medium = 2,
    Large = 3
}

public enum ObjectPhysicsGravity
{
    Unset = 0,
    On = 1,
    Off = 2
}

/// <summary>Ticket 155 — how the player interacts with the object. Unset = blank.</summary>
public enum ObjectInteraction
{
    Unset = 0,
    PickUp = 1,
    Use = 2,
    Open = 3,
    Combine = 4,
    Sit = 5,
    None = 6
}

/// <summary>Ticket 155 — how the object is placed in the world. Unset = blank.</summary>
public enum ObjectPlacementMethod
{
    Unset = 0,
    Floor = 1,
    Surface = 2,
    WallMount = 3,
    Hanging = 4,
    GridSnap = 5
}

/// <summary>
/// Ticket 155 — Node-compat bitmask matching in-scene roles
/// (Node_Counter / Keep / Bin / Storage). 0 = unset. Any = all roles.
/// </summary>
[Flags]
public enum ObjectNodeCompat
{
    None = 0,
    Counter = 1 << 0,
    Keep = 1 << 1,
    Bin = 1 << 2,
    Storage = 1 << 3,
    Any = 1 << 4
}

[Serializable]
public struct ObjectSfxSet
{
    public AudioClip pickup;
    public AudioClip drop;
    public AudioClip use;
    public AudioClip open;
    public AudioClip custom;
}

[Serializable]
public struct ObjectUniqueEntry
{
    public string key;
    public string value;
}

/// <summary>
/// Per-object properties asset (ticket 149 + 155). Lives next to the mesh.
/// Knowable fields are filled on generate; judgment fields stay unset.
/// Approved is a status flag, not a lock — editing after approval is expected.
/// </summary>
[CreateAssetMenu(fileName = "ObjectProperties", menuName = "BeatTheMan/Object Properties")]
public class ObjectProperties : ScriptableObject
{
    public string objectName;
    [Tooltip("Object folder asset (DefaultAsset in the Editor).")]
    public UnityEngine.Object folder;
    public ObjectKind kind;
    public bool pickupable;
    public bool approved;

    [Tooltip("Ticket 159 — Pickable / GridFootprint Object only. When true, skip 1×1m cell-fit; keep real-world scale (152). Nodes ignore this.")]
    public bool exemptFromCellFitScaling;

    [Tooltip("Generated prefab path after Approve (ticket 156). Cleared on Unapprove.")]
    public string prefabPath;

    [Tooltip("0 = unset. Do not guess.")]
    public float weight;
    [Tooltip("0 = unset. Do not guess.")]
    public float price;
    public string category;
    public ObjectTriState stackable;
    public ObjectSizeTier sizeTier;
    public ObjectPhysicsGravity physicsGravity;

    public float scaleMultiplier = 1f;
    public Vector3 pivotOffset;

    // --- Ticket 155 (schema + Inspector only; all blank/unset by default) ---
    public ObjectInteraction interaction;
    public ObjectPlacementMethod placementMethod;
    public ObjectNodeCompat nodeCompat;
    public AnimationClip[] animationClips;
    public RuntimeAnimatorController animatorController;
    public ObjectSfxSet sfx;
    public List<ObjectUniqueEntry> uniqueProperties = new List<ObjectUniqueEntry>();
}
