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

/// <summary>
/// Per-object properties asset (ticket 149). Lives next to the mesh.
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
}
