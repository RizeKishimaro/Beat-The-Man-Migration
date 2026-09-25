using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Bake RoomsWorkshop room authoring into the prefab Alpha01 instantiates.
/// In-place overwrite of Room_HR.prefab (GUID kept). Collapse child mesh
/// offsets onto the object root on the room instance only — does not rewrite
/// approved object prefabs or ObjectProperties.pivotOffset.
/// Does not save GridFoundation.
///
/// Standing rule: re-Approving an object that appears in a baked room can
/// restore its mesh child offset. After any object Approve, run Verify on
/// every room containing that object, and report the result.
///
/// Menu: Tools/Rooms/Bake Workshop Room To Prefab
/// Menu: Tools/Rooms/Verify Workshop Room Against Prefab
/// Flags: Temp/bake_workshop_room.flag, Temp/verify_workshop_room.flag
/// Reports: Temp/bake_workshop_room.txt, Temp/verify_workshop_room.txt
/// Play self-test: Temp/bake_workshop_room_play.flag / play.txt
/// </summary>
[InitializeOnLoad]
public static class RoomsWorkshop_BakeRoomPrefab
{
    public const string RoomPrefabPath = "Assets/Prefabs/Buildings/Room_HR.prefab";
    public const string RoomName = "Room_HR";
    const string WorkshopPath = RoomsWorkshopSceneSetup.ScenePath;
    const string Alpha01Path = Alpha01SceneSetup.ScenePath;
    const string PlaygroundPath = "Assets/Scenes/GridFoundation.unity";
    const string BakeFlag = "Temp/bake_workshop_room.flag";
    const string VerifyFlag = "Temp/verify_workshop_room.flag";
    const string PlayFlag = "Temp/bake_workshop_room_play.flag";
    const string BakeReport = "Temp/bake_workshop_room.txt";
    const string VerifyReport = "Temp/verify_workshop_room.txt";
    const string PlayReport = "Temp/bake_workshop_room_play.txt";
    const string PlayPendingKey = "RoomsWorkshop_BakeRoom.playPending";
    const float Ext = 6f;
    const float InteriorMin = 0.3f;
    const float InteriorMax = 5.7f;
    const float BoundsEps = 0.02f;
    const float MeshLocalFail = 1f;
    // compile tick 184-2

    static readonly string[] SkipRootNames =
    {
        "Main Camera", "MainCamera", "Directional Light", "Fill Light",
        "WorldFloor", "GridSystem", RoomName
    };

    static int sPlayWait;
    static StringBuilder sPlayLog;
    static GameObject sPlayRoom;
    static Vector3 sPlayCorner;

    static RoomsWorkshop_BakeRoomPrefab()
    {
        EditorApplication.delayCall += TryFlags;
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlayState;
    }

    static void Poll()
    {
        if (SessionState.GetBool(PlayPendingKey, false) && EditorApplication.isPlaying)
        {
            TickPlay();
            return;
        }
        if (EditorApplication.isCompiling)
            return;
        if (File.Exists(Full(BakeFlag)))
        {
            TryBake();
            return;
        }
        if (File.Exists(Full(VerifyFlag)))
        {
            TryVerifyOnly();
            return;
        }
        if (File.Exists(Full(PlayFlag)))
            TryStartPlay();
    }

    static void TryFlags()
    {
        Poll();
    }

    [MenuItem("Tools/Rooms/Bake Workshop Room To Prefab")]
    public static void MenuBake()
    {
        string report = Bake();
        EditorUtility.DisplayDialog("Bake Workshop Room", Truncate(report, 1600), "OK");
    }

    [MenuItem("Tools/Rooms/Verify Workshop Room Against Prefab")]
    public static void MenuVerify()
    {
        string report = VerifyOnly();
        EditorUtility.DisplayDialog("Verify Workshop Room", Truncate(report, 1600), "OK");
    }

    static void TryBake()
    {
        if (!File.Exists(Full(BakeFlag)))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += TryBake;
            return;
        }
        if (EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += TryBake;
            return;
        }
        try
        {
            File.Delete(Full(BakeFlag));
            string report = Bake();
            Debug.Log("[RoomsWorkshop_BakeRoomPrefab]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReportTo(BakeReport, "ERROR " + e);
            Debug.LogError("[RoomsWorkshop_BakeRoomPrefab] " + e);
        }
    }

    static void TryVerifyOnly()
    {
        if (!File.Exists(Full(VerifyFlag)))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += TryVerifyOnly;
            return;
        }
        if (EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += TryVerifyOnly;
            return;
        }
        try
        {
            File.Delete(Full(VerifyFlag));
            string report = VerifyOnly();
            Debug.Log("[RoomsWorkshop_VerifyRoom]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReportTo(VerifyReport, "ERROR " + e);
            Debug.LogError("[RoomsWorkshop_VerifyRoom] " + e);
        }
    }

    public static string VerifyOnly()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Verify Workshop Room Against Prefab ===");
        sb.AppendLine("writes nothing");
        sb.AppendLine("standing rule: after any object Approve, re-run Verify on rooms that contain it");
        if (!PrepareWorkshop(sb, out GameObject room))
        {
            sb.AppendLine("RESULT=FAIL");
            WriteReportTo(VerifyReport, sb.ToString());
            return sb.ToString();
        }
        bool ok = RunVerify(sb, room, snapshotBefore: null);
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        WriteReportTo(VerifyReport, sb.ToString());
        return sb.ToString();
    }

    public static string Bake()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Bake Workshop Room To Prefab ===");
        sb.AppendLine("time=" + System.DateTime.UtcNow.ToString("o"));
        sb.AppendLine("prefab=" + RoomPrefabPath + " GUID kept, in-place overwrite");
        sb.AppendLine("does not rewrite approved object prefabs or ObjectProperties.pivotOffset");
        sb.AppendLine("does not save GridFoundation");
        sb.AppendLine("standing rule: re-Approving an object in a baked room can restore mesh child offset; Verify after Approve");

        if (!PrepareWorkshop(sb, out GameObject room))
        {
            sb.AppendLine("RESULT=FAIL");
            WriteReportTo(BakeReport, sb.ToString());
            return sb.ToString();
        }

        if (!Classify(room, sb, out List<GameObject> already, out List<GameObject> absorbed, out List<GameObject> straddle))
        {
            sb.AppendLine("RESULT=FAIL");
            WriteReportTo(BakeReport, sb.ToString());
            return sb.ToString();
        }

        if (straddle.Count > 0)
        {
            sb.AppendLine("REFUSE: renderer bounds straddle the 6x6 envelope (no guess)");
            foreach (GameObject go in straddle)
            {
                Bounds b = RendererBounds(go);
                sb.AppendLine("  straddle " + go.name + " worldAABB x[" + F(b.min.x) + "," + F(b.max.x)
                    + "] z[" + F(b.min.z) + "," + F(b.max.z) + "]");
            }
            sb.AppendLine("RESULT=FAIL");
            WriteReportTo(BakeReport, sb.ToString());
            return sb.ToString();
        }

        bool rebake = already.Count > 0;
        sb.AppendLine("rebake=" + rebake);

        sb.AppendLine("--- already children ---");
        if (already.Count == 0)
            sb.AppendLine("(none)");
        foreach (GameObject go in already)
            sb.AppendLine("  " + go.name + " " + LocalPose(go.transform));

        sb.AppendLine("--- absorbed loose objects ---");
        if (absorbed.Count == 0)
            sb.AppendLine("(none)");
        foreach (GameObject go in absorbed)
            sb.AppendLine("  " + go.name + " world=" + go.transform.position);

        var furniture = new List<GameObject>();
        furniture.AddRange(already);
        foreach (GameObject go in absorbed)
        {
            go.transform.SetParent(room.transform, true);
            furniture.Add(go);
            sb.AppendLine("parented loose " + go.name + " -> local " + LocalPose(go.transform));
        }

        var snapshots = new Dictionary<string, List<PropEntry>>();
        var colliderOld = new Dictionary<string, Vector2Box>();
        var boundsBefore = new Dictionary<string, Bounds>();
        foreach (GameObject go in furniture)
        {
            snapshots[go.name] = Snapshot(go);
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box != null)
                colliderOld[go.name] = new Vector2Box { center = box.center, size = box.size };
            boundsBefore[go.name] = RendererBounds(go);
        }

        sb.AppendLine("--- collapse + collider refit ---");
        foreach (GameObject go in furniture)
        {
            Transform mesh = FindMeshChild(go.transform);
            Vector3 meshLocalBefore = mesh != null ? mesh.localPosition : Vector3.zero;
            Vector3 rootBefore = go.transform.localPosition;
            CollapseOntoRoot(go);
            BoxCollider box = go.GetComponent<BoxCollider>();
            Vector3 boxOldC = colliderOld.ContainsKey(go.name) ? colliderOld[go.name].center : Vector3.zero;
            Vector3 boxOldS = colliderOld.ContainsKey(go.name) ? colliderOld[go.name].size : Vector3.zero;
            Vector3 boxNewC = boxOldC;
            Vector3 boxNewS = boxOldS;
            if (box != null)
            {
                RefitBoxCollider(go, box);
                boxNewC = box.center;
                boxNewS = box.size;
            }
            Vector3 meshLocalAfter = mesh != null ? mesh.localPosition : Vector3.zero;
            sb.AppendLine("  " + go.name);
            sb.AppendLine("    root local " + rootBefore + " -> " + go.transform.localPosition);
            sb.AppendLine("    mesh local " + meshLocalBefore + " -> " + meshLocalAfter
                + (mesh != null ? " path=" + RelPath(go.transform, mesh) : " (no mesh child)"));
            sb.AppendLine("    BoxCollider center " + boxOldC + " -> " + boxNewC
                + " size " + boxOldS + " -> " + boxNewS);
            Bounds vis = RendererBounds(go);
            Bounds prev = boundsBefore[go.name];
            sb.AppendLine("    visual AABB before x[" + F(prev.min.x) + "," + F(prev.max.x)
                + "] z[" + F(prev.min.z) + "," + F(prev.max.z) + "]");
            sb.AppendLine("    visual AABB after  x[" + F(vis.min.x) + "," + F(vis.max.x)
                + "] z[" + F(vis.min.z) + "," + F(vis.max.z) + "]");
        }

        sb.AppendLine("--- report printed before prefab write ---");
        WriteReportTo(BakeReport, sb.ToString());

        GameObject saved = PrefabUtility.SaveAsPrefabAssetAndConnect(
            room, RoomPrefabPath, InteractionMode.AutomatedAction, out bool prefabOk);
        if (!prefabOk || saved == null)
        {
            sb.AppendLine("FAIL SaveAsPrefabAssetAndConnect " + RoomPrefabPath);
            sb.AppendLine("RESULT=FAIL");
            WriteReportTo(BakeReport, sb.ToString());
            return sb.ToString();
        }

        EditorSceneManager.MarkSceneDirty(room.scene);
        bool workshopSaved = EditorSceneManager.SaveScene(room.scene, WorkshopPath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("wrote " + RoomPrefabPath + " guid kept");
        sb.AppendLine("workshopSaved=" + workshopSaved);

        bool verifyOk = RunVerify(sb, room, snapshots);
        bool ok = workshopSaved && verifyOk;
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        WriteReportTo(BakeReport, sb.ToString());

        if (ok)
        {
            EditorSceneManager.OpenScene(Alpha01Path, OpenSceneMode.Single);
            sb.AppendLine("reloaded " + Alpha01Path + " for play self-test");
            WriteReportTo(BakeReport, sb.ToString());
            File.WriteAllText(Full(PlayFlag), "run\n");
        }
        return sb.ToString();
    }

    static bool PrepareWorkshop(StringBuilder sb, out GameObject room)
    {
        room = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            sb.AppendLine("FAIL: editor is in Play mode");
            return false;
        }
        DiscardDirtyPlayground(sb);
        if (!File.Exists(Full(WorkshopPath)))
        {
            sb.AppendLine("FAIL missing " + WorkshopPath);
            return false;
        }
        Scene scene = EditorSceneManager.OpenScene(WorkshopPath, OpenSceneMode.Single);
        sb.AppendLine("opened " + scene.path);
        room = FindRoot(scene, RoomName);
        if (room == null)
        {
            sb.AppendLine("FAIL missing " + RoomName);
            return false;
        }
        Vector3 origin = room.transform.position;
        if (origin.sqrMagnitude > 0.0001f)
        {
            sb.AppendLine("FAIL room root is not floor-origin corner: " + origin);
            return false;
        }
        sb.AppendLine("room origin=" + origin + " (floor-origin corner)");
        return true;
    }

    static bool Classify(
        GameObject room,
        StringBuilder sb,
        out List<GameObject> already,
        out List<GameObject> absorbed,
        out List<GameObject> straddle)
    {
        already = CollectFurniture(room);
        absorbed = new List<GameObject>();
        straddle = new List<GameObject>();
        Scene scene = room.scene;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null || root == room)
                continue;
            if (IsSkipRoot(root.name))
                continue;
            if (!LooksLikeFurniture(root))
                continue;
            Bounds b = RendererBounds(root);
            EnvelopeKind kind = ClassifyEnvelope(room.transform, b);
            if (kind == EnvelopeKind.Inside)
                absorbed.Add(root);
            else if (kind == EnvelopeKind.Straddle)
                straddle.Add(root);
            else
                sb.AppendLine("loose outside (left in scene): " + root.name);
        }
        return true;
    }

    static List<GameObject> CollectFurniture(GameObject room)
    {
        var list = new List<GameObject>();
        for (int i = 0; i < room.transform.childCount; i++)
        {
            GameObject child = room.transform.GetChild(i).gameObject;
            if (IsShellChildName(child.name))
                continue;
            string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child);
            if (!string.IsNullOrEmpty(src)
                && (src.EndsWith("DefaultBuilding.prefab") || src.EndsWith("DefaultBuilding.fbx")
                    || src.Contains("/Prefabs/Grid/")))
                continue;
            list.Add(child);
        }
        return list;
    }

    static bool IsShellChildName(string name)
    {
        if (name.StartsWith("Wall_") || name.StartsWith("Door"))
            return true;
        return name == "InteriorTrigger" || name == "RoomFloor" || name == "Roof"
            || name == "Ceiling" || name == "DefaultBuilding";
    }

    static bool LooksLikeFurniture(GameObject go)
    {
        if (go.GetComponent<CarryableItem>() != null || go.GetComponent<PushableItem>() != null)
            return true;
        if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
            return false;
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
        return !IsShellPrefab(path) && !string.IsNullOrEmpty(path);
    }

    static bool IsShellPrefab(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        if (path == RoomPrefabPath)
            return true;
        if (path.EndsWith("DefaultBuilding.prefab") || path.EndsWith("DefaultBuilding.fbx"))
            return true;
        if (path.Contains("/Grid/"))
            return true;
        return false;
    }

    static bool IsSkipRoot(string name)
    {
        for (int i = 0; i < SkipRootNames.Length; i++)
            if (name == SkipRootNames[i])
                return true;
        return false;
    }

    enum EnvelopeKind { Inside, Outside, Straddle }

    static EnvelopeKind ClassifyEnvelope(Transform room, Bounds world)
    {
        Vector3 min = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, 0f, float.NegativeInfinity);
        Vector3 c = world.center;
        Vector3 e = world.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                0f,
                (i & 2) == 0 ? -e.z : e.z);
            Vector3 local = room.InverseTransformPoint(corner);
            min.x = Mathf.Min(min.x, local.x);
            min.z = Mathf.Min(min.z, local.z);
            max.x = Mathf.Max(max.x, local.x);
            max.z = Mathf.Max(max.z, local.z);
        }
        bool fullyIn = min.x >= -0.0001f && max.x <= Ext + 0.0001f
            && min.z >= -0.0001f && max.z <= Ext + 0.0001f;
        bool fullyOut = max.x < -0.0001f || min.x > Ext + 0.0001f
            || max.z < -0.0001f || min.z > Ext + 0.0001f;
        if (fullyIn)
            return EnvelopeKind.Inside;
        if (fullyOut)
            return EnvelopeKind.Outside;
        return EnvelopeKind.Straddle;
    }

    static void CollapseOntoRoot(GameObject go)
    {
        Transform mesh = FindMeshChild(go.transform);
        Bounds vis = RendererBounds(go);
        Vector3 desiredRoot = new Vector3(vis.center.x, vis.min.y, vis.center.z);
        Vector3 meshWorld = mesh != null ? mesh.position : vis.center;
        go.transform.position = desiredRoot;
        if (mesh != null)
        {
            Transform parent = mesh.parent != null ? mesh.parent : go.transform;
            mesh.localPosition = parent.InverseTransformPoint(meshWorld);
        }
    }

    static void RefitBoxCollider(GameObject go, BoxCollider box)
    {
        Bounds? world = CollectRendererBounds(go);
        if (!world.HasValue)
            return;
        Bounds b = world.Value;
        Vector3 lossy = go.transform.lossyScale;
        box.center = go.transform.InverseTransformPoint(b.center);
        box.size = new Vector3(
            b.size.x / Mathf.Max(0.0001f, lossy.x),
            b.size.y / Mathf.Max(0.0001f, lossy.y),
            b.size.z / Mathf.Max(0.0001f, lossy.z));
    }

    static Transform FindMeshChild(Transform root)
    {
        Transform wrapper = root.Find("Wrapper");
        if (wrapper != null && wrapper.childCount > 0)
            return wrapper.GetChild(0);
        Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in rs)
        {
            if (r != null && r.transform != root)
                return r.transform;
        }
        return null;
    }

    static bool RunVerify(StringBuilder sb, GameObject workshopRoom, Dictionary<string, List<PropEntry>> snapshotBefore)
    {
        sb.AppendLine("--- verify instantiate vs workshop ---");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPrefabPath);
        if (prefab == null)
        {
            sb.AppendLine("FAIL missing " + RoomPrefabPath);
            return false;
        }

        GameObject spawned = Object.Instantiate(prefab);
        spawned.name = RoomName;
        spawned.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        bool ok = true;
        try
        {
            List<GameObject> workshopFurn = CollectFurniture(workshopRoom);
            List<GameObject> spawnedFurn = CollectFurniture(spawned);
            var spawnByName = new Dictionary<string, GameObject>();
            sb.AppendLine("workshopFurn=" + workshopFurn.Count + " spawnedFurn=" + spawnedFurn.Count);
            sb.AppendLine("spawn children=" + ChildNames(spawned.transform));
            foreach (GameObject go in spawnedFurn)
            {
                if (spawnByName.ContainsKey(go.name))
                {
                    sb.AppendLine("FAIL duplicate spawned name " + go.name);
                    ok = false;
                }
                else
                    spawnByName[go.name] = go;
            }

            foreach (GameObject w in workshopFurn)
            {
                if (!spawnByName.ContainsKey(w.name))
                {
                    sb.AppendLine("FAIL missing on spawn: " + w.name);
                    ok = false;
                    continue;
                }
                GameObject s = spawnByName[w.name];
                spawnByName.Remove(w.name);
                Bounds wb = RendererBounds(w);
                Bounds sbnds = RendererBounds(s);
                Vector3 dMin = sbnds.min - wb.min;
                Vector3 dMax = sbnds.max - wb.max;
                bool boundsOk = Mathf.Abs(dMin.x) <= BoundsEps && Mathf.Abs(dMin.y) <= BoundsEps
                    && Mathf.Abs(dMin.z) <= BoundsEps && Mathf.Abs(dMax.x) <= BoundsEps
                    && Mathf.Abs(dMax.y) <= BoundsEps && Mathf.Abs(dMax.z) <= BoundsEps;
                sb.AppendLine("  " + w.name + " bounds "
                    + (boundsOk ? "PASS" : "FAIL")
                    + " workshop x[" + F(wb.min.x) + "," + F(wb.max.x) + "] z[" + F(wb.min.z) + "," + F(wb.max.z) + "]"
                    + " spawn x[" + F(sbnds.min.x) + "," + F(sbnds.max.x) + "] z[" + F(sbnds.min.z) + "," + F(sbnds.max.z) + "]");
                if (!boundsOk)
                    ok = false;

                bool inside = sbnds.min.x >= InteriorMin - BoundsEps && sbnds.max.x <= InteriorMax + BoundsEps
                    && sbnds.min.z >= InteriorMin - BoundsEps && sbnds.max.z <= InteriorMax + BoundsEps;
                bool inFootprint = sbnds.min.x >= -BoundsEps && sbnds.max.x <= Ext + BoundsEps
                    && sbnds.min.z >= -BoundsEps && sbnds.max.z <= Ext + BoundsEps;
                sb.AppendLine("    interior=" + inside + " footprint=" + inFootprint);
                if (!inFootprint)
                {
                    sb.AppendLine("    FAIL outside room footprint");
                    ok = false;
                }

                Transform mesh = FindMeshChild(s.transform);
                if (mesh != null)
                {
                    float xz = new Vector2(mesh.localPosition.x, mesh.localPosition.z).magnitude;
                    sb.AppendLine("    mesh local=" + mesh.localPosition + " xz=" + xz.ToString("F3"));
                    if (xz > MeshLocalFail || Mathf.Abs(mesh.localPosition.z) > MeshLocalFail)
                    {
                        sb.AppendLine("    FAIL mesh child local offset still large");
                        ok = false;
                    }
                }

                List<PropEntry> after = Snapshot(s);
                List<PropEntry> before = snapshotBefore != null && snapshotBefore.ContainsKey(w.name)
                    ? snapshotBefore[w.name]
                    : Snapshot(w);
                string meshPath = mesh != null ? RelPath(s.transform, mesh) : "";
                List<string> diffs = DiffSnapshots(before, after, meshPath);
                if (diffs.Count == 0)
                    sb.AppendLine("    components PASS");
                else
                {
                    ok = false;
                    sb.AppendLine("    components FAIL");
                    foreach (string d in diffs)
                        sb.AppendLine("      " + d);
                }
            }

            foreach (var kv in spawnByName)
            {
                sb.AppendLine("FAIL extra on spawn: " + kv.Key);
                ok = false;
            }
        }
        finally
        {
            Object.DestroyImmediate(spawned);
        }
        return ok;
    }

    struct PropEntry
    {
        public string path;
        public string type;
        public string field;
        public string value;
    }

    struct Vector2Box
    {
        public Vector3 center;
        public Vector3 size;
    }

    static List<PropEntry> Snapshot(GameObject root)
    {
        var list = new List<PropEntry>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null)
                continue;
            string path = RelPath(root.transform, t);
            Component[] comps = t.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null)
                    continue;
                string type = c.GetType().FullName;
                var so = new SerializedObject(c);
                SerializedProperty p = so.GetIterator();
                while (p.Next(true))
                {
                    if (ShouldSkipProp(p))
                        continue;
                    if (p.propertyType == SerializedPropertyType.Generic
                        || p.propertyType == SerializedPropertyType.ArraySize)
                        continue;
                    list.Add(new PropEntry
                    {
                        path = path,
                        type = type,
                        field = p.propertyPath,
                        value = PropValue(p)
                    });
                }
            }
        }
        return list;
    }

    static bool ShouldSkipProp(SerializedProperty p)
    {
        string n = p.propertyPath;
        if (string.IsNullOrEmpty(n) || n == "m_Script")
            return true;
        if (n.StartsWith("m_CorrespondingSourceObject") || n.StartsWith("m_PrefabInstance")
            || n.StartsWith("m_PrefabAsset") || n.StartsWith("m_PrefabInternal"))
            return true;
        if (n == "m_GameObject" || n.StartsWith("m_GameObject."))
            return true;
        if (n.StartsWith("m_Father") || n.StartsWith("m_Children") || n.StartsWith("m_TransformParent")
            || n == "m_RootOrder" || n.Contains("m_FileID") || n.Contains("m_PathID"))
            return true;
        if (n == "m_ObjectHideFlags")
            return true;
        return false;
    }

    static string PropValue(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.intValue.ToString();
            case SerializedPropertyType.Boolean: return p.boolValue.ToString();
            case SerializedPropertyType.Float: return p.floatValue.ToString("G9");
            case SerializedPropertyType.String: return p.stringValue ?? "";
            case SerializedPropertyType.Enum: return p.enumValueIndex.ToString();
            case SerializedPropertyType.Vector2: return p.vector2Value.ToString("F5");
            case SerializedPropertyType.Vector3: return p.vector3Value.ToString("F5");
            case SerializedPropertyType.Vector4: return p.vector4Value.ToString("F5");
            case SerializedPropertyType.Quaternion: return p.quaternionValue.ToString("F5");
            case SerializedPropertyType.Color: return p.colorValue.ToString("F4");
            case SerializedPropertyType.LayerMask: return p.intValue.ToString();
            case SerializedPropertyType.ObjectReference:
                if (p.objectReferenceValue == null)
                    return "null";
                string ap = AssetDatabase.GetAssetPath(p.objectReferenceValue);
                return string.IsNullOrEmpty(ap) ? p.objectReferenceValue.name : ap;
            default:
                return p.propertyType.ToString();
        }
    }

    static bool IsAllowedDiff(string path, string type, string field, string meshPath)
    {
        bool isRootXform = path == "" && type == "UnityEngine.Transform";
        if (isRootXform && (field.StartsWith("m_LocalPosition") || field.StartsWith("m_LocalRotation")
            || field.StartsWith("m_LocalScale") || field.StartsWith("m_LocalEulerAnglesHint")))
            return true;
        bool isRootBox = path == "" && type == "UnityEngine.BoxCollider";
        if (isRootBox && (field.StartsWith("m_Size") || field.StartsWith("m_Center")))
            return true;
        if (!string.IsNullOrEmpty(meshPath) && path == meshPath && type == "UnityEngine.Transform"
            && field.StartsWith("m_LocalPosition"))
            return true;
        return false;
    }

    static List<string> DiffSnapshots(List<PropEntry> before, List<PropEntry> after, string meshPath)
    {
        var beforeMap = new Dictionary<string, string>();
        foreach (PropEntry e in before)
            beforeMap[e.path + "|" + e.type + "|" + e.field] = e.value;
        var afterMap = new Dictionary<string, string>();
        foreach (PropEntry e in after)
            afterMap[e.path + "|" + e.type + "|" + e.field] = e.value;

        var diffs = new List<string>();
        foreach (var kv in beforeMap)
        {
            string[] parts = SplitKey(kv.Key);
            if (IsHierarchyIdentityField(parts[2]))
                continue;
            if (afterMap.TryGetValue(kv.Key, out string av))
            {
                if (av != kv.Value && !IsAllowedDiff(parts[0], parts[1], parts[2], meshPath))
                    diffs.Add(parts[1] + " " + (parts[0] == "" ? "(root)" : parts[0]) + " " + parts[2]
                        + " before=" + kv.Value + " after=" + av);
                afterMap.Remove(kv.Key);
            }
            else if (!IsAllowedDiff(parts[0], parts[1], parts[2], meshPath))
                diffs.Add("missing after: " + parts[1] + " " + parts[2] + " on " + (parts[0] == "" ? "(root)" : parts[0]));
        }
        foreach (var kv in afterMap)
        {
            string[] parts = SplitKey(kv.Key);
            if (IsHierarchyIdentityField(parts[2]))
                continue;
            if (!IsAllowedDiff(parts[0], parts[1], parts[2], meshPath))
                diffs.Add("extra after: " + parts[1] + " " + parts[2] + " on " + (parts[0] == "" ? "(root)" : parts[0]));
        }
        return diffs;
    }

    static bool IsHierarchyIdentityField(string field)
    {
        return field.StartsWith("m_Father") || field.StartsWith("m_Children")
            || field.StartsWith("m_TransformParent") || field == "m_RootOrder"
            || field.Contains("m_FileID") || field.Contains("m_PathID");
    }

    static string[] SplitKey(string key)
    {
        int a = key.IndexOf('|');
        int b = key.LastIndexOf('|');
        return new[]
        {
            key.Substring(0, a),
            key.Substring(a + 1, b - a - 1),
            key.Substring(b + 1)
        };
    }

    static void OnPlayState(PlayModeStateChange c)
    {
        if (c == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PlayPendingKey, false))
        {
            if (sPlayLog == null)
            {
                sPlayLog = new StringBuilder();
                if (File.Exists(Full(PlayReport)))
                    sPlayLog.Append(File.ReadAllText(Full(PlayReport)));
            }
            sPlayWait = 0;
            sPlayLog.AppendLine("entered Play");
            WriteReportTo(PlayReport, sPlayLog.ToString());
            FocusGameView();
        }
        if (c == PlayModeStateChange.EnteredEditMode)
        {
            sPlayWait = 0;
            if (SessionState.GetBool(PlayPendingKey, false))
                FinishPlay(false, "FAIL play exited before self-test finished");
        }
    }

    static void TryStartPlay()
    {
        if (!File.Exists(Full(PlayFlag)))
            return;
        if (EditorApplication.isCompiling)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        try { File.Delete(Full(PlayFlag)); } catch { }
        DiscardDirtyPlayground(new StringBuilder());
        EditorSceneManager.OpenScene(Alpha01Path, OpenSceneMode.Single);
        sPlayLog = new StringBuilder();
        sPlayLog.AppendLine("=== Bake play self-test Alpha01 ===");
        sPlayLog.AppendLine("opened " + Alpha01Path);
        sPlayWait = 0;
        sPlayRoom = null;
        sPlayCorner = Vector3.zero;
        SessionState.SetBool(PlayPendingKey, true);
        WriteReportTo(PlayReport, sPlayLog.ToString());
        FocusGameView();
        EditorApplication.isPlaying = true;
    }

    static void TickPlay()
    {
        if (!EditorApplication.isPlaying)
            return;
        sPlayWait++;
        if (sPlayLog == null)
        {
            sPlayLog = new StringBuilder();
            if (File.Exists(Full(PlayReport)))
                sPlayLog.Append(File.ReadAllText(Full(PlayReport)));
        }

        if (sPlayWait == 1)
            FocusGameView();

        if (sPlayWait == 25)
        {
            try
            {
                PlaceRoomForPlay();
            }
            catch (System.Exception e)
            {
                FinishPlay(false, "FAIL play exception " + e);
            }
            return;
        }

        if (sPlayWait == 50)
        {
            try
            {
                InspectPlacedRoom();
            }
            catch (System.Exception e)
            {
                FinishPlay(false, "FAIL play exception " + e);
            }
            return;
        }

        if (sPlayWait > 180)
            FinishPlay(false, "FAIL play timeout wait=" + sPlayWait);
    }

    static void PlaceRoomForPlay()
    {
        RoomPlacement place = Object.FindAnyObjectByType<RoomPlacement>();
        GridSystem grid = GridSystem.Instance;
        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        if (place == null || grid == null || player == null)
        {
            FinishPlay(false, "FAIL missing RoomPlacement/GridSystem/Player");
            return;
        }
        sPlayLog.AppendLine("RoomPlacement prefab=" + (place.buildingPrefab != null
            ? AssetDatabase.GetAssetPath(place.buildingPrefab) : "NULL"));
        GridCoord anchor = RoomSlotGrid.SlotAnchorFromWorld(grid, player.transform.position);
        if (!RoomSlotGrid.IsSlotFree(grid, anchor))
            anchor = new GridCoord(anchor.x + RoomSlotGrid.SlotCells, anchor.z);
        bool placed = place.TryPlace(anchor);
        sPlayLog.AppendLine("TryPlace " + anchor + " ok=" + placed);
        if (!placed)
        {
            FinishPlay(false, "FAIL TryPlace");
            return;
        }
        Vector3 corner = grid.CellToWorldCorner(anchor);
        sPlayCorner = corner;
        GameObject room = FindSpawnedRoom(corner);
        if (room == null)
        {
            FinishPlay(false, "FAIL spawned room not found");
            return;
        }
        sPlayRoom = room;
        sPlayLog.AppendLine("spawned room=" + room.name + " root=" + room.transform.root.name
            + " children=" + ChildNames(room.transform));
        player.transform.position = corner + new Vector3(3f, 0f, 2.2f);
        sPlayLog.AppendLine("moved player into room for camera");
    }

    static void InspectPlacedRoom()
    {
        GameObject room = sPlayRoom;
        Vector3 corner = sPlayCorner;
        if (room == null)
        {
            FinishPlay(false, "FAIL spawned room lost before inspect");
            return;
        }
        GameObject printer = FindNamedChild(room, "Cream_Cushioned");
        GameObject cooler = FindNamedChild(room, "Water_Coole");
        if (printer == null)
            printer = FindNamedChild(room, "printer");
        sPlayLog.AppendLine("printer=" + (printer != null ? printer.name : "MISSING"));
        sPlayLog.AppendLine("cooler=" + (cooler != null ? cooler.name : "MISSING"));
        bool pass = printer != null && cooler != null;
        if (printer != null)
        {
            Bounds b = RendererBounds(printer);
            float localMaxZ = b.max.z - corner.z;
            float localMinZ = b.min.z - corner.z;
            float localMinX = b.min.x - corner.x;
            float localMaxX = b.max.x - corner.x;
            sPlayLog.AppendLine("printer world AABB x[" + F(b.min.x) + "," + F(b.max.x)
                + "] z[" + F(b.min.z) + "," + F(b.max.z) + "]");
            sPlayLog.AppendLine("printer room-local z[" + F(localMinZ) + "," + F(localMaxZ)
                + "] x[" + F(localMinX) + "," + F(localMaxX) + "]");
            bool inside = localMinX >= -BoundsEps && localMaxX <= Ext + BoundsEps
                && localMinZ >= -BoundsEps && localMaxZ <= Ext + BoundsEps;
            sPlayLog.AppendLine("printer inside back wall / interior=" + inside);
            if (!inside)
                pass = false;
        }
        if (cooler != null)
        {
            Bounds b = RendererBounds(cooler);
            float lz = b.center.z - corner.z;
            float lx = b.center.x - corner.x;
            sPlayLog.AppendLine("cooler room-local center x=" + F(lx) + " z=" + F(lz));
            sPlayLog.AppendLine("cooler world AABB x[" + F(b.min.x) + "," + F(b.max.x)
                + "] z[" + F(b.min.z) + "," + F(b.max.z) + "]");
            bool inside = (b.min.x - corner.x) >= -BoundsEps
                && (b.max.x - corner.x) <= Ext + BoundsEps
                && (b.min.z - corner.z) >= -BoundsEps
                && (b.max.z - corner.z) <= Ext + BoundsEps;
            sPlayLog.AppendLine("cooler interior=" + inside);
            if (!inside)
                pass = false;
        }
        try
        {
            CapturePlayShot("play");
        }
        catch (System.Exception e)
        {
            sPlayLog.AppendLine("shot skip " + e.GetType().Name);
        }
        FinishPlay(pass, pass ? "saw printer inside back-wall footprint, cooler in unchanged region" : "visual FAIL");
    }

    static GameObject FindSpawnedRoom(Vector3 corner)
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            if (t == null || t.parent == null)
                continue;
            if (t.name != RoomName && !t.name.StartsWith(RoomName))
                continue;
            if (Vector3.Distance(t.position, corner) < 0.2f)
                return t.gameObject;
        }
        foreach (var vis in Object.FindObjectsByType<BuildingInteriorVisibility>(FindObjectsInactive.Exclude))
        {
            Transform root = vis.transform.root;
            if (Vector3.Distance(root.position, corner) < 0.2f)
                return root.gameObject;
        }
        return null;
    }

    static GameObject FindNamedChild(GameObject root, string token)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != null && t.name.Contains(token))
                return t.gameObject;
        return null;
    }

    static void FinishPlay(bool ok, string note)
    {
        if (sPlayLog == null)
            sPlayLog = new StringBuilder();
        sPlayLog.AppendLine(note);
        sPlayLog.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        WriteReportTo(PlayReport, sPlayLog.ToString());
        SessionState.SetBool(PlayPendingKey, false);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
        Debug.Log("[RoomsWorkshop_BakeRoomPlay]\n" + sPlayLog);
    }

    static void FocusGameView()
    {
        System.Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return;
        EditorWindow.GetWindow(gvType, false, "Game", true).Focus();
    }

    static void CapturePlayShot(string name)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            sPlayLog.AppendLine("FAIL capture no Camera.main");
            return;
        }
        Vector2 gv = Handles.GetMainGameViewSize();
        int w = Mathf.Max(16, Mathf.RoundToInt(gv.x));
        int h = Mathf.Max(16, Mathf.RoundToInt(gv.y));
        if (h < 200)
        {
            w = 1280;
            h = 720;
        }
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        cam.targetTexture = prev;
        Object.DestroyImmediate(rt);
        string path = Full("Temp/bake_workshop_room_" + name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        sPlayLog.AppendLine("shot " + path + " " + tex.width + "x" + tex.height);
        Object.DestroyImmediate(tex);
    }

    static Bounds RendererBounds(GameObject go)
    {
        Bounds? b = CollectRendererBounds(go);
        return b ?? new Bounds(go.transform.position, Vector3.zero);
    }

    static Bounds? CollectRendererBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds();
        bool any = false;
        foreach (Renderer r in rs)
        {
            if (r == null || !r.enabled)
                continue;
            if (!any)
            {
                b = r.bounds;
                any = true;
            }
            else
                b.Encapsulate(r.bounds);
        }
        return any ? b : (Bounds?)null;
    }

    static string RelPath(Transform root, Transform t)
    {
        if (t == root)
            return "";
        var parts = new List<string>();
        Transform cur = t;
        while (cur != null && cur != root)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    static string LocalPose(Transform t)
    {
        return "pos=" + t.localPosition + " rotY=" + t.localEulerAngles.y.ToString("F1");
    }

    static string ChildNames(Transform t)
    {
        var names = new List<string>();
        for (int i = 0; i < t.childCount; i++)
            names.Add(t.GetChild(i).name);
        return names.Count == 0 ? "(none)" : string.Join(", ", names);
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root != null && root.name == name)
                return root;
        return null;
    }

    static void DiscardDirtyPlayground(StringBuilder sb)
    {
        Scene current = SceneManager.GetActiveScene();
        if (current.path != PlaygroundPath || !current.isDirty)
            return;
        EditorSceneManager.OpenScene(PlaygroundPath, OpenSceneMode.Single);
        sb.AppendLine("discarded unsaved GridFoundation dirt (did not save playground)");
    }

    static void WriteReportTo(string rel, string report)
    {
        string full = Full(rel);
        string dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(full, report ?? "");
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }

    static string F(float v)
    {
        return v.ToString("F2");
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max) + "\n…";
    }
}
