using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 169 — wire RoomPlacement on Alpha01 BuildPlacement, assign DefaultBuilding,
/// add GridFootprint Building 6×6 on the prefab (classification only).
/// Does not save GridFoundation. Does not pull wall meshes together.
/// Flag: Temp/alpha01_169_setup.flag
/// Report: Temp/alpha01_169/setup.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_169SceneSetup
{
    public const string ScenePath = Alpha01SceneSetup.ScenePath;
    public const string PrefabPath = RoomPlacement.PrefabPath;
    public const string BuildRootName = Alpha01_168SceneSetup.BuildRootName;

    const string FlagPath = "Temp/alpha01_169_setup.flag";
    const string ReportPath = "Temp/alpha01_169/setup.txt";

    static Alpha01_169SceneSetup()
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

    [MenuItem("Tools/Scenes/169 Setup Alpha01 Room Spawn")]
    public static void MenuSetup()
    {
        string report = RunSetup();
        WriteReport(report);
        EditorUtility.DisplayDialog("169 Setup", Truncate(report, 1600), "OK");
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
            string report = RunSetup();
            WriteReport(report);
            Debug.Log("[Alpha01_169SceneSetup]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Alpha01_169SceneSetup] " + e);
        }
    }

    public static string RunSetup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 169 Alpha01 room spawn wire ===");
        sb.AppendLine("does not save GridFoundation");
        sb.AppendLine("does not pull wall meshes together");

        DiscardDirtyIfNotAlpha01(sb);
        EnsurePrefabFootprint(sb);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            sb.AppendLine("FAIL missing " + PrefabPath);
            return sb.ToString();
        }

        if (!File.Exists(Full(ScenePath)))
        {
            sb.AppendLine("FAIL missing " + ScenePath);
            return sb.ToString();
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("opened " + scene.path);

        GameObject build = FindRoot(scene, BuildRootName);
        if (build == null)
        {
            sb.AppendLine("FAIL missing " + BuildRootName);
            return sb.ToString();
        }

        if (build.GetComponent<BuildMenuController>() == null)
            build.AddComponent<BuildMenuController>();
        if (build.GetComponent<RoomSlotPlusPrompt>() == null)
            build.AddComponent<RoomSlotPlusPrompt>();

        RoomPlacement place = build.GetComponent<RoomPlacement>();
        if (place == null)
            place = build.AddComponent<RoomPlacement>();
        place.buildingPrefab = prefab;
        EditorUtility.SetDirty(place);
        sb.AppendLine("RoomPlacement prefab=" + PrefabPath);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved=" + saved + " " + ScenePath);

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("reloaded " + SceneManager.GetActiveScene().path);

        RoomPlacement wired = Object.FindAnyObjectByType<RoomPlacement>();
        bool ok = saved
            && SceneManager.GetActiveScene().path == ScenePath
            && wired != null
            && wired.buildingPrefab != null;
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static void EnsurePrefabFootprint(StringBuilder sb)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            GridFootprint fp = contents.GetComponent<GridFootprint>();
            if (fp == null)
                fp = contents.AddComponent<GridFootprint>();
            fp.type = AssetGridType.Building;
            fp.sizeInCells = new Vector2Int(RoomSlotGrid.SlotCells, RoomSlotGrid.SlotCells);
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            sb.AppendLine("GridFootprint Building " + fp.sizeInCells + " on " + PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    static void DiscardDirtyIfNotAlpha01(StringBuilder sb)
    {
        Scene current = SceneManager.GetActiveScene();
        if (current.path == ScenePath || !current.isDirty)
            return;
        if (string.IsNullOrEmpty(current.path))
            return;
        string path = current.path;
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        sb.AppendLine("discarded dirty " + path + " (did not save)");
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
