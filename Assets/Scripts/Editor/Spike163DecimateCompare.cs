using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// SPIKE (pre-163) — THROWAWAY. Side-by-side Play-mode capture of the raw printer
/// mesh against its Blender-decimated copies (Collapse mode, ratio 0.5 / 0.25).
///
/// Not part of the automated pipeline: nothing here is called by 148/152/153/149/150,
/// no ObjectProperties field is written, and the approved printer.prefab / printer.mat
/// are read-only inputs. All spawned scenery lives under a DO_NOT_SAVE parent that is
/// destroyed when Play exits, and GridFoundation is never saved.
///
/// Flag: Temp/spike163.flag  (first line: "050" or "025")
/// Report: Temp/spike163/report.txt   Shot: Temp/spike163/spike163_&lt;variant&gt;_play.png
/// </summary>
[InitializeOnLoad]
public static class Spike163DecimateCompare
{
    const string OriginalFbx =
        "Assets/Objects/Pickable/printer/Meshy_AI_Cream_Cushioned_Woode_0901123805_texture.fbx";
    const string PrinterMat = "Assets/Objects/Pickable/printer/printer.mat";
    const string Dec050Fbx = "Assets/_Spike163/Printer_Dec050/Printer_Dec050.fbx";
    const string Dec025Fbx = "Assets/_Spike163/Printer_Dec025/Printer_Dec025.fbx";

    const string FlagPath = "Temp/spike163.flag";
    const string OutDir = "Temp/spike163";
    const string ReportPath = "Temp/spike163/report.txt";
    const string ParentName = "Spike163_DO_NOT_SAVE";
    const string PendingKey = "Spike163.pending";
    const string EnteredPlayKey = "Spike163.enteredPlay";
    const string VariantKey = "Spike163.variant";
    const string ViewKey = "Spike163.view";

    // Ticket 160 pipeline default (MeshyModelImportDefaults): Calculate @ 90, Mikk.
    const float SmoothingAngle = MeshyModelImportDefaults.NormalSmoothingAngle;

    static readonly Vector3 ViewDir = new Vector3(8f, -7f, 8f).normalized;

    static int sPhase;
    static int sWait;
    static StringBuilder sLog;
    static string sVariant = "050";

    static Spike163DecimateCompare()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    [MenuItem("Tools/Objects/Spike163 Decimate Compare 0.5")]
    public static void Menu050() { Kick("050"); }

    [MenuItem("Tools/Objects/Spike163 Decimate Compare 0.25")]
    public static void Menu025() { Kick("025"); }

    static void Kick(string variant)
    {
        Directory.CreateDirectory(Full(OutDir));
        File.WriteAllText(Full(FlagPath), variant + "\n");
    }

    static void OnPlay(PlayModeStateChange c)
    {
        if (c != PlayModeStateChange.EnteredEditMode)
            return;
        GameObject parent = GameObject.Find(ParentName);
        if (parent != null)
            UnityEngine.Object.DestroyImmediate(parent);
    }

    static void Poll()
    {
        if (sPhase == 1)
        {
            sWait++;
            if (sWait < 80)
                return;
            GrabPlay();
            return;
        }

        if (sPhase != 0 || EditorApplication.isCompiling)
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
            sVariant = SessionState.GetString(VariantKey, "050");
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
            sVariant = body == "025" ? "025" : "050";
        }

        try
        {
            BeginEdit();
        }
        catch (Exception e)
        {
            File.WriteAllText(Full(ReportPath), "FAIL BeginEdit " + e);
            Debug.LogError("[Spike163] " + e);
            sPhase = 0;
            SessionState.SetBool(PendingKey, false);
            SessionState.SetBool(EnteredPlayKey, false);
        }
    }

    static void BeginEdit()
    {
        sLog = new StringBuilder();
        Directory.CreateDirectory(Full(OutDir));
        string decFbx = sVariant == "025" ? Dec025Fbx : Dec050Fbx;
        float ratio = sVariant == "025" ? 0.25f : 0.5f;

        sLog.AppendLine("=== SPIKE163 decimate compare — ratio " + ratio + " ===");
        sLog.AppendLine("Decimate modifier: COLLAPSE, ratio=" + ratio + " (Blender, applied pre-export)");

        // Step 4: ticket 160 fix on the decimated output — topology changed, so the
        // source FBX's own normals cannot be assumed valid.
        DumpImporter("BEFORE decimated", decFbx);
        ApplyCalculateNormals(decFbx);
        DumpImporter("AFTER decimated", decFbx);
        DumpImporter("original (pipeline)", OriginalFbx);

        GameObject anchor = GameObject.Find("Meshy_Chair_Spike");
        if (anchor == null)
            throw new Exception("no Meshy_Chair_Spike anchor in scene");

        GameObject old = GameObject.Find(ParentName);
        if (old != null)
            UnityEngine.Object.DestroyImmediate(old);
        var parent = new GameObject(ParentName);

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(PrinterMat);
        // Clear floor area behind the props row — nothing else renders near (0,0,-5).
        Vector3 basePos = new Vector3(anchor.transform.position.x - 1.5f, 0f, -5f);

        // Offset along the capture camera's right axis so the pair reads level, not staggered.
        Vector3 right = Vector3.Cross(Vector3.up, ViewDir).normalized;
        GameObject a = Spawn(parent.transform, OriginalFbx, basePos - right * 0.7f,
            "ORIGINAL " + Tris(OriginalFbx) + " tris", mat);
        GameObject b = Spawn(parent.transform, decFbx, basePos + right * 0.7f,
            "DECIMATE " + ratio + "  " + Tris(decFbx) + " tris", mat);

        sLog.AppendLine("tris original=" + Tris(OriginalFbx) + " decimated=" + Tris(decFbx));
        RecordView(a, b);

        SessionState.SetBool(PendingKey, true);
        SessionState.SetBool(EnteredPlayKey, true);
        SessionState.SetString(VariantKey, sVariant);
        File.WriteAllText(Full(ReportPath), sLog.ToString() + "\nENTERING PLAY...\n");
        sPhase = 1;
        sWait = 0;
        EditorApplication.isPlaying = true;
    }

    static int Tris(string fbx)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (go == null)
            return -1;
        int t = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh != null)
                t += mf.sharedMesh.triangles.Length / 3;
        }
        return t;
    }

    static void ApplyCalculateNormals(string fbx)
    {
        var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (mi == null)
        {
            sLog.AppendLine("FAIL missing importer " + fbx);
            return;
        }
        bool dirty = false;
        if (mi.importNormals != ModelImporterNormals.Calculate)
        {
            mi.importNormals = ModelImporterNormals.Calculate;
            dirty = true;
        }
        if (!Mathf.Approximately(mi.normalSmoothingAngle, SmoothingAngle))
        {
            mi.normalSmoothingAngle = SmoothingAngle;
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
            sLog.AppendLine("reimported Calculate angle=" + SmoothingAngle + " " + fbx);
        }
        else
            sLog.AppendLine("already Calculate angle=" + SmoothingAngle + " " + fbx);
    }

    static void DumpImporter(string label, string fbx)
    {
        var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
        sLog.AppendLine("--- " + label + " " + fbx + " ---");
        if (mi == null)
        {
            sLog.AppendLine("  missing");
            return;
        }
        sLog.AppendLine("  importNormals=" + mi.importNormals
            + " smoothAngle=" + mi.normalSmoothingAngle
            + " tangents=" + mi.importTangents
            + " tris=" + Tris(fbx));
    }

    static GameObject Spawn(Transform parent, string fbx, Vector3 pos, string label, Material mat)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (src == null)
            throw new Exception("missing FBX " + fbx);
        var go = (GameObject)UnityEngine.Object.Instantiate(src);
        go.name = "SPIKE163_" + Path.GetFileNameWithoutExtension(fbx);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 35f, 0f));

        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.DestroyImmediate(col);
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
            UnityEngine.Object.DestroyImmediate(rb);

        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            r.enabled = true;
            r.forceRenderingOff = false;
            r.allowOcclusionWhenDynamic = false;
            if (mat != null)
                r.sharedMaterial = mat;
        }

        // Sit on the anchor's floor plane.
        Bounds bb = Encapsulate(go);
        Vector3 p = go.transform.position;
        p.y += pos.y - bb.min.y;
        go.transform.position = p;

        AddLabel(go, label);
        sLog.AppendLine("spawned " + go.name + " at " + go.transform.position + " mat=" + (mat != null ? mat.name : "null"));
        return go;
    }

    static void AddLabel(GameObject go, string text)
    {
        var label = new GameObject("label");
        label.transform.SetParent(go.transform, false);
        Bounds b = Encapsulate(go);
        label.transform.position = go.transform.position + Vector3.up * (b.size.y + 0.12f);
        var tm = label.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 48;
        tm.characterSize = 0.03f;
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            tm.font = font;
        label.transform.rotation = Quaternion.LookRotation(ViewDir);
    }

    static void GrabPlay()
    {
        Texture2D tex = RenderCapture();
        string shot = OutDir + "/spike163_" + sVariant + "_play.png";
        if (tex != null && tex.width > 32)
        {
            File.WriteAllBytes(Full(shot), tex.EncodeToPNG());
            sLog.AppendLine("shot=" + shot + " " + tex.width + "x" + tex.height);
            UnityEngine.Object.DestroyImmediate(tex);
        }
        else
            sLog.AppendLine("shot grab=null");

        sLog.AppendLine("RESULT=OK variant=" + sVariant);
        File.WriteAllText(Full(ReportPath), sLog.ToString());
        Debug.Log("[Spike163]\n" + sLog);
        sPhase = 0;
        SessionState.SetBool(PendingKey, false);
        SessionState.SetBool(EnteredPlayKey, false);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static Bounds Encapsulate(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds(go.transform.position, Vector3.one * 0.01f);
        bool any = false;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] is MeshRenderer == false)
                continue;
            if (!any) { b = rends[i].bounds; any = true; }
            else b.Encapsulate(rends[i].bounds);
        }
        return b;
    }

    /// <summary>
    /// Stores the framing for the capture camera. Main Camera / CameraFollow are never
    /// touched — the shot is rendered by a throwaway camera instead, so gameplay
    /// (and any other session driving this editor) is left alone.
    /// </summary>
    static void RecordView(GameObject a, GameObject b)
    {
        Bounds bounds = Encapsulate(a);
        bounds.Encapsulate(Encapsulate(b));
        float size = Mathf.Max(bounds.size.y, Mathf.Max(bounds.size.x, bounds.size.z) / 1.5f) * 0.8f + 0.15f;
        Vector3 pos = bounds.center - ViewDir * 8f;
        SessionState.SetString(ViewKey, pos.x + "," + pos.y + "," + pos.z + "|" + size);
        sLog.AppendLine("capture view pos=" + pos + " orthoSize=" + size);
    }

    static Texture2D RenderCapture()
    {
        string v = SessionState.GetString(ViewKey, "");
        if (v.Length == 0)
            return null;
        string[] parts = v.Split('|');
        string[] xyz = parts[0].Split(',');
        float size;
        if (parts.Length < 2 || !float.TryParse(parts[1], out size))
            return null;
        var pos = new Vector3(float.Parse(xyz[0]), float.Parse(xyz[1]), float.Parse(xyz[2]));

        var camGo = new GameObject("Spike163_CaptureCam");
        var cam = camGo.AddComponent<Camera>();
        Camera main = Camera.main;
        if (main != null)
        {
            cam.CopyFrom(main);
            var srcData = main.GetComponent("UniversalAdditionalCameraData");
            if (srcData != null)
                camGo.AddComponent(srcData.GetType());
        }
        cam.orthographic = true;
        cam.orthographicSize = size;
        cam.transform.rotation = Quaternion.LookRotation(ViewDir);
        cam.transform.position = pos;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.depth = -100f;
        cam.targetTexture = null;

        var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(camGo);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        return tex;
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
