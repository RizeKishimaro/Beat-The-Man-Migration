using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 160 step 3 — inspect normal-map + mesh tangent import. No changes, no scene save.
/// Flag: Temp/fd_normal_inspect.flag
/// </summary>
[InitializeOnLoad]
public static class Bake160NormalInspect
{
    const string FlagPath = "Temp/fd_normal_inspect.flag";
    const string ReportPath = "Temp/fd_normal_inspect.txt";

    static Bake160NormalInspect()
    {
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling)
            return;
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        File.Delete(flag);
        try
        {
            string report = Run();
            File.WriteAllText(Full(ReportPath), report);
            Debug.Log("[Bake160NormalInspect]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(Full(ReportPath), "FAIL " + e);
        }
    }

    [MenuItem("Tools/Objects/160 Normal Map Inspect")]
    public static void Menu()
    {
        File.WriteAllText(Full(ReportPath), Run());
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 160 normal/tangent inspect (no changes) ===");
        DumpTex(sb, "CHAIR_N",
            "Assets/Objects/Meshy_Chair_Unity/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture_normal.png");
        DumpTex(sb, "COOLER_N",
            "Assets/Objects/Pickable/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture_fbx/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture_normal.png");
        DumpTex(sb, "PRINTER_N_control",
            "Assets/Objects/Pickable/printer/Meshy_AI_Cream_Cushioned_Woode_0901123805_texture_normal.png");
        DumpFbx(sb, "CHAIR_FBX", "Assets/Objects/Meshy_Chair_Unity/Meshy_Chair_Unity.fbx");
        DumpFbx(sb, "COOLER_FBX",
            "Assets/Objects/Pickable/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture_fbx/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture.fbx");
        DumpFbx(sb, "PRINTER_PREFAB_control", "Assets/Objects/Pickable/printer/printer.prefab");
        sb.AppendLine("RESULT=OK");
        return sb.ToString();
    }

    static void DumpTex(StringBuilder sb, string label, string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        sb.AppendLine("--- " + label + " " + path + " ---");
        if (ti == null)
        {
            sb.AppendLine("  MISSING importer (file may not exist)");
            return;
        }
        sb.AppendLine("  textureType=" + ti.textureType);
        sb.AppendLine("  sRGBTexture=" + ti.sRGBTexture);
        sb.AppendLine("  convertToNormalmap=" + ti.convertToNormalmap);
        try
        {
            var p = ti.GetType().GetProperty("flipGreenChannel");
            if (p != null)
                sb.AppendLine("  flipGreenChannel=" + p.GetValue(ti, null));
        }
        catch { }
        sb.AppendLine("  textureCompression=" + ti.textureCompression);
        sb.AppendLine("  maxTextureSize=" + ti.maxTextureSize);
        sb.AppendLine("  mipmapEnabled=" + ti.mipmapEnabled);
    }

    static void DumpFbx(StringBuilder sb, string label, string path)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        sb.AppendLine("--- " + label + " " + path + " ---");
        if (mi == null)
        {
            sb.AppendLine("  MISSING importer");
            return;
        }
        sb.AppendLine("  importNormals=" + mi.importNormals);
        sb.AppendLine("  importTangents=" + mi.importTangents);
        sb.AppendLine("  normalCalculationMode=" + mi.normalCalculationMode);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null)
        {
            sb.AppendLine("  FBX GameObject null");
            return;
        }
        var filters = go.GetComponentsInChildren<MeshFilter>(true);
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int n = 0;
        for (int i = 0; i < filters.Length; i++)
            DumpMesh(sb, filters[i].sharedMesh, filters[i].name, ref n);
        for (int i = 0; i < smrs.Length; i++)
            DumpMesh(sb, smrs[i].sharedMesh, smrs[i].name, ref n);
        if (n == 0)
            sb.AppendLine("  no MeshFilter/SkinnedMeshRenderer");
    }

    static void DumpMesh(StringBuilder sb, Mesh mesh, string name, ref int n)
    {
        if (mesh == null)
            return;
        n++;
        int tan = mesh.tangents != null ? mesh.tangents.Length : 0;
        int nor = mesh.normals != null ? mesh.normals.Length : 0;
        int uv = mesh.uv != null ? mesh.uv.Length : 0;
        int zeroTan = 0;
        int zeroN = 0;
        var tans = mesh.tangents;
        var norms = mesh.normals;
        int check = Mathf.Min(mesh.vertexCount, tan);
        for (int i = 0; i < check; i++)
        {
            Vector4 t = tans[i];
            if (new Vector3(t.x, t.y, t.z).sqrMagnitude < 1e-8f)
                zeroTan++;
        }
        check = Mathf.Min(mesh.vertexCount, nor);
        for (int i = 0; i < check; i++)
        {
            if (norms[i].sqrMagnitude < 1e-8f)
                zeroN++;
        }
        sb.AppendLine("  mesh=" + name + "/" + mesh.name
            + " verts=" + mesh.vertexCount
            + " normals=" + nor
            + " tangents=" + tan
            + " uvs=" + uv
            + " zeroNormals=" + zeroN
            + " zeroTangents=" + zeroTan);
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
