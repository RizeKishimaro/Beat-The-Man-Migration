using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 160 — switch chair + cooler FBX normals Import → Calculate, Play-verify.
/// Does not save GridFoundation. Restores CameraFollow.
/// Flag: Temp/fd_calc_normals.flag  (optional first line = smoothing angle, default 60)
/// </summary>
[InitializeOnLoad]
public static class Bake160CalcNormals
{
    const string FlagPath = "Temp/fd_calc_normals.flag";
    const string ReportPath = "Temp/fd_calc_normals.txt";
    const string CamRestorePath = "Temp/fd_calc_cam_restore.txt";
    const string ShotDir = "Temp/fd_blotch";
    const string PendingKey = "Bake160CalcNormals.pending";
    const string EnteredPlayKey = "Bake160CalcNormals.enteredPlay";
    const string AngleKey = "Bake160CalcNormals.angle";
    const string ChairFbx = "Assets/Objects/Meshy_Chair_Unity/Meshy_Chair_Unity.fbx";
    const string CoolerFbx =
        "Assets/Objects/Pickable/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture_fbx/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture.fbx";

    static int sPhase;
    static int sWait;
    static StringBuilder sLog;
    static float sAngle = 60f;

    static Bake160CalcNormals()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    static void OnPlay(PlayModeStateChange c)
    {
        if (c == PlayModeStateChange.EnteredEditMode)
            RestoreCam();
    }

    static void Poll()
    {
        if (sPhase == 1)
        {
            sWait++;
            KeepFollowOff();
            if (sWait < 80)
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
            sAngle = SessionState.GetFloat(AngleKey, 60f);
            sPhase = 1;
            sWait = 0;
            return;
        }

        if (!flag && !pending)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        if (flag)
        {
            string body = File.ReadAllText(Full(FlagPath)).Trim();
            File.Delete(Full(FlagPath));
            float a;
            if (float.TryParse(body, out a) && a > 0f && a <= 180f)
                sAngle = a;
            else
                sAngle = 60f;
        }

        try
        {
            BeginEdit();
        }
        catch (System.Exception e)
        {
            File.WriteAllText(Full(ReportPath), "FAIL BeginEdit " + e);
            sPhase = 0;
            SessionState.SetBool(PendingKey, false);
            SessionState.SetBool(EnteredPlayKey, false);
        }
    }

    [MenuItem("Tools/Objects/160 Calculate Vertex Normals (60)")]
    public static void Menu()
    {
        File.WriteAllText(Full(FlagPath), "60\n");
    }

    static void BeginEdit()
    {
        sLog = new StringBuilder();
        Directory.CreateDirectory(Full(ShotDir));
        sLog.AppendLine("=== Ticket 160 Calculate vertex normals ===");
        sLog.AppendLine("smoothingAngle=" + sAngle);

        DumpFbx(sLog, "BEFORE chair", ChairFbx);
        DumpFbx(sLog, "BEFORE cooler", CoolerFbx);

        ApplyCalculate(ChairFbx, sAngle, sLog);
        ApplyCalculate(CoolerFbx, sAngle, sLog);

        DumpFbx(sLog, "AFTER chair", ChairFbx);
        DumpFbx(sLog, "AFTER cooler", CoolerFbx);

        GameObject chair = GameObject.Find("Meshy_Chair_Spike");
        GameObject cooler = FindCooler();
        if (cooler == null && chair != null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(CoolerFbx);
            if (src != null)
            {
                cooler = (GameObject)Object.Instantiate(src);
                cooler.name = "DIAG_COOLER_CALC";
                cooler.transform.position = chair.transform.position + new Vector3(1.2f, 0f, 0f);
                cooler.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
                var rb = cooler.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
                sLog.AppendLine("spawned unsaved DIAG_COOLER_CALC next to chair");
            }
        }

        if (chair == null)
        {
            sLog.AppendLine("FAIL no Meshy_Chair_Spike");
            File.WriteAllText(Full(ReportPath), sLog.ToString());
            sPhase = 0;
            return;
        }

        PoseCamera(chair, cooler);
        SessionState.SetBool(PendingKey, true);
        SessionState.SetBool(EnteredPlayKey, true);
        SessionState.SetFloat(AngleKey, sAngle);
        File.WriteAllText(Full(ReportPath), sLog.ToString() + "\nENTERING PLAY...\n");
        sPhase = 1;
        sWait = 0;
        EditorApplication.isPlaying = true;
    }

    static void GrabPlay()
    {
        KeepFollowOff();
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        Texture2D tex = GrabGameRt();
        try
        {
            var sc = ScreenCapture.CaptureScreenshotAsTexture();
            if (sc != null && sc.height >= 200 && (tex == null || sc.width * sc.height > tex.width * tex.height))
            {
                if (tex != null)
                    Object.DestroyImmediate(tex);
                tex = sc;
            }
            else if (sc != null)
                Object.DestroyImmediate(sc);
        }
        catch { }
        string shot = ShotDir + "/calc_n" + Mathf.RoundToInt(sAngle) + "_play.png";
        if (tex != null && tex.width > 32)
        {
            File.WriteAllBytes(Full(shot), tex.EncodeToPNG());
            sLog.AppendLine("shot=" + shot + " " + tex.width + "x" + tex.height);
            Object.DestroyImmediate(tex);
        }
        else
            sLog.AppendLine("shot grab=null");

        var chair = GameObject.Find("Meshy_Chair_Spike");
        if (chair != null)
        {
            var mf = chair.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                DumpMesh(sLog, "play.chair", mf.sharedMesh);
        }
        sLog.AppendLine("RESULT=OK angle=" + sAngle);
        File.WriteAllText(Full(ReportPath), sLog.ToString());
        Debug.Log("[Bake160CalcNormals]\n" + sLog);
        sPhase = 0;
        SessionState.SetBool(PendingKey, false);
        SessionState.SetBool(EnteredPlayKey, false);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static void ApplyCalculate(string fbx, float angle, StringBuilder sb)
    {
        var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (mi == null)
        {
            sb.AppendLine("FAIL missing importer " + fbx);
            return;
        }
        bool dirty = false;
        if (mi.importNormals != ModelImporterNormals.Calculate)
        {
            mi.importNormals = ModelImporterNormals.Calculate;
            dirty = true;
        }
        if (!Mathf.Approximately(mi.normalSmoothingAngle, angle))
        {
            mi.normalSmoothingAngle = angle;
            dirty = true;
        }
        if (mi.importTangents != ModelImporterTangents.CalculateMikk)
        {
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            dirty = true;
        }
        if (dirty)
        {
            mi.SaveAndReimport();
            sb.AppendLine("reimported " + fbx + " Calculate angle=" + angle);
        }
        else
            sb.AppendLine("already Calculate angle=" + angle + " " + fbx);
    }

    static void DumpFbx(StringBuilder sb, string label, string path)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        sb.AppendLine("--- " + label + " ---");
        if (mi == null)
        {
            sb.AppendLine("  missing");
            return;
        }
        sb.AppendLine("  importNormals=" + mi.importNormals
            + " smoothAngle=" + mi.normalSmoothingAngle
            + " tangents=" + mi.importTangents);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null)
            return;
        var filters = go.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i].sharedMesh != null)
                DumpMesh(sb, filters[i].sharedMesh.name, filters[i].sharedMesh);
        }
    }

    static void DumpMesh(StringBuilder sb, string name, Mesh mesh)
    {
        var n = mesh.normals;
        var c = mesh.colors;
        int zero = 0;
        Vector3 sum = Vector3.zero;
        float minY = 1f, maxY = -1f;
        for (int i = 0; i < n.Length; i++)
        {
            if (n[i].sqrMagnitude < 1e-8f)
                zero++;
            sum += n[i];
            if (n[i].y < minY) minY = n[i].y;
            if (n[i].y > maxY) maxY = n[i].y;
        }
        Vector3 avg = n.Length > 0 ? sum / n.Length : Vector3.zero;
        sb.AppendLine("  mesh=" + name + " verts=" + mesh.vertexCount
            + " normals=" + n.Length
            + " zeroN=" + zero
            + " avgN=" + avg
            + " nY=[" + minY.ToString("F2") + "," + maxY.ToString("F2") + "]"
            + " colors=" + (c != null ? c.Length : 0)
            + " tangents=" + (mesh.tangents != null ? mesh.tangents.Length : 0));
    }

    static GameObject FindCooler()
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null)
                continue;
            string n = all[i].name;
            if (n.IndexOf("Water_Coole", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("DIAG_COOLER", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return all[i].gameObject;
        }
        return null;
    }

    static void PoseCamera(GameObject chair, GameObject cooler)
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        Behaviour follow = null;
        foreach (var c in cam.GetComponents<Behaviour>())
        {
            if (c.GetType().Name == "CameraFollow")
            {
                follow = c;
                break;
            }
        }
        bool followOn = follow != null && follow.enabled;
        File.WriteAllText(Full(CamRestorePath),
            cam.transform.position.x + "," + cam.transform.position.y + "," + cam.transform.position.z + "|"
            + cam.transform.rotation.x + "," + cam.transform.rotation.y + ","
            + cam.transform.rotation.z + "," + cam.transform.rotation.w + "|"
            + (cam.orthographic ? cam.orthographicSize : 6f) + "|"
            + (followOn ? 1 : 0));
        if (follow != null)
            follow.enabled = false;

        Bounds b = Encapsulate(chair);
        if (cooler != null)
            b.Encapsulate(Encapsulate(cooler));
        cam.orthographic = true;
        cam.orthographicSize = 2.2f;
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(8f, -7f, 8f).normalized);
        cam.transform.position = b.center + new Vector3(-4.8f, 4.2f, -4.8f);
    }

    static Bounds Encapsulate(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds(go.transform.position, Vector3.one);
        bool any = false;
        for (int i = 0; i < rends.Length; i++)
        {
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
        foreach (var c in cam.GetComponents<Behaviour>())
        {
            if (c.GetType().Name == "CameraFollow")
                c.enabled = followOn != 0;
        }
        try { File.Delete(p); } catch { }
    }

    static void KeepFollowOff()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        foreach (var c in cam.GetComponents<Behaviour>())
        {
            if (c.GetType().Name == "CameraFollow" && c.enabled)
                c.enabled = false;
        }
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
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public))
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

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
