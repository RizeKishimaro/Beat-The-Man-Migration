using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Force-reloads Assets/Scenes/Alpha01.unity from disk.
/// Does not save Alpha01 or GridFoundation.
/// Flag: Temp/force_reload_alpha01.flag
/// Report: Temp/force_reload_alpha01_report.txt
/// </summary>
[InitializeOnLoad]
public static class ForceReloadAlpha01
{
    const string ScenePath = Alpha01SceneSetup.ScenePath;
    const string PlaygroundPath = "Assets/Scenes/GridFoundation.unity";
    const string FlagPath = "Temp/force_reload_alpha01.flag";
    const string ReportPath = "Temp/force_reload_alpha01_report.txt";

    static ForceReloadAlpha01()
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

    [MenuItem("Tools/Scenes/Force Reload Alpha01 Scene")]
    public static void MenuForceReload()
    {
        string report = ForceReloadAndVerify();
        WriteReport(report);
        EditorUtility.DisplayDialog("Force Reload Alpha01", report, "OK");
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
            string report = ForceReloadAndVerify();
            WriteReport(report);
            Debug.Log("[ForceReloadAlpha01]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[ForceReloadAlpha01] " + e);
        }
    }

    static string ForceReloadAndVerify()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Force reload Alpha01 ===");
        sb.AppendLine("does not save Alpha01 or GridFoundation");

        Scene current = SceneManager.GetActiveScene();
        if (current.path == PlaygroundPath && current.isDirty)
        {
            EditorSceneManager.OpenScene(PlaygroundPath, OpenSceneMode.Single);
            sb.AppendLine("discarded unsaved GridFoundation dirt (did not save playground)");
        }

        string fullScene = Path.GetFullPath(Path.Combine(Application.dataPath, "Scenes/Alpha01.unity"));
        if (!File.Exists(fullScene))
        {
            sb.AppendLine("FAIL scene missing on disk: " + ScenePath);
            return sb.ToString();
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("opened=" + scene.path);

        GridSystem grid = Object.FindAnyObjectByType<GridSystem>();
        sb.AppendLine(grid != null
            ? "GridSystem OK origin=" + grid.originWorld + " cellSize=" + grid.cellSize
            : "FAIL no GridSystem in scene");

        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        sb.AppendLine(player != null ? "Player OK" : "FAIL no Player");

        CameraFollow follow = Object.FindAnyObjectByType<CameraFollow>();
        sb.AppendLine(follow != null && follow.enabled ? "CameraFollow ON" : "FAIL CameraFollow missing/off");

        BuildMenuController menu = Object.FindAnyObjectByType<BuildMenuController>();
        RoomPlacement place = Object.FindAnyObjectByType<RoomPlacement>();
        sb.AppendLine(menu != null ? "BuildMenuController OK" : "FAIL no BuildMenuController");
        sb.AppendLine(place != null && place.buildingPrefab != null
            ? "RoomPlacement OK prefab=" + RoomPlacement.PrefabPath
            : "FAIL RoomPlacement missing or unassigned");

        bool ok = grid != null
            && player != null
            && follow != null && follow.enabled
            && menu != null
            && place != null && place.buildingPrefab != null
            && SceneManager.GetActiveScene().path == ScenePath;
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }

    static void WriteReport(string report)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
    }
}
