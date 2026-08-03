using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Ensures a static outdoor floor exists so Rigidbody objects (crates) have ground.
/// CafeRoom only includes RoomFloor inside the shop.
/// </summary>
public static class WorldFloorEnsure
{
    public const string FloorName = "WorldFloor";

    // Thin slab: top face sits at y=0 where Player / crates spawn.
    static readonly Vector3 FloorPos = new Vector3(0f, -0.1f, 2f);
    static readonly Vector3 FloorScale = new Vector3(40f, 0.2f, 40f);
    static readonly Color FloorColor = new Color(0.42f, 0.42f, 0.40f, 1f);

    public static void Ensure()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t != null && t.name == FloorName)
                return;
        }

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = FloorName;
        floor.transform.SetPositionAndRotation(FloorPos, Quaternion.identity);
        floor.transform.localScale = FloorScale;

        // Static ground — no Rigidbody.
        var rb = floor.GetComponent<Rigidbody>();
        if (rb != null)
            Object.Destroy(rb);

        var rend = floor.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = null;
#if UNITY_EDITOR
            mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Items/WorldFloor_Mat.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = FloorColor;
                AssetDatabase.CreateAsset(mat, "Assets/Art/Items/WorldFloor_Mat.mat");
                mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Items/WorldFloor_Mat.mat");
            }
#endif
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = FloorColor;
            }
            rend.sharedMaterial = mat;
        }

        floor.isStatic = true;
        Debug.Log($"[WorldFloorEnsure] Created {FloorName} at {FloorPos} scale={FloorScale}");
    }
}
