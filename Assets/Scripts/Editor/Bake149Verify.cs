using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 149 — verify Properties SO, stub, URP Lit remap, packed mask on
/// CreamChair_Raw153. Flag: Temp/bake149_verify.flag
/// Report: BlenderSource/Bake/_bake149_unity_report.txt
/// </summary>
[InitializeOnLoad]
public static class Bake149Verify
{
    const string Folder = "Assets/Objects/Pickable/CreamChair_Raw153";
    const string FlagPath = "Temp/bake149_verify.flag";
    const string ReportPath = "BlenderSource/Bake/_bake149_unity_report.txt";

    static Bake149Verify()
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

    public static void RunFromBatch()
    {
        ObjectFolderGenerator.GenerateNow();
        string report = Measure();
        WriteReport(report);
        Debug.Log("[Bake149Verify]\n" + report);
    }

    [MenuItem("Tools/Objects/Verify 149 CreamChair_Raw153")]
    public static void MenuVerify()
    {
        RunFromBatch();
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
        EditorUtility.DisplayDialog("Ticket 149", Truncate(File.ReadAllText(full), 1600), "OK");
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
            RunFromBatch();
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Bake149Verify] " + e);
        }
    }

    static string Measure()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 149 CreamChair_Raw153 verify ===");
        sb.AppendLine("folder: " + Folder);

        string soPath = Folder + "/CreamChair_Raw153_Properties.asset";
        string stubPath = Folder + "/CreamChair_Raw153.cs";
        string matPath = Folder + "/CreamChair_Raw153.mat";
        string maskPath = Folder + "/CreamChair_Raw153_Mask.png";
        string fbxPath = FindFbx();

        var so = AssetDatabase.LoadAssetAtPath<ObjectProperties>(soPath);
        sb.AppendLine("--- Properties SO ---");
        sb.AppendLine("path: " + soPath);
        sb.AppendLine("exists: " + (so != null));
        if (so != null)
        {
            sb.AppendLine("objectName: " + so.objectName);
            sb.AppendLine("folder: " + (so.folder != null ? so.folder.name : "(null)"));
            sb.AppendLine("kind: " + so.kind);
            sb.AppendLine("pickupable: " + so.pickupable);
            sb.AppendLine("approved: " + so.approved);
            sb.AppendLine("weight: " + so.weight);
            sb.AppendLine("price: " + so.price);
            sb.AppendLine("category: '" + (so.category ?? "") + "'");
            sb.AppendLine("stackable: " + so.stackable);
            sb.AppendLine("sizeTier: " + so.sizeTier);
            sb.AppendLine("physicsGravity: " + so.physicsGravity);
            sb.AppendLine("scaleMultiplier: " + so.scaleMultiplier);
            sb.AppendLine("pivotOffset: " + so.pivotOffset);
            bool soOk = so.objectName == "CreamChair_Raw153"
                && so.kind == ObjectKind.Pickable
                && so.pickupable
                && !so.approved
                && so.stackable == ObjectTriState.Unset
                && so.sizeTier == ObjectSizeTier.Unset
                && so.physicsGravity == ObjectPhysicsGravity.Unset
                && Mathf.Abs(so.scaleMultiplier - 1f) < 0.001f
                && so.pivotOffset == Vector3.zero;
            sb.AppendLine("CHECK so: " + soOk);
        }
        else
            sb.AppendLine("CHECK so: False");

        sb.AppendLine("--- Stub ---");
        sb.AppendLine("path: " + stubPath);
        bool stubExists = File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", stubPath)));
        sb.AppendLine("exists: " + stubExists);
        bool stubOk = false;
        if (stubExists)
        {
            string src = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", stubPath)));
            stubOk = src.Contains("class CreamChair_Raw153 : ImportedPickable")
                && !src.Contains(": CarryableItem")
                && !src.Contains(": DefaultNode");
            sb.AppendLine("CHECK stub ImportedPickable (not CarryableItem): " + stubOk);
        }
        else
            sb.AppendLine("CHECK stub ImportedPickable (not CarryableItem): False");

        sb.AppendLine("--- Material ---");
        sb.AppendLine("path: " + matPath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        sb.AppendLine("exists: " + (mat != null));
        bool matOk = false;
        if (mat != null)
        {
            sb.AppendLine("shader: " + mat.shader.name);
            Texture baseMap = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
            Texture bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            Texture metal = mat.HasProperty("_MetallicGlossMap") ? mat.GetTexture("_MetallicGlossMap") : null;
            Texture emission = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
            sb.AppendLine("_BaseMap: " + TexName(baseMap));
            sb.AppendLine("_BumpMap: " + TexName(bump));
            sb.AppendLine("_MetallicGlossMap: " + TexName(metal));
            sb.AppendLine("_EmissionMap: " + TexName(emission));
            sb.AppendLine("keyword _NORMALMAP: " + mat.IsKeywordEnabled("_NORMALMAP"));
            sb.AppendLine("keyword _METALLICSPECGLOSSMAP: " + mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"));
            sb.AppendLine("keyword _EMISSION: " + mat.IsKeywordEnabled("_EMISSION"));
            if (mat.HasProperty("_Metallic"))
                sb.AppendLine("_Metallic: " + mat.GetFloat("_Metallic"));
            if (mat.HasProperty("_Smoothness"))
                sb.AppendLine("_Smoothness: " + mat.GetFloat("_Smoothness"));
            if (mat.HasProperty("_EmissionColor"))
                sb.AppendLine("_EmissionColor: " + mat.GetColor("_EmissionColor"));
            matOk = mat.shader != null
                && mat.shader.name == "Universal Render Pipeline/Lit"
                && baseMap != null
                && bump != null
                && metal != null
                && metal.name.Contains("Mask")
                && mat.IsKeywordEnabled("_NORMALMAP")
                && mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP");
            sb.AppendLine("CHECK mat URP Lit + maps + mask: " + matOk);
        }
        else
            sb.AppendLine("CHECK mat URP Lit + maps + mask: False");

        sb.AppendLine("--- Mask ---");
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
        sb.AppendLine("path: " + maskPath);
        sb.AppendLine("exists: " + (mask != null));
        var maskImp = AssetImporter.GetAtPath(maskPath) as TextureImporter;
        if (maskImp != null)
            sb.AppendLine("importer sRGB=" + maskImp.sRGBTexture + " type=" + maskImp.textureType + " (expect False / Default)");
        bool maskOk = mask != null && maskImp != null && !maskImp.sRGBTexture;
        sb.AppendLine("CHECK mask: " + maskOk);

        sb.AppendLine("--- FBX remap ---");
        sb.AppendLine("fbx: " + (fbxPath ?? "(missing)"));
        bool remapOk = false;
        if (!string.IsNullOrEmpty(fbxPath))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var r = go != null ? go.GetComponentInChildren<Renderer>(true) : null;
            if (r != null && r.sharedMaterial != null)
            {
                sb.AppendLine("renderer.sharedMaterial: " + r.sharedMaterial.name);
                sb.AppendLine("renderer.shader: " + r.sharedMaterial.shader.name);
                remapOk = r.sharedMaterial == mat
                    || (mat != null && r.sharedMaterial.name == mat.name);
                Texture rm = r.sharedMaterial.HasProperty("_MetallicGlossMap")
                    ? r.sharedMaterial.GetTexture("_MetallicGlossMap")
                    : null;
                sb.AppendLine("renderer _MetallicGlossMap: " + TexName(rm));
            }
            else
                sb.AppendLine("no renderer/material on FBX");

            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer != null)
            {
                var map = importer.GetExternalObjectMap();
                sb.AppendLine("externalObjectMap count: " + map.Count);
                foreach (var kvp in map)
                    sb.AppendLine("  remap " + kvp.Key.type + " '" + kvp.Key.name + "' -> " + (kvp.Value != null ? kvp.Value.name : "null"));
            }
        }
        sb.AppendLine("CHECK remap: " + remapOk);

        bool all = so != null && !so.approved && stubOk && matOk && maskOk && remapOk;
        sb.AppendLine();
        sb.AppendLine(all ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static string FindFbx()
    {
        string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", Folder));
        if (!Directory.Exists(abs))
            return null;
        foreach (string file in Directory.GetFiles(abs, "*.fbx"))
        {
            string name = Path.GetFileName(file);
            return Folder + "/" + name;
        }
        return null;
    }

    static string TexName(Texture t)
    {
        return t != null ? t.name : "(null)";
    }

    static void WriteReport(string report)
    {
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report);
    }

    static string Truncate(string s, int n)
    {
        return s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
