using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Force-reloads Assets/Scenes/GridFoundation.unity from disk after external edits.
/// Flag: Temp/force_reload_grid_foundation.flag
/// </summary>
[InitializeOnLoad]
public static class ForceReloadGridFoundation
{
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string FlagPath = "Temp/force_reload_grid_foundation.flag";
    const string ReportPath = "Temp/force_reload_grid_foundation_report.txt";

    static ForceReloadGridFoundation()
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

    [MenuItem("Tools/Grid/Force Reload GridFoundation Scene")]
    public static void MenuForceReload()
    {
        string report = ForceReloadAndVerify();
        WriteReport(report);
        EditorUtility.DisplayDialog("Force Reload GridFoundation", report, "OK");
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
            string report = ForceReloadAndVerify();
            WriteReport(report);
            Debug.Log("[ForceReloadGridFoundation]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[ForceReloadGridFoundation] " + e);
        }
    }

    static string ForceReloadAndVerify()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Force reload GridFoundation ===");

        if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ScenePath))))
        {
            // ScenePath is Assets-relative; File.Exists needs full path
        }

        string fullScene = Path.GetFullPath(Path.Combine(Application.dataPath, "Scenes/GridFoundation.unity"));
        if (!File.Exists(fullScene))
        {
            sb.AppendLine("FAIL scene missing on disk: " + ScenePath);
            return sb.ToString();
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine($"opened={scene.path}");

        GridSystem grid = Object.FindAnyObjectByType<GridSystem>();
        sb.AppendLine(grid != null
            ? $"GridSystem OK origin={grid.originWorld} cellSize={grid.cellSize} overlay={grid.GetComponent<GridOverlay>() != null}"
            : "FAIL no GridSystem in scene");

        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        sb.AppendLine(player != null ? "Player OK" : "FAIL no Player");

        bool crate = false;
        bool cube = false;
        int nodes = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t.name == "Item_Crate_Base") crate = true;
            if (t.name == "GridAxisTestCube") cube = true;
        }
        nodes = Object.FindObjectsByType<DefaultNode>(FindObjectsInactive.Include).Length;
        sb.AppendLine(crate ? "Item_Crate_Base OK" : "FAIL no Item_Crate_Base");
        sb.AppendLine(cube ? "GridAxisTestCube OK" : "WARN no GridAxisTestCube");
        sb.AppendLine(nodes >= 4 ? $"DefaultNodes OK count={nodes}" : $"FAIL DefaultNodes count={nodes} (want >=4)");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Grid/GridSystem.prefab");
        sb.AppendLine(prefab != null && prefab.GetComponent<GridSystem>() != null
            ? "Prefab Assets/Prefabs/Grid/GridSystem.prefab OK"
            : "FAIL GridSystem.prefab missing");

        sb.AppendLine(grid != null && player != null && crate && nodes >= 4 ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static void WriteReport(string report)
    {
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
    }
}
