using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 103 spike: import Meshy chair into GridFoundation, measure transform/bounds,
/// wire existing Object components (CarryableItem / GridFootprint / physics) without
/// editing existing scripts or deleting existing scene objects.
/// Flag: Temp/setup_meshy_chair_spike.flag
/// </summary>
[InitializeOnLoad]
public static class MeshyChairSpikeSetup
{
    // Prefer Blender re-export (Pre-Object checklist). Fall back to raw Meshy FBX.
    const string FbxPathPreferred = "Assets/Objects/Meshy_Chair_Unity/Meshy_Chair_Unity.fbx";
    const string FbxPathRaw =
        "Assets/Objects/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture_fbx/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture.fbx";
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string InstanceName = "Meshy_Chair_Spike";
    const string FlagPath = "Temp/setup_meshy_chair_spike.flag";
    const string ReportPath = "Temp/meshy_chair_spike_report.txt";
    const string ReloadFlagPath = "Temp/force_reload_grid_foundation.flag";

    // Place near existing Nodes without overlapping them (Counter ~3.5,-0.5 Keep ~4.5,-0.5).
    static readonly Vector3 PlacePos = new Vector3(1.5f, 0f, -0.5f);

    static MeshyChairSpikeSetup()
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
            return; // keep flag; PollFlag retries after play exits
        }
        File.Delete(flag);
        string report = RunSpike();
        WriteReport(report);
        Debug.Log("[MeshyChairSpikeSetup]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    [MenuItem("Tools/Grid/Meshy Chair Spike (GridFoundation)")]
    public static void SetupFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            File.WriteAllText(Full(FlagPath), "spike\n");
            EditorUtility.DisplayDialog(
                "Meshy Chair Spike",
                "Exiting Play Mode — spike will run automatically in a moment.",
                "OK");
            return;
        }
        string report = RunSpike();
        WriteReport(report);
        EditorUtility.DisplayDialog("Meshy Chair Spike", Truncate(report, 1600), "OK");
        Debug.Log("[MeshyChairSpikeSetup]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    public static string RunSpike()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Meshy chair pipeline spike (ticket 103) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            sb.AppendLine("FAIL: still in play mode — cannot OpenScene");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        string fbxPath = File.Exists(Full(FbxPathPreferred)) ? FbxPathPreferred : FbxPathRaw;
        sb.AppendLine($"fbx={fbxPath}");
        sb.AppendLine($"pipeline={(fbxPath == FbxPathPreferred ? "Blender re-export" : "direct Meshy FBX")}");

        // Static prop import settings (avoid Generic/Humanoid avatar collapsing bounds).
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        }
        if (importer != null)
        {
            bool dirty = false;
            if (importer.animationType != ModelImporterAnimationType.None)
            {
                importer.animationType = ModelImporterAnimationType.None;
                dirty = true;
            }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                dirty = true;
            }
            if (importer.materialLocation != ModelImporterMaterialLocation.External)
            {
                importer.materialLocation = ModelImporterMaterialLocation.External;
                dirty = true;
            }
            if (!importer.bakeAxisConversion)
            {
                // Blender export already baked; leave false for re-export, true only if needed.
            }
            if (dirty)
                importer.SaveAndReimport();
            else
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        }
        else
        {
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        }
        AssetDatabase.Refresh();

        GameObject fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbxRoot == null)
        {
            sb.AppendLine("FAIL: FBX not imported / LoadAssetAtPath returned null");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer != null)
        {
            sb.AppendLine($"importer.globalScale={importer.globalScale}");
            sb.AppendLine($"importer.useFileScale={importer.useFileScale}");
            sb.AppendLine($"importer.materialImportMode={importer.materialImportMode}");
            sb.AppendLine($"importer.animationType={importer.animationType}");
            sb.AppendLine($"importer.bakeAxisConversion={importer.bakeAxisConversion}");
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Remove prior spike instance only — never touch GridSystem / Nodes / cube / Player.
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != null && root.name == InstanceName)
                Object.DestroyImmediate(root);
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxRoot, scene);
        if (inst == null)
            inst = Object.Instantiate(fbxRoot);
        inst.name = InstanceName;

        // Direct import first: identity rotation/scale at measured floor sit.
        inst.transform.SetPositionAndRotation(PlacePos, Quaternion.identity);
        inst.transform.localScale = Vector3.one;

        // Hierarchy / mesh diagnostics (catch empty-bounds imports).
        foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
        {
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (mf == null && mr == null && smr == null && t == inst.transform)
                continue;
            if (mf != null || mr != null || smr != null || t.childCount == 0)
            {
                string meshInfo = "none";
                if (mf != null && mf.sharedMesh != null)
                    meshInfo = $"MeshFilter verts={mf.sharedMesh.vertexCount} localBounds={mf.sharedMesh.bounds.size}";
                if (smr != null && smr.sharedMesh != null)
                    meshInfo = $"Skinned verts={smr.sharedMesh.vertexCount} localBounds={smr.sharedMesh.bounds.size}";
                sb.AppendLine($"child={t.name} lossyScale={t.lossyScale} {meshInfo}");
            }
        }

        Bounds? bounds = CollectRendererBounds(inst);
        sb.AppendLine("--- direct import (identity rot/scale) ---");
        sb.AppendLine($"pos={inst.transform.position}");
        sb.AppendLine($"rotEuler={inst.transform.rotation.eulerAngles}");
        sb.AppendLine($"lossyScale={inst.transform.lossyScale}");
        if (bounds.HasValue)
        {
            Bounds b = bounds.Value;
            sb.AppendLine($"rendererBounds.min={b.min}");
            sb.AppendLine($"rendererBounds.max={b.max}");
            sb.AppendLine($"rendererBounds.size={b.size}");
            sb.AppendLine($"rendererBounds.center={b.center}");

            // Sit bottom on y=0.
            float lift = -b.min.y;
            Vector3 p = inst.transform.position;
            p.y += lift;
            // Snap XZ to cell center via GridSystem if present.
            GridSystem grid = Object.FindAnyObjectByType<GridSystem>();
            if (grid != null)
            {
                GridCoord cell = grid.WorldToCell(new Vector3(PlacePos.x, 0f, PlacePos.z));
                Vector3 center = grid.CellToWorldCenter(cell);
                p.x = center.x;
                p.z = center.z;
                sb.AppendLine($"gridCell={cell} cellCenterXZ=({center.x},{center.z})");
            }
            inst.transform.position = p;
            bounds = CollectRendererBounds(inst);
            b = bounds.Value;
            sb.AppendLine($"afterFloorSit pos={inst.transform.position}");
            sb.AppendLine($"afterFloorSit bounds.size={b.size} minY={b.min.y:F4}");

            bool rotOk = NearZero(inst.transform.rotation.eulerAngles);
            bool scaleOk = NearOne(inst.transform.lossyScale);
            bool footprintOk = b.size.x <= 1.02f && b.size.z <= 1.02f;
            sb.AppendLine($"check.rotationIdentity={rotOk} euler={inst.transform.rotation.eulerAngles}");
            sb.AppendLine($"check.scaleOne={scaleOk} lossy={inst.transform.lossyScale}");
            sb.AppendLine($"check.objectFootprintFits1x1={footprintOk} (ARCHITECTURE Object rule)");
            if (!footprintOk)
                sb.AppendLine("FRICTION: chair XZ exceeds 1m×1m Object cell — batch 101 Objects may need resize or type=Node");
            if (!rotOk)
                sb.AppendLine("FRICTION: non-identity rotation — may need Blender re-export (Apply All Transforms / -Z Forward Y Up)");
        }
        else
        {
            sb.AppendLine("FAIL: no renderers on instance");
        }

        // Materials / textures
        int matCount = 0;
        int missingMainTex = 0;
        foreach (var rend in inst.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in rend.sharedMaterials)
            {
                if (mat == null)
                    continue;
                matCount++;
                bool hasMap = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null
                    || mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
                if (!hasMap)
                    missingMainTex++;
                sb.AppendLine($"mat={mat.name} shader={mat.shader.name} hasBaseOrMainTex={hasMap}");
            }
        }
        sb.AppendLine($"materialsCounted={matCount} missingBaseOrMainTex={missingMainTex}");
        if (missingMainTex > 0)
            sb.AppendLine("FRICTION: PBR textures not auto-bound — may need Material Creation Mode / remaps or Blender pack");

        // Wire Object gameplay components (additive on NEW instance only).
        WireObjectComponents(inst, sb);

        // Interaction readiness + editor smoke (PickUp → Drop → TryAccept) without Play Mode.
        bool hasCarry = inst.GetComponent<CarryableItem>() != null;
        bool hasRb = inst.GetComponent<Rigidbody>() != null;
        bool hasCol = inst.GetComponentInChildren<Collider>() != null;
        bool hasFp = inst.GetComponent<GridFootprint>() != null;
        bool hasPlayer = Object.FindAnyObjectByType<PlayerController>() != null;
        GameObject counterGo = GameObject.Find("Node_Counter");
        GameObject keepGo = GameObject.Find("Node_Keep");
        bool hasGrid = Object.FindAnyObjectByType<GridSystem>() != null;
        sb.AppendLine("--- interaction wiring ---");
        sb.AppendLine($"CarryableItem={hasCarry} Rigidbody={hasRb} Collider={hasCol} GridFootprint={hasFp}");
        sb.AppendLine($"scene Player={hasPlayer} Node_Counter={counterGo != null} Node_Keep={keepGo != null} GridSystem={hasGrid}");

        bool smokeOk = RunCarryNodeSmoke(inst, counterGo != null ? counterGo : keepGo, sb);
        sb.AppendLine($"carryNodeSmoke={smokeOk}");
        sb.AppendLine("playTest=MANUAL follow-up: E pick up chair near Player, E place on Counter/Keep");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine($"saved={ScenePath} instance={InstanceName}");

        bool ok = hasCarry && hasRb && hasCol && hasFp && hasPlayer && hasGrid && bounds.HasValue && smokeOk;
        sb.AppendLine(ok ? "RESULT=OK (import+wire+smoke)" : "RESULT=FAIL");
        return sb.ToString();
    }

    /// <summary>
    /// Headless: PickUp → Drop → TryAccept on a scene Node, then restore chair pose.
    /// Uses live scene Nodes; does not destroy or reconfigure them.
    /// </summary>
    static bool RunCarryNodeSmoke(GameObject chair, GameObject nodeGo, StringBuilder sb)
    {
        sb.AppendLine("--- carry/node smoke ---");
        CarryableItem item = chair != null ? chair.GetComponent<CarryableItem>() : null;
        DefaultNode node = nodeGo != null ? nodeGo.GetComponent<DefaultNode>() : null;
        if (item == null || node == null)
        {
            sb.AppendLine("FAIL smoke missing CarryableItem or DefaultNode");
            return false;
        }

        Vector3 restorePos = chair.transform.position;
        Quaternion restoreRot = chair.transform.rotation;

        var socketGo = new GameObject("MeshySpike_TempCarrySocket");
        socketGo.transform.position = restorePos + Vector3.up * 1.45f;
        try
        {
            // Clear any prior occupant so TryAccept can succeed.
            if (node.IsOccupied && node.Occupant != null)
                node.NotifyItemRemoved(node.Occupant);

            item.PickUp(socketGo.transform);
            bool carried = item.IsCarried && item.Body != null && item.Body.isKinematic;
            sb.AppendLine($"pickUp ok={carried} isCarried={item.IsCarried}");

            Vector3 dropPos = restorePos + new Vector3(0.5f, 0f, 0f);
            item.Drop(dropPos, restoreRot);
            bool dropped = !item.IsCarried && item.Body != null && !item.Body.isKinematic;
            sb.AppendLine($"drop ok={dropped} pos={chair.transform.position}");

            item.PickUp(socketGo.transform);
            bool accepted = node.TryAccept(item);
            // PlaceOnNode clears carried flag; item parents under Node Slot.
            bool parentedToNode = chair.transform.IsChildOf(nodeGo.transform);
            sb.AppendLine($"tryAccept={accepted} parentedToNode={parentedToNode} nodeOccupied={node.IsOccupied} isCarried={item.IsCarried}");

            // Restore free chair for Play testing (remove from node, re-place on floor).
            if (node.IsOccupied && node.Occupant != null)
                node.NotifyItemRemoved(node.Occupant);
            if (item.IsCarried)
                item.Drop(restorePos, restoreRot);
            else
            {
                // Mimic Drop physics restore after PlaceOnNode left kinematic parented state.
                chair.transform.SetParent(null, true);
                chair.transform.SetPositionAndRotation(restorePos, restoreRot);
                chair.transform.localScale = Vector3.one;
                if (item.Body != null)
                {
                    item.Body.detectCollisions = true;
                    item.Body.isKinematic = false;
                    item.Body.useGravity = true;
                }
                var cols = chair.GetComponentsInChildren<Collider>(true);
                foreach (var c in cols)
                    c.enabled = true;
            }

            chair.transform.SetPositionAndRotation(restorePos, restoreRot);
            sb.AppendLine($"restoredFloor pos={chair.transform.position}");
            return carried && dropped && accepted && parentedToNode;
        }
        catch (System.Exception e)
        {
            sb.AppendLine("FAIL smoke exception: " + e.Message);
            return false;
        }
        finally
        {
            Object.DestroyImmediate(socketGo);
        }
    }

    static void WireObjectComponents(GameObject root, StringBuilder sb)
    {
        // Prefer root; if mesh is on child only, still put gameplay on root.
        Bounds? b = CollectRendererBounds(root);
        Vector3 size = b.HasValue ? b.Value.size : new Vector3(0.7f, 0.7f, 0.7f);
        Vector3 localCenter = root.transform.InverseTransformPoint(b.HasValue ? b.Value.center : root.transform.position);

        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = root.AddComponent<BoxCollider>();
        // Approximate collider in local space from world bounds (works at identity scale).
        box.size = new Vector3(
            size.x / Mathf.Max(0.0001f, root.transform.lossyScale.x),
            size.y / Mathf.Max(0.0001f, root.transform.lossyScale.y),
            size.z / Mathf.Max(0.0001f, root.transform.lossyScale.z));
        box.center = localCenter;
        sb.AppendLine($"BoxCollider size={box.size} center={box.center}");

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb == null)
            rb = root.AddComponent<Rigidbody>();
        rb.mass = 5f;
        rb.linearDamping = 2f;
        rb.angularDamping = 2.4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        PushableItem push = root.GetComponent<PushableItem>();
        if (push == null)
            push = root.AddComponent<PushableItem>();
        push.ApplyDefaults();

        if (root.GetComponent<CarryableItem>() == null)
            root.AddComponent<CarryableItem>();

        GridFootprint fp = root.GetComponent<GridFootprint>();
        if (fp == null)
            fp = root.AddComponent<GridFootprint>();
        fp.type = AssetGridType.Object;
        fp.sizeInCells = Vector2Int.one;
        sb.AppendLine("wired: BoxCollider+Rigidbody+PushableItem+CarryableItem+GridFootprint(Object 1x1)");
    }

    static Bounds? CollectRendererBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
            return null;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);
        return b;
    }

    static bool NearZero(Vector3 e)
    {
        float wrap(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }
        return Mathf.Abs(wrap(e.x)) < 1f && Mathf.Abs(wrap(e.y)) < 1f && Mathf.Abs(wrap(e.z)) < 1f;
    }

    static bool NearOne(Vector3 s) =>
        Mathf.Abs(s.x - 1f) < 0.02f && Mathf.Abs(s.y - 1f) < 0.02f && Mathf.Abs(s.z - 1f) < 0.02f;

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
