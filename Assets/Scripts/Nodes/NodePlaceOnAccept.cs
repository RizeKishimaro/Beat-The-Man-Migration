using UnityEngine;

/// <summary>
/// Independent-layer Counter behavior: Object placed on top as a static prop.
/// Master accept/place already did the work — this marks the Node as a
/// "place on top" variant for clarity and future counter-specific logic.
/// </summary>
[RequireComponent(typeof(DefaultNode))]
public class NodePlaceOnAccept : MonoBehaviour
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
        Debug.Log($"[NodePlaceOnAccept] {name} placed {item.name} on top (counter)");
    }
}
