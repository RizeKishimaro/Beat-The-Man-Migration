using UnityEngine;

/// <summary>
/// Independent-layer Storage: Object is kept inside the Node — shape hidden,
/// still occupied / pick-up-able. Master PlaceOnNode already parents to Slot;
/// this only toggles visibility.
/// </summary>
[RequireComponent(typeof(DefaultNode))]
public class NodeStoreInsideOnAccept : MonoBehaviour
{
    DefaultNode node;

    void Awake()
    {
        node = GetComponent<DefaultNode>();
        node.OnObjectAccepted.AddListener(OnAccepted);
        node.OnObjectRemoved.AddListener(OnRemoved);
    }

    void OnDestroy()
    {
        if (node == null)
            return;
        node.OnObjectAccepted.RemoveListener(OnAccepted);
        node.OnObjectRemoved.RemoveListener(OnRemoved);
    }

    void OnAccepted(CarryableItem item)
    {
        if (item == null)
            return;
        item.SetRenderersVisible(false);
        Debug.Log($"[NodeStoreInsideOnAccept] {name} stored {item.name} inside (hidden)");
    }

    void OnRemoved(CarryableItem item)
    {
        if (item == null)
            return;
        item.SetRenderersVisible(true);
    }
}
