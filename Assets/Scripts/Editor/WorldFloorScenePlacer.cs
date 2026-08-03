using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places WorldFloor into For Cursor.unity.
/// Auto-runs when Temp/place_world_floor.flag is present.
/// </summary>
[InitializeOnLoad]
public static class WorldFloorScenePlacer
{
    const string ScenePath = "Assets/For Cursor.unity";
    const string FlagPath = "Temp/place_world_floor.flag";
    const string VerifyPath = "Temp/agent_verify_world_floor.txt";
    const int PlacerRevision = 4;

    static WorldFloorScenePlacer()
    {
        EditorApplication.delayCall += TryAutoPlace;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.delayCall += TryAutoPlace;
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += TryAutoPlace;
    }

    static void TryAutoPlace()
    {
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
        if (!File.Exists(flag))
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        try
        {
            File.Delete(flag);
            string report = PlaceAndSave();
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)),
                report);
            Debug.Log("[WorldFloorScenePlacer]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)),
                "FAIL: " + e);
            Debug.LogError("[WorldFloorScenePlacer] " + e);
        }
    }

    [MenuItem("Tools/For Cursor/Place WorldFloor")]
    public static void PlaceMenu()
    {
        string report = PlaceAndSave();
        File.WriteAllText(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)),
            report);
        Debug.Log("[WorldFloorScenePlacer]\n" + report);
        EditorUtility.DisplayDialog("WorldFloor", report, "OK");
    }

    static string PlaceAndSave()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("PlacerRevision=" + PlacerRevision);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Remove any prior WorldFloor roots
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == WorldFloorEnsure.FloorName)
                Object.DestroyImmediate(root);
        }

        WorldFloorEnsure.Ensure();

        GameObject floor = GameObject.Find(WorldFloorEnsure.FloorName);
        if (floor == null)
            throw new System.Exception("WorldFloor missing after Ensure()");

        var col = floor.GetComponent<Collider>();
        sb.AppendLine($"pos={floor.transform.position}");
        sb.AppendLine($"scale={floor.transform.localScale}");
        sb.AppendLine($"collider={(col != null)} isTrigger={(col != null && col.isTrigger)}");
        sb.AppendLine($"static={floor.isStatic}");
        sb.AppendLine("OK");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
