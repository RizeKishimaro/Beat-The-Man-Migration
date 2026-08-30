using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 156 — verify Approve builds a working prefab and Unapprove deletes it
/// for CreamChair_Raw153.
/// Flag: Temp/bake156_verify.flag
/// Report: BlenderSource/Bake/_bake156_unity_report.txt
/// </summary>
[InitializeOnLoad]
public static class Bake156Verify
{
    const string PropsPath =
        "Assets/Objects/Pickable/CreamChair_Raw153/CreamChair_Raw153_Properties.asset";
    const string PrefabPath =
        "Assets/Objects/Pickable/CreamChair_Raw153/CreamChair_Raw153.prefab";
    const string FlagPath = "Temp/bake156_verify.flag";
    const string ReportPath = "BlenderSource/Bake/_bake156_unity_report.txt";

    static Bake156Verify()
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

    [MenuItem("Tools/Objects/Verify 156 CreamChair_Raw153")]
    public static void MenuVerify()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Ticket 156", Truncate(report, 1600), "OK");
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
            Debug.Log("[Bake156Verify]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Bake156Verify] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 156 CreamChair_Raw153 verify ===");

        var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(PropsPath);
        if (props == null)
        {
            sb.AppendLine("FAIL missing " + PropsPath);
            sb.AppendLine("PASS=False");
            return sb.ToString();
        }

        // Baseline: unapproved, no leftover prefab.
        props.approved = false;
        ObjectApprovedPrefabBuilder.TearDown(props);
        props.weight = 0f; // unset → default mass 1
        props.physicsGravity = ObjectPhysicsGravity.Unset; // → On
        props.pickupable = true;
        props.scaleMultiplier = 1f;
        props.pivotOffset = Vector3.zero;
        EditorUtility.SetDirty(props);
        AssetDatabase.SaveAssets();

        bool goneBefore = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null;
        sb.AppendLine("--- Baseline ---");
        sb.AppendLine("prefabAbsentBefore: " + goneBefore);

        // --- Approve / Build ---
        props.approved = true;
        string builtPath;
        try
        {
            builtPath = ObjectApprovedPrefabBuilder.Build(props);
        }
        catch (System.Exception e)
        {
            sb.AppendLine("FAIL build: " + e);
            props.approved = false;
            EditorUtility.SetDirty(props);
            AssetDatabase.SaveAssets();
            sb.AppendLine("PASS=False");
            return sb.ToString();
        }

        sb.AppendLine("--- After Approve ---");
        sb.AppendLine("builtPath: " + builtPath);
        sb.AppendLine("props.prefabPath: " + props.prefabPath);
        sb.AppendLine("props.approved: " + props.approved);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        sb.AppendLine("prefabExists: " + (prefab != null));

        bool massOk = false, gravityOk = false, carryOk = false, hierarchyOk = false, stubOk = false;
        if (prefab != null)
        {
            Transform wrapper = prefab.transform.Find("Wrapper");
            Transform mesh = wrapper != null ? wrapper.Find("Mesh") : null;
            hierarchyOk = wrapper != null && mesh != null
                && Approximately(wrapper.localScale, Vector3.one)
                && Approximately(mesh.localPosition, Vector3.zero);
            sb.AppendLine("hierarchy Wrapper/Mesh: " + hierarchyOk);

            Rigidbody rb = prefab.GetComponent<Rigidbody>();
            massOk = rb != null && Mathf.Abs(rb.mass - ObjectApprovedPrefabBuilder.DefaultMass) < 0.001f;
            gravityOk = rb != null && rb.useGravity;
            sb.AppendLine("mass: " + (rb != null ? rb.mass.ToString() : "null") + " (want " + ObjectApprovedPrefabBuilder.DefaultMass + ")");
            sb.AppendLine("useGravity: " + (rb != null && rb.useGravity));

            CarryableItem carry = prefab.GetComponent<CarryableItem>();
            PushableItem push = prefab.GetComponent<PushableItem>();
            BoxCollider box = prefab.GetComponent<BoxCollider>();
            GridFootprint fp = prefab.GetComponent<GridFootprint>();
            var stub = prefab.GetComponent<CreamChair_Raw153>();
            var imported = prefab.GetComponent<ImportedPickable>();

            carryOk = carry != null && push != null && box != null
                && fp != null && fp.type == AssetGridType.Object;
            stubOk = stub != null && imported != null && imported.properties == props;
            sb.AppendLine("CarryableItem: " + (carry != null));
            sb.AppendLine("PushableItem: " + (push != null));
            sb.AppendLine("BoxCollider: " + (box != null));
            sb.AppendLine("GridFootprint Object: " + (fp != null && fp.type == AssetGridType.Object));
            sb.AppendLine("stub+properties: " + stubOk);
        }

        bool approveOk = prefab != null && massOk && gravityOk && carryOk && hierarchyOk && stubOk
            && props.prefabPath == PrefabPath;
        sb.AppendLine("CHECK approve builds working prefab: " + approveOk);

        // --- Unapprove / TearDown ---
        props.approved = false;
        ObjectApprovedPrefabBuilder.TearDown(props);
        EditorUtility.SetDirty(props);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameObject after = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        bool deleted = after == null && string.IsNullOrEmpty(props.prefabPath);
        sb.AppendLine("--- After Unapprove ---");
        sb.AppendLine("prefabExists: " + (after != null));
        sb.AppendLine("props.prefabPath: '" + (props.prefabPath ?? "") + "'");
        sb.AppendLine("CHECK unapprove deletes prefab: " + deleted);

        // Second pass: explicit weight + gravity Off
        props.weight = 4.5f;
        props.physicsGravity = ObjectPhysicsGravity.Off;
        props.approved = true;
        ObjectApprovedPrefabBuilder.Build(props);
        GameObject prefab2 = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Rigidbody rb2 = prefab2 != null ? prefab2.GetComponent<Rigidbody>() : null;
        bool weightPass = rb2 != null
            && Mathf.Abs(rb2.mass - 4.5f) < 0.001f
            && rb2.useGravity == false;
        sb.AppendLine("--- Weight/gravity override ---");
        sb.AppendLine("mass: " + (rb2 != null ? rb2.mass.ToString() : "null"));
        sb.AppendLine("useGravity: " + (rb2 != null && rb2.useGravity));
        sb.AppendLine("CHECK weight=4.5 gravity=Off: " + weightPass);

        // Restore pending baseline (no prefab, blank weight).
        props.approved = false;
        props.weight = 0f;
        props.physicsGravity = ObjectPhysicsGravity.Unset;
        ObjectApprovedPrefabBuilder.TearDown(props);
        EditorUtility.SetDirty(props);
        AssetDatabase.SaveAssets();

        bool restored = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null && !props.approved;
        sb.AppendLine("--- Restored pending baseline ---");
        sb.AppendLine("prefabAbsent: " + restored);
        sb.AppendLine("approved: " + props.approved);

        bool all = approveOk && deleted && weightPass && restored;
        sb.AppendLine("PASS=" + all);
        return sb.ToString();
    }

    static bool Approximately(Vector3 a, Vector3 b)
    {
        return Vector3.Distance(a, b) < 0.001f;
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
