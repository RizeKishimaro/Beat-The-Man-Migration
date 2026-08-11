using UnityEngine;

/// <summary>
/// Outdoor: follow player at outdoor ortho size.
/// Ticket 122: while inside a BuildingInteriorVisibility volume, zoom in 25%
/// (ortho 6 → 4.5) and lock framing to that room's screen center.
/// Enter/exit eases camera position + zoom only (player movement untouched).
/// </summary>
[DefaultExecutionOrder(60)]
public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Vector3 offset = new Vector3(-8f, 7f, -8f);
    [SerializeField] float outdoorOrthographicSize = 6f;
    [Tooltip("25% zoom-in vs outdoor (6 * 0.75 = 4.5).")]
    [SerializeField] float interiorOrthographicSize = 4.5f;
    [Tooltip("Camera ease duration when entering/exiting a room (seconds).")]
    [SerializeField] float roomTransitionDuration = 0.3f;
    [Tooltip("Optional XZ nudge if the room still looks off-center.")]
    [SerializeField] Vector2 interiorFocusNudge;

    Camera cam;
    BuildingInteriorVisibility[] rooms;
    float roomsRefreshAt = -999f;

    float currentSize;
    float sizeVelocity;
    Vector3 posVelocity;

    void Awake()
    {
        cam = GetComponent<Camera>();
        currentSize = outdoorOrthographicSize;
        ApplyZoom(currentSize);
    }

    void LateUpdate()
    {
        BuildingInteriorVisibility inside = FindInsideRoom();

        Vector3 desiredPos;
        float desiredSize;

        if (inside != null)
        {
            desiredSize = interiorOrthographicSize;
            // Center the room's full screen bounds (walls have height — ground
            // focus alone leaves the room high on screen).
            desiredPos = CameraPosCenteringRoom(inside, desiredSize);
        }
        else
        {
            desiredSize = outdoorOrthographicSize;
            if (target == null)
                return;
            desiredPos = target.position + offset;
        }

        float smoothTime = Mathf.Max(0.0001f, roomTransitionDuration);
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPos,
            ref posVelocity,
            smoothTime);

        currentSize = Mathf.SmoothDamp(
            currentSize,
            desiredSize,
            ref sizeVelocity,
            smoothTime);
        ApplyZoom(currentSize);
    }

    BuildingInteriorVisibility FindInsideRoom()
    {
        if (rooms == null || Time.unscaledTime >= roomsRefreshAt)
        {
            rooms = FindObjectsByType<BuildingInteriorVisibility>(FindObjectsSortMode.None);
            roomsRefreshAt = Time.unscaledTime + 1f;
        }

        for (int i = 0; i < rooms.Length; i++)
        {
            BuildingInteriorVisibility room = rooms[i];
            if (room != null && room.IsInside)
                return room;
        }

        return null;
    }

    Vector3 RoomFocusPoint(BuildingInteriorVisibility room)
    {
        Vector3 focus;
        if (room.interiorVolume != null)
        {
            Vector3 c = room.interiorVolume.bounds.center;
            focus = new Vector3(c.x, 0f, c.z);
        }
        else
        {
            focus = room.transform.position;
            focus.y = 0f;
        }

        focus.x += interiorFocusNudge.x;
        focus.z += interiorFocusNudge.y;
        return focus;
    }

    Bounds RoomWorldBounds(BuildingInteriorVisibility room)
    {
        if (room.interiorVolume != null)
            return room.interiorVolume.bounds;

        return new Bounds(RoomFocusPoint(room), new Vector3(6f, 3f, 6f));
    }

    /// <summary>
    /// Camera pose that puts the room's screen-space AABB center at viewport
    /// (0.5, 0.5) — fixes vertical bias from wall height in iso view.
    /// </summary>
    Vector3 CameraPosCenteringRoom(BuildingInteriorVisibility room, float orthoSize)
    {
        Vector3 focus = RoomFocusPoint(room);
        Vector3 forward = transform.forward;
        Vector3 basePos = Mathf.Abs(forward.y) < 0.001f
            ? focus + offset
            : focus - forward * Mathf.Abs(offset.y / forward.y);

        Bounds bounds = RoomWorldBounds(room);
        // 8 corners of the room volume in viewport space under base pose.
        float minU = 1f, maxU = 0f, minV = 1f, maxV = 0f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = bounds.center + Vector3.Scale(
                bounds.extents,
                new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));
            Vector3 vp = WorldToViewport(basePos, orthoSize, corner);
            minU = Mathf.Min(minU, vp.x);
            maxU = Mathf.Max(maxU, vp.x);
            minV = Mathf.Min(minV, vp.y);
            maxV = Mathf.Max(maxV, vp.y);
        }

        float midU = (minU + maxU) * 0.5f;
        float midV = (minV + maxV) * 0.5f;

        // Ortho: viewport 0..1 maps to 2*size (vertical) and 2*size*aspect (horizontal).
        float aspect = cam != null ? cam.aspect : (Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f);
        Vector3 shift =
            transform.right * ((midU - 0.5f) * 2f * orthoSize * aspect) +
            transform.up * ((midV - 0.5f) * 2f * orthoSize);

        return basePos + shift;
    }

    Vector3 WorldToViewport(Vector3 camPos, float orthoSize, Vector3 world)
    {
        // Manual ortho projection matching Unity's WorldToViewportPoint for this pose.
        Vector3 local = Quaternion.Inverse(transform.rotation) * (world - camPos);
        float aspect = cam != null ? cam.aspect : (Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f);
        float u = local.x / (orthoSize * aspect * 2f) + 0.5f;
        float v = local.y / (orthoSize * 2f) + 0.5f;
        return new Vector3(u, v, local.z);
    }

    void ApplyZoom(float size)
    {
        if (cam == null)
            cam = GetComponent<Camera>();
        if (cam == null)
            return;

        cam.orthographic = true;
        cam.orthographicSize = size;
    }
}
