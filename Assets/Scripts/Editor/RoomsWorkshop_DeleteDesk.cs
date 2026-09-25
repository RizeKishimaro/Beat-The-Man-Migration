using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Remove the old Retro Desk from RoomsWorkshop and delete its Node folder
/// so a better-textured replacement can drop in. Does not save Alpha01 or
/// GridFoundation. Does not rebuild the workshop.
/// Flag: Temp/rooms_workshop_delete_desk.flag
/// Report: Temp/rooms_workshop_delete_desk.txt
/// </summary>
[InitializeOnLoad]
public static class RoomsWorkshop_DeleteDesk
{
    const string ScenePath = RoomsWorkshopSceneSetup.ScenePath;
    const string PlaygroundPath = "Assets/Scenes/GridFoundation.unity";
    const string AssetFolder = "Assets/Objects/Nodes/Meshy_AI_Retro_Desk_Setup_0916095854_texture_fbx";
    const string FlagPath = "Temp/rooms_workshop_delete_desk.flag";
    const string ReportPath = "Temp/rooms_workshop_delete_desk.txt";
    const string Needle = "Retro_Desk";
    // compile tick 1

    static RoomsWorkshop_DeleteDesk()
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

    [MenuItem("Tools/Scenes/Delete Retro Desk From RoomsWorkshop")]
    public static void MenuRun()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Delete Retro Desk", Truncate(report, 1600), "OK");
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
            string report = Run();
            WriteReport(report);
            Debug.Log("[RoomsWorkshop_DeleteDesk]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[RoomsWorkshop_DeleteDesk] " + e);
        }
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Delete Retro Desk From RoomsWorkshop ===");
        sb.AppendLine("does not save Alpha01 or GridFoundation");

        Scene current = SceneManager.GetActiveScene();
        if (current.path == PlaygroundPath && current.isDirty)
        {
            EditorSceneManager.OpenScene(PlaygroundPath, OpenSceneMode.Single);
            sb.AppendLine("discarded unsaved GridFoundation dirt (did not save playground)");
            current = SceneManager.GetActiveScene();
        }

        if (!File.Exists(Full(ScenePath)))
        {
            sb.AppendLine("FAIL missing " + ScenePath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        Scene scene;
        if (current.path == ScenePath)
        {
            scene = current;
            sb.AppendLine("using open workshop (kept unsaved dressing)");
        }
        else
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("opened " + scene.path);
        }

        int destroyed = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;
            Transform hit = FindNamed(root.transform, Needle);
            if (hit == null)
                continue;
            sb.AppendLine("destroyed '" + hit.name + "' path=" + PathOf(hit));
            Object.DestroyImmediate(hit.gameObject);
            destroyed++;
        }
        sb.AppendLine("destroyedCount=" + destroyed);

        if (AssetDatabase.IsValidFolder(AssetFolder))
        {
            bool deleted = AssetDatabase.DeleteAsset(AssetFolder);
            sb.AppendLine("deleted folder " + AssetFolder + " ok=" + deleted);
        }
        else
        {
            sb.AppendLine("folder already gone " + AssetFolder);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved=" + saved);

        Scene reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject leftover = null;
        foreach (GameObject root in reopened.GetRootGameObjects())
        {
            Transform hit = FindNamed(root.transform, Needle);
            if (hit != null)
            {
                leftover = hit.gameObject;
                break;
            }
        }
        bool folderGone = !AssetDatabase.IsValidFolder(AssetFolder);
        bool ok = saved && leftover == null && folderGone;
        sb.AppendLine("reloaded " + reopened.path);
        sb.AppendLine("leftoverDesk=" + (leftover != null) + " folderGone=" + folderGone);
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static Transform FindNamed(Transform t, string needle)
    {
        if (t.name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return t;
        foreach (Transform child in t)
        {
            Transform hit = FindNamed(child, needle);
            if (hit != null)
                return hit;
        }
        return null;
    }

    static string PathOf(Transform t)
    {
        string p = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            p = t.name + "/" + p;
        }
        return p;
    }

    static void WriteReport(string report)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report ?? "");
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max) + "\n…";
    }
}
