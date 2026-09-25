using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Opens Assets/Scenes/RoomsWorkshop.unity from disk (Single).
/// Does not save Alpha01 or GridFoundation.
/// Flag: Temp/open_rooms_workshop.flag
/// Report: Temp/open_rooms_workshop.txt
/// </summary>
[InitializeOnLoad]
public static class OpenRoomsWorkshop
{
    const string ScenePath = RoomsWorkshopSceneSetup.ScenePath;
    const string PlaygroundPath = "Assets/Scenes/GridFoundation.unity";
    const string FlagPath = "Temp/open_rooms_workshop.flag";
    const string ReportPath = "Temp/open_rooms_workshop.txt";
    // compile tick 6

    static OpenRoomsWorkshop()
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

    [MenuItem("Tools/Scenes/Open RoomsWorkshop")]
    public static void MenuOpen()
    {
        string report = Open();
        WriteReport(report);
        Debug.Log("[OpenRoomsWorkshop]\n" + report);
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
            string report = Open();
            WriteReport(report);
            Debug.Log("[OpenRoomsWorkshop]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[OpenRoomsWorkshop] " + e);
        }
    }

    static string Open()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Open RoomsWorkshop ===");
        sb.AppendLine("does not save Alpha01 or GridFoundation");

        Scene current = SceneManager.GetActiveScene();
        sb.AppendLine("active before: '" + current.path + "' dirty=" + current.isDirty);

        if (current.path == PlaygroundPath && current.isDirty)
        {
            EditorSceneManager.OpenScene(PlaygroundPath, OpenSceneMode.Single);
            sb.AppendLine("discarded unsaved GridFoundation dirt (did not save playground)");
        }

        if (!File.Exists(Full(ScenePath)))
        {
            sb.AppendLine("FAIL missing " + ScenePath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject hr = FindRoot(scene, "Room_HR");
        GameObject cube = FindRoot(scene, "HRDept_Shell_6x6");
        sb.AppendLine("opened=" + scene.path);
        sb.AppendLine("Room_HR=" + (hr != null) + " cubeGone=" + (cube == null));
        bool ok = scene.path == ScenePath && hr != null && cube == null;
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

    static void WriteReport(string report)
    {
        File.WriteAllText(Full(ReportPath), report ?? "");
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }
}
