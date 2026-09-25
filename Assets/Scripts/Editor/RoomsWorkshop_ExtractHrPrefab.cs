using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Parent RoomsWorkshop copier + water cooler under Room_HR, save Prefab Variant
/// Assets/Prefabs/Buildings/Room_HR.prefab, wire Alpha01 RoomPlacement to it.
/// Does not edit DefaultBuilding.prefab. Does not save GridFoundation.
/// Flag: Temp/extract_hr_prefab.flag
/// Report: Temp/extract_hr_prefab.txt
/// </summary>
[InitializeOnLoad]
public static class RoomsWorkshop_ExtractHrPrefab
{
    public const string HrPrefabPath = "Assets/Prefabs/Buildings/Room_HR.prefab";
    const string WorkshopPath = RoomsWorkshopSceneSetup.ScenePath;
    const string Alpha01Path = Alpha01SceneSetup.ScenePath;
    const string PlaygroundPath = "Assets/Scenes/GridFoundation.unity";
    const string HrName = "Room_HR";
    const string CopierName = "Meshy_AI_Cream_Cushioned_Woode_0902130031_texture_fbx";
    const string CoolerName = "Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture";
    const string FlagPath = "Temp/extract_hr_prefab.flag";
    const string ReportPath = "Temp/extract_hr_prefab.txt";
    const string FixCopierFlagPath = "Temp/fix_hr_copier.flag";
    const string FixCopierReportPath = "Temp/fix_hr_copier.txt";
    const float InteriorMin = 0.3f;
    const float InteriorMax = 5.7f;

    static RoomsWorkshop_ExtractHrPrefab()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (EditorApplication.isCompiling)
            return;
        if (File.Exists(Full(FixCopierFlagPath)))
        {
            TryFixCopier();
            return;
        }
        if (!File.Exists(Full(FlagPath)))
            return;
        TryRun();
    }

    [MenuItem("Tools/Scenes/Extract Room_HR Prefab + Wire Alpha01")]
    public static void MenuRun()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Extract Room_HR", Truncate(report, 1600), "OK");
    }

    [MenuItem("Tools/Scenes/Fix Room_HR Copier Inside")]
    public static void MenuFixCopier()
    {
        string report = FixCopierInside();
        WriteReportTo(FixCopierReportPath, report);
        EditorUtility.DisplayDialog("Fix Room_HR Copier", Truncate(report, 1600), "OK");
    }

    static void TryRun()
    {
        if (!File.Exists(Full(FlagPath)))
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
            File.Delete(Full(FlagPath));
            string report = Run();
            WriteReport(report);
            Debug.Log("[RoomsWorkshop_ExtractHrPrefab]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[RoomsWorkshop_ExtractHrPrefab] " + e);
        }
    }

    static void TryFixCopier()
    {
        if (!File.Exists(Full(FixCopierFlagPath)))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += TryFixCopier;
            return;
        }
        if (EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += TryFixCopier;
            return;
        }

        try
        {
            File.Delete(Full(FixCopierFlagPath));
            string report = FixCopierInside();
            WriteReportTo(FixCopierReportPath, report);
            Debug.Log("[RoomsWorkshop_FixHrCopier]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReportTo(FixCopierReportPath, "ERROR " + e);
            Debug.LogError("[RoomsWorkshop_FixHrCopier] " + e);
        }
    }

    public static string FixCopierInside()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Fix Room_HR copier inside interior ===");
        sb.AppendLine("time=" + System.DateTime.UtcNow.ToString("o"));
        sb.AppendLine("does not edit DefaultBuilding.prefab");
        sb.AppendLine("does not save GridFoundation");

        DiscardDirtyPlayground(sb);

        GameObject contents = PrefabUtility.LoadPrefabContents(HrPrefabPath);
        if (contents == null)
        {
            sb.AppendLine("FAIL missing " + HrPrefabPath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        try
        {
            GameObject copier = FindIn(contents, CopierName);
            if (copier == null)
            {
                sb.AppendLine("FAIL copier not in prefab");
                sb.AppendLine("RESULT=FAIL");
                return sb.ToString();
            }

            Transform wrapper = copier.transform.Find("Wrapper");
            Transform mesh = wrapper != null && wrapper.childCount > 0 ? wrapper.GetChild(0) : null;
            Bounds before = RendererBounds(copier);
            sb.AppendLine("before bounds z[" + before.min.z.ToString("F2") + "," + before.max.z.ToString("F2")
                + "] x[" + before.min.x.ToString("F2") + "," + before.max.x.ToString("F2") + "]");
            sb.AppendLine("copier root=" + copier.transform.localPosition
                + (mesh != null ? " meshLocal=" + mesh.localPosition : ""));

            Vector3 shift = Vector3.zero;
            if (before.max.x > InteriorMax) shift.x -= before.max.x - InteriorMax;
            if (before.min.x < InteriorMin) shift.x += InteriorMin - before.min.x;
            if (before.max.z > InteriorMax) shift.z -= before.max.z - InteriorMax;
            if (before.min.z < InteriorMin) shift.z += InteriorMin - before.min.z;
            copier.transform.position += shift;
            sb.AppendLine("clamp shift=" + shift);

            Bounds after = RendererBounds(copier);
            sb.AppendLine("after bounds z[" + after.min.z.ToString("F2") + "," + after.max.z.ToString("F2")
                + "] x[" + after.min.x.ToString("F2") + "," + after.max.x.ToString("F2") + "]");

            PrefabUtility.SaveAsPrefabAsset(contents, HrPrefabPath);
            AssetDatabase.SaveAssets();
            sb.AppendLine("saved " + HrPrefabPath);

            bool inside = after.min.x >= InteriorMin - 0.01f
                && after.max.x <= InteriorMax + 0.01f
                && after.min.z >= InteriorMin - 0.01f
                && after.max.z <= InteriorMax + 0.01f;
            if (!inside)
            {
                sb.AppendLine("FAIL still outside interior");
                sb.AppendLine("RESULT=FAIL");
                return sb.ToString();
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        if (File.Exists(Full(Alpha01Path)))
        {
            EditorSceneManager.OpenScene(Alpha01Path, OpenSceneMode.Single);
            sb.AppendLine("reloaded " + SceneManager.GetActiveScene().path);
        }

        bool ok = SceneManager.GetActiveScene().path == Alpha01Path;
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static Bounds RendererBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in rs)
        {
            if (r == null || !r.enabled)
                continue;
            if (!any)
            {
                b = r.bounds;
                any = true;
            }
            else
                b.Encapsulate(r.bounds);
        }
        return b;
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Extract Room_HR prefab + wire Alpha01 ===");
        sb.AppendLine("time=" + System.DateTime.UtcNow.ToString("o"));
        sb.AppendLine("does not edit DefaultBuilding.prefab");
        sb.AppendLine("does not save GridFoundation");

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            sb.AppendLine("FAIL: editor is in Play mode");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        DiscardDirtyPlayground(sb);

        if (!File.Exists(Full(WorkshopPath)))
        {
            sb.AppendLine("FAIL missing " + WorkshopPath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        Scene workshop = EditorSceneManager.OpenScene(WorkshopPath, OpenSceneMode.Single);
        sb.AppendLine("opened " + workshop.path);

        GameObject hr = FindNamed(workshop, HrName);
        GameObject copier = FindNamed(workshop, CopierName);
        GameObject cooler = FindNamed(workshop, CoolerName);
        if (hr == null || copier == null || cooler == null)
        {
            sb.AppendLine("FAIL Room_HR=" + (hr != null)
                + " copier=" + (copier != null)
                + " cooler=" + (cooler != null));
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        ParentKeepWorld(copier, hr, sb);
        ParentKeepWorld(cooler, hr, sb);

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Buildings"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            AssetDatabase.CreateFolder("Assets/Prefabs", "Buildings");
        }

        GameObject saved = PrefabUtility.SaveAsPrefabAssetAndConnect(
            hr, HrPrefabPath, InteractionMode.AutomatedAction, out bool prefabOk);
        if (!prefabOk || saved == null)
        {
            sb.AppendLine("FAIL SaveAsPrefabAssetAndConnect " + HrPrefabPath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        EditorSceneManager.MarkSceneDirty(workshop);
        bool workshopSaved = EditorSceneManager.SaveScene(workshop, WorkshopPath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("prefab=" + HrPrefabPath);
        sb.AppendLine("workshopSaved=" + workshopSaved);

        GameObject contents = PrefabUtility.LoadPrefabContents(HrPrefabPath);
        try
        {
            bool prefabCopier = FindIn(contents, CopierName) != null;
            bool prefabCooler = FindIn(contents, CoolerName) != null;
            sb.AppendLine("prefab copier=" + prefabCopier + " cooler=" + prefabCooler);
            if (!prefabCopier || !prefabCooler)
            {
                sb.AppendLine("FAIL furniture missing from prefab");
                sb.AppendLine("RESULT=FAIL");
                return sb.ToString();
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HrPrefabPath);
        if (prefab == null)
        {
            sb.AppendLine("FAIL load " + HrPrefabPath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        if (!File.Exists(Full(Alpha01Path)))
        {
            sb.AppendLine("FAIL missing " + Alpha01Path);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        Scene alpha = EditorSceneManager.OpenScene(Alpha01Path, OpenSceneMode.Single);
        sb.AppendLine("opened " + alpha.path);

        RoomPlacement place = Object.FindAnyObjectByType<RoomPlacement>();
        if (place == null)
        {
            sb.AppendLine("FAIL missing RoomPlacement");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        place.buildingPrefab = prefab;
        EditorUtility.SetDirty(place);
        EditorSceneManager.MarkSceneDirty(alpha);
        bool alphaSaved = EditorSceneManager.SaveScene(alpha, Alpha01Path);
        AssetDatabase.SaveAssets();
        sb.AppendLine("alphaSaved=" + alphaSaved);

        EditorSceneManager.OpenScene(Alpha01Path, OpenSceneMode.Single);
        sb.AppendLine("reloaded " + SceneManager.GetActiveScene().path);

        RoomPlacement wired = Object.FindAnyObjectByType<RoomPlacement>();
        string wiredPath = wired != null && wired.buildingPrefab != null
            ? AssetDatabase.GetAssetPath(wired.buildingPrefab)
            : "";
        sb.AppendLine("RoomPlacement prefab=" + wiredPath);
        sb.AppendLine("RoomPlacement.PrefabPath=" + RoomPlacement.PrefabPath);

        bool ok = workshopSaved && alphaSaved
            && SceneManager.GetActiveScene().path == Alpha01Path
            && wiredPath == HrPrefabPath
            && RoomPlacement.PrefabPath == HrPrefabPath;
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static void ParentKeepWorld(GameObject child, GameObject parent, StringBuilder sb)
    {
        GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(child);
        if (root == null)
            root = child;
        if (root.transform.parent == parent.transform)
        {
            sb.AppendLine("already child: " + root.name);
            return;
        }
        Vector3 pos = root.transform.position;
        Quaternion rot = root.transform.rotation;
        Vector3 scale = root.transform.localScale;
        root.transform.SetParent(parent.transform, true);
        sb.AppendLine("parented " + root.name
            + " pos=" + pos
            + " rot=" + rot.eulerAngles
            + " scale=" + scale);
    }

    static void DiscardDirtyPlayground(StringBuilder sb)
    {
        Scene current = SceneManager.GetActiveScene();
        if (current.path != PlaygroundPath || !current.isDirty)
            return;
        EditorSceneManager.OpenScene(PlaygroundPath, OpenSceneMode.Single);
        sb.AppendLine("discarded unsaved GridFoundation dirt (did not save playground)");
    }

    static GameObject FindNamed(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            GameObject hit = FindIn(root, name);
            if (hit != null)
                return hit;
        }
        return null;
    }

    static GameObject FindIn(GameObject root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != null && t.name == name)
                return t.gameObject;
        return null;
    }

    static void WriteReport(string report)
    {
        WriteReportTo(ReportPath, report);
    }

    static void WriteReportTo(string rel, string report)
    {
        File.WriteAllText(Full(rel), report ?? "");
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max) + "\n…";
    }
}
