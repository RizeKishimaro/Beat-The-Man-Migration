using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single global runtime grid: cell occupancy, snap-to-cell, and world/cell
/// conversion. One shared coordinate space for exterior placement AND
/// building interiors — interior cells are just the subset of this same
/// grid that fall inside a building's footprint, not a separate grid.
///
/// Purely additive: does not read or modify Rigidbody physics,
/// CarrySocket/CarryableItem, the crate carry system, or ShopDoorLeaf.
/// Carry/drop integration is a separate, later ticket.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GridSystem : MonoBehaviour
{
    public static GridSystem Instance { get; private set; }

    [Tooltip("World position of the corner of cell (0,0).")]
    public Vector3 originWorld = Vector3.zero;

    [Tooltip("Size of one grid cell in meters. Must match the Unity/Blender editor grid (1m).")]
    public float cellSize = 1f;

    readonly Dictionary<GridCoord, object> occupants = new Dictionary<GridCoord, object>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("GridSystem: multiple instances in scene — keeping the first, disabling this one.", this);
            enabled = false;
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // --- World/cell conversion (Unity-space native) ---

    public GridCoord WorldToCell(Vector3 worldPos)
    {
        Vector3 local = worldPos - originWorld;
        int x = Mathf.FloorToInt(local.x / cellSize);
        int z = Mathf.FloorToInt(local.z / cellSize);
        return new GridCoord(x, z);
    }

    public Vector3 CellToWorldCorner(GridCoord cell)
    {
        return originWorld + new Vector3(cell.x * cellSize, 0f, cell.z * cellSize);
    }

    public Vector3 CellToWorldCenter(GridCoord cell)
    {
        return CellToWorldCorner(cell) + new Vector3(cellSize * 0.5f, 0f, cellSize * 0.5f);
    }

    // --- Blender-authored input: axis mirroring handled here, once, automatically. ---
    // Callers never need to know about or reapply BlenderUnityAxisConversion themselves.

    public GridCoord BlenderPositionToCell(Vector3 blenderPosition)
    {
        return WorldToCell(BlenderUnityAxisConversion.BlenderToUnity(blenderPosition));
    }

    // --- Multi-cell footprint helpers ---

    public IEnumerable<GridCoord> FootprintCells(GridCoord anchor, Vector2Int sizeInCells)
    {
        for (int dx = 0; dx < sizeInCells.x; dx++)
            for (int dz = 0; dz < sizeInCells.y; dz++)
                yield return new GridCoord(anchor.x + dx, anchor.z + dz);
    }

    /// <summary>Pure function — returns the snapped corner position and anchor cell without mutating occupancy.</summary>
    public Vector3 SnapToCell(Vector3 worldPos, out GridCoord anchorCell)
    {
        anchorCell = WorldToCell(worldPos);
        return CellToWorldCorner(anchorCell);
    }

    // --- Occupancy ---

    public bool IsFree(IEnumerable<GridCoord> cells)
    {
        foreach (GridCoord c in cells)
            if (occupants.ContainsKey(c))
                return false;
        return true;
    }

    public bool TryOccupy(IEnumerable<GridCoord> cells, object occupant)
    {
        List<GridCoord> list = new List<GridCoord>(cells);
        if (!IsFree(list))
            return false;
        foreach (GridCoord c in list)
            occupants[c] = occupant;
        return true;
    }

    public void Release(IEnumerable<GridCoord> cells)
    {
        foreach (GridCoord c in cells)
            occupants.Remove(c);
    }

    public object GetOccupant(GridCoord cell)
    {
        occupants.TryGetValue(cell, out object occupant);
        return occupant;
    }

    public bool IsOccupied(GridCoord cell) => occupants.ContainsKey(cell);
}
