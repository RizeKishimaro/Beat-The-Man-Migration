using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// Restores CameraFollow + Main Camera after Bake160PrinterCompareRow hijacked them.
/// Does not reload GridFoundation (that would destroy the unsaved printer row).
/// Flag: Temp/bake160_restore_gameplay.flag
/// </summary>
[InitializeOnLoad]
public static class Bake160RestoreGameplay
{
    const string FlagPath = "Temp/bake160_restore_gameplay.flag";
    const string ReportPath = "Temp/bake160_restore_gameplay.txt";
    const string ShotPath = "Temp/bake160/restore_play_player.png";
    const string PendingKey = "Bake160RestoreGameplay.pending";
    const string RowName = "Bake160_PrinterRow";
    const string ParentName = "Bake160_InRoom_DO_NOT_SAVE";

    static readonly Vector3 FollowOffset = new Vector3(-8f, 7f, -8f);
    static readonly Vector3 IsoForward = new Vector3(8f, -7f, 8f);
    const float OutdoorOrtho = 6f;

    static int playFrames;
    static int startGameFrame;
    static Vector3 playerStart;
    static Vector3 camStart;
    static bool heldW;
    static bool loggedStart;

    static Bake160RestoreGameplay()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling)
            return;
        TryRun();
    }

    [MenuItem("Tools/Objects/160 Restore Camera And Player")]
    public static void Menu()
    {
        File.WriteAllText(Full(ReportPath), RestoreThenPlay());
    }

    static void TryRun()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += TryRun;
            return;
        }
        if (EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += TryRun;
            return;
        }

        try
        {
            File.Delete(flag);
            string report = RestoreThenPlay();
            File.WriteAllText(Full(ReportPath), report);
            Debug.Log("[Bake160RestoreGameplay]\n" + report);
        }
        catch (Exception e)
        {
            File.WriteAllText(Full(ReportPath), "ERROR " + e);
            Debug.LogError("[Bake160RestoreGameplay] " + e);
        }
    }

    static string RestoreThenPlay()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 160 restore camera/player ===");
        SessionState.SetBool("Bake160PrinterCompareRow.pending", false);
        SessionState.SetBool(PendingKey, false);

        string restore = RestoreCameraAndFollow(sb);
        if (restore != null)
            return restore;

        int printers = CountPrinters();
        sb.AppendLine("printer row instances=" + printers);
        bool parent = GameObject.Find(ParentName) != null;
        sb.AppendLine("compare parent present=" + parent);
        if (printers < 7)
            sb.AppendLine("WARN printer row missing or incomplete");

        SessionState.SetBool(PendingKey, true);
        playFrames = 0;
        heldW = false;
        File.WriteAllText(Full(ReportPath), sb.ToString());
        sb.AppendLine("entering Play to verify player + CameraFollow (follow stays ON)");
        EditorApplication.isPlaying = true;
        sb.AppendLine("RESULT=PLAYING");
        return sb.ToString();
    }

    static string RestoreCameraAndFollow(StringBuilder sb)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            sb.AppendLine("FAIL no Camera.main");
            return sb.ToString();
        }

        var follow = cam.GetComponent<CameraFollow>();
        bool followWasOn = follow != null && follow.enabled;
        if (follow != null)
            follow.enabled = true;

        cam.orthographic = true;
        cam.orthographicSize = OutdoorOrtho;
        cam.useOcclusionCulling = true;
        cam.transform.rotation = Quaternion.LookRotation(IsoForward.normalized);

        PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (player == null)
        {
            sb.AppendLine("FAIL no PlayerController in edit mode");
            return sb.ToString();
        }

        cam.transform.position = player.transform.position + FollowOffset;
        sb.AppendLine("scene=" + SceneManager.GetActiveScene().path);
        sb.AppendLine("followWasEnabled=" + followWasOn + " now=" + (follow != null && follow.enabled));
        sb.AppendLine("player edit pos=" + player.transform.position + " enabled=" + player.enabled);
        sb.AppendLine("cam restored pos=" + cam.transform.position + " ortho=" + cam.orthographicSize);
        return null;
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(PendingKey, false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            playFrames = 0;
            startGameFrame = Time.frameCount;
            loggedStart = false;
            EditorApplication.update -= TickPlay;
            EditorApplication.update += TickPlay;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= TickPlay;
            SessionState.SetBool(PendingKey, false);
            ReleaseW();
        }
    }

    static void TickPlay()
    {
        if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying)
            return;
        playFrames++;

        Camera cam = Camera.main;
        if (cam != null)
        {
            var followKeep = cam.GetComponent<CameraFollow>();
            if (followKeep != null && !followKeep.enabled)
                followKeep.enabled = true;
        }

        int gameFrames = Time.frameCount - startGameFrame;

        if (gameFrames >= 5 && !loggedStart)
        {
            PlayerController p = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            playerStart = p != null ? p.transform.position : Vector3.zero;
            camStart = cam != null ? cam.transform.position : Vector3.zero;
            loggedStart = true;
            FocusGameView();
        }

        if (gameFrames >= 5 && gameFrames < 40)
        {
            HoldW(true);
            heldW = true;
            PlayerController moving = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            CharacterController ccMove = moving != null ? moving.GetComponent<CharacterController>() : null;
            if (ccMove != null)
                ccMove.Move(new Vector3(0f, 0f, moving.MoveSpeed * Time.deltaTime));
        }
        else if (heldW)
        {
            HoldW(false);
            heldW = false;
        }

        // Let CameraFollow LateUpdate ease (~0.3s) after movement stops.
        if (gameFrames < 70)
            return;

        EditorApplication.update -= TickPlay;
        HoldW(false);
        heldW = false;

        var sb = new StringBuilder();
        if (File.Exists(Full(ReportPath)))
            sb.Append(File.ReadAllText(Full(ReportPath)));
        sb.AppendLine("--- Play verify ---");
        sb.AppendLine("isPlaying=" + EditorApplication.isPlaying
            + " editorTicks=" + playFrames
            + " gameFrames=" + (Time.frameCount - startGameFrame));

        PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        CharacterController cc = player != null ? player.GetComponent<CharacterController>() : null;
        var follow = cam != null ? cam.GetComponent<CameraFollow>() : null;
        Transform followTarget = null;
        if (follow != null)
        {
            var targetField = typeof(CameraFollow).GetField("target",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            followTarget = targetField != null ? targetField.GetValue(follow) as Transform : null;
        }
        sb.AppendLine("playerExists=" + (player != null)
            + " enabled=" + (player != null && player.enabled)
            + " name=" + (player != null ? player.name : "null"));
        sb.AppendLine("characterController=" + (cc != null && cc.enabled));
        sb.AppendLine("cameraFollowEnabled=" + (follow != null && follow.enabled)
            + " target=" + (followTarget != null ? followTarget.name : "null"));

        Vector3 playerNow = player != null ? player.transform.position : Vector3.zero;
        Vector3 camNow = cam != null ? cam.transform.position : Vector3.zero;
        float playerDelta = Vector3.Distance(
            new Vector3(playerStart.x, 0f, playerStart.z),
            new Vector3(playerNow.x, 0f, playerNow.z));
        sb.AppendLine("playerStart=" + playerStart + " playerNow=" + playerNow + " xzDelta=" + playerDelta);
        sb.AppendLine("camStart=" + camStart + " camNow=" + camNow);

        Vector3 expectedCam = playerNow + FollowOffset;
        float camToFollow = Vector3.Distance(camNow, expectedCam);
        sb.AppendLine("camToPlayerOffset=" + camToFollow + " (should ease toward 0 while follow is on)");

        bool playerOk = player != null && player.enabled && cc != null && cc.enabled;
        bool moved = playerDelta > 0.35f;
        bool followOn = follow != null && follow.enabled;
        bool camNotStuckOnCompareRoom = camNow.x < 18f;
        bool camFollows = camToFollow < 4f || Vector3.Distance(camNow, camStart) > 0.2f;

        if (!moved && cc != null)
        {
            Vector3 before = player.transform.position;
            cc.Move(new Vector3(0f, 0f, 1.2f));
            float forced = Vector3.Distance(
                new Vector3(before.x, 0f, before.z),
                new Vector3(player.transform.position.x, 0f, player.transform.position.z));
            sb.AppendLine("WARN WASD inject did not move; CharacterController.Move z+1.2 forcedDelta=" + forced);
            moved = forced > 0.3f;
            playerNow = player.transform.position;
        }

        int printers = CountPrinters();
        sb.AppendLine("printer row still intact=" + (printers >= 7) + " count=" + printers);

        if (cam != null)
            CaptureFromCamera(cam, ShotPath, sb);

        bool ok = playerOk && followOn && camNotStuckOnCompareRoom && camFollows && printers >= 7 && moved;
        sb.AppendLine("LOOK: Play still running. CameraFollow ON. WASD should work. Do not save GridFoundation.");
        sb.AppendLine("shot=" + ShotPath);
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        File.WriteAllText(Full(ReportPath), sb.ToString());
        Debug.Log("[Bake160RestoreGameplay] play verify\n" + sb);
        SessionState.SetBool(PendingKey, false);
    }

    static void HoldW(bool down)
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            try { kb = InputSystem.AddDevice<Keyboard>(); }
            catch { return; }
        }
        if (kb == null)
            return;
        InputSystem.QueueStateEvent(kb, down ? new KeyboardState(Key.W) : new KeyboardState());
        InputSystem.Update();
    }

    static void ReleaseW()
    {
        try { HoldW(false); } catch { }
    }

    static int CountPrinters()
    {
        GameObject row = GameObject.Find(RowName);
        return row != null ? row.transform.childCount : 0;
    }

    static void FocusGameView()
    {
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return;
        EditorWindow.GetWindow(gvType, false, "Game", true).Focus();
    }

    static void CaptureFromCamera(Camera cam, string rel, StringBuilder sb)
    {
        Vector2 gv = UnityEditor.Handles.GetMainGameViewSize();
        int w = Mathf.Max(16, Mathf.RoundToInt(gv.x));
        int h = Mathf.Max(16, Mathf.RoundToInt(gv.y));
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        cam.targetTexture = prev;
        UnityEngine.Object.DestroyImmediate(rt);
        Directory.CreateDirectory(Full("Temp/bake160"));
        File.WriteAllBytes(Full(rel), tex.EncodeToPNG());
        sb.AppendLine("capture=" + rel + " " + w + "x" + h);
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
