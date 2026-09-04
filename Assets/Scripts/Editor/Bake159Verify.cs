using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 159 — cell-fit default for new Pickable Properties; Nodes stay 1;
/// existing assets are not rewritten; exemption does not silently rewrite.
/// Flag: Temp/bake159_verify.flag
/// Report: BlenderSource/Bake/_bake159_unity_report.txt
/// </summary>
[InitializeOnLoad]
public static class Bake159Verify
{
    const string ChairFolder = "Assets/Objects/Pickable/CreamChair_Raw153";
    const string ChairProps =
        "Assets/Objects/Pickable/CreamChair_Raw153/CreamChair_Raw153_Properties.asset";
    const string PickableFolder = "Assets/_Bake159/Bake159CellFit";
    const string NodeFolder = "Assets/_Bake159/Bake159NodeFit";
    const string FlagPath = "Temp/bake159_verify.flag";
    const string ReportPath = "BlenderSource/Bake/_bake159_unity_report.txt";

    static Bake159Verify()
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

    [MenuItem("Tools/Objects/Verify 159 Cell-Fit Scaling")]
    public static void MenuVerify()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Ticket 159", Truncate(report, 1600), "OK");
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
            Debug.Log("[Bake159Verify]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Bake159Verify] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 159 cell-fit verify ===");
        CleanupTemp();

        bool formulaOk = CheckFormula(sb);
        bool pickableOk = CheckNewPickable(sb);
        bool existingOk = CheckExistingChairUnchanged(sb);
        bool nodeOk = CheckNewNode(sb);
        bool exemptOk = CheckExemptDoesNotRewrite(sb);

        CleanupTemp();

        bool all = formulaOk && pickableOk && existingOk && nodeOk && exemptOk;
        sb.AppendLine();
        sb.AppendLine(all ? "RESULT=OK" : "RESULT=FAIL");
        sb.AppendLine("PASS=" + all);
        return sb.ToString();
    }

    static bool CheckFormula(StringBuilder sb)
    {
        sb.AppendLine("--- Formula max(X,Z) ---");
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.hideFlags = HideFlags.HideAndDontSave;
        cube.transform.position = Vector3.zero;
        cube.transform.rotation = Quaternion.identity;
        cube.transform.localScale = new Vector3(0.4f, 2f, 0.8f);
        bool measured = ObjectCellFitScaling.TryMeasureFootprintXZ(cube, out float maxXZ);
        float mul = ObjectCellFitScaling.MultiplierFromFootprint(maxXZ);
        Object.DestroyImmediate(cube);

        sb.AppendLine("cube scale (0.4, 2, 0.8) maxXZ=" + maxXZ + " mul=" + mul + " (want 0.8 / 1.25)");
        bool zeroOk = Mathf.Abs(ObjectCellFitScaling.MultiplierFromFootprint(0f) - 1f) < 0.001f;
        sb.AppendLine("zero footprint → 1: " + zeroOk);

        bool ok = measured
            && Mathf.Abs(maxXZ - 0.8f) < 0.02f
            && Mathf.Abs(mul - 1.25f) < 0.02f
            && zeroOk;
        sb.AppendLine("CHECK formula: " + ok);
        return ok;
    }

    static bool CheckNewPickable(StringBuilder sb)
    {
        sb.AppendLine("--- New Pickable generate ---");
        string srcFbx = FindFbx(ChairFolder);
        if (string.IsNullOrEmpty(srcFbx))
        {
            sb.AppendLine("FAIL no CreamChair FBX");
            sb.AppendLine("CHECK new pickable: False");
            return false;
        }

        EnsureFolder("Assets/_Bake159");
        EnsureFolder(PickableFolder);
        string destFbx = PickableFolder + "/Bake159CellFit.fbx";
        if (!AssetDatabase.CopyAsset(srcFbx, destFbx))
        {
            sb.AppendLine("FAIL CopyAsset " + srcFbx + " → " + destFbx);
            sb.AppendLine("CHECK new pickable: False");
            return false;
        }
        AssetDatabase.ImportAsset(destFbx, ImportAssetOptions.ForceUpdate);

        string log = ObjectFolderGenerator.EnsureProperties(
            PickableFolder, "Bake159CellFit", ObjectKind.Pickable, destFbx);
        sb.AppendLine("EnsureProperties: " + log);

        string propsPath = PickableFolder + "/Bake159CellFit_Properties.asset";
        var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(propsPath);
        if (props == null)
        {
            sb.AppendLine("FAIL missing " + propsPath);
            sb.AppendLine("CHECK new pickable: False");
            return false;
        }

        bool computed = ObjectCellFitScaling.TryComputeForFbx(destFbx, out float expect, out float maxXZ);
        sb.AppendLine("kind: " + props.kind);
        sb.AppendLine("exemptFromCellFitScaling: " + props.exemptFromCellFitScaling);
        sb.AppendLine("scaleMultiplier: " + props.scaleMultiplier);
        sb.AppendLine("computed fit: " + expect + " maxXZ=" + maxXZ);
        bool ok = computed
            && props.kind == ObjectKind.Pickable
            && !props.exemptFromCellFitScaling
            && !props.approved
            && Mathf.Abs(props.scaleMultiplier - expect) < 0.001f;
        sb.AppendLine("CHECK new pickable: " + ok);
        return ok;
    }

    static bool CheckExistingChairUnchanged(StringBuilder sb)
    {
        sb.AppendLine("--- Existing CreamChair (must stay 1) ---");
        var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(ChairProps);
        if (props == null)
        {
            sb.AppendLine("FAIL missing " + ChairProps);
            sb.AppendLine("CHECK existing unchanged: False");
            return false;
        }
        sb.AppendLine("scaleMultiplier: " + props.scaleMultiplier);
        sb.AppendLine("exemptFromCellFitScaling: " + props.exemptFromCellFitScaling);
        bool ok = Mathf.Abs(props.scaleMultiplier - 1f) < 0.001f
            && !props.exemptFromCellFitScaling;
        sb.AppendLine("CHECK existing unchanged: " + ok);
        return ok;
    }

    static bool CheckNewNode(StringBuilder sb)
    {
        sb.AppendLine("--- New Node generate ---");
        string srcFbx = FindFbx(ChairFolder);
        if (string.IsNullOrEmpty(srcFbx))
        {
            sb.AppendLine("CHECK new node: False");
            return false;
        }

        EnsureFolder("Assets/_Bake159");
        EnsureFolder(NodeFolder);
        string destFbx = NodeFolder + "/Bake159NodeFit.fbx";
        if (!AssetDatabase.CopyAsset(srcFbx, destFbx))
        {
            sb.AppendLine("FAIL CopyAsset node");
            sb.AppendLine("CHECK new node: False");
            return false;
        }
        AssetDatabase.ImportAsset(destFbx, ImportAssetOptions.ForceUpdate);
        string log = ObjectFolderGenerator.EnsureProperties(
            NodeFolder, "Bake159NodeFit", ObjectKind.Node, destFbx);
        sb.AppendLine("EnsureProperties: " + log);

        string propsPath = NodeFolder + "/Bake159NodeFit_Properties.asset";
        var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(propsPath);
        if (props == null)
        {
            sb.AppendLine("FAIL missing " + propsPath);
            sb.AppendLine("CHECK new node: False");
            return false;
        }

        sb.AppendLine("kind: " + props.kind);
        sb.AppendLine("scaleMultiplier: " + props.scaleMultiplier);
        bool ok = props.kind == ObjectKind.Node
            && Mathf.Abs(props.scaleMultiplier - 1f) < 0.001f
            && !props.exemptFromCellFitScaling;
        sb.AppendLine("CHECK new node: " + ok);
        return ok;
    }

    static bool CheckExemptDoesNotRewrite(StringBuilder sb)
    {
        sb.AppendLine("--- Exempt does not rewrite existing scale ---");
        string propsPath = PickableFolder + "/Bake159CellFit_Properties.asset";
        var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(propsPath);
        if (props == null)
        {
            sb.AppendLine("FAIL missing pickable props for exempt check");
            sb.AppendLine("CHECK exempt no rewrite: False");
            return false;
        }

        float before = props.scaleMultiplier;
        props.exemptFromCellFitScaling = true;
        EditorUtility.SetDirty(props);
        AssetDatabase.SaveAssets();

        string log = ObjectFolderGenerator.EnsureProperties(
            PickableFolder, "Bake159CellFit", ObjectKind.Pickable,
            PickableFolder + "/Bake159CellFit.fbx");
        sb.AppendLine("EnsureProperties (exists): " + log);
        AssetDatabase.ImportAsset(propsPath, ImportAssetOptions.ForceUpdate);
        props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(propsPath);

        sb.AppendLine("scale before: " + before);
        sb.AppendLine("scale after: " + (props != null ? props.scaleMultiplier.ToString() : "null"));
        sb.AppendLine("exempt: " + (props != null && props.exemptFromCellFitScaling));
        bool ok = props != null
            && props.exemptFromCellFitScaling
            && Mathf.Abs(props.scaleMultiplier - before) < 0.001f
            && log != null && log.Contains("properties=exists");
        sb.AppendLine("CHECK exempt no rewrite: " + ok);
        return ok;
    }

    static void CleanupTemp()
    {
        DeleteFolder("Assets/_Bake159");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void DeleteFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            AssetDatabase.DeleteAsset(folder);
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;
        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = Path.GetFileName(folder);
        if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
            AssetDatabase.CreateFolder(parent, name);
    }

    static string FindFbx(string folder)
    {
        string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", folder));
        if (!Directory.Exists(abs))
            return null;
        foreach (string file in Directory.GetFiles(abs, "*.fbx"))
            return folder + "/" + Path.GetFileName(file);
        foreach (string file in Directory.GetFiles(abs, "*.FBX"))
            return folder + "/" + Path.GetFileName(file);
        return null;
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
