using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Replaces the CoffeeShop_final prefab instance in "For Cursor" with CafeRoom,
/// while keeping Player, Main Camera, lights, and town untouched.
/// </summary>
public static class CafeRoomSceneInstaller
{
    const string ScenePath = "Assets/For Cursor.unity";
    const string CafeRoomPath = "Assets/Art/Shop/CafeRoom.fbx";
    const string OldShopName = "CoffeeShop_final";

    [MenuItem("Tools/For Cursor/Replace CoffeeShop With CafeRoom")]
    public static void ReplaceShopInOpenOrSavedScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!ReplaceInActiveScene())
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[CafeRoom] Replaced CoffeeShop_final with CafeRoom. Player + Camera kept.");
    }

    /// <summary>Callable from Unity batchmode: -executeMethod CafeRoomSceneInstaller.ReplaceFromBatch</summary>
    public static void ReplaceFromBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!ReplaceInActiveScene())
        {
            Debug.LogError("[CafeRoom] Replace failed.");
            EditorApplication.Exit(1);
            return;
        }

        EditorSceneManager.SaveScene(scene);
        Debug.Log("[CafeRoom] Batch replace complete.");
        EditorApplication.Exit(0);
    }

    static bool ReplaceInActiveScene()
    {
        GameObject cafeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CafeRoomPath);
        if (cafeAsset == null)
        {
            Debug.LogError("[CafeRoom] Missing asset at " + CafeRoomPath + ". Wait for Unity to finish importing, then retry.");
            return false;
        }

        // Remove old shop prefab instance(s)
        var toDestroy = new List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == OldShopName || root.name.StartsWith(OldShopName))
                toDestroy.Add(root);
        }

        foreach (var go in toDestroy)
        {
            Debug.Log("[CafeRoom] Removing " + go.name);
            Object.DestroyImmediate(go);
        }

        // Also clear any leftover CafeRoom from a previous run
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == "CafeRoom" || root.name.StartsWith("CafeRoom"))
                Object.DestroyImmediate(root);
        }

        GameObject cafe = PrefabUtility.InstantiatePrefab(cafeAsset) as GameObject;
        if (cafe == null)
            cafe = Object.Instantiate(cafeAsset);
        cafe.name = "CafeRoom";
        cafe.transform.position = new Vector3(-0.58f, 0f, 4.73f); // same spot as old shop
        cafe.transform.rotation = Quaternion.identity;
        cafe.transform.localScale = Vector3.one;

        // Colliders on all meshes (in case importer didn't)
        foreach (var filter in cafe.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.GetComponent<MeshCollider>() == null && filter.sharedMesh != null)
            {
                var mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
            }
        }

        // Door hinge wiring
        Transform hinge = FindDeep(cafe.transform, "DoorHinge");
        if (hinge != null)
        {
            var leaf = hinge.GetComponent<ShopDoorLeaf>();
            if (leaf == null)
                leaf = hinge.gameObject.AddComponent<ShopDoorLeaf>();
            leaf.openAngle = 90f;
            leaf.openDirection = -1f; // opens outward for this model
            leaf.swingSpeed = 120f;
            leaf.contactPadding = 0.2f;
        }
        else
        {
            Debug.LogWarning("[CafeRoom] DoorHinge not found — door won't auto-swing until you add ShopDoorLeaf.");
        }

        // Interior visibility trigger
        SetupInteriorTrigger(cafe);

        // Place player just outside the front door
        var player = Object.FindFirstObjectByType<PlayerController>();
        if (player != null)
        {
            Vector3 doorFront = cafe.transform.position + new Vector3(0f, 0f, -4.2f);
            player.transform.position = doorFront + Vector3.up * 1.0f;
        }

        Selection.activeGameObject = cafe;
        return true;
    }

    static void SetupInteriorTrigger(GameObject cafe)
    {
        Transform existing = cafe.transform.Find("InteriorTrigger");
        GameObject triggerGo = existing != null ? existing.gameObject : new GameObject("InteriorTrigger");
        triggerGo.transform.SetParent(cafe.transform, false);
        // Room interior roughly 6x5 centered on cafe origin; old shop used local offset
        triggerGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);

        var box = triggerGo.GetComponent<BoxCollider>();
        if (box == null)
            box = triggerGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(5.5f, 2.8f, 4.5f);
        box.center = Vector3.zero;

        var vis = triggerGo.GetComponent<ShopWallVisibility>();
        if (vis == null)
            vis = triggerGo.AddComponent<ShopWallVisibility>();
        vis.interiorVolume = box;

        var wallRenderers = new List<Renderer>();
        foreach (var t in cafe.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("Wall_Front") || t.name == "Wall_Back" || t.name == "Wall_Left" || t.name == "Wall_Right")
            {
                var r = t.GetComponent<Renderer>();
                if (r != null)
                    wallRenderers.Add(r);
            }
        }

        // For isometric, hide only the front wall pieces so the camera can see in
        var frontOnly = new List<Renderer>();
        foreach (var r in wallRenderers)
        {
            if (r.name.StartsWith("Wall_Front"))
                frontOnly.Add(r);
        }
        vis.wallRenderers = frontOnly.Count > 0 ? frontOnly.ToArray() : wallRenderers.ToArray();
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, name);
            if (found != null)
                return found;
        }
        return null;
    }
}
