using UnityEngine;

/// <summary>
/// Head layer for the Overcooked-style bounce: Y bob + side sway locked to
/// ProceduralBounceMotion's footstep phase. Subtle spring settle on stop.
/// </summary>
[DefaultExecutionOrder(10)]
public class ProceduralHeadBounceMotion : MonoBehaviour
{
    [SerializeField] float headBobHeight = 0.035f;
    [SerializeField] float swayAmount = 0.025f;
    [SerializeField] float swayTiltDeg = 4f;

    [Header("Stop spring (subtle overshoot)")]
    [Tooltip("Higher = snappier pull to rest.")]
    [SerializeField] float settleStiffness = 220f;
    [Tooltip("Higher = less bounce. Keep just under critical for a tiny overshoot.")]
    [SerializeField] float settleDamping = 24f;

    ProceduralBounceMotion rootBounce;
    Vector3 restLocalPosition;
    Quaternion restLocalRotation;

    Vector3 posVelocity;
    float tilt;
    float tiltVelocity;
    bool wasMoving;

    public void Bind(ProceduralBounceMotion bounce)
    {
        rootBounce = bounce;
    }

    void Awake()
    {
        restLocalPosition = transform.localPosition;
        restLocalRotation = transform.localRotation;
        if (rootBounce == null)
            rootBounce = GetComponentInParent<ProceduralBounceMotion>();
    }

    void LateUpdate()
    {
        if (rootBounce == null)
            return;

        bool moving = rootBounce.IsMoving;

        if (moving)
        {
            // Drop spring state immediately so rapid start/stop stays snappy.
            posVelocity = Vector3.zero;
            tiltVelocity = 0f;
            wasMoving = true;

            float wave = rootBounce.Wave;
            float up = Mathf.Abs(wave);

            Vector3 pos = restLocalPosition;
            pos.y += up * headBobHeight;
            pos.x += wave * swayAmount;

            tilt = wave * swayTiltDeg;
            Vector3 euler = restLocalRotation.eulerAngles;
            transform.localPosition = pos;
            transform.localRotation = Quaternion.Euler(euler.x, euler.y, euler.z + tilt);
        }
        else
        {
            // Underdamped spring from rest velocity overshoots past rest once.
            // Tiny toward-rest seed — enough for a cute tick, not a second hop.
            if (wasMoving)
            {
                wasMoving = false;
                posVelocity = (restLocalPosition - transform.localPosition) * 1.5f;
                tiltVelocity = -tilt * 1.5f;
            }

            SpringToRest(Time.deltaTime);
        }
    }

    void SpringToRest(float dt)
    {
        Vector3 pos = transform.localPosition;
        Vector3 posError = restLocalPosition - pos;
        posVelocity += posError * settleStiffness * dt;
        posVelocity *= Mathf.Exp(-settleDamping * dt);
        pos += posVelocity * dt;
        transform.localPosition = pos;

        float tiltError = 0f - tilt;
        tiltVelocity += tiltError * settleStiffness * dt;
        tiltVelocity *= Mathf.Exp(-settleDamping * dt);
        tilt += tiltVelocity * dt;

        Vector3 euler = restLocalRotation.eulerAngles;
        transform.localRotation = Quaternion.Euler(euler.x, euler.y, euler.z + tilt);

        Vector3 remain = restLocalPosition - transform.localPosition;
        if (remain.sqrMagnitude < 1e-8f
            && posVelocity.sqrMagnitude < 1e-8f
            && Mathf.Abs(tilt) < 0.01f
            && Mathf.Abs(tiltVelocity) < 0.01f)
        {
            transform.localPosition = restLocalPosition;
            transform.localRotation = restLocalRotation;
            posVelocity = Vector3.zero;
            tilt = 0f;
            tiltVelocity = 0f;
        }
    }
}
