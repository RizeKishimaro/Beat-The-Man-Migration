using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play Mode verify for single-mesh shader-clip wall visibility.
/// Flag: Temp/verify_shader_clip.flag
/// </summary>
[InitializeOnLoad]
public static class ShaderClipVerifyCapture
{
    const string FlagPath = "Temp/verify_shader_clip.flag";
    const string ReportPath = "Temp/agent_verify_shader_clip.txt";
    const string ScenePath = "Assets/For Cursor.unity";
    const string PendingKey = "ShaderClipVerifyCapture.pending";
    const string ShotOutside = "Assets/Screenshots/shader-clip-outside.png";
    const string ShotInside = "Assets/Screenshots/shader-clip-inside.png";

    static bool pending;
    static int frames;
    static double lastPoll;
    static StringBuilder report;

    static ShaderClipVerifyCapture()
    {
        EditorApplication.update += PollFlag;
        EditorApplication.playModeStateChanged += OnPlayMode;
        if (SessionState.GetBool(PendingKey, false) && EditorApplication.isPlaying)
        {
            pending = true;
            frames = 0;
            report = new StringBuilder();
            EditorApplication.update += TickPlay;
        }
    }

    [MenuItem("Tools/For Cursor/Verify Shader Clip Walls")]
    public static void FromMenu() => ArmAndPlay();

    static void PollFlag()
    {
        if (pending || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (EditorApplication.timeSinceStartup - lastPoll < 0.4)
            return;
        lastPoll = EditorApplication.timeSinceStartup;
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
        if (!File.Exists(flag))
            return;
        try { File.Delete(flag); } catch { }
        ArmAndPlay();
    }

    static void ArmAndPlay()
    {
        if (pending || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var vis in Object.FindObjectsByType<BuildingInteriorVisibility>(FindObjectsInactive.Exclude))
            vis.DestroyOrphanSolidStrips();
        pending = true;
        SessionState.SetBool(PendingKey, true);
        frames = 0;
        report = new StringBuilder();
        Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "Screenshots")));
        Debug.Log("[ShaderClipVerifyCapture] Entering Play Mode…");
        EditorApplication.isPlaying = true;
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(PendingKey, false) && !pending)
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            pending = true;
            SessionState.SetBool(PendingKey, true);
            frames = 0;
            if (report == null) report = new StringBuilder();
            EditorApplication.update -= TickPlay;
            EditorApplication.update += TickPlay;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= TickPlay;
            pending = false;
            SessionState.SetBool(PendingKey, false);
        }
    }

    static void TickPlay()
    {
        if (!pending || !EditorApplication.isPlaying)
            return;
        frames++;
        if (frames == 40) PoseOutside();
        else if (frames == 55)
        {
            report.AppendLine("=== OUTSIDE ===");
            Dump(report);
            RenderShot(ShotOutside);
            report.AppendLine("shot=" + ShotOutside);
        }
        else if (frames == 70) PoseInside();
        else if (frames == 95)
        {
            report.AppendLine("=== INSIDE ===");
            Dump(report);
            RenderShot(ShotInside);
            report.AppendLine("shot=" + ShotInside);
            Finish();
        }
    }

    static GameObject Cafe() => GameObject.Find("CafeRoom");

    static void PoseOutside()
    {
        var cafe = Cafe();
        var player = Object.FindAnyObjectByType<PlayerController>();
        var vis = Object.FindAnyObjectByType<BuildingInteriorVisibility>();
        if (cafe == null || player == null) return;
        player.transform.position = cafe.transform.TransformPoint(new Vector3(2f, 0.05f, -4f));
        if (vis != null) vis.EditorSetInsideForTest(false);
        PoseCam(cafe.transform.TransformPoint(new Vector3(0f, 1.2f, -1f)), new Vector3(-6f, 5f, -6f), 5f);
    }

    static void PoseInside()
    {
        var cafe = Cafe();
        var player = Object.FindAnyObjectByType<PlayerController>();
        var vis = Object.FindAnyObjectByType<BuildingInteriorVisibility>();
        if (cafe == null || player == null) return;
        player.transform.position = cafe.transform.TransformPoint(new Vector3(0f, 1f, 0.3f));
        if (vis != null) vis.EditorSetInsideForTest(true);
        PoseCam(cafe.transform.TransformPoint(new Vector3(-1.5f, 0.8f, -1.5f)), new Vector3(-5f, 4.5f, -5f), 4.5f);
    }

    static void PoseCam(Vector3 focus, Vector3 offset, float ortho)
    {
        Camera cam = Camera.main ?? Object.FindAnyObjectByType<Camera>();
        if (cam == null) return;
        var follow = cam.GetComponent<CameraFollow>();
        if (follow != null) follow.enabled = false;
        cam.orthographic = true;
        cam.orthographicSize = ortho;
        cam.transform.position = focus + offset;
        cam.transform.LookAt(focus);
    }

    static void Dump(StringBuilder sb)
    {
        var vis = Object.FindAnyObjectByType<BuildingInteriorVisibility>();
        if (vis == null) { sb.AppendLine("FAIL: no BuildingInteriorVisibility"); return; }
        sb.AppendLine("isInside=" + vis.IsInside);
        sb.Append(vis.DebugDumpStripState());
        bool anySolid = false;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t != null && t.name.EndsWith("_SolidStrip"))
            {
                anySolid = true;
                sb.AppendLine("LEAK " + t.name);
            }
        }
        sb.AppendLine(anySolid ? "CHECK_NO_SOLIDSTRIP=FAIL" : "CHECK_NO_SOLIDSTRIP=PASS");
        if (vis.cullableWalls == null) return;
        foreach (var r in vis.cullableWalls)
        {
            if (r == null) continue;
            if (!r.enabled) sb.AppendLine("FAIL_RENDERER_DISABLED " + r.name);
            string shader = r.sharedMaterial != null && r.sharedMaterial.shader != null
                ? r.sharedMaterial.shader.name : "null";
            if (!shader.Contains("BuildingWallStrip"))
                sb.AppendLine("WARN_SHADER " + r.name + " " + shader);
        }
    }

    static void RenderShot(string assetPath)
    {
        Camera cam = Camera.main ?? Object.FindAnyObjectByType<Camera>();
        if (cam == null) { report.AppendLine("FAIL: no camera"); return; }
        int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = prev;
        RenderTexture.active = null;
        byte[] png = tex.EncodeToPNG();
        Object.Destroy(rt);
        Object.Destroy(tex);
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        File.WriteAllBytes(full, png);
        report.AppendLine("wroteBytes=" + png.Length);
        Debug.Log("[ShaderClipVerifyCapture] Wrote " + assetPath);
    }

    static void Finish()
    {
        bool ok = File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ShotOutside)))
                  && File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ShotInside)));
        report.AppendLine(ok ? "OK_SHADER_CLIP" : "FAIL_MISSING_SHOTS");
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath)), report.ToString());
        Debug.Log("[ShaderClipVerifyCapture]\n" + report);
        pending = false;
        SessionState.SetBool(PendingKey, false);
        EditorApplication.update -= TickPlay;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.isPlaying = false;
            AssetDatabase.Refresh();
        };
    }
}
