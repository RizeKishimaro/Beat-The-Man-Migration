using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 162 — scaleMultiplier is written to the approved prefab Wrapper
/// (root stays 1). Changing scale while approved rebuilds the prefab in place
/// (GUID preserved). Instantiates the real printer prefab and measures it.
/// Flag: Temp/bake162_verify.flag
/// Report: BlenderSource/Bake/_bake162_unity_report.txt
/// Shot: Temp/bake162_printer.png
/// </summary>
[InitializeOnLoad]
public static class Bake162Verify
{
    const string PropsPath = "Assets/Objects/Pickable/printer/printer_Properties.asset";
    const string PrefabPath = "Assets/Objects/Pickable/printer/printer.prefab";
    const float CellFit = 1.5562313f;
    const float ProbeScale = 2f;
    const string FlagPath = "Temp/bake162_verify.flag";
    const string ReportPath = "BlenderSource/Bake/_bake162_unity_report.txt";
    const string ShotPath = "Temp/bake162_printer.png";

    static Bake162Verify()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRun();
    }

    [MenuItem("Tools/Objects/Verify 162 Prefab Scale Multiplier")]
    public static void MenuVerify()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Ticket 162", Truncate(report, 1600), "OK");
    }

    static void TryRun()
    {
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
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
            string report = Run();
            WriteReport(report);
            Debug.Log("[Bake162Verify]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Bake162Verify] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 162 prefab Wrapper scale verify ===");

        var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(PropsPath);
        if (props == null)
        {
            sb.AppendLine("FAIL missing " + PropsPath);
            sb.AppendLine("PASS=False");
            return sb.ToString();
        }

        float originalScale = props.scaleMultiplier;
        bool originalApproved = props.approved;
        string originalGuid = AssetDatabase.AssetPathToGUID(PrefabPath);

        try
        {
            props.approved = true;
            props.scaleMultiplier = CellFit;
            EditorUtility.SetDirty(props);
            ObjectApprovedPrefabBuilder.Build(props);

            bool fitOk = CheckPrefab(sb, "cell-fit " + CellFit, CellFit, expectFootprint: 1f);
            string guidAfterFit = AssetDatabase.AssetPathToGUID(PrefabPath);

            props.scaleMultiplier = ProbeScale;
            EditorUtility.SetDirty(props);
            ObjectApprovedPrefabBuilder.Build(props);
            bool probeOk = CheckPrefab(sb, "rebuild at " + ProbeScale, ProbeScale, expectFootprint: -1f);
            string guidAfterProbe = AssetDatabase.AssetPathToGUID(PrefabPath);

            bool guidOk = !string.IsNullOrEmpty(originalGuid)
                && originalGuid == guidAfterFit
                && originalGuid == guidAfterProbe;
            sb.AppendLine("prefab GUID: " + originalGuid);
            sb.AppendLine("CHECK GUID preserved across rebuilds: " + guidOk);

            props.scaleMultiplier = originalScale > 0.01f ? originalScale : CellFit;
            props.approved = true;
            EditorUtility.SetDirty(props);
            ObjectApprovedPrefabBuilder.Build(props);
            AssetDatabase.SaveAssets();

            bool shotOk = CaptureInstance(sb);

            bool all = fitOk && probeOk && guidOk && shotOk;
            sb.AppendLine();
            sb.AppendLine(all ? "RESULT=OK" : "RESULT=FAIL");
            sb.AppendLine("PASS=" + all);
            return sb.ToString();
        }
        catch (System.Exception e)
        {
            sb.AppendLine("ERROR " + e);
            sb.AppendLine("PASS=False");
            return sb.ToString();
        }
        finally
        {
            var restore = AssetDatabase.LoadAssetAtPath<ObjectProperties>(PropsPath);
            if (restore != null)
            {
                restore.scaleMultiplier = originalScale > 0.01f ? originalScale : CellFit;
                restore.approved = originalApproved;
                EditorUtility.SetDirty(restore);
                if (restore.approved)
                {
                    try { ObjectApprovedPrefabBuilder.Build(restore); }
                    catch (System.Exception) { }
                }
                AssetDatabase.SaveAssets();
            }
        }
    }

    static bool CheckPrefab(StringBuilder sb, string label, float wantScale, float expectFootprint)
    {
        sb.AppendLine("--- " + label + " ---");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            sb.AppendLine("FAIL missing prefab");
            sb.AppendLine("CHECK " + label + ": False");
            return false;
        }

        Transform wrapper = prefab.transform.Find("Wrapper");
        Vector3 rootScale = prefab.transform.localScale;
        Vector3 wrapScale = wrapper != null ? wrapper.localScale : Vector3.zero;
        sb.AppendLine("root.localScale: " + rootScale);
        sb.AppendLine("Wrapper.localScale: " + wrapScale);

        BoxCollider box = prefab.GetComponent<BoxCollider>();
        if (box != null)
            sb.AppendLine("BoxCollider size: " + box.size);

        bool rootOk = Approximately(rootScale, Vector3.one);
        bool wrapOk = wrapper != null
            && Mathf.Abs(wrapScale.x - wantScale) < 0.002f
            && Mathf.Abs(wrapScale.y - wantScale) < 0.002f
            && Mathf.Abs(wrapScale.z - wantScale) < 0.002f;

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        float maxXZ = 0f;
        try
        {
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            Bounds? b = CollectRendererBounds(inst);
            if (b.HasValue)
            {
                maxXZ = Mathf.Max(b.Value.size.x, b.Value.size.z);
                sb.AppendLine("instance footprint max(X,Z): " + maxXZ);
            }
            else
                sb.AppendLine("instance footprint: (no renderers)");
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }

        bool footOk = maxXZ > 0.01f;
        if (expectFootprint > 0f)
            footOk = Mathf.Abs(maxXZ - expectFootprint) < 0.08f;

        bool ok = rootOk && wrapOk && footOk;
        sb.AppendLine("CHECK " + label + ": " + ok
            + " (root1=" + rootOk + " wrap=" + wrapOk + " foot=" + footOk + ")");
        return ok;
    }

    static bool CaptureInstance(StringBuilder sb)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            sb.AppendLine("CHECK capture: False");
            return false;
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.name = "Bake162_printer_probe";
        inst.transform.position = new Vector3(0f, 0f, 0f);
        inst.hideFlags = HideFlags.DontSave;

        var camGo = new GameObject("Bake162Cam");
        camGo.hideFlags = HideFlags.HideAndDontSave;
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.18f, 0.2f, 1f);
        cam.orthographic = true;
        cam.orthographicSize = 1.2f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 20f;
        camGo.transform.position = new Vector3(0f, 0.8f, -3f);
        camGo.transform.LookAt(new Vector3(0f, 0.6f, 0f));

        bool ok = false;
        try
        {
            int w = 640;
            int h = 480;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ShotPath));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);

            Transform wrapper = inst.transform.Find("Wrapper");
            sb.AppendLine("shot: " + ShotPath + " bytes=" + new FileInfo(full).Length);
            sb.AppendLine("live instance root: " + inst.transform.localScale);
            sb.AppendLine("live instance Wrapper: " + (wrapper != null ? wrapper.localScale.ToString() : "null"));
            ok = File.Exists(full) && wrapper != null
                && Mathf.Abs(wrapper.localScale.x - CellFit) < 0.002f;
            sb.AppendLine("CHECK capture + live Wrapper: " + ok);
        }
        finally
        {
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(inst);
        }
        return ok;
    }

    static Bounds? CollectRendererBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
            return null;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
        {
            if (rends[i] != null)
                b.Encapsulate(rends[i].bounds);
        }
        return b;
    }

    static bool Approximately(Vector3 a, Vector3 b)
    {
        return Mathf.Abs(a.x - b.x) < 0.002f
            && Mathf.Abs(a.y - b.y) < 0.002f
            && Mathf.Abs(a.z - b.z) < 0.002f;
    }

    static void WriteReport(string text)
    {
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, text ?? "");
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max - 1) + "…";
    }
}
