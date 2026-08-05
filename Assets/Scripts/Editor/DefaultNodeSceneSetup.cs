using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds DefaultNode prefabs (Bin / Counter / Keep) — all 1×1 for now —
/// and places them in GridFoundation with distinct colours.
/// Master DefaultNode + Independent behavior components (future Nodes plug in here).
/// Flag: Temp/setup_default_nodes.flag
/// </summary>
[InitializeOnLoad]
public static class DefaultNodeSceneSetup
{
    const string PrefabDir = "Assets/Prefabs/Nodes";
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string FlagPath = "Temp/setup_default_nodes.flag";
    const string ReportPath = "Temp/setup_default_nodes_report.txt";
    const string ReloadFlagPath = "Temp/force_reload_grid_foundation.flag";

    enum NodeKind
    {
        Destroy,     // Bin — consume
        Place,       // Counter — visible on top
        Keep,        // Keep — visible on slot
        StoreInside, // Storage — kept inside, mesh hidden
    }

    static DefaultNodeSceneSetup()
    {
        EditorApplication.delayCall += TryFlag;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryFlag();
    }

    static void TryFlag()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        File.Delete(flag);
        string report = RunSetup(batch: true);
        WriteReport(report);
        Debug.Log("[DefaultNodeSceneSetup]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    [MenuItem("Tools/Grid/Setup Default Nodes (GridFoundation)")]
    public static void SetupFromMenu()
    {
        string report = RunSetup(batch: false);
        WriteReport(report);
        EditorUtility.DisplayDialog("Default Nodes", Truncate(report, 1400), "OK");
        Debug.Log("[DefaultNodeSceneSetup]\n" + report);
    }

    public static string RunSetup(bool batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Default Node setup (ticket 102) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");
        sb.AppendLine("variants: Bin=DESTROY(red) Counter=PLACE(beige) Keep=KEEP(green) Storage=INSIDE(purple)");
        sb.AppendLine("layout: Counter+Keep adjacent for nearest-node testing");

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            sb.AppendLine("exited play mode before setup");
        }

        EnsureFolders();

        GameObject binPrefab = EnsureNodePrefab(
            "Node_Bin",
            new Color(0.85f, 0.2f, 0.15f), // red — destroy
            NodeKind.Destroy,
            sb);
        GameObject counterPrefab = EnsureNodePrefab(
            "Node_Counter",
            new Color(0.82f, 0.72f, 0.45f), // beige — place on top
            NodeKind.Place,
            sb);
        GameObject keepPrefab = EnsureNodePrefab(
            "Node_Keep",
            new Color(0.2f, 0.65f, 0.35f), // green — keep visible on slot
            NodeKind.Keep,
            sb);
        GameObject storagePrefab = EnsureNodePrefab(
            "Node_Storage",
            new Color(0.55f, 0.3f, 0.85f), // purple — store inside (hidden)
            NodeKind.StoreInside,
            sb);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;
            if (root.name == "Node_Station" || root.name.StartsWith("Node_Station"))
            {
                Object.DestroyImmediate(root);
                sb.AppendLine("removed retired Node_Station");
            }
        }

        // Adjacent pair for proximity testing (cells 3,-1 and 4,-1).
        PlaceOrReplace(scene, counterPrefab, "Node_Counter", new Vector3(3f, 0f, -1f), sb);
        PlaceOrReplace(scene, keepPrefab, "Node_Keep", new Vector3(4f, 0f, -1f), sb);
        PlaceOrReplace(scene, binPrefab, "Node_Bin", new Vector3(3f, 0f, -3f), sb);
        PlaceOrReplace(scene, storagePrefab, "Node_Storage", new Vector3(5f, 0f, -1f), sb);

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
            AssetDatabase.CreateFolder("Assets/Prefabs", "Nodes");
    }

    static GameObject EnsureNodePrefab(string name, Color color, NodeKind kind, StringBuilder sb)
    {
        string path = $"{PrefabDir}/{name}.prefab";

        // Delete prior asset so we don't leave stale multi-slot / Station wiring.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            AssetDatabase.DeleteAsset(path);

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = name;
        // Full 1m × 1m cell footprint (and 1m tall block).
        root.transform.localScale = Vector3.one;
        root.transform.position = Vector3.zero;

        var rb = root.GetComponent<Rigidbody>();
        if (rb != null)
            Object.DestroyImmediate(rb);

        var rend = root.GetComponent<Renderer>();
        if (rend != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = color;
            string matPath = $"{PrefabDir}/{name}_Mat.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null)
                AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(mat, matPath);
            rend.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        var footprint = root.AddComponent<GridFootprint>();
        footprint.type = AssetGridType.Node;
        footprint.sizeInCells = Vector2Int.one;

        var node = root.AddComponent<DefaultNode>();
        SerializedObject so = new SerializedObject(node);
        so.FindProperty("acceptAnyObject").boolValue = true;
        so.FindProperty("acceptedObjectNames").arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();

        var slotGo = new GameObject(DefaultNode.SlotName);
        slotGo.transform.SetParent(root.transform, false);
        // Storage sits inside the cube; others sit on the top face (unit cube local y = +0.5).
        float slotY = kind == NodeKind.StoreInside ? 0f : 0.5f;
        slotGo.transform.localPosition = new Vector3(0f, slotY, 0f);
        slotGo.transform.localScale = Vector3.one;
        SerializedObject so2 = new SerializedObject(node);
        so2.FindProperty("slot").objectReferenceValue = slotGo.transform;
        so2.ApplyModifiedPropertiesWithoutUndo();

        switch (kind)
        {
            case NodeKind.Destroy:
                root.AddComponent<NodeDestroyOnAccept>();
                break;
            case NodeKind.Place:
                root.AddComponent<NodePlaceOnAccept>();
                break;
            case NodeKind.Keep:
                root.AddComponent<NodeKeepOnAccept>();
                break;
            case NodeKind.StoreInside:
                root.AddComponent<NodeStoreInsideOnAccept>();
                break;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        sb.AppendLine($"prefab={path} kind={kind} color={color} size=1x1");
        return prefab;
    }

    static void PlaceOrReplace(Scene scene, GameObject prefab, string name, Vector3 pos, StringBuilder sb)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != null && root.name == name)
                Object.DestroyImmediate(root);
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        inst.name = name;
        float halfY = inst.transform.localScale.y * 0.5f;
        Vector3 world = new Vector3(pos.x, halfY, pos.z);
        GridSystem grid = Object.FindAnyObjectByType<GridSystem>();
        if (grid != null)
        {
            GridCoord cell = grid.WorldToCell(new Vector3(pos.x, 0f, pos.z));
            Vector3 center = grid.CellToWorldCenter(cell);
            world = new Vector3(center.x, halfY, center.z);
        }
        inst.transform.SetPositionAndRotation(world, Quaternion.identity);
        sb.AppendLine($"scene+={name} at {inst.transform.position}");
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static void WriteReport(string report)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max) + "\n…";
    }
}
