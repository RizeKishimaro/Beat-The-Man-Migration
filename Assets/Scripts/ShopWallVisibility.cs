using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Legacy scene component. At runtime it bootstraps BuildingInteriorVisibility
/// (shader height-clip strip) so older scene instances that still serialize this
/// type pick up the dollhouse cutaway without requiring a scene re-wire.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ShopWallVisibility : MonoBehaviour
{
    [Tooltip("Unused when BuildingInteriorVisibility takes over; kept for serialization.")]
    public Renderer[] wallRenderers;

    [Tooltip("Optional explicit interior volume. If left empty, uses this GameObject's own Collider.")]
    public Collider interiorVolume;

    private void Awake()
    {
        BuildingInteriorVisibility existing = GetComponent<BuildingInteriorVisibility>();
        if (existing != null)
        {
            enabled = false;
            return;
        }

        BuildingInteriorVisibility vis = gameObject.AddComponent<BuildingInteriorVisibility>();
        Collider volume = interiorVolume != null ? interiorVolume : GetComponent<Collider>();
        vis.interiorVolume = volume;
        vis.stripHeight = 0.3f;

        Transform cafe = transform.parent != null ? transform.parent : transform;
        var always = new List<Renderer>();
        var cullable = new List<Renderer>();
        var roofs = new List<Renderer>();
        var floors = new List<Renderer>();
        Renderer wallBack = null;

        foreach (Transform t in cafe.GetComponentsInChildren<Transform>(true))
        {
            Renderer r = t.GetComponent<Renderer>();
            if (r == null)
                continue;
            string n = t.name;
            if (n == "Wall_Back")
                wallBack = r;
            else if (n.StartsWith("Wall_Front") || n == "Wall_Left" || n == "Wall_Right")
                cullable.Add(r);
            else if (n == "RoomCeiling")
                roofs.Add(r);
            else if (n == "RoomFloor")
                floors.Add(r);
        }

        vis.alwaysVisibleWalls = always.ToArray(); // empty — Wall_Back outside strip system
        vis.cullableWalls = cullable.ToArray();
        vis.roofRenderers = roofs.ToArray();
        vis.floorRenderers = floors.ToArray();

#if UNITY_EDITOR
        if (vis.wallStripMaterial == null)
            vis.wallStripMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Buildings/Building_Wall.mat");
        if (vis.wallStripOnMaterial == null)
            vis.wallStripOnMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Buildings/Building_Wall_StripOn.mat");
        if (vis.wallLitMaterial == null)
            vis.wallLitMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Buildings/Building_Wall_Lit.mat");
#endif
        if (wallBack != null)
        {
            if (vis.wallLitMaterial != null)
                wallBack.sharedMaterial = vis.wallLitMaterial;
            wallBack.SetPropertyBlock(null);
        }

        vis.ForceRefresh();
        enabled = false;
        Debug.Log(
            "[ShopWallVisibility] Bootstrapped BuildingInteriorVisibility — " +
            $"always={always.Count} cullable={cullable.Count} roof={roofs.Count} floor={floors.Count}");
    }
}
