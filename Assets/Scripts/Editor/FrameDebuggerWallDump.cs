using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Play-mode Frame Debugger dump for DefaultBuilding_117 ghostly walls.
/// Diagnoses which draw call paints the see-through wall pixels.
/// Does not change meshes, materials, or the scene.
/// Flag: Temp/frame_debugger_wall.flag
/// Report: Temp/frame_debugger_wall.txt
/// </summary>
[InitializeOnLoad]
public static class FrameDebuggerWallDump
{
    const string FlagPath = "Temp/frame_debugger_wall.flag";
    const string VerifyFlagPath = "Temp/fd_dn_verify.flag";
    const string ReportPath = "Temp/frame_debugger_wall.txt";
    const string AllPath = "Temp/frame_debugger_wall_all.txt";
    const string RestorePath = "Temp/frame_debugger_wall_restore.txt";
    const string ShotConfirm = "Temp/fd_wall_confirm.png";
    const BindingFlags Stat = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    static int sPhase; // 0 idle, 1 pose wait, 2 fd wait, 3 step events, 4 strip-inside
    static int sWait;
    static int sStepIndex;
    static List<int> sStepLimits;
    static Color sPrevLeft;
    static Color sPrevFront;
    static bool sVerifyMode;
    static Vector3 sCamPos;
    static Vector3 sPlayerPos;
    static bool sSavedPlayer;
    static Quaternion sCamRot;
    static float sOrtho;
    static bool sFollowWasOn;
    static bool sSavedCam;
    static StringBuilder sLog;
    static Vector2 sWallUv; // viewport 0-1 of Wall_Left outer-face sample
    static Vector2 sFrontUv;
    static Camera sCam;
    static bool sWasPaused;

    static FrameDebuggerWallDump()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += _ =>
        {
            if (!EditorApplication.isPlaying)
                TryRestoreFromDisk();
        };
    }

    static void Poll()
    {
        if (sPhase == 0)
        {
            if (EditorApplication.isCompiling)
                return;
            if (File.Exists(Full(VerifyFlagPath)))
            {
                File.Delete(Full(VerifyFlagPath));
                sVerifyMode = true;
                Begin();
                return;
            }
            if (!File.Exists(Full(FlagPath)))
                return;
            File.Delete(Full(FlagPath));
            sVerifyMode = false;
            Begin();
            return;
        }

        if (sPhase == 1)
        {
            sWait++;
            if (sWait < 20)
                return;
            CaptureConfirmAndEnableFd();
            return;
        }

        if (sPhase == 2)
        {
            sWait++;
            int count = GetEventCount();
            bool on = FrameDebugger.enabled;
            if ((on && count > 0) || sWait >= 90)
                StartStepping();
            return;
        }

        if (sPhase == 3)
        {
            sWait++;
            if (sWait < 5)
                return;
            CaptureOneStep();
            return;
        }

        if (sPhase == 4)
        {
            sWait++;
            if (sWait < 40)
                return;
            FinishInsideStripCapture();
        }
    }

    [MenuItem("Tools/Buildings/Frame Debugger Dump (walls)")]
    public static void MenuRun()
    {
        if (sPhase != 0)
            return;
        Begin();
    }

    static void Begin()
    {
        sLog = new StringBuilder();
        sLog.AppendLine("=== Frame Debugger wall dump" + (sVerifyMode ? " (DepthNormals verify)" : " (no geometry fixes)") + " ===");
        sLog.AppendLine($"time={DateTime.UtcNow:o}");
        sLog.AppendLine($"isPlaying={EditorApplication.isPlaying}");
        sLog.AppendLine($"activeScene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}");

        sCam = Camera.main ?? UnityEngine.Object.FindAnyObjectByType<Camera>();
        Transform building = FindBuilding();
        if (sCam == null || building == null)
        {
            sLog.AppendLine($"FAIL cam={(sCam != null)} building={(building != null)}");
            File.WriteAllText(Full(ReportPath), sLog.ToString());
            sPhase = 0;
            return;
        }

        var follow = sCam.GetComponent<Behaviour>();
        foreach (var c in sCam.GetComponents<Behaviour>())
        {
            if (c.GetType().Name == "CameraFollow")
            {
                follow = c;
                break;
            }
        }
        sFollowWasOn = follow != null && follow.enabled;
        if (follow != null)
            follow.enabled = false;

        sCamPos = sCam.transform.position;
        sCamRot = sCam.transform.rotation;
        sOrtho = sCam.orthographic ? sCam.orthographicSize : -1f;
        sSavedCam = true;
        WriteRestoreFile();

        Bounds env = Encapsulate(building);
        Vector3 corner = new Vector3(env.min.x, env.min.y + env.size.y * 0.45f, env.min.z);
        sCam.orthographic = true;
        if (sCam.orthographicSize < 1f)
            sCam.orthographicSize = 5f;
        sCam.transform.position = corner + new Vector3(-2.2f, 1.8f, -2.2f);
        sCam.transform.LookAt(corner + Vector3.up * 0.9f);
        sLog.AppendLine($"framed cam pos={sCam.transform.position} rot={sCam.transform.eulerAngles} ortho={sCam.orthographicSize}");

        Renderer wallLeft = FindRenderer(building, "Wall_Left");
        Renderer wallFront = FindRenderer(building, "Wall_Front_Left");
        if (wallLeft != null)
        {
            Vector3 p = OuterFaceSample(wallLeft.bounds, preferMinX: true);
            Vector3 vp = sCam.WorldToViewportPoint(p);
            sWallUv = new Vector2(vp.x, vp.y);
            sLog.AppendLine($"Wall_Left sampleWS={p} viewport={vp} rendererEnabled={wallLeft.enabled} mat={MatInfo(wallLeft)}");
        }
        if (wallFront != null)
        {
            Vector3 p = OuterFaceSample(wallFront.bounds, preferMinX: false);
            Vector3 vp = sCam.WorldToViewportPoint(p);
            sFrontUv = new Vector2(vp.x, vp.y);
            sLog.AppendLine($"Wall_Front_Left sampleWS={p} viewport={vp} rendererEnabled={wallFront.enabled} mat={MatInfo(wallFront)}");
        }

        DumpLiveWalls(sLog, building);
        DumpShaderPasses(sLog, wallLeft);

        sPhase = 1;
        sWait = 0;
        File.WriteAllText(Full(ReportPath), sLog.ToString() + "\nWAITING pose frames...\n");
    }

    static void CaptureConfirmAndEnableFd()
    {
        if (sCam != null)
            CaptureCamera(sCam, ShotConfirm);
        sLog.AppendLine($"confirmShot={ShotConfirm}");

        Type util = FindT("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility")
                    ?? FindT("FrameDebuggerUtility");
        if (util != null)
        {
            var loc = util.GetProperty("locallySupported", Stat);
            sLog.AppendLine($"locallySupported={(loc != null ? loc.GetValue(null, null) : "noprop")}");
        }

        sWasPaused = EditorApplication.isPaused;
        if (EditorApplication.isPlaying && !EditorApplication.isPaused)
        {
            EditorApplication.isPaused = true;
            sLog.AppendLine("paused play mode (required before Frame Debugger capture)");
        }

        bool opened = EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");
        sLog.AppendLine($"menu Window/Analysis/Frame Debugger={opened}");

        Type winType = FindT("UnityEditor.FrameDebuggerWindow") ?? FindT("FrameDebuggerWindow");
        EditorWindow win = null;
        if (winType != null)
        {
            win = EditorWindow.GetWindow(winType);
            var dis = winType.GetProperty("IsDisabledInEditor", Inst);
            var enabling = winType.GetProperty("IsEnablingFrameDebugger", Inst);
            sLog.AppendLine($"FD window IsDisabledInEditor={(dis != null ? dis.GetValue(win, null) : "noprop")} IsEnabling={(enabling != null ? enabling.GetValue(win, null) : "noprop")}");
            var enableM = winType.GetMethod("EnableFrameDebugger", Inst);
            if (enableM != null)
            {
                try
                {
                    enableM.Invoke(win, null);
                    sLog.AppendLine("invoked FrameDebuggerWindow.EnableFrameDebugger()");
                }
                catch (Exception e)
                {
                    sLog.AppendLine("EnableFrameDebugger() threw: " + (e.InnerException != null ? e.InnerException.Message : e.Message));
                }
            }
        }

        sLog.AppendLine($"SetEnabled(true,0)={TrySetFd(true)}");
        sLog.AppendLine($"FrameDebugger.enabled={FrameDebugger.enabled} count={GetEventCount()} paused={EditorApplication.isPaused}");

        sPhase = 2;
        sWait = 0;
        File.WriteAllText(Full(ReportPath), sLog.ToString() + "\nWAITING FD events...\n");
    }

    static void StartStepping()
    {
        try
        {
            sLog.AppendLine($"FrameDebugger.enabled={FrameDebugger.enabled} waitFrames={sWait} count={GetEventCount()}");
            DumpEvents(sLog);
        }
        catch (Exception e)
        {
            sLog.AppendLine("DumpEvents FAIL: " + e);
        }

        int n = GetEventCount();
        sStepLimits = new List<int>();
        int[] extra = { 9, 10, 11, 12, 13, 25, 26, 27, 28, 29, 30, n };
        for (int i = 14; i <= 24 && i <= n; i++)
            sStepLimits.Add(i);
        foreach (int x in extra)
        {
            if (x >= 1 && x <= n && !sStepLimits.Contains(x))
                sStepLimits.Add(x);
        }
        sStepLimits.Sort();
        sLog.AppendLine("--- stepping Game view at limits: " + string.Join(",", sStepLimits) + " ---");
        File.WriteAllText(Full(ReportPath), sLog.ToString() + "\nWAITING steps...\n");
        sPrevLeft = new Color(-1, 0, 0, 0);
        sPrevFront = new Color(-1, 0, 0, 0);
        sStepIndex = 0;
        sPhase = 3;
        sWait = 99;
        if (sStepLimits.Count > 0)
            SetLimit(sStepLimits[0]);
    }

    static void CaptureOneStep()
    {
        if (sStepLimits == null || sStepIndex >= sStepLimits.Count)
        {
            EndDump();
            return;
        }

        int lim = sStepLimits[sStepIndex];
        SetLimit(lim);
        Texture2D tex = GrabGamePixels();
        string shot = $"Temp/fd_step_{lim:D2}.png";
        if (tex != null)
        {
            SaveTex(tex, shot);
            Color left = SampleUv(tex, sWallUv);
            Color front = SampleUv(tex, sFrontUv);
            float dL = ColorDist(sPrevLeft, left);
            float dF = ColorDist(sPrevFront, front);
            sLog.AppendLine($"STEP limit={lim}/{GetEventCount()} left={Fmt(left)} dL={dL:F3} front={Fmt(front)} dF={dF:F3} shot={shot}");
            sPrevLeft = left;
            sPrevFront = front;
            UnityEngine.Object.DestroyImmediate(tex);
        }
        else
            sLog.AppendLine($"STEP limit={lim} grab=null");

        File.WriteAllText(Full(ReportPath), sLog.ToString());
        sStepIndex++;
        sWait = 0;
        if (sStepIndex < sStepLimits.Count)
            SetLimit(sStepLimits[sStepIndex]);
        else
            EndDump();
    }

    static void EndDump()
    {
        try
        {
            TrySetFd(false);
            Type winType = FindT("UnityEditor.FrameDebuggerWindow") ?? FindT("FrameDebuggerWindow");
            if (winType != null)
            {
                foreach (var obj in Resources.FindObjectsOfTypeAll(winType))
                {
                    var dis = winType.GetMethod("DisableFrameDebugger", Inst);
                    if (dis != null && obj != null)
                    {
                        try { dis.Invoke(obj, null); }
                        catch { }
                    }
                }
            }
            if (!sWasPaused)
                EditorApplication.isPaused = false;
            sLog.AppendLine($"FrameDebugger.enabled after disable={FrameDebugger.enabled} paused={EditorApplication.isPaused}");
        }
        catch (Exception e)
        {
            sLog.AppendLine("FAIL: " + e);
            Debug.LogError("[FrameDebuggerWallDump] " + e);
        }

        if (sVerifyMode && EditorApplication.isPlaying)
        {
            if (sCam != null)
                CaptureCamera(sCam, "Temp/fd_wall_fixed_outside.png");
            sLog.AppendLine("outsideShot=Temp/fd_wall_fixed_outside.png");
            var vis = UnityEngine.Object.FindAnyObjectByType<BuildingInteriorVisibility>();
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                sPlayerPos = player.transform.position;
                sSavedPlayer = true;
                Transform building = FindBuilding();
                Vector3 insidePos = building != null
                    ? building.position + new Vector3(4f, 0.05f, 4f)
                    : player.transform.position + Vector3.forward;
                if (vis != null && vis.interiorVolume != null)
                    insidePos = vis.interiorVolume.bounds.center + Vector3.down * 0.8f;
                player.transform.position = insidePos;
                sLog.AppendLine($"moved player to {insidePos} (was {sPlayerPos}) for 117 strip check");
            }
            if (vis != null)
            {
                vis.EditorSetInsideForTest(true);
                sLog.AppendLine("EditorSetInsideForTest(true)");
            }
            else
                sLog.AppendLine("WARN: no BuildingInteriorVisibility for strip check");
            sPhase = 4;
            sWait = 0;
            File.WriteAllText(Full(ReportPath), sLog.ToString() + "\nWAITING inside strip...\n");
            return;
        }

        RestoreCamAndFollow();
        File.WriteAllText(Full(ReportPath), sLog.ToString());
        Debug.Log("[FrameDebuggerWallDump] wrote " + Full(ReportPath) + " chars=" + sLog.Length);
        sPhase = 0;
        sSavedCam = false;
        sVerifyMode = false;
    }

    static void FinishInsideStripCapture()
    {
        var vis = UnityEngine.Object.FindAnyObjectByType<BuildingInteriorVisibility>();
        if (vis != null)
            sLog.AppendLine("inside dump:\n" + vis.DebugDumpStripState());
        if (sCam != null)
            CaptureCamera(sCam, "Temp/fd_wall_strip_inside.png");
        sLog.AppendLine("insideShot=Temp/fd_wall_strip_inside.png");
        if (vis != null)
        {
            vis.EditorSetInsideForTest(false);
            sLog.AppendLine("EditorSetInsideForTest(false) restored");
        }
        if (sSavedPlayer)
        {
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player != null)
                player.transform.position = sPlayerPos;
            sSavedPlayer = false;
            sLog.AppendLine($"restored player to {sPlayerPos}");
        }
        RestoreCamAndFollow();
        File.WriteAllText(Full(ReportPath), sLog.ToString());
        Debug.Log("[FrameDebuggerWallDump] verify done " + Full(ReportPath));
        sPhase = 0;
        sSavedCam = false;
        sVerifyMode = false;
    }

    static void ScanFdTypes(StringBuilder sb)
    {
        int n = 0;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            foreach (var t in types)
            {
                if (t == null || t.Name == null)
                    continue;
                if (t.Name.IndexOf("FrameDebugger", StringComparison.Ordinal) < 0)
                    continue;
                n++;
                sb.AppendLine($"TYPE {t.FullName} asm={asm.GetName().Name}");
                if (t.Name == "FrameDebuggerUtility" || t.Name == "FrameDebuggerInternal"
                    || t.Name == "FrameDebuggerWindow" || t.Name == "FrameDebuggerEvent"
                    || t.Name == "FrameDebuggerEventData")
                {
                    foreach (var p in t.GetProperties(Stat | Inst))
                        sb.AppendLine($"  prop {p.Name}:{p.PropertyType.Name}");
                    foreach (var f in t.GetFields(Stat | Inst))
                        sb.AppendLine($"  field {f.Name}:{f.FieldType.Name}");
                    foreach (var m in t.GetMethods(Stat | Inst))
                    {
                        if (m.IsSpecialName)
                            continue;
                        if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_"))
                            continue;
                        if (t.Name == "FrameDebuggerWindow" && m.Name != "EnableFrameDebugger"
                            && m.Name != "DisableFrameDebugger" && m.Name != "ChangeFrameEventLimit"
                            && m.Name != "SetEnabled")
                            continue;
                        sb.Append("  meth " + m.Name + "(");
                        var ps = m.GetParameters();
                        for (int i = 0; i < ps.Length; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(ps[i].ParameterType.Name);
                        }
                        sb.AppendLine(")");
                    }
                }
            }
        }
        sb.AppendLine($"frameDebuggerTypesFound={n}");
    }

    static Type FindT(string simpleOrFull)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = asm.GetType(simpleOrFull, false);
            if (t != null)
                return t;
            try
            {
                foreach (var x in asm.GetTypes())
                {
                    if (x != null && (x.Name == simpleOrFull || x.FullName == simpleOrFull))
                        return x;
                }
            }
            catch { }
        }
        return null;
    }

    static bool TrySetFd(bool on)
    {
        Type util = FindT("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility")
                    ?? FindT("FrameDebuggerUtility");
        if (util == null)
            return false;
        var m = util.GetMethod("SetEnabled", Stat, null, new[] { typeof(bool), typeof(int) }, null)
                ?? util.GetMethod("SetEnabled", Stat);
        if (m == null)
            return false;
        try
        {
            var ps = m.GetParameters();
            if (ps.Length == 2)
                m.Invoke(null, new object[] { on, 0 });
            else if (ps.Length == 1)
                m.Invoke(null, new object[] { on });
            else
                return false;
            return true;
        }
        catch (Exception e)
        {
            if (sLog != null)
                sLog.AppendLine("SetEnabled threw: " + (e.InnerException != null ? e.InnerException.Message : e.Message));
            return false;
        }
    }

    static int GetEventCount()
    {
        Type util = FindT("FrameDebuggerUtility") ?? FindT("UnityEditorInternal.FrameDebuggerUtility");
        if (util == null)
            return -1;
        var p = util.GetProperty("count", Stat);
        if (p != null)
        {
            try { return Convert.ToInt32(p.GetValue(null, null)); }
            catch { }
        }
        var f = util.GetField("count", Stat);
        if (f != null)
        {
            try { return Convert.ToInt32(f.GetValue(null)); }
            catch { }
        }
        return -1;
    }

    static bool SetLimit(int limit)
    {
        bool ok = false;
        Type util = FindT("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility")
                    ?? FindT("FrameDebuggerUtility");
        if (util != null)
        {
            var p = util.GetProperty("limit", Stat);
            if (p != null && p.CanWrite)
            {
                p.SetValue(null, limit, null);
                ok = true;
            }
        }
        Type winType = FindT("UnityEditor.FrameDebuggerWindow") ?? FindT("FrameDebuggerWindow");
        if (winType != null)
        {
            var m = winType.GetMethod("ChangeFrameEventLimit", Inst, null, new[] { typeof(int) }, null);
            foreach (var obj in Resources.FindObjectsOfTypeAll(winType))
            {
                if (obj == null || m == null)
                    continue;
                try
                {
                    m.Invoke(obj, new object[] { limit });
                    ok = true;
                    var w = obj as EditorWindow;
                    if (w != null)
                        w.Repaint();
                }
                catch { }
            }
        }
        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
        {
            if (w != null && w.GetType().Name == "GameView")
                w.Repaint();
        }
        return ok;
    }

    static void DumpEvents(StringBuilder sb)
    {
        Type util = FindT("FrameDebuggerUtility") ?? FindT("UnityEditorInternal.FrameDebuggerUtility");
        if (util == null)
        {
            sb.AppendLine("NO FrameDebuggerUtility");
            return;
        }

        Array events = null;
        var getEvents = util.GetMethod("GetFrameEvents", Stat);
        if (getEvents != null)
            events = getEvents.Invoke(null, null) as Array;

        int n = events != null ? events.Length : GetEventCount();
        sb.AppendLine($"eventCount={n}");

        var getData = util.GetMethod("GetFrameEventData", Stat);
        var getName = util.GetMethod("GetFrameEventInfoName", Stat);
        var getObj = util.GetMethod("GetFrameEventObject", Stat);
        var interesting = new StringBuilder();
        var all = new StringBuilder();
        all.AppendLine($"ALL n={n}");
        int hits = 0;
        int depthNormalCount = 0;
        int wallStripHits = 0;
        var depthNormalLines = new StringBuilder();
        int dumpN = Math.Min(n, 1200);
        for (int i = 0; i < dumpN; i++)
        {
            string name = "";
            if (getName != null)
            {
                try { name = getName.Invoke(null, new object[] { i }) as string ?? ""; }
                catch { }
            }
            string objName = "";
            if (getObj != null)
            {
                try
                {
                    var o = getObj.Invoke(null, new object[] { i }) as UnityEngine.Object;
                    if (o != null)
                        objName = o.name + "(" + o.GetType().Name + ")";
                }
                catch { }
            }
            string evDesc = events != null && i < events.Length ? DescribeObj(events.GetValue(i), 8) : "";
            string dataDesc = "";
            SetLimit(i + 1);
            if (getData != null)
                dataDesc = InvokeGetData(getData, i);
            string line = $"[{i}] {name} obj={objName} {evDesc} {dataDesc}";
            all.AppendLine(TrimLine(line, 400));
            string lower = (name + " " + objName + " " + dataDesc).ToLowerInvariant();
            if (lower.Contains("depthnormal") || lower.Contains("depth-normal"))
            {
                depthNormalCount++;
                depthNormalLines.AppendLine(TrimLine(line, 500));
            }
            if (lower.Contains("buildingwallstrip") || lower.Contains("beattheman/building"))
                wallStripHits++;
            if (IsInteresting(name + " " + objName + " " + dataDesc))
            {
                hits++;
                interesting.AppendLine(TrimLine(line, 900));
            }
        }
        sb.AppendLine($"interestingEvents={hits}/{n}");
        sb.AppendLine($"depthNormalPrepassEvents={depthNormalCount} buildingWallStripNameHits={wallStripHits}");
        sb.AppendLine("--- DepthNormal events ---");
        sb.Append(depthNormalLines);
        sb.AppendLine("--- interesting ---");
        sb.Append(interesting);
        File.WriteAllText(Full(AllPath), all.ToString());
        sb.AppendLine($"wrote {AllPath}");
    }

    static string InvokeGetData(MethodInfo getData, int i)
    {
        var ps = getData.GetParameters();
        try
        {
            object data = null;
            if (ps.Length == 2)
            {
                data = Activator.CreateInstance(ps[1].ParameterType);
                object[] args = { i, data };
                object ok = getData.Invoke(null, args);
                data = args[1];
                if (ok is bool b && !b)
                    return "GetFrameEventData=false";
            }
            else if (ps.Length == 1)
                data = getData.Invoke(null, new object[] { i });
            else
                return "GetFrameEventData unhandled sig";
            if (data == null)
                return "data=null";
            return DescribeEventData(data);
        }
        catch (Exception e)
        {
            return "GetFrameEventData:" + (e.InnerException != null ? e.InnerException.Message : e.Message);
        }
    }

    static string DescribeEventData(object data)
    {
        var sb = new StringBuilder();
        string[] keys =
        {
            "m_OriginalShaderName", "m_RealShaderName", "m_PassName", "m_PassLightMode",
            "shaderKeywords", "m_Mesh", "m_VertexCount", "m_IndexCount", "m_InstanceCount",
            "m_DrawCallCount", "m_BlendState", "m_RasterState", "m_DepthState",
            "m_RenderTargetName", "m_BatchBreakCause"
        };
        foreach (var name in keys)
        {
            var f = data.GetType().GetField(name, Inst);
            if (f == null)
                continue;
            object v = f.GetValue(data);
            if (SkipVal(v) && !(v is int || v is UnityEngine.Object))
                continue;
            if (v != null && v.GetType().Name.StartsWith("FrameDebugger") && !v.GetType().IsEnum)
                sb.Append($" {name}={{ {DescribeObj(v, 16)} }}");
            else
                sb.Append($" {name}={FmtVal(v)}");
        }
        return sb.ToString();
    }

    static void SampleWallPixels(StringBuilder sb)
    {
        int n = GetEventCount();
        if (n <= 0)
        {
            sb.AppendLine("SKIP pixel sample: no events");
            return;
        }

        sb.AppendLine("--- wall-pixel step ---");
        sb.AppendLine($"Wall_Left uv={sWallUv} Front uv={sFrontUv}");

        var samples = new List<int>();
        for (int i = 1; i <= n; i++)
        {
            if (i == 1 || i == n || i % Math.Max(1, n / 40) == 0)
                samples.Add(i);
        }
        // Also hit every event if small.
        if (n <= 120)
        {
            samples.Clear();
            for (int i = 1; i <= n; i++)
                samples.Add(i);
        }

        Color prevL = new Color(-1, -1, -1, 0);
        Color prevF = new Color(-1, -1, -1, 0);
        int firstLeftChange = -1;
        int firstFrontChange = -1;
        var deltas = new StringBuilder();

        foreach (int lim in samples)
        {
            if (!SetLimit(lim))
            {
                sb.AppendLine("SetLimit failed — cannot step Game view");
                break;
            }
            // Force game view repaint
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w != null && w.GetType().Name == "GameView")
                    w.Repaint();
            }
            Texture2D tex = GrabGamePixels();
            if (tex == null)
            {
                sb.AppendLine($"limit={lim} grab=null");
                continue;
            }
            Color left = SampleUv(tex, sWallUv);
            Color front = SampleUv(tex, sFrontUv);
            float dL = ColorDist(prevL, left);
            float dF = ColorDist(prevF, front);
            bool notable = dL > 0.04f || dF > 0.04f || lim == 1 || lim == n;
            if (notable)
            {
                deltas.AppendLine(
                    $"limit={lim}/{n} left={Fmt(left)} dL={dL:F3} front={Fmt(front)} dF={dF:F3} tex={tex.width}x{tex.height}");
                if (firstLeftChange < 0 && prevL.r >= 0 && dL > 0.08f)
                    firstLeftChange = lim;
                if (firstFrontChange < 0 && prevF.r >= 0 && dF > 0.08f)
                    firstFrontChange = lim;
            }
            if (lim == 1 || lim == n || lim == firstLeftChange)
                SaveTex(tex, $"Temp/fd_wall_limit_{lim:D3}.png");
            prevL = left;
            prevF = front;
            UnityEngine.Object.DestroyImmediate(tex);
        }

        SetLimit(n);
        sb.AppendLine($"firstLeftPixelChangeAtLimit={firstLeftChange}");
        sb.AppendLine($"firstFrontPixelChangeAtLimit={firstFrontChange}");
        sb.Append(deltas);
    }

    static Texture2D GrabGamePixels()
    {
        Type gvType = FindT("UnityEditor.GameView") ?? FindT("GameView");
        if (gvType != null)
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll(gvType))
            {
                var w = obj as EditorWindow;
                if (w == null)
                    continue;
                foreach (var f in gvType.GetFields(Inst))
                {
                    if (!typeof(RenderTexture).IsAssignableFrom(f.FieldType))
                        continue;
                    var rt = f.GetValue(w) as RenderTexture;
                    if (rt != null && rt.width > 32 && rt.height > 32)
                        return ReadRt(rt);
                }
                foreach (var p in gvType.GetProperties(Inst))
                {
                    if (p.GetIndexParameters().Length > 0)
                        continue;
                    if (!typeof(RenderTexture).IsAssignableFrom(p.PropertyType))
                        continue;
                    RenderTexture rt;
                    try { rt = p.GetValue(w, null) as RenderTexture; }
                    catch { continue; }
                    if (rt != null && rt.width > 32 && rt.height > 32)
                        return ReadRt(rt);
                }
            }
        }

        try
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex != null && tex.width > 32)
                return tex;
        }
        catch { }

        if (sCam != null)
        {
            int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = sCam.targetTexture;
            sCam.targetTexture = rt;
            sCam.Render();
            var tex = ReadRt(rt);
            sCam.targetTexture = prev;
            UnityEngine.Object.DestroyImmediate(rt);
            return tex;
        }
        return null;
    }

    static Texture2D ReadRt(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        return tex;
    }

    static Color SampleUv(Texture2D tex, Vector2 uv)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (tex.width - 1)), 0, tex.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (tex.height - 1)), 0, tex.height - 1);
        return tex.GetPixel(x, y);
    }

    static float ColorDist(Color a, Color b)
    {
        if (a.r < 0)
            return 0f;
        Vector3 d = new Vector3(a.r - b.r, a.g - b.g, a.b - b.b);
        return d.magnitude;
    }

    static string Fmt(Color c) => $"({c.r:F3},{c.g:F3},{c.b:F3})";

    static void SaveTex(Texture2D tex, string relative)
    {
        try { File.WriteAllBytes(Full(relative), tex.EncodeToPNG()); }
        catch { }
    }

    static void CaptureCamera(Camera cam, string relative)
    {
        int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var tex = ReadRt(rt);
        cam.targetTexture = prev;
        UnityEngine.Object.DestroyImmediate(rt);
        File.WriteAllBytes(Full(relative), tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static string DescribeObj(object obj, int maxFields)
    {
        if (obj == null)
            return "null";
        var sb = new StringBuilder();
        sb.Append(obj.GetType().Name);
        int n = 0;
        foreach (var f in obj.GetType().GetFields(Inst))
        {
            if (n >= maxFields)
                break;
            object v;
            try { v = f.GetValue(obj); }
            catch { continue; }
            if (SkipVal(v))
                continue;
            sb.Append(' ');
            sb.Append(f.Name);
            sb.Append('=');
            sb.Append(FmtVal(v));
            n++;
        }
        foreach (var p in obj.GetType().GetProperties(Inst))
        {
            if (n >= maxFields || p.GetIndexParameters().Length > 0)
                continue;
            object v;
            try { v = p.GetValue(obj, null); }
            catch { continue; }
            if (SkipVal(v))
                continue;
            sb.Append(' ');
            sb.Append(p.Name);
            sb.Append('=');
            sb.Append(FmtVal(v));
            n++;
        }
        return sb.ToString();
    }

    static bool SkipVal(object v)
    {
        if (v == null)
            return true;
        if (v is UnityEngine.Object u)
            return u == null;
        string s = v.ToString();
        return string.IsNullOrEmpty(s) || s == "0" || s == "False" || s == "None";
    }

    static string FmtVal(object v)
    {
        if (v is UnityEngine.Object u)
            return u != null ? $"{u.name}({u.GetType().Name})" : "null";
        if (v is Array arr)
            return $"[{arr.Length}]";
        string s = v.ToString();
        if (s.Length > 160)
            s = s.Substring(0, 160) + "…";
        return s;
    }

    static bool IsInteresting(string line)
    {
        string L = line.ToLowerInvariant();
        return L.Contains("wall") || L.Contains("building") || L.Contains("ssao")
            || L.Contains("ambient") || L.Contains("occlusion") || L.Contains("strip")
            || L.Contains("unlit") || L.Contains("forward") || L.Contains("defaultbuilding")
            || L.Contains("blend") || L.Contains("transparent") || L.Contains("ceiling")
            || L.Contains("roof") || L.Contains("draw") || L.Contains("inner")
            || L.Contains("cull") || L.Contains("zwrite") || L.Contains("depth");
    }

    static string TrimLine(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

    static void DumpLiveWalls(StringBuilder sb, Transform building)
    {
        foreach (var r in building.GetComponentsInChildren<Renderer>())
        {
            if (r == null)
                continue;
            bool keep = r.name.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0
                        || r.name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0
                        || r.name.IndexOf("Roof", StringComparison.OrdinalIgnoreCase) >= 0
                        || r.name.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!keep)
                continue;
            sb.AppendLine($"live {GetPath(r.transform)} enabled={r.enabled} {MatInfo(r)} aabb={r.bounds.min}->{r.bounds.max}");
        }
    }

    static void DumpShaderPasses(StringBuilder sb, Renderer wall)
    {
        if (wall == null || wall.sharedMaterial == null || wall.sharedMaterial.shader == null)
            return;
        var mat = wall.sharedMaterial;
        var sh = mat.shader;
        sb.AppendLine($"shaderPasses name={sh.name} passCount={sh.passCount}");
        var lightMode = new UnityEngine.Rendering.ShaderTagId("LightMode");
        for (int p = 0; p < sh.passCount; p++)
        {
            var lm = sh.FindPassTagValue(p, lightMode);
            sb.AppendLine($"  pass[{p}] lightMode={lm.name}");
        }
        foreach (string passName in new[] { "ForwardLit", "ForwardLitInnerCap", "ShadowCaster", "DepthOnly", "DepthNormals" })
        {
            try
            {
                sb.AppendLine($"  GetShaderPassEnabled({passName})={mat.GetShaderPassEnabled(passName)}");
            }
            catch (Exception e)
            {
                sb.AppendLine($"  GetShaderPassEnabled({passName}) {e.Message}");
            }
        }
    }

    static string MatInfo(Renderer r)
    {
        var m = r.sharedMaterial;
        if (m == null)
            return "mat=null";
        string kw = "";
        try { kw = string.Join(",", m.enabledKeywords != null
            ? Array.ConvertAll(m.enabledKeywords, k => k.name) : Array.Empty<string>()); }
        catch { }
        string shader = m.shader != null ? m.shader.name : "null";
        return $"mat={m.name} shader={shader} queue={m.renderQueue} kw=[{kw}] cull={m.GetFloat("_Cull")} strip={m.GetFloat("_StripEnabled")}";
    }

    static Vector3 OuterFaceSample(Bounds b, bool preferMinX)
    {
        if (preferMinX)
            return new Vector3(b.min.x, b.min.y + b.size.y * 0.45f, b.min.z + b.size.z * 0.5f);
        return new Vector3(b.min.x + b.size.x * 0.5f, b.min.y + b.size.y * 0.45f, b.min.z);
    }

    static Renderer FindRenderer(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t.GetComponent<Renderer>();
        }
        return null;
    }

    static void WriteRestoreFile()
    {
        File.WriteAllText(Full(RestorePath),
            $"{sCamPos.x},{sCamPos.y},{sCamPos.z}|{sCamRot.x},{sCamRot.y},{sCamRot.z},{sCamRot.w}|{sOrtho}|{(sFollowWasOn ? 1 : 0)}");
    }

    static void TryRestoreFromDisk()
    {
        string p = Full(RestorePath);
        if (!File.Exists(p))
            return;
        RestoreCamAndFollow();
    }

    static void RestoreCamAndFollow()
    {
        Camera cam = sCam != null ? sCam : Camera.main ?? UnityEngine.Object.FindAnyObjectByType<Camera>();
        string p = Full(RestorePath);
        Vector3 pos = sCamPos;
        Quaternion rot = sCamRot;
        float ortho = sOrtho;
        bool followOn = sFollowWasOn;
        if (File.Exists(p))
        {
            try
            {
                string[] parts = File.ReadAllText(p).Split('|');
                var xyz = parts[0].Split(',');
                pos = new Vector3(float.Parse(xyz[0]), float.Parse(xyz[1]), float.Parse(xyz[2]));
                var q = parts[1].Split(',');
                rot = new Quaternion(float.Parse(q[0]), float.Parse(q[1]), float.Parse(q[2]), float.Parse(q[3]));
                ortho = float.Parse(parts[2]);
                followOn = parts[3].Trim() == "1";
            }
            catch { }
        }
        if (cam != null && sSavedCam)
        {
            cam.transform.position = pos;
            cam.transform.rotation = rot;
            if (ortho > 0f)
                cam.orthographicSize = ortho;
            foreach (var c in cam.GetComponents<Behaviour>())
            {
                if (c.GetType().Name == "CameraFollow")
                    c.enabled = followOn;
            }
        }
        try { if (File.Exists(p)) File.Delete(p); } catch { }
        if (sLog != null)
            sLog.AppendLine("restored Camera.main + CameraFollow");
    }

    static Transform FindBuilding()
    {
        foreach (string n in new[] { "DefaultBuilding_117", "DefaultBuilding" })
        {
            var go = GameObject.Find(n);
            if (go != null)
                return go.transform;
        }
        return null;
    }

    static Bounds Encapsulate(Transform root)
    {
        Bounds? b = null;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (b == null) b = r.bounds;
            else
            {
                Bounds bb = b.Value;
                bb.Encapsulate(r.bounds);
                b = bb;
            }
        }
        return b ?? new Bounds(root.position, Vector3.one);
    }

    static string GetPath(Transform t)
    {
        var sb = new StringBuilder();
        while (t != null)
        {
            if (sb.Length > 0)
                sb.Insert(0, "/");
            sb.Insert(0, t.name);
            t = t.parent;
        }
        return sb.ToString();
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
