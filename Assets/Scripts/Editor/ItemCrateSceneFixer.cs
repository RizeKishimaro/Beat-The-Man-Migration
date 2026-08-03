using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Removes stale/broken/duplicate Item_Crate_Base instances and leaves exactly one
/// healthy Prefab instance. Also clears center-pivot junk.
/// Flag: Temp/fix_scene_crates.flag
/// </summary>
[InitializeOnLoad]
public static class ItemCrateSceneFixer
{
    const string ScenePath = "Assets/For Cursor.unity";
    const string PrefabPath = "Assets/Art/Items/Item_Crate_Base.prefab";
    const string FlagPath = "Temp/fix_scene_crates.flag";
    const string VerifyPath = "Temp/agent_verify_crate_scene.txt";
    const int Revision = 1;

    static ItemCrateSceneFixer()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += TryRun;
        };
    }

    [InitializeOnLoadMethod]
    static void Boot() => EditorApplication.delayCall += TryRun;

    static void TryRun()
    {
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // Exit play mode and retry — do not consume the flag yet.
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += TryRun;
            return;
        }

        try
        {
            File.Delete(flag);
            string report = FixScene();
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
            Debug.Log("[ItemCrateSceneFixer]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)),
                "FAIL: " + e);
            Debug.LogError("[ItemCrateSceneFixer] " + e);
        }
    }

    [MenuItem("Tools/For Cursor/Fix Scene Crates (exactly one)")]
    public static void MenuFix()
    {
        string report = FixScene();
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
        EditorUtility.DisplayDialog("Scene Crates", report, "OK");
    }

    /// <summary>Batchmode entry: Unity -batchmode -executeMethod ItemCrateSceneFixer.BatchFix</summary>
    public static void BatchFix()
    {
        try
        {
            string report = FixScene();
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
            Debug.Log("[ItemCrateSceneFixer] BatchFix\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)),
                "FAIL: " + e);
            Debug.LogError("[ItemCrateSceneFixer] BatchFix " + e);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    public static string FixScene()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Revision=" + Revision);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int before = 0;
        var toDestroy = new System.Collections.Generic.List<GameObject>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null || t.name != "Item_Crate_Base")
                continue;
            before++;
            toDestroy.Add(t.gameObject);
        }
        sb.AppendLine("beforeCount=" + before);

        foreach (var go in toDestroy)
            Object.DestroyImmediate(go);

        // Also destroy any MultiProbe leftovers
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t != null && t.name.StartsWith("Item_Crate_Base_MultiProbe"))
                Object.DestroyImmediate(t.gameObject);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new System.Exception("Missing prefab: " + PrefabPath);

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.name = "Item_Crate_Base";
        inst.transform.SetPositionAndRotation(new Vector3(0.9f, 0f, -1.2f), Quaternion.identity);
        inst.transform.localScale = Vector3.one;

        bool healthy = ItemCrateWorldSpawn.IsHealthyPrefabCrate(inst);
        int after = ItemCrateWorldSpawn.CountValidPrefabCrates();
        sb.AppendLine($"placed=1 healthy={healthy} validCount={after}");
        sb.AppendLine($"isPrefabInstance={PrefabUtility.IsPartOfPrefabInstance(inst)}");
        sb.AppendLine($"hasCarryable={inst.GetComponent<CarryableItem>() != null}");
        sb.AppendLine($"hasPushable={inst.GetComponent<PushableItem>() != null}");
        sb.AppendLine($"rbKinematic={inst.GetComponent<Rigidbody>()?.isKinematic}");
        sb.AppendLine("staleIdsNote=removed all scene PrefabInstances; new instance uses current prefab fileIDs");

        if (!healthy || after != 1)
            throw new System.Exception("Expected exactly 1 healthy crate after fix");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("OK");
        return sb.ToString();
    }
}

