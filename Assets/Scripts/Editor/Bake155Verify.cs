using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 155 — verify expanded Properties schema on CreamChair_Raw153:
/// new fields blank/unset; existing 149/150 fields unchanged.
/// Flag: Temp/bake155_verify.flag
/// Report: BlenderSource/Bake/_bake155_unity_report.txt
/// </summary>
[InitializeOnLoad]
public static class Bake155Verify
{
    const string PropsPath =
        "Assets/Objects/Pickable/CreamChair_Raw153/CreamChair_Raw153_Properties.asset";
    const string FlagPath = "Temp/bake155_verify.flag";
    const string ReportPath = "BlenderSource/Bake/_bake155_unity_report.txt";

    static Bake155Verify()
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

    [MenuItem("Tools/Objects/Verify 155 CreamChair_Raw153")]
    public static void MenuVerify()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Ticket 155", Truncate(report, 1600), "OK");
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
            Debug.Log("[Bake155Verify]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Bake155Verify] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 155 CreamChair_Raw153 verify ===");

        // Force re-serialize so Unity fills missing YAML keys for new fields.
        AssetDatabase.ImportAsset(PropsPath, ImportAssetOptions.ForceUpdate);
        var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(PropsPath);
        if (props == null)
        {
            sb.AppendLine("FAIL missing " + PropsPath);
            sb.AppendLine("PASS=False");
            return sb.ToString();
        }

        sb.AppendLine("--- Existing 149/150 fields (must be unchanged) ---");
        sb.AppendLine("objectName: " + props.objectName);
        sb.AppendLine("kind: " + props.kind);
        sb.AppendLine("pickupable: " + props.pickupable);
        sb.AppendLine("approved: " + props.approved);
        sb.AppendLine("weight: " + props.weight);
        sb.AppendLine("price: " + props.price);
        sb.AppendLine("category: '" + (props.category ?? "") + "'");
        sb.AppendLine("stackable: " + props.stackable);
        sb.AppendLine("sizeTier: " + props.sizeTier);
        sb.AppendLine("physicsGravity: " + props.physicsGravity);
        sb.AppendLine("scaleMultiplier: " + props.scaleMultiplier);
        sb.AppendLine("pivotOffset: " + props.pivotOffset);

        bool existingOk =
            props.objectName == "CreamChair_Raw153"
            && props.kind == ObjectKind.Pickable
            && props.pickupable
            && !props.approved
            && Mathf.Abs(props.weight) < 0.0001f
            && Mathf.Abs(props.price) < 0.0001f
            && string.IsNullOrEmpty(props.category)
            && props.stackable == ObjectTriState.Unset
            && props.sizeTier == ObjectSizeTier.Unset
            && props.physicsGravity == ObjectPhysicsGravity.Unset
            && Mathf.Abs(props.scaleMultiplier - 1f) < 0.001f
            && props.pivotOffset == Vector3.zero;
        sb.AppendLine("CHECK existing 149/150 unchanged: " + existingOk);

        sb.AppendLine("--- New 155 fields (must be blank/unset) ---");
        sb.AppendLine("interaction: " + props.interaction);
        sb.AppendLine("placementMethod: " + props.placementMethod);
        sb.AppendLine("nodeCompat: " + props.nodeCompat + " (" + (int)props.nodeCompat + ")");
        sb.AppendLine("animationClips nullOrEmpty: "
            + (props.animationClips == null || props.animationClips.Length == 0));
        sb.AppendLine("animatorController: " + (props.animatorController == null ? "null" : props.animatorController.name));
        sb.AppendLine("sfx.pickup: " + (props.sfx.pickup == null ? "null" : props.sfx.pickup.name));
        sb.AppendLine("sfx.drop: " + (props.sfx.drop == null ? "null" : props.sfx.drop.name));
        sb.AppendLine("sfx.use: " + (props.sfx.use == null ? "null" : props.sfx.use.name));
        sb.AppendLine("sfx.open: " + (props.sfx.open == null ? "null" : props.sfx.open.name));
        sb.AppendLine("sfx.custom: " + (props.sfx.custom == null ? "null" : props.sfx.custom.name));
        int uniqueCount = props.uniqueProperties == null ? 0 : props.uniqueProperties.Count;
        sb.AppendLine("uniqueProperties.Count: " + uniqueCount);

        bool blanksOk =
            props.interaction == ObjectInteraction.Unset
            && props.placementMethod == ObjectPlacementMethod.Unset
            && props.nodeCompat == ObjectNodeCompat.None
            && (props.animationClips == null || props.animationClips.Length == 0)
            && props.animatorController == null
            && props.sfx.pickup == null
            && props.sfx.drop == null
            && props.sfx.use == null
            && props.sfx.open == null
            && props.sfx.custom == null
            && uniqueCount == 0;
        sb.AppendLine("CHECK new fields blank/unset: " + blanksOk);

        // Confirm CustomEditor type is present (Inspector surfaces Judgment fields).
        var editors = UnityEditor.Editor.CreateEditor(props);
        bool editorOk = editors != null && editors.GetType().Name == "ObjectPropertiesEditor";
        sb.AppendLine("CHECK ObjectPropertiesEditor: " + editorOk);
        if (editors != null)
            Object.DestroyImmediate(editors);

        bool all = existingOk && blanksOk && editorOk;
        sb.AppendLine("PASS=" + all);
        return sb.ToString();
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
