using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 130 — Wheels (https://trello.com/c/oBQ8790I).
/// Import the approved ChromeDrift wheel into GridFoundation, replacing the Fusion XP prototype.
/// Flag: Temp/setup_wheel_chromedrift.flag
/// </summary>
[InitializeOnLoad]
public static class WheelChromeDriftSceneSetup
{
    const string FbxPath = "Assets/Art/Vehicles/Wheel_ChromeDrift/Wheel_ChromeDrift.fbx";
    const string DecalTexPath = "Assets/Art/Vehicles/Wheel_ChromeDrift/SoftTyre.png";
    const string MatDir = "Assets/Art/Vehicles/Wheel_ChromeDrift";
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string InstanceName = "Wheel_ChromeDrift";
    const string OldInstanceName = "Wheel_FusionXP";
    const string FlagPath = "Temp/setup_wheel_chromedrift.flag";
    const string ReportPath = "Temp/setup_wheel_chromedrift_report.txt";
    const string ReloadFlagPath = "Temp/force_reload_grid_foundation.flag";

    static readonly Vector3 PlacePos = new Vector3(3.5f, 0f, -0.5f);

    static WheelChromeDriftSceneSetup()
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
        if (EditorApplication.isCompiling)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        File.Delete(flag);
        string report = Run();
        WriteReport(report);
        Debug.Log("[WheelChromeDriftSceneSetup]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    [MenuItem("Tools/Grid/Setup ChromeDrift Wheel (ticket 130)")]
    public static void SetupFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            File.WriteAllText(Full(FlagPath), "setup\n");
            return;
        }
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("ChromeDrift Wheel", Truncate(report, 1600), "OK");
        Debug.Log("[WheelChromeDriftSceneSetup]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Wheel_ChromeDrift game import (ticket 130) ===");

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            sb.AppendLine("FAIL still in play mode");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        ConfigureDecalTexture(sb);
        Material rubber = BuildLit(MatDir + "/Tire_Rubber.mat", new Color(0.04f, 0.04f, 0.04f), 0f, 0.55f, sb);
        Material chrome = BuildLit(MatDir + "/Wheel_HubWhiteChrome.mat", new Color(0.90f, 0.91f, 0.94f), 0.42f, 0.86f, sb);
        Material bolt = BuildLit(MatDir + "/Wheel_GlossBlack.mat", new Color(0.03f, 0.03f, 0.035f), 0.05f, 0.88f, sb);
        Material decal = BuildDecal(sb);
        ConfigureFbx(sb);

        GameObject fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbxRoot == null)
        {
            sb.AppendLine("FAIL FBX not imported");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;
            if (root.name == InstanceName || root.name == OldInstanceName)
                Object.DestroyImmediate(root);
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxRoot, scene);
        if (inst == null)
            inst = Object.Instantiate(fbxRoot);
        inst.name = InstanceName;
        inst.transform.SetPositionAndRotation(PlacePos, Quaternion.identity);
        inst.transform.localScale = Vector3.one;

        foreach (var rend in inst.GetComponentsInChildren<Renderer>(true))
        {
            string n = rend.gameObject.name;
            Material mat = rubber;
            if (n.Contains("Rim"))
                mat = chrome;
            else if (n.Contains("Bolt"))
                mat = bolt;
            else if (n.Contains("Decal"))
                mat = decal;
            else if (n.Contains("Tire"))
                mat = rubber;
            var mats = rend.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = mat;
            rend.sharedMaterials = mats;
            if (n.Contains("Decal"))
            {
                rend.shadowCastingMode = ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }
            sb.AppendLine($"mat {n} -> {(mat != null ? mat.name : "null")}");
        }

        Bounds? bounds = CollectRendererBounds(inst);
        if (bounds.HasValue)
        {
            Bounds b = bounds.Value;
            sb.AppendLine($"importBounds size={b.size} min={b.min}");
            if (b.size.y + 0.05f < b.size.x && b.size.y + 0.05f < b.size.z)
            {
                inst.transform.Rotate(90f, 0f, 0f, Space.World);
                bounds = CollectRendererBounds(inst);
                b = bounds.Value;
                sb.AppendLine($"rotatedAxleToHorizontal size={b.size}");
            }

            Vector3 p = inst.transform.position;
            p.y += -b.min.y;
            GridSystem grid = Object.FindAnyObjectByType<GridSystem>();
            if (grid != null)
            {
                GridCoord cell = grid.WorldToCell(new Vector3(PlacePos.x, 0f, PlacePos.z));
                Vector3 center = grid.CellToWorldCenter(cell);
                p.x = center.x;
                p.z = center.z;
                sb.AppendLine($"gridCell={cell} centerXZ=({center.x},{center.z})");
            }
            inst.transform.position = p;
            b = CollectRendererBounds(inst).Value;
            sb.AppendLine($"floorSit pos={inst.transform.position} size={b.size} minY={b.min.y:F4}");
            sb.AppendLine($"diameterXZ={Mathf.Max(b.size.x, b.size.z):F3} thickness={Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z)):F3}");
        }
        else
            sb.AppendLine("FAIL no renderers");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine($"saved={ScenePath} instance={InstanceName}");
        sb.AppendLine(bounds.HasValue && rubber != null && chrome != null && bolt != null && decal != null
            ? "RESULT=OK"
            : "RESULT=FAIL");
        return sb.ToString();
    }

    static Material BuildLit(string path, Color albedo, float metallic, float smoothness, StringBuilder sb)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            sb.AppendLine("FAIL URP Lit shader missing");
            return null;
        }
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(lit);
            AssetDatabase.CreateAsset(mat, path);
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        mat.shader = lit;
        mat.SetColor("_BaseColor", albedo);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_EmissionColor"))
            mat.SetColor("_EmissionColor", Color.black);
        mat.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(mat);
        sb.AppendLine($"mat={path} metal={metallic} smooth={smoothness}");
        return mat;
    }

    static Material BuildDecal(StringBuilder sb)
    {
        Material mat = BuildLit(MatDir + "/Tire_Decal.mat", Color.white, 0f, 0.45f, sb);
        if (mat == null)
            return null;
        Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(DecalTexPath);
        mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);
        sb.AppendLine($"decalTex={(tex != null ? DecalTexPath : "MISSING")}");
        return mat;
    }

    static void ConfigureDecalTexture(StringBuilder sb)
    {
        AssetDatabase.ImportAsset(DecalTexPath, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(DecalTexPath) as TextureImporter;
        if (imp == null)
        {
            sb.AppendLine("FAIL missing " + DecalTexPath);
            return;
        }
        bool dirty = false;
        if (imp.textureType != TextureImporterType.Default) { imp.textureType = TextureImporterType.Default; dirty = true; }
        if (!imp.sRGBTexture) { imp.sRGBTexture = true; dirty = true; }
        if (!imp.alphaIsTransparency) { imp.alphaIsTransparency = true; dirty = true; }
        if (imp.wrapMode != TextureWrapMode.Repeat) { imp.wrapMode = TextureWrapMode.Repeat; dirty = true; }
        if (dirty)
            imp.SaveAndReimport();
        sb.AppendLine($"tex {DecalTexPath} sRGB={imp.sRGBTexture} alphaIsTransparency={imp.alphaIsTransparency}");
    }

    static void ConfigureFbx(StringBuilder sb)
    {
        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            sb.AppendLine("FAIL no ModelImporter");
            return;
        }
        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.None)
        {
            importer.animationType = ModelImporterAnimationType.None;
            dirty = true;
        }
        if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            dirty = true;
        }
        if (dirty)
            importer.SaveAndReimport();
        sb.AppendLine($"fbx anim={importer.animationType} mats={importer.materialImportMode} scale={importer.globalScale}");
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

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static void WriteReport(string report)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
    }

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}
