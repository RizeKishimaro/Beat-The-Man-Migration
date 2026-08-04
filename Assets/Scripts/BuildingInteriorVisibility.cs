using UnityEngine;

/// <summary>
/// Camera-relative Building interior visibility — single-mesh shader clip.
///
/// Outside: full walls + roof. Inside: cullable walls (Wall_Left, Wall_Right,
/// Wall_Front_Left, Wall_Front_Right, Wall_Front_Lintel) set _StripEnabled on
/// BeatTheMan/BuildingWallStrip so the shader discards pixels above ~0.3m.
/// Wall_Front_* pieces always strip while inside (front-facing rule);
/// Wall_Left/Wall_Right strip only when facing the camera, same rule as the
/// front pieces use for camera-facing. Wall_Back is always fully visible
/// (URP Lit, no MPB/strip) regardless of camera angle or inside/outside state.
/// Floor stays full; roof hides; doors are never touched.
///
/// Architecture: ONE mesh per wall. No *_SolidStrip cubes. No renderer disable
/// for strip behavior (lintels fully discard via the same height clip).
/// </summary>
[RequireComponent(typeof(Collider))]
[DefaultExecutionOrder(50)]
public class BuildingInteriorVisibility : MonoBehaviour
{
    static readonly int StripEnabledId = Shader.PropertyToID("_StripEnabled");
    static readonly int StripHeightId = Shader.PropertyToID("_StripHeight");
    static readonly int WallBaseYId = Shader.PropertyToID("_WallBaseY");

    [Header("Volumes")]
    public Collider interiorVolume;

    [Header("Classification")]
    [Tooltip("Wall_Back only — Building_Wall_Lit, never stripped, outside this system.")]
    public Renderer[] alwaysVisibleWalls;
    public Renderer[] cullableWalls;
    public Renderer[] roofRenderers;
    public Renderer[] floorRenderers;

    [Header("Strip")]
    [Tooltip("URP Lit material for Wall_Back (outside strip system).")]
    public Material wallLitMaterial;
    [Tooltip("BeatTheMan/BuildingWallStrip — strip OFF (_StripEnabled=0).")]
    public Material wallStripMaterial;
    [Tooltip("BeatTheMan/BuildingWallStrip — strip ON (_StripEnabled=1 baked).")]
    public Material wallStripOnMaterial;
    public float stripHeight = 0.3f;
    public float facingDotThreshold = 0.05f;

    Transform player;
    Camera cam;
    bool inside;
    float buildingBaseY;

    public bool IsInside => inside;
    public float BuildingBaseY => buildingBaseY;

    void Awake()
    {
        if (interiorVolume == null)
            interiorVolume = GetComponent<Collider>();
        ResolveMaterials();
        CacheBuildingBaseY();
        ApplyWallBaseYToSharedMats();
        DestroyOrphanSolidStrips();
        ApplyWallMaterials();
    }

    void Start()
    {
        PlayerController pc = FindAnyObjectByType<PlayerController>();
        if (pc != null)
            player = pc.transform;
        ResolveCamera();
        inside = false;
        ApplyVisibility();
    }

    void LateUpdate()
    {
        if (player == null || interiorVolume == null)
            return;
        if (cam == null)
            ResolveCamera();

        Vector3 sample = player.position + Vector3.up * 0.9f;
        inside = interiorVolume.bounds.Contains(sample)
                 || interiorVolume.bounds.Contains(player.position);
        ApplyVisibility();
    }

    void ResolveCamera()
    {
        cam = Camera.main;
        if (cam == null)
            cam = FindAnyObjectByType<Camera>();
    }

    void ResolveMaterials()
    {
#if UNITY_EDITOR
        if (wallStripMaterial == null)
            wallStripMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Buildings/Building_Wall.mat");
        if (wallStripOnMaterial == null)
            wallStripOnMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Buildings/Building_Wall_StripOn.mat");
        if (wallLitMaterial == null)
            wallLitMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/Buildings/Building_Wall_Lit.mat");
#endif
        if (wallStripMaterial == null && wallLitMaterial != null)
            wallStripMaterial = wallLitMaterial;
        if (wallStripOnMaterial == null && wallStripMaterial != null)
            wallStripOnMaterial = wallStripMaterial;
    }

    void ApplyWallBaseYToSharedMats()
    {
        if (wallStripMaterial != null)
        {
            wallStripMaterial.SetFloat(WallBaseYId, buildingBaseY);
            wallStripMaterial.SetFloat(StripHeightId, stripHeight);
            wallStripMaterial.SetFloat(StripEnabledId, 0f);
            wallStripMaterial.DisableKeyword("_STRIP_ON");
        }
        if (wallStripOnMaterial != null && wallStripOnMaterial != wallStripMaterial)
        {
            wallStripOnMaterial.SetFloat(WallBaseYId, buildingBaseY);
            wallStripOnMaterial.SetFloat(StripHeightId, stripHeight);
            wallStripOnMaterial.SetFloat(StripEnabledId, 1f);
            wallStripOnMaterial.EnableKeyword("_STRIP_ON");
        }
    }

    void CacheBuildingBaseY()
    {
        buildingBaseY = float.MaxValue;
        bool found = false;
        void Consider(Renderer[] list)
        {
            if (list == null)
                return;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] == null)
                    continue;
                float y = list[i].bounds.min.y;
                buildingBaseY = found ? Mathf.Min(buildingBaseY, y) : y;
                found = true;
            }
        }
        Consider(cullableWalls);
        Consider(alwaysVisibleWalls);
        if (!found && floorRenderers != null)
        {
            for (int i = 0; i < floorRenderers.Length; i++)
            {
                if (floorRenderers[i] == null)
                    continue;
                buildingBaseY = floorRenderers[i].bounds.min.y;
                found = true;
                break;
            }
        }
        if (!found)
            buildingBaseY = interiorVolume != null ? interiorVolume.bounds.min.y : 0f;
    }

    /// <summary>Remove any leaked *_SolidStrip cubes from the abandoned two-object approach.</summary>
    public void DestroyOrphanSolidStrips()
    {
        Transform root = transform.parent != null ? transform.parent : transform;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null || !child.name.EndsWith("_SolidStrip"))
                continue;
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || !t.name.EndsWith("_SolidStrip"))
                continue;
            if (Application.isPlaying)
                Destroy(t.gameObject);
            else
                DestroyImmediate(t.gameObject);
        }
    }

    void ApplyWallMaterials()
    {
        // Cullable walls only — never touch Wall_Back / alwaysVisibleWalls (Lit, no strip).
        Material wallMat = wallStripMaterial != null ? wallStripMaterial : wallLitMaterial;
        if (wallMat == null)
            return;
        AssignMat(cullableWalls, wallMat);
    }

    static void AssignMat(Renderer[] renderers, Material mat)
    {
        if (renderers == null)
            return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;
            renderers[i].sharedMaterial = mat;
            renderers[i].enabled = true;
        }
    }

    void ApplyVisibility()
    {
        // Floor always on. Roof toggles with inside (not a wall-strip concern).
        SetEnabled(floorRenderers, true);
        SetEnabled(roofRenderers, !inside);

        // alwaysVisibleWalls (Wall_Back, Wall_Left, Wall_Right) are never ApplyStripFlag'd.

        if (!inside)
        {
            ApplyStripFlags(cullableWalls, strip: false);
            return;
        }

        if (cullableWalls == null)
            return;

        Vector3 roomCenter = interiorVolume != null
            ? interiorVolume.bounds.center
            : transform.position;

        // Only the front wall pieces are cullable now — each strips independently
        // on its own front-facing / camera-facing rule. No corner pairing.
        int n = cullableWalls.Length;
        for (int i = 0; i < n; i++)
        {
            Renderer r = cullableWalls[i];
            if (r == null)
                continue;
            bool want = IsFrontWallPiece(r) || IsFacingCamera(r, roomCenter);
            ApplyStripFlag(r, want);
        }
    }

    void ApplyStripFlags(Renderer[] renderers, bool strip)
    {
        if (renderers == null)
            return;
        for (int i = 0; i < renderers.Length; i++)
            ApplyStripFlag(renderers[i], strip);
    }

    void ApplyStripFlag(Renderer r, bool strip)
    {
        if (r == null)
            return;

        // Never disable the wall renderer for strip behavior.
        r.enabled = true;

        // Clear any leftover MPB so baked material _StripEnabled is not overridden.
        r.SetPropertyBlock(null);

        Material offMat = wallStripMaterial != null ? wallStripMaterial : wallLitMaterial;
        Material onMat = wallStripOnMaterial != null ? wallStripOnMaterial : offMat;
        Material wallMat = strip ? onMat : offMat;
        if (wallMat != null)
            r.sharedMaterial = wallMat;
    }

    static bool IsFrontWallPiece(Renderer r)
    {
        return r != null && r.gameObject.name.StartsWith("Wall_Front");
    }

    bool IsFacingCamera(Renderer r, Vector3 roomCenter)
    {
        if (cam == null)
            return true;

        Vector3 outward = r.bounds.center - roomCenter;
        outward.y = 0f;
        if (outward.sqrMagnitude < 1e-6f)
            return true;
        outward.Normalize();

        Vector3 toCam = cam.transform.position - r.bounds.center;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 1e-6f)
            return true;
        toCam.Normalize();

        return Vector3.Dot(outward, toCam) > facingDotThreshold;
    }

    void SetEnabled(Renderer[] renderers, bool enabled)
    {
        if (renderers == null)
            return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = enabled;
        }
    }

    /// <summary>1 if strip clip is on for this renderer, else 0. -1 if null.</summary>
    public float DebugGetStripEnabled(Renderer r)
    {
        if (r == null || r.sharedMaterial == null)
            return -1f;
        if (wallStripOnMaterial != null && r.sharedMaterial == wallStripOnMaterial)
            return 1f;
        if (r.sharedMaterial.IsKeywordEnabled("_STRIP_ON"))
            return 1f;
        if (r.sharedMaterial.HasProperty(StripEnabledId))
            return r.sharedMaterial.GetFloat(StripEnabledId);
        return 0f;
    }

    public string DebugDumpStripState()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"buildingBaseY={buildingBaseY:F3} stripHeight={stripHeight:F3} inside={inside}");
        void Dump(string label, Renderer[] list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
            {
                Renderer r = list[i];
                if (r == null) continue;
                float se = DebugGetStripEnabled(r);
                string shader = r.sharedMaterial != null && r.sharedMaterial.shader != null
                    ? r.sharedMaterial.shader.name : "null";
                sb.AppendLine(
                    $"{label} {r.name} enabled={r.enabled} strip={se:F0} " +
                    $"mat={(r.sharedMaterial != null ? r.sharedMaterial.name : "null")} shader={shader} " +
                    $"boundsY={r.bounds.min.y:F2}..{r.bounds.max.y:F2}");
            }
        }
        Dump("cullable", cullableWalls);
        Dump("always", alwaysVisibleWalls);

        int solidStrips = 0;
        Transform root = transform.parent != null ? transform.parent : transform;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform t = root.GetChild(i);
            if (t != null && t.name.EndsWith("_SolidStrip"))
                solidStrips++;
        }
        sb.AppendLine($"solidStripChildren={solidStrips} (want 0)");
        return sb.ToString();
    }

    // Back-compat name used by older verify tools.
    public string DebugDumpStripMeshes() => DebugDumpStripState();

    public void ForceRefresh()
    {
        ResolveMaterials();
        CacheBuildingBaseY();
        ApplyWallBaseYToSharedMats();
        DestroyOrphanSolidStrips();
        ApplyWallMaterials();
        ApplyVisibility();
    }

#if UNITY_EDITOR
    public void EditorForceRefresh() => ForceRefresh();

    public void EditorSetInsideForTest(bool value)
    {
        inside = value;
        ApplyVisibility();
    }
#endif
}


