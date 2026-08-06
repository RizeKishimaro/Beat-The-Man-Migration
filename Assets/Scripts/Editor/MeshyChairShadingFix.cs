using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ticket 103 shading investigate/report. Texture type fixes are applied via .meta;
/// this verifies material wiring, mesh normals, and nearby objects. No scale changes.
/// Flag: Temp/fix_meshy_chair_shading.flag
/// </summary>
[InitializeOnLoad]
public static class MeshyChairShadingFix
{
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string ChairName = "Meshy_Chair_Spike";
    const string FbxPath = "Assets/Objects/Meshy_Chair_Unity/Meshy_Chair_Unity.fbx";
    const string MatPath =
        "Assets/Objects/Meshy_Chair_Unity/Materials/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture.mat";
    const string NormalPath =
        "Assets/Objects/Meshy_Chair_Unity/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture_normal.png";
    const string FlagPath = "Temp/fix_meshy_chair_shading.flag";
    const string ReportPath = "Temp/meshy_chair_shading_fix_report.txt";
    const string ReloadFlagPath = "Temp/force_reload_grid_foundation.flag";

    static MeshyChairShadingFix()
    {
        EditorApplication.update += Poll;
    }

    static void Poll()
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
        File.WriteAllText(Full(ReportPath), report);
        Debug.Log("[MeshyChairShadingFix]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    [MenuItem("Tools/Grid/Fix Meshy Chair Shading (no scale)")]
    public static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            File.WriteAllText(Full(FlagPath), "fix\n");
            EditorUtility.DisplayDialog("Meshy Chair Shading", "Exiting Play Mode — will re-run.", "OK");
            return;
        }
        string report = Run();
        File.WriteAllText(Full(ReportPath), report);
        EditorUtility.DisplayDialog("Meshy Chair Shading", Truncate(report, 1800), "OK");
        Debug.Log("[MeshyChairShadingFix]\n" + report);
        File.WriteAllText(Full(ReloadFlagPath), "reload\n");
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Meshy chair shading investigate + fix ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");

        // Force reimport textures so NormalMap meta sticks
        AssetDatabase.ImportAsset(NormalPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject chair = GameObject.Find(ChairName);
        string sourcePath = null;
        if (chair != null)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(chair);
            sourcePath = src != null ? AssetDatabase.GetAssetPath(src) : "(no prefab source)";
        }

        sb.AppendLine("--- 1) IMPORT PATH ---");
        sb.AppendLine($"sceneInstance={ChairName} found={chair != null}");
        sb.AppendLine($"prefabSourcePath={sourcePath}");
        bool viaBlender = sourcePath != null && sourcePath.Replace('\\', '/') == FbxPath;
        sb.AppendLine(viaBlender
            ? "PATH=Blender re-export Meshy_Chair_Unity.fbx (NOT direct Meshy FBX)"
            : $"PATH=check source={sourcePath}");

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        sb.AppendLine("--- 2) MATERIAL / SHADING ---");
        if (mat != null)
        {
            // Ensure emission off (was assigned map + white color without _EMISSION)
            if (mat.HasProperty("_EmissionMap"))
                mat.SetTexture("_EmissionMap", null);
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", Color.black);
            mat.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(mat);

            sb.AppendLine($"mat={mat.name} shader={mat.shader.name}");
            sb.AppendLine($"keywords={string.Join(",", mat.shaderKeywords)}");
            sb.AppendLine($"_BaseMap={TexName(mat.GetTexture("_BaseMap"))}");
            sb.AppendLine($"_BumpMap={TexName(mat.GetTexture("_BumpMap"))}");
            sb.AppendLine($"_MetallicGlossMap={TexName(mat.GetTexture("_MetallicGlossMap"))}");
            sb.AppendLine($"_EmissionMap={TexName(mat.GetTexture("_EmissionMap"))} emission={mat.GetColor("_EmissionColor")}");
            sb.AppendLine($"_Metallic={mat.GetFloat("_Metallic")} _Smoothness={mat.GetFloat("_Smoothness")}");
        }

        var nrmImp = AssetImporter.GetAtPath(NormalPath) as TextureImporter;
        sb.AppendLine($"normalImporter type={nrmImp?.textureType} sRGB={nrmImp?.sRGBTexture} (expect NormalMap / False)");

        string metalPath = "Assets/Objects/Meshy_Chair_Unity/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture_metallic.png";
        string roughPath = "Assets/Objects/Meshy_Chair_Unity/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture_roughness.png";
        var metalImp = AssetImporter.GetAtPath(metalPath) as TextureImporter;
        var roughImp = AssetImporter.GetAtPath(roughPath) as TextureImporter;
        sb.AppendLine($"metallicImporter type={metalImp?.textureType} sRGB={metalImp?.sRGBTexture}");
        sb.AppendLine($"roughnessImporter type={roughImp?.textureType} sRGB={roughImp?.sRGBTexture}");
        sb.AppendLine("NOTE: MetallicGlossMap still NULL — Meshy ships separate metal/rough; URP Lit needs packed mask (R=metal,A=1-rough). Unwired metal/rough = flat scalars, NOT melted look.");
        sb.AppendLine("ROOT CAUSE of melted: normal map was textureType=Default sRGB=1 while plugged into _BumpMap.");

        sb.AppendLine("--- 3) GEOMETRY / NORMALS ---");
        if (chair != null)
        {
            var mf = chair.GetComponentInChildren<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh != null)
            {
                sb.AppendLine($"mesh={mesh.name} verts={mesh.vertexCount} tris={mesh.triangles.Length / 3}");
                sb.AppendLine($"normals={mesh.normals.Length} tangents={mesh.tangents.Length} uvs={mesh.uv.Length}");
                int zeroN = 0;
                Vector3 sum = Vector3.zero;
                var norms = mesh.normals;
                for (int i = 0; i < norms.Length; i++)
                {
                    if (norms[i].sqrMagnitude < 1e-8f) zeroN++;
                    if (i < 100) sum += norms[i];
                }
                sb.AppendLine($"zeroNormals={zeroN}/{norms.Length}");
                if (norms.Length > 0)
                    sb.AppendLine($"avgNormalSample={sum / Mathf.Min(100, norms.Length)}");
                sb.AppendLine($"localBounds={mesh.bounds.size}");
            }
            ModelImporter mi = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (mi != null)
                sb.AppendLine($"importer normals={mi.importNormals} tangents={mi.importTangents}");
        }

        sb.AppendLine("--- 4) NEARBY OBJECTS ---");
        Vector3 chairPos = chair != null ? chair.transform.position : new Vector3(1.5f, 0f, -0.5f);
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            float d = Vector3.Distance(r.bounds.center, chairPos);
            if (d > 3.5f) continue;
            sb.AppendLine($"renderer={r.gameObject.name} path={PathOf(r.transform)} size={r.bounds.size} dist={d:F2}");
        }
        foreach (var cc in Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Include))
        {
            float d = Vector3.Distance(cc.transform.position, chairPos);
            sb.AppendLine($"CharacterController on={cc.gameObject.name} h={cc.height} r={cc.radius} dist={d:F2} (gray capsule gizmo if selected)");
        }
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include))
        {
            float d = Vector3.Distance(col.bounds.center, chairPos);
            if (d > 3.5f) continue;
            sb.AppendLine($"collider={col.GetType().Name} on={col.gameObject.name} size={col.bounds.size} dist={d:F2}");
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        bool nrmOk = nrmImp != null && nrmImp.textureType == TextureImporterType.NormalMap;
        sb.AppendLine(nrmOk ? "RESULT=OK normal map type fixed; scale untouched" : "RESULT=FAIL normal map still wrong type");
        return sb.ToString();
    }

    static string TexName(Texture t) => t != null ? t.name : "NULL";
    static string PathOf(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}
