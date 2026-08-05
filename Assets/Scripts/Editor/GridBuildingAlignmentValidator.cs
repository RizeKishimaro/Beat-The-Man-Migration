using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Report-only building exterior/interior grid alignment checks.
/// Rule: exterior footprint and interior floor each resolve to whole cells;
/// wall ring = Outside | Wall cell | Inside (wall thickness eats Outside, never Inside).
/// Does NOT modify CafeRoom or any scene content.
/// Flag: Temp/run_grid_building_validate.flag
/// </summary>
[InitializeOnLoad]
public static class GridBuildingAlignmentValidator
{
    const string ReportPath = "Temp/grid_building_alignment_report.txt";
    const string FlagPath = "Temp/run_grid_building_validate.flag";
    const float Epsilon = 0.001f;

    static GridBuildingAlignmentValidator()
    {
        EditorApplication.delayCall += TryFlag;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryFlag();
    }

    static void TryFlag()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        File.Delete(flag);
        string report = RunValidation();
        WriteReport(report);
        Debug.Log("[GridBuildingAlignmentValidator]\n" + report);
    }

    [MenuItem("Tools/Grid/Validate Building Alignment (report)")]
    public static void ValidateFromMenu()
    {
        string report = RunValidation();
        WriteReport(report);
        EditorUtility.DisplayDialog("Building Alignment", Truncate(report, 1400), "OK");
        Debug.Log("[GridBuildingAlignmentValidator]\n" + report);
    }

    public static string RunValidation()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Building exterior/interior alignment (report-only) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");
        sb.AppendLine("rules=whole-cell exterior+interior; wall ring Outside|Wall|Inside; wall thickness absorbs Outside only");

        int issues = 0;

        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        var buildings = new List<(string path, GridFootprint fp)>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;
            foreach (GridFootprint fp in prefab.GetComponentsInChildren<GridFootprint>(true))
            {
                if (fp.type == AssetGridType.Building)
                    buildings.Add((path, fp));
            }
        }

        foreach (GridFootprint fp in Object.FindObjectsByType<GridFootprint>(FindObjectsInactive.Include))
        {
            if (fp.type != AssetGridType.Building)
                continue;
            string scene = fp.gameObject.scene.IsValid() ? fp.gameObject.scene.path : "(unsaved)";
            buildings.Add((scene, fp));
        }

        sb.AppendLine($"buildingFootprintsFound={buildings.Count}");
        if (buildings.Count == 0)
        {
            sb.AppendLine("NOTE: No GridFootprint type=Building yet (expected until shop interior rebuild).");
            sb.AppendLine("Running read-only CafeRoom bounds probe if asset present…");
            issues += ProbeCafeRoomReadOnly(sb);
        }
        else
        {
            foreach (var (path, fp) in buildings)
                issues += ValidateFootprintBuilding(sb, path, fp);
        }

        sb.AppendLine(issues == 0 ? "RESULT=PASS (no alignment issues flagged)" : $"RESULT=ISSUES flagged={issues}");
        return sb.ToString();
    }

    static int ValidateFootprintBuilding(StringBuilder sb, string path, GridFootprint fp)
    {
        int issues = 0;
        sb.AppendLine($"--- Building '{GetHierarchyPath(fp.transform)}' @ {path} ---");
        sb.AppendLine($"  declared sizeInCells={fp.sizeInCells}");

        if (fp.sizeInCells.x < 1 || fp.sizeInCells.y < 1)
        {
            sb.AppendLine("  FAIL sizeInCells must be >= 1");
            issues++;
        }

        Bounds? bounds = CollectRendererBounds(fp.gameObject);
        if (!bounds.HasValue)
        {
            sb.AppendLine("  WARN no renderers — cannot verify world AABB vs cells");
            return issues;
        }

        Bounds b = bounds.Value;
        float minX = b.min.x;
        float maxX = b.max.x;
        float minZ = b.min.z;
        float maxZ = b.max.z;

        if (!IsNearInteger(minX) || !IsNearInteger(minZ))
        {
            sb.AppendLine($"  FAIL exterior corner not on integer meter: min=({minX:F4},{minZ:F4})");
            issues++;
        }
        else
            sb.AppendLine($"  OK exterior min corner on grid ({minX:F3},{minZ:F3})");

        float sizeX = maxX - minX;
        float sizeZ = maxZ - minZ;
        if (!IsNearInteger(sizeX) || !IsNearInteger(sizeZ))
        {
            sb.AppendLine($"  FAIL exterior size not whole cells: size=({sizeX:F4},{sizeZ:F4})");
            issues++;
        }
        else
            sb.AppendLine($"  OK exterior size whole cells ({sizeX:F3},{sizeZ:F3})");

        Transform interior = FindNamed(fp.transform, "RoomFloor", "InteriorFloor", "Floor", "Interior");
        if (interior != null)
        {
            Bounds? ib = CollectRendererBounds(interior.gameObject);
            if (ib.HasValue)
            {
                Bounds ibv = ib.Value;
                float iSizeX = ibv.max.x - ibv.min.x;
                float iSizeZ = ibv.max.z - ibv.min.z;
                if (!IsNearInteger(ibv.min.x) || !IsNearInteger(ibv.min.z)
                    || !IsNearInteger(iSizeX) || !IsNearInteger(iSizeZ))
                {
                    sb.AppendLine(
                        $"  FAIL interior floor not whole-cell aligned: " +
                        $"min=({ibv.min.x:F4},{ibv.min.z:F4}) size=({iSizeX:F4},{iSizeZ:F4})");
                    issues++;
                }
                else
                {
                    sb.AppendLine(
                        $"  OK interior floor whole-cell: min=({ibv.min.x:F3},{ibv.min.z:F3}) " +
                        $"size=({iSizeX:F3},{iSizeZ:F3})");
                    if (ibv.min.x <= minX + Epsilon || ibv.max.x >= maxX - Epsilon
                        || ibv.min.z <= minZ + Epsilon || ibv.max.z >= maxZ - Epsilon)
                    {
                        sb.AppendLine(
                            "  FAIL interior reaches exterior edge — wall ring likely eating Inside " +
                            "(walls must occupy Outside/Wall cells only)");
                        issues++;
                    }
                    else
                        sb.AppendLine("  OK interior inset from exterior (Outside/Wall absorption plausible)");
                }
            }
        }
        else
            sb.AppendLine("  WARN no interior floor child found (RoomFloor/InteriorFloor/Floor/Interior)");

        return issues;
    }

    static int ProbeCafeRoomReadOnly(StringBuilder sb)
    {
        const string cafePath = "Assets/Art/Shop/CafeRoom.fbx";
        GameObject cafeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(cafePath);
        if (cafeAsset == null)
        {
            sb.AppendLine("CafeRoom.fbx not found — skip probe.");
            return 0;
        }

        GameObject temp = Object.Instantiate(cafeAsset);
        temp.name = "__CafeRoom_GridProbe";
        temp.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Bounds? bounds = CollectRendererBounds(temp);
            if (!bounds.HasValue)
            {
                sb.AppendLine("CafeRoom probe: no renderers.");
                return 0;
            }

            Bounds b = bounds.Value;
            sb.AppendLine(
                $"CafeRoom (read-only) world AABB min=({b.min.x:F3},{b.min.y:F3},{b.min.z:F3}) " +
                $"max=({b.max.x:F3},{b.max.y:F3},{b.max.z:F3}) " +
                $"sizeXZ=({b.size.x:F3},{b.size.z:F3})");

            int issues = 0;
            if (!IsNearInteger(b.min.x) || !IsNearInteger(b.min.z)
                || !IsNearInteger(b.size.x) || !IsNearInteger(b.size.z))
            {
                sb.AppendLine(
                    "CafeRoom probe: FAIL current mesh is NOT whole-cell aligned at origin — " +
                    "expected; rebuild tracked on Basic shop interior blockout (do not retrofit here).");
                issues++;
            }
            else
                sb.AppendLine("CafeRoom probe: AABB already whole-cell at default transform.");

            Transform floor = FindNamed(temp.transform, "RoomFloor", "InteriorFloor", "Floor");
            if (floor != null)
            {
                Bounds? fb = CollectRendererBounds(floor.gameObject);
                if (fb.HasValue)
                    sb.AppendLine(
                        $"CafeRoom RoomFloor sizeXZ=({fb.Value.size.x:F3},{fb.Value.size.z:F3}) " +
                        $"min=({fb.Value.min.x:F3},{fb.Value.min.z:F3})");
            }
            else
                sb.AppendLine("CafeRoom probe: no RoomFloor child name match.");

            return issues;
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    static Bounds? CollectRendererBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
            return null;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);
        return b;
    }

    static Transform FindNamed(Transform root, params string[] names)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            foreach (string n in names)
            {
                if (t.name == n)
                    return t;
            }
        }
        return null;
    }

    static bool IsNearInteger(float v) => Mathf.Abs(v - Mathf.Round(v)) <= 0.02f;

    static string GetHierarchyPath(Transform t)
    {
        var parts = new List<string>();
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static void WriteReport(string report)
    {
        string full = Full(ReportPath);
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
