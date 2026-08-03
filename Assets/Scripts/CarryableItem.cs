using UnityEngine;

/// <summary>
/// Marks an Object Prefab as overhead-carryable (big/heavy items).
/// Lives alongside PushableItem — not the in-front Overcooked carry path.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class CarryableItem : MonoBehaviour
{
    [Tooltip("Extra local Y on the carry socket (usually 0 with bottom-center pivot).")]
    public float socketLocalYOffset = 0f;

    Rigidbody body;
    Collider[] colliders;
    bool[] colliderWasEnabled;
    bool[] colliderWasTrigger;
    RigidbodyInterpolation savedInterpolation;
    bool carried;

    public bool IsCarried => carried;
    public Rigidbody Body => body != null ? body : GetComponent<Rigidbody>();

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        CacheColliders();
    }

    void CacheColliders()
    {
        colliders = GetComponentsInChildren<Collider>(true);
        colliderWasEnabled = new bool[colliders.Length];
        colliderWasTrigger = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
        {
            colliderWasEnabled[i] = colliders[i].enabled;
            colliderWasTrigger[i] = colliders[i].isTrigger;
        }
    }

    public void PickUp(Transform socket)
    {
        if (carried || socket == null)
            return;

        if (body == null)
            body = GetComponent<Rigidbody>();
        if (colliders == null || colliders.Length == 0)
            CacheColliders();

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;
            colliderWasEnabled[i] = colliders[i].enabled;
            colliderWasTrigger[i] = colliders[i].isTrigger;
            colliders[i].enabled = false;
        }

        savedInterpolation = body.interpolation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = false;
        body.interpolation = RigidbodyInterpolation.None;

        transform.SetParent(socket, false);
        transform.localPosition = new Vector3(0f, socketLocalYOffset, 0f);
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        carried = true;
        AssertCarryPose(socket);
    }

    public void Drop(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (!carried)
            return;

        transform.SetParent(null, true);
        transform.SetPositionAndRotation(worldPosition, worldRotation);

        if (body == null)
            body = GetComponent<Rigidbody>();

        body.detectCollisions = true;
        body.isKinematic = false;
        body.useGravity = true;
        body.interpolation = savedInterpolation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        if (colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null)
                    continue;
                colliders[i].isTrigger = colliderWasTrigger[i];
                colliders[i].enabled = colliderWasEnabled[i];
            }
        }

        carried = false;
    }

    /// <summary>Safety net: keep kinematic + socket local pose for the whole carry.</summary>
    public void AssertCarryPose(Transform socket)
    {
        if (!carried || socket == null)
            return;

        if (transform.parent != socket)
            transform.SetParent(socket, false);

        Vector3 want = new Vector3(0f, socketLocalYOffset, 0f);
        if (transform.localPosition != want)
            transform.localPosition = want;
        if (transform.localRotation != Quaternion.identity)
            transform.localRotation = Quaternion.identity;

        if (body == null)
            body = GetComponent<Rigidbody>();
        if (body != null)
        {
            if (!body.isKinematic)
                body.isKinematic = true;
            if (body.useGravity)
                body.useGravity = false;
            if (body.interpolation != RigidbodyInterpolation.None)
                body.interpolation = RigidbodyInterpolation.None;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
