using UnityEngine;

/// <summary>
/// Independent-layer Keep/storage behavior: Object stays on the Slot.
/// Master accept/place already did the work — this marks the Node as a
/// "keep" variant for clarity and future storage-specific logic.
/// </summary>
[RequireComponent(typeof(DefaultNode))]
public class NodeKeepOnAccept : MonoBehaviour
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
        Debug.Log($"[NodeKeepOnAccept] {name} kept {item.name} on slot (storage)");
    }
}
