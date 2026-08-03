using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(-6.124f, 5f, -6.124f);

    private void LateUpdate()
    {
        if (target == null) return;

        transform.position = target.position + offset;
    }
}
