using UnityEngine;

/// <summary>
/// Generic seat reference for jump-on (tickets 115/116). Child "Seat" defines
/// landing pose and facing — same spirit as DefaultNode.Slot.
/// Drop on any seatable object (chair, open-top car, etc.).
/// Optionally freezes/restores a Rigidbody while the player is mounted.
/// </summary>
[DisallowMultipleComponent]
public class JumpOnSeatPoint : MonoBehaviour
{
    public const string SeatName = "Seat";

    [Tooltip("Landing pose for jump-on. Forward = correctly seated facing.")]
    [SerializeField] Transform seat;

    [Tooltip("Offset from Seat when landing: X=right, Y=up, Z=forward (Seat-local).")]
    [SerializeField] Vector3 sitOffset = new Vector3(0f, 0.05f, 0.1f);

    [Tooltip("Local position used only when auto-creating a missing Seat child.")]
    [SerializeField] Vector3 defaultSeatLocalPosition = new Vector3(0f, 0.38f, 0.05f);

    [Tooltip(
        "If true and a Rigidbody is present, freeze it kinematic while mounted " +
        "(pushable props). Turn OFF for seats that are already static/kinematic " +
        "(e.g. future vehicles).")]
    [SerializeField] bool freezeRigidbodyOnMount = true;

    Rigidbody body;
    bool frozen;
    bool savedKinematic;
    bool savedGravity;
    bool savedDetectCollisions;
    RigidbodyInterpolation savedInterpolation;
    CollisionDetectionMode savedCollisionMode;

    public Transform Seat => EnsureSeat();
    public Vector3 SitOffset => sitOffset;
    public bool FreezeRigidbodyOnMount => freezeRigidbodyOnMount;
    public bool IsFrozen => frozen;

    void Awake()
    {
        EnsureSeat();
        body = GetComponent<Rigidbody>();
    }

    Transform EnsureSeat()
    {
        if (seat != null)
            return seat;

        Transform existing = transform.Find(SeatName);
        if (existing != null)
        {
            seat = existing;
            return seat;
        }

        var go = new GameObject(SeatName);
        seat = go.transform;
        seat.SetParent(transform, false);
        seat.localPosition = defaultSeatLocalPosition;
        seat.localRotation = Quaternion.identity;
        seat.localScale = Vector3.one;
        return seat;
    }

    /// <summary>
    /// Optionally freeze Rigidbody for jump-on. No-ops when freeze is disabled
    /// or there is no Rigidbody.
    /// </summary>
    public void FreezeStatic()
    {
        if (!freezeRigidbodyOnMount)
            return;

        if (body == null)
            body = GetComponent<Rigidbody>();
        if (body == null || frozen)
            return;

        savedKinematic = body.isKinematic;
        savedGravity = body.useGravity;
        savedDetectCollisions = body.detectCollisions;
        savedInterpolation = body.interpolation;
        savedCollisionMode = body.collisionDetectionMode;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        // Keep collisions so the player can stand on it once landed.
        body.detectCollisions = true;

        frozen = true;
    }

    /// <summary>Restore Rigidbody state after jump-off (only if we froze it).</summary>
    public void RestoreDynamic()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (body == null || !frozen)
            return;

        body.detectCollisions = savedDetectCollisions;
        body.isKinematic = savedKinematic;
        body.useGravity = savedGravity;
        body.interpolation = savedInterpolation;
        body.collisionDetectionMode = savedCollisionMode;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        // Pushable props only — do not force dynamic defaults onto non-pushables.
        if (!body.isKinematic)
        {
            PushableItem pushable = GetComponent<PushableItem>();
            pushable?.ApplyDefaults();
        }

        frozen = false;
    }
}
