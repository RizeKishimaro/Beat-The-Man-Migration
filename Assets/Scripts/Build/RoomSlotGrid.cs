using UnityEngine;

/// <summary>
/// Ticket 168/169/179 — 6×6 room slots on the existing 1m GridSystem (ticket 100).
/// </summary>
public static class RoomSlotGrid
{
    public const int SlotCells = 6;

    public static GridCoord SlotAnchorFromCell(GridCoord cell)
    {
        int x = Mathf.FloorToInt(cell.x / (float)SlotCells) * SlotCells;
        int z = Mathf.FloorToInt(cell.z / (float)SlotCells) * SlotCells;
        return new GridCoord(x, z);
    }

    public static GridCoord SlotAnchorFromWorld(GridSystem grid, Vector3 world)
    {
        return SlotAnchorFromCell(grid.WorldToCell(world));
    }

    public static bool IsSlotFree(GridSystem grid, GridCoord anchor)
    {
        if (grid == null)
            return true;
        return grid.IsFree(grid.FootprintCells(anchor, new Vector2Int(SlotCells, SlotCells)));
    }

    public static Vector3 SlotCenterWorld(GridSystem grid, GridCoord anchor)
    {
        Vector3 corner = grid.CellToWorldCorner(anchor);
        float half = grid.cellSize * SlotCells * 0.5f;
        return corner + new Vector3(half, 0f, half);
    }
}
