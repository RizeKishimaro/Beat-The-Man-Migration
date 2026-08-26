using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 152 — measure production bake import + safety-net landing.
/// Batch: Unity -batchmode -executeMethod Bake152Verify.RunFromBatch
/// </summary>
public static class Bake152Verify
{
    const string BakedChair =
        "Assets/_Bake152/CreamChair/CreamChair.fbx";
    const string KnownGoodPath = "Assets/Objects/Meshy_Chair_Unity/Meshy_Chair_Unity.fbx";
    const string RawChair =
        "Assets/Objects/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture_fbx/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture.fbx";
    const string BakedRacer =
        "Assets/_Bake152/FormulaRacer/FormulaRacer.fbx";
    const string RawRacer =
        "Assets/Vehicles/Meshy_AI_White_Formula_Racer_0803114154_texture_fbx > Reference/Meshy_AI_White_Formula_Racer_0803114154_texture.fbx";
    const string BakedCrate = "Assets/_Bake152/Crate/Crate.fbx";
    const string BakedCube = "Assets/_Bake152/TestCube/TestCube.fbx";
    const string BakedCenteredCube = "Assets/_Bake152/CenteredCube/CenteredCube.fbx";
    const string ReviewJson =
        "Assets/Objects/_Review/Drawer_Cabinet/NEEDS_REVIEW.json";
    const string ReviewFbx =
        "Assets/Objects/_Review/Drawer_Cabinet/Drawer_Cabinet.fbx";
    const string ChairReviewJson =
        "Assets/Objects/_Review/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture/NEEDS_REVIEW.json";
    const string ReportPath = "BlenderSource/Bake/_bake152_unity_report.txt";

    public static void RunFromBatch()
    {
        AssetDatabase.Refresh();
        string report = MeasureAll();
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
        Debug.Log("[Bake152Verify]\n" + report);
    }

    [MenuItem("Tools/Bake 152/Verify Production Bake Import")]
    public static void MenuVerify()
    {
        RunFromBatch();
        EditorUtility.DisplayDialog("Bake 152", Truncate(File.ReadAllText(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath))), 1600), "OK");
    }

    static string MeasureAll()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 152 Unity import verify ===");
        sb.AppendLine("Expect: rotation (0,0,0), scale (1,1,1), floor min.y~0, sibling PNGs.");
        sb.AppendLine();
        Measure(sb, "KNOWN-GOOD manual chair", KnownGoodPath);
        Measure(sb, "RAW Meshy chair (no bake)", RawChair);
        Measure(sb, "BAKED cream chair (152)", BakedChair);
        ListSiblingPngs(sb, BakedChair);
        Measure(sb, "RAW Formula Racer (no bake)", RawRacer);
        Measure(sb, "BAKED Formula Racer (152)", BakedRacer);
        ListSiblingPngs(sb, BakedRacer);
        Measure(sb, "BAKED crate (floor-origin)", BakedCrate);
        Measure(sb, "BAKED GridAxisTestCube (XY-recenter)", BakedCube);
        Measure(sb, "BAKED centered default cube (min.y should become 0)", BakedCenteredCube);
        sb.AppendLine("--- cream chair should NOT be in _Review after bake ---");
        sb.AppendLine("stale chair NEEDS_REVIEW.json exists: " + File.Exists(ChairReviewJson));
        sb.AppendLine("--- SAFETY NET: Drawer_Cabinet ---");
        sb.AppendLine("NEEDS_REVIEW.json exists: " + File.Exists(ReviewJson));
        sb.AppendLine("original FBX copied:      " + File.Exists(ReviewFbx));
        sb.AppendLine("baked FBX at review path: " + File.Exists(
            "Assets/Objects/_Review/Drawer_Cabinet/Drawer_Cabinet_baked.fbx"));
        if (File.Exists(ReviewJson))
            sb.AppendLine(File.ReadAllText(ReviewJson));
        return sb.ToString();
    }

    static void ListSiblingPngs(StringBuilder sb, string fbxPath)
    {
        string dir = Path.GetDirectoryName(fbxPath);
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", dir ?? ""));
        sb.AppendLine("sibling PNGs next to " + fbxPath + ":");
        if (!Directory.Exists(full))
        {
            sb.AppendLine("  (folder missing)");
            sb.AppendLine();
            return;
        }
        string[] pngs = Directory.GetFiles(full, "*.png");
        if (pngs.Length == 0)
            sb.AppendLine("  (none)");
        foreach (string p in pngs)
            sb.AppendLine("  " + Path.GetFileName(p));
        sb.AppendLine();
    }

    static void Measure(StringBuilder sb, string label, string assetPath)
    {
        sb.AppendLine("--- " + label + " ---");
        sb.AppendLine("path: " + assetPath);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (go == null)
        {
            sb.AppendLine("MISSING asset (not imported / bake halted?)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("root.localEuler: " + FormatEuler(go.transform.localEulerAngles));
        sb.AppendLine("root.localScale: " + FormatVec(go.transform.localScale));
        sb.AppendLine("root.localPos:   " + FormatVec(go.transform.localPosition));

        var r = go.GetComponentInChildren<Renderer>();
        if (r != null)
        {
            Bounds b = r.bounds;
            sb.AppendLine("renderer.bounds.size: " + FormatVec(b.size));
            sb.AppendLine("renderer.bounds.min:  " + FormatVec(b.min));
            sb.AppendLine("renderer.bounds.max:  " + FormatVec(b.max));
            sb.AppendLine("floor min.y~0: " + (Mathf.Abs(b.min.y) < 0.02f));
        }
        else
            sb.AppendLine("no Renderer");

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
        return Mathf.Abs(Mathf.DeltaAngle(0f, deg));
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
