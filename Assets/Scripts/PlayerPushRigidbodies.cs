using UnityEngine;

/// <summary>
/// Shared CharacterController → Rigidbody shove for Object-type items.
/// Attach to Player (with CharacterController). Does not hardcode crate types.
/// Uses ForceMode.Force because OnControllerColliderHit fires every contact frame;
/// Impulse-per-frame would launch light objects.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerPushRigidbodies : MonoBehaviour
{
    [Tooltip("Continuous planar shove while CharacterController contacts a Rigidbody.")]
    [SerializeField] float pushForce = 28f;

    [Tooltip("Extra scale from PlanarSpeed / MoveSpeed (0 at standstill → +boost at full move).")]
    [SerializeField] float speedBoost = 0.75f;

    [Tooltip("Clamp planar force magnitude so sustained contact can't launch objects.")]
    [SerializeField] float maxPushForce = 48f;

    CharacterController controller;
    PlayerController player;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        player = GetComponent<PlayerController>();

        // Spike tuning if scene still has older serialized values.
        pushForce = 28f;
        speedBoost = 0.75f;
        maxPushForce = 48f;
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;
        if (body == null || body.isKinematic)
            return;

        CarryableItem carryable = hit.collider.GetComponentInParent<CarryableItem>();
        if (carryable != null && carryable.IsCarried)
            return;

        // Don't lift objects — planar shove only.
        Vector3 forceDir = hit.moveDirection;
        forceDir.y = 0f;
        if (forceDir.sqrMagnitude < 0.001f)
        {
            forceDir = transform.forward;
            forceDir.y = 0f;
        }
        if (forceDir.sqrMagnitude < 0.001f)
            return;
        forceDir.Normalize();

        float speedFactor = 1f;
        if (player != null && player.MoveSpeed > 0.01f)
            speedFactor = 1f + speedBoost * Mathf.Clamp01(player.PlanarSpeed / player.MoveSpeed);

        float itemMul = 1f;
        PushableItem pushable = hit.collider.GetComponentInParent<PushableItem>();
        if (pushable != null)
            itemMul = pushable.pushMultiplier;

        float force = Mathf.Min(pushForce * speedFactor * itemMul, maxPushForce);
        body.AddForce(forceDir * force, ForceMode.Force);
    }
}
