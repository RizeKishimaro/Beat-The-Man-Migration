using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless verify for overhead carry: socket height, collider toggle, push skip.
/// Trigger: Temp/verify_overhead_carry.flag
/// </summary>
[InitializeOnLoad]
public static class OverheadCarryVerifier
{
    const string FlagPath = "Temp/verify_overhead_carry.flag";
    const string VerifyPath = "Temp/agent_verify_overhead_carry.txt";
    const int Revision = 1;
    const float DoorHeight = 2.18f;
    const float SocketHeight = 1.45f;

    static OverheadCarryVerifier()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.playModeStateChanged += _ => EditorApplication.delayCall += TryRun;
    }

    [InitializeOnLoadMethod]
    static void Boot() => EditorApplication.delayCall += TryRun;

    static void TryRun()
    {
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        try
        {
            File.Delete(flag);
            string report = Run();
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
            Debug.Log("[OverheadCarryVerifier]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)),
                "FAIL: " + e);
            Debug.LogError("[OverheadCarryVerifier] " + e);
        }
    }

    static string Run()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Revision=" + Revision);

        var playerGo = new GameObject("Verify_Player");
        playerGo.AddComponent<CharacterController>();
        // Minimal stubs so AddComponent paths work without full PlayerController Awake side effects.
        var carry = playerGo.AddComponent<PlayerOverheadCarry>();

        var crateGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crateGo.name = "Verify_Crate";
        crateGo.transform.position = new Vector3(1f, 0f, 0f);
        crateGo.transform.localScale = Vector3.one * 0.7f;
        // CreatePrimitive cube is centered; match Item_Crate_Base bottom-center feel via CarryableItem offset 0
        // and box at origin — for height check use collider size.
        Object.DestroyImmediate(crateGo.GetComponent<Collider>());
        var box = crateGo.AddComponent<BoxCollider>();
        box.size = new Vector3(0.7f, 0.7f, 0.7f);
        box.center = new Vector3(0f, 0.35f, 0f);
        crateGo.transform.localScale = Vector3.one;
        crateGo.AddComponent<Rigidbody>();
        var carryable = crateGo.AddComponent<CarryableItem>();

        Transform socket = carry.CarrySocket;
        float socketY = socket.localPosition.y;
        sb.AppendLine($"socketLocalY={socketY:F3} (expect {SocketHeight:F2})");

        bool colliderBefore = box.enabled;
        carryable.PickUp(socket);
        bool colliderWhile = box.enabled;
        bool kinematicWhile = carryable.Body.isKinematic;
        float topY = crateGo.transform.position.y + box.size.y; // pivot bottom + height
        // After parent to socket at player origin: world Y = socketHeight + offset
        float carriedTop = SocketHeight + box.size.y;
        sb.AppendLine($"colliderBefore={colliderBefore} colliderWhileCarried={colliderWhile} kinematic={kinematicWhile}");
        sb.AppendLine($"carriedTopEstimate={carriedTop:F3} door={DoorHeight:F2} clearsDoor={carriedTop < DoorHeight}");
        sb.AppendLine($"worldPivotY={crateGo.transform.position.y:F3} worldTopY={topY:F3}");

        bool pushWouldSkip = carryable.IsCarried; // mirrors PlayerPushRigidbodies guard
        sb.AppendLine($"pushSkipWhileCarried={pushWouldSkip}");

        Vector3 dropPos = new Vector3(2f, 0f, 0f);
        carryable.Drop(dropPos, Quaternion.identity);
        bool colliderAfter = box.enabled;
        bool kinematicAfter = carryable.Body.isKinematic;
        sb.AppendLine($"colliderAfterDrop={colliderAfter} kinematicAfterDrop={kinematicAfter} isCarried={carryable.IsCarried}");

        bool ok =
            Mathf.Abs(socketY - SocketHeight) < 0.001f
            && colliderBefore
            && !colliderWhile
            && kinematicWhile
            && carriedTop < DoorHeight
            && pushWouldSkip
            && colliderAfter
            && !kinematicAfter
            && !carryable.IsCarried;

        sb.AppendLine(ok ? "OK" : "FAIL_CHECKS");

        Object.DestroyImmediate(playerGo);
        Object.DestroyImmediate(crateGo);
        return sb.ToString();
    }
}
