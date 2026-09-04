using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 159 — 1×1m cell-fit for single-cell Object (Pickable) meshes.
/// Footprint = max(bounds.size.x, bounds.size.z) at imported scale, Y ignored
/// for the ratio. Uniform scale still applies to height via scaleMultiplier.
/// Does not touch the 152 FBX bake.
/// </summary>
public static class ObjectCellFitScaling
{
    /// <summary>Matches GridSystem.cellSize default (1m Unity/Blender cell).</summary>
    public const float CellSizeMeters = 1f;
    const float Epsilon = 0.0001f;

    public static float MultiplierFromFootprint(float maxXZ)
    {
        if (maxXZ <= Epsilon)
            return 1f;
        return Mathf.Max(0.01f, CellSizeMeters / maxXZ);
    }

    public static bool TryMeasureFootprintXZ(GameObject root, out float maxXZ)
    {
        maxXZ = 0f;
        if (root == null)
            return false;

        bool started = false;
        Bounds b = default;
        MeshRenderer[] mesh = root.GetComponentsInChildren<MeshRenderer>(true);
        if (mesh != null)
        {
            for (int i = 0; i < mesh.Length; i++)
                Encapsulate(ref b, ref started, mesh[i]);
        }
        SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skinned != null)
        {
            for (int i = 0; i < skinned.Length; i++)
                Encapsulate(ref b, ref started, skinned[i]);
        }
        if (!started)
            return false;

        maxXZ = Mathf.Max(b.size.x, b.size.z);
        return maxXZ > Epsilon;
    }

    public static bool TryComputeForFbx(string fbxPath, out float multiplier, out float maxXZ)
    {
        multiplier = 1f;
        maxXZ = 0f;
        if (string.IsNullOrEmpty(fbxPath))
            return false;

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (asset == null)
            return false;

        GameObject inst = Object.Instantiate(asset);
        inst.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            if (!TryMeasureFootprintXZ(inst, out maxXZ))
                return false;
            multiplier = MultiplierFromFootprint(maxXZ);
            return true;
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }
    }

    public static bool TryComputeFor(ObjectProperties props, out float multiplier, out float maxXZ)
    {
        multiplier = 1f;
        maxXZ = 0f;
        if (props == null)
            return false;
        return TryComputeForFbx(ObjectPropertiesPreview.FindFbxFor(props), out multiplier, out maxXZ);
    }

    static void Encapsulate(ref Bounds b, ref bool started, Renderer r)
    {
        if (r == null)
            return;
        if (!started)
        {
            b = r.bounds;
            started = true;
        }
        else
            b.Encapsulate(r.bounds);
    }
}
