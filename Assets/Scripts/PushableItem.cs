using UnityEngine;

/// <summary>
/// Object-type physics defaults for pickup-able items (cardboard-box feel).
/// Put on Item_* Prefabs so future Object Prefabs inherit the same behavior.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class PushableItem : MonoBehaviour
{
    [Tooltip("How strongly CharacterController hits shove this body.")]
    public float pushMultiplier = 1f;

    [Header("Default Rigidbody (applied in Awake)")]
    public float mass = 3.5f;
    public float linearDamping = 2.0f;
    public float angularDamping = 2.4f;

    Rigidbody body;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        ApplyDefaults();
    }

    public void ApplyDefaults()
    {
        // Never clobber carry state (was forcing isKinematic=false while held).
        CarryableItem carryable = GetComponent<CarryableItem>();
        if (carryable != null && carryable.IsCarried)
            return;

        if (body == null)
            body = GetComponent<Rigidbody>();
        if (body == null)
            return;

        body.mass = mass;
        body.linearDamping = linearDamping;
        body.angularDamping = angularDamping;
        body.useGravity = true;
        body.isKinematic = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public Rigidbody Body => body != null ? body : GetComponent<Rigidbody>();
}
