using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Disable Wall_* meshes on RoomsWorkshop Room_HR only (instance overrides).
/// Does not edit DefaultBuilding.prefab. Does not save Alpha01 or GridFoundation.
/// Flag: Temp/rooms_workshop_disable_hr_walls.flag
/// Report: Temp/rooms_workshop_disable_hr_walls.txt
/// </summary>
[InitializeOnLoad]
public static class RoomsWorkshop_DisableHrWalls
{
    const string ScenePath = RoomsWorkshopSceneSetup.ScenePath;
    const string HrName = "Room_HR";
    const string FlagPath = "Temp/rooms_workshop_disable_hr_walls.flag";
    const string BasesFlagPath = "Temp/rooms_workshop_hr_wall_bases.flag";
    const string ReportPath = "Temp/rooms_workshop_disable_hr_walls.txt";
    const string BasesReportPath = "Temp/rooms_workshop_hr_wall_bases.txt";
    const string PlaygroundPath = "Assets/Scenes/GridFoundation.unity";

    static readonly string[] WallNames =
    {
        "Wall_Left", "Wall_Right", "Wall_Back",
        "Wall_Front_Left", "Wall_Front_Right", "Wall_Front_Lintel"
    };

    static RoomsWorkshop_DisableHrWalls()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (File.Exists(Full(BasesFlagPath)))
        {
            TryRunBases();
            return;
        }
        if (!File.Exists(Full(FlagPath)))
            return;
        TryRun();
    }

    [MenuItem("Tools/Scenes/Disable Room_HR Walls (RoomsWorkshop)")]
    public static void MenuRun()
    {
        string report = Run();
        WriteReport(report);
        Debug.Log("[RoomsWorkshop_DisableHrWalls]\n" + report);
    }

    [MenuItem("Tools/Scenes/Room_HR Walls: Bases Only (RoomsWorkshop)")]
    public static void MenuBases()
    {
        string report = RunBases();
        WriteReportTo(BasesReportPath, report);
        Debug.Log("[RoomsWorkshop_HrWallBases]\n" + report);
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
            Debug.Log("[RoomsWorkshop_DisableHrWalls]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[RoomsWorkshop_DisableHrWalls] " + e);
        }
    }

    static void TryRunBases()
    {
        if (!File.Exists(Full(BasesFlagPath)))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += TryRunBases;
            return;
        }
        if (EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += TryRunBases;
            return;
        }

        try
        {
            File.Delete(Full(BasesFlagPath));
            string report = RunBases();
            WriteReportTo(BasesReportPath, report);
            Debug.Log("[RoomsWorkshop_HrWallBases]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReportTo(BasesReportPath, "ERROR " + e);
            Debug.LogError("[RoomsWorkshop_HrWallBases] " + e);
        }
    }

    public static string RunBases()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Room_HR walls: cutaway bases only ===");
        sb.AppendLine("does not edit DefaultBuilding.prefab");
        sb.AppendLine("does not save Alpha01 or GridFoundation");

        Scene current = SceneManager.GetActiveScene();
        sb.AppendLine("active before: '" + current.path + "' dirty=" + current.isDirty);
        if (current.path == PlaygroundPath && current.isDirty)
        {
            EditorSceneManager.OpenScene(PlaygroundPath, OpenSceneMode.Single);
            sb.AppendLine("discarded unsaved GridFoundation dirt (did not save playground)");
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject hr = FindRoot(scene, HrName);
        if (hr == null)
        {
            sb.AppendLine("FAIL missing " + HrName);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        for (int i = 0; i < WallNames.Length; i++)
        {
            Transform t = FindNamed(hr.transform, WallNames[i]);
            if (t == null)
            {
                sb.AppendLine("warn missing " + WallNames[i]);
                continue;
            }
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            sb.AppendLine(WallNames[i] + " goActive=" + t.gameObject.activeSelf);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
        }

        BuildingInteriorVisibility vis = hr.GetComponentInChildren<BuildingInteriorVisibility>(true);
        if (vis == null)
        {
            sb.AppendLine("FAIL no BuildingInteriorVisibility on " + HrName);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        BuildingInteriorVisibility.WallBasesOnly = true;
        vis.ForceRefresh();
        PrefabUtility.RecordPrefabInstancePropertyModifications(hr);
        PrefabUtility.RecordPrefabInstancePropertyModifications(vis);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved=" + saved + " WallBasesOnly=" + BuildingInteriorVisibility.WallBasesOnly);

        scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        hr = FindRoot(scene, HrName);
        vis = hr != null ? hr.GetComponentInChildren<BuildingInteriorVisibility>(true) : null;
        if (vis != null)
        {
            BuildingInteriorVisibility.WallBasesOnly = true;
            vis.ForceRefresh();
            sb.AppendLine(vis.DebugDumpStripState().TrimEnd());
        }

        int inactive = 0;
        int lintelOn = 0;
        if (hr != null)
        {
            for (int i = 0; i < WallNames.Length; i++)
            {
                Transform t = FindNamed(hr.transform, WallNames[i]);
                if (t == null)
                    continue;
                if (!t.gameObject.activeSelf)
                    inactive++;
                Renderer r = t.GetComponent<Renderer>();
                if (WallNames[i].Contains("Lintel") && r != null && r.enabled)
                    lintelOn++;
            }
        }

        sb.AppendLine("reloaded " + scene.path + " wallsInactive=" + inactive + " lintelRendererOn=" + lintelOn);
        bool ok = saved && hr != null && vis != null && inactive == 0 && lintelOn == 0;
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Disable Room_HR walls (RoomsWorkshop) ===");
        sb.AppendLine("does not edit DefaultBuilding.prefab");
        sb.AppendLine("does not save Alpha01 or GridFoundation");

        Scene current = SceneManager.GetActiveScene();
        sb.AppendLine("active before: '" + current.path + "' dirty=" + current.isDirty);
        if (current.path == PlaygroundPath && current.isDirty)
        {
            EditorSceneManager.OpenScene(PlaygroundPath, OpenSceneMode.Single);
            sb.AppendLine("discarded unsaved GridFoundation dirt (did not save playground)");
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject hr = FindRoot(scene, HrName);
        if (hr == null)
        {
            sb.AppendLine("FAIL missing " + HrName);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        int n = 0;
        for (int i = 0; i < WallNames.Length; i++)
        {
            Transform t = FindNamed(hr.transform, WallNames[i]);
            if (t == null)
            {
                sb.AppendLine("warn missing " + WallNames[i]);
                continue;
            }
            if (t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(false);
                n++;
            }
            sb.AppendLine(WallNames[i] + " active=" + t.gameObject.activeSelf);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(hr);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved=" + saved + " disabled=" + n);

        scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        hr = FindRoot(scene, HrName);
        int stillOn = 0;
        if (hr != null)
        {
            for (int i = 0; i < WallNames.Length; i++)
            {
                Transform t = FindNamed(hr.transform, WallNames[i]);
                if (t != null && t.gameObject.activeSelf)
                    stillOn++;
            }
        }
        sb.AppendLine("reloaded " + scene.path + " wallsStillActive=" + stillOn);
        bool ok = saved && hr != null && stillOn == 0;
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

    static Transform FindNamed(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    static void WriteReport(string report)
    {
        WriteReportTo(ReportPath, report);
    }

    static void WriteReportTo(string rel, string report)
    {
        File.WriteAllText(Full(rel), report ?? "");
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }
}
