using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 151 — measure FBX import transform/bounds. No scene writes.
/// Batch: Unity -batchmode -executeMethod Spike151Verify.RunFromBatch
/// </summary>
public static class Spike151Verify
{
    const string BakedPath = "Assets/_Spike151/CreamChair_HeadlessBake/CreamChair_HeadlessBake.fbx";
    const string RawPath =
        "Assets/Objects/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture_fbx/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture.fbx";
    const string KnownGoodPath = "Assets/Objects/Meshy_Chair_Unity/Meshy_Chair_Unity.fbx";
    const string CratePath = "Assets/Art/Items/Item_Crate_Base.fbx";
    const string ReportPath = "BlenderSource/Spike151/_spike151_unity_report.txt";

    public static void RunFromBatch()
    {
        AssetDatabase.Refresh();
        string report = MeasureAll();
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
        Debug.Log("[Spike151Verify]\n" + report);
    }

    [MenuItem("Tools/Spike 151/Verify Headless Bake Import")]
    public static void MenuVerify()
    {
        RunFromBatch();
        EditorUtility.DisplayDialog("Spike 151", Truncate(File.ReadAllText(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath))), 1600), "OK");
    }

    static string MeasureAll()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Spike 151 Unity import verify ===");
        sb.AppendLine("Expect (GH5cYKPv): rotation (0,0,0), scale (1,1,1); bounds in metres.");
        sb.AppendLine();
        Measure(sb, "CRATE (known recipe-correct)", CratePath);
        Measure(sb, "RAW Meshy (no Blender)", RawPath);
        Measure(sb, "KNOWN-GOOD manual Blender re-export", KnownGoodPath);
        Measure(sb, "HEADLESS bake (this spike)", BakedPath);
        return sb.ToString();
    }

    static void Measure(StringBuilder sb, string label, string assetPath)
    {
        sb.AppendLine("--- " + label + " ---");
        sb.AppendLine("path: " + assetPath);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (go == null)
        {
            sb.AppendLine("MISSING asset (not imported yet?)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("root.localEuler: " + FormatEuler(go.transform.localEulerAngles));
        sb.AppendLine("root.localScale: " + FormatVec(go.transform.localScale));
        sb.AppendLine("root.localPos:   " + FormatVec(go.transform.localPosition));
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
        {
            if (t == go.transform)
                continue;
            bool rotOkChild = ApproxZero(t.localEulerAngles);
            bool scaleOkChild = ApproxOne(t.localScale);
            sb.AppendLine(
                "  child '" + t.name + "' euler=" + FormatEuler(t.localEulerAngles)
                + " scale=" + FormatVec(t.localScale)
                + " pos=" + FormatVec(t.localPosition)
                + " rot0=" + rotOkChild + " scale1=" + scaleOkChild);
        }

        var r = go.GetComponentInChildren<Renderer>();
        if (r != null)
        {
            Bounds b = r.bounds;
            sb.AppendLine("renderer.bounds.size: " + FormatVec(b.size) + "  (world AABB metres if scale=1)");
            sb.AppendLine("renderer.bounds.min:  " + FormatVec(b.min));
            sb.AppendLine("renderer.bounds.max:  " + FormatVec(b.max));
        }
        else
            sb.AppendLine("no Renderer");

        var mf = go.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Bounds mb = mf.sharedMesh.bounds;
            sb.AppendLine("mesh.bounds.size:     " + FormatVec(mb.size));
            sb.AppendLine("mesh.bounds.center:   " + FormatVec(mb.center));
        }

        bool rotOk = ApproxZero(go.transform.localEulerAngles);
        bool scaleOk = ApproxOne(go.transform.localScale);
        sb.AppendLine("CHECK rotation~0: " + rotOk + "  scale~1: " + scaleOk);

        var im = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (im != null)
            sb.AppendLine("importer globalScale=" + im.globalScale + " useFileScale=" + im.useFileScale);

        sb.AppendLine();
    }

    static bool ApproxZero(Vector3 euler)
    {
        return WrapDelta(euler.x) < 0.5f && WrapDelta(euler.y) < 0.5f && WrapDelta(euler.z) < 0.5f;
    }

    static float WrapDelta(float deg)
    {
        float d = Mathf.Abs(Mathf.DeltaAngle(0f, deg));
        return d;
    }

    static bool ApproxOne(Vector3 s)
    {
        return Mathf.Abs(s.x - 1f) < 0.01f && Mathf.Abs(s.y - 1f) < 0.01f && Mathf.Abs(s.z - 1f) < 0.01f;
    }

    static string FormatEuler(Vector3 v)
    {
        return "(" + v.x.ToString("0.###") + ", " + v.y.ToString("0.###") + ", " + v.z.ToString("0.###") + ")";
    }

    static string FormatVec(Vector3 v)
    {
        return "(" + v.x.ToString("0.####") + ", " + v.y.ToString("0.####") + ", " + v.z.ToString("0.####") + ")";
    }

    static string Truncate(string s, int n)
    {
        return s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
