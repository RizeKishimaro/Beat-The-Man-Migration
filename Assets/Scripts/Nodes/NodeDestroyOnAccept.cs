using UnityEngine;

/// <summary>
/// Independent-layer Bin behavior: destroys accepted Objects and clears the Node slot.
/// </summary>
[RequireComponent(typeof(DefaultNode))]
public class NodeDestroyOnAccept : MonoBehaviour
{
    DefaultNode node;

    void Awake()
    {
        node = GetComponent<DefaultNode>();
        node.OnObjectAccepted.AddListener(OnAccepted);
    }

    void OnDestroy()
    {
        if (node != null)
            node.OnObjectAccepted.RemoveListener(OnAccepted);
    }

    void OnAccepted(CarryableItem item)
    {
        if (item == null)
            return;
        node.NotifyConsumed(item);
        Destroy(item.gameObject);
        Debug.Log($"[NodeDestroyOnAccept] {name} consumed {item.name}");
    }
}
