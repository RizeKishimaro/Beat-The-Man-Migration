using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Ensures exactly one healthy Item_Crate_Base prefab instance outside CafeRoom.
/// Never CreatePrimitive-fallback when the Prefab asset exists.
/// </summary>
public class ItemCrateWorldSpawn : MonoBehaviour
{
    const string PrefabPath = "Assets/Art/Items/Item_Crate_Base.prefab";
    const string FbxPath = "Assets/Art/Items/Item_Crate_Base.fbx";
    const string CrateName = "Item_Crate_Base";

    public static readonly Vector3 OutsideFloorPos = new Vector3(0.9f, 0f, -1.2f);

    [SerializeField] GameObject cratePrefab;

    void Awake()
    {
        EnsureOutsideCrate(cratePrefab);
    }

    public static void EnsureOutsideCrate(GameObject prefab = null)
    {
        // Always strip center-pivot junk / broken leftovers first.
        DestroyJunkCrates();

        var healthy = new System.Collections.Generic.List<GameObject>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t != null && t.name == CrateName && IsHealthyPrefabCrate(t.gameObject))
                healthy.Add(t.gameObject);
        }

        // Keep exactly one healthy Prefab crate — never spawn a duplicate.
        if (healthy.Count >= 1)
        {
            EnsureObjectPhysics(healthy[0]);
            for (int i = 1; i < healthy.Count; i++)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    Object.DestroyImmediate(healthy[i]);
                else
#endif
                    Object.Destroy(healthy[i]);
            }
            return;
        }

        if (prefab == null)
            prefab = LoadCratePrefab();

        if (prefab == null)
        {
            Debug.LogError(
                "[ItemCrateWorldSpawn] Item_Crate_Base.prefab missing — refusing CreatePrimitive fallback.");
            return;
        }

        GameObject instance = Object.Instantiate(prefab);
        instance.name = CrateName;
        instance.transform.SetPositionAndRotation(OutsideFloorPos, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        EnsureObjectPhysics(instance);
        instance.SetActive(true);
        Debug.Log($"[ItemCrateWorldSpawn] Placed single {CrateName} at {instance.transform.position}");
    }

    public static int CountValidPrefabCrates()
    {
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t != null && t.name == CrateName && IsHealthyPrefabCrate(t.gameObject))
                n++;
        }
        return n;
    }

    /// <summary>
    /// Prefab-linked (or runtime instance of Prefab) with bottom-center collider setup.
    /// Rejects CreatePrimitive-style center-pivot cubes (uniform scale ≠ 1 with mesh cube).
    /// </summary>
    public static bool IsHealthyPrefabCrate(GameObject go)
    {
        if (go == null || go.name != CrateName)
            return false;

        var box = go.GetComponent<BoxCollider>();
        var rb = go.GetComponent<Rigidbody>();
        var mesh = go.GetComponentInChildren<MeshFilter>();
        if (box == null || rb == null || mesh == null || mesh.sharedMesh == null)
            return false;

        // Fallback cubes use localScale ~0.7 on a unit primitive; Prefab keeps scale 1.
        Vector3 s = go.transform.localScale;
        if (Mathf.Abs(s.x - 1f) > 0.01f || Mathf.Abs(s.y - 1f) > 0.01f || Mathf.Abs(s.z - 1f) > 0.01f)
            return false;

        // Bottom-center collider: center.y ≈ half height.
        if (box.center.y < box.size.y * 0.25f)
            return false;

        return true;
    }

    public static void DestroyJunkCrates()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null || t.name != CrateName)
                continue;
            if (IsHealthyPrefabCrate(t.gameObject))
                continue;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(t.gameObject);
            else
#endif
                Object.Destroy(t.gameObject);
        }
    }

    static void EnsureObjectPhysics(GameObject instance)
    {
        CarryableItem carryable = instance.GetComponent<CarryableItem>();
        if (carryable != null && carryable.IsCarried)
            return;

        BoxCollider box = instance.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = instance.AddComponent<BoxCollider>();
            box.size = new Vector3(0.7f, 0.7f, 0.7f);
            box.center = new Vector3(0f, 0.35f, 0f);
        }

#if UNITY_EDITOR
        if (box.sharedMaterial == null)
        {
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(
                "Assets/Art/Items/Item_Cardboard.physicMaterial");
            if (mat != null)
                box.sharedMaterial = mat;
        }
#endif

        if (instance.GetComponent<Rigidbody>() == null)
            instance.AddComponent<Rigidbody>();

        PushableItem pushable = instance.GetComponent<PushableItem>();
        if (pushable == null)
            pushable = instance.AddComponent<PushableItem>();
        pushable.ApplyDefaults();

        if (instance.GetComponent<CarryableItem>() == null)
            instance.AddComponent<CarryableItem>();
    }

    static GameObject LoadCratePrefab()
    {
#if UNITY_EDITOR
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
            return prefab;
        Debug.LogWarning("[ItemCrateWorldSpawn] Prefab missing at " + PrefabPath);
        return null;
#else
        return Resources.Load<GameObject>("Items/Item_Crate_Base");
#endif
    }
}
