using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tickets 115/116: Action (Space / North) near a JumpOnSeatPoint while not
/// carrying jumps on. Optionally freezes the seatable's Rigidbody on press.
/// Action again or move input reverses jump-off and restores physics.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerJumpOnSeat : MonoBehaviour
{
    // Spawn≈(-0.58,0,-0.5) chair≈(1.5,0,-0.5) → ~2.08 m; keep radius above that.
    [SerializeField] float detectRadius = 2.5f;
    [SerializeField] float dismountMoveThreshold = 0.2f;

    PlayerOverheadCarry carry;
    ProceduralSeatJumpMotion jumpMotion;

    JumpOnSeatPoint mountedSeat;
    Vector3 returnPos;
    Quaternion returnRot;
    bool mounted;

    public bool IsJumping => jumpMotion != null && jumpMotion.IsPlaying;
    public bool IsMounted => mounted;
    public bool BlocksMovement => IsJumping || IsMounted;

    void Awake()
    {
        carry = GetComponent<PlayerOverheadCarry>();
        jumpMotion = GetComponent<ProceduralSeatJumpMotion>();
        if (jumpMotion == null)
            jumpMotion = gameObject.AddComponent<ProceduralSeatJumpMotion>();
    }

    void Update()
    {
        if (IsJumping)
            return;

        if (mounted)
        {
            if (WasActionPressed() || HasMoveInput())
                BeginJumpOff();
            return;
        }

        if (!WasActionPressed())
            return;
        if (carry != null && carry.IsCarrying)
        {
            Debug.Log("[PlayerJumpOnSeat] Space ignored — currently carrying");
            return;
        }

        JumpOnSeatPoint seatPoint = FindNearestSeat();
        if (seatPoint == null)
        {
            Debug.Log(
                $"[PlayerJumpOnSeat] Space — no JumpOnSeatPoint within {detectRadius:F1} m " +
                $"(player at {transform.position})");
            return;
        }

        BeginJumpOn(seatPoint);
    }

    void BeginJumpOn(JumpOnSeatPoint seatPoint)
    {
        // Freeze BEFORE the hop so CC contact can't shove a pushable seat away.
        seatPoint.FreezeStatic();

        returnPos = transform.position;
        returnRot = transform.rotation;
        mountedSeat = seatPoint;

        Transform seat = seatPoint.Seat;
        Vector3 endPos = seat.TransformPoint(seatPoint.SitOffset);
        Quaternion endRot = Quaternion.Euler(0f, seat.eulerAngles.y, 0f);

        // Keep CharacterController off while seated — re-enabling it was
        // snapping us back up onto the seat collider and eating sitOffset.
        jumpMotion.TryPlay(endPos, endRot, enableControllerOnComplete: false, () =>
        {
            mounted = true;
            transform.SetPositionAndRotation(endPos, endRot);
        });
    }

    void BeginJumpOff()
    {
        JumpOnSeatPoint seatPoint = mountedSeat;
        if (seatPoint == null)
        {
            mounted = false;
            return;
        }

        mounted = false;
        mountedSeat = null;

        // Reverse hop back to pre-mount pose, then restore walk + seat physics.
        jumpMotion.TryPlay(returnPos, returnRot, enableControllerOnComplete: true, () =>
        {
            seatPoint.RestoreDynamic();
        });
    }

    bool HasMoveInput()
    {
        Vector2 input = ReadMoveInput();
        return input.sqrMagnitude >= dismountMoveThreshold * dismountMoveThreshold;
    }

    static Vector2 ReadMoveInput()
    {
        Vector2 input = Vector2.zero;
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
        }

        Gamepad pad = Gamepad.current;
        if (pad != null)
            input += pad.leftStick.ReadValue();

        if (input.sqrMagnitude > 1f)
            input.Normalize();
        return input;
    }

    JumpOnSeatPoint FindNearestSeat()
    {
        // Distance to seat roots — more reliable than OverlapSphere alone
        // (spawn can sit just outside collider reach at the old 1.6 m radius).
        JumpOnSeatPoint best = null;
        float bestDist = float.MaxValue;
        Vector3 origin = transform.position;
        float maxSqr = detectRadius * detectRadius;

        JumpOnSeatPoint[] seats = FindObjectsByType<JumpOnSeatPoint>(FindObjectsSortMode.None);
        for (int i = 0; i < seats.Length; i++)
        {
            JumpOnSeatPoint seatPoint = seats[i];
            if (seatPoint == null || !seatPoint.isActiveAndEnabled)
                continue;

            float d = (seatPoint.transform.position - origin).sqrMagnitude;
            if (d > maxSqr || d >= bestDist)
                continue;

            bestDist = d;
            best = seatPoint;
        }

        return best;
    }

    static bool WasActionPressed()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.spaceKey.wasPressedThisFrame)
            return true;

        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonNorth.wasPressedThisFrame)
            return true;

        return false;
    }
}
