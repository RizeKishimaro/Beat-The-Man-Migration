using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 174 diagnose-only. Reads probe / renderer / material. Does not modify
/// or save anything.
/// Flag: Temp/alpha01_174_diagnose.flag
/// Report: Temp/alpha01_174/diagnose.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_174Diagnose
{
    const string FlagPath = "Temp/alpha01_174_diagnose.flag";
    const string ReportPath = "Temp/alpha01_174/diagnose.txt";

    static Alpha01_174Diagnose()
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

    [MenuItem("Tools/Scenes/174 Diagnose Glossy Floor (read-only)")]
    public static void Menu()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("174 Diagnose", Truncate(report, 1800), "OK");
    }

    static void TryRun()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            return;
        if (EditorApplication.isCompiling)
            return;
        try
        {
            File.Delete(Full(FlagPath));
            string report = Run();
            WriteReport(report);
            Debug.Log("[Alpha01_174Diagnose]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Alpha01_174Diagnose] " + e);
        }
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 174 diagnose (read-only, no save) ===");
        sb.AppendLine("scene=" + SceneManager.GetActiveScene().path
            + " playing=" + EditorApplication.isPlaying
            + " dirty=" + SceneManager.GetActiveScene().isDirty);

        ReflectionProbe[] probes = Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include);
        sb.AppendLine("--- 1 probe count=" + probes.Length);
        foreach (ReflectionProbe probe in probes)
        {
            Bounds b = probe.bounds;
            sb.AppendLine("name=" + probe.name
                + " active=" + probe.gameObject.activeInHierarchy
                + " enabled=" + probe.enabled);
            sb.AppendLine("  mode=" + probe.mode
                + " refresh=" + probe.refreshMode
                + " resolution=" + probe.resolution
                + " boxProjection=" + probe.boxProjection);
            sb.AppendLine("  pos=" + probe.transform.position
                + " size=" + probe.size
                + " center=" + probe.center);
            sb.AppendLine("  worldBounds=" + b.min + " .. " + b.max);
            Texture baked = probe.bakedTexture;
            Texture custom = probe.customBakedTexture;
            Texture tex = probe.texture;
            sb.AppendLine("  bakedTexture=" + TexInfo(baked)
                + " customBakedTexture=" + TexInfo(custom)
                + " texture=" + TexInfo(tex));
            sb.AppendLine("  realtimeReflectionProbes(quality)=" + QualitySettings.realtimeReflectionProbes);
        }

        GameObject floor = GameObject.Find(WorldFloorEnsure.FloorName);
        sb.AppendLine("--- 2 WorldFloor=" + (floor != null));
        if (floor != null)
        {
            var rend = floor.GetComponent<Renderer>();
            sb.AppendLine("  renderer=" + (rend != null)
                + " reflectionProbeUsage=" + (rend != null ? rend.reflectionProbeUsage.ToString() : "n/a")
                + " lightProbeUsage=" + (rend != null ? rend.lightProbeUsage.ToString() : "n/a")
                + " hasMPB=" + (rend != null && rend.HasPropertyBlock()));
            if (rend != null)
            {
                Bounds fb = rend.bounds;
                sb.AppendLine("  floorWorldBounds=" + fb.min + " .. " + fb.max);
                if (probes.Length > 0)
                    sb.AppendLine("  floorInsideProbe0=" + probes[0].bounds.Intersects(fb));
            }
        }

        Material shared = null;
        if (floor != null)
        {
            var rend = floor.GetComponent<Renderer>();
            if (rend != null)
                shared = rend.sharedMaterial;
        }
        sb.AppendLine("--- 3/4 material");
        DumpMat(sb, "shared", shared);
        sb.AppendLine("scene dirty=" + SceneManager.GetActiveScene().isDirty + " (diagnose must not save)");
        return sb.ToString();
    }

    static void DumpMat(StringBuilder sb, string label, Material mat)
    {
        if (mat == null)
        {
            sb.AppendLine("  " + label + "=null");
            return;
        }
        string shader = mat.shader != null ? mat.shader.name : "null";
        bool envOff = mat.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF");
        float env = mat.HasProperty("_EnvironmentReflections") ? mat.GetFloat("_EnvironmentReflections") : -1f;
        float smooth = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : -1f;
        float metal = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : -1f;
        Color c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
        sb.AppendLine("  " + label + " name=" + mat.name
            + " shader=" + shader
            + " envReflectionsProp=" + env
            + " keyword_ENVIRONMENTREFLECTIONS_OFF=" + envOff
            + " smoothness=" + smooth
            + " metallic=" + metal
            + " baseColor=" + c);
    }

    static string TexInfo(Texture t)
    {
        if (t == null)
            return "NULL";
        return t.name + " type=" + t.GetType().Name + " " + t.width + "x" + t.height;
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
