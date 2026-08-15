using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 136 — F1_Car sync tool (https://trello.com/c/wpmkPxiD).
///
/// The master .blend is a living file: parts get exported over time (126-131).
/// Two gaps this closes:
///
/// GAP 1 — a newly exported part FBX does not appear in F1_Car.prefab on its
/// own; it needed a manual drag-and-parent every time. Sync adds any part
/// folder that is missing from the prefab, at local zero per ticket 135.
///
/// GAP 2 — nudging a part's position/rotation directly in Unity becomes a
/// prefab override, which Unity then protects: a genuinely different position
/// exported from Blender afterwards will NOT take effect until the override is
/// cleared. Silent, and easy to do by accident. Check reports these.
///
/// On reporting overrides: a healthy prefab already carries ~10 transform
/// overrides per part whose values are identical to the source FBX (position
/// zero, rotation identity — the 135 convention, recorded rather than deviant),
/// plus 16 deliberate material overrides from ticket 130. Flagging the mere
/// presence of an override would therefore report ~40 phantom problems on a
/// perfectly correct prefab, and the tool would be ignored inside a week. So
/// every override is compared against the value in its source asset, and only
/// genuine divergence is raised.
///
/// Flags: Temp/f1_car_sync.flag containing "check" or "sync".
/// </summary>
[InitializeOnLoad]
public static class F1CarSyncTool
{
    const string FlagPath = "Temp/f1_car_sync.flag";
    const string ReportPath = "Temp/f1_car_sync_report.txt";

    /// <summary>Transform properties — a real divergence here is GAP 2 and blocks Blender sync.</summary>
    static readonly string[] TransformProps =
    {
        "m_LocalPosition", "m_LocalRotation", "m_LocalScale", "m_LocalEulerAnglesHint",
    };

    static F1CarSyncTool()
    {
        EditorApplication.delayCall += TryFlag;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (!File.Exists(F1CarConvention.Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryFlag();
    }

    static void TryFlag()
    {
        string flag = F1CarConvention.Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        string mode = File.ReadAllText(flag).Trim().ToLowerInvariant();
        File.Delete(flag);
        string report = Run(applyChanges: mode == "sync");
        WriteReport(report);
        Debug.Log("[F1CarSyncTool]\n" + report);
    }

    [MenuItem("Tools/Grid/Check F1_Car (ticket 136)")]
    public static void CheckFromMenu() => RunFromMenu(applyChanges: false);

    [MenuItem("Tools/Grid/Sync F1_Car (ticket 136)")]
    public static void SyncFromMenu() => RunFromMenu(applyChanges: true);

    static void RunFromMenu(bool applyChanges)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            File.WriteAllText(F1CarConvention.Full(FlagPath), applyChanges ? "sync\n" : "check\n");
            return;
        }
        string report = Run(applyChanges);
        WriteReport(report);
        EditorUtility.DisplayDialog(applyChanges ? "Sync F1_Car" : "Check F1_Car",
            Truncate(report, 1600), "OK");
        Debug.Log("[F1CarSyncTool]\n" + report);
    }

    /// <param name="applyChanges">false = report only, nothing is written.</param>
    public static string Run(bool applyChanges)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== F1_Car sync (ticket 136) — mode={(applyChanges ? "SYNC" : "CHECK")} ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");
        sb.AppendLine($"prefab={F1CarConvention.PrefabPath}");
        sb.AppendLine($"parts={F1CarConvention.PartsDir}");

        if (AssetDatabase.LoadAssetAtPath<GameObject>(F1CarConvention.PrefabPath) == null)
        {
            sb.AppendLine($"FAIL: prefab not found at {F1CarConvention.PrefabPath}");
            sb.AppendLine("Run Tools/Grid/Setup Workshop Room (ticket 132) to build it first.");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        List<F1CarConvention.Part> parts = F1CarConvention.DiscoverParts(sb);
        sb.AppendLine($"discovered {parts.Count} part folder(s): "
                      + string.Join(", ", parts.Select(p => p.Name)));
        if (parts.Count == 0)
        {
            sb.AppendLine("FAIL: no parts discovered — nothing to sync against.");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        GameObject root = PrefabUtility.LoadPrefabContents(F1CarConvention.PrefabPath);
        bool dirty = false;
        int errors = 0, warnings = 0, pendingCount = 0;
        bool pending = false;

        try
        {
            // --- root sanity: ticket 135 puts the one and only scale here ---
            Vector3 rootScale = root.transform.localScale;
            bool scaleOk = (rootScale - Vector3.one * F1CarConvention.Scale).sqrMagnitude < 1e-6f;
            sb.AppendLine($"root scale={rootScale} "
                          + (scaleOk ? "OK" : $"ERROR expected uniform {F1CarConvention.Scale} (ticket 135)"));
            if (!scaleOk)
                errors++;

            // --- map existing children by their source FBX ---
            var byGuid = new Dictionary<string, GameObject>();
            var strays = new List<GameObject>();
            foreach (Transform child in root.transform)
            {
                GameObject go = child.gameObject;
                string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                string guid = string.IsNullOrEmpty(src) ? null : AssetDatabase.AssetPathToGUID(src);
                if (guid == null)
                    strays.Add(go);
                else if (!byGuid.ContainsKey(guid))
                    byGuid[guid] = go;
                else
                    strays.Add(go); // duplicate instance of a part already mapped
            }

            // --- GAP 1: add any part folder missing from the prefab ---
            sb.AppendLine("--- parts ---");
            int added = 0;
            foreach (F1CarConvention.Part p in parts)
            {
                if (byGuid.TryGetValue(p.Guid, out GameObject existing))
                {
                    sb.AppendLine($"  present '{existing.name}' <- {p.FbxPath}");
                    continue;
                }

                if (!applyChanges)
                {
                    sb.AppendLine($"  MISSING '{p.Name}' <- {p.FbxPath} (sync would add it)");
                    added++;
                    continue;
                }

                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(p.FbxPath);
                var part = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.scene);
                part.name = p.Name;
                part.transform.SetParent(root.transform, false);
                F1CarConvention.PlacePart(part.transform);
                F1CarConvention.ReuseAuthoredMaterials(part, sb);
                byGuid[p.Guid] = part;
                dirty = true;
                added++;
                sb.AppendLine($"  ADDED '{p.Name}' at local zero (ticket 135) <- {p.FbxPath}");
            }
            if (added == 0)
                sb.AppendLine("  all discovered parts already present");

            // A child whose source FBX no longer has a part folder — e.g. a
            // retired export like Wheel_ChromeDrift (ticket 133). Never removed
            // automatically: that is a modelling decision, not a sync one.
            var knownGuids = new HashSet<string>(parts.Select(p => p.Guid));
            foreach (KeyValuePair<string, GameObject> kv in byGuid)
            {
                if (knownGuids.Contains(kv.Key))
                    continue;
                string src = AssetDatabase.GUIDToAssetPath(kv.Key);
                sb.AppendLine($"  WARN child '{kv.Value.name}' has no part folder — source {src}");
                warnings++;
            }
            foreach (GameObject stray in strays)
            {
                sb.AppendLine($"  WARN child '{stray.name}' is not a part prefab instance");
                warnings++;
            }

            if (dirty)
            {
                PrefabUtility.SaveAsPrefabAsset(root, F1CarConvention.PrefabPath);
                AssetDatabase.SaveAssets();
                sb.AppendLine($"saved={F1CarConvention.PrefabPath}");

                // Reload before auditing: GetPropertyModifications does not see
                // edits made in this session until they are serialised, so a
                // part added moments ago would report none of the material
                // overrides just applied to it — making two consecutive runs
                // disagree about the same prefab.
                PrefabUtility.UnloadPrefabContents(root);
                root = PrefabUtility.LoadPrefabContents(F1CarConvention.PrefabPath);
            }

            // --- GAP 2: overrides that genuinely differ from the source ---
            sb.AppendLine("--- overrides ---");
            foreach (Transform child in root.transform)
            {
                GameObject go = child.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
                    continue;

                var diverged = new List<string>();
                var materials = new List<string>();
                var other = new List<string>();
                int noop = 0;

                foreach (PropertyModification m in PrefabUtility.GetPropertyModifications(go)
                         ?? new PropertyModification[0])
                {
                    if (m == null || m.target == null)
                        continue;
                    if (Matches(m))
                    {
                        noop++;
                        continue;
                    }

                    // Name the target object: several renderers under one part
                    // carry the same propertyPath (m_Materials.Array.data[0]…),
                    // so the path alone cannot tell them apart in the report.
                    string line = $"{m.target.name}.{m.propertyPath} = {Describe(m)}";
                    string head = m.propertyPath.Split('.')[0];
                    if (TransformProps.Contains(head))
                        diverged.Add(line);
                    else if (head == "m_Materials")
                        materials.Add(line);
                    else
                        other.Add(line);
                }

                sb.AppendLine($"  '{go.name}': {noop} matching source, "
                              + $"{diverged.Count} transform, {materials.Count} material, {other.Count} other");

                foreach (string l in diverged)
                {
                    // This is the GAP 2 failure: Blender cannot overwrite this.
                    sb.AppendLine($"    ERROR override differs from source — {l}");
                    errors++;
                }
                foreach (string l in materials)
                    sb.AppendLine($"    INFO authored material (expected, ticket 130) — {l}");
                foreach (string l in other)
                {
                    sb.AppendLine($"    WARN unclassified override — {l}");
                    warnings++;
                }

                // Ticket 135 guard, at prefab level rather than scene level.
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if ((t.localScale - Vector3.one).sqrMagnitude > 1e-6f)
                    {
                        sb.AppendLine($"    ERROR per-part scale on '{t.name}' = {t.localScale}"
                                      + " — must be 1 (ticket 135)");
                        errors++;
                    }
                }
            }

            if (!dirty)
                sb.AppendLine(applyChanges ? "no changes needed" : "check only — nothing written");

            // Check mode leaves missing parts unfixed, so the prefab is still
            // out of date when it returns. Saying OK there would let anyone
            // reading the result line conclude all is well.
            pendingCount = added;
            pending = !applyChanges && added > 0;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        sb.AppendLine($"--- {errors} error(s), {warnings} warning(s)"
                      + (pending ? $", {pendingCount} part(s) awaiting sync" : "") + " ---");
        if (errors > 0)
        {
            sb.AppendLine("Clear a diverging transform override via the prefab's Overrides dropdown");
            sb.AppendLine("(Revert), then re-export from Blender — otherwise the export is ignored.");
        }
        if (pending)
            sb.AppendLine("Run Tools/Grid/Sync F1_Car (ticket 136) to add the missing part(s).");
        sb.AppendLine(errors > 0 || pending ? "RESULT=ISSUES" : "RESULT=OK");
        return sb.ToString();
    }

    /// <summary>
    /// True when the override records the same value the source asset already
    /// has — a no-op, not a deviation. This is what keeps the report honest.
    /// </summary>
    static bool Matches(PropertyModification m)
    {
        var so = new SerializedObject(m.target);
        SerializedProperty sp = so.FindProperty(m.propertyPath);
        if (sp == null)
            return false; // cannot resolve — report it rather than hide it

        switch (sp.propertyType)
        {
            case SerializedPropertyType.ObjectReference:
                return sp.objectReferenceValue == m.objectReference;
            case SerializedPropertyType.Float:
                return float.TryParse(m.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
                       && Mathf.Abs(sp.floatValue - f) < 1e-5f;
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.LayerMask:
            case SerializedPropertyType.Enum:
                return int.TryParse(m.value, out int i) && sp.intValue == i;
            case SerializedPropertyType.Boolean:
                return (m.value == "1") == sp.boolValue;
            case SerializedPropertyType.String:
                return sp.stringValue == m.value;
            default:
                return false;
        }
    }

    static string Describe(PropertyModification m) =>
        m.objectReference != null ? m.objectReference.name : $"'{m.value}'";

    static void WriteReport(string report)
    {
        string full = F1CarConvention.Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max) + "\n…";
    }
}
