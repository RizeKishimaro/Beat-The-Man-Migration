using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 168 — bake grass WorldFloor + build-prompt objects into Alpha01.
/// Does not save GridFoundation. Does not spawn rooms.
/// Flag: Temp/alpha01_168_setup.flag
/// Report: Temp/alpha01_168/setup.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_168SceneSetup
{
    public const string ScenePath = Alpha01SceneSetup.ScenePath;
    public const string GrassTexPath = "Assets/Art/Environment/Grass_Albedo.png";
    public const string GrassMatPath = "Assets/Art/Environment/Grass_Mat.mat";
    public const string BuildRootName = "BuildPlacement";
    public const float FloorMeters = 96f;
    public const float FloorThickness = 0.2f;

    const string FlagPath = "Temp/alpha01_168_setup.flag";
    const string ReportPath = "Temp/alpha01_168/setup.txt";
    const int TexSize = 128;

    static Alpha01_168SceneSetup()
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

    [MenuItem("Tools/Scenes/168 Setup Alpha01 Grass + Build Prompt")]
    public static void MenuSetup()
    {
        string report = RunSetup();
        WriteReport(report);
        EditorUtility.DisplayDialog("168 Setup", Truncate(report, 1600), "OK");
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
            string report = RunSetup();
            WriteReport(report);
            Debug.Log("[Alpha01_168SceneSetup]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Alpha01_168SceneSetup] " + e);
        }
    }

    public static string RunSetup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 168 Alpha01 grass + build prompt ===");
        sb.AppendLine("does not save GridFoundation");
        sb.AppendLine("does not spawn rooms (169)");

        DiscardDirtyIfNotAlpha01(sb);
        EnsureGrassAssets(sb);

        if (!File.Exists(Full(ScenePath)))
        {
            sb.AppendLine("FAIL missing " + ScenePath);
            return sb.ToString();
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("opened " + scene.path);

        Material grass = AssetDatabase.LoadAssetAtPath<Material>(GrassMatPath);
        if (grass == null)
        {
            sb.AppendLine("FAIL missing " + GrassMatPath);
            return sb.ToString();
        }

        GameObject floor = FindRoot(scene, WorldFloorEnsure.FloorName);
        if (floor != null)
            Object.DestroyImmediate(floor);

        floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(floor, scene);
        floor.name = WorldFloorEnsure.FloorName;
        floor.transform.SetPositionAndRotation(
            new Vector3(0f, -FloorThickness * 0.5f, 0f),
            Quaternion.identity);
        floor.transform.localScale = new Vector3(FloorMeters, FloorThickness, FloorMeters);
        Rigidbody rb = floor.GetComponent<Rigidbody>();
        if (rb != null)
            Object.DestroyImmediate(rb);
        var rend = floor.GetComponent<Renderer>();
        if (rend != null)
            rend.sharedMaterial = grass;
        floor.isStatic = true;
        sb.AppendLine("WorldFloor pos=" + floor.transform.position
            + " scale=" + floor.transform.localScale
            + " mat=" + GrassMatPath);

        GameObject build = FindRoot(scene, BuildRootName);
        if (build != null)
            Object.DestroyImmediate(build);
        build = new GameObject(BuildRootName);
        SceneManager.MoveGameObjectToScene(build, scene);
        build.AddComponent<BuildMenuController>();
        build.AddComponent<RoomSlotPlusPrompt>();
        sb.AppendLine("BuildPlacement + BuildMenuController + RoomSlotPlusPrompt");

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved=" + saved + " " + ScenePath);

        // Reload from disk so the Editor matches the written scene.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("reloaded " + SceneManager.GetActiveScene().path);
        sb.AppendLine(saved && SceneManager.GetActiveScene().path == ScenePath ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static void DiscardDirtyIfNotAlpha01(StringBuilder sb)
    {
        Scene current = SceneManager.GetActiveScene();
        if (current.path == ScenePath || !current.isDirty)
            return;
        if (string.IsNullOrEmpty(current.path))
            return;
        string path = current.path;
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        sb.AppendLine("discarded dirty " + path + " (did not save)");
    }

    static void EnsureGrassAssets(StringBuilder sb)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art"))
            AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder("Assets/Art/Environment"))
            AssetDatabase.CreateFolder("Assets/Art", "Environment");

        WriteGrassPng(Full(GrassTexPath));
        AssetDatabase.ImportAsset(GrassTexPath);
        var importer = AssetImporter.GetAtPath(GrassTexPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 128;
            importer.SaveAndReimport();
        }
        sb.AppendLine("grass tex=" + GrassTexPath);

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexPath);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(GrassMatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, GrassMatPath);
            mat = AssetDatabase.LoadAssetAtPath<Material>(GrassMatPath);
        }
        if (mat.shader != shader && shader != null)
            mat.shader = shader;
        if (tex != null)
        {
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetTextureScale("_BaseMap", new Vector2(FloorMeters, FloorMeters));
            }
            if (mat.HasProperty("_MainTex"))
            {
                mat.SetTexture("_MainTex", tex);
                mat.SetTextureScale("_MainTex", new Vector2(FloorMeters, FloorMeters));
            }
        }
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.08f);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);
        sb.AppendLine("grass mat tiling=" + FloorMeters);
    }

    static void WriteGrassPng(string fullPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGB24, false);
        Color a = new Color(0.23f, 0.42f, 0.18f);
        Color b = new Color(0.32f, 0.52f, 0.22f);
        Color c = new Color(0.18f, 0.34f, 0.14f);
        for (int y = 0; y < TexSize; y++)
        {
            for (int x = 0; x < TexSize; x++)
            {
                float n = Hash(x, y);
                float n2 = Hash((x * 3) & (TexSize - 1), (y * 3) & (TexSize - 1));
                float t = n * 0.65f + n2 * 0.35f;
                Color col = t < 0.33f ? a : (t < 0.66f ? b : c);
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        File.WriteAllBytes(fullPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static float Hash(int x, int y)
    {
        int n = x * 374761393 + y * 668265263;
        n = (n ^ (n >> 13)) * 1274126177;
        return (n & 0x7fffffff) / (float)int.MaxValue;
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root != null && root.name == name)
                return root;
        return null;
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
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
        return s.Substring(0, max) + "\n…";
    }
}
