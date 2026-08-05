using UnityEngine;

/// <summary>
/// Declares an object's grid footprint and asset-type classification
/// (mirrors ARCHITECTURE.md). Additive component — attach alongside
/// existing gameplay components (e.g. CarryableItem on Object prefabs);
/// never modifies them.
///
/// Enforces the ticket's footprint rule at edit time: Object-type assets
/// must be exactly 1x1 cells. Character/Building/Node/Vehicle may span
/// multiple cells.
/// </summary>
public class GridFootprint : MonoBehaviour
{
    public AssetGridType type = AssetGridType.Object;

    [Tooltip("Footprint size in cells. Object type is locked to (1,1).")]
    public Vector2Int sizeInCells = Vector2Int.one;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (type == AssetGridType.Object && sizeInCells != Vector2Int.one)
        {
            Debug.LogError(
                $"GridFootprint on '{name}': Object-type assets must be exactly 1x1 cells " +
                $"(per ARCHITECTURE.md footprint rule), but sizeInCells was {sizeInCells}. Clamping to (1,1).",
                this);
            sizeInCells = Vector2Int.one;
        }
        sizeInCells.x = Mathf.Max(1, sizeInCells.x);
        sizeInCells.y = Mathf.Max(1, sizeInCells.y);
    }
#endif
}
