using UnityEngine;

/// <summary>
/// Ticket 172 — 117 strip on a shared party wall when the player is inside
/// either adjacent room. Does not change BuildingInteriorVisibility.
/// </summary>
[DefaultExecutionOrder(70)]
public class SharedWallVisibility : MonoBehaviour
{
    public BuildingInteriorVisibility roomA;
    public BuildingInteriorVisibility roomB;
    public Renderer[] wallPieces;
    public Renderer[] ceilingPieces;
    public float facingDotThreshold = 0.05f;

    Camera cam;

    void LateUpdate()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null)
                cam = FindAnyObjectByType<Camera>();
        }

        bool insideA = roomA != null && roomA.IsInside;
        bool insideB = roomB != null && roomB.IsInside;
        bool inside = insideA || insideB;

        bool showCeil = (roomA != null && roomA.showCeiling)
                        || (roomB != null && roomB.showCeiling);
        SetEnabled(ceilingPieces, showCeil && !inside);

        if (wallPieces == null)
            return;
        if (!inside)
        {
            for (int i = 0; i < wallPieces.Length; i++)
                ApplyStripFlag(wallPieces[i], false, roomA);
            return;
        }

        Vector3 centerA = VolumeCenter(roomA);
        Vector3 centerB = VolumeCenter(roomB);
        for (int i = 0; i < wallPieces.Length; i++)
        {
            Renderer r = wallPieces[i];
            bool strip = (insideA && IsFacingCamera(r, centerA))
                         || (insideB && IsFacingCamera(r, centerB));
            ApplyStripFlag(r, strip, roomA != null ? roomA : roomB);
        }
    }

    static Vector3 VolumeCenter(BuildingInteriorVisibility vis)
    {
        if (vis != null && vis.interiorVolume != null)
            return vis.interiorVolume.bounds.center;
        return vis != null ? vis.transform.position : Vector3.zero;
    }

    bool IsFacingCamera(Renderer r, Vector3 roomCenter)
    {
        if (r == null || cam == null)
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

    static void ApplyStripFlag(Renderer r, bool strip, BuildingInteriorVisibility vis)
    {
        if (r == null)
            return;
        r.enabled = true;
        r.SetPropertyBlock(null);
        Material offMat = vis != null ? vis.wallStripMaterial : null;
        Material onMat = vis != null && vis.wallStripOnMaterial != null
            ? vis.wallStripOnMaterial
            : offMat;
        Material wallMat = strip ? onMat : offMat;
        if (wallMat != null)
            r.sharedMaterial = wallMat;
    }

    static void SetEnabled(Renderer[] renderers, bool enabled)
    {
        if (renderers == null)
            return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = enabled;
        }
    }
}
