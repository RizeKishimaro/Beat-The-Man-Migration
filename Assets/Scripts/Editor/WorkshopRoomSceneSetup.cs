using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 132 — Workshop/Garage room (https://trello.com/c/AeKRQ6pD).
/// Dresses the existing DefaultBuilding instance (ticket 117, tweaked in 122)
/// as a workshop and stands the ticket-132 chassis on a 2x3 plinth in it.
/// Does not build a new room shell.
///
/// Placement is derived from the building's own InteriorTrigger volume at run
/// time rather than hardcoded, so it survives the building being moved. The
/// walls and floor are unnamed children in the prefab, so InteriorTrigger is
/// the only reliable geometric handle.
///
/// Flag: Temp/setup_workshop_room.flag
/// </summary>
[InitializeOnLoad]
public static class WorkshopRoomSceneSetup
{
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    // Parts are discovered from the Objects/F1 Car/ folder convention rather
    // than listed here, so a newly exported part appears in this scene without
    // a code change — and, more importantly, so a re-run of this script cannot
    // silently drop a part that F1CarSyncTool (136) added to the prefab.
    const string BuildingName = "DefaultBuilding_117";
    const string MatDir = "Assets/Art/Workshop";
    const string CarPrefabDir = F1CarConvention.PrefabDir;
    const string CarPrefabPath = F1CarConvention.PrefabPath;
    const string PlinthName = "Workshop_Plinth";
    const string CarRootName = F1CarConvention.RootName;
    const string ChassisName = "F1_Chassis";
    const string FlagPath = "Temp/setup_workshop_room.flag";
    const string ReportPath = "Temp/setup_workshop_room_report.txt";
    const string ShotPath = "Temp/workshop_room.png";
    const string ReloadFlagPath = "Temp/force_reload_grid_foundation.flag";

    // Plinth: 2 cells wide (X) x 3 cells deep (Z), 0.5m tall. Cells are 1m.
    // Fixed spec — the plinth does not change when the car scale changes.
    static readonly Vector2Int PlinthCells = new Vector2Int(2, 3);
    const float PlinthHeight = 0.5f;

    // Interior is 5.4×5.4 m (ticket 179). Centred across X, and
    // set one cell off the door wall in Z, which is the -Z side of the shell.
    const int InsetFromDoorWallCells = 1;

    /// <summary>Ticket 135 scale/yaw — defined once in F1CarConvention.</summary>
    const float CarScale = F1CarConvention.Scale;
    const float CarYaw = F1CarConvention.Yaw;

    static WorkshopRoomSceneSetup()
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
        Debug.Log("[WorkshopRoomSceneSetup]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    [MenuItem("Tools/Grid/Setup Workshop Room (ticket 132)")]
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
        EditorUtility.DisplayDialog("Workshop Room", Truncate(report, 1600), "OK");
        Debug.Log("[WorkshopRoomSceneSetup]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Workshop room (ticket 132) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject building = FindRoot(scene, BuildingName);
        if (building == null)
        {
            sb.AppendLine($"FAIL: no root named '{BuildingName}' in {ScenePath}.");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        // The shell's only named, reliably-placed handle.
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
            sb.AppendLine("FAIL: DefaultBuilding has no InteriorTrigger — cannot derive interior bounds.");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        Bounds room = new Bounds(
            interior.transform.TransformPoint(interior.center),
            Vector3.Scale(interior.size, interior.transform.lossyScale));
        float floorY = room.min.y;
        sb.AppendLine($"interior world bounds min={room.min} max={room.max} size={room.size}");
        sb.AppendLine($"floorY={floorY:F3}");

        GridSystem grid = Object.FindFirstObjectByType<GridSystem>();
        float cell = grid != null ? grid.cellSize : 1f;
        sb.AppendLine(grid != null
            ? $"grid found: origin={grid.originWorld} cellSize={cell}"
            : "WARN no GridSystem in scene — assuming 1m cells");

        // Cell-align to the grid so the plinth sits on whole cells.
        int minCellX = Mathf.RoundToInt((room.min.x - (grid != null ? grid.originWorld.x : 0f)) / cell);
        int minCellZ = Mathf.RoundToInt((room.min.z - (grid != null ? grid.originWorld.z : 0f)) / cell);
        int roomCellsX = Mathf.RoundToInt(room.size.x / cell);
        int roomCellsZ = Mathf.RoundToInt(room.size.z / cell);

        // Centred across X; offset off the -Z (door) wall in Z.
        int anchorX = minCellX + Mathf.FloorToInt((roomCellsX - PlinthCells.x) / 2f);
        int anchorZ = minCellZ + InsetFromDoorWallCells;
        sb.AppendLine($"room cells: x[{minCellX}..{minCellX + roomCellsX}] z[{minCellZ}..{minCellZ + roomCellsZ}]");
        sb.AppendLine($"plinth anchor cell=({anchorX},{anchorZ}) size={PlinthCells.x}x{PlinthCells.y} cells");

        Vector3 plinthMin = new Vector3(
            (grid != null ? grid.originWorld.x : 0f) + anchorX * cell,
            floorY,
            (grid != null ? grid.originWorld.z : 0f) + anchorZ * cell);
        Vector3 plinthSize = new Vector3(PlinthCells.x * cell, PlinthHeight, PlinthCells.y * cell);
        Vector3 plinthCentre = plinthMin + plinthSize * 0.5f;
        float plinthTopY = floorY + PlinthHeight;

        DestroyChild(building.transform, PlinthName);
        DestroyChild(building.transform, CarRootName);
        DestroyChild(building.transform, ChassisName); // legacy: chassis used to be parented directly

        Material plinthMat = EnsureMaterial("Workshop_Plinth", new Color(0.28f, 0.29f, 0.32f), 0.1f, 0.65f);

        GameObject plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plinth.name = PlinthName;
        plinth.transform.SetParent(building.transform, true);
        plinth.transform.position = plinthCentre;
        plinth.transform.rotation = Quaternion.identity;
        plinth.transform.localScale = plinthSize;
        plinth.GetComponent<Renderer>().sharedMaterial = plinthMat;
        // Visual is 0.5m tall. A matching BoxCollider is a step for the
        // CharacterController (no gravity, stepOffset 0.3). Stretch the
        // collider into a wall so the display blocks walking without a step-up.
        BoxCollider plinthCol = plinth.GetComponent<BoxCollider>();
        if (plinthCol == null)
            plinthCol = plinth.AddComponent<BoxCollider>();
        const float blockHeight = 2f;
        float sy = plinthSize.y;
        plinthCol.enabled = true;
        plinthCol.size = new Vector3(1f, blockHeight / sy, 1f);
        plinthCol.center = new Vector3(0f, (blockHeight * 0.5f - sy * 0.5f) / sy, 0f);

        // "Object" type is locked to 1x1 by GridFootprint.OnValidate, so a 2x3
        // plinth is classified Node (a fixed, multi-cell fixture).
        GridFootprint fp = plinth.GetComponent<GridFootprint>();
        if (fp == null)
            fp = plinth.AddComponent<GridFootprint>();
        fp.type = AssetGridType.Node;
        fp.sizeInCells = PlinthCells;
        sb.AppendLine($"plinth: centre={plinthCentre} scale={plinthSize} top={plinthTopY:F3}");

        // Assembled-car root carries the one and only scale multiplier (135).
        // Parts go under it at local scale 1 and are never scaled themselves.
        GameObject carRoot = new GameObject(CarRootName);
        SceneManager.MoveGameObjectToScene(carRoot, scene);
        carRoot.transform.SetParent(building.transform, true);
        carRoot.transform.position = plinthCentre;
        // Set before parts go on: the seating maths below measures world bounds,
        // which only line up if the car is already rotated.
        carRoot.transform.rotation = Quaternion.Euler(0f, CarYaw, 0f);
        carRoot.transform.localScale = Vector3.one * CarScale;

        sb.AppendLine($"car root '{CarRootName}' scale={CarScale} yaw={CarYaw} (ticket 135; parts stay at local scale 1)");

        var discovered = F1CarConvention.DiscoverParts(sb);
        if (discovered.Count == 0)
        {
            sb.AppendLine($"FAIL: no part FBXs found under {F1CarConvention.PartsDir}");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        foreach (F1CarConvention.Part p in discovered)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(p.FbxPath);
            if (asset == null)
            {
                sb.AppendLine($"FAIL: part FBX not imported yet at {p.FbxPath}");
                sb.AppendLine("RESULT=FAIL");
                return sb.ToString();
            }

            GameObject part = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            part.name = p.Name;
            part.transform.SetParent(carRoot.transform, false);
            F1CarConvention.PlacePart(part.transform);

            Bounds pb = RendererBounds(part);
            sb.AppendLine($"  part '{part.name}' at local zero, world size="
                          + $"{pb.size.x:F3} x {pb.size.y:F3} x {pb.size.z:F3}");

            ReuseAuthoredMaterials(part, sb);
        }

        // Sit the whole assembly on the plinth using its real renderer bounds
        // rather than assuming where the pivot falls inside the mesh.
        Bounds cb = RendererBounds(carRoot);
        Vector3 fix = carRoot.transform.position;
        fix.y += plinthTopY - cb.min.y;
        fix.x += plinthCentre.x - cb.center.x;
        fix.z += plinthCentre.z - cb.center.z;
        carRoot.transform.position = fix;

        Bounds after = RendererBounds(carRoot);
        sb.AppendLine($"whole car bounds min={after.min} max={after.max} size={after.size}");
        sb.AppendLine($"car sits at y={after.min.y:F4} (plinth top {plinthTopY:F4})");
        bool overhangs = after.size.x > plinthSize.x + 1e-3f || after.size.z > plinthSize.z + 1e-3f;
        sb.AppendLine(overhangs
            ? $"WARN car overhangs the plinth (car {after.size.x:F2}x{after.size.z:F2} vs plinth {plinthSize.x:F2}x{plinthSize.z:F2})"
            : "car fits within the plinth footprint");

        // Ticket 135 guard: the scale lives on the car root and nowhere else.
        foreach (Transform t in carRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == carRoot.transform)
                continue;
            if ((t.localScale - Vector3.one).sqrMagnitude > 1e-6f)
                sb.AppendLine($"WARN per-part scale on '{t.name}' = {t.localScale} — should be 1 (ticket 135)");
        }

        // Persist the assembled car as the reusable master (ARCHITECTURE.md:
        // "Each Master = one reusable Prefab"). This is what carries the ×3.2
        // to 133/134 and any future scene — without it the scale would live
        // only in this one scene and this one script.
        SaveCarPrefab(carRoot, sb);

        Capture(room, building, sb);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine($"saved={ScenePath}");
        sb.AppendLine("RESULT=OK");
        return sb.ToString();
    }

    /// <summary>Ticket 130 authored materials — map and swap live in F1CarConvention.</summary>
    static void ReuseAuthoredMaterials(GameObject part, StringBuilder sb) =>
        F1CarConvention.ReuseAuthoredMaterials(part, sb);

    static void SaveCarPrefab(GameObject carRoot, StringBuilder sb)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(CarPrefabDir))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Vehicles");

        GameObject saved = PrefabUtility.SaveAsPrefabAssetAndConnect(
            carRoot, CarPrefabPath, InteractionMode.AutomatedAction, out bool ok);
        if (!ok || saved == null)
        {
            sb.AppendLine($"WARN could not save {CarPrefabPath}");
            return;
        }
        sb.AppendLine($"prefab={CarPrefabPath} (scale={CarScale}, yaw={CarYaw} baked on root)");
    }

    static Bounds RendererBounds(GameObject go) => F1CarConvention.RendererBounds(go);

    /// <summary>Isometric render of the room, matching the game camera angle, for eyeballing placement.</summary>
    static void Capture(Bounds room, GameObject building, StringBuilder sb)
    {
        GameObject camGo = new GameObject("~WorkshopShotCam");
        // The wall/roof cutaway is a play-mode effect, so for a still we drop it
        // ourselves: the ceiling, plus the two walls between camera and room
        // (camera sits off -X/-Z, matching the game's isometric offset).
        var hidden = new List<Renderer>();
        foreach (Renderer r in building.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled)
                continue;
            Bounds b = r.bounds;
            bool ceiling = b.min.y >= room.max.y - 0.25f;
            bool nearWallZ = b.max.z <= room.min.z + 0.4f;
            bool nearWallX = b.max.x <= room.min.x + 0.4f;
            if (ceiling || nearWallZ || nearWallX)
            {
                r.enabled = false;
                hidden.Add(r);
            }
        }
        try
        {
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            camGo.transform.position = room.center + new Vector3(-8f, 7f, -8f);
            camGo.transform.LookAt(room.center);

            var rt = new RenderTexture(1200, 900, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;

            string full = Full(ShotPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            sb.AppendLine($"shot={ShotPath}");
        }
        catch (System.Exception e)
        {
            sb.AppendLine("WARN capture failed: " + e.Message);
        }
        finally
        {
            Object.DestroyImmediate(camGo);
            foreach (Renderer r in hidden)
                if (r != null)
                    r.enabled = true;
        }
    }

    static Material EnsureMaterial(string name, Color colour, float metallic, float smoothness)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art"))
            AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/Art", "Workshop");

        string path = MatDir + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            // Explicit null check: ?? is unreliable against UnityEngine.Object.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = colour;
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
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
