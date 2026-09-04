using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 163 close-out — stall zip through Drop/Decimate (must halt), then
/// re-drop the 3.05M chair zip through Drop/Decimate/Pickable, approve, Play-shot.
/// Does not save GridFoundation. Restores CameraFollow via Bake160PipelineE2E.
/// Flag: Temp/bake163_closeout.flag
/// Stall report: Temp/bake163_e2e/stall_unity.txt
/// Chair report: Temp/bake163_e2e/report.txt
/// Play shot (after 160 e2e): Temp/fd_pipeline_e2e/play.png
/// </summary>
[InitializeOnLoad]
public static class Bake163Closeout
{
    const string FlagPath = "Temp/bake163_closeout.flag";
    const string OutDir = "Temp/bake163_e2e";
    const string StallReportPath = "Temp/bake163_e2e/stall_unity.txt";
    const string ReportPath = "Temp/bake163_e2e/report.txt";
    const string StallStem = "Ticket163_StallNet";
    const string ChairStem = "Meshy_AI_Cream_Cushioned_Woode_0902130031_texture_fbx";
    const string ChairZipSrc =
        "Drop/_Processed/Decimate/Nodes/Meshy_AI_Cream_Cushioned_Woode_0902130031_texture_fbx/"
        + "Meshy_AI_Cream_Cushioned_Woode_0902130031_texture_fbx.zip";
    const string StallFbx = "Temp/bake163/stall/dirty_join.fbx";
    const string PlayFlag = "Temp/fd_pipeline_e2e.flag";
    const string PlayShotFlag = "Temp/bake163_play.flag";
    const string PlayShotPath = "Temp/bake163_e2e/play.png";
    const string PlayReportPath = "Temp/bake163_e2e/play_report.txt";
    const string PlayCamRestore = "Temp/bake163_e2e_cam.txt";
    const string PlayParent = "Bake163_Play_DO_NOT_SAVE";
    const string ChairPrefab =
        "Assets/Objects/Pickable/Meshy_AI_Cream_Cushioned_Woode_0902130031_texture_fbx/"
        + "Meshy_AI_Cream_Cushioned_Woode_0902130031_texture_fbx.prefab";
    const string PlayPendingKey = "Bake163Play.pending";
    const string PlayEnteredKey = "Bake163Play.entered";
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    static readonly Vector3 PlayPlace = new Vector3(2.2f, 0f, 2.2f);
    static int sPlayPhase;
    static int sPlayWait;
    static StringBuilder sPlayLog;

    static Bake163Closeout()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Objects/163 Close-out (stall + Decimate drop + Play)")]
    public static void MenuRun()
    {
        File.WriteAllText(Full(FlagPath), "run\n");
    }

    static void PollFlag()
    {
        PollPlayShot();
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRun();
    }

    static void OnPlay(PlayModeStateChange c)
    {
        if (c != PlayModeStateChange.EnteredEditMode)
            return;
        RestorePlayCam();
        DestroyPlayParent();
    }

    static void TryRun()
    {
        if (!File.Exists(Full(FlagPath)))
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
            File.Delete(Full(FlagPath));
            string report = Run();
            WriteText(ReportPath, report);
            Debug.Log("[Bake163Closeout]\n" + report);
        }
        catch (Exception e)
        {
            WriteText(ReportPath, "ERROR " + e);
            Debug.LogError("[Bake163Closeout] " + e);
        }
    }

    static string Run()
    {
        Directory.CreateDirectory(Full(OutDir));
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 163 close-out ===");
        sb.AppendLine("scene=" + SceneManager.GetActiveScene().path);

        EnsureGridFoundation(sb);

        bool stallOk = RunStallThroughWatcher(sb);
        sb.AppendLine("CHECK stall watcher halt: " + stallOk);
        if (!stallOk)
        {
            sb.AppendLine("RESULT=FAIL stall");
            return sb.ToString();
        }

        bool dropOk = RunChairDrop(sb);
        sb.AppendLine("CHECK chair Drop/Decimate land: " + dropOk);
        if (!dropOk)
        {
            sb.AppendLine("RESULT=FAIL drop");
            return sb.ToString();
        }

        File.WriteAllText(Full(PlayFlag), ChairStem + "\n");
        sb.AppendLine("wrote " + PlayFlag + " stem=" + ChairStem);
        sb.AppendLine("Bake160PipelineE2E will Approve + Play-shot Temp/fd_pipeline_e2e/play.png");
        sb.AppendLine("RESULT=HANDED_TO_PLAY");
        return sb.ToString();
    }

    static void EnsureGridFoundation(StringBuilder sb)
    {
        string path = SceneManager.GetActiveScene().path;
        if (string.Equals(path, ScenePath, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("already on GridFoundation");
            return;
        }
        EditorSceneManager.OpenScene(ScenePath);
        sb.AppendLine("opened " + ScenePath + " (not saved)");
    }

    static bool RunStallThroughWatcher(StringBuilder sb)
    {
        sb.AppendLine("--- stall zip → Drop/Decimate/Pickable ---");
        string fbxAbs = Full(StallFbx);
        if (!File.Exists(fbxAbs))
        {
            sb.AppendLine("FAIL missing " + StallFbx);
            return false;
        }

        string zipAbs = Full("Temp/bake163_e2e/" + StallStem + ".zip");
        if (File.Exists(zipAbs))
            File.Delete(zipAbs);
        using (var zip = ZipFile.Open(zipAbs, ZipArchiveMode.Create))
            zip.CreateEntryFromFile(fbxAbs, StallStem + ".fbx");

        string dropZip = Full("Drop/Decimate/Pickable/" + StallStem + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(dropZip));
        File.Copy(zipAbs, dropZip, true);
        sb.AppendLine("dropped " + Rel(dropZip));

        RawDropWatcher.RunFromBatch();
        ObjectFolderGenerator.GenerateNow();

        string needs = Full("Drop/_NeedsReview/Decimate/Pickable/" + StallStem);
        string review = Full("Assets/Objects/_Review/" + StallStem);
        string incoming = Full("Assets/Objects/_Incoming/Pickable/" + StallStem);
        string dest = Full("Assets/Objects/Pickable/" + StallStem);
        string bakeOut = Full("Drop/_BakeOut/" + StallStem + "/" + StallStem + ".fbx");
        string nrJson = Path.Combine(review, "NEEDS_REVIEW.json");

        bool needsOk = Directory.Exists(needs);
        bool reviewOk = File.Exists(nrJson);
        bool noIncoming = !Directory.Exists(incoming);
        bool noDest = !Directory.Exists(dest);
        bool noBaked = !File.Exists(bakeOut);

        sb.AppendLine("Drop/_NeedsReview: " + needsOk + " " + Rel(needs));
        sb.AppendLine("Assets/Objects/_Review NEEDS_REVIEW.json: " + reviewOk);
        sb.AppendLine("Incoming absent: " + noIncoming);
        sb.AppendLine("dest Pickable absent: " + noDest);
        sb.AppendLine("baked FBX absent: " + noBaked);

        string reason = "";
        if (reviewOk)
        {
            File.Copy(nrJson, Full("Temp/bake163_e2e/stall_NEEDS_REVIEW.json"), true);
            reason = File.ReadAllText(nrJson);
            sb.AppendLine("NEEDS_REVIEW.json:");
            sb.AppendLine(reason);
        }

        bool reasonOk = reason.IndexOf("decimate_stall", StringComparison.Ordinal) >= 0
            && reason.IndexOf("24000", StringComparison.Ordinal) >= 0;
        bool ok = needsOk && reviewOk && noIncoming && noDest && noBaked && reasonOk;

        WriteText(StallReportPath, sb.ToString());
        CleanupStall(sb);
        return ok;
    }

    static void CleanupStall(StringBuilder sb)
    {
        TryDeleteAsset("Assets/Objects/_Review/" + StallStem);
        TryDeleteDir(Full("Drop/_NeedsReview/Decimate/Pickable/" + StallStem));
        TryDeleteDir(Full("Drop/_Unpacked/Decimate/Pickable/" + StallStem));
        TryDeleteDir(Full("Drop/_BakeOut/" + StallStem));
        TryDeleteDir(Full("Drop/_Processed/Decimate/Pickable/" + StallStem));
        string leftover = Full("Drop/Decimate/Pickable/" + StallStem + ".zip");
        if (File.Exists(leftover))
            File.Delete(leftover);
        TryDeleteAsset("Assets/Objects/_Incoming/Pickable/" + StallStem);
        AssetDatabase.Refresh();
        sb.AppendLine("cleaned stall drop leftovers (Temp copy of NEEDS_REVIEW kept)");
    }

    static bool RunChairDrop(StringBuilder sb)
    {
        sb.AppendLine("--- chair zip → Drop/Decimate/Pickable ---");
        string srcZip = Full(ChairZipSrc);
        if (!File.Exists(srcZip))
        {
            sb.AppendLine("FAIL missing " + ChairZipSrc);
            return false;
        }

        TryDeleteAsset("Assets/Objects/_Incoming/Nodes/" + ChairStem);
        TryDeleteAsset("Assets/Objects/_Incoming/Pickable/" + ChairStem);
        TryDeleteAsset("Assets/Objects/Nodes/" + ChairStem);
        TryDeleteAsset("Assets/Objects/Pickable/" + ChairStem);
        AssetDatabase.Refresh();

        string dropZip = Full("Drop/Decimate/Pickable/" + ChairStem + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(dropZip));
        File.Copy(srcZip, dropZip, true);
        sb.AppendLine("dropped " + Rel(dropZip) + " bytes=" + new FileInfo(dropZip).Length);

        RawDropWatcher.RunFromBatch();
        ObjectFolderGenerator.GenerateNow();
        AssetDatabase.Refresh();

        string destDir = "Assets/Objects/Pickable/" + ChairStem;
        string bakeJson = Full("Drop/_BakeOut/" + ChairStem);
        string jsonPath = null;
        if (Directory.Exists(bakeJson))
            jsonPath = Directory.GetFiles(bakeJson, "*.bake.json").FirstOrDefault();

        if (!string.IsNullOrEmpty(jsonPath))
        {
            File.Copy(jsonPath, Full("Temp/bake163_e2e/chair.bake.json"), true);
            string json = File.ReadAllText(jsonPath);
            sb.AppendLine("bake.json " + Rel(jsonPath));
            sb.AppendLine("decimate_requested: " + json.Contains("\"decimate_requested\": true"));
            sb.AppendLine("target 1500: " + json.Contains("\"target_tris\": 1500"));
            int trisAfter = IndexInt(json, "\"tris_after_decimate\":");
            int trisBake = IndexInt(json, "\"tris_after_bake\":");
            sb.AppendLine("tris_after_decimate=" + trisAfter + " tris_after_bake=" + trisBake);
        }
        else
            sb.AppendLine("WARN no bake.json");

        if (!AssetDatabase.IsValidFolder(destDir))
        {
            sb.AppendLine("FAIL dest missing " + destDir);
            return false;
        }

        string fbx = FindFbx(destDir);
        sb.AppendLine("dest fbx=" + fbx);
        int unityTris = CountTris(fbx);
        sb.AppendLine("unity mesh tris=" + unityTris);

        var importer = string.IsNullOrEmpty(fbx) ? null : AssetImporter.GetAtPath(fbx) as ModelImporter;
        bool normalsOk = false;
        if (importer != null)
        {
            sb.AppendLine("importNormals=" + importer.importNormals
                + " smooth=" + importer.normalSmoothingAngle
                + " tangents=" + importer.importTangents);
            normalsOk = importer.importNormals == ModelImporterNormals.Calculate
                && Mathf.Abs(importer.normalSmoothingAngle - MeshyModelImportDefaults.NormalSmoothingAngle) < 0.5f
                && importer.importTangents == ModelImporterTangents.CalculateMikk;
        }
        sb.AppendLine("CHECK 160 Calculate/90/Mikk: " + normalsOk);

        bool propsOk = AssetDatabase.LoadAssetAtPath<ObjectProperties>(
            destDir + "/" + ChairStem + "_Properties.asset") != null;
        bool matOk = AssetDatabase.LoadAssetAtPath<Material>(
            destDir + "/" + ChairStem + ".mat") != null;
        sb.AppendLine("Properties: " + propsOk + " mat: " + matOk);

        bool trisOk = unityTris > 1400 && unityTris < 1700;
        sb.AppendLine("CHECK unity tris near 1500: " + trisOk + " (" + unityTris + ")");
        return propsOk && matOk && normalsOk && trisOk && !string.IsNullOrEmpty(fbx);
    }

    static int CountTris(string fbx)
    {
        if (string.IsNullOrEmpty(fbx))
            return -1;
        int n = 0;
        UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbx);
        for (int i = 0; i < all.Length; i++)
        {
            var mesh = all[i] as Mesh;
            if (mesh == null)
                continue;
            n += mesh.triangles.Length / 3;
        }
        return n;
    }

    static string FindFbx(string folder)
    {
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }

    static int IndexInt(string json, string key)
    {
        int i = json.IndexOf(key, StringComparison.Ordinal);
        if (i < 0)
            return -1;
        i += key.Length;
        while (i < json.Length && (json[i] == ' ' || json[i] == '\n' || json[i] == '\t'))
            i++;
        int j = i;
        while (j < json.Length && char.IsDigit(json[j]))
            j++;
        int n;
        return j > i && int.TryParse(json.Substring(i, j - i), out n) ? n : -1;
    }

    static void TryDeleteAsset(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath) || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
            AssetDatabase.DeleteAsset(assetPath);
        else
            TryDeleteDir(Full(assetPath));
    }

    static void TryDeleteDir(string abs)
    {
        try
        {
            if (Directory.Exists(abs))
                Directory.Delete(abs, true);
            string meta = abs + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }
        catch (Exception)
        {
        }
    }

    static void WriteText(string rel, string text)
    {
        string full = Full(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, text ?? "");
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }

    static string Rel(string abs)
    {
        string root = Full(".");
        if (abs.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return abs.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
        return abs;
    }

    static void PollPlayShot()
    {
        if (sPlayPhase == 1)
        {
            sPlayWait++;
            ApplyPlayView();
            if (sPlayWait < 40)
                return;
            GrabPlayShot();
            return;
        }

        if (EditorApplication.isCompiling)
            return;

        bool flag = File.Exists(Full(PlayShotFlag));
        bool pending = SessionState.GetBool(PlayPendingKey, false);
        bool entered = SessionState.GetBool(PlayEnteredKey, false);

        if (entered && EditorApplication.isPlaying)
        {
            if (sPlayLog == null)
            {
                sPlayLog = new StringBuilder();
                if (File.Exists(Full(PlayReportPath)))
                    sPlayLog.Append(File.ReadAllText(Full(PlayReportPath)));
                sPlayLog.AppendLine("resumed in Play");
            }
            sPlayPhase = 1;
            sPlayWait = 0;
            return;
        }

        if (!flag && !pending)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (EditorApplication.isPlaying && !entered)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        if (flag)
        {
            try { File.Delete(Full(PlayShotFlag)); } catch { }
        }

        Camera cam = FindSceneCamera();
        if (cam == null)
        {
            WriteText(PlayReportPath, "FAIL no scene camera\nRESULT=FAIL\n");
            Debug.LogError("[Bake163Closeout] FAIL no scene camera");
            return;
        }

        StartPlayShot(cam);
    }

    static void StartPlayShot(Camera cam)
    {
        sPlayLog = new StringBuilder();
        sPlayLog.AppendLine("=== Ticket 163 Play shot ===");
        sPlayLog.AppendLine("scene=" + SceneManager.GetActiveScene().path);
        sPlayLog.AppendLine("camera=" + cam.name + " tag=" + cam.tag);

        DestroyPlayParent();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChairPrefab);
        if (prefab == null)
        {
            sPlayLog.AppendLine("FAIL missing prefab");
            WriteText(PlayReportPath, sPlayLog.ToString());
            return;
        }

        var parent = new GameObject(PlayParent);
        parent.hideFlags = HideFlags.HideAndDontSave;
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "DecimateChair1500";
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetParent(parent.transform, true);
        go.transform.SetPositionAndRotation(PlayPlace, Quaternion.Euler(0f, 35f, 0f));

        Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
                b.Encapsulate(rends[i].bounds);
            Vector3 p = go.transform.position;
            p.y += -b.min.y;
            go.transform.position = p;
            sPlayLog.AppendLine("spawned at " + go.transform.position + " size=" + b.size);
        }

        var mf = go.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
            sPlayLog.AppendLine("tris=" + (mf.sharedMesh.triangles.Length / 3)
                + " verts=" + mf.sharedMesh.vertexCount);

        var rend = go.GetComponentInChildren<Renderer>();
        if (rend != null && rend.sharedMaterial != null)
        {
            var mat = rend.sharedMaterial;
            sPlayLog.AppendLine("mat=" + mat.name + " shader=" + mat.shader.name);
            if (mat.HasProperty("_BaseMap"))
                sPlayLog.AppendLine("baseMap=" + (mat.GetTexture("_BaseMap") != null));
        }

        PosePlayCam(cam, go, true);
        SessionState.SetBool(PlayPendingKey, true);
        SessionState.SetBool(PlayEnteredKey, true);
        sPlayLog.AppendLine("entering Play");
        WriteText(PlayReportPath, sPlayLog.ToString());
        EditorApplication.isPlaying = true;
    }

    static void ApplyPlayView()
    {
        Camera cam = FindSceneCamera();
        GameObject target = GameObject.Find(PlayParent);
        if (cam == null || target == null)
            return;
        PosePlayCam(cam, target, false);
    }

    static void PosePlayCam(Camera cam, GameObject target, bool saveRestore)
    {
        Behaviour follow = FindFollow(cam);
        if (saveRestore)
        {
            File.WriteAllText(Full(PlayCamRestore),
                cam.transform.position.x + "," + cam.transform.position.y + "," + cam.transform.position.z + "|"
                + cam.transform.rotation.x + "," + cam.transform.rotation.y + ","
                + cam.transform.rotation.z + "," + cam.transform.rotation.w + "|"
                + (cam.orthographic ? cam.orthographicSize : 6f) + "|"
                + (follow != null && follow.enabled ? 1 : 0));
        }
        if (follow != null)
            follow.enabled = false;
        cam.enabled = true;

        Bounds b = new Bounds(PlayPlace, Vector3.one);
        Renderer[] rends = target.GetComponentsInChildren<Renderer>(true);
        if (rends.Length > 0)
        {
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
                b.Encapsulate(rends[i].bounds);
        }
        cam.orthographic = true;
        cam.orthographicSize = 1.25f;
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(8f, -7f, 8f).normalized);
        cam.transform.position = b.center + new Vector3(-2.5f, 2.2f, -2.5f);
        if (saveRestore)
            sPlayLog.AppendLine("camera posed at " + cam.transform.position);
    }

    static void GrabPlayShot()
    {
        ApplyPlayView();
        Directory.CreateDirectory(Full(OutDir));
        Camera cam = FindSceneCamera();
        Texture2D tex = cam != null ? CaptureRt(cam) : null;
        if (tex != null && tex.width >= 32)
        {
            File.WriteAllBytes(Full(PlayShotPath), tex.EncodeToPNG());
            sPlayLog.AppendLine("play shot " + tex.width + "x" + tex.height
                + " bytes=" + new FileInfo(Full(PlayShotPath)).Length);
            UnityEngine.Object.DestroyImmediate(tex);
        }
        else
            sPlayLog.AppendLine("FAIL play shot missing");

        sPlayLog.AppendLine("Camera.main=" + (Camera.main != null ? Camera.main.name : "null"));
        sPlayLog.AppendLine("spawned=" + (GameObject.Find(PlayParent) != null));
        sPlayLog.AppendLine("RESULT=OK inspect play.png");
        WriteText(PlayReportPath, sPlayLog.ToString());
        Debug.Log("[Bake163Closeout play]\n" + sPlayLog);

        sPlayPhase = 0;
        SessionState.SetBool(PlayPendingKey, false);
        SessionState.SetBool(PlayEnteredKey, false);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static Texture2D CaptureRt(Camera cam)
    {
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
        return tex;
    }

    static Camera FindSceneCamera()
    {
        if (Camera.main != null)
            return Camera.main;
        Camera[] all = Resources.FindObjectsOfTypeAll<Camera>();
        for (int i = 0; i < all.Length; i++)
        {
            Camera c = all[i];
            if (c == null || !c.gameObject.scene.IsValid())
                continue;
            if (c.name.Contains("Preview"))
                continue;
            return c;
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

    static void RestorePlayCam()
    {
        Camera cam = FindSceneCamera();
        string p = Full(PlayCamRestore);
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

    static void DestroyPlayParent()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == PlayParent && all[i].gameObject.scene.IsValid())
                UnityEngine.Object.DestroyImmediate(all[i].gameObject);
        }
    }
}
