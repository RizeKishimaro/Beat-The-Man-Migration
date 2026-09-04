using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 163 — existing zip-drop regression (Drop/Pickable, no --decimate)
/// plus 160 normals check on the landing path.
/// Flag: Temp/bake163_verify.flag
/// Report: Temp/bake163/unity_zipdrop.txt
/// </summary>
[InitializeOnLoad]
public static class Bake163Verify
{
    const string FlagPath = "Temp/bake163_verify.flag";
    const string ReportPath = "Temp/bake163/unity_zipdrop.txt";
    const string Stem = "Ticket163_ExistingFlowRegress";
    const string SrcDir =
        "Assets/Objects/Pickable/Meshy_AI_Cream_Cushioned_Woode_0901111846_texture_fbx";

    static Bake163Verify()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRun();
    }

    [MenuItem("Tools/Objects/Verify 163 Zip-Drop Regression")]
    public static void MenuVerify()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Ticket 163", Truncate(report, 1600), "OK");
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
            Debug.Log("[Bake163Verify]\n" + report);
        }
        catch (Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Bake163Verify] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 163 existing zip-drop regression ===");
        sb.AppendLine("stem: " + Stem);
        sb.AppendLine("drop: Drop/Pickable (NOT Drop/Decimate)");

        string zipPath = Full("Temp/bake163/" + Stem + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
        BuildSourceZip(zipPath, sb);

        string dropZip = Full("Drop/Pickable/" + Stem + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(dropZip));
        File.Copy(zipPath, dropZip, true);
        sb.AppendLine("copied zip → " + Rel(dropZip));

        RawDropWatcher.RunFromBatch();
        ObjectFolderGenerator.GenerateNow();
        sb.AppendLine("RawDropWatcher.RunFromBatch + GenerateNow finished");

        string bakeJson = FindBakeJson(Stem);
        if (string.IsNullOrEmpty(bakeJson) || !File.Exists(bakeJson))
        {
            sb.AppendLine("FAIL missing bake.json under Drop/_BakeOut/" + Stem);
            sb.AppendLine("RESULT=FAIL");
            sb.AppendLine("PASS=False");
            Cleanup(sb);
            return sb.ToString();
        }

        string json = File.ReadAllText(bakeJson);
        File.Copy(bakeJson, Full("Temp/bake163/existing_flow.bake.json"), true);
        sb.AppendLine("bake.json: " + Rel(bakeJson));

        bool decimateRequested = json.Contains("\"decimate_requested\": true");
        bool has163Step = json.Contains("163 ");
        int trisIn = ReadIntField(json, "\"tris\":");
        int trisOut = ReadIntField(json, "\"tris_after_bake\":");
        sb.AppendLine("decimate_requested: " + decimateRequested);
        sb.AppendLine("steps contain 163: " + has163Step);
        sb.AppendLine("tris: " + trisIn + " → " + trisOut);

        string destFbx = FindDestFbx(Stem);
        sb.AppendLine("dest fbx: " + (destFbx ?? "(missing)"));

        bool normalsOk = false;
        if (!string.IsNullOrEmpty(destFbx))
        {
            var importer = AssetImporter.GetAtPath(destFbx) as ModelImporter;
            if (importer != null)
            {
                sb.AppendLine("importNormals: " + importer.importNormals);
                sb.AppendLine("normalSmoothingAngle: " + importer.normalSmoothingAngle);
                sb.AppendLine("importTangents: " + importer.importTangents);
                normalsOk = importer.importNormals == ModelImporterNormals.Calculate
                    && Mathf.Abs(importer.normalSmoothingAngle - MeshyModelImportDefaults.NormalSmoothingAngle) < 0.01f
                    && importer.importTangents == ModelImporterTangents.CalculateMikk;
            }
        }

        bool propsOk = AssetDatabase.LoadAssetAtPath<ObjectProperties>(
            "Assets/Objects/Pickable/" + Stem + "/" + Stem + "_Properties.asset") != null;
        sb.AppendLine("149 Properties.asset present: " + propsOk);

        bool flowOk = !decimateRequested && !has163Step && trisIn > 0 && trisIn == trisOut;
        bool all = flowOk && normalsOk && propsOk;
        sb.AppendLine("CHECK existing zip-drop tris unchanged, no 163 steps: " + flowOk);
        sb.AppendLine("CHECK 160 Calculate/90/Mikk: " + normalsOk);
        sb.AppendLine("CHECK 149 properties generated: " + propsOk);
        sb.AppendLine(all ? "RESULT=OK" : "RESULT=FAIL");
        sb.AppendLine("PASS=" + all);

        Cleanup(sb);
        return sb.ToString();
    }

    static void BuildSourceZip(string zipPath, StringBuilder sb)
    {
        string srcAbs = Full(SrcDir);
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (string file in Directory.GetFiles(srcAbs))
            {
                string ext = Path.GetExtension(file);
                if (ext.Equals(".fbx", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    zip.CreateEntryFromFile(file, Path.GetFileName(file));
                }
            }
        }
        sb.AppendLine("zip bytes=" + new FileInfo(zipPath).Length);
    }

    static string FindBakeJson(string stem)
    {
        string dir = Full("Drop/_BakeOut/" + stem);
        if (!Directory.Exists(dir))
            return null;
        return Directory.GetFiles(dir, "*.bake.json").FirstOrDefault();
    }

    static string FindDestFbx(string stem)
    {
        string dir = "Assets/Objects/Pickable/" + stem;
        if (!AssetDatabase.IsValidFolder(dir))
            return null;
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { dir });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }

    static int ReadIntField(string json, string key)
    {
        int i = json.IndexOf(key, StringComparison.Ordinal);
        if (i < 0)
            return -1;
        i += key.Length;
        while (i < json.Length && (json[i] == ' ' || json[i] == '\n'))
            i++;
        int j = i;
        while (j < json.Length && char.IsDigit(json[j]))
            j++;
        if (j <= i)
            return -1;
        int n;
        return int.TryParse(json.Substring(i, j - i), out n) ? n : -1;
    }

    static void Cleanup(StringBuilder sb)
    {
        string dest = "Assets/Objects/Pickable/" + Stem;
        if (AssetDatabase.IsValidFolder(dest))
        {
            AssetDatabase.DeleteAsset(dest);
            sb.AppendLine("cleaned dest " + dest);
        }
        TryDeleteDir(Full("Assets/Objects/_Incoming/Pickable/" + Stem));
        TryDeleteDir(Full("Drop/_BakeOut/" + Stem));
        TryDeleteDir(Full("Drop/_Processed/Pickable/" + Stem));
        TryDeleteDir(Full("Drop/_Unpacked/Pickable/" + Stem));
        TryDeleteDir(Full("Drop/_NeedsReview/Pickable/" + Stem));
        string leftoverZip = Full("Drop/Pickable/" + Stem + ".zip");
        if (File.Exists(leftoverZip))
            File.Delete(leftoverZip);
        AssetDatabase.Refresh();
    }

    static void TryDeleteDir(string abs)
    {
        try
        {
            if (Directory.Exists(abs))
                Directory.Delete(abs, true);
        }
        catch (Exception)
        {
        }
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }

    static string Rel(string abs)
    {
        string root = Full(".");
        if (abs.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return abs.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
        return abs;
    }

    static void WriteReport(string text)
    {
        string full = Full(ReportPath);
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
