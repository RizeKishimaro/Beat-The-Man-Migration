using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 157 — Object Browser. Catalog of every ObjectProperties under
/// Assets/Objects/{Pickable,Nodes}. Sibling of Pending Review (150); does
/// not auto-open, does not list 152/153 flagged folders.
/// </summary>
public class ObjectBrowserWindow : EditorWindow
{
    const string MenuPath = "Tools/Objects/Object Browser";
    const string LogPrefix = "[ObjectBrowser]";

    static readonly Color PendingBg = new Color(0.85f, 0.55f, 0.12f, 1f);
    static readonly Color ApprovedBg = new Color(0.18f, 0.55f, 0.28f, 1f);

    static readonly string[] ScanRoots =
    {
        "Assets/Objects/Pickable",
        "Assets/Objects/Nodes"
    };

    Vector2 scroll;
    string search = "";
    readonly List<BrowserRow> all = new List<BrowserRow>();
    readonly List<BrowserRow> shown = new List<BrowserRow>();

    public struct BrowserRow
    {
        public string Name;
        public string Kind;
        public bool Approved;
        public string SelectPath;
    }

    [MenuItem(MenuPath)]
    public static void Open()
    {
        var win = GetWindow<ObjectBrowserWindow>("Object Browser");
        win.minSize = new Vector2(420, 280);
        win.Refresh();
        win.Show();
    }

    void OnEnable()
    {
        Refresh();
    }

    void OnFocus()
    {
        Refresh();
    }

    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            Refresh();
        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
        ApplyFilter();
        GUILayout.Label(
            all.Count + " objects · " + shown.Count + " shown",
            EditorStyles.miniLabel,
            GUILayout.Width(160));
        EditorGUILayout.EndHorizontal();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        if (shown.Count == 0)
        {
            EditorGUILayout.HelpBox("No objects match.", MessageType.None);
        }
        else
        {
            for (int i = 0; i < shown.Count; i++)
                DrawRow(shown[i]);
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawRow(BrowserRow row)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(row.Name, EditorStyles.linkLabel))
            PingProperties(row.SelectPath);
        GUILayout.FlexibleSpace();
        GUILayout.Label(row.Kind, EditorStyles.miniLabel);
        Color prev = GUI.contentColor;
        GUI.contentColor = row.Approved ? ApprovedBg : PendingBg;
        GUILayout.Label(row.Approved ? "APPROVED" : "PENDING", EditorStyles.miniLabel);
        GUI.contentColor = prev;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    public void Refresh()
    {
        all.Clear();
        CollectAll(all);
        ApplyFilter();
    }

    void ApplyFilter()
    {
        shown.Clear();
        Filter(all, search, shown);
    }

    public static void CollectAll(List<BrowserRow> dst)
    {
        dst.Clear();
        for (int r = 0; r < ScanRoots.Length; r++)
            ScanRoot(ScanRoots[r], dst);
        dst.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    static void ScanRoot(string root, List<BrowserRow> dst)
    {
        if (!AssetDatabase.IsValidFolder(root))
            return;
        string[] guids = AssetDatabase.FindAssets("t:ObjectProperties", new[] { root });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var props = AssetDatabase.LoadAssetAtPath<ObjectProperties>(path);
            if (props == null)
                continue;
            dst.Add(new BrowserRow
            {
                Name = string.IsNullOrEmpty(props.objectName) ? props.name : props.objectName,
                Kind = props.kind.ToString(),
                Approved = props.approved,
                SelectPath = path
            });
        }
    }

    public static void Filter(List<BrowserRow> source, string query, List<BrowserRow> dst)
    {
        dst.Clear();
        string q = query == null ? "" : query.Trim();
        for (int i = 0; i < source.Count; i++)
        {
            BrowserRow row = source[i];
            if (MatchesSearch(row, q))
                dst.Add(row);
        }
    }

    public static bool MatchesSearch(BrowserRow row, string query)
    {
        if (string.IsNullOrEmpty(query))
            return true;
        if (row.Name != null && row.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (row.SelectPath != null &&
            row.SelectPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return false;
    }

    /// <summary>
    /// Same Properties-asset ping as PendingReviewWindow.ActivateRow
    /// (Selection.activeObject + PingObject). Copied, not refactored.
    /// </summary>
    public static bool PingProperties(string selectPath)
    {
        if (string.IsNullOrEmpty(selectPath))
            return false;
        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(selectPath);
        if (obj != null)
        {
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
            return true;
        }

        Debug.LogWarning(LogPrefix + " could not select " + selectPath);
        return false;
    }
}
