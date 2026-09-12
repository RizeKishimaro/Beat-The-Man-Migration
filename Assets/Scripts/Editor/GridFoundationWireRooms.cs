using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires Alpha01 room creation (168/169) into the live GridFoundation scene.
/// Does not save GridFoundation. Does not touch DefaultBuilding_117 or CameraFollow.
/// Flag: Temp/gridfoundation_wire_rooms.flag
/// Report: Temp/gridfoundation_wire_rooms.txt
/// </summary>
[InitializeOnLoad]
public static class GridFoundationWireRooms
{
    public const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    public const string BuildRootName = "BuildPlacement";

    const string FlagPath = "Temp/gridfoundation_wire_rooms.flag";
    const string ReportPath = "Temp/gridfoundation_wire_rooms.txt";

    static GridFoundationWireRooms()
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

    [MenuItem("Tools/Grid/Wire Room Creation Into GridFoundation (no save)")]
    public static void MenuWire()
    {
        string report = RunWire();
        WriteReport(report);
        EditorUtility.DisplayDialog("Wire Room Creation", Truncate(report, 1600), "OK");
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
            string report = RunWire();
            WriteReport(report);
            Debug.Log("[GridFoundationWireRooms]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[GridFoundationWireRooms] " + e);
        }
    }

    public static string RunWire()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Wire room creation into GridFoundation (no save) ===");
        sb.AppendLine("does not save GridFoundation");
        sb.AppendLine("does not touch DefaultBuilding_117 / CameraFollow");

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            sb.AppendLine("exited play mode");
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!File.Exists(Full(ScenePath)))
            {
                sb.AppendLine("FAIL missing " + ScenePath);
                return sb.ToString();
            }
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("opened " + scene.path);
        }
        else
        {
            sb.AppendLine("already on " + scene.path + " dirty=" + scene.isDirty);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPlacement.PrefabPath);
        if (prefab == null)
        {
            sb.AppendLine("FAIL missing " + RoomPlacement.PrefabPath);
            return sb.ToString();
        }

        GameObject build = FindRoot(scene, BuildRootName);
        if (build == null)
        {
            build = new GameObject(BuildRootName);
            SceneManager.MoveGameObjectToScene(build, scene);
            sb.AppendLine("created " + BuildRootName);
        }
        else
        {
            sb.AppendLine("reused " + BuildRootName);
        }

        if (build.GetComponent<BuildMenuController>() == null)
            build.AddComponent<BuildMenuController>();
        if (build.GetComponent<RoomSlotPlusPrompt>() == null)
            build.AddComponent<RoomSlotPlusPrompt>();

        RoomPlacement place = build.GetComponent<RoomPlacement>();
        if (place == null)
            place = build.AddComponent<RoomPlacement>();
        place.buildingPrefab = prefab;

        EditorSceneManager.MarkSceneDirty(scene);

        GridSystem grid = Object.FindAnyObjectByType<GridSystem>();
        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        sb.AppendLine(grid != null ? "GridSystem OK" : "FAIL no GridSystem");
        sb.AppendLine(player != null ? "Player OK" : "FAIL no Player");
        sb.AppendLine("BuildMenuController=" + (build.GetComponent<BuildMenuController>() != null));
        sb.AppendLine("RoomSlotPlusPrompt=" + (build.GetComponent<RoomSlotPlusPrompt>() != null));
        sb.AppendLine("RoomPlacement prefab=" + (place.buildingPrefab != null ? RoomPlacement.PrefabPath : "NULL"));
        sb.AppendLine("saved=false dirty=" + scene.isDirty);

        bool ok = grid != null
            && player != null
            && build.GetComponent<BuildMenuController>() != null
            && build.GetComponent<RoomSlotPlusPrompt>() != null
            && place.buildingPrefab != null
            && SceneManager.GetActiveScene().path == ScenePath;
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root != null && root.name == name)
                return root;
        return null;
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
