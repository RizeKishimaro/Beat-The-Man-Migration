using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Verifies hand raise defaults + that raised local pose stays put when
/// Character_Root bobs (bounce layers via parent, no fighting).
/// Flag: Temp/verify_hand_carry.flag
/// </summary>
[InitializeOnLoad]
public static class HandCarryMotionVerifier
{
    const string FlagPath = "Temp/verify_hand_carry.flag";
    const string VerifyPath = "Temp/agent_verify_hand_carry.txt";

    static HandCarryMotionVerifier()
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
            EditorApplication.delayCall += TryRun;
            return;
        }

        try
        {
            File.Delete(flag);
            string report = Run();
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
            Debug.Log("[HandCarryMotionVerifier]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)),
                "FAIL: " + e);
            Debug.LogError("[HandCarryMotionVerifier] " + e);
        }
    }

    [MenuItem("Tools/For Cursor/Verify Hand Carry Motion")]
    public static void MenuVerify()
    {
        string report = Run();
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
        EditorUtility.DisplayDialog("Hand Carry Motion", report, "OK");
    }

    static string Run()
    {
        var sb = new System.Text.StringBuilder();

        var rootGo = new GameObject("Character_Root");
        var handL = new GameObject("Character_HandL").transform;
        var handR = new GameObject("Character_HandR").transform;
        handL.SetParent(rootGo.transform, false);
        handR.SetParent(rootGo.transform, false);
        handL.localPosition = new Vector3(-0.2f, 0.6f, 0.1f);
        handR.localPosition = new Vector3(0.2f, 0.6f, 0.1f);
        handL.localRotation = Quaternion.identity;
        handR.localRotation = Quaternion.identity;

        Vector3 restL = handL.localPosition;
        Vector3 restR = handR.localPosition;

        var hands = rootGo.AddComponent<ProceduralHandCarryMotion>();
        hands.BindHands();

        sb.AppendLine($"defaults offset={hands.EditorRaiseLocalOffset} euler={hands.EditorRaiseLocalEuler}");
        sb.AppendLine($"defaults raiseDuration={hands.EditorRaiseDuration} lowerDuration={hands.EditorLowerDuration}");

        bool defaultsOk =
            Approx(hands.EditorRaiseLocalOffset, new Vector3(0f, 0.1f, 0.55f))
            && Approx(hands.EditorRaiseLocalEuler, new Vector3(180f, 0f, 12f))
            && Mathf.Abs(hands.EditorRaiseDuration - 0.2f) < 0.001f
            && Mathf.Abs(hands.EditorLowerDuration - 0.2f) < 0.001f;
        sb.AppendLine("defaultsMatch=" + defaultsOk);

        hands.NotifyPickedUp();
        hands.EditorSnapRaised();

        Vector3 raisedLocalL = handL.localPosition;
        Vector3 raisedLocalR = handR.localPosition;
        Vector3 expectedL = restL + new Vector3(0f, 0.1f, 0.55f);
        Vector3 expectedR = restR + new Vector3(0f, 0.1f, 0.55f);
        bool poseOk = Approx(raisedLocalL, expectedL) && Approx(raisedLocalR, expectedR);
        sb.AppendLine($"raisedLocalL={raisedLocalL} expected={expectedL}");
        sb.AppendLine($"raisedLocalR={raisedLocalR} expected={expectedR}");
        sb.AppendLine("raisedPoseOk=" + poseOk);

        bool holdsSteady = hands.EditorRotationHoldsSteady(45);
        sb.AppendLine("rotationHoldsSteady=" + holdsSteady);

        float worldYBefore = handL.position.y;
        // Simulate ProceduralBounceMotion parent bob — hands must keep local raise.
        rootGo.transform.localPosition = new Vector3(0f, 0.08f, 0f);
        bool localStable = Approx(handL.localPosition, raisedLocalL) && Approx(handR.localPosition, raisedLocalR);
        bool worldFollowsParent = handL.position.y > worldYBefore + 0.07f;
        sb.AppendLine($"localStableUnderParentBob={localStable} worldFollowsParent={worldFollowsParent}");

        hands.NotifyDropped();
        hands.EditorSnapRest();
        bool restOk = Approx(handL.localPosition, restL) && Approx(handR.localPosition, restR);
        sb.AppendLine("restoredRestOk=" + restOk);

        bool ok = defaultsOk && poseOk && holdsSteady && localStable && worldFollowsParent && restOk;
        sb.AppendLine(ok ? "OK" : "FAIL_CHECKS");

        Object.DestroyImmediate(rootGo);
        return sb.ToString();
    }

    static bool Approx(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-6f;
}
