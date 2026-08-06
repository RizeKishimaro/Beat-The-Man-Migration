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
    Vector3 restWorldScale = Vector3.one;

    public bool IsCarried => carried;
    public Rigidbody Body => body != null ? body : GetComponent<Rigidbody>();

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        CacheColliders();
        restWorldScale = transform.lossyScale;
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

        // Preserve current world size (e.g. Meshy chair at 1.5) across carry.
        restWorldScale = transform.lossyScale;

        transform.SetParent(socket, false);
        transform.localPosition = new Vector3(0f, socketLocalYOffset, 0f);
        transform.localRotation = Quaternion.identity;
        ApplyRestLocalScaleUnder(socket);

        carried = true;
        AssertCarryPose(socket);
    }

    public void Drop(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (!carried)
            return;

        transform.SetParent(null, true);
        transform.SetPositionAndRotation(worldPosition, worldRotation);
        transform.localScale = restWorldScale;

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

    /// <summary>
    /// Places a carried Object onto a Node Slot (kinematic, no gravity, parented).
    /// Does not change floor Drop() behavior. Leaves colliders enabled so the
    /// Object can be picked up again from the Node.
    /// </summary>
    public void PlaceOnNode(Transform slot)
    {
        if (!carried || slot == null)
            return;

        if (body == null)
            body = GetComponent<Rigidbody>();
        if (colliders == null || colliders.Length == 0)
            CacheColliders();

        transform.SetParent(slot, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        // Counteract parent Node scale so the Object keeps its rest world size.
        ApplyRestLocalScaleUnder(slot);

        // Zero velocity only while still non-kinematic (Unity warns otherwise).
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = true;
        body.interpolation = RigidbodyInterpolation.None;

        if (colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null)
                    continue;
                colliders[i].isTrigger = colliderWasTrigger[i];
                colliders[i].enabled = true;
            }
        }

        carried = false;
    }

    void ApplyRestLocalScaleUnder(Transform parent)
    {
        Vector3 ps = parent != null ? parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(
            Mathf.Approximately(ps.x, 0f) ? restWorldScale.x : restWorldScale.x / ps.x,
            Mathf.Approximately(ps.y, 0f) ? restWorldScale.y : restWorldScale.y / ps.y,
            Mathf.Approximately(ps.z, 0f) ? restWorldScale.z : restWorldScale.z / ps.z);
    }

    /// <summary>Used by Storage Nodes to hide/show mesh while the Object stays inside.</summary>
    public void SetRenderersVisible(bool visible)
    {
        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null)
                rends[i].enabled = visible;
        }
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
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            if (body.useGravity)
                body.useGravity = false;
            if (body.interpolation != RigidbodyInterpolation.None)
                body.interpolation = RigidbodyInterpolation.None;
        }
    }
}
