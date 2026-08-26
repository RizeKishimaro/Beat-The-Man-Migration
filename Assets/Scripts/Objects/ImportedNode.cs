using UnityEngine;

/// <summary>
/// Thin Node Independent-layer marker (ticket 149). Holds ObjectProperties
/// only. Master accept/slot stays on DefaultNode (prefab-on-approve).
/// </summary>
public class ImportedNode : MonoBehaviour
{
    public ObjectProperties properties;
}
