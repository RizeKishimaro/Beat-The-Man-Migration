using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Ticket 169 — Play-mode spawn checks on Alpha01.
/// CameraFollow stays ON. Does not save Alpha01 or GridFoundation.
/// Flag: Temp/alpha01_169_play.flag
/// Report: Temp/alpha01_169/play.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_169PlayVerify
{
    const string FlagPath = "Temp/alpha01_169_play.flag";
    const string ReportPath = "Temp/alpha01_169/play.txt";
    const string ShotDir = "Temp/alpha01_169";
    const string ScenePath = Alpha01SceneSetup.ScenePath;
    const string PendingKey = "Alpha01_169Play.pending";

    static int sPhase;
    static int sWait;
    static int sEnterFrame = -1;
    static int sLastPlayFrame = -1;
    static StringBuilder sLog;
    static GridCoord sAnchor;
    static GridCoord sNeighbor;
    static int sAfterClose;
    static int sAfterFirst;
    static Vector3 sFirstOrigin;

    static Alpha01_169PlayVerify()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Scenes/169 Play-verify Alpha01 Room Spawn")]
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
        sLog.AppendLine("=== Ticket 169 Play verify ===");
        sLog.AppendLine("does not save Alpha01 or GridFoundation");
        sLog.AppendLine("CameraFollow stays ON");
        sLog.AppendLine("mesh gap left as-is");

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
            BuildMenuController menu = Object.FindAnyObjectByType<BuildMenuController>();
            RoomPlacement place = Object.FindAnyObjectByType<RoomPlacement>();
            GridSystem grid = GridSystem.Instance;
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            if (menu == null || place == null || grid == null || player == null)
            {
                Fail("FAIL missing menu/placement/grid/player");
                return;
            }
            if (place.buildingPrefab == null)
            {
                Fail("FAIL RoomPlacement.buildingPrefab not assigned");
                return;
            }
            sAnchor = RoomSlotGrid.SlotAnchorFromWorld(grid, player.transform.position);
            sNeighbor = new GridCoord(sAnchor.x + RoomSlotGrid.SlotCells, sAnchor.z);
            sLog.AppendLine("slot=" + sAnchor + " neighbor=" + sNeighbor
                + " prefab=" + place.buildingPrefab.name
                + " buildings=" + CountBuildings());
            menu.Open(sAnchor);
            sLog.AppendLine("opened menu");
        }

        if (sWait == 14)
        {
            BuildMenuController menu = Object.FindAnyObjectByType<BuildMenuController>();
            Button close = FindButton("CloseButton");
            close?.onClick.Invoke();
            sAfterClose = CountBuildings();
            sLog.AppendLine("Close button buildings=" + sAfterClose
                + " menuOpen=" + (menu != null && menu.IsOpen));
            sLog.AppendLine("CHECK close no spawn: " + (sAfterClose == 0));
        }

        if (sWait == 20)
        {
            BuildMenuController menu = Object.FindAnyObjectByType<BuildMenuController>();
            menu.Open(sAnchor);
            Button place = FindButton("DefaultBuildingButton");
            if (place == null)
            {
                Fail("FAIL DefaultBuildingButton missing");
                return;
            }
            place.onClick.Invoke();
            sAfterFirst = CountBuildings();
            sLog.AppendLine("confirm buildings=" + sAfterFirst
                + " menuOpen=" + (menu != null && menu.IsOpen));
            sLog.AppendLine("CHECK spawn one: " + (sAfterFirst == 1));

            GridSystem grid = GridSystem.Instance;
            Vector3 expect = grid.CellToWorldCorner(sAnchor);
            expect.y = 0f;
            Transform spawned = FindSpawned(sAnchor, grid);
            sFirstOrigin = spawned != null ? spawned.position : Vector3.positiveInfinity;
            float d = spawned != null ? Vector3.Distance(spawned.position, expect) : 999f;
            sLog.AppendLine("origin=" + sFirstOrigin + " expect=" + expect + " dist=" + d);
            sLog.AppendLine("CHECK origin at slot corner: " + (d < 0.01f));
            sLog.AppendLine("CHECK slot occupied: " + !RoomSlotGrid.IsSlotFree(grid, sAnchor));
            sLog.AppendLine("CHECK neighbor free: " + RoomSlotGrid.IsSlotFree(grid, sNeighbor));
        }

        if (sWait == 26)
        {
            var prompt = Object.FindAnyObjectByType<RoomSlotPlusPrompt>();
            if (prompt != null)
                prompt.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
            Transform plus = FindPlus();
            sLog.AppendLine("plusActive after spawn=" + (plus != null && plus.gameObject.activeSelf));
            sLog.AppendLine("CHECK plus hidden: " + (plus == null || !plus.gameObject.activeSelf));
            CaptureCam("spawned");
            CaptureGameView("spawned_gv");
        }

        if (sWait == 32)
        {
            RoomPlacement place = Object.FindAnyObjectByType<RoomPlacement>();
            GridSystem grid = GridSystem.Instance;
            bool placed = place.TryPlace(sNeighbor);
            int afterSecond = CountBuildings();
            sLog.AppendLine("neighbor TryPlace=" + placed + " buildings=" + afterSecond);
            sLog.AppendLine("CHECK second spawn: " + (placed && afterSecond == 2));
            Transform second = FindSpawned(sNeighbor, grid);
            if (second != null)
            {
                float gap = Vector3.Distance(
                    new Vector3(sFirstOrigin.x, 0f, sFirstOrigin.z),
                    new Vector3(second.position.x, 0f, second.position.z));
                sLog.AppendLine("origin gap xz=" + gap);
                sLog.AppendLine("CHECK tiles flush 8m: " + (Mathf.Abs(gap - 8f) < 0.01f));
                sLog.AppendLine("CHECK neighbor occupied: " + !RoomSlotGrid.IsSlotFree(grid, sNeighbor));
            }
            CaptureCam("two_rooms");
        }

        if (sWait == 38)
        {
            Finish();
        }

        if (sWait > 120)
            Fail("FAIL timeout");
    }

    static Transform FindSpawned(GridCoord anchor, GridSystem grid)
    {
        Vector3 expect = grid.CellToWorldCorner(anchor);
        expect.y = 0f;
        Transform best = null;
        float bestD = 0.05f;
        foreach (var vis in Object.FindObjectsByType<BuildingInteriorVisibility>(FindObjectsInactive.Exclude))
        {
            Transform root = vis.transform;
            while (root.parent != null && root.parent.GetComponent<RoomPlacement>() == null)
                root = root.parent;
            if (root.parent == null || root.parent.GetComponent<RoomPlacement>() == null)
                continue;
            float d = Vector3.Distance(root.position, expect);
            if (d < bestD)
            {
                bestD = d;
                best = root;
            }
        }
        return best;
    }

    static int CountBuildings()
    {
        int n = 0;
        foreach (var vis in Object.FindObjectsByType<BuildingInteriorVisibility>(FindObjectsInactive.Exclude))
        {
            Transform root = vis.transform;
            while (root.parent != null && root.parent.GetComponent<RoomPlacement>() == null)
                root = root.parent;
            if (root.parent != null && root.parent.GetComponent<RoomPlacement>() != null)
                n++;
        }
        return n;
    }

    static Transform FindPlus()
    {
        GameObject go = GameObject.Find(RoomSlotPlusPrompt.PlusName);
        return go != null ? go.transform : null;
    }

    static Button FindButton(string name)
    {
        GameObject go = GameObject.Find(name);
        return go != null ? go.GetComponent<Button>() : null;
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
        sLog.AppendLine("RESULT=OK inspect Temp/alpha01_169/*.png");
        WriteReport();
        Debug.Log("[Alpha01_169PlayVerify]\n" + sLog);
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
        Debug.LogError("[Alpha01_169PlayVerify] " + msg);
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
