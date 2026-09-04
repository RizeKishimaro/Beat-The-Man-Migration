using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 160 Part B — after a genuine Drop/ zip has been processed by 153/152/148/149,
/// approve, spawn the real prefab, Play-verify blotches / cell-fit / gloss / 161.
/// Does not save GridFoundation. Restores CameraFollow.
/// Flag: Temp/fd_pipeline_e2e.flag  (first line optional object stem; default Ticket160_PipelineProbe)
/// Report: Temp/fd_pipeline_e2e.txt
/// Shot: Temp/fd_pipeline_e2e/play.png
/// </summary>
[InitializeOnLoad]
public static class Bake160PipelineE2E
{
    const string DefaultStem = "Ticket160_PipelineProbe";
    const string FlagPath = "Temp/fd_pipeline_e2e.flag";
    const string CleanupFlagPath = "Temp/fd_pipeline_e2e_cleanup.flag";
    const string CleanupReportPath = "Temp/fd_pipeline_e2e_cleanup.txt";
    const string ReportPath = "Temp/fd_pipeline_e2e.txt";
    const string CamRestorePath = "Temp/fd_pipeline_e2e_cam.txt";
    const string ShotDir = "Temp/fd_pipeline_e2e";
    const string ParentName = "Bake160_PipelineE2E_DO_NOT_SAVE";
    const string PendingKey = "Bake160PipelineE2E.pending";
    const string EnteredPlayKey = "Bake160PipelineE2E.enteredPlay";
    const string StemKey = "Bake160PipelineE2E.stem";
    const string WaitStartKey = "Bake160PipelineE2E.waitStart";
    const float WaitTimeoutSec = 600f;
    static readonly Vector3 PlacePos = new Vector3(40f, 0f, 40f);

    static int sPhase;
    static int sWait;
    static StringBuilder sLog;

    static Bake160PipelineE2E()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Objects/160 Pipeline E2E Verify")]
    public static void MenuVerify()
    {
        File.WriteAllText(Full(FlagPath), DefaultStem + "\n");
        SessionState.SetFloat(WaitStartKey, 0f);
        SessionState.SetBool(PendingKey, false);
        SessionState.SetBool(EnteredPlayKey, false);
        SessionState.SetString(StemKey, DefaultStem);
        ObjectIncomingPostprocessor.OrganizeNow();
        ObjectFolderGenerator.GenerateNow();
        Debug.Log("[Bake160PipelineE2E] menu — flag written, organize kicked");
    }

    static void OnPlay(PlayModeStateChange c)
    {
        if (c == PlayModeStateChange.EnteredEditMode)
        {
            RestoreCam();
            DestroyProbe();
        }
    }

    static void Poll()
    {
        if (File.Exists(Full(CleanupFlagPath))
            && !EditorApplication.isCompiling
            && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            try { File.Delete(Full(CleanupFlagPath)); } catch { }
            string report = DestroyProbe();
            File.WriteAllText(Full(CleanupReportPath), report);
            Debug.Log("[Bake160PipelineE2E] cleanup\n" + report);
        }

        if (sPhase == 1)
        {
            sWait++;
            ApplyChairView();
            if (sWait < 50)
                return;
            GrabPlay();
            return;
        }

        if (sPhase != 0)
            return;
        if (EditorApplication.isCompiling)
            return;

        bool flag = File.Exists(Full(FlagPath));
        bool pending = SessionState.GetBool(PendingKey, false);
        bool enteredPlay = SessionState.GetBool(EnteredPlayKey, false);

        if (enteredPlay && EditorApplication.isPlaying)
        {
            if (sLog == null)
            {
                sLog = new StringBuilder();
                if (File.Exists(Full(ReportPath)))
                    sLog.Append(File.ReadAllText(Full(ReportPath)));
                sLog.AppendLine("resumed in Play after domain reload");
            }
            sPhase = 1;
            sWait = 0;
            return;
        }

        if (!flag && !pending)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (EditorApplication.isPlaying && !enteredPlay)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        string stem = ReadStem();
        SessionState.SetString(StemKey, stem);

        if (!DestReady(stem, out string waitReason))
        {
            if (NeedsReview(stem, out string reviewDetail))
            {
                ConsumeFlag();
                WriteFail("FAIL bake/incoming landed in _NeedsReview: " + reviewDetail);
                return;
            }

            double lastOrg = SessionState.GetFloat("Bake160PipelineE2E.lastOrg", 0f);
            if (EditorApplication.timeSinceStartup - lastOrg > 1.5)
            {
                SessionState.SetFloat("Bake160PipelineE2E.lastOrg",
                    (float)EditorApplication.timeSinceStartup);
                File.WriteAllText(Full("Temp/fd_pipeline_e2e_wait.txt"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + waitReason
                    + " compiling=" + EditorApplication.isCompiling
                    + " playing=" + EditorApplication.isPlaying);
                ObjectIncomingPostprocessor.OrganizeNow();
                ObjectFolderGenerator.GenerateNow();
            }

            double start = SessionState.GetFloat(WaitStartKey, 0f);
            if (start < 1f)
            {
                start = EditorApplication.timeSinceStartup;
                SessionState.SetFloat(WaitStartKey, (float)start);
            }
            if (EditorApplication.timeSinceStartup - start > WaitTimeoutSec)
            {
                ConsumeFlag();
                WriteFail("FAIL timed out waiting for dest (" + waitReason + ")");
                return;
            }
            return;
        }

        if (EditorApplication.isPlaying)
            return;

        ConsumeFlag();
        SessionState.SetFloat(WaitStartKey, 0f);
        RunEditThenPlay(stem);
    }

    static string ReadStem()
    {
        string saved = SessionState.GetString(StemKey, "");
        if (!string.IsNullOrEmpty(saved))
            return saved;
        string flag = Full(FlagPath);
        if (File.Exists(flag))
        {
            string[] lines = File.ReadAllLines(flag);
            if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0])
                && !lines[0].Trim().Equals("run", StringComparison.OrdinalIgnoreCase))
                return lines[0].Trim();
        }
        return DefaultStem;
    }

    static bool DestReady(string stem, out string reason)
    {
        string folder = DestFolder(stem);
        if (!Directory.Exists(Abs(folder)))
        {
            reason = "no dest folder " + folder;
            return false;
        }
        if (!File.Exists(Abs(folder + "/" + stem + "_Properties.asset")))
        {
            reason = "no Properties";
            return false;
        }
        if (!File.Exists(Abs(folder + "/" + stem + ".mat")))
        {
            reason = "no mat";
            return false;
        }
        if (!File.Exists(Abs(folder + "/" + stem + ".cs")))
        {
            reason = "no stub";
            return false;
        }
        reason = "ok";
        return true;
    }

    static bool NeedsReview(string stem, out string detail)
    {
        string[] roots =
        {
            "Drop/_NeedsReview/Pickable/" + stem,
            "Drop/_NeedsReview/Nodes/" + stem,
            "Assets/Objects/_Review/" + stem
        };
        for (int i = 0; i < roots.Length; i++)
        {
            if (Directory.Exists(Abs(roots[i])))
            {
                detail = roots[i];
                return true;
            }
        }
        detail = "";
        return false;
    }

    static void RunEditThenPlay(string stem)
    {
        sLog = new StringBuilder();
        sLog.AppendLine("=== Ticket 160 Part B pipeline e2e ===");
        sLog.AppendLine("stem=" + stem);
        sLog.AppendLine("defaults=MeshyModelImportDefaults Calculate " + MeshyModelImportDefaults.NormalSmoothingAngle);

        string folder = DestFolder(stem);
        string fbx = FindFbx(folder);
        sLog.AppendLine("folder=" + folder);
        sLog.AppendLine("fbx=" + fbx);

        bool normalsOk = CheckNormals(fbx, sLog);
        bool glossOk = CheckGloss(folder, stem, sLog);
        bool dupOk = CheckNoDuplicate(stem, sLog);
        bool compileOk = !EditorUtility.scriptCompilationFailed;
        sLog.AppendLine("CHECK compile errors: " + compileOk
            + " scriptCompilationFailed=" + EditorUtility.scriptCompilationFailed);

        var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(folder + "/" + stem + "_Properties.asset");
        if (props == null)
        {
            WriteFail("FAIL missing Properties");
            return;
        }

        if (!props.approved)
        {
            props.approved = true;
            EditorUtility.SetDirty(props);
            try
            {
                ObjectApprovedPrefabBuilder.Build(props);
                sLog.AppendLine("approved + prefab built");
            }
            catch (Exception e)
            {
                WriteFail("FAIL Approve/Build: " + e);
                return;
            }
        }
        else
        {
            sLog.AppendLine("already approved; rebuilding prefab");
            ObjectApprovedPrefabBuilder.Build(props);
        }

        bool scaleOk = CheckCellFitPrefab(props, sLog);

        SpawnPrefab(props, sLog);
        PoseCamera(sLog);
        FocusGameView();
        SetGameViewScale1x(sLog);

        sLog.AppendLine("CHECK1_normals_auto=" + normalsOk);
        sLog.AppendLine("CHECK2_cellfit_prefab=" + scaleOk);
        sLog.AppendLine("CHECK3_gloss=" + glossOk);
        sLog.AppendLine("CHECK4_compile_dup=" + (compileOk && dupOk));
        File.WriteAllText(Full(ReportPath), sLog.ToString());

        SessionState.SetBool(PendingKey, true);
        SessionState.SetBool(EnteredPlayKey, true);
        sLog.AppendLine("entering Play");
        File.WriteAllText(Full(ReportPath), sLog.ToString());
        EditorApplication.isPlaying = true;
    }

    static bool CheckNormals(string fbx, StringBuilder sb)
    {
        sb.AppendLine("--- CHECK1 importer (must be automatic Calculate, no manual tweak) ---");
        var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (mi == null)
        {
            sb.AppendLine("FAIL no importer");
            return false;
        }
        sb.AppendLine("importNormals=" + mi.importNormals);
        sb.AppendLine("normalSmoothingAngle=" + mi.normalSmoothingAngle);
        sb.AppendLine("importTangents=" + mi.importTangents);
        bool ok = mi.importNormals == ModelImporterNormals.Calculate
            && Mathf.Abs(mi.normalSmoothingAngle - MeshyModelImportDefaults.NormalSmoothingAngle) < 0.5f
            && mi.importTangents == ModelImporterTangents.CalculateMikk;
        sb.AppendLine("CHECK1 auto Calculate 90: " + ok);
        return ok;
    }

    static bool CheckGloss(string folder, string stem, StringBuilder sb)
    {
        sb.AppendLine("--- CHECK3 material smoothness (ticket 160) ---");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(folder + "/" + stem + ".mat");
        if (mat == null)
        {
            sb.AppendLine("FAIL no mat");
            return false;
        }
        sb.AppendLine("shader=" + (mat.shader != null ? mat.shader.name : "null"));
        float smooth = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : -1f;
        sb.AppendLine("_Smoothness=" + smooth);
        bool mask = mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP");
        sb.AppendLine("_METALLICSPECGLOSSMAP=" + mask);
        bool shaderOk = mat.shader != null
            && mat.shader.name.IndexOf("Lit", StringComparison.OrdinalIgnoreCase) >= 0;
        bool glossOk = shaderOk && Mathf.Abs(smooth - 0.5f) < 0.02f;
        sb.AppendLine("CHECK3 URP Lit smoothness 0.5: " + glossOk);
        return glossOk;
    }

    static bool CheckNoDuplicate(string stem, StringBuilder sb)
    {
        sb.AppendLine("--- CHECK4 duplicate folder / class (ticket 161) ---");
        string nodes = "Assets/Objects/Nodes/" + stem;
        bool nodesGone = !Directory.Exists(Abs(nodes));
        sb.AppendLine("Nodes dest absent: " + nodesGone + " " + nodes);

        var seen = new System.Collections.Generic.Dictionary<string, string>(
            StringComparer.Ordinal);
        bool unique = true;
        string[] roots = { "Assets/Objects/Pickable", "Assets/Objects/Nodes" };
        for (int r = 0; r < roots.Length; r++)
        {
            string abs = Abs(roots[r]);
            if (!Directory.Exists(abs))
                continue;
            foreach (string file in Directory.GetFiles(abs, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                int i = text.IndexOf("public class ", StringComparison.Ordinal);
                if (i < 0)
                    continue;
                int start = i + "public class ".Length;
                int end = text.IndexOf(' ', start);
                int colon = text.IndexOf(':', start);
                if (colon >= 0 && (end < 0 || colon < end))
                    end = colon;
                if (end < 0)
                    continue;
                string cls = text.Substring(start, end - start).Trim();
                if (string.IsNullOrEmpty(cls))
                    continue;
                if (seen.TryGetValue(cls, out string prior))
                {
                    sb.AppendLine("DUPLICATE class " + cls + " in " + prior + " and " + file);
                    unique = false;
                }
                else
                    seen[cls] = file;
            }
        }
        sb.AppendLine("CHECK unique stub classes: " + unique);
        bool ok = nodesGone && unique;
        sb.AppendLine("CHECK4 no dup folder/class: " + ok);
        return ok;
    }

    static bool CheckCellFitPrefab(ObjectProperties props, StringBuilder sb)
    {
        sb.AppendLine("--- CHECK2 cell-fit on REAL prefab (ticket 162) ---");
        sb.AppendLine("kind=" + props.kind);
        sb.AppendLine("exempt=" + props.exemptFromCellFitScaling);
        sb.AppendLine("scaleMultiplier=" + props.scaleMultiplier);

        string fbx = ObjectPropertiesPreview.FindFbxFor(props);
        bool computed = ObjectCellFitScaling.TryComputeForFbx(fbx, out float expect, out float maxXZ);
        sb.AppendLine("fbx footprint maxXZ=" + maxXZ + " expectMultiplier=" + expect);

        string prefabPath = props.prefabPath;
        sb.AppendLine("prefabPath=" + prefabPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            sb.AppendLine("FAIL missing prefab");
            return false;
        }

        Transform wrapper = prefab.transform.Find("Wrapper");
        if (wrapper == null && prefab.transform.childCount > 0)
            wrapper = prefab.transform.GetChild(0);
        Vector3 rootScale = prefab.transform.localScale;
        Vector3 wrapScale = wrapper != null ? wrapper.localScale : Vector3.zero;
        sb.AppendLine("prefab root scale=" + rootScale);
        sb.AppendLine("prefab Wrapper scale=" + wrapScale);

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.hideFlags = HideFlags.HideAndDontSave;
        inst.transform.position = Vector3.zero;
        inst.transform.rotation = Quaternion.identity;
        bool measured = ObjectCellFitScaling.TryMeasureFootprintXZ(inst, out float worldXZ);
        UnityEngine.Object.DestroyImmediate(inst);
        sb.AppendLine("instanced prefab world maxXZ=" + worldXZ);

        bool wrapOk = wrapper != null
            && Mathf.Abs(wrapper.localScale.x - props.scaleMultiplier) < 0.01f
            && Mathf.Abs(rootScale.x - 1f) < 0.01f;
        bool fitOk = computed && Mathf.Abs(props.scaleMultiplier - expect) < 0.02f;
        bool worldOk = measured && Mathf.Abs(worldXZ - ObjectCellFitScaling.CellSizeMeters) < 0.08f;
        bool ok = wrapOk && fitOk && worldOk;
        sb.AppendLine("CHECK2 wrapper=multiplier, root=1, worldXZ~1m: " + ok
            + " wrapOk=" + wrapOk + " fitOk=" + fitOk + " worldOk=" + worldOk);
        return ok;
    }

    static void SpawnPrefab(ObjectProperties props, StringBuilder sb)
    {
        GameObject old = GameObject.Find(ParentName);
        if (old != null)
            UnityEngine.Object.DestroyImmediate(old);

        string[] hide =
        {
            "Bake160_InRoom_DO_NOT_SAVE",
            "Bake160_PrinterRow"
        };
        for (int i = 0; i < hide.Length; i++)
        {
            GameObject extra = GameObject.Find(hide[i]);
            if (extra != null)
                extra.SetActive(false);
        }

        var parent = new GameObject(ParentName);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(props.prefabPath);
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = props.objectName;
        go.transform.SetParent(parent.transform, true);
        go.transform.SetPositionAndRotation(PlacePos, Quaternion.Euler(0f, 35f, 0f));

        Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
                b.Encapsulate(rends[i].bounds);
            Vector3 p = go.transform.position;
            p.y += -b.min.y;
            go.transform.position = p;
            sb.AppendLine("spawned prefab at " + go.transform.position + " bounds=" + b.size);
        }
        else
            sb.AppendLine("WARN spawned with no renderers");
    }

    static void PoseCamera(StringBuilder sb)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            sb.AppendLine("WARN no Camera.main");
            return;
        }
        Behaviour follow = FindFollow(cam);
        bool followOn = follow != null && follow.enabled;
        File.WriteAllText(Full(CamRestorePath),
            cam.transform.position.x + "," + cam.transform.position.y + "," + cam.transform.position.z + "|"
            + cam.transform.rotation.x + "," + cam.transform.rotation.y + ","
            + cam.transform.rotation.z + "," + cam.transform.rotation.w + "|"
            + (cam.orthographic ? cam.orthographicSize : 6f) + "|"
            + (followOn ? 1 : 0));
        if (follow != null)
            follow.enabled = false;

        GameObject target = GameObject.Find(ParentName);
        Bounds b = new Bounds(PlacePos, Vector3.one);
        if (target != null)
        {
            Renderer[] rends = target.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++)
                    b.Encapsulate(rends[i].bounds);
            }
        }
        cam.orthographic = true;
        cam.orthographicSize = 1.15f;
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(8f, -7f, 8f).normalized);
        cam.transform.position = b.center + new Vector3(-2.4f, 2.1f, -2.4f);
        sb.AppendLine("camera posed at " + cam.transform.position + " ortho=" + cam.orthographicSize);
    }

    static void ApplyChairView()
    {
        KeepFollowOff();
        Camera cam = Camera.main;
        GameObject target = GameObject.Find(ParentName);
        if (cam == null || target == null)
            return;
        Renderer[] rends = target.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0)
            return;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);
        cam.orthographic = true;
        cam.orthographicSize = 1.15f;
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(8f, -7f, 8f).normalized);
        cam.transform.position = b.center + new Vector3(-2.4f, 2.1f, -2.4f);
    }

    static void GrabPlay()
    {
        ApplyChairView();
        FocusGameView();
        SetGameViewScale1x(sLog);
        Directory.CreateDirectory(Full(ShotDir));
        string shot = Full(ShotDir + "/play.png");

        FocusGameView();
        Texture2D tex = null;
        try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
        catch { tex = null; }
        if (tex == null || tex.height < 200)
        {
            if (tex != null)
                UnityEngine.Object.DestroyImmediate(tex);
            tex = GrabGameRt();
        }
        if (tex != null && tex.width >= 32 && tex.height >= 32)
        {
            File.WriteAllBytes(shot, tex.EncodeToPNG());
            sLog.AppendLine("play shot " + tex.width + "x" + tex.height + " bytes=" + new FileInfo(shot).Length);
            UnityEngine.Object.DestroyImmediate(tex);
        }
        else
        {
            sLog.AppendLine("FAIL play shot missing or too small");
        }

        GameObject spawned = GameObject.Find(ParentName);
        sLog.AppendLine("play spawned=" + (spawned != null));
        if (spawned != null)
        {
            var mf = spawned.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var n = mf.sharedMesh.normals;
                int zero = 0;
                for (int i = 0; i < n.Length; i++)
                {
                    if (n[i].sqrMagnitude < 1e-8f)
                        zero++;
                }
                sLog.AppendLine("play mesh verts=" + mf.sharedMesh.vertexCount
                    + " normals=" + n.Length + " zeroN=" + zero);
            }
        }

        bool compileOk = !EditorUtility.scriptCompilationFailed;
        sLog.AppendLine("play compileFailed=" + EditorUtility.scriptCompilationFailed);
        sLog.AppendLine(compileOk ? "RESULT=OK (inspect play.png for blotches/gloss)" : "RESULT=FAIL compile");
        File.WriteAllText(Full(ReportPath), sLog.ToString());
        Debug.Log("[Bake160PipelineE2E]\n" + sLog);

        sPhase = 0;
        SessionState.SetBool(PendingKey, false);
        SessionState.SetBool(EnteredPlayKey, false);
        SessionState.SetString(StemKey, "");
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static string DestroyProbe()
    {
        var sb = new StringBuilder();
        sb.AppendLine("scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        string scenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        bool onDisk = false;
        if (!string.IsNullOrEmpty(scenePath))
        {
            string abs = Full(scenePath);
            onDisk = File.Exists(abs)
                && File.ReadAllText(abs).IndexOf(ParentName, StringComparison.Ordinal) >= 0;
        }
        sb.AppendLine("onDisk=" + onDisk);

        GameObject go = null;
        var all = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i].name != ParentName)
                continue;
            if (!all[i].gameObject.scene.IsValid())
                continue;
            go = all[i].gameObject;
            break;
        }
        if (go == null)
        {
            sb.AppendLine("live " + ParentName + ": absent");
            sb.AppendLine(onDisk ? "RESULT=FAIL still on disk" : "RESULT=CLEAN");
            return sb.ToString();
        }
        UnityEngine.Object.DestroyImmediate(go);
        bool still = false;
        all = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == ParentName && all[i].gameObject.scene.IsValid())
            {
                still = true;
                break;
            }
        }
        sb.AppendLine("destroyed " + ParentName);
        sb.AppendLine("live after: " + (still ? "STILL PRESENT" : "absent"));
        sb.AppendLine((!still && !onDisk) ? "RESULT=CLEAN" : "RESULT=FAIL");
        return sb.ToString();
    }

    static void ConsumeFlag()
    {
        try
        {
            string p = Full(FlagPath);
            if (File.Exists(p))
                File.Delete(p);
        }
        catch { }
    }

    static void WriteFail(string msg)
    {
        if (sLog == null)
            sLog = new StringBuilder();
        sLog.AppendLine(msg);
        sLog.AppendLine("RESULT=FAIL");
        File.WriteAllText(Full(ReportPath), sLog.ToString());
        Debug.LogError("[Bake160PipelineE2E] " + msg);
        SessionState.SetBool(PendingKey, false);
        SessionState.SetBool(EnteredPlayKey, false);
        SessionState.SetString(StemKey, "");
        SessionState.SetFloat(WaitStartKey, 0f);
    }

    static string DestFolder(string stem) => "Assets/Objects/Pickable/" + stem;

    static string FindFbx(string folder)
    {
        string abs = Abs(folder);
        if (!Directory.Exists(abs))
            return null;
        foreach (string file in Directory.GetFiles(abs, "*.fbx"))
        {
            string n = file.Replace('\\', '/');
            int i = n.IndexOf("/Assets/", StringComparison.Ordinal);
            if (i >= 0)
                return n.Substring(i + 1);
            i = n.IndexOf("Assets/", StringComparison.Ordinal);
            return i >= 0 ? n.Substring(i) : n;
        }
        return null;
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

    static void KeepFollowOff()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        Behaviour follow = FindFollow(cam);
        if (follow != null && follow.enabled)
            follow.enabled = false;
    }

    static void RestoreCam()
    {
        Camera cam = Camera.main;
        string p = Full(CamRestorePath);
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

    static Texture2D GrabGameRt()
    {
        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
        {
            if (w.GetType().Name != "GameView")
                continue;
            w.Focus();
            w.Repaint();
            foreach (var f in w.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (!typeof(RenderTexture).IsAssignableFrom(f.FieldType))
                    continue;
                var rt = f.GetValue(w) as RenderTexture;
                if (rt == null || rt.width < 32 || rt.height < 32)
                    continue;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                return tex;
            }
        }
        return null;
    }

    static bool SetGameViewScale1x(StringBuilder sb)
    {
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return false;
        EditorWindow gv = EditorWindow.GetWindow(gvType, false, null, true);
        if (gv == null)
            return false;
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object za = gvType.GetField("m_ZoomArea", inst)?.GetValue(gv);
        if (za != null)
        {
            var scaleProp = za.GetType().GetProperty("scale", inst);
            if (scaleProp != null && scaleProp.CanWrite)
            {
                scaleProp.SetValue(za, Vector2.one, null);
                gv.Repaint();
                sb.AppendLine("GameView scale=1");
                return true;
            }
        }
        return false;
    }

    static void FocusGameView()
    {
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return;
        EditorWindow gv = EditorWindow.GetWindow(gvType, false, "Game", true);
        gv.Focus();
        gv.Repaint();
    }

    static string Abs(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative.Replace('/', Path.DirectorySeparatorChar)));

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
