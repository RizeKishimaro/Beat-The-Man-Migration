/// <summary>
/// Mirrors the five asset categories from ARCHITECTURE.md. Drives grid
/// footprint rules: Object must be exactly 1x1 cells; Character, Building,
/// Node, and Vehicle may span multiple cells.
/// </summary>
public enum AssetGridType
{
    Object,
    Character,
    Building,
    Node,
    Vehicle,
}
