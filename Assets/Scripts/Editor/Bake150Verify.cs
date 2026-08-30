using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 150 — verify Approve button, scale/pivot live preview, and Pending
/// Review panel against CreamChair_Raw153_Properties.
/// Flag: Temp/bake150_verify.flag
/// Report: BlenderSource/Bake/_bake150_unity_report.txt
/// </summary>
[InitializeOnLoad]
public static class Bake150Verify
{
    const string PropsPath =
        "Assets/Objects/Pickable/CreamChair_Raw153/CreamChair_Raw153_Properties.asset";
    const string FlagPath = "Temp/bake150_verify.flag";
    const string ReportPath = "BlenderSource/Bake/_bake150_unity_report.txt";

    static Bake150Verify()
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

    [MenuItem("Tools/Objects/Verify 150 CreamChair_Raw153")]
    public static void MenuVerify()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Ticket 150", Truncate(report, 1600), "OK");
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
            Debug.Log("[Bake150Verify]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Bake150Verify] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 150 CreamChair_Raw153 verify ===");

        var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(PropsPath);
        if (props == null)
        {
            sb.AppendLine("FAIL missing " + PropsPath);
            sb.AppendLine("PASS=False");
            return sb.ToString();
        }

        // Restore a known pending baseline first.
        props.approved = false;
        props.scaleMultiplier = 1f;
        props.pivotOffset = Vector3.zero;
        EditorUtility.SetDirty(props);
        AssetDatabase.SaveAssets();
        ObjectPropertiesPreview.ClearAllPreviews();

        // --- Panel shows it while unapproved ---
        int pendingBefore = CountAwaitingNamed("CreamChair_Raw153");
        sb.AppendLine("--- Pending Review before approve ---");
        sb.AppendLine("awaitingContainsCreamChair: " + (pendingBefore > 0));
        sb.AppendLine("CountPending total: " + PendingReviewWindow.CountPending());
        bool panelBeforeOk = pendingBefore > 0;

        // --- Scale / pivot live preview ---
        props.scaleMultiplier = 1.5f;
        props.pivotOffset = new Vector3(0.1f, 0f, -0.2f);
        EditorUtility.SetDirty(props);

        GameObject root = ObjectPropertiesPreview.EnsurePreview(props);
        sb.AppendLine("--- Live preview ---");
        sb.AppendLine("previewRoot: " + (root != null ? root.name : "null"));

        bool previewOk = false;
        if (root != null)
        {
            Transform wrapper = root.transform.Find("Wrapper");
            Transform mesh = wrapper != null ? wrapper.Find("Mesh") : null;
            sb.AppendLine("wrapperScale: " + (wrapper != null ? wrapper.localScale.ToString("F3") : "null"));
            sb.AppendLine("meshLocalPos: " + (mesh != null ? mesh.localPosition.ToString("F3") : "null"));

            previewOk = wrapper != null && mesh != null
                && Approximately(wrapper.localScale, Vector3.one * 1.5f)
                && Approximately(mesh.localPosition, new Vector3(0.1f, 0f, -0.2f))
                && Approximately(mesh.localScale, Vector3.one);

            // Live update path (property change after preview exists)
            props.scaleMultiplier = 2f;
            props.pivotOffset = new Vector3(0f, 0.25f, 0f);
            ObjectPropertiesPreview.ApplyTransforms(props);
            sb.AppendLine("afterLiveUpdate wrapperScale: " + wrapper.localScale.ToString("F3"));
            sb.AppendLine("afterLiveUpdate meshLocalPos: " + mesh.localPosition.ToString("F3"));
            previewOk = previewOk
                && Approximately(wrapper.localScale, Vector3.one * 2f)
                && Approximately(mesh.localPosition, new Vector3(0f, 0.25f, 0f));
        }
        sb.AppendLine("CHECK preview scale/pivot: " + previewOk);

        // --- Approve button equivalent ---
        props.approved = true;
        EditorUtility.SetDirty(props);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int pendingAfter = CountAwaitingNamed("CreamChair_Raw153");
        sb.AppendLine("--- Pending Review after approve ---");
        sb.AppendLine("approved: " + props.approved);
        sb.AppendLine("awaitingContainsCreamChair: " + (pendingAfter > 0));
        bool approveOk = props.approved && pendingAfter == 0;
        sb.AppendLine("CHECK approve clears panel row: " + approveOk);

        // Leave Sam's review state pending again; clear preview clutter.
        props.approved = false;
        props.scaleMultiplier = 1f;
        props.pivotOffset = Vector3.zero;
        EditorUtility.SetDirty(props);
        AssetDatabase.SaveAssets();
        ObjectPropertiesPreview.ClearAllPreviews();
        PendingReviewWindow.RequestRefresh();
        PendingReviewWindow.RepaintOpen();

        int pendingRestored = CountAwaitingNamed("CreamChair_Raw153");
        sb.AppendLine("--- Restored pending baseline ---");
        sb.AppendLine("approved: " + props.approved);
        sb.AppendLine("awaitingContainsCreamChair: " + (pendingRestored > 0));

        bool all = panelBeforeOk && previewOk && approveOk && pendingRestored > 0;
        sb.AppendLine("PASS=" + all);
        return sb.ToString();
    }

    static int CountAwaitingNamed(string objectName)
    {
        string[] guids = AssetDatabase.FindAssets("t:ObjectProperties", new[]
        {
            "Assets/Objects/Pickable",
            "Assets/Objects/Nodes"
        });
        int n = 0;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(path);
            if (props == null || props.approved)
                continue;
            string name = string.IsNullOrEmpty(props.objectName) ? props.name : props.objectName;
            if (name == objectName || path.Contains(objectName))
                n++;
        }
        return n;
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
