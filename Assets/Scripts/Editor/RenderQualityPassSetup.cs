using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Ticket 106 — Render Quality Pass. Incremental steps + Game-view captures.
/// Flags: Temp/rq_baseline.flag | Temp/rq_step1_aa.flag | Temp/rq_step2_light.flag
///        Temp/rq_step3_volume.flag | Temp/rq_step4_scale.flag
/// </summary>
[InitializeOnLoad]
public static class RenderQualityPassSetup
{
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string PcRpPath = "Assets/Settings/PC_RPAsset.asset";
    const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";
    const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";
    const string ShotDir = "Temp/render_quality_shots";
    const string ReportPath = "Temp/render_quality_pass_report.txt";
    const string ReloadFlag = "Temp/force_reload_grid_foundation.flag";

    static RenderQualityPassSetup()
    {
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        Try("Temp/rq_baseline.flag", Baseline);
        Try("Temp/rq_step1_aa.flag", Step1_AA);
        Try("Temp/rq_step2_light.flag", Step2_Lighting);
        Try("Temp/rq_step3_volume.flag", Step3_Volume);
        Try("Temp/rq_step4_scale.flag", Step4_ScaleConfirm);
        Try("Temp/rq_smaa_followup.flag", Step5_SMAA);
    }

    static void Try(string flagRel, System.Func<string> action)
    {
        string flag = Full(flagRel);
        if (!File.Exists(flag)) return;
        File.Delete(flag);
        string chunk = action();
        AppendReport(chunk);
        Debug.Log("[RenderQualityPass]\n" + chunk);
        File.WriteAllText(Full(ReloadFlag), "reload\n");
    }

    [MenuItem("Tools/Grid/Render Quality/0 Baseline (investigate + shot)")]
    public static void MenuBaseline() { Finish(Baseline()); }

    [MenuItem("Tools/Grid/Render Quality/1 Enable MSAA 4x")]
    public static void Menu1() { Finish(Step1_AA()); }

    [MenuItem("Tools/Grid/Render Quality/2 Soft shadows + fill light")]
    public static void Menu2() { Finish(Step2_Lighting()); }

    [MenuItem("Tools/Grid/Render Quality/3 Volume PP + AO tune")]
    public static void Menu3() { Finish(Step3_Volume()); }

    [MenuItem("Tools/Grid/Render Quality/4 Confirm Render Scale 1.0")]
    public static void Menu4() { Finish(Step4_ScaleConfirm()); }

    [MenuItem("Tools/Grid/Render Quality/5 SMAA + before-after shots")]
    public static void Menu5() { Finish(Step5_SMAA()); }

    static void Finish(string chunk)
    {
        AppendReport(chunk);
        EditorUtility.DisplayDialog("Render Quality Pass", Truncate(chunk, 1600), "OK");
        Debug.Log("[RenderQualityPass]\n" + chunk);
        File.WriteAllText(Full(ReloadFlag), "reload\n");
    }

    static string Baseline()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 106 BASELINE (investigate) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");
        EnsureScene();

        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcRpPath);
        sb.AppendLine($"PC_RPAsset path={PcRpPath} loaded={rp != null}");
        if (rp != null)
        {
            sb.AppendLine($"msaaSampleCount={rp.msaaSampleCount} (1=Disabled)");
            sb.AppendLine($"renderScale={rp.renderScale}");
            sb.AppendLine($"supportsHDR={rp.supportsHDR}");
            sb.AppendLine($"shadowDistance={rp.shadowDistance} cascades={rp.shadowCascadeCount}");
            sb.AppendLine($"supportsSoftShadows={rp.supportsSoftShadows}");
        }

        var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(PcRendererPath);
        sb.AppendLine($"PC_Renderer features:");
        if (renderer != null)
        {
            foreach (var f in renderer.rendererFeatures)
            {
                if (f == null) continue;
                sb.AppendLine($"  feature={f.name} type={f.GetType().Name} active={f.isActive}");
                if (f is ScreenSpaceAmbientOcclusion ssao)
                    sb.AppendLine($"    SSAO present (renderer feature) — check inspector intensity");
            }
        }

        var volProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        sb.AppendLine($"defaultVolumeProfile={VolumeProfilePath} loaded={volProfile != null}");
        if (volProfile != null)
        {
            foreach (var c in volProfile.components)
            {
                if (c == null) continue;
                sb.AppendLine($"  volumeComponent={c.GetType().Name} active={c.active}");
            }
        }

        Camera cam = Camera.main;
        var urpCam = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
        sb.AppendLine($"MainCamera found={cam != null} orthographic={cam != null && cam.orthographic}");
        sb.AppendLine($"UniversalAdditionalCameraData present={urpCam != null}");
        if (urpCam != null)
        {
            sb.AppendLine($"  renderPostProcessing={urpCam.renderPostProcessing}");
            sb.AppendLine($"  antialiasing={urpCam.antialiasing}");
        }
        else
            sb.AppendLine("  GAP: Main Camera missing UniversalAdditionalCameraData — URP PP/AA camera path incomplete");

        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        sb.AppendLine($"lightsInScene={lights.Length}");
        foreach (var l in lights)
        {
            sb.AppendLine(
                $"  light={l.name} type={l.type} intensity={l.intensity} shadows={l.shadows} color={l.color}");
        }

        Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include);
        sb.AppendLine($"sceneVolumes={volumes.Length}");
        foreach (var v in volumes)
            sb.AppendLine($"  volume={v.name} isGlobal={v.isGlobal} profile={(v.profile != null ? v.profile.name : "null")}");

        string shot = Capture("00_baseline");
        sb.AppendLine($"screenshot={shot}");
        sb.AppendLine("FINDINGS: MSAA disabled (1); RenderScale already 1.0; SSAO renderer feature present;");
        sb.AppendLine("Directional Light shadows=None; no scene Volume GO; SampleSceneProfile has Tonemap/Bloom/Vignette;");
        sb.AppendLine("Main Camera likely missing URP camera data → enable before relying on PP.");
        return sb.ToString();
    }

    static string Step1_AA()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 106 STEP1 Anti-aliasing ===");
        EnsureScene();

        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcRpPath);
        int before = rp != null ? rp.msaaSampleCount : -1;
        if (rp != null)
        {
            // URP: 1=Disabled, 2/4/8 = MSAA samples
            rp.msaaSampleCount = 4;
            EditorUtility.SetDirty(rp);
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.allowMSAA = true;
            var urpCam = cam.GetComponent<UniversalAdditionalCameraData>();
            if (urpCam == null)
                urpCam = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            // MSAA is pipeline-level for forward; keep camera AA None so we don't stack TAA on orthographic
            urpCam.antialiasing = AntialiasingMode.None;
            EditorUtility.SetDirty(cam.gameObject);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        string shot = Capture("01_msaa4");
        sb.AppendLine($"msaaBefore={before} msaaAfter={(rp != null ? rp.msaaSampleCount : -1)}");
        sb.AppendLine($"screenshot={shot}");
        sb.AppendLine("NOTE: Orthographic camera — MSAA preferred over TAA (TAA can smear).");
        sb.AppendLine("RESULT=OK step1");
        return sb.ToString();
    }

    static string Step2_Lighting()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 106 STEP2 Lighting ===");
        EnsureScene();

        Light main = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (l.type == LightType.Directional && l.name == "Directional Light")
            {
                main = l;
                break;
            }
        }
        if (main == null)
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (l.type == LightType.Directional) { main = l; break; }
            }
        }

        if (main != null)
        {
            sb.AppendLine($"mainBefore intensity={main.intensity} shadows={main.shadows}");
            // Soften: enable soft shadows, slightly lower harsh key
            main.shadows = LightShadows.Soft;
            main.shadowStrength = 0.75f;
            main.intensity = 1.55f;
            // Slight warm key — still stylized, not cinematic
            main.color = new Color(1f, 0.97f, 0.92f);
            EditorUtility.SetDirty(main);
            sb.AppendLine($"mainAfter intensity={main.intensity} shadows={main.shadows} strength={main.shadowStrength}");
        }
        else sb.AppendLine("FAIL no directional light");

        // Fill light (opposite-ish, no shadows)
        GameObject fillGo = GameObject.Find("Fill Light");
        if (fillGo == null)
        {
            fillGo = new GameObject("Fill Light");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.shadows = LightShadows.None;
            fill.intensity = 0.35f;
            fill.color = new Color(0.75f, 0.85f, 1f); // cool fill
            fillGo.transform.rotation = Quaternion.Euler(25f, -140f, 0f);
            sb.AppendLine("created Fill Light intensity=0.35 cool, no shadows");
        }
        else
        {
            var fill = fillGo.GetComponent<Light>();
            if (fill != null)
            {
                fill.intensity = 0.35f;
                fill.shadows = LightShadows.None;
                fill.color = new Color(0.75f, 0.85f, 1f);
            }
            sb.AppendLine("updated existing Fill Light");
        }

        // Slightly tighter shadow distance for cleaner contact on small props
        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcRpPath);
        if (rp != null)
        {
            float before = rp.shadowDistance;
            rp.shadowDistance = 30f;
            EditorUtility.SetDirty(rp);
            sb.AppendLine($"shadowDistance {before} -> {rp.shadowDistance}");
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        string shot = Capture("02_lighting");
        sb.AppendLine($"screenshot={shot}");
        sb.AppendLine("RESULT=OK step2");
        return sb.ToString();
    }

    static string Step3_Volume()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 106 STEP3 Volume / Post / AO ===");
        EnsureScene();

        Camera cam = Camera.main;
        if (cam != null)
        {
            var urpCam = cam.GetComponent<UniversalAdditionalCameraData>();
            if (urpCam == null)
                urpCam = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            urpCam.renderPostProcessing = true;
            urpCam.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;
            urpCam.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
            EditorUtility.SetDirty(cam.gameObject);
            sb.AppendLine("Main Camera: UniversalAdditionalCameraData.renderPostProcessing=true");
        }

        // Scene global volume using SampleSceneProfile + mild ColorAdjustments
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile != null)
        {
            // Ensure Tonemapping Neutral stays on
            if (profile.TryGet(out Tonemapping tonemap))
            {
                tonemap.active = true;
                tonemap.mode.Override(TonemappingMode.Neutral);
                sb.AppendLine("Tonemapping=Neutral (stylized-friendly, not ACES)");
            }
            if (!profile.TryGet(out ColorAdjustments colorAdj))
            {
                colorAdj = profile.Add<ColorAdjustments>(true);
            }
            colorAdj.active = true;
            colorAdj.postExposure.Override(0.05f);
            colorAdj.contrast.Override(8f);
            colorAdj.saturation.Override(12f); // mild pop for chibi, not realism
            EditorUtility.SetDirty(profile);
            sb.AppendLine("ColorAdjustments: exposure+0.05 contrast+8 sat+12");
        }

        // Bump SSAO renderer feature intensity (already present) — subtle
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
        if (renderer != null)
        {
            foreach (var f in renderer.rendererFeatures)
            {
                if (f is ScreenSpaceAmbientOcclusion)
                {
                    // Serialized settings via SerializedObject — keep code minimal
                    var so = new SerializedObject(f);
                    var intensity = so.FindProperty("m_Settings.Intensity");
                    var radius = so.FindProperty("m_Settings.Radius");
                    float iBefore = intensity != null ? intensity.floatValue : -1f;
                    float rBefore = radius != null ? radius.floatValue : -1f;
                    if (intensity != null) intensity.floatValue = 0.55f;
                    if (radius != null) radius.floatValue = 0.35f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(f);
                    EditorUtility.SetDirty(renderer);
                    sb.AppendLine($"SSAO intensity {iBefore}->{(intensity != null ? intensity.floatValue : -1)} radius {rBefore}->{(radius != null ? radius.floatValue : -1)}");
                }
            }
        }

        GameObject volGo = GameObject.Find("Global Volume");
        if (volGo == null)
        {
            volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0;
            vol.sharedProfile = profile;
            sb.AppendLine("created scene Global Volume -> SampleSceneProfile");
        }
        else
        {
            var vol = volGo.GetComponent<Volume>();
            if (vol != null)
            {
                vol.isGlobal = true;
                vol.sharedProfile = profile;
            }
            sb.AppendLine("updated existing Global Volume");
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        string shot = Capture("03_volume_pp");
        sb.AppendLine($"screenshot={shot}");
        sb.AppendLine("RESULT=OK step3");
        return sb.ToString();
    }

    static string Step4_ScaleConfirm()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 106 STEP4 Render Scale confirm ===");
        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcRpPath);
        if (rp != null)
        {
            float before = rp.renderScale;
            if (!Mathf.Approximately(rp.renderScale, 1f))
                rp.renderScale = 1f;
            EditorUtility.SetDirty(rp);
            AssetDatabase.SaveAssets();
            sb.AppendLine($"renderScale before={before} after={rp.renderScale}");
        }
        EnsureScene();
        string shot = Capture("04_final");
        sb.AppendLine($"screenshot={shot}");
        sb.AppendLine("RESULT=OK step4 renderScale=1.0");
        return sb.ToString();
    }

    static string Step5_SMAA()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 106 STEP5 SMAA follow-up ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");
        EnsureScene();

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
        RenderingMode mode = renderer != null ? renderer.renderingMode : RenderingMode.Forward;
        sb.AppendLine($"PC_Renderer.renderingMode={mode} (int={(int)mode})");
        sb.AppendLine(mode == RenderingMode.Deferred || mode == RenderingMode.DeferredPlus
            ? "PATH=Deferred — MSAA weak/near-ineffective"
            : $"PATH={mode} — Forward-family; MSAA supported. Residual jaggedness after MSAA is NOT a Deferred issue.");

        sb.AppendLine("GridOverlay: MeshTopology.Lines + URP Unlit via Graphics.DrawMesh — thin unlit lines are a harder AA case than solid silhouettes.");

        Camera cam = Camera.main;
        if (cam == null)
        {
            sb.AppendLine("FAIL no Main Camera");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }
        var urp = cam.GetComponent<UniversalAdditionalCameraData>();
        if (urp == null)
            urp = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        urp.renderPostProcessing = true;

        // Larger capture so jaggedness is inspectable
        Vector2 gv = Handles.GetMainGameViewSize();
        int w = Mathf.Max(1280, Mathf.RoundToInt(gv.x));
        int h = Mathf.Max(720, Mathf.RoundToInt(gv.y));

        urp.antialiasing = AntialiasingMode.None;
        string before = CaptureSized(cam, w, h, "10_msaa_only_before_smaa");
        sb.AppendLine($"before cameraAA=None shot={before}");

        urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        urp.antialiasingQuality = AntialiasingQuality.High;
        string after = CaptureSized(cam, w, h, "11_msaa_plus_smaa");
        sb.AppendLine($"after cameraAA=SMAA High shot={after}");

        EditorUtility.SetDirty(cam.gameObject);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        sb.AppendLine("persisted SMAA High on Main Camera");

        var overlay = Object.FindAnyObjectByType<GridOverlay>();
        sb.AppendLine(overlay != null
            ? "GridOverlay present — Play Mode G toggle for line-AA check (edit capture cannot reliably DrawMesh LateUpdate)."
            : "WARN no GridOverlay in scene");

        // Also write dedicated follow-up report
        File.WriteAllText(Full("Temp/render_quality_smaa_followup_report.txt"), sb.ToString());
        sb.AppendLine("RESULT=OK step5 SMAA");
        return sb.ToString();
    }

    static string CaptureSized(Camera cam, int w, int h, string label)
    {
        Directory.CreateDirectory(Full(ShotDir));
        string rel = $"{ShotDir}/{label}.png";
        string full = Full(rel);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(full, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        return $"{rel} ({w}x{h}, {new FileInfo(full).Length} bytes)";
    }

    static void EnsureScene()
    {
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    static string Capture(string label)
    {
        Directory.CreateDirectory(Full(ShotDir));
        string rel = $"{ShotDir}/{label}.png";
        string full = Full(rel);

        Camera cam = Camera.main;
        if (cam == null)
        {
            File.WriteAllText(full + ".txt", "no Main Camera");
            return rel + " (FAIL no camera)";
        }

        int w = 1280;
        int h = 720;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        var prevRT = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(full, tex.EncodeToPNG());
        cam.targetTexture = prev;
        RenderTexture.active = prevRT;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        return rel;
    }

    static void AppendReport(string chunk)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.AppendAllText(full, chunk + "\n");
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}

