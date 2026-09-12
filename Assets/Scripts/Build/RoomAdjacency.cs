using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ticket 172 — after 169 spawn, replace facing walls of adjacent 6×6 rooms
/// with one shared wall + ShopDoorLeaf pair at the tile boundary.
/// Placement-time only. Does not edit DefaultBuilding meshes.
/// </summary>
public static class RoomAdjacency
{
    public const int SidePosX = 1;
    public const int SideNegX = 2;
    public const int SidePosZ = 4;
    public const int SideNegZ = 8;
    public const string SharedRootPrefix = "SharedWall";

    const float Ext = 6f;
    const float WMin = 0f;
    const float WMax = 6f;
    const float WallT = 0.3f;
    const float WallH = 3.0f;
    const float DoorW = 1.5f;
    const float DoorH = 2.2f;
    const float LeafT = 0.06f;

    static readonly HashSet<string> ConnectedEdges = new HashSet<string>();

    public static void ResetConnectedEdges()
    {
        ConnectedEdges.Clear();
    }

    static readonly string[] PosXNames = { "Wall_Right" };
    static readonly string[] NegXNames = { "Wall_Left" };
    static readonly string[] PosZNames = { "Wall_Back" };
    static readonly string[] NegZNames =
    {
        "Wall_Front_Left", "Wall_Front_Right", "Wall_Front_Lintel",
        "DoorHinge_Left", "DoorHinge_Right"
    };

    public static void OnPlaced(GameObject room)
    {
        if (room == null)
            return;
        RoomSlotAnchor self = room.GetComponent<RoomSlotAnchor>();
        GridSystem grid = GridSystem.Instance;
        if (self == null || grid == null)
            return;

        TrySide(self, grid, SidePosX, new GridCoord(self.Anchor.x + RoomSlotGrid.SlotCells, self.Anchor.z));
        TrySide(self, grid, SideNegX, new GridCoord(self.Anchor.x - RoomSlotGrid.SlotCells, self.Anchor.z));
        TrySide(self, grid, SidePosZ, new GridCoord(self.Anchor.x, self.Anchor.z + RoomSlotGrid.SlotCells));
        TrySide(self, grid, SideNegZ, new GridCoord(self.Anchor.x, self.Anchor.z - RoomSlotGrid.SlotCells));
    }

    static void TrySide(RoomSlotAnchor self, GridSystem grid, int side, GridCoord neighborAnchor)
    {
        if (self.HasSide(side))
            return;
        GameObject otherGo = grid.GetOccupant(neighborAnchor) as GameObject;
        if (otherGo == null)
            return;
        RoomSlotAnchor other = otherGo.GetComponent<RoomSlotAnchor>();
        if (other == null)
            return;

        string key = EdgeKey(self.Anchor, other.Anchor);
        if (!ConnectedEdges.Add(key))
            return;

        HideSide(self.transform, side);
        HideSide(other.transform, Opposite(side));
        self.MarkSide(side);
        other.MarkSide(Opposite(side));
        BuildShared(self, other, side);
    }

    static void BuildShared(RoomSlotAnchor a, RoomSlotAnchor b, int sideFromA)
    {
        bool alongZ = sideFromA == SidePosX || sideFromA == SideNegX;
        RoomSlotAnchor westOrSouth = a;
        if (alongZ)
        {
            if (a.Anchor.x > b.Anchor.x)
                westOrSouth = b;
        }
        else if (a.Anchor.z > b.Anchor.z)
            westOrSouth = b;

        Vector3 origin = westOrSouth.transform.position;
        float boundary = alongZ ? origin.x + Ext : origin.z + Ext;
        float along0 = (alongZ ? origin.z : origin.x) + WMin;
        float along1 = (alongZ ? origin.z : origin.x) + WMax;
        float mid = (alongZ ? origin.z : origin.x) + Ext * 0.5f;
        float door0 = mid - DoorW * 0.5f;
        float door1 = mid + DoorW * 0.5f;

        Transform parent = a.transform.parent != null ? a.transform.parent : a.transform;
        GameObject root = new GameObject(
            SharedRootPrefix + "_" + a.Anchor.x + "_" + a.Anchor.z
            + "_" + b.Anchor.x + "_" + b.Anchor.z);
        root.transform.SetParent(parent, false);
        root.transform.position = Vector3.zero;

        Material wallMat = FindMat(a.transform, "Wall_Left", "Wall_Right", "Wall_Back");
        Material doorMat = FindMat(a.transform, "DoorLeaf_Left", "DoorLeaf_Right");
        if (doorMat == null)
            doorMat = MakeDoorMat();

        var walls = new List<Renderer>();
        walls.Add(MakeBox(root.transform, "Wall_Shared_A",
            alongZ, boundary, along0, door0, 0f, WallH, WallT, wallMat, true));
        walls.Add(MakeBox(root.transform, "Wall_Shared_B",
            alongZ, boundary, door1, along1, 0f, WallH, WallT, wallMat, true));
        walls.Add(MakeBox(root.transform, "Wall_Shared_Lintel",
            alongZ, boundary, door0, door1, DoorH, WallH, WallT, wallMat, true));

        MakeDoors(root.transform, alongZ, boundary, door0, door1, doorMat);

        SharedWallVisibility vis = root.AddComponent<SharedWallVisibility>();
        vis.roomA = a.GetComponentInChildren<BuildingInteriorVisibility>(true);
        vis.roomB = b.GetComponentInChildren<BuildingInteriorVisibility>(true);
        vis.wallPieces = walls.ToArray();
        vis.ceilingPieces = null;
    }

    static void MakeDoors(Transform parent, bool alongZ, float boundary, float door0, float door1, Material doorMat)
    {
        Vector3 leftPos = alongZ
            ? new Vector3(boundary, 0f, door0)
            : new Vector3(door0, 0f, boundary);
        Vector3 rightPos = alongZ
            ? new Vector3(boundary, 0f, door1)
            : new Vector3(door1, 0f, boundary);
        Quaternion leftRot = alongZ
            ? Quaternion.LookRotation(Vector3.left, Vector3.up)
            : Quaternion.LookRotation(Vector3.forward, Vector3.up);
        Quaternion rightRot = leftRot * Quaternion.Euler(0f, 180f, 0f);

        MakeHinge(parent, "DoorHinge_Left", leftPos, leftRot, doorMat);
        MakeHinge(parent, "DoorHinge_Right", rightPos, rightRot, doorMat);
    }

    static void MakeHinge(Transform parent, string name, Vector3 pos, Quaternion rot, Material doorMat)
    {
        GameObject hinge = new GameObject(name);
        hinge.transform.SetParent(parent, false);
        hinge.transform.SetPositionAndRotation(pos, rot);
        hinge.AddComponent<ShopDoorLeaf>();

        GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leaf.name = name.Contains("Left") ? "DoorLeaf_Left" : "DoorLeaf_Right";
        leaf.transform.SetParent(hinge.transform, false);
        leaf.transform.localPosition = new Vector3(DoorW * 0.25f, DoorH * 0.5f, 0f);
        leaf.transform.localRotation = Quaternion.identity;
        leaf.transform.localScale = new Vector3(DoorW * 0.5f, DoorH, LeafT);
        Renderer rend = leaf.GetComponent<Renderer>();
        if (rend != null && doorMat != null)
            rend.sharedMaterial = doorMat;
    }

    static Renderer MakeBox(
        Transform parent, string name, bool alongZ,
        float acrossMid, float along0, float along1,
        float y0, float y1, float acrossSize,
        Material mat, bool keepCollider)
    {
        float alongSize = along1 - along0;
        if (alongSize < 0.001f || y1 - y0 < 0.001f || acrossSize < 0.001f)
            return null;
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        Vector3 center = alongZ
            ? new Vector3(acrossMid, (y0 + y1) * 0.5f, (along0 + along1) * 0.5f)
            : new Vector3((along0 + along1) * 0.5f, (y0 + y1) * 0.5f, acrossMid);
        go.transform.position = center;
        go.transform.localScale = alongZ
            ? new Vector3(acrossSize, y1 - y0, alongSize)
            : new Vector3(alongSize, y1 - y0, acrossSize);
        Renderer rend = go.GetComponent<Renderer>();
        if (rend != null && mat != null)
            rend.sharedMaterial = mat;
        if (!keepCollider)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                Object.Destroy(col);
        }
        return rend;
    }

    static void HideSide(Transform root, int side)
    {
        string[] names = NamesFor(side);
        for (int i = 0; i < names.Length; i++)
        {
            Transform t = FindNamed(root, names[i]);
            if (t != null)
                t.gameObject.SetActive(false);
        }
    }

    static string[] NamesFor(int side)
    {
        if (side == SidePosX) return PosXNames;
        if (side == SideNegX) return NegXNames;
        if (side == SidePosZ) return PosZNames;
        return NegZNames;
    }

    static int Opposite(int side)
    {
        if (side == SidePosX) return SideNegX;
        if (side == SideNegX) return SidePosX;
        if (side == SidePosZ) return SideNegZ;
        return SidePosZ;
    }

    static string EdgeKey(GridCoord a, GridCoord b)
    {
        if (a.x < b.x || (a.x == b.x && a.z < b.z))
            return a.x + "," + a.z + ":" + b.x + "," + b.z;
        return b.x + "," + b.z + ":" + a.x + "," + a.z;
    }

    static Transform FindNamed(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    static Material FindMat(Transform root, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            Transform t = FindNamed(root, names[i]);
            if (t == null)
                continue;
            Renderer r = t.GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != null)
                return r.sharedMaterial;
        }
        return null;
    }

    static Material MakeDoorMat()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return null;
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", new Color(0.45f, 0.26f, 0.13f));
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", new Color(0.45f, 0.26f, 0.13f));
        return mat;
    }
}
