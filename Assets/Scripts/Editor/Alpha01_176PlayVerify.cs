using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Ticket 176 — Play-mode look at the NEW ROOM left drawer.
/// CameraFollow stays ON. Does not save Alpha01 or GridFoundation. Does not spawn rooms.
/// Flag: Temp/alpha01_176_play.flag
/// Report: Temp/alpha01_176/play.txt
/// Shots: Temp/alpha01_176/*.png
/// </summary>
[InitializeOnLoad]
public static class Alpha01_176PlayVerify
{
    const string FlagPath = "Temp/alpha01_176_play.flag";
    const string ReportPath = "Temp/alpha01_176/play.txt";
    const string ShotDir = "Temp/alpha01_176";
    const string ScenePath = Alpha01SceneSetup.ScenePath;
    const string PendingKey = "Alpha01_176Play.pending";

    static int sPhase;
    static int sWait;
    static int sEnterFrame = -1;
    static int sLastPlayFrame = -1;
    static float sPlayEnteredAt = -1f;
    static float sOpenedAt = -1f;
    static bool sWarped;
    static bool sOpened;
    static bool sShotOpen;
    static bool sShotHover;
    static bool sShotSelected;
    static object sPrevPlayBehavior;
    static StringBuilder sLog;

    static Alpha01_176PlayVerify()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Scenes/176 Play-verify NEW ROOM drawer")]
    public static void Menu()
    {
        File.WriteAllText(Full(FlagPath), "run\n");
    }

    static void OnPlay(PlayModeStateChange c)
    {
        if (c == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
        {
            if (sLog == null)
            {
                sLog = new StringBuilder();
                if (File.Exists(Full(ReportPath)))
                    sLog.Append(File.ReadAllText(Full(ReportPath)));
            }
            sLog.AppendLine("EnteredPlayMode");
            KeepFollowOn();
            FocusGameView();
            WriteReport();
            return;
        }
        if (c != PlayModeStateChange.EnteredEditMode)
            return;
        sPhase = 0;
        sEnterFrame = -1;
        sWait = 0;
        sLastPlayFrame = -1;
        sPlayEnteredAt = -1f;
        sOpenedAt = -1f;
        sWarped = false;
        sOpened = false;
        sShotOpen = false;
        sShotHover = false;
        sShotSelected = false;
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
                WriteReport();
            }
            if (sEnterFrame < 0)
            {
                sEnterFrame = Time.frameCount;
                sLastPlayFrame = -1;
                sWait = 0;
                sPlayEnteredAt = -1f;
                sOpenedAt = -1f;
                sWarped = false;
                sOpened = false;
                sShotOpen = false;
                sShotHover = false;
                sShotSelected = false;
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
        sLog.AppendLine("=== Ticket 176 Play verify NEW ROOM drawer ===");
        sLog.AppendLine("does not save Alpha01 or GridFoundation");
        sLog.AppendLine("CameraFollow stays ON");
        sLog.AppendLine("does not spawn rooms");

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
        sLog.AppendLine("entering Play");
        WriteReport();
        SessionState.SetBool(PendingKey, true);
        sEnterFrame = -1;
        PreferPlayUnfocused();
        EditorApplication.isPlaying = true;
    }

    static void TickPlay()
    {
        if (!EditorApplication.isPlaying)
            return;

        KeepFollowOn();
        FocusGameView();
        PreferPlayUnfocused();
        if (EditorApplication.isPaused)
            EditorApplication.isPaused = false;
        EditorApplication.QueuePlayerLoopUpdate();

        if (sPlayEnteredAt < 0f)
            sPlayEnteredAt = (float)EditorApplication.timeSinceStartup;
        float elapsed = (float)EditorApplication.timeSinceStartup - sPlayEnteredAt;

        if (!sWarped && elapsed >= 0.4f)
        {
            sWarped = true;
            PlayerController p = Object.FindAnyObjectByType<PlayerController>();
            if (p != null)
            {
                CharacterController cc = p.GetComponent<CharacterController>();
                if (cc != null)
                    cc.enabled = false;
                p.transform.position = new Vector3(3f, 0f, 3f);
                if (cc != null)
                    cc.enabled = true;
            }
            sLog.AppendLine("warped player to 6x6 slot centre (3,0,3) elapsed=" + elapsed.ToString("0.00"));
            WriteReport();
        }

        if (!sOpened && elapsed >= 0.75f)
        {
            BuildMenuController menu = Object.FindAnyObjectByType<BuildMenuController>();
            GridSystem grid = GridSystem.Instance;
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            if (menu == null || grid == null || player == null)
            {
                Fail("FAIL missing menu/grid/player");
                return;
            }
            GridCoord anchor = RoomSlotGrid.SlotAnchorFromWorld(grid, player.transform.position);
            menu.Open(anchor);
            sOpened = true;
            sOpenedAt = (float)EditorApplication.timeSinceStartup;
            sLog.AppendLine("opened menu slot=" + anchor
                + " IsOpen=" + menu.IsOpen
                + " timeScale=" + Time.timeScale
                + " IsOpenForPlayer0=" + BuildMenuController.IsOpenForPlayer(0)
                + " titleFont=" + NewRoomFonts.TitleSource());
            WriteReport();
        }

        float now = (float)EditorApplication.timeSinceStartup;
        if (sOpened && !sShotOpen)
        {
            if (!DrawerIsIn())
            {
                if (now - sOpenedAt > 3f)
                    Fail("FAIL drawer did not slide in pos=" + DrawerPos());
                return;
            }
            if (now - sOpenedAt < 0.6f)
                return;
            sShotOpen = true;
            DumpMenu();
            CaptureGameView("drawer_open");
            RoomPanelView garage = FindPanel("Garage");
            if (garage != null)
                garage.PreviewHover();
            sLog.AppendLine("preview hover Garage pos=" + DrawerPos());
            WriteReport();
            return;
        }

        if (sShotOpen && !sShotHover && now - sOpenedAt >= 1.05f)
        {
            sShotHover = true;
            CaptureGameView("drawer_hover");
            sLog.AppendLine("hover spring Garage");
            WriteReport();
            return;
        }

        if (sShotHover && !sShotSelected && now - sOpenedAt >= 1.55f)
        {
            sShotSelected = true;
            CaptureGameView("drawer_selected");
            Finish();
            return;
        }

        if (elapsed > 12f)
            Fail("FAIL timeout elapsed=" + elapsed.ToString("0.00"));
    }

    static void DumpMenu()
    {
        BuildMenuController menu = Object.FindAnyObjectByType<BuildMenuController>();
        Transform canvas = GameObject.Find(BuildMenuController.CanvasName)?.transform;
        Transform drawer = canvas != null ? canvas.Find("Drawer") : null;
        RectTransform drawerRt = drawer != null ? drawer.GetComponent<RectTransform>() : null;
        Text title = drawer != null ? drawer.Find("Title")?.GetComponent<Text>() : null;
        Transform list = drawer != null ? drawer.Find("RoomList") : null;
        RawImage wall = drawer != null ? drawer.GetComponentInChildren<RawImage>() : null;
        Image dim = canvas != null ? FindNamedImage(canvas, "Dim") : null;
        Button place = FindButton("DefaultBuildingButton");
        Button close = FindButton("CloseButton");
        int panels = list != null ? list.GetComponent<Transform>().childCount : 0;

        sLog.AppendLine("menu.IsOpen=" + (menu != null && menu.IsOpen));
        sLog.AppendLine("canvas=" + (canvas != null && canvas.gameObject.activeInHierarchy));
        sLog.AppendLine("drawerW=" + (drawerRt != null ? drawerRt.rect.width.ToString("0.0") : "null")
            + " pos=" + (drawerRt != null ? drawerRt.anchoredPosition.ToString() : "null"));
        sLog.AppendLine("title=" + (title != null ? title.text : "null")
            + " font=" + (title != null && title.font != null ? title.font.name : "null")
            + " size=" + (title != null ? title.fontSize.ToString() : "n/a"));
        sLog.AppendLine("panels=" + panels
            + " placeBtn=" + (place != null)
            + " closeBtn=" + (close != null)
            + " dim=" + (dim != null));
        sLog.AppendLine("wallpaper=" + (wall != null && wall.texture != null ? wall.texture.name : "null")
            + " uv=" + (wall != null ? wall.uvRect.ToString() : "n/a"));
        sLog.AppendLine("timeScale=" + Time.timeScale);

        if (list != null)
        {
            for (int i = 0; i < list.childCount; i++)
            {
                RoomPanelView p = list.GetChild(i).GetComponent<RoomPanelView>();
                if (p != null)
                {
                    Text label = p.GetComponentInChildren<Text>();
                    sLog.AppendLine("  panel " + i + " " + p.Title + " " + p.Price
                        + " label=" + (label != null ? label.text.Replace("\n", "\\n") : "null"));
                }
            }
        }

        Transform plus = GameObject.Find(RoomSlotPlusPrompt.PlusName)?.transform;
        sLog.AppendLine("plusWhileOpen=" + (plus != null && plus.gameObject.activeSelf));
        CameraFollow follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        sLog.AppendLine("CameraFollow=" + (follow != null && follow.enabled));
    }

    static RoomPanelView FindPanel(string title)
    {
        RoomPanelView[] panels = Object.FindObjectsByType<RoomPanelView>(FindObjectsInactive.Exclude);
        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] != null && panels[i].Title == title)
                return panels[i];
        }
        return null;
    }

    static RectTransform FindDrawer()
    {
        Transform canvas = GameObject.Find(BuildMenuController.CanvasName)?.transform;
        Transform drawer = canvas != null ? canvas.Find("Drawer") : null;
        return drawer != null ? drawer.GetComponent<RectTransform>() : null;
    }

    static bool DrawerIsIn()
    {
        RectTransform rt = FindDrawer();
        return rt != null && Mathf.Abs(rt.anchoredPosition.x) < 2f;
    }

    static string DrawerPos()
    {
        RectTransform rt = FindDrawer();
        return rt != null ? rt.anchoredPosition.ToString() : "null";
    }

    static Image FindNamedImage(Transform root, string name)
    {
        Transform t = root.Find(name);
        return t != null ? t.GetComponent<Image>() : null;
    }

    static Button FindButton(string name)
    {
        GameObject go = GameObject.Find(name);
        return go != null ? go.GetComponent<Button>() : null;
    }

    static void Finish()
    {
        KeepFollowOn();
        sLog.AppendLine("scene dirty (must not save)=" + SceneManager.GetActiveScene().isDirty);
        sLog.AppendLine("RESULT=OK inspect Temp/alpha01_176/*.png");
        WriteReport();
        Debug.Log("[Alpha01_176PlayVerify]\n" + sLog);
        RestorePlayBehavior();
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
        Debug.LogError("[Alpha01_176PlayVerify]\n" + sLog);
        RestorePlayBehavior();
        SessionState.SetBool(PendingKey, false);
        sPhase = 0;
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static void PreferPlayUnfocused()
    {
        try
        {
            System.Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            System.Type pmvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
            if (gvType == null || pmvType == null)
                return;
            System.Type behaviorType = pmvType.GetNestedType("EnterPlayModeBehavior");
            PropertyInfo prop = pmvType.GetProperty("enterPlayModeBehavior", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (behaviorType == null || prop == null || !prop.CanWrite)
                return;
            EditorWindow gv = EditorWindow.GetWindow(gvType, false, "Game", true);
            if (gv == null)
                return;
            object unfocused = System.Enum.Parse(behaviorType, "PlayUnfocused");
            if (sPrevPlayBehavior == null)
                sPrevPlayBehavior = prop.GetValue(gv, null);
            prop.SetValue(gv, unfocused, null);
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo lowRes = gvType.GetField("m_LowResolutionForAspectRatios", inst);
            if (lowRes != null && lowRes.FieldType == typeof(bool))
                lowRes.SetValue(gv, false);
            PropertyInfo lowResProp = gvType.GetProperty("lowResolutionForAspectRatios", inst);
            if (lowResProp != null && lowResProp.CanWrite && lowResProp.PropertyType == typeof(bool))
                lowResProp.SetValue(gv, false, null);
        }
        catch { }
    }

    static void RestorePlayBehavior()
    {
        if (sPrevPlayBehavior == null)
            return;
        System.Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        System.Type pmvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
        PropertyInfo prop = pmvType != null
            ? pmvType.GetProperty("enterPlayModeBehavior", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            : null;
        if (gvType == null || prop == null || !prop.CanWrite)
            return;
        EditorWindow gv = EditorWindow.GetWindow(gvType, false, "Game", true);
        if (gv != null)
            prop.SetValue(gv, sPrevPlayBehavior, null);
        sPrevPlayBehavior = null;
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

    static void CaptureGameView(string name)
    {
        FocusGameView();
        Texture2D tex = GrabGameViewRt();
        if (tex == null)
        {
            sLog.AppendLine("FAIL gameview capture " + name + " null");
            return;
        }
        Directory.CreateDirectory(Full(ShotDir));
        string path = Full(ShotDir + "/" + name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        sLog.AppendLine("shot " + name + " " + tex.width + "x" + tex.height + " bytes=" + new FileInfo(path).Length
            + " source=GameViewRT pos=" + DrawerPos());
        Object.DestroyImmediate(tex);
    }

    static Texture2D GrabGameViewRt()
    {
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        System.Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return null;
        foreach (Object obj in Resources.FindObjectsOfTypeAll(gvType))
        {
            var w = obj as EditorWindow;
            if (w == null)
                continue;
            w.Repaint();
            foreach (FieldInfo f in gvType.GetFields(inst))
            {
                if (!typeof(RenderTexture).IsAssignableFrom(f.FieldType))
                    continue;
                var rt = f.GetValue(w) as RenderTexture;
                if (rt != null && rt.width > 32 && rt.height > 32)
                    return ReadRt(rt);
            }
            foreach (PropertyInfo p in gvType.GetProperties(inst))
            {
                if (p.GetIndexParameters().Length > 0)
                    continue;
                if (!typeof(RenderTexture).IsAssignableFrom(p.PropertyType))
                    continue;
                RenderTexture rt;
                try { rt = p.GetValue(w, null) as RenderTexture; }
                catch { continue; }
                if (rt != null && rt.width > 32 && rt.height > 32)
                    return ReadRt(rt);
            }
        }
        try
        {
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex != null && tex.width > 32)
                return tex;
        }
        catch { }
        return null;
    }

    static Texture2D ReadRt(RenderTexture rt)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        int w = tex.width;
        int h = tex.height;
        Color32[] px = tex.GetPixels32();
        Color32[] flipped = new Color32[px.Length];
        for (int y = 0; y < h; y++)
            System.Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
        tex.SetPixels32(flipped);
        tex.Apply();
        return tex;
    }

    static void FocusGameView()
    {
        System.Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return;
        EditorWindow gv = EditorWindow.GetWindow(gvType, false, "Game", true);
        if (gv != null)
        {
            gv.Focus();
            gv.Repaint();
        }
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }

    static void WriteReport()
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, sLog != null ? sLog.ToString() : "");
    }
}
