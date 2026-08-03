using UnityEngine;

/// <summary>
/// Procedural overhead-carry hand raise for static-mesh Character_HandL/R.
/// Layers on top of ProceduralBounceMotion: writes local pose from rest + raise
/// offset; walk bob/squash stays on Character_Root parent (no fighting).
/// </summary>
[DefaultExecutionOrder(10)]
public class ProceduralHandCarryMotion : MonoBehaviour
{
    [Header("Raise Height / Offset (local, added to rest)")]
    [Tooltip("Character_Root is Z-up (Blender). X≈0 (no inward). Y=slight forward (world -Z). Z=lift (world +Y).")]
    [SerializeField] Vector3 raiseLocalOffset = new Vector3(0f, 0.1f, 0.55f);

    [Header("Raise Euler (degrees)")]
    [Tooltip("Palm faces -local Z (= world down at rest). ~180° local X flips palm to world up. Small ±Z = slight inward cup.")]
    [SerializeField] Vector3 raiseLocalEuler = new Vector3(180f, 0f, 12f);

    [Header("Raise / Lower Duration (seconds)")]
    [SerializeField] float raiseDuration = 0.2f;
    [SerializeField] float lowerDuration = 0.2f;

    Transform handL;
    Transform handR;
    Vector3 restPosL;
    Vector3 restPosR;
    Quaternion restRotL;
    Quaternion restRotR;
    // Cached once per raise — never rebuild from Euler every frame (avoids 180° Slerp flip/spin).
    Quaternion raisedRotL;
    Quaternion raisedRotR;
    Vector3 raisedPosL;
    Vector3 raisedPosR;

    float weight;       // 0 = rest, 1 = raised
    float targetWeight;
    bool bound;
    bool targetsCached;

    public bool IsRaised => weight > 0.01f;
    public float Weight => weight;

    void Awake()
    {
        BindHands();
    }

    public void BindHands()
    {
        handL = FindDeepChild(transform, "Character_HandL");
        handR = FindDeepChild(transform, "Character_HandR");
        if (handL == null || handR == null)
        {
            Debug.LogWarning(
                "[ProceduralHandCarryMotion] Character_HandL/R not found under " + name);
            bound = false;
            return;
        }

        // Never recapture rest while raising/raised — that compounds relative Euler and spins.
        if (bound && (weight > 0.001f || targetWeight > 0.001f))
            return;

        CaptureRestPoseInternal();
        bound = true;
        targetsCached = false;
    }

    public void CaptureRestPose()
    {
        if (handL == null || handR == null)
            BindHands();
        if (!bound)
            return;
        if (weight > 0.001f || targetWeight > 0.001f)
            return;

        CaptureRestPoseInternal();
        targetsCached = false;
    }

    void CaptureRestPoseInternal()
    {
        restPosL = handL.localPosition;
        restPosR = handR.localPosition;
        restRotL = handL.localRotation;
        restRotR = handR.localRotation;
    }

    void CacheRaisedTargets()
    {
        Vector3 offsetL = new Vector3(-raiseLocalOffset.x, raiseLocalOffset.y, raiseLocalOffset.z);
        Vector3 offsetR = new Vector3(raiseLocalOffset.x, raiseLocalOffset.y, raiseLocalOffset.z);
        raisedPosL = restPosL + offsetL;
        raisedPosR = restPosR + offsetR;

        // Build raised rotation as stable quaternions (AngleAxis), not per-frame Euler rebuild.
        // Palm -local Z → +local Z is 180° about local X; small Z = inward cup.
        Quaternion palmFlip = Quaternion.AngleAxis(raiseLocalEuler.x, Vector3.right);
        Quaternion cupL = Quaternion.AngleAxis(raiseLocalEuler.z, Vector3.forward);
        Quaternion cupR = Quaternion.AngleAxis(-raiseLocalEuler.z, Vector3.forward);
        if (Mathf.Abs(raiseLocalEuler.y) > 0.01f)
        {
            Quaternion yaw = Quaternion.AngleAxis(raiseLocalEuler.y, Vector3.up);
            palmFlip = yaw * palmFlip;
        }

        raisedRotL = restRotL * palmFlip * cupL;
        raisedRotR = restRotR * palmFlip * cupR;

        // Same-hemisphere as rest so Slerp takes the short arc once (no 360° flip).
        if (Quaternion.Dot(restRotL, raisedRotL) < 0f)
            raisedRotL = Negate(raisedRotL);
        if (Quaternion.Dot(restRotR, raisedRotR) < 0f)
            raisedRotR = Negate(raisedRotR);

        targetsCached = true;
    }

    /// <summary>Called from PlayerOverheadCarry after a successful pickup.</summary>
    public void NotifyPickedUp()
    {
        if (!bound)
            BindHands();
        if (!bound)
            return;

        // Freeze rest + raised targets for this carry so LateUpdate only Slerps toward a fixed quat.
        if (weight < 0.001f && targetWeight < 0.001f)
            CaptureRestPoseInternal();
        CacheRaisedTargets();
        targetWeight = 1f;
    }

    /// <summary>Called from PlayerOverheadCarry when the item is put down.</summary>
    public void NotifyDropped()
    {
        targetWeight = 0f;
    }

    void LateUpdate()
    {
        if (!bound)
            return;

        if (!targetsCached)
            CacheRaisedTargets();

        // Already settled at target — snap hold, do not keep interpolating.
        if (Mathf.Abs(weight - targetWeight) < 0.0001f)
        {
            weight = targetWeight;
            ApplyPose(weight);
            return;
        }

        float duration = targetWeight > weight ? raiseDuration : lowerDuration;
        duration = Mathf.Max(duration, 0.0001f);
        weight = Mathf.MoveTowards(weight, targetWeight, Time.deltaTime / duration);
        ApplyPose(weight);
    }

    void ApplyPose(float w)
    {
        float t = w * w * (3f - 2f * w); // smoothstep on weight only (0→1 once, then holds)

        handL.localPosition = Vector3.Lerp(restPosL, raisedPosL, t);
        handR.localPosition = Vector3.Lerp(restPosR, raisedPosR, t);
        // Shortest-path quaternion Slerp between two fixed quats — never raw Euler lerp.
        handL.localRotation = ShortSlerp(restRotL, raisedRotL, t);
        handR.localRotation = ShortSlerp(restRotR, raisedRotR, t);
    }

    static Quaternion ShortSlerp(Quaternion a, Quaternion b, float t)
    {
        if (Quaternion.Dot(a, b) < 0f)
            b = Negate(b);
        return Quaternion.Slerp(a, b, Mathf.Clamp01(t));
    }

    static Quaternion Negate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, -q.w);

#if UNITY_EDITOR
    /// <summary>Editor/tests: snap to full raise and confirm local pose is stable under parent bob.</summary>
    public void EditorSnapRaised()
    {
        if (!bound)
            BindHands();
        CacheRaisedTargets();
        weight = 1f;
        targetWeight = 1f;
        ApplyPose(1f);
    }

    public void EditorSnapRest()
    {
        weight = 0f;
        targetWeight = 0f;
        if (bound)
            ApplyPose(0f);
    }

    /// <summary>Editor: simulate settled carry frames; rotation must not drift.</summary>
    public bool EditorRotationHoldsSteady(int frames = 30)
    {
        EditorSnapRaised();
        Quaternion a = handL.localRotation;
        Quaternion b = handR.localRotation;
        for (int i = 0; i < frames; i++)
        {
            // Mimic LateUpdate settled path.
            ApplyPose(1f);
            if (Quaternion.Angle(a, handL.localRotation) > 0.05f)
                return false;
            if (Quaternion.Angle(b, handR.localRotation) > 0.05f)
                return false;
        }
        return true;
    }

    public Vector3 EditorRaiseLocalOffset => raiseLocalOffset;
    public Vector3 EditorRaiseLocalEuler => raiseLocalEuler;
    public float EditorRaiseDuration => raiseDuration;
    public float EditorLowerDuration => lowerDuration;
#endif

    static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeepChild(parent.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }
}
