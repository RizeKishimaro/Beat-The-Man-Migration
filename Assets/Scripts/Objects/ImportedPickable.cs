using UnityEngine;

/// <summary>
/// Thin Pickable Independent-layer marker (ticket 149). Holds ObjectProperties
/// only. Master carry/physics stays on CarryableItem (prefab-on-approve).
/// </summary>
public class ImportedPickable : MonoBehaviour
{
    public ObjectProperties properties;
}
