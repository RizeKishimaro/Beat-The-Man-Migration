using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 150 — Pending Review panel. Lists ObjectProperties with
/// approved=false plus 152/153 flagged review folders. Auto-opens once
/// per Editor session when count &gt; 0.
/// </summary>
public class PendingReviewWindow : EditorWindow
{
    const string MenuPath = "Tools/Objects/Pending Review";
    const string SessionOpenedKey = "BeatTheMan.PendingReview.OpenedThisSession";
    const string LogPrefix = "[PendingReview]";

    static PendingReviewWindow openInstance;
    static bool refreshQueued;
    static double nextDropPollAt;

    Vector2 scroll;
    readonly List<ReviewRow> awaiting = new List<ReviewRow>();
    readonly List<ReviewRow> flagged = new List<ReviewRow>();

    public static bool HasOpenInstance => openInstance != null;

    struct ReviewRow
    {
        public string Name;
        public string Kind;
        public string Source;
        public string Reason;
        public string SelectPath;
        public bool RevealInFinder;
    }

    [MenuItem(MenuPath)]
    public static void Open()
    {
        var win = GetWindow<PendingReviewWindow>("Pending Review");
        win.minSize = new Vector2(420, 280);
        win.Refresh();
        win.Show();
    }

    public static void RequestRefresh()
    {
        refreshQueued = true;
    }

    public static void RepaintOpen()
    {
        if (openInstance != null)
        {
            openInstance.Refresh();
            openInstance.Repaint();
        }
    }

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.delayCall += TryAutoOpenOnce;
    }

    static void TryAutoOpenOnce()
    {
        if (Application.isBatchMode)
            return;
        if (SessionState.GetBool(SessionOpenedKey, false))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += TryAutoOpenOnce;
            return;
        }

        int count = CountPending();
        SessionState.SetBool(SessionOpenedKey, true);
        if (count <= 0)
            return;

        Open();
        Debug.Log(LogPrefix + " auto-opened (" + count + " pending/flagged)");
    }

    void OnEnable()
    {
        openInstance = this;
        Refresh();
        EditorApplication.update += OnEditorUpdate;
    }

    void OnDisable()
    {
        if (openInstance == this)
            openInstance = null;
        EditorApplication.update -= OnEditorUpdate;
    }

    void OnFocus()
    {
        Refresh();
    }

    void OnEditorUpdate()
    {
        if (refreshQueued)
        {
            refreshQueued = false;
            Refresh();
            Repaint();
        }

        if (EditorApplication.timeSinceStartup >= nextDropPollAt)
        {
            nextDropPollAt = EditorApplication.timeSinceStartup + 2.0;
            // Drop/ is outside AssetDatabase — light poll, full refresh of flagged lists.
            int before = flagged.Count;
            flagged.RemoveAll(r => r.Source == "153 drop-fail");
            ScanFlaggedDrop(flagged);
            if (flagged.Count != before)
                Repaint();
        }
    }

    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            Refresh();
        GUILayout.FlexibleSpace();
        GUILayout.Label(
            awaiting.Count + " awaiting · " + flagged.Count + " flagged",
            EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawSection("Awaiting approval", awaiting);
        EditorGUILayout.Space(8);
        DrawSection("Flagged for manual review", flagged);

        EditorGUILayout.EndScrollView();
    }

    void DrawSection(string title, List<ReviewRow> rows)
    {
        EditorGUILayout.LabelField(title + " (" + rows.Count + ")", EditorStyles.boldLabel);
        if (rows.Count == 0)
        {
            EditorGUILayout.HelpBox("None.", MessageType.None);
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            ReviewRow row = rows[i];
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(row.Name, EditorStyles.linkLabel))
                ActivateRow(row);
            GUILayout.FlexibleSpace();
            GUILayout.Label(row.Kind, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(row.Source, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(row.Reason))
                EditorGUILayout.LabelField(Truncate(row.Reason, 140), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }
    }

    void ActivateRow(ReviewRow row)
    {
        if (string.IsNullOrEmpty(row.SelectPath))
            return;

        if (row.RevealInFinder)
        {
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", row.SelectPath));
            if (Directory.Exists(abs) || File.Exists(abs))
                EditorUtility.RevealInFinder(abs);
            else
                Debug.LogWarning(LogPrefix + " missing on disk: " + abs);
            return;
        }

        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(row.SelectPath);
        if (obj == null && AssetDatabase.IsValidFolder(row.SelectPath))
            obj = AssetDatabase.LoadAssetAtPath<DefaultAsset>(row.SelectPath);
        if (obj != null)
        {
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }
        else
            Debug.LogWarning(LogPrefix + " could not select " + row.SelectPath);
    }

    public void Refresh()
    {
        awaiting.Clear();
        flagged.Clear();
        ScanAwaiting(awaiting);
        ScanFlaggedReview(flagged);
        ScanFlaggedDrop(flagged);
    }

    public static int CountPending()
    {
        var a = new List<ReviewRow>();
        var f = new List<ReviewRow>();
        ScanAwaiting(a);
        ScanFlaggedReview(f);
        ScanFlaggedDrop(f);
        return a.Count + f.Count;
    }

    static void ScanAwaiting(List<ReviewRow> dst)
    {
        ScanAwaitingRoot("Assets/Objects/Pickable", "Pickable", dst);
        ScanAwaitingRoot("Assets/Objects/Nodes", "Node", dst);
    }

    static void ScanAwaitingRoot(string root, string kind, List<ReviewRow> dst)
    {
        if (!AssetDatabase.IsValidFolder(root))
            return;
        string[] guids = AssetDatabase.FindAssets("t:ObjectProperties", new[] { root });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(path);
            if (props == null || props.approved)
                continue;
            dst.Add(new ReviewRow
            {
                Name = string.IsNullOrEmpty(props.objectName) ? props.name : props.objectName,
                Kind = props.kind.ToString(),
                Source = "pending",
                Reason = path,
                SelectPath = path,
                RevealInFinder = false
            });
        }
    }

    static void ScanFlaggedReview(List<ReviewRow> dst)
    {
        const string root = "Assets/Objects/_Review";
        string abs = Abs(root);
        if (!Directory.Exists(abs))
            return;

        foreach (string dir in Directory.GetDirectories(abs))
        {
            string json = Path.Combine(dir, "NEEDS_REVIEW.json");
            if (!File.Exists(json))
                continue;
            string name = Path.GetFileName(dir);
            string assetFolder = root + "/" + name;
            dst.Add(new ReviewRow
            {
                Name = name,
                Kind = "—",
                Source = "152 safety-net",
                Reason = ReadJsonField(json, "reason") ?? ReadJsonField(json, "detail") ?? "NEEDS_REVIEW",
                SelectPath = assetFolder,
                RevealInFinder = false
            });
        }
    }

    static void ScanFlaggedDrop(List<ReviewRow> dst)
    {
        ScanDropKind("Pickable", dst);
        ScanDropKind("Nodes", dst);
    }

    static void ScanDropKind(string kind, List<ReviewRow> dst)
    {
        string root = "Drop/_NeedsReview/" + kind;
        string abs = Abs(root);
        if (!Directory.Exists(abs))
            return;

        foreach (string dir in Directory.GetDirectories(abs))
        {
            string json = Path.Combine(dir, "FAILED.json");
            if (!File.Exists(json))
                continue;
            string name = Path.GetFileName(dir);
            dst.Add(new ReviewRow
            {
                Name = name,
                Kind = kind,
                Source = "153 drop-fail",
                Reason = ReadJsonField(json, "reason") ?? ReadJsonField(json, "detail") ?? "FAILED",
                SelectPath = root + "/" + name,
                RevealInFinder = true
            });
        }
    }

    static string ReadJsonField(string path, string field)
    {
        try
        {
            string text = File.ReadAllText(path);
            var m = Regex.Match(text, "\"" + field + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!m.Success)
                return null;
            return m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\n", " ");
        }
        catch
        {
            return null;
        }
    }

    static string Abs(string projectRelative)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRelative));
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max - 1) + "…";
    }
}
