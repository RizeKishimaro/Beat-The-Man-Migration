using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Imports the 1x1m drawer cabinet as static room furniture: shiny white
/// body/drawers, black handles, box collider, 1x1 Object grid footprint.
/// Places it in DefaultBuilding_117 against the back wall, drawers facing in.
/// Flag: Temp/setup_drawer_cabinet.flag
/// </summary>
[InitializeOnLoad]
public static class DrawerCabinetSceneSetup
{
    const string FbxPath = "Assets/Objects/Furniture/Drawer_Cabinet/Drawer_Cabinet.fbx";
    const string MatDir = "Assets/Art/Furniture";
    const string WhiteMatPath = MatDir + "/DrawerCabinet_White.mat";
    const string BlackMatPath = MatDir + "/DrawerCabinet_HandleBlack.mat";
    const string PrefabDir = "Assets/Prefabs/Furniture";
    const string PrefabPath = PrefabDir + "/Drawer_Cabinet.prefab";
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string BuildingName = "DefaultBuilding_117";
    const string InstanceName = "Drawer_Cabinet";
    const string FlagPath = "Temp/setup_drawer_cabinet.flag";
    const string ReportPath = "Temp/setup_drawer_cabinet_report.txt";
    const string ReloadFlagPath = "Temp/force_reload_grid_foundation.flag";

    static DrawerCabinetSceneSetup()
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
        if (EditorApplication.isCompiling)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        File.Delete(flag);
        string report = Run();
        WriteReport(report);
        Debug.Log("[DrawerCabinetSceneSetup]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    [MenuItem("Tools/Grid/Setup Drawer Cabinet")]
    public static void SetupFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            File.WriteAllText(Full(FlagPath), "setup\n");
            return;
        }
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Drawer Cabinet", Truncate(report, 1600), "OK");
        Debug.Log("[DrawerCabinetSceneSetup]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Drawer_Cabinet furniture ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            sb.AppendLine("FAIL still in play mode");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        ConfigureFbx(sb);
        Material white = BuildLit(WhiteMatPath, new Color(0.96f, 0.97f, 0.99f), 0.22f, 0.86f, sb);
        Material black = BuildLit(BlackMatPath, new Color(0.02f, 0.02f, 0.02f), 0.80f, 0.45f, sb);
        if (white == null || black == null)
        {
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        GameObject fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbxRoot == null)
        {
            sb.AppendLine("FAIL FBX not imported: " + FbxPath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject building = FindRoot(scene, BuildingName);
        if (building == null)
        {
            sb.AppendLine($"FAIL: no root named '{BuildingName}'");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        BoxCollider interior = null;
        foreach (var box in building.GetComponentsInChildren<BoxCollider>(true))
        {
            if (box.name == "InteriorTrigger")
            {
                interior = box;
                break;
            }
        }
        if (interior == null)
        {
            sb.AppendLine("FAIL: DefaultBuilding has no InteriorTrigger");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        Bounds room = new Bounds(
            interior.transform.TransformPoint(interior.center),
            Vector3.Scale(interior.size, interior.transform.lossyScale));
        float floorY = room.min.y;
        sb.AppendLine($"interior min={room.min} max={room.max}");

        GridSystem grid = Object.FindAnyObjectByType<GridSystem>();
        float cell = grid != null ? grid.cellSize : 1f;
        Vector3 origin = grid != null ? grid.originWorld : Vector3.zero;
        int minCellX = Mathf.RoundToInt((room.min.x - origin.x) / cell);
        int minCellZ = Mathf.RoundToInt((room.min.z - origin.z) / cell);
        int roomCellsX = Mathf.RoundToInt(room.size.x / cell);
        int roomCellsZ = Mathf.RoundToInt(room.size.z / cell);
        // Back-left cell: against +Z wall, clear of the centred plinth/car.
        int placeX = minCellX;
        int placeZ = minCellZ + roomCellsZ - 1;
        Vector3 cellCenter = new Vector3(
            origin.x + (placeX + 0.5f) * cell,
            floorY,
            origin.z + (placeZ + 0.5f) * cell);
        sb.AppendLine($"place cell=({placeX},{placeZ}) centerXZ=({cellCenter.x:F3},{cellCenter.z:F3})");

        DestroyChild(building.transform, InstanceName);

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxRoot, scene);
        if (inst == null)
            inst = Object.Instantiate(fbxRoot);
        inst.name = InstanceName;
        inst.transform.SetParent(building.transform, true);
        inst.transform.localScale = Vector3.one;
        inst.transform.rotation = Quaternion.identity;
        inst.transform.position = cellCenter;

        AssignMaterials(inst, white, black, sb);
        FaceDrawersIntoRoom(inst, Vector3.back, sb);

        Bounds b = RendererBounds(inst);
        Vector3 p = inst.transform.position;
        p.x = cellCenter.x;
        p.z = cellCenter.z;
        p.y += floorY - b.min.y;
        inst.transform.position = p;

        MeshFilter mf = inst.GetComponentInChildren<MeshFilter>();
        BoxCollider col = inst.GetComponent<BoxCollider>();
        if (col == null)
            col = inst.AddComponent<BoxCollider>();
        if (mf != null && mf.sharedMesh != null)
        {
            col.center = mf.sharedMesh.bounds.center;
            col.size = mf.sharedMesh.bounds.size;
        }

        GridFootprint fp = inst.GetComponent<GridFootprint>();
        if (fp == null)
            fp = inst.AddComponent<GridFootprint>();
        fp.type = AssetGridType.Object;
        fp.sizeInCells = Vector2Int.one;

        Rigidbody rb = inst.GetComponent<Rigidbody>();
        if (rb != null)
            Object.DestroyImmediate(rb);

        b = RendererBounds(inst);
        sb.AppendLine($"world size={b.size.x:F3} x {b.size.y:F3} x {b.size.z:F3} minY={b.min.y:F4}");
        sb.AppendLine($"pos={inst.transform.position} scale={inst.transform.localScale}");

        bool sizeOk = Mathf.Abs(b.size.x - 1f) < 0.05f
                      && Mathf.Abs(b.size.z - 1f) < 0.05f
                      && Mathf.Abs(b.size.y - 1f) < 0.05f;
        sb.AppendLine(sizeOk ? "size 1x1x1 OK" : "WARN size is not 1x1x1 m");

        SavePrefab(inst, sb);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine($"saved={ScenePath}");
        sb.AppendLine(sizeOk && white != null && black != null ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static void ConfigureFbx(StringBuilder sb)
    {
        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            sb.AppendLine("FAIL no ModelImporter");
            return;
        }
        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.None)
        {
            importer.animationType = ModelImporterAnimationType.None;
            dirty = true;
        }
        if (Mathf.Abs(importer.globalScale - 1f) > 1e-6f)
        {
            importer.globalScale = 1f;
            dirty = true;
        }
        if (!importer.useFileScale)
        {
            importer.useFileScale = true;
            dirty = true;
        }
        if (importer.bakeAxisConversion)
        {
            importer.bakeAxisConversion = false;
            dirty = true;
        }
        if (dirty)
            importer.SaveAndReimport();
        sb.AppendLine($"fbx anim={importer.animationType} scale={importer.globalScale} bakeAxis={importer.bakeAxisConversion}");
    }

    static Material BuildLit(string path, Color albedo, float metallic, float smoothness, StringBuilder sb)
    {
        EnsureFolder(MatDir);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            sb.AppendLine("FAIL URP Lit shader missing");
            return null;
        }
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(lit);
            AssetDatabase.CreateAsset(mat, path);
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        mat.shader = lit;
        mat.SetColor("_BaseColor", albedo);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", albedo);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness"))
            mat.SetFloat("_Glossiness", smoothness);
        if (mat.HasProperty("_EmissionColor"))
            mat.SetColor("_EmissionColor", Color.black);
        mat.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(mat);
        sb.AppendLine($"mat={path} metal={metallic} smooth={smoothness}");
        return mat;
    }

    static void AssignMaterials(GameObject inst, Material white, Material black, StringBuilder sb)
    {
        foreach (Renderer rend in inst.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = rend.sharedMaterials;
            if (mats.Length == 0)
                mats = new Material[2];
            for (int i = 0; i < mats.Length; i++)
            {
                string key = mats[i] != null ? mats[i].name.Replace(" (Instance)", "").Trim() : "";
                if (key.IndexOf("Handle", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || key.IndexOf("Black", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || i == 1)
                    mats[i] = black;
                else
                    mats[i] = white;
            }
            if (mats.Length == 1)
                mats = new[] { white, black };
            rend.sharedMaterials = mats;
            sb.AppendLine($"renderer '{rend.gameObject.name}' slots={mats.Length}");
        }
    }

    static void FaceDrawersIntoRoom(GameObject inst, Vector3 desiredWorld, StringBuilder sb)
    {
        MeshFilter mf = inst.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || mf.sharedMesh.subMeshCount < 2)
        {
            sb.AppendLine("WARN cannot detect handle facing");
            return;
        }
        Vector3 localFace = SubmeshCentroid(mf.sharedMesh, 1) - SubmeshCentroid(mf.sharedMesh, 0);
        localFace.y = 0f;
        if (localFace.sqrMagnitude < 1e-8f)
        {
            sb.AppendLine("WARN handle offset is vertical");
            return;
        }
        localFace.Normalize();
        Vector3 worldFace = inst.transform.TransformDirection(localFace);
        worldFace.y = 0f;
        worldFace.Normalize();
        desiredWorld.y = 0f;
        desiredWorld.Normalize();
        float yaw = Vector3.SignedAngle(worldFace, desiredWorld, Vector3.up);
        inst.transform.Rotate(0f, yaw, 0f, Space.World);
        sb.AppendLine($"handle localFace={localFace} yaw={yaw:F1}");
    }

    static Vector3 SubmeshCentroid(Mesh mesh, int sub)
    {
        int[] idx = mesh.GetIndices(sub);
        Vector3[] verts = mesh.vertices;
        Vector3 sum = Vector3.zero;
        int n = 0;
        for (int i = 0; i < idx.Length; i++)
        {
            sum += verts[idx[i]];
            n++;
        }
        return n > 0 ? sum / n : Vector3.zero;
    }

    static void SavePrefab(GameObject inst, StringBuilder sb)
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder(PrefabDir);
        GameObject saved = PrefabUtility.SaveAsPrefabAssetAndConnect(
            inst, PrefabPath, InteractionMode.AutomatedAction, out bool ok);
        if (!ok || saved == null)
            sb.AppendLine($"WARN could not save {PrefabPath}");
        else
            sb.AppendLine($"prefab={PrefabPath}");
    }

    static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0)
            return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++)
            b.Encapsulate(rs[i].bounds);
        return b;
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root != null && root.name == name)
                return root;
        return null;
    }

    static void DestroyChild(Transform parent, string name)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform c = parent.GetChild(i);
            if (c.name == name)
                Object.DestroyImmediate(c.gameObject);
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static void WriteReport(string report)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
    }

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}
