using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 161 — after duplicate dest folders are gone, confirm compile-safe
/// stubs and that printer has 159 cell-fit.
/// Flag: Temp/bake161_verify.flag
/// Report: BlenderSource/Bake/_bake161_unity_report.txt
/// </summary>
[InitializeOnLoad]
public static class Bake161Verify
{
    const string PrinterFolder = "Assets/Objects/Pickable/printer";
    const string PrinterProps = "Assets/Objects/Pickable/printer/printer_Properties.asset";
    const string StaleWaterNodes =
        "Assets/Objects/Nodes/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture_fbx";
    const string StalePrinterNodes =
        "Assets/Objects/Nodes/Meshy_AI_Cream_Cushioned_Woode_0901123805_texture_fbx";
    const string FlagPath = "Temp/bake161_verify.flag";
    const string ReportPath = "BlenderSource/Bake/_bake161_unity_report.txt";

    static Bake161Verify()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRun();
    }

    [MenuItem("Tools/Objects/Verify 161 Duplicate Folders + Printer Cell-Fit")]
    public static void MenuVerify()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Ticket 161", Truncate(report, 1600), "OK");
    }

    static void TryRun()
    {
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
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
            string report = Run();
            WriteReport(report);
            Debug.Log("[Bake161Verify]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Bake161Verify] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 161 duplicate-folder / printer cell-fit verify ===");

        bool staleGone = !AssetDatabase.IsValidFolder(StaleWaterNodes)
            && !Directory.Exists(Abs(StaleWaterNodes))
            && !AssetDatabase.IsValidFolder(StalePrinterNodes)
            && !Directory.Exists(Abs(StalePrinterNodes));
        sb.AppendLine("stale Nodes watercooler gone: " + !Directory.Exists(Abs(StaleWaterNodes)));
        sb.AppendLine("stale Nodes printer-source gone: " + !Directory.Exists(Abs(StalePrinterNodes)));
        sb.AppendLine("CHECK stale folders gone: " + staleGone);

        bool unique = StubsAreUnique(sb);
        bool printerOk = CheckPrinterCellFit(sb);

        bool all = staleGone && unique && printerOk;
        sb.AppendLine();
        sb.AppendLine(all ? "RESULT=OK" : "RESULT=FAIL");
        sb.AppendLine("PASS=" + all);
        return sb.ToString();
    }

    static bool StubsAreUnique(StringBuilder sb)
    {
        sb.AppendLine("--- Stub class names ---");
        var seen = new System.Collections.Generic.Dictionary<string, string>(
            System.StringComparer.Ordinal);
        bool unique = true;
        string[] roots =
        {
            "Assets/Objects/Pickable",
            "Assets/Objects/Nodes"
        };
        for (int r = 0; r < roots.Length; r++)
        {
            string abs = Abs(roots[r]);
            if (!Directory.Exists(abs))
                continue;
            foreach (string file in Directory.GetFiles(abs, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                string cls = ExtractClass(text);
                string rel = file.Replace('\\', '/');
                int idx = rel.IndexOf("Assets/Objects/");
                if (idx >= 0)
                    rel = rel.Substring(idx);
                sb.AppendLine(rel + " → " + (cls ?? "(none)"));
                if (string.IsNullOrEmpty(cls))
                    continue;
                if (seen.TryGetValue(cls, out string prior))
                {
                    sb.AppendLine("DUPLICATE class " + cls + " in " + prior + " and " + rel);
                    unique = false;
                }
                else
                    seen[cls] = rel;
            }
        }
        sb.AppendLine("CHECK unique stub classes: " + unique);
        return unique;
    }

    static string ExtractClass(string text)
    {
        const string prefix = "public class ";
        int i = text.IndexOf(prefix, System.StringComparison.Ordinal);
        if (i < 0)
            return null;
        int start = i + prefix.Length;
        int end = text.IndexOf(' ', start);
        int colon = text.IndexOf(':', start);
        if (colon >= 0 && (end < 0 || colon < end))
            end = colon;
        if (end < 0)
            return null;
        return text.Substring(start, end - start).Trim();
    }

    static bool CheckPrinterCellFit(StringBuilder sb)
    {
        sb.AppendLine("--- Printer cell-fit ---");
        var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(PrinterProps);
        if (props == null)
        {
            sb.AppendLine("FAIL missing " + PrinterProps);
            sb.AppendLine("CHECK printer cell-fit: False");
            return false;
        }

        string fbx = ObjectPropertiesPreview.FindFbxFor(props);
        bool computed = ObjectCellFitScaling.TryComputeForFbx(fbx, out float expect, out float maxXZ);
        sb.AppendLine("kind: " + props.kind);
        sb.AppendLine("exemptFromCellFitScaling: " + props.exemptFromCellFitScaling);
        sb.AppendLine("scaleMultiplier: " + props.scaleMultiplier);
        sb.AppendLine("fbx: " + fbx);
        sb.AppendLine("computed fit: " + expect + " maxXZ=" + maxXZ);

        bool ok = props.kind == ObjectKind.Pickable
            && !props.exemptFromCellFitScaling
            && computed
            && maxXZ > 0.0001f
            && Mathf.Abs(props.scaleMultiplier - expect) < 0.001f
            && Mathf.Abs(expect * maxXZ - ObjectCellFitScaling.CellSizeMeters) < 0.02f;
        sb.AppendLine("CHECK printer cell-fit: " + ok);
        return ok;
    }

    static string Abs(string assetPath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }

    static void WriteReport(string text)
    {
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, text ?? "");
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max - 1) + "…";
    }
}
