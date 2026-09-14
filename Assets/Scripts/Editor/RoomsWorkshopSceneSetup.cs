using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 178 — RoomsWorkshop comparison scene (https://trello.com/c/bCg5ZhlI).
/// Creates Assets/Scenes/RoomsWorkshop.unity (ticket 137's disposable
/// comparison scene) with one untouched DefaultBuilding (117) instance as the
/// v1 reference and a 6x6 "HR Dept" test shell two cells to its right.
///
/// The scene is built ADDITIVELY and then closed: whatever scene is open
/// (Alpha01 while Cursor is in it) is never switched, saved or dirtied. The
/// new scene is made active only for the duration of the build so transient
/// objects never land in the previously active scene.
///
/// The shell is throwaway geometry — plain cube primitives, no prefab, no
/// RoomSlotAnchor / GridFootprint / cutaway visibility. Only footprint and
/// door proportion are under test. Ticket 179 made DefaultBuilding native 6×6
/// (FlushToSlot removed), so the v1-flushed-to-8 baseline in this scene is stale.
///
/// Flag: Temp/setup_rooms_workshop_178.flag
/// </summary>
[InitializeOnLoad]
public static class RoomsWorkshopSceneSetup
{
    public const string ScenePath = "Assets/Scenes/RoomsWorkshop.unity";
    const string BuildingPrefabPath = "Assets/Prefabs/Buildings/DefaultBuilding.prefab";
    const string BuildingFbxPath = "Assets/Art/Buildings/DefaultBuilding.fbx";
    const string GridPrefabPath = "Assets/Prefabs/Grid/GridSystem.prefab";
    const string WallMatPath = "Assets/Art/Buildings/Building_Wall_Lit.mat";
    const string WorldFloorMatPath = "Assets/Art/Environment/GlossyFloor_Mat.mat";
    const string FlagPath = "Temp/setup_rooms_workshop_178.flag";
    const string ReportPath = "Temp/rooms_workshop_178_report.txt";
    const string ShotAngled = "Temp/rooms_workshop_178_angled_v1flushed.png";
    const string ShotTopDown = "Temp/rooms_workshop_178_topdown_v1flushed.png";
    const string ShotAsSaved = "Temp/rooms_workshop_178_angled_as_saved.png";

    public const string V1Name = "DefaultBuilding_v1_ref";
    public const string V2Name = "HRDept_Shell_6x6";

    // v1 sits on tile 0..8; v2 starts two cells to the right so the two shells
    // read separately. Both door walls sit on the z=0 line.
    static readonly Vector3 V1Origin = Vector3.zero;
    static readonly Vector3 V2Origin = new Vector3(10f, 0f, 0f);

    // v2 shell spec (ticket 178 plan, approved 2026-09-12).
    // 6.0m exterior wall-to-wall = "6 cells" the same way v1 reads as 8 in Play.
    public const float Ext = 6f;
    public const float WallT = 0.3f;   // same as v1
    public const float WallH = 3f;     // same as v1 — only the footprint is under test
    public const float DoorW = 1.5f;   // 1.8 * 6/8 = 1.35 would give 0.675m leaves; 1.5 keeps 0.75m leaves
    public const float DoorH = 2.2f;   // same as v1
    const float LeafT = 0.06f;         // same as v1's DoorLeaf meshes
    const float FloorT = 0.05f;        // same as v1's RoomFloor

    // Alpha01 camera (Alpha01SceneSetup): ortho 6, offset (-8,7,-8), looks at target.
    // Target is the middle of the pair: v1 spans x 0..8, v2 spans x 10..16.
    static readonly Vector3 CamOffset = new Vector3(-8f, 7f, -8f);
    static readonly Vector3 CamTarget = new Vector3(8f, 0f, 4f);
    const float OrthoSize = 6f;

    static RoomsWorkshopSceneSetup()
    {
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        File.Delete(Full(FlagPath));
        string report = Run();
        WriteReport(report);
        Debug.Log("[RoomsWorkshopSceneSetup]\n" + report);
    }

    [MenuItem("Tools/Grid/Setup RoomsWorkshop (ticket 178)")]
    public static void SetupFromMenu()
    {
        string report = Run();
        WriteReport(report);
        Debug.Log("[RoomsWorkshopSceneSetup]\n" + report);
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== RoomsWorkshop (ticket 178) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            sb.AppendLine("FAIL: editor is in Play mode — refusing to build scenes under a running session.");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        Scene previous = SceneManager.GetActiveScene();
        bool previousDirtyBefore = previous.isDirty;
        sb.AppendLine($"active before: '{previous.path}' dirty={previousDirtyBefore}");
        if (File.Exists(Full(ScenePath)))
            sb.AppendLine($"note: {ScenePath} already exists and will be overwritten (disposable comparison scene).");

        var buildingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingPrefabPath);
        var gridPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GridPrefabPath);
        var wallMat = AssetDatabase.LoadAssetAtPath<Material>(WallMatPath);
        var worldFloorMat = AssetDatabase.LoadAssetAtPath<Material>(WorldFloorMatPath);
        Material floorMat = FbxMaterial("Floor_Mat");
        Material doorMat = FbxMaterial("Door_Mat");
        if (buildingPrefab == null || gridPrefab == null || wallMat == null || GameplayCameraPrefab.Load() == null)
        {
            sb.AppendLine($"FAIL: missing asset — building={(buildingPrefab != null)} grid={(gridPrefab != null)} wallMat={(wallMat != null)} camera={(GameplayCameraPrefab.Load() != null)}");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }
        if (floorMat == null) { floorMat = wallMat; sb.AppendLine("warn: FBX Floor_Mat not found, using wall material for the shell floor"); }
        if (doorMat == null) { doorMat = wallMat; sb.AppendLine("warn: FBX Door_Mat not found, using wall material for the door leaves"); }
        if (worldFloorMat == null) sb.AppendLine("warn: GlossyFloor_Mat not found, WorldFloor uses the default material");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        bool saved = false;
        try
        {
            // Route every new GameObject into the workshop scene, not Alpha01.
            SceneManager.SetActiveScene(scene);

            // --- Scene skeleton (mirrors Alpha01SceneSetup so camera/scale match) ---
            GameObject gridInst = (GameObject)PrefabUtility.InstantiatePrefab(gridPrefab, scene);
            gridInst.name = "GridSystem";
            gridInst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            GridSystem gs = gridInst.GetComponent<GridSystem>();
            gs.originWorld = Vector3.zero;
            gs.cellSize = 1f;
            GridOverlay overlay = gridInst.GetComponent<GridOverlay>();
            if (overlay != null)
            {
                // Comparison scene: grid on by default (G still toggles it in Play).
                var oso = new SerializedObject(overlay);
                var vis = oso.FindProperty("visible");
                if (vis != null) vis.boolValue = true;
                oso.ApplyModifiedPropertiesWithoutUndo();
            }
            sb.AppendLine("GridSystem prefab origin=0 cellSize=1 overlay=" + (overlay != null) + " visibleByDefault=true");

            GameObject worldFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            worldFloor.name = "WorldFloor";
            worldFloor.transform.SetPositionAndRotation(new Vector3(0f, -0.1f, 0f), Quaternion.identity);
            worldFloor.transform.localScale = new Vector3(96f, 0.2f, 96f);
            if (worldFloorMat != null)
                worldFloor.GetComponent<MeshRenderer>().sharedMaterial = worldFloorMat;

            GameObject camGo = GameplayCameraPrefab.InstantiateLookingAt(
                scene,
                CamTarget + CamOffset,
                CamTarget,
                followTarget: null,
                enableFollow: false,
                solidColorBackground: true);
            if (camGo == null)
            {
                sb.AppendLine("FAIL Instantiate " + GameplayCameraPrefab.PrefabPath);
                sb.AppendLine("RESULT=FAIL");
                return sb.ToString();
            }
            Camera cam = camGo.GetComponent<Camera>();
            sb.AppendLine("Main Camera prefab static, ortho=" + OrthoSize + " offset=" + CamOffset + " target=" + CamTarget + " (CameraFollow disabled)");

            // Ticket 106 — shared key + fill rig (was a lone key light).
            GameplayLightingRig.CreateRig(scene, GameplayLightingRig.DefaultKeyIntensity, sb);

            // --- v1 reference: untouched DefaultBuilding prefab instance on tile 0..8 ---
            GameObject v1 = (GameObject)PrefabUtility.InstantiatePrefab(buildingPrefab, scene);
            v1.name = V1Name;
            v1.transform.SetPositionAndRotation(V1Origin, Quaternion.identity);
            sb.AppendLine($"{V1Name}: DefaultBuilding.prefab instance at {V1Origin} (tile 0..8)");

            // --- v2: 6x6 HR Dept test shell, cube primitives only ---
            GameObject v2 = BuildShell(wallMat, floorMat, doorMat);
            v2.transform.SetPositionAndRotation(V2Origin, Quaternion.identity);
            sb.AppendLine($"{V2Name}: cubes at {V2Origin} ext={Ext} wallT={WallT} wallH={WallH} doorW={DoorW} doorH={DoorH} (tile x 10..16, z 0..6)");

            EditorSceneManager.MarkSceneDirty(scene);
            saved = EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            sb.AppendLine("saved=" + saved + " " + ScenePath);

            // --- Verification (edit mode; nothing below is saved) ---
            AppendBounds(sb, "v1 as saved (raw FBX, edit mode)", v1);
            AppendBounds(sb, "v2 as saved", v2);
            Screenshot(cam, Full(ShotAsSaved), 1920, 1080);
            sb.AppendLine("shot: " + ShotAsSaved);

            // Temporary v1 flush = what RoomSlotAnchor does in Play; discarded on close.
            RoomSlotAnchor anchor = v1.GetComponent<RoomSlotAnchor>();
            if (anchor != null)
            {
                anchor.Init(default(GridCoord));
                AppendBounds(sb, "v1 flushed (temp, = Play-mode shape)", v1);
            }
            else
            {
                sb.AppendLine("warn: v1 has no RoomSlotAnchor — flushed shot equals as-saved");
            }
            GameObject gridLines = TempGridLines();
            Screenshot(cam, Full(ShotAngled), 1920, 1080);
            sb.AppendLine("shot: " + ShotAngled);

            GameObject topGo = new GameObject("TempTopDownCam");
            Camera top = topGo.AddComponent<Camera>();
            top.orthographic = true;
            top.orthographicSize = 6f;
            top.clearFlags = CameraClearFlags.SolidColor;
            top.backgroundColor = cam.backgroundColor;
            topGo.AddComponent<UniversalAdditionalCameraData>();
            topGo.transform.position = new Vector3(8f, 30f, 4f);
            topGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Screenshot(top, Full(ShotTopDown), 1920, 1080);
            sb.AppendLine("shot: " + ShotTopDown);
            Object.DestroyImmediate(topGo);
            Object.DestroyImmediate(gridLines);
        }
        finally
        {
            if (previous.IsValid())
                SceneManager.SetActiveScene(previous);
            // Discards the temporary flush / grid lines; the saved file is untouched.
            EditorSceneManager.CloseScene(scene, true);
        }

        Scene after = SceneManager.GetActiveScene();
        sb.AppendLine($"active after: '{after.path}' dirty={after.isDirty}");
        bool ok = saved
            && File.Exists(Full(ScenePath))
            && after.path == previous.path
            && after.isDirty == previousDirtyBefore;
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    /// <summary>
    /// Mitred like the current v1 FBX: front/back walls run the full exterior
    /// width, side walls sit between them. No overlapping corner boxes, so no
    /// coplanar faces (ticket 119's see-through corner).
    /// </summary>
    static GameObject BuildShell(Material wallMat, Material floorMat, Material doorMat)
    {
        GameObject root = new GameObject(V2Name);
        Transform p = root.transform;
        float mid = Ext * 0.5f;
        float inner0 = WallT;
        float inner1 = Ext - WallT;
        float door0 = mid - DoorW * 0.5f;
        float door1 = mid + DoorW * 0.5f;

        Box(p, "Floor", floorMat, inner0, inner1, 0f, FloorT, inner0, inner1);
        Box(p, "Wall_Back", wallMat, 0f, Ext, 0f, WallH, inner1, Ext);
        Box(p, "Wall_Left", wallMat, 0f, WallT, 0f, WallH, inner0, inner1);
        Box(p, "Wall_Right", wallMat, inner1, Ext, 0f, WallH, inner0, inner1);
        Box(p, "Wall_Front_Left", wallMat, 0f, door0, 0f, WallH, 0f, WallT);
        Box(p, "Wall_Front_Right", wallMat, door1, Ext, 0f, WallH, 0f, WallT);
        Box(p, "Wall_Front_Lintel", wallMat, door0, door1, DoorH, WallH, 0f, WallT);
        // Static leaves centred in the wall thickness, like v1's DoorLeaf meshes.
        float leafZ0 = WallT * 0.5f - LeafT * 0.5f;
        Box(p, "DoorLeaf_Left", doorMat, door0, mid, 0f, DoorH, leafZ0, leafZ0 + LeafT);
        Box(p, "DoorLeaf_Right", doorMat, mid, door1, 0f, DoorH, leafZ0, leafZ0 + LeafT);
        return root;
    }

    static GameObject Box(Transform parent, string name, Material mat,
        float x0, float x1, float y0, float y1, float z0, float z1)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        go.transform.localScale = new Vector3(x1 - x0, y1 - y0, z1 - z0);
        if (mat != null)
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Material FbxMaterial(string name)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(BuildingFbxPath))
            if (o is Material m && m.name == name)
                return m;
        return null;
    }

    static void AppendBounds(StringBuilder sb, string label, GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) { sb.AppendLine($"{label}: no renderers"); return; }
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs)
            if (r.enabled && r.gameObject.activeInHierarchy)
                b.Encapsulate(r.bounds);
        sb.AppendLine($"{label}: x[{b.min.x:F2},{b.max.x:F2}] z[{b.min.z:F2},{b.max.z:F2}] y[{b.min.y:F2},{b.max.y:F2}] -> {b.size.x:F2} x {b.size.z:F2} wall-to-wall, {b.size.y:F2} tall");
    }

    /// <summary>Thin unlit cubes on 1m lines over both tiles; edit-mode stand-in for GridOverlay.</summary>
    static GameObject TempGridLines()
    {
        GameObject root = new GameObject("TempGridLines");
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        Material m = new Material(sh != null ? sh : Shader.Find("Unlit/Color"));
        m.color = new Color(0.2f, 0.85f, 1f, 1f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
        const float y = 0.07f;   // above both room floors (0.05) and the world floor (0)
        for (int z = -2; z <= 10; z++)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "gz" + z;
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = new Vector3(8f, y, z);
            g.transform.localScale = new Vector3(22f, 0.02f, 0.04f);
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
        for (int x = -3; x <= 19; x++)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "gx" + x;
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = new Vector3(x, y, 4f);
            g.transform.localScale = new Vector3(0.04f, 0.02f, 12f);
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
        return root;
    }

    static void Screenshot(Camera cam, string file, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24);
        RenderTexture prevTarget = cam.targetTexture;
        RenderTexture prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }

    static void WriteReport(string report)
    {
        Directory.CreateDirectory(Full("Temp"));
        File.WriteAllText(Full(ReportPath), report);
    }

    static string Full(string rel)
    {
        return Path.Combine(Directory.GetParent(Application.dataPath).FullName, rel);
    }
}
