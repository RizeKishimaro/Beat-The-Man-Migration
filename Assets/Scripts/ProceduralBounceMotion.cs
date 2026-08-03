using UnityEngine;

/// <summary>
/// Overcooked-style procedural bounce: squash/stretch + vertical bob on Character_Root
/// while the player is moving. Throwaway spike — tune via Inspector.
/// </summary>
[DefaultExecutionOrder(0)]
public class ProceduralBounceMotion : MonoBehaviour
{
    // Was 6 → 4.8 → 4; another −10% so the pulse sits a bit calmer.
    [SerializeField] float bounceHz = 3.6f;
    [SerializeField] float bounceHeight = 0.08f;
    [SerializeField] float squashAmount = 0.18f;
    [SerializeField] float stretchAmount = 0.12f;
    [SerializeField] float moveSpeedThreshold = 0.05f;
    [SerializeField] float settleSpeed = 12f;

    PlayerController player;
    Vector3 restLocalPosition;
    Vector3 restLocalScale;
    float phase;
    float wave;
    bool isMoving;

    /// <summary>Footstep phase in radians — shared with head bounce.</summary>
    public float Phase => phase;

    /// <summary>Current sin(phase), signed −1..1 for alternating sway.</summary>
    public float Wave => wave;

    public bool IsMoving => isMoving;

    void Awake()
    {
        restLocalPosition = transform.localPosition;
        restLocalScale = transform.localScale;
        player = GetComponentInParent<PlayerController>();
        EnsureHeadBounce();
        EnsureHandCarry();
    }

    void EnsureHeadBounce()
    {
        Transform head = FindDeepChild(transform, "Character_Head");
        if (head == null)
            return;

        ProceduralHeadBounceMotion headBounce = head.GetComponent<ProceduralHeadBounceMotion>();
        if (headBounce == null)
            headBounce = head.gameObject.AddComponent<ProceduralHeadBounceMotion>();
        headBounce.Bind(this);
    }

    void EnsureHandCarry()
    {
        ProceduralHandCarryMotion hands = GetComponent<ProceduralHandCarryMotion>();
        if (hands == null)
            hands = gameObject.AddComponent<ProceduralHandCarryMotion>();
        // BindHands no-ops rest recapture while raised; safe to call once at setup.
        hands.BindHands();
    }

    void LateUpdate()
    {
        float speed = player != null ? player.PlanarSpeed : 0f;
        isMoving = speed > moveSpeedThreshold;

        if (isMoving)
        {
            // Movement-driven phase so bounce rate tracks how fast we're going.
            float speedFactor = Mathf.Clamp01(speed / Mathf.Max(player.MoveSpeed, 0.01f));
            phase += Time.deltaTime * bounceHz * Mathf.Lerp(0.75f, 1.25f, speedFactor) * Mathf.PI * 2f;

            // 0..1 footstep-like pulse (two peaks per cycle feel like steps).
            wave = Mathf.Sin(phase);
            float up = Mathf.Abs(wave);

            float yOffset = up * bounceHeight;
            // Compress near the ground (wave near 0), stretch at the peak (wave near ±1).
            float squash = (1f - up) * squashAmount;
            float stretch = up * stretchAmount;

            Vector3 scale = restLocalScale;
            scale.y *= 1f - squash + stretch;
            scale.x *= 1f + squash * 0.5f - stretch * 0.25f;
            scale.z *= 1f + squash * 0.5f - stretch * 0.25f;

            transform.localPosition = restLocalPosition + Vector3.up * yOffset;
            transform.localScale = scale;
        }
        else
        {
            phase = 0f;
            wave = 0f;
            transform.localPosition = Vector3.Lerp(
                transform.localPosition,
                restLocalPosition,
                1f - Mathf.Exp(-settleSpeed * Time.deltaTime));
            transform.localScale = Vector3.Lerp(
                transform.localScale,
                restLocalScale,
                1f - Mathf.Exp(-settleSpeed * Time.deltaTime));
        }
    }

    public void CaptureRestPose()
    {
        restLocalPosition = transform.localPosition;
        restLocalScale = transform.localScale;
    }

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
