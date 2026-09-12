using UnityEngine;

/// <summary>
/// Ticket 172/173/179 — slot identity on DefaultBuilding.
/// Isolated shell is authored at 6×6 (tile = exterior). No stretch-to-fill.
/// Connected: trim floor / ceiling / perpendicular walls to the shared inner face.
/// </summary>
public class RoomSlotAnchor : MonoBehaviour
{
    public const float Ext = 6f;
    public const float WallT = 0.3f;
    public const float Half = WallT * 0.5f;

    public GridCoord Anchor;
    public byte ConnectedMask;
    public Vector3 OrigInteriorSize;
    public Vector3 OrigInteriorCenter;
    public BoxCollider Interior;

    bool inited;

    struct MeshOrig
    {
        public string name;
        public Transform t;
        public Vector3 localScale;
        public Vector3 position;
    }

    MeshOrig[] meshes;

    static readonly string[] StretchNames =
    {
        "RoomFloor", "RoomCeiling",
        "Wall_Front_Left", "Wall_Front_Right", "Wall_Back",
        "Wall_Left", "Wall_Right"
    };

    void Start()
    {
        if (inited)
            return;
        GridSystem grid = GridSystem.Instance;
        GridCoord a = default;
        if (grid != null)
            a = RoomSlotGrid.SlotAnchorFromWorld(grid, transform.position);
        Init(a);
    }

    public void Init(GridCoord anchor)
    {
        Anchor = anchor;
        BuildingInteriorVisibility vis = GetComponentInChildren<BuildingInteriorVisibility>(true);
        Interior = vis != null ? vis.interiorVolume as BoxCollider : null;
        inited = true;
        if (Interior != null)
        {
            OrigInteriorSize = Interior.size;
            OrigInteriorCenter = Interior.center;
        }
        CacheMeshes();
    }

    public bool HasSide(int sideBit)
    {
        return (ConnectedMask & (byte)sideBit) != 0;
    }

    public void MarkSide(int sideBit)
    {
        ConnectedMask |= (byte)sideBit;
        RefreshInterior();
        StretchShell();
    }

    void ApplyLocalBox(Transform t, Renderer rend, Vector3 tgtMin, Vector3 tgtMax)
    {
        RoomLocalAabb(transform, rend.bounds, out Vector3 min, out Vector3 max);
        tgtMin.y = min.y;
        tgtMax.y = max.y;
        Vector3 origSize = max - min;
        Vector3 targetSize = tgtMax - tgtMin;
        if (origSize.x < 1e-5f || origSize.y < 1e-5f || origSize.z < 1e-5f)
            return;
        Vector3 origScale = t.localScale;
        t.localScale = RoomAxisScale(t, transform, origScale,
            targetSize.x / origSize.x,
            targetSize.y / origSize.y,
            targetSize.z / origSize.z);
        RoomLocalAabb(transform, rend.bounds, out Vector3 curMin, out Vector3 curMax);
        Vector3 curCenter = (curMin + curMax) * 0.5f;
        Vector3 tgtCenter = (tgtMin + tgtMax) * 0.5f;
        t.position += transform.TransformVector(tgtCenter - curCenter);
    }

    void CacheMeshes()
    {
        meshes = new MeshOrig[StretchNames.Length];
        for (int i = 0; i < StretchNames.Length; i++)
        {
            Transform t = FindNamed(transform, StretchNames[i]);
            meshes[i].name = StretchNames[i];
            meshes[i].t = t;
            if (t != null)
            {
                meshes[i].localScale = t.localScale;
                meshes[i].position = t.position;
            }
        }
    }

    void RefreshInterior()
    {
        if (Interior == null)
            return;
        Vector3 min = OrigInteriorCenter - OrigInteriorSize * 0.5f;
        Vector3 max = OrigInteriorCenter + OrigInteriorSize * 0.5f;
        if ((ConnectedMask & RoomAdjacency.SidePosX) != 0)
            max.x = Ext;
        if ((ConnectedMask & RoomAdjacency.SideNegX) != 0)
            min.x = 0f;
        if ((ConnectedMask & RoomAdjacency.SidePosZ) != 0)
            max.z = Ext;
        if ((ConnectedMask & RoomAdjacency.SideNegZ) != 0)
            min.z = 0f;
        Interior.size = max - min;
        Interior.center = (max + min) * 0.5f;
    }

    void StretchShell()
    {
        if (meshes == null)
            return;
        for (int i = 0; i < meshes.Length; i++)
        {
            Transform t = meshes[i].t;
            if (t == null || !t.gameObject.activeSelf)
                continue;
            Renderer rend = t.GetComponent<Renderer>();
            if (rend == null)
                continue;

            t.localScale = meshes[i].localScale;
            t.position = meshes[i].position;
            RoomLocalAabb(transform, rend.bounds, out Vector3 min, out Vector3 max);
            if (meshes[i].name == "RoomFloor" || meshes[i].name == "RoomCeiling")
                ExpandFloor(ref min, ref max);
            else
                TrimWall(meshes[i].name, ref min, ref max);
            ApplyLocalBox(t, rend, min, max);
        }
    }

    void ExpandFloor(ref Vector3 min, ref Vector3 max)
    {
        if ((ConnectedMask & RoomAdjacency.SidePosX) != 0)
            max.x = Ext;
        if ((ConnectedMask & RoomAdjacency.SideNegX) != 0)
            min.x = 0f;
        if ((ConnectedMask & RoomAdjacency.SidePosZ) != 0)
            max.z = Ext;
        if ((ConnectedMask & RoomAdjacency.SideNegZ) != 0)
            min.z = 0f;
    }

    void TrimWall(string name, ref Vector3 min, ref Vector3 max)
    {
        if (name == "Wall_Front_Right" && (ConnectedMask & RoomAdjacency.SidePosX) != 0)
            max.x = Ext - Half;
        if (name == "Wall_Front_Left" && (ConnectedMask & RoomAdjacency.SideNegX) != 0)
            min.x = Half;
        if (name == "Wall_Back")
        {
            if ((ConnectedMask & RoomAdjacency.SidePosX) != 0)
                max.x = Ext - Half;
            if ((ConnectedMask & RoomAdjacency.SideNegX) != 0)
                min.x = Half;
        }
        if (name == "Wall_Left" || name == "Wall_Right")
        {
            if ((ConnectedMask & RoomAdjacency.SidePosZ) != 0)
                max.z = Ext - Half;
            if ((ConnectedMask & RoomAdjacency.SideNegZ) != 0)
                min.z = Half;
        }
    }

    /// <summary>
    /// World AABB → room-local AABB via all 8 corners (not just bounds.min/max).
    /// </summary>
    public static void RoomLocalAabb(Transform room, Bounds world, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        Vector3 c = world.center;
        Vector3 e = world.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = c + Vector3.Scale(e, new Vector3(
                (i & 1) == 0 ? -1f : 1f,
                (i & 2) == 0 ? -1f : 1f,
                (i & 4) == 0 ? -1f : 1f));
            Vector3 lp = room.InverseTransformPoint(p);
            min = Vector3.Min(min, lp);
            max = Vector3.Max(max, lp);
        }
    }

    static Vector3 RoomAxisScale(
        Transform mesh, Transform room, Vector3 origScale,
        float mulX, float mulY, float mulZ)
    {
        Vector3 n = origScale;
        n.x *= RoomMulForWorldDir(mesh.TransformDirection(Vector3.right), room, mulX, mulY, mulZ);
        n.y *= RoomMulForWorldDir(mesh.TransformDirection(Vector3.up), room, mulX, mulY, mulZ);
        n.z *= RoomMulForWorldDir(mesh.TransformDirection(Vector3.forward), room, mulX, mulY, mulZ);
        return n;
    }

    static float RoomMulForWorldDir(Vector3 worldDir, Transform room, float mx, float my, float mz)
    {
        Vector3 local = room.InverseTransformDirection(worldDir);
        float ax = Mathf.Abs(local.x);
        float ay = Mathf.Abs(local.y);
        float az = Mathf.Abs(local.z);
        if (ax >= ay && ax >= az)
            return mx;
        if (ay >= az)
            return my;
        return mz;
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

    /// <summary>
    /// True if the mesh has a triangle on room-local plane `axis`=`plane`
    /// whose room-space normal points along `sign` (+1 or -1).
    /// </summary>
    public static bool MeshHasRoomFace(Transform mesh, Transform room, int axis, float plane, float sign)
    {
        if (mesh == null || room == null)
            return false;
        MeshFilter mf = mesh.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
            return false;
        Mesh m = mf.sharedMesh;
        Vector3[] v = m.vertices;
        int[] t = m.triangles;
        const float planeEps = 0.02f;
        const float nDot = 0.5f;
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            Vector3 a = room.InverseTransformPoint(mesh.TransformPoint(v[t[i]]));
            Vector3 b = room.InverseTransformPoint(mesh.TransformPoint(v[t[i + 1]]));
            Vector3 c = room.InverseTransformPoint(mesh.TransformPoint(v[t[i + 2]]));
            Vector3 mid = (a + b + c) / 3f;
            float p = axis == 0 ? mid.x : (axis == 1 ? mid.y : mid.z);
            if (Mathf.Abs(p - plane) > planeEps)
                continue;
            Vector3 n = Vector3.Cross(b - a, c - a);
            n = n.normalized;
            float ns = axis == 0 ? n.x : (axis == 1 ? n.y : n.z);
            if (ns * sign > nDot)
                return true;
        }
        return false;
    }

}
