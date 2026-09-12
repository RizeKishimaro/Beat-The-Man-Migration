using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 173 — diagnose double-wall / gap at connected edges.
/// All four neighbour directions. Does not save Alpha01.
/// Flag: Temp/alpha01_173_join.flag
/// Report: Temp/alpha01_173/join.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_173JoinDump
{
    const string FlagPath = "Temp/alpha01_173_join.flag";
    const string ReportPath = "Temp/alpha01_173/join.txt";
    const string ShotDir = "Temp/alpha01_173";
    const string ScenePath = Alpha01SceneSetup.ScenePath;
    const string PendingKey = "Alpha01_173Join.pending";
    const float Ext = 6f;

    static int sPhase;
    static int sWait;
    static int sEnterFrame = -1;
    static int sLastPlayFrame = -1;
    static StringBuilder sLog;
    static GridCoord sAnchor;
    static int sCheckFail;
    static readonly GridCoord[] sOccupied = new GridCoord[8];
    static int sOccupiedN;

    struct DirCase
    {
        public string name;
        public int dx;
        public int dz;
        public string originFacing;
        public string neighborFacing;
        public DirCase(string n, int x, int z, string of, string nf)
        {
            name = n; dx = x; dz = z; originFacing = of; neighborFacing = nf;
        }
    }

    static readonly DirCase[] Dirs =
    {
        new DirCase("PosX", RoomSlotGrid.SlotCells, 0, "Wall_Right", "Wall_Left"),
        new DirCase("NegX", -RoomSlotGrid.SlotCells, 0, "Wall_Left", "Wall_Right"),
        new DirCase("PosZ", 0, RoomSlotGrid.SlotCells, "Wall_Back", "Wall_Front_Left"),
        new DirCase("NegZ", 0, -RoomSlotGrid.SlotCells, "Wall_Front_Left", "Wall_Back")
    };

    static Alpha01_173JoinDump()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Scenes/173 Dump Join Walls")]
    public static void Menu()
    {
        File.WriteAllText(Full(FlagPath), "run\n");
    }

    static void OnPlay(PlayModeStateChange c)
    {
        if (c != PlayModeStateChange.EnteredEditMode)
            return;
        sPhase = 0;
        sEnterFrame = -1;
        sWait = 0;
        sLastPlayFrame = -1;
    }

    static void Poll()
    {
        if (sPhase == 1)
        {
            TickPlay();
            return;
        }
        if (EditorApplication.isCompiling)
            return;

        bool flag = File.Exists(Full(FlagPath));
        bool pending = SessionState.GetBool(PendingKey, false);
        if (pending && EditorApplication.isPlaying)
        {
            if (sLog == null)
            {
                sLog = new StringBuilder();
                if (File.Exists(Full(ReportPath)))
                    sLog.Append(File.ReadAllText(Full(ReportPath)));
                sLog.AppendLine("entered Play");
            }
            if (sEnterFrame < 0)
            {
                sEnterFrame = Time.frameCount;
                sLastPlayFrame = -1;
                sWait = 0;
                FocusGameView();
            }
            sPhase = 1;
            return;
        }

        if (!flag)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        try { File.Delete(Full(FlagPath)); } catch { }
        Begin();
    }

    static void Begin()
    {
        sLog = new StringBuilder();
        sLog.AppendLine("=== Ticket 173 join verify (plan v2) ===");
        sOccupiedN = 0;
        sCheckFail = 0;

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            if (!File.Exists(Full(ScenePath)))
            {
                Fail("FAIL missing " + ScenePath);
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        SessionState.SetBool(PendingKey, true);
        WriteReport();
        EditorApplication.isPlaying = true;
    }

    static void TickPlay()
    {
        if (!EditorApplication.isPlaying)
            return;
        int f = Time.frameCount;
        if (f == sLastPlayFrame)
            return;
        sLastPlayFrame = f;
        sWait++;

        if (sWait == 8)
        {
            RoomPlacement place = Object.FindAnyObjectByType<RoomPlacement>();
            GridSystem grid = GridSystem.Instance;
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            if (place == null || grid == null || player == null)
            {
                Fail("FAIL missing placement/grid/player");
                return;
            }
            sAnchor = RoomSlotGrid.SlotAnchorFromWorld(grid, player.transform.position);
            sLog.AppendLine("origin slot=" + sAnchor);
        }

        if (sWait == 12)
            DumpDir(0);
        if (sWait == 18)
            DumpDir(1);
        if (sWait == 24)
            DumpDir(2);
        if (sWait == 30)
            DumpDir(3);

        if (sWait == 34)
        {
            Wipe();
            if (sCheckFail > 0)
            {
                Fail("FAIL " + sCheckFail + " checks");
                return;
            }
            sLog.AppendLine("RESULT=OK four-dir corner gaps");
            WriteReport();
            Debug.Log("[Alpha01_173JoinDump]\n" + sLog);
            SessionState.SetBool(PendingKey, false);
            sPhase = 0;
            EditorApplication.isPlaying = false;
        }

        if (sWait > 120)
            Fail("FAIL timeout");
    }

    static void DumpDir(int i)
    {
        DirCase d = Dirs[i];
        sLog.AppendLine();
        sLog.AppendLine("======== " + d.name + " ========");
        Wipe();
        RoomPlacement place = Object.FindAnyObjectByType<RoomPlacement>();
        GridSystem grid = GridSystem.Instance;
        GridCoord neighbor = new GridCoord(sAnchor.x + d.dx, sAnchor.z + d.dz);
        bool a = PlaceTracked(place, sAnchor);
        bool b = PlaceTracked(place, neighbor);
        sLog.AppendLine("placed origin=" + a + " neighbor=" + b + " " + neighbor);

        Transform origin = FindRoom(sAnchor);
        Transform other = FindRoom(neighbor);
        int sharedN = Object.FindObjectsByType<SharedWallVisibility>(FindObjectsInactive.Exclude).Length;
        sLog.AppendLine("shared count=" + sharedN);
        sLog.AppendLine("GetOccupant neighbor=" + (grid.GetOccupant(neighbor) != null));
        sLog.AppendLine("GetOccupant origin=" + (grid.GetOccupant(sAnchor) != null));

        if (origin == null || other == null)
        {
            sLog.AppendLine("FAIL rooms not found");
            return;
        }

        RoomSlotAnchor oa = origin.GetComponent<RoomSlotAnchor>();
        RoomSlotAnchor na = other.GetComponent<RoomSlotAnchor>();
        sLog.AppendLine("origin mask=" + (oa != null ? oa.ConnectedMask.ToString() : "null")
            + " neighbor mask=" + (na != null ? na.ConnectedMask.ToString() : "null"));

        DumpNamed(origin, "origin", d.originFacing);
        DumpNamed(other, "neighbor", d.neighborFacing);
        if (d.dz != 0)
        {
            DumpNamed(origin, "origin", "Wall_Front_Right");
            DumpNamed(origin, "origin", "Wall_Front_Lintel");
            DumpNamed(other, "neighbor", "Wall_Front_Right");
            DumpNamed(other, "neighbor", "Wall_Front_Lintel");
        }

        DumpNamed(origin, "origin", "Wall_Front_Left");
        DumpNamed(origin, "origin", "Wall_Front_Right");
        DumpNamed(origin, "origin", "Wall_Back");
        DumpNamed(origin, "origin", "Wall_Left");
        DumpNamed(origin, "origin", "Wall_Right");
        DumpNamed(origin, "origin", "RoomFloor");
        DumpNamed(other, "neighbor", "Wall_Front_Left");
        DumpNamed(other, "neighbor", "Wall_Front_Right");
        DumpNamed(other, "neighbor", "Wall_Back");
        DumpNamed(other, "neighbor", "Wall_Left");
        DumpNamed(other, "neighbor", "Wall_Right");
        DumpNamed(other, "neighbor", "RoomFloor");

        float boundary = d.dx != 0
            ? Mathf.Min(sAnchor.x, neighbor.x) + Ext
            : Mathf.Min(sAnchor.z, neighbor.z) + Ext;
        sLog.AppendLine("boundary=" + boundary + " axis=" + (d.dx != 0 ? "X" : "Z"));

        DumpNearSeam(origin, "origin", d.dx != 0, boundary);
        DumpNearSeam(other, "neighbor", d.dx != 0, boundary);

        SharedWallVisibility shared = Object.FindAnyObjectByType<SharedWallVisibility>();
        if (shared != null)
        {
            Renderer[] pieces = shared.GetComponentsInChildren<Renderer>(true);
            for (int p = 0; p < pieces.Length; p++)
            {
                Renderer r = pieces[p];
                Bounds wb = r.bounds;
                sLog.AppendLine("SHARED " + r.name
                    + " active=" + r.gameObject.activeInHierarchy
                    + " rend=" + r.enabled
                    + " world=" + wb.min + " .. " + wb.max);
            }
        }

        MeasureGap(origin, other, d, boundary);
        CheckSeam(d, origin, other, boundary, shared);
        CaptureJoin(d.name.ToLowerInvariant(), origin, other, d);
    }

    static void DumpNamed(Transform room, string label, string name)
    {
        Transform t = FindNamed(room, name);
        if (t == null)
        {
            sLog.AppendLine(label + " " + name + " MISSING");
            return;
        }
        Renderer rend = t.GetComponent<Renderer>();
        string aabb = "";
        if (rend != null)
        {
            RoomSlotAnchor.RoomLocalAabb(room, rend.bounds, out Vector3 min, out Vector3 max);
            aabb = " local=" + min + " .. " + max
                + " world=" + rend.bounds.min + " .. " + rend.bounds.max;
        }
        sLog.AppendLine(label + " " + name
            + " activeSelf=" + t.gameObject.activeSelf
            + " activeHier=" + t.gameObject.activeInHierarchy
            + " rend=" + (rend != null && rend.enabled)
            + aabb);
    }

    static void DumpNearSeam(Transform room, string label, bool axisX, float boundary)
    {
        var rends = room.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            Renderer r = rends[i];
            if (r == null)
                continue;
            Bounds b = r.bounds;
            float dist = axisX
                ? Mathf.Min(Mathf.Abs(b.min.x - boundary), Mathf.Abs(b.max.x - boundary))
                : Mathf.Min(Mathf.Abs(b.min.z - boundary), Mathf.Abs(b.max.z - boundary));
            bool straddles = axisX
                ? b.min.x < boundary && b.max.x > boundary
                : b.min.z < boundary && b.max.z > boundary;
            if (dist > 0.4f && !straddles)
                continue;
            sLog.AppendLine("SEAM " + label + " " + r.name
                + " active=" + r.gameObject.activeInHierarchy
                + " rend=" + r.enabled
                + " dist=" + dist
                + " straddle=" + straddles
                + " world=" + b.min + " .. " + b.max);
        }
    }

    static void MeasureGap(Transform origin, Transform other, DirCase d, float boundary)
    {
        Renderer oFace = RendererOf(origin, d.originFacing);
        Renderer nFace = RendererOf(other, d.neighborFacing);
        if (oFace == null || nFace == null)
        {
            sLog.AppendLine("GAP facing renderer missing");
            return;
        }
        if (d.dx != 0)
        {
            float oOuter = d.dx > 0 ? oFace.bounds.max.x : oFace.bounds.min.x;
            float nOuter = d.dx > 0 ? nFace.bounds.min.x : nFace.bounds.max.x;
            float gap = d.dx > 0 ? nOuter - oOuter : oOuter - nOuter;
            sLog.AppendLine("GAP facing-outers=" + gap
                + " originOuter=" + oOuter + " neighborOuter=" + nOuter
                + " (0=touching, >0=gap, <0=overlap)");
            sLog.AppendLine("GAP originInnerToBoundary="
                + (d.dx > 0 ? boundary - oFace.bounds.min.x : oFace.bounds.max.x - boundary));
        }
        else
        {
            float oOuter = d.dz > 0 ? oFace.bounds.max.z : oFace.bounds.min.z;
            float nOuter = d.dz > 0 ? nFace.bounds.min.z : nFace.bounds.max.z;
            float gap = d.dz > 0 ? nOuter - oOuter : oOuter - nOuter;
            sLog.AppendLine("GAP facing-outers=" + gap
                + " originOuter=" + oOuter + " neighborOuter=" + nOuter
                + " (0=touching, >0=gap, <0=overlap)");
        }
        Transform oT = FindNamed(origin, d.originFacing);
        Transform nT = FindNamed(other, d.neighborFacing);
        sLog.AppendLine("HIDE origin." + d.originFacing + " activeHier="
            + (oT != null && oT.gameObject.activeInHierarchy));
        sLog.AppendLine("HIDE neighbor." + d.neighborFacing + " activeHier="
            + (nT != null && nT.gameObject.activeInHierarchy));
    }

    static void CheckSeam(
        DirCase d, Transform origin, Transform other, float boundary,
        SharedWallVisibility shared)
    {
        const float Tol = 0.02f;
        Check(d.name + " placed", origin != null && other != null);
        Check(d.name + " shared exists", shared != null);
        Check(d.name + " hide origin." + d.originFacing,
            FindNamed(origin, d.originFacing) != null
            && !FindNamed(origin, d.originFacing).gameObject.activeInHierarchy);
        Check(d.name + " hide neighbor." + d.neighborFacing,
            FindNamed(other, d.neighborFacing) != null
            && !FindNamed(other, d.neighborFacing).gameObject.activeInHierarchy);
        if (shared == null)
            return;

        Bounds enc = EncapsulateShared(shared);
        if (d.dx != 0)
        {
            float along = enc.max.z - enc.min.z;
            float thick = enc.max.x - enc.min.x;
            float center = (enc.min.x + enc.max.x) * 0.5f;
            sLog.AppendLine(d.name + " shared alongZ=" + along + " thickX=" + thick + " centerX=" + center);
            Check(d.name + " shared along 6", Mathf.Abs(along - Ext) < Tol);
            Check(d.name + " shared thick 0.3", Mathf.Abs(thick - 0.3f) < Tol);
            Check(d.name + " shared on boundary", Mathf.Abs(center - boundary) < Tol);

            if (d.dx > 0)
            {
                CheckAcross(d.name + " origin Front_Right", RendererOf(origin, "Wall_Front_Right"), enc, true, true);
                CheckAcross(d.name + " origin Back", RendererOf(origin, "Wall_Back"), enc, true, true);
                CheckAcross(d.name + " origin floor", RendererOf(origin, "RoomFloor"), enc, true, true);
                CheckAcross(d.name + " neighbor Front_Left", RendererOf(other, "Wall_Front_Left"), enc, true, false);
                CheckAcross(d.name + " neighbor Back", RendererOf(other, "Wall_Back"), enc, true, false);
                CheckAcross(d.name + " neighbor floor", RendererOf(other, "RoomFloor"), enc, true, false);
            }
            else
            {
                CheckAcross(d.name + " origin Front_Left", RendererOf(origin, "Wall_Front_Left"), enc, true, false);
                CheckAcross(d.name + " origin Back", RendererOf(origin, "Wall_Back"), enc, true, false);
                CheckAcross(d.name + " origin floor", RendererOf(origin, "RoomFloor"), enc, true, false);
                CheckAcross(d.name + " neighbor Front_Right", RendererOf(other, "Wall_Front_Right"), enc, true, true);
                CheckAcross(d.name + " neighbor Back", RendererOf(other, "Wall_Back"), enc, true, true);
                CheckAcross(d.name + " neighbor floor", RendererOf(other, "RoomFloor"), enc, true, true);
            }
        }
        else
        {
            float along = enc.max.x - enc.min.x;
            float thick = enc.max.z - enc.min.z;
            float center = (enc.min.z + enc.max.z) * 0.5f;
            sLog.AppendLine(d.name + " shared alongX=" + along + " thickZ=" + thick + " centerZ=" + center);
            Check(d.name + " shared along 6", Mathf.Abs(along - Ext) < Tol);
            Check(d.name + " shared thick 0.3", Mathf.Abs(thick - 0.3f) < Tol);
            Check(d.name + " shared on boundary", Mathf.Abs(center - boundary) < Tol);

            if (d.dz > 0)
            {
                CheckAcross(d.name + " origin Left", RendererOf(origin, "Wall_Left"), enc, false, true);
                CheckAcross(d.name + " origin Right", RendererOf(origin, "Wall_Right"), enc, false, true);
                CheckAcross(d.name + " origin floor", RendererOf(origin, "RoomFloor"), enc, false, true);
                CheckAcross(d.name + " neighbor Left", RendererOf(other, "Wall_Left"), enc, false, false);
                CheckAcross(d.name + " neighbor Right", RendererOf(other, "Wall_Right"), enc, false, false);
                CheckAcross(d.name + " neighbor floor", RendererOf(other, "RoomFloor"), enc, false, false);
            }
            else
            {
                CheckAcross(d.name + " origin Left", RendererOf(origin, "Wall_Left"), enc, false, false);
                CheckAcross(d.name + " origin Right", RendererOf(origin, "Wall_Right"), enc, false, false);
                CheckAcross(d.name + " origin floor", RendererOf(origin, "RoomFloor"), enc, false, false);
                CheckAcross(d.name + " neighbor Left", RendererOf(other, "Wall_Left"), enc, false, true);
                CheckAcross(d.name + " neighbor Right", RendererOf(other, "Wall_Right"), enc, false, true);
                CheckAcross(d.name + " neighbor floor", RendererOf(other, "RoomFloor"), enc, false, true);
            }
        }

        CheckSharedHeight(d.name, origin, enc);
        CheckOmittedCaps(d, origin, other, enc);
    }

    static void CheckSharedHeight(string dir, Transform origin, Bounds shared)
    {
        const float Tol = 0.02f;
        Renderer wall = RendererOf(origin, "Wall_Back");
        if (wall == null)
            wall = RendererOf(origin, "Wall_Left");
        float wallY = wall != null ? wall.bounds.max.y : 3f;
        sLog.AppendLine(dir + " shared y=" + shared.min.y + ".." + shared.max.y
            + " wallMaxY=" + wallY);
        Check(dir + " shared y0=0", Mathf.Abs(shared.min.y) < Tol);
        Check(dir + " shared height matches wall", Mathf.Abs(shared.max.y - wallY) < Tol);
    }

    static void CheckOmittedCaps(DirCase d, Transform origin, Transform other, Bounds shared)
    {
        if (d.dx > 0)
        {
            CheckNoCapToward(d.name + " origin Front_Right", origin, "Wall_Front_Right", shared, true, true);
            CheckNoCapToward(d.name + " origin Back", origin, "Wall_Back", shared, true, true);
            CheckNoCapToward(d.name + " neighbor Front_Left", other, "Wall_Front_Left", shared, true, false);
            CheckNoCapToward(d.name + " neighbor Back", other, "Wall_Back", shared, true, false);
        }
        else if (d.dx < 0)
        {
            CheckNoCapToward(d.name + " origin Front_Left", origin, "Wall_Front_Left", shared, true, false);
            CheckNoCapToward(d.name + " origin Back", origin, "Wall_Back", shared, true, false);
            CheckNoCapToward(d.name + " neighbor Front_Right", other, "Wall_Front_Right", shared, true, true);
            CheckNoCapToward(d.name + " neighbor Back", other, "Wall_Back", shared, true, true);
        }
        else if (d.dz > 0)
        {
            CheckNoCapToward(d.name + " origin Left", origin, "Wall_Left", shared, false, true);
            CheckNoCapToward(d.name + " origin Right", origin, "Wall_Right", shared, false, true);
            CheckNoCapToward(d.name + " neighbor Left", other, "Wall_Left", shared, false, false);
            CheckNoCapToward(d.name + " neighbor Right", other, "Wall_Right", shared, false, false);
        }
        else
        {
            CheckNoCapToward(d.name + " origin Left", origin, "Wall_Left", shared, false, false);
            CheckNoCapToward(d.name + " origin Right", origin, "Wall_Right", shared, false, false);
            CheckNoCapToward(d.name + " neighbor Left", other, "Wall_Left", shared, false, true);
            CheckNoCapToward(d.name + " neighbor Right", other, "Wall_Right", shared, false, true);
        }
    }

    static void CheckNoCapToward(
        string label, Transform room, string wallName, Bounds shared, bool axisX, bool originSide)
    {
        Transform wall = FindNamed(room, wallName);
        if (wall == null)
        {
            Check(label + " omitted cap", false);
            return;
        }
        float planeW = originSide
            ? (axisX ? shared.min.x : shared.min.z)
            : (axisX ? shared.max.x : shared.max.z);
        Vector3 worldPt = axisX ? new Vector3(planeW, 0f, 0f) : new Vector3(0f, 0f, planeW);
        Vector3 roomPt = room.InverseTransformPoint(worldPt);
        float plane = axisX ? roomPt.x : roomPt.z;
        float sign = originSide ? 1f : -1f;
        int axis = axisX ? 0 : 2;
        bool has = RoomSlotAnchor.MeshHasRoomFace(wall, room, axis, plane, sign);
        sLog.AppendLine(label + " hasCap=" + has + " plane=" + plane);
        Check(label + " omitted cap", !has);
    }

    /// <summary>
    /// originSide=true: this mesh is on the negative side of the seam (should end at shared.min).
    /// originSide=false: mesh on the positive side (should start at shared.max).
    /// </summary>
    static void CheckAcross(string label, Renderer mesh, Bounds shared, bool axisX, bool originSide)
    {
        const float Tol = 0.02f;
        if (mesh == null)
        {
            Check(label + " exists", false);
            return;
        }
        Bounds b = mesh.bounds;
        float meshEnd = originSide
            ? (axisX ? b.max.x : b.max.z)
            : (axisX ? b.min.x : b.min.z);
        float sharedFace = originSide
            ? (axisX ? shared.min.x : shared.min.z)
            : (axisX ? shared.max.x : shared.max.z);
        float gap = originSide ? sharedFace - meshEnd : meshEnd - sharedFace;
        float overlap = originSide
            ? meshEnd - sharedFace
            : sharedFace - meshEnd;
        sLog.AppendLine(label + " gap=" + gap + " overlap=" + overlap
            + " meshEnd=" + meshEnd + " sharedFace=" + sharedFace);
        Check(label + " zero gap", Mathf.Abs(gap) < Tol);
        Check(label + " no overlap", overlap < Tol);
    }

    static Bounds EncapsulateShared(SharedWallVisibility shared)
    {
        Bounds enc = new Bounds();
        bool first = true;
        string[] names = { "Wall_Shared_A", "Wall_Shared_B", "Wall_Shared_Lintel" };
        for (int i = 0; i < names.Length; i++)
        {
            Transform t = FindNamed(shared.transform, names[i]);
            Renderer r = t != null ? t.GetComponent<Renderer>() : null;
            if (r == null)
                continue;
            if (first)
            {
                enc = r.bounds;
                first = false;
            }
            else
                enc.Encapsulate(r.bounds);
        }
        return enc;
    }

    static void Check(string name, bool ok)
    {
        if (!ok)
            sCheckFail++;
        sLog.AppendLine("CHECK " + name + ": " + ok);
    }

    static Renderer RendererOf(Transform room, string name)
    {
        Transform t = FindNamed(room, name);
        return t != null ? t.GetComponent<Renderer>() : null;
    }

    static void CaptureJoin(string name, Transform origin, Transform other, DirCase d)
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        Vector3 mid = (origin.position + other.position) * 0.5f;
        mid.y = 0f;
        Vector3 oldPos = cam.transform.position;
        Quaternion oldRot = cam.transform.rotation;
        cam.transform.position = mid + new Vector3(-6f, 10f, -6f);
        cam.transform.LookAt(mid + Vector3.up * 1.2f);
        Directory.CreateDirectory(Full(ShotDir));
        Vector2 gv = Handles.GetMainGameViewSize();
        int w = Mathf.Max(16, Mathf.RoundToInt(gv.x));
        int h = Mathf.Max(16, Mathf.RoundToInt(gv.y));
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        string path = Full(ShotDir + "/join_" + name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        sLog.AppendLine("shot join_" + name + " bytes=" + new FileInfo(path).Length);
        Object.DestroyImmediate(tex);
        cam.transform.SetPositionAndRotation(oldPos, oldRot);
    }

    static bool PlaceTracked(RoomPlacement place, GridCoord anchor)
    {
        bool ok = place.TryPlace(anchor);
        if (ok && sOccupiedN < sOccupied.Length)
        {
            sOccupied[sOccupiedN] = anchor;
            sOccupiedN++;
        }
        return ok;
    }

    static void Wipe()
    {
        GridSystem grid = GridSystem.Instance;
        var slots = Object.FindObjectsByType<RoomSlotAnchor>(FindObjectsInactive.Exclude);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                Object.DestroyImmediate(slots[i].gameObject);
        }
        var shared = Object.FindObjectsByType<SharedWallVisibility>(FindObjectsInactive.Exclude);
        for (int i = 0; i < shared.Length; i++)
        {
            if (shared[i] != null)
                Object.DestroyImmediate(shared[i].gameObject);
        }
        if (grid != null)
        {
            for (int i = 0; i < sOccupiedN; i++)
            {
                grid.Release(grid.FootprintCells(
                    sOccupied[i], new Vector2Int(RoomSlotGrid.SlotCells, RoomSlotGrid.SlotCells)));
            }
        }
        sOccupiedN = 0;
        RoomAdjacency.ResetConnectedEdges();
    }

    static Transform FindRoom(GridCoord anchor)
    {
        foreach (var slot in Object.FindObjectsByType<RoomSlotAnchor>(FindObjectsInactive.Exclude))
        {
            if (slot.Anchor.Equals(anchor))
                return slot.transform;
        }
        return null;
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

    static void Fail(string msg)
    {
        if (sLog == null)
            sLog = new StringBuilder();
        sLog.AppendLine(msg);
        sLog.AppendLine("RESULT=FAIL");
        WriteReport();
        SessionState.SetBool(PendingKey, false);
        sPhase = 0;
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static void FocusGameView()
    {
        System.Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return;
        EditorWindow.GetWindow(gvType, false, "Game", true).Focus();
    }

    static void WriteReport()
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, sLog != null ? sLog.ToString() : "");
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }
}
