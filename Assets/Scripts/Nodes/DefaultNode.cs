using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Master-layer NODE atom (ARCHITECTURE): static, no gravity, never carried.
/// Accepts compatible Objects onto a local Slot, fires OnObjectAccepted.
/// Independent overrides plug in via accept filters + OnObjectAccepted listeners
/// (Bin destroy, Counter place, Keep/storage, future Station recipes, etc.).
/// One Slot for now — multi-cell multi-slot Nodes can come later.
/// </summary>
[DisallowMultipleComponent]
public class DefaultNode : MonoBehaviour
{
    public const string SlotName = "Slot";

    [Tooltip("If true, accepts any CarryableItem. If false, name must match acceptedObjectNames.")]
    [SerializeField] bool acceptAnyObject = true;

    [Tooltip("Used when acceptAnyObject is false. Matches CarryableItem.gameObject.name.")]
    [SerializeField] string[] acceptedObjectNames;

    [Tooltip("Local landing point (like CarrySocket). Auto-created as child 'Slot' if null.")]
    [SerializeField] Transform slot;

    [SerializeField] bool registerGridOccupancy = true;

    [SerializeField] UnityEvent<CarryableItem> onObjectAccepted = new UnityEvent<CarryableItem>();
    [SerializeField] UnityEvent<CarryableItem> onObjectRemoved = new UnityEvent<CarryableItem>();

    CarryableItem occupiedBy;
    GridFootprint footprint;
    GridCoord occupiedAnchor;
    bool gridRegistered;

    public bool AcceptAnyObject => acceptAnyObject;
    public bool IsOccupied => occupiedBy != null;
    public bool IsFullyOccupied => IsOccupied;
    public CarryableItem Occupant => occupiedBy;
    public Transform Slot => EnsureSlot();
    public UnityEvent<CarryableItem> OnObjectAccepted => onObjectAccepted;
    public UnityEvent<CarryableItem> OnObjectRemoved => onObjectRemoved;

    void Awake()
    {
        EnsureSlot();
        footprint = GetComponent<GridFootprint>();
        if (footprint == null)
        {
            footprint = gameObject.AddComponent<GridFootprint>();
            footprint.type = AssetGridType.Node;
            footprint.sizeInCells = Vector2Int.one;
        }
        else if (footprint.type != AssetGridType.Node)
        {
            Debug.LogWarning(
                $"DefaultNode on '{name}': GridFootprint.type was {footprint.type}; expected Node.",
                this);
        }
    }

    void Start()
    {
        TryRegisterGrid();
    }

    void OnDestroy()
    {
        ReleaseGrid();
    }

    public bool CanAccept(CarryableItem item)
    {
        if (item == null)
            return false;
        if (IsOccupied)
            return false;
        if (acceptAnyObject)
            return true;
        if (acceptedObjectNames == null || acceptedObjectNames.Length == 0)
            return false;

        string n = item.gameObject.name;
        for (int i = 0; i < acceptedObjectNames.Length; i++)
        {
            string want = acceptedObjectNames[i];
            if (string.IsNullOrEmpty(want))
                continue;
            if (string.Equals(n, want, StringComparison.Ordinal)
                || n.StartsWith(want, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Places the carried item on Slot and fires OnObjectAccepted.
    /// Caller clears hand on success. Reject = wrong type or occupied.
    /// </summary>
    public bool TryAccept(CarryableItem item)
    {
        if (!CanAccept(item))
            return false;

        Transform landing = EnsureSlot();
        item.PlaceOnNode(landing);
        occupiedBy = item;
        onObjectAccepted.Invoke(item);

        if (occupiedBy == null)
            return true;
        if (occupiedBy.gameObject == null)
            occupiedBy = null;

        return true;
    }

    public void NotifyItemRemoved(CarryableItem item)
    {
        if (occupiedBy != item)
            return;
        occupiedBy = null;
        if (item != null)
            onObjectRemoved.Invoke(item);
    }

    public void NotifyConsumed(CarryableItem item)
    {
        if (occupiedBy == item || occupiedBy == null)
        {
            CarryableItem gone = occupiedBy ?? item;
            occupiedBy = null;
            if (gone != null)
                onObjectRemoved.Invoke(gone);
        }
    }

    Transform EnsureSlot()
    {
        if (slot != null)
            return slot;

        Transform existing = transform.Find(SlotName);
        if (existing != null)
        {
            slot = existing;
            return slot;
        }

        var go = new GameObject(SlotName);
        slot = go.transform;
        slot.SetParent(transform, false);
        slot.localPosition = new Vector3(0f, 0.5f, 0f);
        slot.localRotation = Quaternion.identity;
        slot.localScale = Vector3.one;
        return slot;
    }

    void TryRegisterGrid()
    {
        if (!registerGridOccupancy || gridRegistered)
            return;
        GridSystem grid = GridSystem.Instance;
        if (grid == null || footprint == null)
            return;

        occupiedAnchor = grid.WorldToCell(transform.position);
        if (grid.TryOccupy(grid.FootprintCells(occupiedAnchor, footprint.sizeInCells), this))
            gridRegistered = true;
        else
            Debug.LogWarning($"DefaultNode '{name}': grid cells occupied — footprint not registered.", this);
    }

    void ReleaseGrid()
    {
        if (!gridRegistered)
            return;
        GridSystem grid = GridSystem.Instance;
        if (grid != null && footprint != null)
            grid.Release(grid.FootprintCells(occupiedAnchor, footprint.sizeInCells));
        gridRegistered = false;
    }
}
