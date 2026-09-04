using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Read-only camera-follow scope check. Does not save any scene.
/// CameraFollow stays ON. Flag: Temp/cam_judder_scope.flag (scene path inside).
/// Report: Temp/cam_judder_scope.txt (appended per scene).
/// </summary>
[InitializeOnLoad]
public static class CamJudderScope
{
    const string FlagPath = "Temp/cam_judder_scope.flag";
    const string ReportPath = "Temp/cam_judder_scope.txt";
    const string PendingKey = "CamJudderScope.pending";
    static readonly Vector3 Offset = new Vector3(-8f, 7f, -8f);

    static int sPhase;
    static int sEnterFrame = -1;
    static int sLastFrame = -1;
    static bool sHeldW;
    static bool sHooked;
    static StringBuilder sLog;
    static readonly List<float> sFollowErr = new List<float>();
    static readonly List<float> sLockErr = new List<float>();
    static readonly List<float> sDt = new List<float>();
    static Vector3 sPrevPlayer;
    static Vector3 sPrevCam;
    static bool sHavePrev;

    static CamJudderScope()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    static void OnPlay(PlayModeStateChange c)
    {
        if (c != PlayModeStateChange.EnteredEditMode)
            return;
        sHeldW = false;
        HookInput(false);
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

        string flag = Full(FlagPath);
        bool pending = SessionState.GetBool(PendingKey, false);

        if (pending && EditorApplication.isPlaying)
        {
            if (sLog == null)
            {
                sLog = new StringBuilder();
                if (File.Exists(Full(ReportPath)))
                    sLog.Append(File.ReadAllText(Full(ReportPath)));
            }
            if (sEnterFrame < 0)
            {
                HookInput(true);
                sEnterFrame = Time.frameCount;
                sLastFrame = -1;
                sHavePrev = false;
                sFollowErr.Clear();
                sLockErr.Clear();
                sDt.Clear();
            }
            sPhase = 1;
            return;
        }

        if (!File.Exists(flag))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        string scenePath = File.ReadAllText(flag).Trim();
        try { File.Delete(flag); } catch { }
        Begin(scenePath);
    }

    static void Begin(string scenePath)
    {
        sLog = new StringBuilder();
        if (File.Exists(Full(ReportPath)))
            sLog.Append(File.ReadAllText(Full(ReportPath)));
        sLog.AppendLine("=== Cam judder scope ===");
        sLog.AppendLine("will not save " + scenePath);

        if (SceneManager.GetActiveScene().path != scenePath)
        {
            if (!File.Exists(Full(scenePath)))
            {
                sLog.AppendLine("FAIL missing " + scenePath);
                WriteReport();
                return;
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            sLog.AppendLine("opened " + scenePath + " (did not save previous)");
        }

        Camera cam = Camera.main;
        var follow = cam != null ? cam.GetComponent<CameraFollow>() : null;
        if (follow != null && !follow.enabled)
            follow.enabled = true;

        var urp = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
        sLog.AppendLine("scene=" + SceneManager.GetActiveScene().path);
        sLog.AppendLine("CameraFollow enabled=" + (follow != null && follow.enabled)
            + " method=LateUpdate executionOrder=60");
        sLog.AppendLine("PlayerController move=Update (CharacterController.Move * deltaTime)");
        if (urp != null)
            sLog.AppendLine("URP antialiasing=" + urp.antialiasing + " quality=" + urp.antialiasingQuality
                + " post=" + urp.renderPostProcessing);
        else
            sLog.AppendLine("URP additional data=null");
        if (cam != null)
            sLog.AppendLine("clearFlags=" + cam.clearFlags + " ortho=" + cam.orthographicSize);

        SessionState.SetBool(PendingKey, true);
        WriteReport();
        EditorApplication.isPlaying = true;
    }

    static void TickPlay()
    {
        if (sEnterFrame < 0)
            sEnterFrame = Time.frameCount;
        int f = Time.frameCount - sEnterFrame;
        if (f < 0)
        {
            sEnterFrame = Time.frameCount;
            sLastFrame = -1;
            return;
        }
        if (f > 400)
        {
            sLog.AppendLine("FAIL timeout playFrame=" + f);
            Finish();
            return;
        }
        if (f == sLastFrame)
            return;
        sLastFrame = f;

        var follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (follow != null && !follow.enabled)
            follow.enabled = true;

        if (f >= 20 && f < 140)
            sHeldW = true;
        else
            sHeldW = false;

        if (f >= 40 && f < 140)
            Sample();

        if (f == 160)
            Finish();
    }

    static void Sample()
    {
        PlayerController p = Object.FindAnyObjectByType<PlayerController>();
        Camera cam = Camera.main;
        if (p == null || cam == null)
            return;
        Vector3 player = p.transform.position;
        Vector3 camPos = cam.transform.position;
        Vector3 desired = player + Offset;
        sFollowErr.Add(Vector3.Distance(camPos, desired));
        sDt.Add(Time.deltaTime);
        if (sHavePrev)
        {
            Vector3 pDelta = player - sPrevPlayer;
            Vector3 cDelta = camPos - sPrevCam;
            pDelta.y = 0f;
            cDelta.y = 0f;
            sLockErr.Add(Vector3.Distance(cDelta, pDelta));
        }
        sPrevPlayer = player;
        sPrevCam = camPos;
        sHavePrev = true;
    }

    static void Finish()
    {
        sHeldW = false;
        HookInput(false);
        Summarize("followErr(m) cam vs player+offset", sFollowErr);
        Summarize("lockErr(m) |camDelta-playerDelta| planar", sLockErr);
        Summarize("deltaTime", sDt);
        PlayerController p = Object.FindAnyObjectByType<PlayerController>();
        sLog.AppendLine("end planarSpeed=" + (p != null ? p.PlanarSpeed.ToString("F2") : "n/a"));
        var follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        sLog.AppendLine("CameraFollow still on=" + (follow != null && follow.enabled));
        sLog.AppendLine("scene dirty (must not save)=" + SceneManager.GetActiveScene().isDirty);
        sLog.AppendLine("RESULT=OK");
        WriteReport();
        Debug.Log("[CamJudderScope]\n" + sLog);
        sPhase = 0;
        sEnterFrame = -1;
        SessionState.SetBool(PendingKey, false);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static void Summarize(string label, List<float> vals)
    {
        if (vals.Count == 0)
        {
            sLog.AppendLine(label + " n=0");
            return;
        }
        float min = vals[0], max = vals[0], sum = 0f, sum2 = 0f;
        for (int i = 0; i < vals.Count; i++)
        {
            float v = vals[i];
            if (v < min) min = v;
            if (v > max) max = v;
            sum += v;
            sum2 += v * v;
        }
        float mean = sum / vals.Count;
        float var = Mathf.Max(0f, sum2 / vals.Count - mean * mean);
        sLog.AppendLine(label + " n=" + vals.Count
            + " min=" + min.ToString("F4")
            + " mean=" + mean.ToString("F4")
            + " max=" + max.ToString("F4")
            + " stdev=" + Mathf.Sqrt(var).ToString("F4"));
    }

    static void HookInput(bool on)
    {
        if (on == sHooked)
            return;
        if (on)
            InputSystem.onBeforeUpdate += PushW;
        else
            InputSystem.onBeforeUpdate -= PushW;
        sHooked = on;
    }

    static void PushW()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            try { kb = InputSystem.AddDevice<Keyboard>(); }
            catch { return; }
        }
        if (kb == null)
            return;
        InputSystem.QueueStateEvent(kb, sHeldW ? new KeyboardState(Key.W) : new KeyboardState());
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
