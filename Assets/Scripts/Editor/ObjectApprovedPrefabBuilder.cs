using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 156 — on Approve, generate/update a real gameplay prefab from
/// ObjectProperties. On Unapprove, delete it. Co-located next to the mesh:
/// Assets/Objects/{Pickable|Nodes}/&lt;Name&gt;/&lt;Name&gt;.prefab
/// </summary>
public static class ObjectApprovedPrefabBuilder
{
    public const float DefaultMass = 1f;
    public const float LinearDamping = 2f;
    public const float AngularDamping = 2.4f;
    const string LogPrefix = "[ObjectApprovedPrefab]";

    public static string Build(ObjectProperties props)
    {
        if (props == null)
            throw new ArgumentNullException(nameof(props));

        string folder = ResolveFolder(props);
        if (string.IsNullOrEmpty(folder))
            throw new InvalidOperationException("ObjectProperties has no folder: " + props.name);

        string name = string.IsNullOrEmpty(props.objectName) ? props.name : props.objectName;
        string prefabPath = Normalize(folder + "/" + name + ".prefab");
        string fbxPath = ObjectPropertiesPreview.FindFbxFor(props);
        if (string.IsNullOrEmpty(fbxPath))
            throw new InvalidOperationException("No FBX next to " + name);

        GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbxAsset == null)
            throw new InvalidOperationException("Failed to load FBX: " + fbxPath);

        GameObject root = new GameObject(name);
        try
        {
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var wrapper = new GameObject("Wrapper");
            wrapper.transform.SetParent(root.transform, false);
            float scale = Mathf.Max(0.01f, props.scaleMultiplier);
            wrapper.transform.localScale = Vector3.one * scale;

            GameObject mesh = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            if (mesh == null)
                mesh = UnityEngine.Object.Instantiate(fbxAsset);
            mesh.name = "Mesh";
            mesh.transform.SetParent(wrapper.transform, false);
            mesh.transform.localPosition = props.pivotOffset;
            mesh.transform.localRotation = Quaternion.identity;
            mesh.transform.localScale = Vector3.one;

            if (props.kind == ObjectKind.Node)
                WireNode(root, props);
            else
                WirePickable(root, props);

            AttachStubAndProperties(root, folder, name, props);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                AssetDatabase.DeleteAsset(prefabPath);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool ok);
            if (!ok || saved == null)
                throw new InvalidOperationException("SaveAsPrefabAsset failed: " + prefabPath);

            props.prefabPath = prefabPath;
            EditorUtility.SetDirty(props);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + " built " + prefabPath);
            return prefabPath;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static void TearDown(ObjectProperties props)
    {
        if (props == null)
            return;

        string path = props.prefabPath;
        if (string.IsNullOrEmpty(path))
        {
            string folder = ResolveFolder(props);
            string name = string.IsNullOrEmpty(props.objectName) ? props.name : props.objectName;
            if (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(name))
                path = Normalize(folder + "/" + name + ".prefab");
        }

        if (!string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
            Debug.Log(LogPrefix + " deleted " + path);
        }

        props.prefabPath = "";
        EditorUtility.SetDirty(props);
        AssetDatabase.SaveAssets();
    }

    static void WirePickable(GameObject root, ObjectProperties props)
    {
        float mass = props.weight > 0.0001f ? props.weight : DefaultMass;
        if (props.weight <= 0.0001f)
            Debug.LogWarning(LogPrefix + " " + props.objectName
                + " weight unset — using default mass " + DefaultMass);

        bool useGravity = props.physicsGravity != ObjectPhysicsGravity.Off;

        FitBoxCollider(root);

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb == null)
            rb = root.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.linearDamping = LinearDamping;
        rb.angularDamping = AngularDamping;
        rb.useGravity = useGravity;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        PushableItem push = root.GetComponent<PushableItem>();
        if (push == null)
            push = root.AddComponent<PushableItem>();
        push.mass = mass;
        push.linearDamping = LinearDamping;
        push.angularDamping = AngularDamping;
        push.pushMultiplier = 1f;
        // ApplyDefaults sets mass/damping/kinematic; gravity already set on rb.
        push.ApplyDefaults();
        rb.useGravity = useGravity;
        rb.mass = mass;

        CarryableItem carry = root.GetComponent<CarryableItem>();
        if (props.pickupable)
        {
            if (carry == null)
                root.AddComponent<CarryableItem>();
        }
        else if (carry != null)
        {
            UnityEngine.Object.DestroyImmediate(carry);
        }

        GridFootprint fp = root.GetComponent<GridFootprint>();
        if (fp == null)
            fp = root.AddComponent<GridFootprint>();
        fp.type = AssetGridType.Object;
        fp.sizeInCells = Vector2Int.one;
    }

    static void WireNode(GameObject root, ObjectProperties props)
    {
        // Visual-only collider optional — nodes are static acceptors.
        FitBoxCollider(root);
        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box != null)
            box.isTrigger = true;

        GridFootprint fp = root.GetComponent<GridFootprint>();
        if (fp == null)
            fp = root.AddComponent<GridFootprint>();
        fp.type = AssetGridType.Node;
        fp.sizeInCells = Vector2Int.one;

        DefaultNode node = root.GetComponent<DefaultNode>();
        if (node == null)
            node = root.AddComponent<DefaultNode>();

        var so = new SerializedObject(node);
        so.FindProperty("acceptAnyObject").boolValue = true;
        so.FindProperty("acceptedObjectNames").arraySize = 0;
        so.FindProperty("registerGridOccupancy").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        Transform slot = root.transform.Find(DefaultNode.SlotName);
        if (slot == null)
        {
            var slotGo = new GameObject(DefaultNode.SlotName);
            slotGo.transform.SetParent(root.transform, false);
            float slotY = (props.nodeCompat & ObjectNodeCompat.Storage) != 0 ? 0f : 0.5f;
            slotGo.transform.localPosition = new Vector3(0f, slotY, 0f);
            slot = slotGo.transform;
        }
        so = new SerializedObject(node);
        so.FindProperty("slot").objectReferenceValue = slot;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Strip prior accept behaviours then add from nodeCompat.
        DestroyImmediateIfPresent<NodePlaceOnAccept>(root);
        DestroyImmediateIfPresent<NodeKeepOnAccept>(root);
        DestroyImmediateIfPresent<NodeDestroyOnAccept>(root);
        DestroyImmediateIfPresent<NodeStoreInsideOnAccept>(root);

        ObjectNodeCompat flags = props.nodeCompat;
        if ((flags & ObjectNodeCompat.Any) != 0 || flags == ObjectNodeCompat.None)
        {
            // accept-any DefaultNode only — no specialised Independent component.
        }
        else
        {
            if ((flags & ObjectNodeCompat.Counter) != 0)
                root.AddComponent<NodePlaceOnAccept>();
            if ((flags & ObjectNodeCompat.Keep) != 0)
                root.AddComponent<NodeKeepOnAccept>();
            if ((flags & ObjectNodeCompat.Bin) != 0)
                root.AddComponent<NodeDestroyOnAccept>();
            if ((flags & ObjectNodeCompat.Storage) != 0)
                root.AddComponent<NodeStoreInsideOnAccept>();
        }
    }

    static void AttachStubAndProperties(GameObject root, string folder, string name, ObjectProperties props)
    {
        string stubPath = Normalize(folder + "/" + name + ".cs");
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(stubPath);
        Type stubType = script != null ? script.GetClass() : null;
        if (stubType != null && typeof(MonoBehaviour).IsAssignableFrom(stubType)
            && root.GetComponent(stubType) == null)
        {
            root.AddComponent(stubType);
        }

        if (props.kind == ObjectKind.Node)
        {
            var imported = root.GetComponent<ImportedNode>();
            if (imported == null)
                imported = root.AddComponent<ImportedNode>();
            imported.properties = props;
        }
        else
        {
            var imported = root.GetComponent<ImportedPickable>();
            if (imported == null)
                imported = root.AddComponent<ImportedPickable>();
            imported.properties = props;
        }
    }

    static void FitBoxCollider(GameObject root)
    {
        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = root.AddComponent<BoxCollider>();

        Bounds? world = CollectRendererBounds(root);
        if (!world.HasValue)
        {
            box.size = Vector3.one;
            box.center = new Vector3(0f, 0.5f, 0f);
            return;
        }

        Bounds b = world.Value;
        Vector3 localCenter = root.transform.InverseTransformPoint(b.center);
        Vector3 lossy = root.transform.lossyScale;
        box.center = localCenter;
        box.size = new Vector3(
            b.size.x / Mathf.Max(0.0001f, lossy.x),
            b.size.y / Mathf.Max(0.0001f, lossy.y),
            b.size.z / Mathf.Max(0.0001f, lossy.z));
    }

    static Bounds? CollectRendererBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
            return null;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
        {
            if (rends[i] != null)
                b.Encapsulate(rends[i].bounds);
        }
        return b;
    }

    static void DestroyImmediateIfPresent<T>(GameObject root) where T : Component
    {
        T c = root.GetComponent<T>();
        if (c != null)
            UnityEngine.Object.DestroyImmediate(c);
    }

    public static string ResolveFolder(ObjectProperties props)
    {
        if (props.folder != null)
        {
            string p = AssetDatabase.GetAssetPath(props.folder);
            if (!string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p))
                return Normalize(p);
        }

        string soPath = AssetDatabase.GetAssetPath(props);
        if (string.IsNullOrEmpty(soPath))
            return null;
        return Normalize(Path.GetDirectoryName(soPath)?.Replace('\\', '/'));
    }

    static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        return path.Replace('\\', '/');
    }
}
