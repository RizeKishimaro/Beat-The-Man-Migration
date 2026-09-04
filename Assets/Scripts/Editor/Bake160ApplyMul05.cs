using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 160 — apply comparison-row #3 (mask on, _Smoothness 0.5) to existing
/// 149 object materials. Does not touch PackMask, Art/, or test compare mats.
/// Flag: Temp/bake160_apply_mul05.flag
/// </summary>
[InitializeOnLoad]
public static class Bake160ApplyMul05
{
    const string FlagPath = "Temp/bake160_apply_mul05.flag";
    const string ReportPath = "Temp/bake160_apply_mul05.txt";
    const string ShotPath = "Temp/bake160/mul05_play.png";
    const string PendingKey = "Bake160ApplyMul05.pending";
    const float Target = 0.5f;

    static int playFrames;

    static Bake160ApplyMul05()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling)
            return;
        TryRun();
    }

    [MenuItem("Tools/Objects/160 Apply Smoothness 0.5 To Objects")]
    public static void Menu()
    {
        File.WriteAllText(Full(ReportPath), Apply());
        EditorUtility.DisplayDialog("Ticket 160", Truncate(File.ReadAllText(Full(ReportPath)), 1600), "OK");
    }

    static void TryRun()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += TryRun;
            return;
        }
        if (EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += TryRun;
            return;
        }

        try
        {
            File.Delete(flag);
            string report = Apply();
            File.WriteAllText(Full(ReportPath), report);
            Debug.Log("[Bake160ApplyMul05]\n" + report);
        }
        catch (Exception e)
        {
            File.WriteAllText(Full(ReportPath), "ERROR " + e);
            Debug.LogError("[Bake160ApplyMul05] " + e);
        }
    }

    static string Apply()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 160 apply mul 0.5 to object mats ===");
        int changed = 0;
        int skipped = 0;
        string[] guids = AssetDatabase.FindAssets("t:Material", new[]
        {
            "Assets/Objects/Pickable",
            "Assets/Objects/Nodes"
        });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path) || path.Contains("/Materials/"))
            {
                skipped++;
                continue;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || !mat.HasProperty("_Smoothness"))
            {
                skipped++;
                continue;
            }

            bool hasMask = mat.HasProperty("_MetallicGlossMap") && mat.GetTexture("_MetallicGlossMap") != null;
            if (!hasMask)
            {
                sb.AppendLine("skip no-mask " + path + " smooth=" + mat.GetFloat("_Smoothness"));
                skipped++;
                continue;
            }

            float before = mat.GetFloat("_Smoothness");
            if (Mathf.Abs(before - Target) < 0.001f)
            {
                sb.AppendLine("already " + path);
                skipped++;
                continue;
            }

            mat.SetFloat("_Smoothness", Target);
            EditorUtility.SetDirty(mat);
            sb.AppendLine("set " + path + " " + before + " -> " + Target);
            changed++;
        }

        AssetDatabase.SaveAssets();
        sb.AppendLine("changed=" + changed + " skipped=" + skipped);

        Material printer = AssetDatabase.LoadAssetAtPath<Material>("Assets/Objects/Pickable/printer/printer.mat");
        sb.AppendLine("printer.mat smoothness=" + (printer != null ? printer.GetFloat("_Smoothness").ToString() : "missing"));

        SessionState.SetBool(PendingKey, true);
        playFrames = 0;
        File.WriteAllText(Full(ReportPath), sb.ToString());
        sb.AppendLine("entering Play (CameraFollow not touched)");
        EditorApplication.isPlaying = true;
        sb.AppendLine("RESULT=PLAYING");
        return sb.ToString();
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(PendingKey, false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            playFrames = 0;
            EditorApplication.update -= TickPlay;
            EditorApplication.update += TickPlay;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= TickPlay;
            SessionState.SetBool(PendingKey, false);
        }
    }

    static void TickPlay()
    {
        if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying)
            return;
        playFrames++;
        if (playFrames < 40)
            return;
        EditorApplication.update -= TickPlay;

        var sb = new StringBuilder();
        if (File.Exists(Full(ReportPath)))
            sb.Append(File.ReadAllText(Full(ReportPath)));
        sb.AppendLine("--- Play ---");
        var follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        sb.AppendLine("isPlaying=" + EditorApplication.isPlaying
            + " followOn=" + (follow != null && follow.enabled)
            + " camPos=" + (Camera.main != null ? Camera.main.transform.position.ToString() : "null"));
        Material printer = UnityEngine.Object.FindAnyObjectByType<MeshRenderer>() != null
            ? AssetDatabase.LoadAssetAtPath<Material>("Assets/Objects/Pickable/printer/printer.mat")
            : AssetDatabase.LoadAssetAtPath<Material>("Assets/Objects/Pickable/printer/printer.mat");
        sb.AppendLine("play printer.mat smoothness=" + (printer != null ? printer.GetFloat("_Smoothness").ToString() : "missing"));
        CaptureGameView(ShotPath, sb);
        sb.AppendLine("LOOK: Play running. CameraFollow untouched. Do not save GridFoundation.");
        sb.AppendLine("RESULT=OK");
        File.WriteAllText(Full(ReportPath), sb.ToString());
        SessionState.SetBool(PendingKey, false);
    }

    static void CaptureGameView(string rel, StringBuilder sb)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            sb.AppendLine("FAIL no camera");
            return;
        }
        Vector2 gv = UnityEditor.Handles.GetMainGameViewSize();
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
        UnityEngine.Object.DestroyImmediate(rt);
        Directory.CreateDirectory(Full("Temp/bake160"));
        File.WriteAllBytes(Full(rel), tex.EncodeToPNG());
        sb.AppendLine("capture=" + rel + " " + w + "x" + h);
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}
