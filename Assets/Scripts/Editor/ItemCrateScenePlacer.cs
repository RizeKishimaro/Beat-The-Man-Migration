using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Places exactly one Item_Crate_Base Prefab outside CafeRoom (not the raw FBX).
/// </summary>
public static class ItemCrateScenePlacer
{
    const string PrefabPath = "Assets/Art/Items/Item_Crate_Base.prefab";
    const string ScenePath = "Assets/For Cursor.unity";
    static readonly Vector3 OutsideFloorPos = new Vector3(0.9f, 0f, -1.2f);

    [MenuItem("Tools/For Cursor/Place Item_Crate_Base Outside")]
    public static void Place()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t != null && t.name == "Item_Crate_Base")
                Object.DestroyImmediate(t.gameObject);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("[ItemCrateScenePlacer] Missing prefab: " + PrefabPath);
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "Item_Crate_Base";
        instance.transform.position = OutsideFloorPos;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        Undo.RegisterCreatedObjectUndo(instance, "Place Item_Crate_Base");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = instance;
        Debug.Log($"[ItemCrateScenePlacer] Placed single Prefab at {OutsideFloorPos}");
    }
}
