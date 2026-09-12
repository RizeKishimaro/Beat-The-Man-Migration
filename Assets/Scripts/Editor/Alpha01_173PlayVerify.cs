using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 173 — Play-mode isolated 6×6 shell + neighbour shared wall.
/// CameraFollow stays ON. Does not save Alpha01 or GridFoundation.
/// Flag: Temp/alpha01_173_play.flag
/// Report: Temp/alpha01_173/play.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_173PlayVerify
{
    const string FlagPath = "Temp/alpha01_173_play.flag";
    const string ReportPath = "Temp/alpha01_173/play.txt";
    const string ShotDir = "Temp/alpha01_173";
    const string ScenePath = Alpha01SceneSetup.ScenePath;
    const string PendingKey = "Alpha01_173Play.pending";
    const float Ext = 6f;
    const float WallT = 0.3f;
    const float Inner = WallT;
    const float Outer = Ext - WallT;
    const float DoorW = 1.5f;
    const float DoorH = 2.2f;

    static int sPhase;
    static int sWait;
    static int sEnterFrame = -1;
    static int sLastPlayFrame = -1;
    static StringBuilder sLog;
    static GridCoord sAnchor;
    static int sCheckFail;
    static readonly GridCoord[] sOccupied = new GridCoord[8];
    static int sOccupiedN;

    static Alpha01_173PlayVerify()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Scenes/173 Play-verify Alpha01 Flush Walls")]
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
                KeepFollowOn();
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
        sLog.AppendLine("=== Ticket 173 Play verify ===");
        sLog.AppendLine("does not save Alpha01 or GridFoundation");
        sLog.AppendLine("CameraFollow stays ON");
        sLog.AppendLine("isolated flush then +X neighbour");
        sCheckFail = 0;
        sOccupiedN = 0;

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            if (!File.Exists(Full(ScenePath)))
            {
                Fail("FAIL missing " + ScenePath);
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sLog.AppendLine("opened " + ScenePath);
        }

        KeepFollowOn();
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
        KeepFollowOn();

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
            bool first = PlaceTracked(place, sAnchor);
            sLog.AppendLine("isolated TryPlace=" + first + " slot=" + sAnchor);
            Check("isolated placed", first);
            Check("isolated no shared", CountShared() == 0);
            Transform room = FindRoom(sAnchor);
            Check("isolated room found", room != null);
            if (room != null)
                CheckIsolated(room, grid);
            CaptureCam("isolated");
            CaptureGameView("isolated_gv");
        }

        if (sWait == 16)
        {
            RoomPlacement place = Object.FindAnyObjectByType<RoomPlacement>();
            if (place == null)
            {
                Fail("FAIL missing RoomPlacement");
                return;
            }
            GridCoord neighbor = new GridCoord(sAnchor.x + RoomSlotGrid.SlotCells, sAnchor.z);
            bool a = PlaceTracked(place, neighbor);
            sLog.AppendLine("neighbor TryPlace=" + a + " " + neighbor);
            Check("PosX placed", a);
            Check("PosX shared count", CountShared() == 1);

            SharedWallVisibility shared = Object.FindAnyObjectByType<SharedWallVisibility>();
            if (shared == null)
            {
                Check("PosX shared exists", false);
            }
            else
            {
                int leaves = shared.GetComponentsInChildren<ShopDoorLeaf>(true).Length;
                sLog.AppendLine("ShopDoorLeaf=" + leaves);
                Check("PosX saloon pair", leaves == 2);
                Transform wallA = FindNamed(shared.transform, "Wall_Shared_A");
                Check("PosX shared piece", wallA != null);
                float boundary = Mathf.Min(sAnchor.x, neighbor.x) + Ext;
                if (wallA != null)
                {
                    sLog.AppendLine("boundary got=" + wallA.position.x + " expect=" + boundary);
                    Check("PosX on boundary", Mathf.Abs(wallA.position.x - boundary) < 0.02f);
                }
            }

            Transform origin = FindRoom(sAnchor);
            Transform other = FindRoom(neighbor);
            Check("PosX rooms found", origin != null && other != null);
            if (origin != null && other != null)
            {
                Check("PosX facing hidden", Hidden(origin, "Wall_Right") && Hidden(other, "Wall_Left"));
                Check("PosX origin front kept", !Hidden(origin, "Wall_Front_Left"));
                CheckSlab(origin, "RoomFloor", 1, 0);
                CheckSlab(origin, "RoomCeiling", 1, 0);
                Check("PosX no join stubs",
                    FindNamed(origin, "Wall_Join_Front") == null
                    && FindNamed(other, "Wall_Join_Front") == null
                    && FindNamed(origin, "Wall_Join_Left") == null);
                Check("PosX no filler", GameObject.Find("FloorFill_A") == null);
            }
            CaptureCam("neighbor");
            CaptureGameView("neighbor_gv");
        }

        if (sWait == 22)
        {
            Wipe();
            if (sCheckFail > 0)
            {
                Fail("FAIL " + sCheckFail + " checks");
                return;
            }
            Finish();
        }

        if (sWait > 120)
            Fail("FAIL timeout");
    }

    static void CheckIsolated(Transform room, GridSystem grid)
    {
        CheckFace(room, "Wall_Left", 0f, WallT, Inner, Outer, true);
        CheckFace(room, "Wall_Right", Outer, Ext, Inner, Outer, true);
        CheckFace(room, "Wall_Back", 0f, Ext, Outer, Ext, true);
        CheckFace(room, "Wall_Front_Left", 0f, float.NaN, 0f, WallT, false);
        CheckFace(room, "Wall_Front_Right", float.NaN, Ext, 0f, WallT, false);
        CheckFace(room, "Wall_Front_Lintel", float.NaN, float.NaN, 0f, WallT, false);

        Transform fl = FindNamed(room, "Wall_Front_Left");
        Transform fr = FindNamed(room, "Wall_Front_Right");
        Transform lintel = FindNamed(room, "Wall_Front_Lintel");
        Renderer flr = fl != null ? fl.GetComponent<Renderer>() : null;
        Renderer frr = fr != null ? fr.GetComponent<Renderer>() : null;
        Renderer lr = lintel != null ? lintel.GetComponent<Renderer>() : null;
        if (flr != null && frr != null)
        {
            RoomSlotAnchor.RoomLocalAabb(room, flr.bounds, out Vector3 lmin, out Vector3 lmax);
            RoomSlotAnchor.RoomLocalAabb(room, frr.bounds, out Vector3 rmin, out Vector3 rmax);
            float hole = rmin.x - lmax.x;
            sLog.AppendLine("door hole x=" + hole + " leftMax=" + lmax.x + " rightMin=" + rmin.x);
            Check("door width 1.5", Mathf.Abs(hole - DoorW) < 0.05f);
        }
        else
            Check("door width 1.5", false);
        if (lr != null)
        {
            RoomSlotAnchor.RoomLocalAabb(room, lr.bounds, out Vector3 cmin, out Vector3 cmax);
            sLog.AppendLine("lintel y=" + cmin.y + ".." + cmax.y);
            Check("door height 2.2", Mathf.Abs(cmin.y - DoorH) < 0.05f);
        }
        else
            Check("door height 2.2", false);

        CheckSlab(room, "RoomFloor", 0, 0);
        CheckSlab(room, "RoomCeiling", 0, 0);

        RoomSlotAnchor slot = room.GetComponent<RoomSlotAnchor>();
        BoxCollider interior = slot != null ? slot.Interior : null;
        if (interior == null)
            Check("InteriorTrigger", false);
        else
        {
            sLog.AppendLine("interior size=" + interior.size + " center=" + interior.center);
            Check("interior size xz 5.4",
                Mathf.Abs(interior.size.x - 5.4f) < 0.05f
                && Mathf.Abs(interior.size.z - 5.4f) < 0.05f);
            Check("interior center xz 3",
                Mathf.Abs(interior.center.x - 3f) < 0.05f
                && Mathf.Abs(interior.center.z - 3f) < 0.05f);
        }

        CheckHinge(room, "DoorHinge_Left");
        CheckHinge(room, "DoorHinge_Right");

        Vector3 corner = grid.CellToWorldCorner(sAnchor);
        corner.y = 0f;
        Renderer leftR = RendererOf(room, "Wall_Left");
        Renderer rightR = RendererOf(room, "Wall_Right");
        Renderer frontR = RendererOf(room, "Wall_Front_Left");
        Renderer backR = RendererOf(room, "Wall_Back");
        if (leftR == null || rightR == null || frontR == null || backR == null)
        {
            Check("world envelope", false);
            return;
        }
        float dx0 = leftR.bounds.min.x - corner.x;
        float dx1 = rightR.bounds.max.x - (corner.x + Ext);
        float dz0 = frontR.bounds.min.z - corner.z;
        float dz1 = backR.bounds.max.z - (corner.z + Ext);
        sLog.AppendLine("slot corner=" + corner);
        sLog.AppendLine("dashed vs walls dx0=" + dx0 + " dx1=" + dx1 + " dz0=" + dz0 + " dz1=" + dz1);
        Check("walls flush world 0/6",
            Mathf.Abs(dx0) < 0.05f && Mathf.Abs(dx1) < 0.05f
            && Mathf.Abs(dz0) < 0.05f && Mathf.Abs(dz1) < 0.05f);
        if (Mathf.Abs(dx0) >= 0.05f || Mathf.Abs(dx1) >= 0.05f
            || Mathf.Abs(dz0) >= 0.05f || Mathf.Abs(dz1) >= 0.05f)
            sLog.AppendLine("OVERLAY OFFSET — dashed is CellToWorldCorner+6x6; do not guess-fix overlay");
    }

    static void CheckFace(
        Transform room, string name,
        float x0, float x1, float z0, float z1, bool checkX)
    {
        Renderer rend = RendererOf(room, name);
        if (rend == null)
        {
            Check(name + " exists", false);
            return;
        }
        RoomSlotAnchor.RoomLocalAabb(room, rend.bounds, out Vector3 min, out Vector3 max);
        sLog.AppendLine(name + " local=" + min + " .. " + max);
        if (!float.IsNaN(x0))
            Check(name + " min.x", Mathf.Abs(min.x - x0) < 0.05f);
        if (!float.IsNaN(x1))
            Check(name + " max.x", Mathf.Abs(max.x - x1) < 0.05f);
        if (!float.IsNaN(z0))
            Check(name + " min.z", Mathf.Abs(min.z - z0) < 0.05f);
        if (!float.IsNaN(z1))
            Check(name + " max.z", Mathf.Abs(max.z - z1) < 0.05f);
        if (checkX)
            Check(name + " thickness", Mathf.Abs((max.x - min.x) - WallT) < 0.05f
                || Mathf.Abs((max.z - min.z) - WallT) < 0.05f);
    }

    static void CheckSlab(Transform room, string meshName, int dx, int dz)
    {
        Renderer rend = RendererOf(room, meshName);
        if (rend == null)
        {
            Check(meshName, false);
            return;
        }
        RoomSlotAnchor.RoomLocalAabb(room, rend.bounds, out Vector3 min, out Vector3 max);
        sLog.AppendLine(meshName + " local=" + min + " .. " + max);
        if (dx > 0)
            Check(meshName + " maxX", Mathf.Abs(max.x - (Ext - WallT * 0.5f)) < 0.05f);
        else
            Check(meshName + " minX isolated", Mathf.Abs(min.x - Inner) < 0.05f);
        if (dx < 0)
            Check(meshName + " minX", Mathf.Abs(min.x - WallT * 0.5f) < 0.05f);
        else if (dx == 0)
            Check(meshName + " maxX isolated", Mathf.Abs(max.x - Outer) < 0.05f);
        if (dz > 0)
            Check(meshName + " maxZ", Mathf.Abs(max.z - (Ext - WallT * 0.5f)) < 0.05f);
        else
            Check(meshName + " minZ isolated", Mathf.Abs(min.z - Inner) < 0.05f);
        if (dz < 0)
            Check(meshName + " minZ", Mathf.Abs(min.z - WallT * 0.5f) < 0.05f);
        else if (dz == 0)
            Check(meshName + " maxZ isolated", Mathf.Abs(max.z - Outer) < 0.05f);
        if (dx != 0)
            Check(meshName + " Z not shrunk", Mathf.Abs((max.z - min.z) - 5.4f) < 0.15f);
        if (dz != 0)
            Check(meshName + " X not shrunk", Mathf.Abs((max.x - min.x) - 5.4f) < 0.15f);
    }

    static void CheckHinge(Transform room, string name)
    {
        Transform t = FindNamed(room, name);
        if (t == null)
        {
            Check(name, false);
            return;
        }
        float up = Vector3.Dot(t.up, Vector3.up);
        sLog.AppendLine(name + " pos=" + t.position + " upDot=" + up + " localY=" + t.localRotation.eulerAngles);
        Check(name + " upright", up > 0.98f);
        ShopDoorLeaf leaf = t.GetComponent<ShopDoorLeaf>();
        Check(name + " ShopDoorLeaf", leaf != null);
    }

    static Renderer RendererOf(Transform room, string name)
    {
        Transform t = FindNamed(room, name);
        return t != null ? t.GetComponent<Renderer>() : null;
    }

    static bool Hidden(Transform room, string name)
    {
        Transform t = FindNamed(room, name);
        return t != null && !t.gameObject.activeInHierarchy;
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

    static void Check(string name, bool ok)
    {
        if (!ok)
            sCheckFail++;
        sLog.AppendLine("CHECK " + name + ": " + ok);
    }

    static int CountShared()
    {
        return Object.FindObjectsByType<SharedWallVisibility>(FindObjectsInactive.Exclude).Length;
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

    static void KeepFollowOn()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        var follow = cam.GetComponent<CameraFollow>();
        if (follow != null && !follow.enabled)
            follow.enabled = true;
    }

    static void CaptureCam(string name)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            sLog.AppendLine("FAIL capture no Camera.main " + name);
            return;
        }
        Directory.CreateDirectory(Full(ShotDir));
        Vector2 gv = Handles.GetMainGameViewSize();
        int w = Mathf.Max(16, Mathf.RoundToInt(gv.x));
        int h = Mathf.Max(16, Mathf.RoundToInt(gv.y));
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        cam.targetTexture = prev;
        Object.DestroyImmediate(rt);
        string path = Full(ShotDir + "/" + name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        sLog.AppendLine("shot " + name + " " + tex.width + "x" + tex.height + " bytes=" + new FileInfo(path).Length);
        Object.DestroyImmediate(tex);
    }

    static void CaptureGameView(string name)
    {
        FocusGameView();
        Texture2D tex = null;
        try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (System.Exception e)
        {
            sLog.AppendLine("FAIL gameview capture " + name + " " + e.GetType().Name);
            return;
        }
        if (tex == null)
        {
            sLog.AppendLine("FAIL gameview capture " + name + " null");
            return;
        }
        Directory.CreateDirectory(Full(ShotDir));
        string path = Full(ShotDir + "/" + name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        sLog.AppendLine("shot " + name + " " + tex.width + "x" + tex.height + " bytes=" + new FileInfo(path).Length + " source=GameView");
        Object.DestroyImmediate(tex);
    }

    static void Finish()
    {
        KeepFollowOn();
        var follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        sLog.AppendLine("CameraFollow still enabled=" + (follow != null && follow.enabled));
        sLog.AppendLine("scene dirty (must not save)=" + SceneManager.GetActiveScene().isDirty);
        sLog.AppendLine("RESULT=OK inspect Temp/alpha01_173/*.png");
        WriteReport();
        Debug.Log("[Alpha01_173PlayVerify]\n" + sLog);
        sPhase = 0;
        SessionState.SetBool(PendingKey, false);
        sEnterFrame = -1;
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static void Fail(string msg)
    {
        if (sLog == null)
            sLog = new StringBuilder();
        sLog.AppendLine(msg);
        sLog.AppendLine("RESULT=FAIL");
        WriteReport();
        Debug.LogError("[Alpha01_173PlayVerify] " + msg);
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
