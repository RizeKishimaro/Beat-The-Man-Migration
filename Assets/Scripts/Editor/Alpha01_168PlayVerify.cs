using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Ticket 168 — Play-mode checks on Alpha01 (grass, +, stub menu, no spawn).
/// CameraFollow stays ON. Does not save Alpha01 or GridFoundation.
/// Flag: Temp/alpha01_168_play.flag
/// Report: Temp/alpha01_168/play.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_168PlayVerify
{
    const string FlagPath = "Temp/alpha01_168_play.flag";
    const string ReportPath = "Temp/alpha01_168/play.txt";
    const string ShotDir = "Temp/alpha01_168";
    const string ScenePath = Alpha01SceneSetup.ScenePath;
    const string PendingKey = "Alpha01_168Play.pending";

    static int sPhase;
    static int sWait;
    static int sEnterFrame = -1;
    static int sLastPlayFrame = -1;
    static StringBuilder sLog;
    static bool sClicked;
    static bool sConfirmed;
    static bool sClosed;

    static Alpha01_168PlayVerify()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Scenes/168 Play-verify Alpha01 Grass + Menu")]
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
                sClicked = sConfirmed = sClosed = false;
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
        sLog.AppendLine("=== Ticket 168 Play verify ===");
        sLog.AppendLine("does not save Alpha01 or GridFoundation");
        sLog.AppendLine("CameraFollow stays ON");

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
            sLog.AppendLine("entered Play");
            ReportGrass();
            ReportPlus(false);
            CaptureCam("grass");
        }

        if (sWait == 16)
        {
            ReportPlus(true);
            CaptureCam("plus");
        }

        if (sWait == 22 && !sClicked)
        {
            BuildMenuController menu = Object.FindAnyObjectByType<BuildMenuController>();
            RoomSlotPlusPrompt prompt = Object.FindAnyObjectByType<RoomSlotPlusPrompt>();
            GridSystem grid = GridSystem.Instance;
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            if (menu == null || grid == null || player == null)
            {
                Fail("FAIL missing menu/grid/player");
                return;
            }
            GridCoord anchor = RoomSlotGrid.SlotAnchorFromWorld(grid, player.transform.position);
            sLog.AppendLine("prompt=" + (prompt != null));
            menu.Open(anchor);
            sClicked = true;
            sLog.AppendLine("opened menu slot=" + anchor);
        }

        if (sWait == 28)
        {
            BuildMenuController menu = Object.FindAnyObjectByType<BuildMenuController>();
            Transform plus = FindPlus();
            sLog.AppendLine("menu open=" + (menu != null && menu.IsOpen)
                + " plusActiveWhileOpen=" + (plus != null && plus.gameObject.activeSelf));
            Button place = FindButton("DefaultBuildingButton");
            Button close = FindButton("CloseButton");
            sLog.AppendLine("buttons place=" + (place != null) + " close=" + (close != null));
            CaptureCam("menu_world");
            CaptureGameView("menu_ui");
            if (place != null)
            {
                int buildingsBefore = CountBuildings();
                place.onClick.Invoke();
                sConfirmed = true;
                int buildingsAfter = CountBuildings();
                sLog.AppendLine("confirm buildings before=" + buildingsBefore + " after=" + buildingsAfter);
                sLog.AppendLine("CHECK no spawn: " + (buildingsAfter == buildingsBefore && buildingsAfter == 0));
            }
        }

        if (sWait == 34)
        {
            BuildMenuController menu = Object.FindAnyObjectByType<BuildMenuController>();
            sLog.AppendLine("menu open after confirm=" + (menu != null && menu.IsOpen));
            if (menu != null && !menu.IsOpen)
            {
                GridSystem grid = GridSystem.Instance;
                PlayerController player = Object.FindAnyObjectByType<PlayerController>();
                GridCoord anchor = RoomSlotGrid.SlotAnchorFromWorld(grid, player.transform.position);
                menu.Open(anchor);
            }
        }

        if (sWait == 38)
        {
            BuildMenuController menu = Object.FindAnyObjectByType<BuildMenuController>();
            if (menu != null && menu.IsOpen)
            {
                Button close = FindButton("CloseButton");
                close?.onClick.Invoke();
                sClosed = true;
            }
            sLog.AppendLine("closed via Close button=" + sClosed
                + " menuOpen=" + (menu != null && menu.IsOpen));
        }

        if (sWait == 44)
        {
            ReportOccupyHidesPlus();
            CaptureCam("plus_after");
            Finish();
        }

        if (sWait > 120)
            Fail("FAIL timeout");
    }

    static void ReportGrass()
    {
        GameObject floor = GameObject.Find(WorldFloorEnsure.FloorName);
        int floors = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t != null && t.name == WorldFloorEnsure.FloorName)
                floors++;
        sLog.AppendLine("WorldFloor count=" + floors);
        if (floor == null)
        {
            sLog.AppendLine("FAIL no WorldFloor");
            return;
        }
        Vector3 scale = floor.transform.localScale;
        var rend = floor.GetComponent<Renderer>();
        string mat = rend != null && rend.sharedMaterial != null ? rend.sharedMaterial.name : "null";
        sLog.AppendLine("WorldFloor scale=" + scale + " pos=" + floor.transform.position + " mat=" + mat);
        sLog.AppendLine("CHECK grass 96x96: "
            + (Mathf.Abs(scale.x - 96f) < 0.01f && Mathf.Abs(scale.z - 96f) < 0.01f && floors == 1));
    }

    static void ReportPlus(bool expectVisible)
    {
        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        GridSystem grid = GridSystem.Instance;
        Transform plus = FindPlus();
        if (player == null || grid == null)
        {
            sLog.AppendLine("FAIL player/grid missing for plus");
            return;
        }
        GridCoord anchor = RoomSlotGrid.SlotAnchorFromWorld(grid, player.transform.position);
        Vector3 center = RoomSlotGrid.SlotCenterWorld(grid, anchor);
        bool free = RoomSlotGrid.IsSlotFree(grid, anchor);
        sLog.AppendLine("slot=" + anchor + " free=" + free + " center=" + center
            + " plusActive=" + (plus != null && plus.gameObject.activeSelf)
            + " plusPos=" + (plus != null ? plus.position.ToString() : "n/a"));
        if (expectVisible && plus != null && plus.gameObject.activeSelf)
        {
            float dxz = Vector2.Distance(
                new Vector2(plus.position.x, plus.position.z),
                new Vector2(center.x, center.z));
            sLog.AppendLine("CHECK plus at slot centre: " + (dxz < 0.05f));
            Collider[] cols = plus.GetComponentsInChildren<Collider>(true);
            sLog.AppendLine("CHECK plus no collider: " + (cols.Length == 0));
        }
    }

    static void ReportOccupyHidesPlus()
    {
        GridSystem grid = GridSystem.Instance;
        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        if (grid == null || player == null)
            return;
        GridCoord anchor = RoomSlotGrid.SlotAnchorFromWorld(grid, player.transform.position);
        var cells = new System.Collections.Generic.List<GridCoord>(
            grid.FootprintCells(anchor, new Vector2Int(RoomSlotGrid.SlotCells, RoomSlotGrid.SlotCells)));
        bool occupied = grid.TryOccupy(cells, "168-probe");
        sLog.AppendLine("probe occupy 6x6=" + occupied);
        var prompt = Object.FindAnyObjectByType<RoomSlotPlusPrompt>();
        if (prompt != null)
            prompt.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        Transform plus = FindPlus();
        sLog.AppendLine("plusActive while occupied=" + (plus != null && plus.gameObject.activeSelf));
        sLog.AppendLine("CHECK plus hides when occupied: " + (plus == null || !plus.gameObject.activeSelf));
        grid.Release(cells);
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

    static int CountBuildings()
    {
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null)
                continue;
            if (t.name == "DefaultBuilding" || t.name.StartsWith("DefaultBuilding_"))
                n++;
        }
        return n;
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
        sLog.AppendLine("clicked=" + sClicked + " confirmed=" + sConfirmed + " closed=" + sClosed);
        sLog.AppendLine("scene dirty (must not save)=" + SceneManager.GetActiveScene().isDirty);
        sLog.AppendLine("RESULT=OK inspect Temp/alpha01_168/*.png");
        WriteReport();
        Debug.Log("[Alpha01_168PlayVerify]\n" + sLog);
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
        Debug.LogError("[Alpha01_168PlayVerify] " + msg);
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
