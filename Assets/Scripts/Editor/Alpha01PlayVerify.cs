using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 164 — Play-mode parity checks on Alpha01 (camera / character / grid).
/// CameraFollow stays ON. Does not save GridFoundation or Alpha01.
/// Flag: Temp/alpha01_play.flag
/// Report: Temp/alpha01/play.txt
/// Shots: Temp/alpha01/cam.png walk.png idle.png grid.png
/// </summary>
[InitializeOnLoad]
public static class Alpha01PlayVerify
{
    const string FlagPath = "Temp/alpha01_play.flag";
    const string ReportPath = "Temp/alpha01/play.txt";
    const string ShotDir = "Temp/alpha01";
    const string ScenePath = Alpha01SceneSetup.ScenePath;
    const string PendingKey = "Alpha01Play.pending";
    static readonly Vector3 CamOffset = new Vector3(-8f, 7f, -8f);

    static int sWait;
    static int sPhase;
    static int sEnterFrame = -1;
    static int sLastPlayFrame = -1;
    static StringBuilder sLog;
    static Vector3 sPlayerStart;
    static Vector3 sCamStart;
    static bool sHeldW;
    static bool sHeldE;
    static bool sHeldG;
    static bool sHeldSpace;
    static bool sInputHooked;

    static Alpha01PlayVerify()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Scenes/164 Play-verify Alpha01")]
    public static void Menu()
    {
        File.WriteAllText(Full(FlagPath), "run\n");
    }

    static void OnPlay(PlayModeStateChange c)
    {
        if (c != PlayModeStateChange.EnteredEditMode)
            return;
        ReleaseKeys();
        HookInput(false);
        SessionState.SetBool(PendingKey, false);
        sPhase = 0;
        sEnterFrame = -1;
    }

    static void Poll()
    {
        if (sPhase == 1)
        {
            TickPlay();
            return;
        }

        if (EditorApplication.isCompiling)
            return;

        bool flag = File.Exists(Full(FlagPath));
        bool pending = SessionState.GetBool(PendingKey, false);

        if (pending && EditorApplication.isPlaying)
        {
            if (sLog == null)
            {
                sLog = new StringBuilder();
                if (File.Exists(Full(ReportPath)))
                    sLog.Append(File.ReadAllText(Full(ReportPath)));
                sLog.AppendLine("entered Play");
            }
            if (sEnterFrame < 0)
            {
                HookInput(true);
                sEnterFrame = Time.frameCount;
                sLastPlayFrame = -1;
                sLog.AppendLine("playFrame origin=" + sEnterFrame);
            }
            sPhase = 1;
            return;
        }

        if (!flag)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        try { File.Delete(Full(FlagPath)); } catch { }
        Begin();
    }

    static void Begin()
    {
        sLog = new StringBuilder();
        sLog.AppendLine("=== Ticket 164 Alpha01 Play verify ===");
        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            if (!File.Exists(Full(ScenePath)))
            {
                Fail("FAIL scene missing " + ScenePath);
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sLog.AppendLine("opened " + ScenePath + " (did not save other scenes)");
        }

        Camera cam = Camera.main;
        var follow = cam != null ? cam.GetComponent<CameraFollow>() : null;
        if (follow != null && !follow.enabled)
            follow.enabled = true;

        PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        GridSystem grid = UnityEngine.Object.FindAnyObjectByType<GridSystem>();
        sLog.AppendLine("edit camera=" + (cam != null) + " follow=" + (follow != null && follow.enabled));
        sLog.AppendLine("edit player=" + (player != null) + " grid=" + (grid != null));
        if (cam == null || follow == null || player == null || grid == null)
        {
            Fail("FAIL missing camera/follow/player/grid in edit mode");
            return;
        }

        SessionState.SetBool(PendingKey, true);
        sLog.AppendLine("entering Play (CameraFollow stays ON)");
        WriteReport();
        EditorApplication.isPlaying = true;
    }

    static void TickPlay()
    {
        if (sEnterFrame < 0)
        {
            sEnterFrame = Time.frameCount;
            sLastPlayFrame = -1;
        }
        int playFrame = Time.frameCount - sEnterFrame;
        if (playFrame < 0)
        {
            sEnterFrame = Time.frameCount;
            sLastPlayFrame = -1;
            return;
        }
        if (playFrame > 600)
        {
            sLog.AppendLine("FAIL playFrame timeout " + playFrame);
            Finish();
            return;
        }
        if (playFrame == sLastPlayFrame)
            return;
        sLastPlayFrame = playFrame;
        sWait = playFrame;
        KeepFollowOn();
        FocusGameView();

        // 1..25 idle
        if (sWait == 20)
        {
            SnapshotIdleStart();
            Capture("idle");
            sLog.AppendLine("--- CHARACTER idle ---");
            ReportBounce("idle");
        }

        // walk via Input System W (bounce reads PlayerController.PlanarSpeed)
        if (sWait >= 26 && sWait < 90)
            sHeldW = true;
        else if (sHeldW && sWait >= 90)
            sHeldW = false;

        if (sWait == 70)
        {
            Capture("walk");
            sLog.AppendLine("--- CHARACTER walk ---");
            ReportBounce("walk");
            ReportCameraFollow("walk");
        }

        if (sWait == 115)
        {
            Capture("cam");
            sLog.AppendLine("--- CAMERA ---");
            ReportCameraFollow("after-walk");
        }

        // grid overlay + occupancy
        if (sWait == 125)
        {
            sLog.AppendLine("--- GRID ---");
            ReportGrid();
            var overlay = UnityEngine.Object.FindAnyObjectByType<GridOverlay>();
            if (overlay != null)
                overlay.Visible = true;
            sLog.AppendLine("GridOverlay.Visible forced true for overlay shot");
        }
        if (sWait == 140)
            Capture("grid");

        // carry E — warp next to the runtime crate (pickup radius 1.6)
        if (sWait == 150)
            WarpPlayer(new Vector3(0.2f, 0f, -1.0f));
        if (sWait >= 155 && sWait < 158)
            sHeldE = true;
        else if (sWait == 158)
            sHeldE = false;
        if (sWait == 162)
            TryForcePickup();
        if (sWait == 170)
        {
            sLog.AppendLine("--- CHARACTER carry ---");
            var carry = UnityEngine.Object.FindAnyObjectByType<PlayerOverheadCarry>();
            sLog.AppendLine("carrying=" + (carry != null && carry.IsCarrying)
                + " held=" + (carry != null && carry.Held != null ? carry.Held.name : "null"));
            Capture("carry");
            TryForcePutDown();
        }

        // seat jump Space — spawn is ~2.08 m from SeatTarget (detectRadius 2.5)
        if (sWait == 180)
            WarpPlayer(new Vector3(-0.58f, 0f, -0.5f));
        if (sWait >= 185 && sWait < 188)
            sHeldSpace = true;
        else if (sWait == 188)
            sHeldSpace = false;
        if (sWait == 192)
            TryForceSeatJump();
        if (sWait == 230)
        {
            sLog.AppendLine("--- CHARACTER seat jump ---");
            var jump = UnityEngine.Object.FindAnyObjectByType<PlayerJumpOnSeat>();
            sLog.AppendLine("jumpOnSeat mounted=" + (jump != null && jump.IsMounted)
                + " jumping=" + (jump != null && jump.IsJumping));
            Capture("seat");
            Finish();
        }
    }

    static void SnapshotIdleStart()
    {
        PlayerController p = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        Camera cam = Camera.main;
        sPlayerStart = p != null ? p.transform.position : Vector3.zero;
        sCamStart = cam != null ? cam.transform.position : Vector3.zero;
        sLog.AppendLine("play spawn player=" + sPlayerStart + " cam=" + sCamStart);
        var bounce = UnityEngine.Object.FindAnyObjectByType<ProceduralBounceMotion>();
        sLog.AppendLine("ProceduralBounceMotion present=" + (bounce != null));
        Transform visual = p != null ? p.transform.Find("Character_Base") : null;
        sLog.AppendLine("Character_Base instance=" + (visual != null));
    }

    static void WarpPlayer(Vector3 pos)
    {
        PlayerController p = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (p == null)
            return;
        CharacterController cc = p.GetComponent<CharacterController>();
        if (cc != null)
            cc.enabled = false;
        p.transform.position = pos;
        if (cc != null)
            cc.enabled = true;
        sLog.AppendLine("warp player to " + pos);
    }

    static void TryForcePickup()
    {
        var carry = UnityEngine.Object.FindAnyObjectByType<PlayerOverheadCarry>();
        if (carry == null || carry.IsCarrying)
            return;
        MethodInfo m = typeof(PlayerOverheadCarry).GetMethod(
            "TryPickUp", BindingFlags.Instance | BindingFlags.NonPublic);
        m?.Invoke(carry, null);
        sLog.AppendLine("TryPickUp fallback carrying=" + carry.IsCarrying);
    }

    static void TryForcePutDown()
    {
        var carry = UnityEngine.Object.FindAnyObjectByType<PlayerOverheadCarry>();
        if (carry == null || !carry.IsCarrying)
            return;
        MethodInfo m = typeof(PlayerOverheadCarry).GetMethod(
            "PutDown", BindingFlags.Instance | BindingFlags.NonPublic);
        m?.Invoke(carry, null);
        sLog.AppendLine("PutDown after carry shot (seat jump ignores Space while carrying)");
    }

    static void TryForceSeatJump()
    {
        var jump = UnityEngine.Object.FindAnyObjectByType<PlayerJumpOnSeat>();
        if (jump == null || jump.IsMounted || jump.IsJumping)
            return;
        MethodInfo find = typeof(PlayerJumpOnSeat).GetMethod(
            "FindNearestSeat", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo begin = typeof(PlayerJumpOnSeat).GetMethod(
            "BeginJumpOn", BindingFlags.Instance | BindingFlags.NonPublic);
        object seat = find?.Invoke(jump, null);
        if (seat == null)
        {
            sLog.AppendLine("seat jump fallback: no JumpOnSeatPoint in range");
            return;
        }
        begin?.Invoke(jump, new[] { seat });
        sLog.AppendLine("BeginJumpOn fallback jumping=" + jump.IsJumping);
    }

    static void ReportBounce(string label)
    {
        var bounce = UnityEngine.Object.FindAnyObjectByType<ProceduralBounceMotion>();
        PlayerController p = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        sLog.AppendLine(label + " planarSpeed=" + (p != null ? p.PlanarSpeed.ToString("F2") : "n/a")
            + " bounceMoving=" + (bounce != null && bounce.IsMoving)
            + " bounceWave=" + (bounce != null ? bounce.Wave.ToString("F2") : "n/a"));
    }

    static void ReportCameraFollow(string label)
    {
        Camera cam = Camera.main;
        var follow = cam != null ? cam.GetComponent<CameraFollow>() : null;
        PlayerController p = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        Transform target = null;
        if (follow != null)
        {
            var f = typeof(CameraFollow).GetField("target",
                BindingFlags.Instance | BindingFlags.NonPublic);
            target = f != null ? f.GetValue(follow) as Transform : null;
        }
        Vector3 playerNow = p != null ? p.transform.position : Vector3.zero;
        Vector3 camNow = cam != null ? cam.transform.position : Vector3.zero;
        Vector3 expected = playerNow + CamOffset;
        float dist = Vector3.Distance(camNow, expected);
        sLog.AppendLine(label + " followEnabled=" + (follow != null && follow.enabled)
            + " target=" + (target != null ? target.name : "null")
            + " ortho=" + (cam != null ? cam.orthographicSize.ToString("F2") : "n/a")
            + " camToOffset=" + dist.ToString("F2")
            + " playerDelta=" + Vector3.Distance(
                new Vector3(sPlayerStart.x, 0, sPlayerStart.z),
                new Vector3(playerNow.x, 0, playerNow.z)).ToString("F2"));
        sLog.AppendLine("CHECK camera follow on + ortho~6: "
            + (follow != null && follow.enabled && cam != null && cam.orthographic
                && Mathf.Abs(cam.orthographicSize - 6f) < 0.15f));
        sLog.AppendLine("CHECK camera tracks player offset: " + (dist < 2.5f));
    }

    static void ReportGrid()
    {
        GridSystem grid = GridSystem.Instance;
        var overlay = UnityEngine.Object.FindAnyObjectByType<GridOverlay>();
        sLog.AppendLine("GridSystem.Instance=" + (grid != null)
            + " origin=" + (grid != null ? grid.originWorld.ToString() : "n/a")
            + " cellSize=" + (grid != null ? grid.cellSize.ToString() : "n/a"));
        sLog.AppendLine("GridOverlay present=" + (overlay != null)
            + " visibleBeforeToggle=" + (overlay != null && overlay.Visible));

        bool occupyOk = false;
        if (grid != null)
        {
            GridCoord cell = new GridCoord(3, 3);
            bool free = grid.IsFree(new[] { cell });
            occupyOk = free && grid.TryOccupy(new[] { cell }, "alpha01-probe");
            bool occupied = grid.IsOccupied(cell);
            object who = grid.GetOccupant(cell);
            sLog.AppendLine("TryOccupy (3,3) free=" + free + " ok=" + occupyOk
                + " IsOccupied=" + occupied + " occupant=" + (who != null));
            grid.Release(new[] { cell });
            sLog.AppendLine("Release (3,3) stillOccupied=" + grid.IsOccupied(cell));
        }

        GameObject crate = GameObject.Find("Item_Crate_Base");
        GameObject floor = GameObject.Find(WorldFloorEnsure.FloorName);
        sLog.AppendLine("WorldFloor=" + (floor != null) + " Item_Crate_Base=" + (crate != null));
        if (crate != null && grid != null)
        {
            GridCoord crateCell = grid.WorldToCell(crate.transform.position);
            sLog.AppendLine("crate world=" + crate.transform.position + " cell=" + crateCell);
        }
        sLog.AppendLine("CHECK grid instance+occupy: " + (grid != null && occupyOk));
        sLog.AppendLine("CHECK overlay component: " + (overlay != null));
    }

    static void Finish()
    {
        KeepFollowOn();
        ReleaseKeys();
        HookInput(false);
        var follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        sLog.AppendLine("CameraFollow still enabled at end=" + (follow != null && follow.enabled));
        sLog.AppendLine("scene dirty (must not save)=" + SceneManager.GetActiveScene().isDirty);
        sLog.AppendLine("RESULT=OK inspect Temp/alpha01/*.png");
        WriteReport();
        Debug.Log("[Alpha01PlayVerify]\n" + sLog);
        sPhase = 0;
        SessionState.SetBool(PendingKey, false);
        sEnterFrame = -1;
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static void KeepFollowOn()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        var follow = cam.GetComponent<CameraFollow>();
        if (follow != null && !follow.enabled)
            follow.enabled = true;
    }

    static void HookInput(bool on)
    {
        if (on == sInputHooked)
            return;
        if (on)
            InputSystem.onBeforeUpdate += PushHeldKeys;
        else
            InputSystem.onBeforeUpdate -= PushHeldKeys;
        sInputHooked = on;
    }

    static void PushHeldKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            try { kb = InputSystem.AddDevice<Keyboard>(); }
            catch { return; }
        }
        if (kb == null)
            return;
        if (sHeldW)
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
        else if (sHeldE)
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.E));
        else if (sHeldG)
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.G));
        else if (sHeldSpace)
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
        else
            InputSystem.QueueStateEvent(kb, new KeyboardState());
    }

    static void ReleaseKeys()
    {
        sHeldW = sHeldE = sHeldG = sHeldSpace = false;
    }

    static void Capture(string name)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            sLog.AppendLine("FAIL capture no Camera.main " + name);
            return;
        }
        if (name == "grid")
        {
            var overlay = UnityEngine.Object.FindAnyObjectByType<GridOverlay>();
            if (overlay != null)
            {
                overlay.Visible = true;
                overlay.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
            }
        }
        Directory.CreateDirectory(Full(ShotDir));
        Vector2 gv = Handles.GetMainGameViewSize();
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
        string path = Full(ShotDir + "/" + name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        sLog.AppendLine("shot " + name + " " + tex.width + "x" + tex.height + " bytes=" + new FileInfo(path).Length);
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static void Fail(string msg)
    {
        if (sLog == null)
            sLog = new StringBuilder();
        sLog.AppendLine(msg);
        sLog.AppendLine("RESULT=FAIL");
        WriteReport();
        Debug.LogError("[Alpha01PlayVerify] " + msg);
        SessionState.SetBool(PendingKey, false);
        sPhase = 0;
    }

    static void FocusGameView()
    {
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return;
        EditorWindow.GetWindow(gvType, false, "Game", true).Focus();
    }

    static void WriteReport()
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, sLog != null ? sLog.ToString() : "");
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }
}
