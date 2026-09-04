using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 157 — verify Object Browser lists CreamChair_Raw153 + orange
/// office chair, search "cream" narrows, click selects the Properties SO.
/// Flag: Temp/bake157_verify.flag
/// Report: BlenderSource/Bake/_bake157_unity_report.txt
/// </summary>
[InitializeOnLoad]
public static class Bake157Verify
{
    const string CreamPath =
        "Assets/Objects/Pickable/CreamChair_Raw153/CreamChair_Raw153_Properties.asset";
    const string OrangePath =
        "Assets/Objects/Pickable/Meshy_AI_Orange_Office_Chair_0826075021_texture_fbx/Meshy_AI_Orange_Office_Chair_0826075021_texture_fbx_Properties.asset";
    const string FlagPath = "Temp/bake157_verify.flag";
    const string ReportPath = "BlenderSource/Bake/_bake157_unity_report.txt";

    static Bake157Verify()
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

    [MenuItem("Tools/Objects/Verify 157 Object Browser")]
    public static void MenuVerify()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Ticket 157", Truncate(report, 1600), "OK");
    }

    public static void RunFromBatch()
    {
        string report = Run();
        WriteReport(report);
        Debug.Log("[Bake157Verify]\n" + report);
        if (!report.Contains("PASS=True"))
            EditorApplication.Exit(1);
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
            Debug.Log("[Bake157Verify]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Bake157Verify] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 157 Object Browser verify ===");

        var all = new List<ObjectBrowserWindow.BrowserRow>();
        ObjectBrowserWindow.CollectAll(all);
        sb.AppendLine("listedCount=" + all.Count);
        for (int i = 0; i < all.Count; i++)
            sb.AppendLine("  " + all[i].Name + " | " + all[i].Kind + " | " +
                          (all[i].Approved ? "APPROVED" : "PENDING") + " | " +
                          all[i].SelectPath);

        bool creamListed = ContainsPath(all, CreamPath);
        bool orangeListed = ContainsPath(all, OrangePath);
        sb.AppendLine("CHECK cream listed: " + creamListed);
        sb.AppendLine("CHECK orange listed: " + orangeListed);

        var creamHits = new List<ObjectBrowserWindow.BrowserRow>();
        ObjectBrowserWindow.Filter(all, "cream", creamHits);
        bool creamInSearch = ContainsPath(creamHits, CreamPath);
        bool orangeInSearch = ContainsPath(creamHits, OrangePath);
        sb.AppendLine("searchCreamCount=" + creamHits.Count);
        sb.AppendLine("CHECK search cream includes cream chair: " + creamInSearch);
        sb.AppendLine("CHECK search cream excludes orange chair: " + !orangeInSearch);

        bool pinged = ObjectBrowserWindow.PingProperties(CreamPath);
        bool selected = Selection.activeObject != null &&
                        AssetDatabase.GetAssetPath(Selection.activeObject) == CreamPath;
        sb.AppendLine("CHECK click ping cream Properties: " + (pinged && selected));
        if (Selection.activeObject != null)
            sb.AppendLine("selection=" + AssetDatabase.GetAssetPath(Selection.activeObject));

        bool pass = creamListed && orangeListed && creamInSearch && !orangeInSearch &&
                    pinged && selected;
        sb.AppendLine("PASS=" + pass);
        return sb.ToString();
    }

    static bool ContainsPath(List<ObjectBrowserWindow.BrowserRow> rows, string path)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].SelectPath == path)
                return true;
        }
        return false;
    }

    static void WriteReport(string text)
    {
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, text);
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max - 1) + "…";
    }
}
