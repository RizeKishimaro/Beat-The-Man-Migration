using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 181 — read-only dump of live AA / Game-view scale. Does not write assets or scenes.
/// Flag: Temp/alpha01_181.flag
/// Report: Temp/alpha01_181/report.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_181Diagnose
{
    const string FlagPath = "Temp/alpha01_181.flag";
    const string ReportPath = "Temp/alpha01_181/report.txt";

    static Alpha01_181Diagnose()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling)
            return;
        TryRun();
    }

    static void TryRun()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling)
            return;
        File.Delete(Full(FlagPath));
        try
        {
            string report = Run();
            Directory.CreateDirectory(Path.GetDirectoryName(Full(ReportPath)));
            File.WriteAllText(Full(ReportPath), report);
            Debug.Log("[Alpha01_181Diagnose]\n" + report);
        }
        catch (System.Exception e)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Full(ReportPath)));
            File.WriteAllText(Full(ReportPath), "FAIL: " + e);
            Debug.LogError("[Alpha01_181Diagnose] " + e);
        }
    }

    [MenuItem("Tools/Scenes/181 Diagnose AA (read-only)")]
    public static void Menu()
    {
        File.WriteAllText(Full(FlagPath), "run\n");
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 181 AA diagnose (read-only) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");
        sb.AppendLine($"activeScene={SceneManager.GetActiveScene().path}");
        sb.AppendLine($"isPlaying={EditorApplication.isPlaying}");

        int q = QualitySettings.GetQualityLevel();
        var qNames = QualitySettings.names;
        sb.AppendLine($"qualityLevel={q} name={(q >= 0 && q < qNames.Length ? qNames[q] : "?")} count={qNames.Length}");
        sb.AppendLine($"QualitySettings.antiAliasing={QualitySettings.antiAliasing} (built-in; URP uses RP asset MSAA)");

        RenderPipelineAsset qPipe = QualitySettings.renderPipeline;
        RenderPipelineAsset gPipe = GraphicsSettings.currentRenderPipeline;
        sb.AppendLine($"QualitySettings.renderPipeline={(qPipe != null ? qPipe.name : "null")}");
        sb.AppendLine($"GraphicsSettings.currentRenderPipeline={(gPipe != null ? gPipe.name : "null")}");

        var urpAsset = gPipe as UniversalRenderPipelineAsset;
        if (urpAsset == null)
            urpAsset = qPipe as UniversalRenderPipelineAsset;
        if (urpAsset != null)
        {
            sb.AppendLine($"activeURP={urpAsset.name}");
            sb.AppendLine($"urp.msaaSampleCount={urpAsset.msaaSampleCount} (1=Disabled, 2/4/8=MSAA)");
            sb.AppendLine($"urp.renderScale={urpAsset.renderScale}");
            sb.AppendLine($"urp.upscalingFilter={urpAsset.upscalingFilter}");
        }
        else
            sb.AppendLine("FAIL no UniversalRenderPipelineAsset on current quality");

        Camera cam = Camera.main;
        if (cam == null)
            cam = Object.FindAnyObjectByType<Camera>();
        if (cam == null)
            sb.AppendLine("FAIL no camera");
        else
        {
            sb.AppendLine($"camera={cam.name} tag={cam.tag} ortho={cam.orthographic} allowMSAA={cam.allowMSAA}");
            var urp = cam.GetComponent<UniversalAdditionalCameraData>();
            if (urp == null)
                sb.AppendLine("UniversalAdditionalCameraData=MISSING");
            else
            {
                sb.AppendLine($"camera.antialiasing={urp.antialiasing} quality={urp.antialiasingQuality}");
                sb.AppendLine($"camera.renderPostProcessing={urp.renderPostProcessing}");
                sb.AppendLine($"camera.taaSettings.quality={urp.taaSettings.quality}");
            }
        }

        Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include);
        sb.AppendLine($"sceneVolumes={volumes.Length}");
        foreach (Volume v in volumes)
        {
            VolumeProfile p = v.sharedProfile != null ? v.sharedProfile : v.profile;
            sb.AppendLine($"  volume={v.name} global={v.isGlobal} weight={v.weight} profile={(p != null ? p.name : "null")}");
            if (p == null)
                continue;
            foreach (VolumeComponent c in p.components)
            {
                if (c == null)
                    continue;
                string n = c.GetType().Name;
                if (n.IndexOf("Anti", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Taa", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Smaa", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Fxaa", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Temporal", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine($"    AA-like component={n} active={c.active}");
            }
        }

        DumpGameView(sb);
        return sb.ToString();
    }

    static void DumpGameView(StringBuilder sb)
    {
        System.Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
        {
            sb.AppendLine("GameView type missing");
            return;
        }
        EditorWindow gv = EditorWindow.GetWindow(gvType, false, "Game", false);
        if (gv == null)
        {
            sb.AppendLine("GameView window missing");
            return;
        }
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object scale = ReadMember(gv, gvType, "m_ScaleValue", "scaleValue", "m_Size");
        object lowRes = ReadMember(gv, gvType, "m_LowResolutionForAspectRatios", "lowResolutionForAspectRatios");
        object vSync = ReadMember(gv, gvType, "m_VSyncEnabled", "vSyncEnabled");
        Vector2 mainSize = Handles.GetMainGameViewSize();
        sb.AppendLine($"GameView position={gv.position}");
        sb.AppendLine($"GameView.GetMainGameViewSize={mainSize.x}x{mainSize.y}");
        sb.AppendLine($"GameView.scaleOrSize={scale}");
        sb.AppendLine($"GameView.lowResolutionForAspectRatios={lowRes}");
        sb.AppendLine($"GameView.vSyncEnabled={vSync}");

        // Unity 6 GameView often stores zoom in ZoomableArea / m_ZoomArea.m_Scale.
        FieldInfo zoomArea = gvType.GetField("m_ZoomArea", inst)
            ?? gvType.GetField("m_ZoomableArea", inst);
        if (zoomArea != null)
        {
            object za = zoomArea.GetValue(gv);
            if (za != null)
            {
                object zScale = ReadMember(za, za.GetType(), "m_Scale", "scale");
                sb.AppendLine($"GameView.zoomArea.scale={zScale}");
            }
        }

        foreach (FieldInfo f in gvType.GetFields(inst))
        {
            if (f.Name.IndexOf("Scale", System.StringComparison.OrdinalIgnoreCase) >= 0
                || f.Name.IndexOf("Zoom", System.StringComparison.OrdinalIgnoreCase) >= 0
                || f.Name.IndexOf("LowRes", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try { sb.AppendLine($"GameView.field {f.Name}={f.GetValue(gv)}"); }
                catch { }
            }
        }
    }

    static object ReadMember(object obj, System.Type t, params string[] names)
    {
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (string n in names)
        {
            FieldInfo f = t.GetField(n, inst);
            if (f != null)
                return f.GetValue(obj);
            PropertyInfo p = t.GetProperty(n, inst);
            if (p != null)
                return p.GetValue(obj, null);
        }
        return null;
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
