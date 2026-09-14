using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates GridSystem.prefab and wires GridFoundation.unity:
/// GridSystem instance, Player, Item_Crate_Base (snapped), Main Camera+follow,
/// Directional Light. Keeps GridAxisTestCube. No building.
/// Flag: Temp/setup_grid_foundation.flag
/// </summary>
[InitializeOnLoad]
public static class GridFoundationSceneSetup
{
    const string PrefabDir = "Assets/Prefabs/Grid";
    const string PrefabPath = PrefabDir + "/GridSystem.prefab";
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string CratePrefabPath = "Assets/Art/Items/Item_Crate_Base.prefab";
    const string CharacterPath = "Assets/Art/Characters/Character_Base.fbx";
    const string FlagPath = "Temp/setup_grid_foundation.flag";
    const string ReportPath = "Temp/setup_grid_foundation_report.txt";

    static GridFoundationSceneSetup()
    {
        EditorApplication.delayCall += TryRunFromFlag;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRunFromFlag();
    }

    [MenuItem("Tools/Grid/Setup GridFoundation Scene + Prefab")]
    public static void SetupFromMenu()
    {
        string report = RunSetup(batch: false);
        WriteReport(report);
        EditorUtility.DisplayDialog("GridFoundation Setup", Truncate(report, 1400), "OK");
        Debug.Log("[GridFoundationSceneSetup]\n" + report);
    }

    /// <summary>Batchmode: -executeMethod GridFoundationSceneSetup.RunFromBatch</summary>
    public static void RunFromBatch()
    {
        try
        {
            string report = RunSetup(batch: true);
            WriteReport(report);
            Debug.Log("[GridFoundationSceneSetup]\n" + report);
            WriteExtra("Temp/grid_object_footprint_scan.txt", GridObjectFootprintBatchScan.RunScan());
            WriteExtra("Temp/grid_building_alignment_report.txt", GridBuildingAlignmentValidator.RunValidation());
            EditorApplication.Exit(report.Contains("RESULT=OK") ? 0 : 1);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError(e);
            EditorApplication.Exit(1);
        }
    }

    static void TryRunFromFlag()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += TryRunFromFlag;
            return;
        }

        File.Delete(flag);
        string report = RunSetup(batch: true);
        WriteReport(report);
        Debug.Log("[GridFoundationSceneSetup]\n" + report);
    }

    public static string RunSetup(bool batch = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== GridFoundation setup ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");

        EnsureFolders();
        GameObject prefab = EnsureGridSystemPrefab(sb);
        EnsureCrateHasGridFootprint(sb);
        if (GameplayCameraPrefab.Load() == null)
        {
            sb.AppendLine("FAIL missing " + GameplayCameraPrefab.PrefabPath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        if (!batch && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            sb.AppendLine("ABORT: user cancelled save of current scenes");
            return sb.ToString();
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        WireScene(scene, prefab, sb);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine($"saved={ScenePath}");
        sb.AppendLine("RESULT=OK");
        return sb.ToString();
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabDir))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Grid");
    }

    static GameObject EnsureGridSystemPrefab(StringBuilder sb)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            GridSystem gs = existing.GetComponent<GridSystem>();
            if (gs != null)
            {
                // Ensure defaults via opening contents
                string path = PrefabPath;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    GridSystem g = root.GetComponent<GridSystem>();
                    g.originWorld = Vector3.zero;
                    g.cellSize = 1f;
                    if (root.GetComponent<GridOverlay>() == null)
                        root.AddComponent<GridOverlay>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                sb.AppendLine($"prefab=updated {PrefabPath}");
                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
        }

        GameObject go = new GameObject("GridSystem");
        GridSystem grid = go.AddComponent<GridSystem>();
        grid.originWorld = Vector3.zero;
        grid.cellSize = 1f;
        go.AddComponent<GridOverlay>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        sb.AppendLine($"prefab=created {PrefabPath}");
        return prefab;
    }

    static void EnsureCrateHasGridFootprint(StringBuilder sb)
    {
        GameObject cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath);
        if (cratePrefab == null)
        {
            sb.AppendLine("WARN missing " + CratePrefabPath);
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(CratePrefabPath);
        try
        {
            GridFootprint fp = root.GetComponent<GridFootprint>();
            if (fp == null)
                fp = root.AddComponent<GridFootprint>();
            fp.type = AssetGridType.Object;
            fp.sizeInCells = Vector2Int.one;
            PrefabUtility.SaveAsPrefabAsset(root, CratePrefabPath);
            sb.AppendLine("crate GridFootprint=Object 1x1 (additive)");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void WireScene(Scene scene, GameObject gridPrefab, StringBuilder sb)
    {
        // Remove prior managed roots (idempotent re-run), keep GridAxisTestCube.
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;
            string n = root.name;
            if (n == "GridSystem" || n == "Player" || n == "Main Camera" || n == "MainCamera"
                || n == "Directional Light" || n == "Item_Crate_Base"
                || n == "WorldFloor" || n == "ItemCrateWorldSpawn")
            {
                Object.DestroyImmediate(root);
            }
        }

        // GridSystem prefab instance
        if (gridPrefab == null)
        {
            sb.AppendLine("FAIL gridPrefab null — abort WireScene");
            return;
        }
        GameObject gridInst = (GameObject)PrefabUtility.InstantiatePrefab(gridPrefab, scene);
        if (gridInst == null)
        {
            sb.AppendLine("FAIL InstantiatePrefab GridSystem returned null");
            return;
        }
        gridInst.name = "GridSystem";
        gridInst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        GridSystem gs = gridInst.GetComponent<GridSystem>();
        gs.originWorld = Vector3.zero;
        gs.cellSize = 1f;
        sb.AppendLine("scene+=GridSystem (origin=0, cellSize=1)");

        // Player
        GameObject player = new GameObject("Player");
        SceneManager.MoveGameObjectToScene(player, scene);
        player.transform.SetPositionAndRotation(new Vector3(-0.58f, 0f, -0.5f), Quaternion.identity);
        CharacterController cc = player.AddComponent<CharacterController>();
        cc.height = 1.56f;
        cc.radius = 0.28f;
        cc.center = new Vector3(0f, 0.78f, 0f);
        cc.skinWidth = 0.08f;
        cc.stepOffset = 0.3f;
        PlayerController pc = player.AddComponent<PlayerController>();
        GameObject character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
        SerializedObject so = new SerializedObject(pc);
        so.FindProperty("characterPrefab").objectReferenceValue = character;
        so.FindProperty("hidePlaceholderVisual").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        sb.AppendLine("scene+=Player (Character_Base assigned)");

        // Crate snapped to cell center (1,0) → world (1.5, 0, 0.5) bottom — use corner snap
        // OutsideFloorPos from ItemCrateWorldSpawn is (0.9,-1.2); snap via grid helpers.
        GameObject cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath);
        Vector3 cratePos = gs.CellToWorldCorner(new GridCoord(1, -2)); // near player outside spawn
        // Keep Y=0 for floor sit; XZ at cell corner (objects snap to corner-origin convention).
        cratePos.y = 0f;
        GameObject crate = (GameObject)PrefabUtility.InstantiatePrefab(cratePrefab, scene);
        crate.name = "Item_Crate_Base";
        crate.transform.SetPositionAndRotation(cratePos, Quaternion.identity);
        crate.transform.localScale = Vector3.one;
        if (crate.GetComponent<GridFootprint>() == null)
        {
            GridFootprint fp = crate.AddComponent<GridFootprint>();
            fp.type = AssetGridType.Object;
            fp.sizeInCells = Vector2Int.one;
        }
        sb.AppendLine($"scene+=Item_Crate_Base at {cratePos} (cell 1,-2 corner)");

        // Main Camera prefab + follow (ticket 120)
        GameplayCameraPrefab.InstantiateLookingAt(
            scene,
            player.transform.position + new Vector3(-8f, 7f, -8f),
            player.transform.position,
            player.transform,
            enableFollow: true,
            solidColorBackground: false);
        sb.AppendLine("scene+=Main Camera prefab + CameraFollow SMAA High");

        // Light — ticket 106 shared key + fill rig (matches what the 2026-08-05
        // render-quality pass left in the saved scene; was a bare 2.0 key).
        GameplayLightingRig.CreateRig(scene, GameplayLightingRig.DefaultKeyIntensity, sb);
        sb.AppendLine("scene+=Directional Light + Fill Light");

        // Confirm GridAxisTestCube still present
        bool hasCube = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != null && root.name == "GridAxisTestCube")
                hasCube = true;
        }
        sb.AppendLine(hasCube ? "kept GridAxisTestCube" : "WARN GridAxisTestCube missing");
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static void WriteReport(string report)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
    }

    static void WriteExtra(string relative, string body)
    {
        string full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, body);
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max) + "\n…";
    }
}
