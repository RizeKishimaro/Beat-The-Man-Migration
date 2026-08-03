using UnityEngine;

/// <summary>
/// Saloon-style hinged door: player push opens it both ways, partial push =
/// partial open, spring settles shut with a short thunk. Kinematic sim on the
/// hinge (CharacterController-friendly — no Rigidbody required).
///
/// Open direction comes only from move velocity along the closed door normal:
/// leaf rotates into the half-space the player is moving toward (hinge fixed).
/// </summary>
public class ShopDoorLeaf : MonoBehaviour
{
    [Header("Limits")]
    [Tooltip("Max swing from closed, in degrees (both directions).")]
    public float maxAngle = 90f;

    [Tooltip("Cap on swing rate. High enough that a full-speed shove can hit ~maxAngle in the doorway.")]
    public float maxAngularSpeed = 300f;

    [Header("Push (direction = move; amount = speed)")]
    [Tooltip("How hard the door chases its open target while the player is pressing through.")]
    public float chaseStrength = 42f;

    [Tooltip("Walk opens this fraction of maxAngle; full MoveSpeed opens the full clamp.")]
    public float walkOpenFraction = 0.35f;

    [Header("Doorway volume (hinge-local, closed pose)")]
    public float doorwayWidth = 1.15f;
    public float doorwayDepth = 1.1f;
    public float doorwayHeight = 2.4f;

    [Header("Spring shut (momentum through closed)")]
    [Tooltip("Pull toward 0°. Higher = snappier return. Pair with settleDamping for overshoot.")]
    public float springStrength = 200f;

    [Tooltip("Settle damping. Too high (~critical) eases into 0 with no overshoot. Lower = more thunk past closed. zeta ≈ damping / (2*sqrt(spring)). Aim ~0.55–0.7 for one small overshoot.")]
    public float damping = 14f;

    [Tooltip("Lighter damping while being shoved so the door can reach full open.")]
    public float pushDamping = 6f;

    CharacterController player;
    PlayerController playerController;
    float closedYAngle;
    Vector3 closedForward;
    Vector3 closedRight;
    float angle;
    float angVel;
    bool wasPressing;

    void Awake()
    {
        // Force settle tuning even if the open scene still has old serialized
        // damping (~critical → ease-in, no overshoot) in memory.
        springStrength = 200f;
        damping = 14f;
        pushDamping = 6f;
    }

    void Start()
    {
        closedYAngle = transform.localEulerAngles.y;
        closedForward = transform.forward;
        closedRight = transform.right;

        Collider[] leafColliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < leafColliders.Length; i++)
        {
            if (leafColliders[i] != null)
                leafColliders[i].isTrigger = true;
        }

        playerController = FindAnyObjectByType<PlayerController>();
        if (playerController != null)
            player = playerController.GetComponent<CharacterController>();

        Debug.Log(
            $"[ShopDoorLeaf] settle spring={springStrength} damping={damping} " +
            $"(zeta≈{damping / (2f * Mathf.Sqrt(springStrength)):F2}) — expect one overshoot past closed.");
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        float torque = 0f;
        bool pressing = false;

        if (player != null && IsInDoorway(player.bounds.center))
        {
            Vector3 vel = GetApproachVelocity();
            float crossing = Vector3.Dot(vel, closedForward);
            float along = Vector3.Dot(player.bounds.center - transform.position, closedForward);

            // Pressing = moving through the door plane. Sign of crossing alone
            // chooses which way the leaf swings (into the travel half-space).
            if (Mathf.Abs(crossing) > 0.05f)
            {
                // Once the player has clearly crossed onto the destination side,
                // stop chasing open and let the spring thunk shut behind them
                // (otherwise chase holds it open until they leave the whole volume).
                bool pastDoor = Mathf.Sign(along) == Mathf.Sign(crossing) && Mathf.Abs(along) > 0.4f;
                if (!pastDoor)
                {
                    pressing = true;

                    float maxSpeed = playerController != null
                        ? Mathf.Max(playerController.MoveSpeed, 0.01f)
                        : 5.5f;
                    float speed01 = Mathf.Clamp01(Mathf.Abs(crossing) / maxSpeed);

                    float openAmount = Mathf.Lerp(walkOpenFraction, 1f, speed01) * maxAngle;
                    float openSign = -Mathf.Sign(crossing);
                    float target = openSign * openAmount;

                    torque += (target - angle) * chaseStrength;
                }
            }
        }

        if (!pressing)
        {
            // On release, seed momentum toward closed so the thunk isn't a
            // from-rest ease when chase left angVel near zero.
            if (wasPressing && Mathf.Abs(angle) > 8f && Mathf.Abs(angVel) < 40f)
                angVel = -Mathf.Sign(angle) * 120f;

            // Underdamped spring-damper (not SmoothDamp/Lerp): carries angular
            // momentum through 0° for one small overshoot, then settles.
            torque += -angle * springStrength;
            torque += -angVel * damping;
        }
        else
        {
            torque += -angVel * pushDamping;
        }

        wasPressing = pressing;

        angVel += torque * dt;
        angVel = Mathf.Clamp(angVel, -maxAngularSpeed, maxAngularSpeed);

        angle += angVel * dt;

        if (angle > maxAngle)
        {
            angle = maxAngle;
            if (angVel > 0f)
                angVel = 0f;
        }
        else if (angle < -maxAngle)
        {
            angle = -maxAngle;
            if (angVel < 0f)
                angVel = 0f;
        }

        // Snap only after the thunk has died — don't kill the overshoot early.
        if (!pressing && Mathf.Abs(angle) < 0.2f && Mathf.Abs(angVel) < 2f)
        {
            angle = 0f;
            angVel = 0f;
        }

        Vector3 euler = transform.localEulerAngles;
        euler.y = closedYAngle + angle;
        transform.localEulerAngles = euler;
    }

    bool IsInDoorway(Vector3 worldPoint)
    {
        Vector3 delta = worldPoint - transform.position;
        float x = Vector3.Dot(delta, closedRight);
        float y = Vector3.Dot(delta, Vector3.up);
        float z = Vector3.Dot(delta, closedForward);

        return x > -0.15f && x < doorwayWidth
            && y > 0f && y < doorwayHeight
            && z > -doorwayDepth && z < doorwayDepth;
    }

    Vector3 GetApproachVelocity()
    {
        Vector3 vel = player != null ? player.velocity : Vector3.zero;

        if (playerController != null && playerController.PlanarSpeed > vel.magnitude + 0.01f)
        {
            Vector3 flat = player.transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.001f)
                return flat.normalized * playerController.PlanarSpeed;
        }

        return vel;
    }
}
