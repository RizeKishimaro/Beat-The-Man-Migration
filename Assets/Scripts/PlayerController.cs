using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    const string CharacterAssetPath = "Assets/Art/Characters/Character_Base.fbx";

    // Working visual: Player 0.2 × Character_Base 5 (net 1.0) → ~1.56u tall.
    // CharacterController must stay on an unscaled Player — Unity does not support
    // scaled controllers (that broke door passage). Bake the net onto the model.
    const float CharacterNetScale = 0.2f * 5f;

    [SerializeField] float moveSpeed = 5.5f;
    [SerializeField] float turnSpeed = 720f;
    [SerializeField] GameObject characterPrefab;
    [SerializeField] bool hidePlaceholderVisual = true;

    // CafeRoom sits near z=4.73 with its front door facing -Z.
    // Spawn clearly outside that entrance.
    static readonly Vector3 OutsideRoomSpawn = new Vector3(-0.58f, 0f, -0.5f);

    CharacterController controller;
    float planarSpeed;

    public float MoveSpeed => moveSpeed;
    public float PlanarSpeed => planarSpeed;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        // Apply spike tuning even if Unity still has an older scene in memory.
        moveSpeed = 5.5f;
        transform.position = OutsideRoomSpawn;
        transform.localScale = Vector3.one;
        ApplyCharacterControllerWorldSize();

        if (GetComponent<PlayerPushRigidbodies>() == null)
            gameObject.AddComponent<PlayerPushRigidbodies>();

        if (GetComponent<PlayerOverheadCarry>() == null)
            gameObject.AddComponent<PlayerOverheadCarry>();

        if (GetComponent<PlayerJumpOnSeat>() == null)
            gameObject.AddComponent<PlayerJumpOnSeat>();

        WorldFloorEnsure.Ensure();
        EnsureCharacterVisual();
        ItemCrateWorldSpawn.EnsureOutsideCrate();
    }

    void Update()
    {
        var jumpOn = GetComponent<PlayerJumpOnSeat>();
        if (jumpOn != null && jumpOn.BlocksMovement)
        {
            planarSpeed = 0f;
            return;
        }

        Vector2 input = ReadMoveInput();
        Vector3 move = new Vector3(input.x, 0f, input.y);
        if (move.sqrMagnitude > 1f)
            move.Normalize();

        Vector3 delta = move * moveSpeed * Time.deltaTime;
        controller.Move(delta);
        planarSpeed = move.magnitude * moveSpeed;

        if (move.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(move, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                turnSpeed * Time.deltaTime);
        }
    }

    void EnsureCharacterVisual()
    {
        if (hidePlaceholderVisual)
        {
            Transform placeholder = transform.Find("PlaceholderVisual");
            if (placeholder != null)
                placeholder.gameObject.SetActive(false);
        }

        // Scene/in-memory Player often already has a dragged-in Character_Base
        // (sometimes from the duplicate Assets/Characters copy) while Awake also
        // Instantiates another — wipe all character visuals, then spawn exactly one.
        DestroyExistingCharacterVisuals();

        if (characterPrefab == null)
            characterPrefab = LoadCharacterPrefabFallback();

        if (characterPrefab == null)
        {
            Debug.LogWarning("[PlayerController] No characterPrefab assigned and no Character_Root found.");
            return;
        }

        GameObject instance = Instantiate(characterPrefab, transform);
        instance.name = "Character_Base";
        instance.transform.localPosition = Vector3.zero;
        // Blender Z-up → Unity Y-up corrective tilt on the model root.
        instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        instance.transform.localScale = Vector3.one * CharacterNetScale;

        Transform root = FindDeepChild(instance.transform, "Character_Root");
        if (root == null)
        {
            root = instance.transform;
            Debug.LogWarning("[PlayerController] Character_Root not found — applying bounce to prefab root.");
        }

        if (root.GetComponent<ProceduralBounceMotion>() == null)
            root.gameObject.AddComponent<ProceduralBounceMotion>();
    }

    void ApplyCharacterControllerWorldSize()
    {
        // Match ~1.56u character height; radius fits the ~0.98u door opening.
        controller.height = 1.56f;
        controller.radius = 0.28f;
        controller.center = new Vector3(0f, 0.78f, 0f);
        controller.skinWidth = 0.08f;
        controller.stepOffset = 0.3f;
    }

    void DestroyExistingCharacterVisuals()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name == "PlaceholderVisual")
                continue;

            bool looksLikeCharacter =
                child.name == "Character_Base"
                || child.name.StartsWith("Character_Base")
                || FindDeepChild(child, "Character_Root") != null;

            if (!looksLikeCharacter)
                continue;

            DestroyImmediate(child.gameObject);
        }
    }

    static GameObject LoadCharacterPrefabFallback()
    {
#if UNITY_EDITOR
        // In-memory scenes often drop the serialized prefab ref when the
        // .unity file was edited externally — reload from disk in Play Mode.
        GameObject fromDisk = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetPath);
        if (fromDisk != null)
            return fromDisk;
#endif
        return Resources.Load<GameObject>("Characters/Character_Base");
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeepChild(parent.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    Vector2 ReadMoveInput()
    {
        Vector2 input = Vector2.zero;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.wKey.isPressed) input.y += 1f;
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();
            if (stick.sqrMagnitude > input.sqrMagnitude)
                input = stick;
        }

        return input;
    }
}

/// <summary>
/// Overhead carry for big/heavy Object items. Separate from future in-front carry.
/// E / gamepad South: pick up nearest CarryableItem or put down current.
/// </summary>
[RequireComponent(typeof(CharacterController))]
// compile-nudge
public class PlayerOverheadCarry : MonoBehaviour
{
    public const string SocketName = "CarrySocket";

    [Tooltip("Local Y of CarrySocket on Player root. 1.45 + 0.7 crate ≈ 2.15, under 2.18 door.")]
    [SerializeField] float socketHeight = 1.45f;

    [SerializeField] float pickupRadius = 1.6f;
    [SerializeField] float dropForward = 0.85f;
    [SerializeField] float nodeOfferRadius = 2.0f;

    Transform carrySocket;
    CarryableItem held;
    CharacterController controller;
    ProceduralHandCarryMotion handCarry;

    public CarryableItem Held => held;
    public bool IsCarrying => held != null;
    public Transform CarrySocket => carrySocket;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        socketHeight = 1.45f;
        EnsureSocket();
    }

    void Update()
    {
        if (!WasInteractPressed())
            return;

        if (held != null)
            PutDown();
        else
            TryPickUp();
    }

    void LateUpdate()
    {
        if (held == null)
            return;
        EnsureSocket();
        held.AssertCarryPose(carrySocket);
    }

    ProceduralHandCarryMotion ResolveHandCarry()
    {
        if (handCarry == null)
            handCarry = GetComponentInChildren<ProceduralHandCarryMotion>();
        return handCarry;
    }

    void EnsureSocket()
    {
        Transform existing = transform.Find(SocketName);
        if (existing != null)
            carrySocket = existing;
        else
        {
            var go = new GameObject(SocketName);
            carrySocket = go.transform;
            carrySocket.SetParent(transform, false);
        }

        carrySocket.localPosition = new Vector3(0f, socketHeight, 0f);
        carrySocket.localRotation = Quaternion.identity;
        carrySocket.localScale = Vector3.one;
    }

    void TryPickUp()
    {
        CarryableItem best = FindNearestCarryable();
        if (best == null)
            return;

        // If the Object is sitting on a Node slot, free that Node first.
        DefaultNode host = best.GetComponentInParent<DefaultNode>();
        host?.NotifyItemRemoved(best);

        best.PickUp(carrySocket);
        held = best;
        held.AssertCarryPose(carrySocket);
        ResolveHandCarry()?.NotifyPickedUp();
        Debug.Log(
            $"[PlayerOverheadCarry] Picked up {best.name}; " +
            $"socketY={socketHeight} top≈{socketHeight + EstimateHeight(best):F2} " +
            $"parent={best.transform.parent}");
    }

    void PutDown()
    {
        if (held == null)
            return;

        DefaultNode node = FindNearestNode();
        if (node != null)
        {
            if (node.TryAccept(held))
            {
                CarryableItem placed = held;
                held = null;
                ResolveHandCarry()?.NotifyDropped();
                Debug.Log($"[PlayerOverheadCarry] Placed {placed.name} on Node '{node.name}'");
                return;
            }

            // Reject-flow A: wrong type or occupied — keep in hand (no-op).
            Debug.Log(
                $"[PlayerOverheadCarry] Node '{node.name}' rejected {held.name} " +
                $"(occupied={node.IsOccupied}) — kept in hand");
            return;
        }

        CarryableItem item = held;
        held = null;

        Vector3 dropPos = ComputeDropPosition(item);
        Quaternion dropRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        item.Drop(dropPos, dropRot);
        ResolveHandCarry()?.NotifyDropped();
        Debug.Log($"[PlayerOverheadCarry] Dropped {item.name} at {dropPos}");
    }

    DefaultNode FindNearestNode()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position + Vector3.up * 0.5f,
            nodeOfferRadius,
            ~0,
            QueryTriggerInteraction.Collide);

        DefaultNode best = null;
        float bestDist = float.MaxValue;
        Vector3 origin = transform.position;

        for (int i = 0; i < hits.Length; i++)
        {
            DefaultNode node = hits[i].GetComponentInParent<DefaultNode>();
            if (node == null)
                continue;

            float d = (node.transform.position - origin).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = node;
            }
        }

        return best;
    }

    CarryableItem FindNearestCarryable()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position + Vector3.up * 0.5f,
            pickupRadius,
            ~0,
            QueryTriggerInteraction.Ignore);

        CarryableItem best = null;
        float bestDist = float.MaxValue;
        Vector3 origin = transform.position;

        for (int i = 0; i < hits.Length; i++)
        {
            CarryableItem item = hits[i].GetComponentInParent<CarryableItem>();
            if (item == null || item.IsCarried)
                continue;

            float d = (item.transform.position - origin).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = item;
            }
        }

        return best;
    }

    Vector3 ComputeDropPosition(CarryableItem item)
    {
        Vector3 planar = transform.position + transform.forward * dropForward;
        float y = transform.position.y;

        Vector3 rayOrigin = planar + Vector3.up * 2f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            y = hit.point.y;

        return new Vector3(planar.x, y, planar.z);
    }

    static float EstimateHeight(CarryableItem item)
    {
        var box = item.GetComponent<BoxCollider>();
        if (box != null)
            return box.size.y * item.transform.lossyScale.y;
        return 0.7f;
    }

    static bool WasInteractPressed()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
            return true;

        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonSouth.wasPressedThisFrame)
            return true;

        return false;
    }
}

