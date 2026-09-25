using UnityEngine;

/// <summary>
/// Ticket 169 — instantiate Room_HR on <see cref="BuildMenuController.PlacementConfirmed"/>.
/// Snaps to 168/179's 6×6 slot corner (137 flush/zero-gap unit). Occupies via GridSystem (100).
/// Ticket 172 connects adjacent rooms at the boundary after occupy.
/// </summary>
[DefaultExecutionOrder(60)]
public class RoomPlacement : MonoBehaviour
{
    public const string PrefabPath = "Assets/Prefabs/Buildings/Room_HR.prefab";

    public GameObject buildingPrefab;

    void OnEnable()
    {
        BuildMenuController.PlacementConfirmed += OnConfirmed;
    }

    void OnDisable()
    {
        BuildMenuController.PlacementConfirmed -= OnConfirmed;
    }

    void OnConfirmed(GridCoord anchor)
    {
        TryPlace(anchor);
    }

    public bool TryPlace(GridCoord anchor)
    {
        GameObject prefab = buildingPrefab;
        GridSystem grid = GridSystem.Instance;
        if (prefab == null || grid == null)
        {
            Debug.LogWarning("RoomPlacement: missing DefaultBuilding prefab or GridSystem.", this);
            return false;
        }
        if (!RoomSlotGrid.IsSlotFree(grid, anchor))
            return false;

        Vector3 corner = grid.CellToWorldCorner(anchor);
        corner.y = 0f;

        GameObject go = Instantiate(prefab, transform);
        go.transform.SetPositionAndRotation(corner, Quaternion.identity);

        if (!grid.TryOccupy(
                grid.FootprintCells(anchor, new Vector2Int(RoomSlotGrid.SlotCells, RoomSlotGrid.SlotCells)),
                go))
        {
            Destroy(go);
            return false;
        }

        RoomSlotAnchor slot = go.GetComponent<RoomSlotAnchor>();
        if (slot == null)
            slot = go.AddComponent<RoomSlotAnchor>();
        slot.Init(anchor);
        RoomAdjacency.OnPlaced(go);
        return true;
    }
}
