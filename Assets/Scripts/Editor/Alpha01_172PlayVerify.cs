using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 172 — Play-mode shared wall + ShopDoorLeaf between two Alpha01 rooms.
/// CameraFollow stays ON. Does not save Alpha01 or GridFoundation.
/// Flag: Temp/alpha01_172_play.flag
/// Report: Temp/alpha01_172/play.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_172PlayVerify
{
    const string FlagPath = "Temp/alpha01_172_play.flag";
    const string ReportPath = "Temp/alpha01_172/play.txt";
    const string ShotDir = "Temp/alpha01_172";
    const string ScenePath = Alpha01SceneSetup.ScenePath;
    const string PendingKey = "Alpha01_172Play.pending";

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
        public DirCase(string n, int x, int z) { name = n; dx = x; dz = z; }
    }

    static readonly DirCase[] Dirs =
    {
        new DirCase("PosX", RoomSlotGrid.SlotCells, 0),
        new DirCase("NegX", -RoomSlotGrid.SlotCells, 0),
        new DirCase("PosZ", 0, RoomSlotGrid.SlotCells),
        new DirCase("NegZ", 0, -RoomSlotGrid.SlotCells)
    };

    static Alpha01_172PlayVerify()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Scenes/172 Play-verify Alpha01 Shared Wall")]
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
        sLog.AppendLine("=== Ticket 172 Play verify ===");
        sLog.AppendLine("does not save Alpha01 or GridFoundation");
        sLog.AppendLine("CameraFollow stays ON");
        sLog.AppendLine("shared wall uses ShopDoorLeaf x2");
        sLog.AppendLine("all four neighbour directions");
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
            sLog.AppendLine("first TryPlace=" + first + " slot=" + sAnchor);
            Check("isolated no shared", CountShared() == 0);
        }

        if (sWait == 16)
            RunDir(0);
        if (sWait == 24)
            RunDir(1);
        if (sWait == 32)
            RunDir(2);
        if (sWait == 40)
            RunDir(3);

        if (sWait == 46)
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

    static void RunDir(int i)
    {
        DirCase d = Dirs[i];
        sLog.AppendLine("--- " + d.name + " ---");
        Wipe();
        RoomPlacement place = Object.FindAnyObjectByType<RoomPlacement>();
        if (place == null)
        {
            Fail("FAIL missing RoomPlacement");
            return;
        }
        GridCoord neighbor = new GridCoord(sAnchor.x + d.dx, sAnchor.z + d.dz);
        bool a = PlaceTracked(place, sAnchor);
        bool b = PlaceTracked(place, neighbor);
        sLog.AppendLine("origin=" + a + " neighbor=" + b + " " + neighbor);
        Check(d.name + " placed", a && b);
        Check(d.name + " shared count", CountShared() == 1);

        SharedWallVisibility shared = Object.FindAnyObjectByType<SharedWallVisibility>();
        if (shared == null)
        {
            Check(d.name + " shared exists", false);
            return;
        }
        int leaves = shared.GetComponentsInChildren<ShopDoorLeaf>(true).Length;
        sLog.AppendLine("ShopDoorLeaf=" + leaves);
        Check(d.name + " saloon pair", leaves == 2);

        Transform wallA = FindNamed(shared.transform, "Wall_Shared_A");
        Check(d.name + " shared piece", wallA != null);
        float boundary = d.dx != 0
            ? Mathf.Min(sAnchor.x, neighbor.x) + 8f
            : Mathf.Min(sAnchor.z, neighbor.z) + 8f;
        if (wallA != null)
        {
            float got = d.dx != 0 ? wallA.position.x : wallA.position.z;
            sLog.AppendLine("boundary got=" + got + " expect=" + boundary);
            Check(d.name + " on boundary", Mathf.Abs(got - boundary) < 0.02f);
        }

        Transform origin = FindRoom(sAnchor);
        Transform other = FindRoom(neighbor);
        Check(d.name + " rooms found", origin != null && other != null);
        if (origin == null || other == null)
            return;

        if (d.dx > 0)
            Check(d.name + " facing hidden", Hidden(origin, "Wall_Right") && Hidden(other, "Wall_Left"));
        else if (d.dx < 0)
            Check(d.name + " facing hidden", Hidden(origin, "Wall_Left") && Hidden(other, "Wall_Right"));
        else if (d.dz > 0)
            Check(d.name + " facing hidden", Hidden(origin, "Wall_Back") && Hidden(other, "Wall_Front_Left"));
        else
            Check(d.name + " facing hidden", Hidden(origin, "Wall_Front_Left") && Hidden(other, "Wall_Back"));

        if (d.dz >= 0)
            Check(d.name + " origin front kept", !Hidden(origin, "Wall_Front_Left"));
        else
            Check(d.name + " origin front hidden", Hidden(origin, "Wall_Front_Left"));

        CheckSlab(d.name, origin, "RoomFloor", d.dx, d.dz);
        CheckSlab(d.name + " ceil", origin, "RoomCeiling", d.dx, d.dz);
        Check(d.name + " no filler", GameObject.Find("FloorFill_A") == null);

        Check(d.name + " no join stubs",
            FindNamed(origin, "Wall_Join_Front") == null
            && FindNamed(origin, "Wall_Join_Back") == null
            && FindNamed(origin, "Wall_Join_Left") == null
            && FindNamed(origin, "Wall_Join_Right") == null
            && FindNamed(other, "Wall_Join_Front") == null
            && FindNamed(other, "Wall_Join_Left") == null);

        CaptureCam(d.name.ToLowerInvariant());
        CaptureGameView(d.name.ToLowerInvariant() + "_gv");
    }

    static void CheckSlab(string label, Transform room, string meshName, int dx, int dz)
    {
        Transform mesh = FindNamed(room, meshName);
        Renderer rend = mesh != null ? mesh.GetComponent<Renderer>() : null;
        if (rend == null)
        {
            Check(label + " " + meshName, false);
            return;
        }
        RoomSlotAnchor.RoomLocalAabb(room, rend.bounds, out Vector3 min, out Vector3 max);
        sLog.AppendLine(label + " " + meshName + " local=" + min + " .. " + max);
        if (dx > 0)
            Check(label + " " + meshName + " maxX", Mathf.Abs(max.x - 7.85f) < 0.05f);
        if (dx < 0)
            Check(label + " " + meshName + " minX", Mathf.Abs(min.x - 0.15f) < 0.05f);
        if (dz > 0)
            Check(label + " " + meshName + " maxZ", Mathf.Abs(max.z - 7.85f) < 0.05f);
        if (dz < 0)
            Check(label + " " + meshName + " minZ", Mathf.Abs(min.z - 0.15f) < 0.05f);
        if (dx != 0)
            Check(label + " " + meshName + " Z not shrunk", Mathf.Abs((max.z - min.z) - 7.4f) < 0.15f);
        if (dz != 0)
            Check(label + " " + meshName + " X not shrunk", Mathf.Abs((max.x - min.x) - 7.4f) < 0.15f);
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
        GridSystem grid = GridSystem.Instance;
        Vector3 expect = grid.CellToWorldCorner(anchor);
        expect.y = 0f;
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
        sLog.AppendLine("RESULT=OK four-dir verify inspect Temp/alpha01_172/*.png");
        WriteReport();
        Debug.Log("[Alpha01_172PlayVerify]\n" + sLog);
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
        Debug.LogError("[Alpha01_172PlayVerify] " + msg);
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
