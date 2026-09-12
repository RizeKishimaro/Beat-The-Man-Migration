using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 174 — Play-mode look at empty 6×6 Alpha01 glossy floor.
/// CameraFollow stays ON. Does not save Alpha01 or GridFoundation. Does not spawn rooms.
/// Flag: Temp/alpha01_174_play.flag
/// Report: Temp/alpha01_174/play.txt
/// Shots: Temp/alpha01_174/*.png
/// </summary>
[InitializeOnLoad]
public static class Alpha01_174PlayVerify
{
    const string FlagPath = "Temp/alpha01_174_play.flag";
    const string ReportPath = "Temp/alpha01_174/play.txt";
    const string ShotDir = "Temp/alpha01_174";
    const string ScenePath = Alpha01SceneSetup.ScenePath;
    const string PendingKey = "Alpha01_174Play.pending";

    static int sPhase;
    static int sWait;
    static int sEnterFrame = -1;
    static int sLastPlayFrame = -1;
    static StringBuilder sLog;

    static Alpha01_174PlayVerify()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Scenes/174 Play-verify Alpha01 Glossy Floor")]
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
            sLog.AppendLine("EnteredPlayMode warp (shots after settle)");
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
            var overlay = Object.FindAnyObjectByType<GridOverlay>();
            if (overlay != null)
                overlay.Visible = true;
            sLog.AppendLine("warped player to 6x6 slot centre (3,0,3) grid overlay on");
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
        sLog.AppendLine("=== Ticket 174 Play verify glossy floor ===");
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
        EditorApplication.isPlaying = true;
    }

    static void TickPlay()
    {
        if (!EditorApplication.isPlaying)
            return;
        if (Time.frameCount == sLastPlayFrame)
            return;
        sLastPlayFrame = Time.frameCount;
        sWait++;
        KeepFollowOn();

        if (sWait == 20)
        {
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
            var overlay = Object.FindAnyObjectByType<GridOverlay>();
            if (overlay != null)
                overlay.Visible = true;
            sLog.AppendLine("warped player to 6x6 slot centre (3,0,3) grid overlay on");
            WriteReport();
        }

        if (sWait == 50)
        {
            DumpLook();
            CaptureCam("slot_cam");
            CaptureGameView("slot_gv");
            Finish();
        }
    }

    static void DumpLook()
    {
        GameObject floor = GameObject.Find(WorldFloorEnsure.FloorName);
        var rend = floor != null ? floor.GetComponent<Renderer>() : null;
        Material mat = rend != null ? rend.sharedMaterial : null;
        sLog.AppendLine("WorldFloor=" + (floor != null)
            + " mat=" + (mat != null ? mat.name : "null")
            + " shader=" + (mat != null && mat.shader != null ? mat.shader.name : "null"));
        if (mat != null)
        {
            Color c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
            float metal = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : -1f;
            float smooth = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : -1f;
            Texture baseMap = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
            sLog.AppendLine("baseColor=" + c + " metallic=" + metal + " smoothness=" + smooth
                + " baseMap=" + (baseMap != null ? baseMap.name : "none"));
        }

        ReflectionProbe probe = Object.FindAnyObjectByType<ReflectionProbe>();
        sLog.AppendLine(probe != null
            ? "probe=" + probe.name + " mode=" + probe.mode
                + " baked=" + (probe.bakedTexture != null ? probe.bakedTexture.name : "NULL")
                + " custom=" + (probe.customBakedTexture != null ? probe.customBakedTexture.name : "NULL")
                + " texture=" + (probe.texture != null ? probe.texture.name : "NULL")
                + " captureOnce=" + (probe.GetComponent<ReflectionProbeCaptureOnce>() != null)
                + " box=" + probe.boxProjection + " size=" + probe.size
                + " pos=" + probe.transform.position
            : "FAIL no ReflectionProbe");

        int rooms = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t != null && (t.name == "DefaultBuilding" || t.name.StartsWith("DefaultBuilding_")))
                rooms++;
        }
        sLog.AppendLine("placedRooms=" + rooms);

        Light sun = null;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (l != null && l.type == LightType.Directional)
            {
                sun = l;
                break;
            }
        }
        sLog.AppendLine(sun != null
            ? "sun=" + sun.name + " intensity=" + sun.intensity + " color=" + sun.color
            : "FAIL no directional light");
        sLog.AppendLine("ambientIntensity=" + RenderSettings.ambientIntensity
            + " ambientMode=" + RenderSettings.ambientMode);

        UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        sLog.AppendLine("urp=" + (urp != null ? urp.name : "null")
            + " realtimeReflectionProbes=" + QualitySettings.realtimeReflectionProbes);

        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        CameraFollow follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        sLog.AppendLine("player=" + (player != null ? player.transform.position.ToString() : "null")
            + " CameraFollow=" + (follow != null && follow.enabled));
        Transform plus = GameObject.Find(RoomSlotPlusPrompt.PlusName)?.transform;
        Renderer plusRend = plus != null ? plus.GetComponentInChildren<Renderer>() : null;
        Texture plusTex = plusRend != null && plusRend.sharedMaterial != null
            ? (plusRend.sharedMaterial.HasProperty("_MainTex")
                ? plusRend.sharedMaterial.GetTexture("_MainTex")
                : plusRend.sharedMaterial.mainTexture)
            : null;
        sLog.AppendLine("plus=" + (plus != null && plus.gameObject.activeSelf)
            + " pos=" + (plus != null ? plus.position.ToString() : "n/a")
            + " scale=" + (plus != null ? plus.lossyScale.ToString() : "n/a")
            + " tex=" + (plusTex != null ? plusTex.name : "NULL"));
    }

    static void Finish()
    {
        KeepFollowOn();
        var follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        sLog.AppendLine("CameraFollow still enabled=" + (follow != null && follow.enabled));
        sLog.AppendLine("scene dirty (must not save)=" + SceneManager.GetActiveScene().isDirty);
        sLog.AppendLine("RESULT=OK inspect Temp/alpha01_174/*.png");
        WriteReport();
        Debug.Log("[Alpha01_174PlayVerify]\n" + sLog);
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
        Debug.LogError("[Alpha01_174PlayVerify]\n" + sLog);
        SessionState.SetBool(PendingKey, false);
        sPhase = 0;
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
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

    static void FocusGameView()
    {
        System.Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return;
        EditorWindow.GetWindow(gvType, false, null, true);
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
