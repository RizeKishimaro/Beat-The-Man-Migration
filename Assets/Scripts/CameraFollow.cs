using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(-8f, 7f, -8f);
    [SerializeField] private float orthographicSize = 6f;

    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();

        // Framing is driven by orthographic size on this camera — NOT by how far
        // away the follow offset places it. Prior zoom edits only changed offset,
        // so they never appeared to do anything while size stayed at 1.
        offset = new Vector3(-8f, 7f, -8f);
        orthographicSize = 6f;
        ApplyZoom();
    }

    private void LateUpdate()
    {
        if (cam != null && !Mathf.Approximately(cam.orthographicSize, orthographicSize))
            ApplyZoom();

        if (target == null) return;

        transform.position = target.position + offset;
    }

    void ApplyZoom()
    {
        if (cam == null)
            cam = GetComponent<Camera>();
        if (cam == null)
            return;

        cam.orthographic = true;
        cam.orthographicSize = orthographicSize;
    }
}
