using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch scan: Object-type GridFootprint assets must be exactly 1×1.
/// Report-only for already-correct sizes; surfaces any that OnValidate would clamp.
/// Does not mutate CarryableItem / physics / Rigidbody.
/// Flag: Temp/run_grid_object_scan.flag
/// </summary>
[InitializeOnLoad]
public static class GridObjectFootprintBatchScan
{
    const string ReportPath = "Temp/grid_object_footprint_scan.txt";
    const string FlagPath = "Temp/run_grid_object_scan.flag";

    static GridObjectFootprintBatchScan()
    {
        EditorApplication.delayCall += TryFlag;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryFlag();
    }

    static void TryFlag()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        File.Delete(flag);
        string report = RunScan();
        WriteReport(report);
        Debug.Log("[GridObjectFootprintBatchScan]\n" + report);
    }

    [MenuItem("Tools/Grid/Scan Object Footprints (batch)")]
    public static void ScanFromMenu()
    {
        string report = RunScan();
        WriteReport(report);
        EditorUtility.DisplayDialog("Object Footprint Scan", Truncate(report, 1400), "OK");
        Debug.Log("[GridObjectFootprintBatchScan]\n" + report);
    }

    public static string RunScan()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Object GridFootprint batch scan ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");

        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        int scanned = 0;
        int objectTyped = 0;
        var violations = new List<string>();
        var ok = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            GridFootprint[] footprints = prefab.GetComponentsInChildren<GridFootprint>(true);
            if (footprints == null || footprints.Length == 0)
                continue;

            scanned++;
            foreach (GridFootprint fp in footprints)
            {
                if (fp.type != AssetGridType.Object)
                    continue;

                objectTyped++;
                string loc = $"{path} :: {GetHierarchyPath(fp.transform)} size={fp.sizeInCells}";
                if (fp.sizeInCells != Vector2Int.one)
                    violations.Add(loc);
                else
                    ok.Add(loc);
            }
        }

        GridFootprint[] sceneFps = Object.FindObjectsByType<GridFootprint>(FindObjectsInactive.Include);
        foreach (GridFootprint fp in sceneFps)
        {
            if (fp.type != AssetGridType.Object)
                continue;
            if (PrefabUtility.IsPartOfPrefabAsset(fp))
                continue;

            objectTyped++;
            string scene = fp.gameObject.scene.IsValid() ? fp.gameObject.scene.path : "(unsaved)";
            string loc = $"{scene} :: {GetHierarchyPath(fp.transform)} size={fp.sizeInCells}";
            if (fp.sizeInCells != Vector2Int.one)
                violations.Add(loc + " [scene]");
            else
                ok.Add(loc + " [scene]");
        }

        sb.AppendLine($"prefabsWithAnyFootprint={scanned}");
        sb.AppendLine($"objectTypedEntries={objectTyped}");
        sb.AppendLine($"violations={violations.Count}");
        sb.AppendLine("--- violations (Object size != 1x1) ---");
        if (violations.Count == 0)
            sb.AppendLine("(none)");
        else
            foreach (string v in violations)
                sb.AppendLine("FAIL " + v);

        sb.AppendLine("--- ok Object 1x1 ---");
        foreach (string o in ok)
            sb.AppendLine("OK " + o);

        sb.AppendLine(violations.Count == 0 ? "RESULT=PASS" : "RESULT=FAIL");
        return sb.ToString();
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static void WriteReport(string report)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
    }

    static string GetHierarchyPath(Transform t)
    {
        var parts = new List<string>();
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max) + "\n…";
    }
}
