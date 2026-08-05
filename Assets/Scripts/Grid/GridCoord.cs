using System;

/// <summary>
/// Integer coordinate of a single grid cell, corner-origin convention:
/// cell (x, z) occupies world space [x*cellSize, (x+1)*cellSize) on X and
/// [z*cellSize, (z+1)*cellSize) on Z, relative to the owning GridSystem's origin.
/// </summary>
[Serializable]
public struct GridCoord : IEquatable<GridCoord>
{
    public int x;
    public int z;

    public GridCoord(int x, int z)
    {
        this.x = x;
        this.z = z;
    }

    public bool Equals(GridCoord other) => x == other.x && z == other.z;
    public override bool Equals(object obj) => obj is GridCoord other && Equals(other);
    public override int GetHashCode() => (x * 397) ^ z;
    public override string ToString() => $"({x}, {z})";

    public static GridCoord operator +(GridCoord a, GridCoord b) => new GridCoord(a.x + b.x, a.z + b.z);
    public static bool operator ==(GridCoord a, GridCoord b) => a.Equals(b);
    public static bool operator !=(GridCoord a, GridCoord b) => !a.Equals(b);
}
