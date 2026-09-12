using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 174 — swap Alpha01 WorldFloor off 168 grass onto a glossy URP Lit mat,
/// add one Reflection Probe over the origin grid that actually captures the scene
/// (Baked with lighting data, or Realtime + one RenderProbe at Start). Does not
/// leave the probe on Custom. Does not delete grass assets. Does not save
/// GridFoundation. Does not spawn rooms.
/// Flag: Temp/alpha01_174_setup.flag
/// Report: Temp/alpha01_174/setup.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_174SceneSetup
{
    public const string ScenePath = Alpha01SceneSetup.ScenePath;
    public const string GrassMatPath = "Assets/Art/Environment/Grass_Mat.mat";
    public const string GrassTexPath = "Assets/Art/Environment/Grass_Albedo.png";
    public const string GlossyMatPath = "Assets/Art/Environment/GlossyFloor_Mat.mat";
    public const string ProbeCubemapPath = "Assets/Art/Environment/GlossyFloor_Probe.exr";
    public const string LightingSettingsPath = "Assets/Settings/Alpha01_LightingSettings.lighting";
    public const string ProbeName = "ReflectionProbe_Ground";
    public const float Metallic = 0.04f;
    public const float Smoothness = 0.55f;
    public const float SunIntensity = 0.95f;
    public const float AmbientIntensity = 1.2f;
    public static readonly Color BaseColor = new Color(0.90f, 0.87f, 0.83f, 1f);

    const string FlagPath = "Temp/alpha01_174_setup.flag";
    const string ReportPath = "Temp/alpha01_174/setup.txt";
    const string PlaygroundPath = "Assets/Scenes/GridFoundation.unity";

    static Alpha01_174SceneSetup()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRun();
    }

    [MenuItem("Tools/Scenes/174 Setup Alpha01 Glossy Floor")]
    public static void MenuSetup()
    {
        string report = RunSetup();
        WriteReport(report);
        EditorUtility.DisplayDialog("174 Setup", Truncate(report, 1600), "OK");
    }

    static void TryRun()
    {
        if (!File.Exists(Full(FlagPath)))
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
            File.Delete(Full(FlagPath));
            string report = RunSetup();
            WriteReport(report);
            Debug.Log("[Alpha01_174SceneSetup]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Alpha01_174SceneSetup] " + e);
        }
    }

    public static string RunSetup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 174 Alpha01 glossy floor ===");
        sb.AppendLine("does not save GridFoundation");
        sb.AppendLine("does not delete grass assets");
        sb.AppendLine("does not spawn rooms");
        sb.AppendLine("revert: assign " + GrassMatPath + " back on WorldFloor");

        Scene current = SceneManager.GetActiveScene();
        if (current.path == PlaygroundPath && current.isDirty)
        {
            EditorSceneManager.OpenScene(PlaygroundPath, OpenSceneMode.Single);
            sb.AppendLine("discarded unsaved GridFoundation dirt (did not save playground)");
        }

        if (AssetDatabase.LoadAssetAtPath<Material>(GrassMatPath) == null)
        {
            sb.AppendLine("FAIL missing " + GrassMatPath);
            return sb.ToString();
        }
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexPath) == null)
            sb.AppendLine("WARN missing " + GrassTexPath + " (left in place if present)");
        else
            sb.AppendLine("kept grass tex=" + GrassTexPath);

        Material glossy = EnsureGlossyMat(sb);
        if (glossy == null)
            return sb.ToString();

        if (!File.Exists(Full(ScenePath)))
        {
            sb.AppendLine("FAIL missing " + ScenePath);
            return sb.ToString();
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("opened " + scene.path);

        GameObject floor = GameObject.Find(WorldFloorEnsure.FloorName);
        if (floor == null)
        {
            sb.AppendLine("FAIL missing WorldFloor");
            return sb.ToString();
        }

        var rend = floor.GetComponent<Renderer>();
        if (rend == null)
        {
            sb.AppendLine("FAIL WorldFloor has no Renderer");
            return sb.ToString();
        }

        string before = rend.sharedMaterial != null ? rend.sharedMaterial.name : "null";
        rend.sharedMaterial = glossy;
        sb.AppendLine("WorldFloor mat " + before + " -> " + glossy.name + " (" + GlossyMatPath + ")");

        Light sun = null;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (l != null && l.type == LightType.Directional)
            {
                sun = l;
                break;
            }
        }
        if (sun != null)
        {
            float was = sun.intensity;
            sun.intensity = SunIntensity;
            RenderSettings.sun = sun;
            RenderSettings.ambientIntensity = AmbientIntensity;
            sb.AppendLine("directional " + sun.name + " intensity " + was + " -> " + sun.intensity
                + " ambientIntensity=" + RenderSettings.ambientIntensity);
        }
        else
            sb.AppendLine("WARN no directional light");

        Vector3 floorScale = floor.transform.localScale;
        Vector3 probeSize = new Vector3(
            Mathf.Max(96f, Mathf.Abs(floorScale.x)),
            20f,
            Mathf.Max(96f, Mathf.Abs(floorScale.z)));

        GameObject probeGo = GameObject.Find(ProbeName);
        if (probeGo != null)
            Object.DestroyImmediate(probeGo);
        probeGo = new GameObject(ProbeName);
        SceneManager.MoveGameObjectToScene(probeGo, scene);
        probeGo.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.identity);

        ReflectionProbe probe = probeGo.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Baked;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.resolution = 128;
        probe.hdr = true;
        probe.boxProjection = true;
        probe.size = probeSize;
        probe.center = Vector3.zero;
        probe.nearClipPlane = 0.3f;
        probe.farClipPlane = 100f;
        probe.intensity = 1f;
        probe.importance = 1;
        probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        probe.customBakedTexture = null;
        sb.AppendLine("probe pos=" + probeGo.transform.position
            + " size=" + probe.size + " mode=" + probe.mode + " res=" + probe.resolution
            + " boxProjection=" + probe.boxProjection);

        CaptureProbeIntoScene(probe, sb);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved=" + saved + " " + ScenePath);

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("reloaded " + SceneManager.GetActiveScene().path);
        CaptureEditShot(sb);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("reloaded after edit shot (warp not saved)");

        GameObject floor2 = GameObject.Find(WorldFloorEnsure.FloorName);
        var rend2 = floor2 != null ? floor2.GetComponent<Renderer>() : null;
        string liveMat = rend2 != null && rend2.sharedMaterial != null ? rend2.sharedMaterial.name : "null";
        ReflectionProbe liveProbe = Object.FindAnyObjectByType<ReflectionProbe>();
        bool grassStillThere = AssetDatabase.LoadAssetAtPath<Material>(GrassMatPath) != null;
        bool probeOk = liveProbe != null
            && liveProbe.mode != ReflectionProbeMode.Custom
            && ProbeHasCapture(liveProbe);
        bool ok = saved
            && SceneManager.GetActiveScene().path == ScenePath
            && liveMat == "GlossyFloor_Mat"
            && probeOk
            && grassStillThere;
        sb.AppendLine("live mat=" + liveMat + " probe=" + (liveProbe != null)
            + " probeMode=" + (liveProbe != null ? liveProbe.mode.ToString() : "n/a")
            + " bakedTexture=" + TexName(liveProbe != null ? liveProbe.bakedTexture : null)
            + " customTex=" + TexName(liveProbe != null ? liveProbe.customBakedTexture : null)
            + " texture=" + TexName(liveProbe != null ? liveProbe.texture : null)
            + " captureOnce=" + (liveProbe != null && liveProbe.GetComponent<ReflectionProbeCaptureOnce>() != null)
            + " grassKept=" + grassStillThere);
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static void CaptureProbeIntoScene(ReflectionProbe probe, StringBuilder sb)
    {
        EnsureLightingSettings(sb);

        Directory.CreateDirectory(Path.GetDirectoryName(Full(ProbeCubemapPath)));
        bool cubemapBake = Lightmapping.BakeReflectionProbe(probe, ProbeCubemapPath);
        AssetDatabase.ImportAsset(ProbeCubemapPath);
        var importer = AssetImporter.GetAtPath(ProbeCubemapPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }
        Texture cube = AssetDatabase.LoadAssetAtPath<Cubemap>(ProbeCubemapPath);
        if (cube == null)
            cube = AssetDatabase.LoadAssetAtPath<Texture>(ProbeCubemapPath);
        probe.mode = ReflectionProbeMode.Baked;
        probe.customBakedTexture = null;
        if (cube != null)
            probe.bakedTexture = cube;
        sb.AppendLine("BakeReflectionProbe=" + cubemapBake
            + " cubemap=" + TexName(cube)
            + " path=" + ProbeCubemapPath
            + " bakedTextureAfterAssign=" + TexName(probe.bakedTexture)
            + " mode=" + probe.mode);

        sb.AppendLine("saving then Lightmapping.Bake() so lighting data holds bakedTexture");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
        bool gi = Lightmapping.Bake();
        LightingDataAsset lda = null;
        try { lda = Lightmapping.lightingDataAsset; }
        catch (System.Exception e) { sb.AppendLine("lightingDataAsset THROW " + e.Message); }
        sb.AppendLine("Lightmapping.Bake=" + gi
            + " lightingData=" + (lda != null ? lda.name + " path=" + AssetDatabase.GetAssetPath(lda) : "NULL")
            + " bakedTexture=" + TexName(probe.bakedTexture)
            + " texture=" + TexName(probe.texture));

        if (probe.bakedTexture != null && probe.mode == ReflectionProbeMode.Baked)
        {
            var leftover = probe.GetComponent<ReflectionProbeCaptureOnce>();
            if (leftover != null)
                Object.DestroyImmediate(leftover);
            sb.AppendLine("using Baked probe capture bakedTexture=" + TexName(probe.bakedTexture));
            return;
        }

        sb.AppendLine("Baked path did not populate bakedTexture; switching to Realtime + RenderProbe once");
        QualitySettings.realtimeReflectionProbes = true;
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        if (probe.GetComponent<ReflectionProbeCaptureOnce>() == null)
            probe.gameObject.AddComponent<ReflectionProbeCaptureOnce>();
        int renderId = probe.RenderProbe();
        sb.AppendLine("realtimeReflectionProbes=" + QualitySettings.realtimeReflectionProbes
            + " editorRenderProbeId=" + renderId
            + " texture=" + TexName(probe.texture)
            + " bakedTexture=" + TexName(probe.bakedTexture));
    }

    static void EnsureLightingSettings(StringBuilder sb)
    {
        LightingSettings ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
        if (ls == null)
        {
            ls = new LightingSettings();
            AssetDatabase.CreateAsset(ls, LightingSettingsPath);
            ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
        }
        if (ls == null)
        {
            sb.AppendLine("WARN could not create " + LightingSettingsPath);
            return;
        }
        ls.bakedGI = true;
        ls.realtimeGI = false;
        EditorUtility.SetDirty(ls);
        Lightmapping.lightingSettings = ls;
        sb.AppendLine("lightingSettings=" + LightingSettingsPath
            + " bakedGI=" + ls.bakedGI
            + " realtimeGI=" + ls.realtimeGI);
    }

    static bool ProbeHasCapture(ReflectionProbe probe)
    {
        if (probe == null)
            return false;
        if (probe.mode == ReflectionProbeMode.Baked)
            return probe.bakedTexture != null;
        if (probe.mode == ReflectionProbeMode.Realtime)
            return probe.GetComponent<ReflectionProbeCaptureOnce>() != null
                && QualitySettings.realtimeReflectionProbes;
        return false;
    }

    static string TexName(Texture t)
    {
        return t != null ? t.name : "NULL";
    }

    static Material EnsureGlossyMat(StringBuilder sb)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
        {
            sb.AppendLine("FAIL URP Lit shader missing");
            return null;
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(GlossyMatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, GlossyMatPath);
            mat = AssetDatabase.LoadAssetAtPath<Material>(GlossyMatPath);
        }
        if (mat.shader != shader)
            mat.shader = shader;

        mat.SetTexture("_BaseMap", null);
        mat.SetTexture("_MainTex", null);
        mat.SetColor("_BaseColor", BaseColor);
        mat.SetColor("_Color", BaseColor);
        mat.SetFloat("_Metallic", Metallic);
        mat.SetFloat("_Smoothness", Smoothness);
        if (mat.HasProperty("_EnvironmentReflections"))
            mat.SetFloat("_EnvironmentReflections", 1f);
        if (mat.HasProperty("_GlossyReflections"))
            mat.SetFloat("_GlossyReflections", 1f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        sb.AppendLine("mat=" + GlossyMatPath
            + " shader=" + mat.shader.name
            + " baseColor=" + BaseColor
            + " metallic=" + Metallic
            + " smoothness=" + Smoothness
            + " inspector=WorldFloor material Base Map colour");
        return mat;
    }

    static void CaptureEditShot(StringBuilder sb)
    {
        PlayerController p = Object.FindAnyObjectByType<PlayerController>();
        if (p != null)
            p.transform.position = new Vector3(4f, 0f, 4f);
        Camera cam = Camera.main;
        if (cam == null)
        {
            sb.AppendLine("WARN edit shot no Camera.main");
            return;
        }
        var follow = cam.GetComponent<CameraFollow>();
        if (follow != null)
            follow.enabled = true;
        string dir = Full("Temp/alpha01_174");
        Directory.CreateDirectory(dir);
        int w = 1600;
        int h = 1000;
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
        string path = Path.Combine(dir, "slot_edit.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        sb.AppendLine("shot slot_edit " + tex.width + "x" + tex.height + " bytes=" + new FileInfo(path).Length);
        Object.DestroyImmediate(tex);
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }

    static void WriteReport(string text)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, text ?? "");
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max) + "\n…";
    }
}
