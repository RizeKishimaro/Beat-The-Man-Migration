using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

/// <summary>
/// Ticket 160 blotch diagnostic — Frame Debugger SSAO isolation.
/// Inspection only: no shader/material/scene saves. Restores CameraFollow.
/// Flag: Temp/fd_blotch.flag
/// </summary>
[InitializeOnLoad]
public static class FrameDebuggerBlotchDump
{
    const string FlagPath = "Temp/fd_blotch.flag";
    const string ReportPath = "Temp/fd_blotch.txt";
    const string AllPath = "Temp/fd_blotch_events.txt";
    const string RestorePath = "Temp/fd_blotch_restore.txt";
    const string ShotDir = "Temp/fd_blotch";
    const string PendingKey = "FdBlotchDump.pending";
    const string EnteredPlayKey = "FdBlotchDump.enteredPlay";
    const string ParentName = "Bake160_BlotchDiag_DO_NOT_SAVE";
    const string ChairFbx = "Assets/Objects/Meshy_Chair_Unity/Meshy_Chair_Unity.fbx";
    const string ChairMat = "Assets/Objects/Meshy_Chair_Unity/Materials/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture.mat";
    const string CoolerFbx =
        "Assets/Objects/Pickable/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture_fbx/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture.fbx";
    const string CoolerMat =
        "Assets/Objects/Pickable/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture_fbx/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture_fbx.mat";

    const BindingFlags Stat = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    static int sPhase;
    static int sWait;
    static StringBuilder sLog;
    static Camera sCam;
    static bool sWasPaused;
    static List<int> sCaptureLimits;
    static int sCapIndex;
    static UnityEngine.Rendering.Universal.ScriptableRendererFeature sSsaoFeature;
    static bool sSsaoWasOn;

    static FrameDebuggerBlotchDump()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    static void OnPlay(PlayModeStateChange c)
    {
        if (c == PlayModeStateChange.ExitingPlayMode || c == PlayModeStateChange.EnteredEditMode)
            RestoreCamAndFollow();
    }

    [MenuItem("Tools/Objects/160 Frame Debugger Blotch Dump")]
    public static void Menu()
    {
        File.WriteAllText(Full(FlagPath), "run\n");
    }

    static void Poll()
    {
        if (sPhase == 1)
        {
            sWait++;
            KeepFollowOff();
            if (sWait == 40)
            {
                SaveGrab("00_confirm.png", "confirm SSAO-on before toggle");
                sSsaoFeature = FindSsaoFeature();
                if (sSsaoFeature != null)
                {
                    sSsaoWasOn = sSsaoFeature.isActive;
                    sSsaoFeature.SetActive(false);
                    sLog.AppendLine("SSAO forced OFF for live compare");
                }
                else
                    sLog.AppendLine("SSAO feature NOT found");
                File.WriteAllText(Full(ReportPath), sLog.ToString());
            }
            if (sWait == 80)
            {
                SaveGrab("90_ssao_off.png", "ssao_off live");
                if (sSsaoFeature != null)
                {
                    sSsaoFeature.SetActive(sSsaoWasOn);
                    sLog.AppendLine("SSAO restored isActive=" + sSsaoWasOn);
                }
                File.WriteAllText(Full(ReportPath), sLog.ToString());
            }
            if (sWait >= 120)
                EnableFd();
            return;
        }
        if (sPhase == 2)
        {
            KeepFollowOff();
            sWait++;
            if ((FdEnabled() && GetEventCount() > 0) || sWait >= 90)
                StartCaptures();
            return;
        }
        if (sPhase == 3)
        {
            KeepFollowOff();
            sWait++;
            if (sWait < 8)
                return;
            CaptureOne();
            return;
        }
        if (sPhase == 4)
        {
            TickSsaoCompare();
            return;
        }

        if (sPhase != 0)
            return;
        if (EditorApplication.isCompiling)
            return;

        bool flag = File.Exists(Full(FlagPath));
        bool pending = SessionState.GetBool(PendingKey, false);
        if (!flag && !pending)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;

        if (!EditorApplication.isPlaying)
        {
            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(EnteredPlayKey, true);
            EditorApplication.isPlaying = true;
            return;
        }

        if (flag)
            File.Delete(Full(FlagPath));
        SessionState.SetBool(PendingKey, false);
        Begin();
    }

    static void Begin()
    {
        sLog = new StringBuilder();
        sLog.AppendLine("=== Frame Debugger blotch dump (inspection only) ===");
        sLog.AppendLine("time=" + DateTime.UtcNow.ToString("o"));
        sLog.AppendLine("isPlaying=" + EditorApplication.isPlaying);
        sLog.AppendLine("scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);

        Directory.CreateDirectory(Full(ShotDir));
        EnsureDiagObjects(sLog);
        DumpMaterials(sLog);
        DumpSsaoSettings(sLog);

        sCam = Camera.main;
        if (sCam == null)
        {
            sLog.AppendLine("FAIL no Camera.main");
            Finish(false);
            return;
        }

        Behaviour follow = FindFollow(sCam);
        bool followWasOn = follow != null && follow.enabled;
        if (follow != null)
            follow.enabled = false;
        File.WriteAllText(Full(RestorePath),
            sCam.transform.position.x + "," + sCam.transform.position.y + "," + sCam.transform.position.z + "|"
            + sCam.transform.rotation.x + "," + sCam.transform.rotation.y + ","
            + sCam.transform.rotation.z + "," + sCam.transform.rotation.w + "|"
            + (sCam.orthographic ? sCam.orthographicSize : 6f) + "|"
            + (followWasOn ? 1 : 0));

        GameObject parent = GameObject.Find(ParentName);
        Bounds b = CollectBounds(parent);
        Vector3 look = b.center;
        sCam.orthographic = true;
        sCam.orthographicSize = 2.2f;
        sCam.transform.rotation = Quaternion.LookRotation(new Vector3(8f, -7f, 8f).normalized);
        sCam.transform.position = look + new Vector3(-5f, 4.4f, -5f);
        sLog.AppendLine("framed cam=" + sCam.transform.position + " look=" + look + " ortho=" + sCam.orthographicSize);

        EditorApplication.ExecuteMenuItem("Window/General/Game");

        sPhase = 1;
        sWait = 0;
        File.WriteAllText(Full(ReportPath), sLog.ToString() + "\nWAITING pose...\n");
    }

    static void EnableFd()
    {
        GrabNamed("01_ssao_global.png", new[]
        {
            "_ScreenSpaceOcclusionTexture",
            "_ScreenSpaceOcclusion",
            "SSAO_OcclusionTexture"
        });
        GrabNamed("02_normals_global.png", new[]
        {
            "_CameraNormalsTexture",
            "_CameraNormals"
        });
        GrabNamed("03_depth_global.png", new[]
        {
            "_CameraDepthTexture",
            "_CameraDepth"
        });

        TrySetFd(false);
        sWasPaused = EditorApplication.isPaused;
        if (EditorApplication.isPlaying && !EditorApplication.isPaused)
        {
            EditorApplication.isPaused = true;
            sLog.AppendLine("paused Play (required for Frame Debugger)");
        }

        bool opened = EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");
        sLog.AppendLine("opened FD menu=" + opened);
        Type winType = FindT("UnityEditor.FrameDebuggerWindow") ?? FindT("FrameDebuggerWindow");
        if (winType != null)
        {
            var win = EditorWindow.GetWindow(winType);
            var enableM = winType.GetMethod("EnableFrameDebugger", Inst);
            if (enableM != null)
            {
                try { enableM.Invoke(win, null); sLog.AppendLine("EnableFrameDebugger invoked"); }
                catch (Exception e) { sLog.AppendLine("EnableFrameDebugger: " + e.Message); }
            }
        }
        sLog.AppendLine("SetEnabled=" + TrySetFd(true));
        sLog.AppendLine("FrameDebugger.enabled=" + FdEnabled() + " count=" + GetEventCount());
        sPhase = 2;
        sWait = 0;
        File.WriteAllText(Full(ReportPath), sLog.ToString() + "\nWAITING FD events...\n");
    }

    static void StartCaptures()
    {
        sLog.AppendLine("FD on=" + FdEnabled() + " count=" + GetEventCount() + " wait=" + sWait);
        DumpEvents(sLog);

        int n = GetEventCount();
        var want = new HashSet<int>();
        string all = File.Exists(Full(AllPath)) ? File.ReadAllText(Full(AllPath)) : "";
        foreach (string line in all.Split('\n'))
        {
            if (line.Length < 4 || line[0] != '[')
                continue;
            int rb = line.IndexOf(']');
            int idx;
            if (rb < 2 || !int.TryParse(line.Substring(1, rb - 1), out idx))
                continue;
            string L = line.ToLowerInvariant();
            bool hit = L.Contains("ssao") || L.Contains("occlusion") || L.Contains("ambient")
                       || L.Contains("depthnormal") || L.Contains("depth-normal")
                       || L.Contains("_screenspaceocclusion") || L.Contains("screen space ao")
                       || L.Contains("screespaceao");
            if (hit)
            {
                want.Add(Mathf.Clamp(idx + 1, 1, Mathf.Max(1, n)));
                sLog.AppendLine("CAPTURE_CANDIDATE limit=" + (idx + 1) + " " + Trim(line, 200));
            }
        }
        want.Add(1);
        if (n > 0)
            want.Add(n);
        sCaptureLimits = new List<int>(want);
        sCaptureLimits.Sort();
        sLog.AppendLine("captureLimits=" + string.Join(",", sCaptureLimits));
        File.WriteAllText(Full(ReportPath), sLog.ToString());
        sCapIndex = 0;
        sPhase = 3;
        sWait = 99;
        DumpEventDatas(sLog);
        if (sCaptureLimits.Count > 0)
            SetLimit(sCaptureLimits[0]);
        else
            Finish(true);
    }

    static void CaptureOne()
    {
        if (sCaptureLimits == null || sCapIndex >= sCaptureLimits.Count)
        {
            Finish(true);
            return;
        }
        int lim = sCaptureLimits[sCapIndex];
        SetLimit(lim);
        Texture2D tex = GrabGamePixels();
        string shot = ShotDir + "/limit_" + lim.ToString("D3") + ".png";
        if (tex != null)
        {
            File.WriteAllBytes(Full(shot), tex.EncodeToPNG());
            sLog.AppendLine("SHOT limit=" + lim + "/" + GetEventCount() + " " + shot
                + " " + tex.width + "x" + tex.height);
            UnityEngine.Object.DestroyImmediate(tex);
        }
        else
            sLog.AppendLine("SHOT limit=" + lim + " grab=null");

        CaptureFdWindow("fdwin_limit_" + lim.ToString("D3") + ".png");
        BlitFdPreviewTextures("fdpreview_limit_" + lim.ToString("D3"));
        string all = File.Exists(Full(AllPath)) ? File.ReadAllText(Full(AllPath)) : "";
        int ev = lim - 1;
        string needle = "[" + ev + "]";
        foreach (string line in all.Split('\n'))
        {
            if (line.StartsWith(needle, StringComparison.Ordinal)
                && line.ToLowerInvariant().Contains("ssao"))
            {
                File.WriteAllText(Full(ShotDir + "/READY_SSAO.txt"),
                    "limit=" + lim + " " + line + "\n");
                break;
            }
        }

        File.WriteAllText(Full(ReportPath), sLog.ToString());
        sCapIndex++;
        sWait = 0;
        if (sCapIndex < sCaptureLimits.Count)
            SetLimit(sCaptureLimits[sCapIndex]);
        else
            Finish(true);
    }

    static void Finish(bool ok)
    {
        try
        {
            if (sSsaoFeature != null)
                sSsaoFeature.SetActive(sSsaoWasOn);
            TrySetFd(false);
            if (!sWasPaused)
                EditorApplication.isPaused = false;
        }
        catch { }
        RestoreCamAndFollow();
        sLog.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        File.WriteAllText(Full(ReportPath), sLog.ToString());
        Debug.Log("[FrameDebuggerBlotchDump]\n" + sLog);
        sPhase = 0;
        bool weEntered = SessionState.GetBool(EnteredPlayKey, false);
        SessionState.SetBool(EnteredPlayKey, false);
        SessionState.SetBool(PendingKey, false);
        if (weEntered && EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static void EnsureDiagObjects(StringBuilder sb)
    {
        GameObject old = GameObject.Find(ParentName);
        if (old != null)
            UnityEngine.Object.DestroyImmediate(old);
        var parent = new GameObject(ParentName);
        Vector3 origin = new Vector3(22f, 0f, 4f);
        GameObject room = GameObject.Find("Bake160_CompareRoom");
        if (room != null)
            origin = room.transform.position;
        else
        {
            GameObject b117 = GameObject.Find("DefaultBuilding_117");
            if (b117 != null)
                origin = b117.transform.position;
        }
        Spawn(parent.transform, ChairFbx, ChairMat, origin + new Vector3(-0.7f, 0.05f, 2.0f), "DIAG_SPIKE_CHAIR", sb);
        Spawn(parent.transform, CoolerFbx, CoolerMat, origin + new Vector3(0.7f, 0.05f, 2.0f), "DIAG_COOLER", sb);
        SitOnFloor(parent, origin.y + 0.05f);
        sb.AppendLine("spawned diagnostic chair+cooler at origin=" + origin + " (unsaved)");
    }

    static void Spawn(Transform parent, string fbx, string matPath, Vector3 pos, string name, StringBuilder sb)
    {
        GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (src == null)
        {
            sb.AppendLine("FAIL missing " + fbx);
            return;
        }
        GameObject go = (GameObject)UnityEngine.Object.Instantiate(src);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 35f, 0f));
        go.transform.localScale = Vector3.one * 1.4f;
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat != null)
        {
            var rends = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < rends.Length; i++)
                rends[i].sharedMaterial = mat;
        }
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        sb.AppendLine("spawn " + name + " mat=" + (mat != null ? mat.name : "null")
            + " shader=" + (mat != null && mat.shader != null ? mat.shader.name : "null"));
    }

    static void SitOnFloor(GameObject parent, float floorTop)
    {
        for (int i = 0; i < parent.transform.childCount; i++)
        {
            Transform t = parent.transform.GetChild(i);
            Bounds b = CollectBounds(t.gameObject);
            Vector3 p = t.position;
            p.y += floorTop - b.min.y;
            t.position = p;
        }
    }

    static void DumpMaterials(StringBuilder sb)
    {
        string[] paths = { ChairMat, CoolerMat };
        var lightMode = new ShaderTagId("LightMode");
        for (int i = 0; i < paths.Length; i++)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
            if (mat == null || mat.shader == null)
            {
                sb.AppendLine("mat MISSING " + paths[i]);
                continue;
            }
            var sh = mat.shader;
            sb.AppendLine("MAT " + paths[i]);
            sb.AppendLine("  shader=" + sh.name + " passCount=" + sh.passCount);
            bool hasDn = false;
            for (int p = 0; p < sh.passCount; p++)
            {
                string lm = sh.FindPassTagValue(p, lightMode).name;
                sb.AppendLine("  pass[" + p + "] LightMode=" + lm);
                if (lm == "DepthNormals" || lm == "DepthNormalsOnly")
                    hasDn = true;
            }
            try { sb.AppendLine("  GetShaderPassEnabled(DepthNormals)=" + mat.GetShaderPassEnabled("DepthNormals")); }
            catch (Exception e) { sb.AppendLine("  GetShaderPassEnabled: " + e.Message); }
            sb.AppendLine("  HAS_DepthNormals_LightMode=" + hasDn);
            sb.AppendLine("  keywords=" + string.Join(",", mat.shaderKeywords));
            sb.AppendLine("  occlusionMap=" + (mat.HasProperty("_OcclusionMap") && mat.GetTexture("_OcclusionMap") != null));
            sb.AppendLine("  bumpMap=" + (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") != null));
            sb.AppendLine("  metallicGloss=" + (mat.HasProperty("_MetallicGlossMap") && mat.GetTexture("_MetallicGlossMap") != null));
        }
    }

    static void DumpSsaoSettings(StringBuilder sb)
    {
        string yaml = File.ReadAllText(Full("Assets/Settings/PC_Renderer.asset"));
        foreach (string key in new[] { "AOMethod:", "Source:", "NormalSamples:", "AfterOpaque:", "m_Active:", "Intensity:" })
        {
            int at = yaml.IndexOf(key, StringComparison.Ordinal);
            if (at >= 0)
            {
                int end = yaml.IndexOf('\n', at);
                sb.AppendLine("PC_Renderer " + yaml.Substring(at, Math.Min(80, end - at)).Trim());
            }
        }
        sb.AppendLine("PC_Renderer SSAO Source 1 = DepthNormals (URP ScreenSpaceAmbientOcclusion.DepthSource)");
    }

    static void DumpEvents(StringBuilder sb)
    {
        Type util = FindT("FrameDebuggerUtility") ?? FindT("UnityEditorInternal.FrameDebuggerUtility");
        if (util == null)
        {
            sb.AppendLine("NO FrameDebuggerUtility");
            return;
        }
        var getName = util.GetMethod("GetFrameEventInfoName", Stat);
        int n = GetEventCount();
        sb.AppendLine("eventCount=" + n);
        var all = new StringBuilder();
        var interesting = new StringBuilder();
        int dumpN = Math.Min(n, 900);
        for (int i = 0; i < dumpN; i++)
        {
            string name = "";
            if (getName != null)
            {
                try { name = getName.Invoke(null, new object[] { i }) as string ?? ""; }
                catch { }
            }
            string line = "[" + i + "] " + name;
            all.AppendLine(line);
            string L = name.ToLowerInvariant();
            if (L.Contains("ssao") || L.Contains("occlusion") || L.Contains("ambient")
                || L.Contains("depthnormal") || L.Contains("depth") || L.Contains("forward")
                || L.Contains("draw") || L.Contains("gbuffer"))
                interesting.AppendLine(line);
        }
        File.WriteAllText(Full(AllPath), all.ToString());
        sb.AppendLine("--- interesting events ---");
        sb.Append(interesting);
        sb.AppendLine("wrote " + AllPath);
    }

    static void KeepFollowOff()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        Behaviour f = FindFollow(cam);
        if (f != null && f.enabled)
            f.enabled = false;
    }

    static void SaveGrab(string file, string label)
    {
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        Texture2D tex = GrabGamePixels();
        try
        {
            var sc = ScreenCapture.CaptureScreenshotAsTexture();
            if (sc != null && sc.width > 32)
            {
                if (tex == null || sc.width * sc.height >= tex.width * tex.height)
                {
                    if (tex != null)
                        UnityEngine.Object.DestroyImmediate(tex);
                    tex = sc;
                }
                else
                    UnityEngine.Object.DestroyImmediate(sc);
            }
        }
        catch { }
        if (tex == null)
        {
            sLog.AppendLine(label + " grab=null");
            return;
        }
        File.WriteAllBytes(Full(ShotDir + "/" + file), tex.EncodeToPNG());
        sLog.AppendLine(label + "=" + ShotDir + "/" + file + " " + tex.width + "x" + tex.height);
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static void GrabNamed(string file, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            Texture t = Shader.GetGlobalTexture(names[i]);
            if (t == null)
            {
                sLog.AppendLine("global " + names[i] + "=null");
                continue;
            }
            sLog.AppendLine("global " + names[i] + "=" + t.width + "x" + t.height + " " + t.GetType().Name);
            Texture2D tex = BlitToTex(t);
            if (tex == null)
                continue;
            File.WriteAllBytes(Full(ShotDir + "/" + file), tex.EncodeToPNG());
            sLog.AppendLine("wrote " + ShotDir + "/" + file + " from " + names[i]);
            UnityEngine.Object.DestroyImmediate(tex);
            return;
        }
    }

    static Texture2D BlitToTex(Texture src)
    {
        int w = Mathf.Clamp(src.width, 16, 2048);
        int h = Mathf.Clamp(src.height, 16, 2048);
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        Texture2D tex = ReadRt(rt);
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    static void DumpEventDatas(StringBuilder sb)
    {
        Type util = FindT("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility")
                    ?? FindT("FrameDebuggerUtility");
        Type dataType = FindT("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData")
                        ?? FindT("FrameDebuggerEventData");
        if (util == null || dataType == null)
        {
            sb.AppendLine("NO GetFrameEventData types util=" + (util != null) + " data=" + (dataType != null));
            return;
        }
        var m = util.GetMethod("GetFrameEventData", Stat);
        if (m == null)
        {
            sb.AppendLine("NO GetFrameEventData method");
            return;
        }
        sb.AppendLine("--- event data fields for SSAO/DepthNormals ---");
        int n = GetEventCount();
        string all = File.Exists(Full(AllPath)) ? File.ReadAllText(Full(AllPath)) : "";
        var want = new List<int>();
        foreach (string line in all.Split('\n'))
        {
            if (line.Length < 4 || line[0] != '[')
                continue;
            int rb = line.IndexOf(']');
            int idx;
            if (rb < 2 || !int.TryParse(line.Substring(1, rb - 1), out idx))
                continue;
            string L = line.ToLowerInvariant();
            if (L.Contains("ssao") || L.Contains("depthnormal"))
                want.Add(idx);
        }
        var ps = m.GetParameters();
        for (int w = 0; w < want.Count; w++)
        {
            int i = want[w];
            if (i < 0 || i >= n)
                continue;
            object data = null;
            try
            {
                if (ps.Length == 2)
                {
                    data = Activator.CreateInstance(dataType);
                    var args = new object[] { i, data };
                    m.Invoke(null, args);
                    data = args[1];
                }
                else
                    data = m.Invoke(null, new object[] { i });
            }
            catch (Exception e)
            {
                sb.AppendLine("eventData[" + i + "] FAIL " + e.Message);
                continue;
            }
            if (data == null)
            {
                sb.AppendLine("eventData[" + i + "]=null");
                continue;
            }
            sb.Append("eventData[" + i + "]");
            foreach (var f in data.GetType().GetFields(Inst))
            {
                object v;
                try { v = f.GetValue(data); }
                catch { continue; }
                if (v == null)
                    continue;
                string vs = v is UnityEngine.Object u && u != null
                    ? u.name + "(" + u.GetType().Name + " " + DescribeTex(u) + ")"
                    : v.ToString();
                if (vs.Length > 120)
                    vs = vs.Substring(0, 120) + "…";
                if (vs == "0" || vs == "False" || vs == "None" || vs == "")
                    continue;
                sb.Append(" " + f.Name + "=" + vs);
            }
            sb.AppendLine();
        }
    }

    static string DescribeTex(UnityEngine.Object u)
    {
        var t = u as Texture;
        if (t == null)
            return "";
        return t.width + "x" + t.height;
    }

    static void CaptureFdWindow(string file)
    {
        Type winType = FindT("UnityEditor.FrameDebuggerWindow") ?? FindT("FrameDebuggerWindow");
        if (winType == null)
        {
            sLog.AppendLine("CaptureFdWindow no type");
            return;
        }
        EditorWindow win = null;
        foreach (var obj in Resources.FindObjectsOfTypeAll(winType))
        {
            win = obj as EditorWindow;
            if (win != null)
                break;
        }
        if (win == null)
        {
            sLog.AppendLine("CaptureFdWindow no window");
            return;
        }
        win.Focus();
        win.Repaint();
        Rect pos = win.position;
        int w = Mathf.Max(8, Mathf.RoundToInt(pos.width));
        int h = Mathf.Max(8, Mathf.RoundToInt(pos.height));
        try
        {
            Type utilType = FindT("UnityEditorInternal.InternalEditorUtility") ?? FindT("InternalEditorUtility");
            if (utilType == null)
            {
                sLog.AppendLine("InternalEditorUtility missing");
                return;
            }
            var read = utilType.GetMethod(
                "ReadScreenPixel", Stat, null, new[] { typeof(Vector2), typeof(int), typeof(int) }, null);
            if (read == null)
            {
                sLog.AppendLine("ReadScreenPixel missing");
                return;
            }
            var cols = read.Invoke(null, new object[] { new Vector2(pos.x, pos.y), w, h }) as Color[];
            if (cols == null || cols.Length != w * h)
            {
                sLog.AppendLine("ReadScreenPixel empty w=" + w + " h=" + h + " n=" + (cols != null ? cols.Length : -1));
                return;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels(cols);
            tex.Apply();
            File.WriteAllBytes(Full(ShotDir + "/" + file), tex.EncodeToPNG());
            sLog.AppendLine("FDWINDOW " + file + " " + w + "x" + h + " pos=" + pos);
            UnityEngine.Object.DestroyImmediate(tex);
        }
        catch (Exception e)
        {
            sLog.AppendLine("CaptureFdWindow FAIL " + e.Message);
        }
    }

    static void BlitFdPreviewTextures(string prefix)
    {
        Type winType = FindT("UnityEditor.FrameDebuggerWindow") ?? FindT("FrameDebuggerWindow");
        if (winType == null)
            return;
        EditorWindow win = null;
        foreach (var obj in Resources.FindObjectsOfTypeAll(winType))
        {
            win = obj as EditorWindow;
            if (win != null)
                break;
        }
        if (win == null)
            return;

        int n = 0;
        foreach (var f in winType.GetFields(Inst))
        {
            if (!typeof(Texture).IsAssignableFrom(f.FieldType))
                continue;
            var t = f.GetValue(win) as Texture;
            if (t == null || t.width < 8 || t.height < 8)
                continue;
            Texture2D tex = BlitToTex(t);
            if (tex == null)
                continue;
            string file = ShotDir + "/" + prefix + "_" + f.Name + ".png";
            File.WriteAllBytes(Full(file), tex.EncodeToPNG());
            sLog.AppendLine("FDFIELD " + f.Name + " " + t.width + "x" + t.height + " -> " + file);
            UnityEngine.Object.DestroyImmediate(tex);
            n++;
        }
        try
        {
            if (win.rootVisualElement != null)
                WalkImages(win.rootVisualElement, prefix, ref n, 0);
        }
        catch (Exception e) { sLog.AppendLine("WalkImages: " + e.Message); }
        sLog.AppendLine("FD preview textures saved=" + n);
    }

    static void WalkImages(VisualElement e, string prefix, ref int n, int depth)
    {
        if (e == null || depth > 12)
            return;
        var img = e as Image;
        if (img != null && img.image != null && img.image.width > 8)
        {
            Texture2D tex = BlitToTex(img.image);
            if (tex != null)
            {
                string file = ShotDir + "/" + prefix + "_ve" + n + ".png";
                File.WriteAllBytes(Full(file), tex.EncodeToPNG());
                sLog.AppendLine("FDVE Image " + img.image.width + "x" + img.image.height + " -> " + file);
                UnityEngine.Object.DestroyImmediate(tex);
                n++;
            }
        }
        if (e.style.backgroundImage.value.texture != null)
        {
            var t = e.style.backgroundImage.value.texture;
            if (t.width > 8)
            {
                Texture2D tex = BlitToTex(t);
                if (tex != null)
                {
                    string file = ShotDir + "/" + prefix + "_bg" + n + ".png";
                    File.WriteAllBytes(Full(file), tex.EncodeToPNG());
                    sLog.AppendLine("FDVE bg " + t.width + "x" + t.height + " name=" + e.name + " -> " + file);
                    UnityEngine.Object.DestroyImmediate(tex);
                    n++;
                }
            }
        }
        foreach (var c in e.Children())
            WalkImages(c, prefix, ref n, depth + 1);
    }

    static void BeginSsaoCompare()
    {
        sLog.AppendLine("--- SSAO on/off Game View compare (feature toggled, restored) ---");
        TrySetFd(false);
        EditorApplication.isPaused = false;
        sSsaoFeature = FindSsaoFeature();
        if (sSsaoFeature != null)
        {
            sSsaoWasOn = sSsaoFeature.isActive;
            sLog.AppendLine("SSAO feature found isActive=" + sSsaoWasOn);
        }
        else
            sLog.AppendLine("SSAO feature NOT found");
        File.WriteAllText(Full(ReportPath), sLog.ToString() + "\nWAITING SSAO compare...\n");
        sPhase = 4;
        sWait = 0;
    }

    static void TickSsaoCompare()
    {
        sWait++;
        if (sWait == 2 && sSsaoFeature != null)
        {
            sSsaoFeature.SetActive(false);
            sLog.AppendLine("SSAO forced OFF");
        }
        if (sWait == 18)
        {
            Texture2D off = GrabGamePixels();
            if (off != null)
            {
                File.WriteAllBytes(Full(ShotDir + "/90_ssao_off.png"), off.EncodeToPNG());
                sLog.AppendLine("ssao_off=" + ShotDir + "/90_ssao_off.png " + off.width + "x" + off.height);
                UnityEngine.Object.DestroyImmediate(off);
            }
            if (sSsaoFeature != null)
            {
                sSsaoFeature.SetActive(sSsaoWasOn);
                sLog.AppendLine("SSAO restored isActive=" + sSsaoWasOn);
            }
        }
        if (sWait == 36)
        {
            Texture2D on = GrabGamePixels();
            if (on != null)
            {
                File.WriteAllBytes(Full(ShotDir + "/91_ssao_on.png"), on.EncodeToPNG());
                sLog.AppendLine("ssao_on=" + ShotDir + "/91_ssao_on.png " + on.width + "x" + on.height);
                UnityEngine.Object.DestroyImmediate(on);
            }
            Finish(true);
        }
        if (sWait == 18 || sWait == 36)
            File.WriteAllText(Full(ReportPath), sLog.ToString());
    }

    static UnityEngine.Rendering.Universal.ScriptableRendererFeature FindSsaoFeature()
    {
        try
        {
            var urp = GraphicsSettings.currentRenderPipeline
                as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp == null)
                return null;
            var so = new SerializedObject(urp);
            var prop = so.FindProperty("m_RendererDataList");
            if (prop == null || prop.arraySize == 0)
                return null;
            var rd = prop.GetArrayElementAtIndex(0).objectReferenceValue
                as UnityEngine.Rendering.Universal.ScriptableRendererData;
            if (rd == null)
                return null;
            foreach (var f in rd.rendererFeatures)
            {
                if (f != null && f.GetType().Name.Contains("ScreenSpaceAmbientOcclusion"))
                    return f;
            }
        }
        catch (Exception e)
        {
            sLog.AppendLine("FindSsaoFeature: " + e.Message);
        }
        return null;
    }

    static void RestoreCamAndFollow()
    {
        Camera cam = Camera.main;
        string p = Full(RestorePath);
        if (cam == null || !File.Exists(p))
            return;
        string[] parts = File.ReadAllText(p).Split('|');
        if (parts.Length < 4)
            return;
        string[] xyz = parts[0].Split(',');
        string[] q = parts[1].Split(',');
        float ortho;
        int followOn;
        if (!float.TryParse(parts[2], out ortho) || !int.TryParse(parts[3], out followOn))
            return;
        cam.transform.position = new Vector3(float.Parse(xyz[0]), float.Parse(xyz[1]), float.Parse(xyz[2]));
        cam.transform.rotation = new Quaternion(float.Parse(q[0]), float.Parse(q[1]), float.Parse(q[2]), float.Parse(q[3]));
        cam.orthographic = true;
        cam.orthographicSize = ortho;
        Behaviour follow = FindFollow(cam);
        if (follow != null)
            follow.enabled = followOn != 0;
        try { File.Delete(p); } catch { }
    }

    static Behaviour FindFollow(Camera cam)
    {
        foreach (var c in cam.GetComponents<Behaviour>())
        {
            if (c.GetType().Name == "CameraFollow")
                return c;
        }
        return null;
    }

    static Bounds CollectBounds(GameObject root)
    {
        if (root == null)
            return new Bounds(Vector3.zero, Vector3.one);
        var rends = root.GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds(root.transform.position, Vector3.one);
        bool any = false;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null)
                continue;
            if (!any)
            {
                b = rends[i].bounds;
                any = true;
            }
            else
                b.Encapsulate(rends[i].bounds);
        }
        return b;
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
            }
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

    static bool FdEnabled()
    {
        try
        {
            Type t = FindT("FrameDebugger") ?? FindT("UnityEditorInternal.FrameDebugger");
            if (t == null)
                return false;
            var p = t.GetProperty("enabled", Stat);
            if (p == null)
                return false;
            return Convert.ToBoolean(p.GetValue(null, null));
        }
        catch { return false; }
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
            else
                m.Invoke(null, new object[] { on });
            return true;
        }
        catch { return false; }
    }

    static int GetEventCount()
    {
        Type util = FindT("FrameDebuggerUtility") ?? FindT("UnityEditorInternal.FrameDebuggerUtility");
        if (util == null)
            return -1;
        var p = util.GetProperty("count", Stat);
        if (p == null)
            return -1;
        try { return Convert.ToInt32(p.GetValue(null, null)); }
        catch { return -1; }
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
                if (m == null || obj == null)
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
        return ok;
    }

    static Type FindT(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = asm.GetType(name);
            if (t != null)
                return t;
            try
            {
                foreach (var x in asm.GetTypes())
                {
                    if (x.Name == name || x.FullName == name)
                        return x;
                }
            }
            catch { }
        }
        return null;
    }

    static string Trim(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
