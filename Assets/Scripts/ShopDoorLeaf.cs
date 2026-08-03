using UnityEngine;

/// <summary>
/// A hinged door leaf that swings open when the player touches it.
/// Rotates around its own local up axis — put this on the hinge pivot
/// (DoorHinge) or on a leaf whose origin is already at the hinge edge.
/// </summary>
public class ShopDoorLeaf : MonoBehaviour
{
    [Tooltip("How far the leaf swings open, in degrees.")]
    public float openAngle = 90f;

    [Tooltip("Positive swings one way around the hinge, negative the other. Flip if the door opens into the wall instead of away from it.")]
    public float openDirection = 1f;

    [Tooltip("How fast the leaf swings open/closed, in degrees per second.")]
    public float swingSpeed = 120f;

    [Tooltip("Extra padding added around the leaf's bounds when testing for player contact, in world units.")]
    public float contactPadding = 0.15f;

    private CharacterController player;
    private float closedYAngle;
    private float currentOffset;

    private void Start()
    {
        closedYAngle = transform.localEulerAngles.y;

        PlayerController pc = FindObjectOfType<PlayerController>();
        if (pc != null)
            player = pc.GetComponent<CharacterController>();
    }

    private void Update()
    {
        bool touching = IsPlayerTouching();
        float targetOffset = touching ? openAngle * Mathf.Sign(openDirection) : 0f;

        currentOffset = Mathf.MoveTowards(currentOffset, targetOffset, swingSpeed * Time.deltaTime);

        Vector3 euler = transform.localEulerAngles;
        euler.y = closedYAngle + currentOffset;
        transform.localEulerAngles = euler;
    }

    private bool IsPlayerTouching()
    {
        if (player == null)
            return false;

        if (!TryGetLeafBounds(out Bounds leafBounds))
            return false;

        leafBounds.Expand(contactPadding);
        return leafBounds.Intersects(player.bounds);
    }

    private bool TryGetLeafBounds(out Bounds bounds)
    {
        Renderer self = GetComponent<Renderer>();
        if (self != null)
        {
            bounds = self.bounds;
            return true;
        }

        Renderer[] childRenderers = GetComponentsInChildren<Renderer>();
        bool has = false;
        bounds = new Bounds(transform.position, Vector3.zero);
        for (int i = 0; i < childRenderers.Length; i++)
        {
            if (childRenderers[i] == null)
                continue;
            if (!has)
            {
                bounds = childRenderers[i].bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(childRenderers[i].bounds);
            }
        }

        return has;
    }
}
