using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tickets 117+119: apply SSAO Samples/BlurQuality quality bump and capture
/// interior (117) + exterior (119) before/after. AO stays fully active.
/// Flag: Temp/ssao_quality_verify_117_119.flag
/// Report: Temp/ssao_quality_verify_117_119.txt
/// Shots: Temp/ssao_q_*.png
/// </summary>
[InitializeOnLoad]
public static class SsaoQualityVerify117_119
{
    // URP ScreenSpaceAmbientOcclusionSettings.AOSampleOption (internal):
    // High=0 → 12 samples, Medium=1 → 8, Low=2 → 4
    const int SamplesHigh = 0;
    const int SamplesMedium = 1;
    const int SamplesLow = 2;
    // BlurQualityOptions: High=0 Bilateral, Medium=1 Gaussian, Low=2 Kawase
    const int BlurHigh = 0;

    const string FlagPath = "Temp/ssao_quality_verify_117_119.flag";
    const string ReportPath = "Temp/ssao_quality_verify_117_119.txt";
    const string ShotDir = "Temp";
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";
    const string DonePath = "Temp/ssao_quality_verify_117_119.done";

    static string SampleName(int i) =>
        i == SamplesHigh ? "High(12)" : i == SamplesMedium ? "Medium(8)" : i == SamplesLow ? "Low(4)" : $"?({i})";

    static string BlurName(int i) =>
        i == 0 ? "High(Bilateral)" : i == 1 ? "Medium(Gaussian)" : i == 2 ? "Low(Kawase)" : $"?({i})";

    static SsaoQualityVerify117_119()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRun();
    }

    static void TryRun()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        File.Delete(flag);
        try
        {
            string report = Run();
            File.WriteAllText(Full(ReportPath), report);
            File.WriteAllText(Full(DonePath), "ok\n" + System.DateTime.UtcNow.ToString("o"));
            Debug.Log("[SsaoQualityVerify117_119]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(Full(ReportPath), "FAIL: " + e);
            File.WriteAllText(Full(DonePath), "fail\n" + e);
            Debug.LogError("[SsaoQualityVerify117_119] " + e);
        }
    }

    [MenuItem("Tools/Buildings/SSAO Quality Verify (117+119)")]
    public static void MenuRun()
    {
        string report = Run();
        File.WriteAllText(Full(ReportPath), report);
        EditorUtility.DisplayDialog("SSAO Quality Verify", Truncate(report, 1400), "OK");
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SSAO quality verify (tickets 117 + 119) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");
        sb.AppendLine("AO stays FULLY ACTIVE — no gating / conditional disable");
        sb.AppendLine();
        sb.AppendLine("URP AOSampleOption enum (YAML int → quality):");
        sb.AppendLine("  High=0 → 12 samples | Medium=1 → 8 samples | Low=2 → 4 samples");
        sb.AppendLine("URP BlurQualityOptions: High=0 Bilateral | Medium=1 Gaussian | Low=2 Kawase");
        sb.AppendLine("NOTE: YAML 'Samples: 1' was already Medium (=8 samples), NOT literal 1 sample.");
        sb.AppendLine("      YAML 'BlurQuality: 0' was already High (Bilateral) — cannot increase further.");
        sb.AppendLine("User asked Samples 1→4 then 8: we A/B Low(4) → Medium(8) → High(12), leave High.");
        sb.AppendLine();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine($"opened={scene.path}");

        Transform building = FindBuildingRoot();
        if (building == null)
        {
            sb.AppendLine("FAIL: DefaultBuilding_117 / DefaultBuilding not found");
            return sb.ToString();
        }
        sb.AppendLine($"buildingRoot={building.name}");

        Camera cam = Camera.main ?? Object.FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            sb.AppendLine("FAIL: no camera");
            return sb.ToString();
        }

        // Always restore camera — avoids diagnostic framing sticking in the Editor.
        Vector3 savedPos = cam.transform.position;
        Quaternion savedRot = cam.transform.rotation;
        float savedFov = cam.fieldOfView;
        bool savedOrtho = cam.orthographic;
        float savedOrthoSize = cam.orthographicSize;

        Directory.CreateDirectory(Full(ShotDir));

        ScreenSpaceAmbientOcclusion ssao = FindSsao();
        if (ssao == null)
        {
            sb.AppendLine("FAIL: ScreenSpaceAmbientOcclusion feature not on PC_Renderer");
            RestoreCam(cam, savedPos, savedRot, savedFov, savedOrtho, savedOrthoSize);
            return sb.ToString();
        }

        var so = new SerializedObject(ssao);
        var samplesProp = so.FindProperty("m_Settings.Samples");
        var blurProp = so.FindProperty("m_Settings.BlurQuality");
        var activeProp = so.FindProperty("m_Active");
        int samplesBefore = samplesProp.enumValueIndex;
        int blurBefore = blurProp.enumValueIndex;
        bool activeBefore = activeProp.boolValue;

        sb.AppendLine($"SSAO m_Active before={activeBefore} (must stay true)");
        sb.AppendLine($"Samples before YAML={samplesBefore} ({SampleName(samplesBefore)})");
        sb.AppendLine($"BlurQuality before YAML={blurBefore} ({BlurName(blurBefore)})");

        // Ensure AO stays on throughout.
        activeProp.boolValue = true;
        // Blur already High; keep High explicitly.
        blurProp.enumValueIndex = BlurHigh;
        so.ApplyModifiedPropertiesWithoutUndo();

        // --- Baseline at current Samples (expect Medium / 8) ---
        CapturePair(cam, building, "ssao_q_00_baseline", sb);

        // --- User step 1: Samples Low = 4 ---
        SetSamples(so, samplesProp, SamplesLow, ssao);
        CapturePair(cam, building, "ssao_q_01_samples4_Low", sb);
        sb.AppendLine("applied Samples=Low (4)");

        // --- User step 2: Samples Medium = 8 ---
        SetSamples(so, samplesProp, SamplesMedium, ssao);
        CapturePair(cam, building, "ssao_q_02_samples8_Medium", sb);
        sb.AppendLine("applied Samples=Medium (8)");

        // --- Escalation / final: Samples High = 12 (only step above prior Medium) ---
        SetSamples(so, samplesProp, SamplesHigh, ssao);
        CapturePair(cam, building, "ssao_q_03_samples12_High", sb);
        sb.AppendLine("applied Samples=High (12) — FINAL leave-on setting");

        // Persist final: High samples + High blur + active
        so.Update();
        samplesProp.enumValueIndex = SamplesHigh;
        blurProp.enumValueIndex = BlurHigh;
        activeProp.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ssao);
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
        if (renderer != null)
            EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssets();

        so.Update();
        sb.AppendLine();
        sb.AppendLine("--- FINAL PC_Renderer SSAO ---");
        sb.AppendLine($"m_Active={activeProp.boolValue}");
        sb.AppendLine($"Samples YAML={samplesProp.enumValueIndex} ({SampleName(samplesProp.enumValueIndex)})");
        sb.AppendLine($"BlurQuality YAML={blurProp.enumValueIndex} ({BlurName(blurProp.enumValueIndex)})");
        sb.AppendLine($"before→after Samples {samplesBefore}→{samplesProp.enumValueIndex} Blur {blurBefore}→{blurProp.enumValueIndex}");
        sb.AppendLine();
        sb.AppendLine("shots:");
        sb.AppendLine("  Temp/ssao_q_00_baseline_117_interior.png + _119_exterior.png");
        sb.AppendLine("  Temp/ssao_q_01_samples4_Low_117_interior.png + _119_exterior.png");
        sb.AppendLine("  Temp/ssao_q_02_samples8_Medium_117_interior.png + _119_exterior.png");
        sb.AppendLine("  Temp/ssao_q_03_samples12_High_117_interior.png + _119_exterior.png");

        RestoreCam(cam, savedPos, savedRot, savedFov, savedOrtho, savedOrthoSize);
        // Discard camera transform dirt from framing; keep asset SSAO save.
        if (EditorSceneManager.GetActiveScene().isDirty)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("camera restored; scene reloaded to discard framing dirt (SSAO asset kept)");
        sb.AppendLine("RESULT=OK");
        return sb.ToString();
    }

    static void SetSamples(
        SerializedObject so,
        SerializedProperty samplesProp,
        int sampleEnum,
        ScreenSpaceAmbientOcclusion ssao)
    {
        so.Update();
        samplesProp.enumValueIndex = sampleEnum;
        var active = so.FindProperty("m_Active");
        if (active != null) active.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ssao);
    }

    static void CapturePair(Camera cam, Transform building, string prefix, StringBuilder sb)
    {
        FrameInteriorFloorWall(cam, building);
        Capture(cam, prefix + "_117_interior.png");
        sb.AppendLine($"shot={ShotDir}/{prefix}_117_interior.png cam={cam.transform.position}");

        FrameExteriorCorner(cam, building);
        Capture(cam, prefix + "_119_exterior.png");
        sb.AppendLine($"shot={ShotDir}/{prefix}_119_exterior.png cam={cam.transform.position}");
    }

    static ScreenSpaceAmbientOcclusion FindSsao()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
        if (renderer == null) return null;
        foreach (var f in renderer.rendererFeatures)
        {
            if (f is ScreenSpaceAmbientOcclusion ssao)
                return ssao;
        }
        return null;
    }

    static Transform FindBuildingRoot()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            if (t.name == "DefaultBuilding_117" || t.name == "DefaultBuilding")
                return t;
        }
        return null;
    }

    static void FrameInteriorFloorWall(Camera cam, Transform building)
    {
        // Inside near Wall_Left × RoomFloor corner — 117 seam location.
        Bounds? b = BoundsOf(building);
        if (b == null) return;
        Bounds bounds = b.Value;
        // Interior sits inset ~1m from exterior; look at left wall / floor join.
        Vector3 focus = new Vector3(bounds.min.x + 1.15f, 0.15f, bounds.min.z + 2.5f);
        cam.transform.position = focus + new Vector3(1.6f, 1.1f, 1.4f);
        cam.transform.LookAt(focus);
        cam.orthographic = false;
        cam.fieldOfView = 50f;
    }

    static void FrameExteriorCorner(Camera cam, Transform building)
    {
        Bounds? b = BoundsOf(building);
        if (b == null) return;
        Bounds bounds = b.Value;
        Vector3 focus = new Vector3(bounds.min.x, bounds.center.y * 0.6f, bounds.min.z);
        cam.transform.position = focus + new Vector3(-2.2f, 1.8f, -2.2f);
        cam.transform.LookAt(focus + Vector3.up * 0.8f);
        cam.orthographic = false;
        cam.fieldOfView = 50f;
    }

    static Bounds? BoundsOf(Transform building)
    {
        Bounds? b = null;
        foreach (var r in building.GetComponentsInChildren<Renderer>())
        {
            if (b == null) b = r.bounds;
            else
            {
                Bounds bb = b.Value;
                bb.Encapsulate(r.bounds);
                b = bb;
            }
        }
        return b;
    }

    static void Capture(Camera cam, string fileName)
    {
        string path = Full(Path.Combine(ShotDir, fileName));
        int w = 1280;
        int h = 720;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = prev;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }

    static void RestoreCam(Camera cam, Vector3 pos, Quaternion rot, float fov, bool ortho, float orthoSize)
    {
        cam.transform.position = pos;
        cam.transform.rotation = rot;
        cam.fieldOfView = fov;
        cam.orthographic = ortho;
        cam.orthographicSize = orthoSize;
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}
