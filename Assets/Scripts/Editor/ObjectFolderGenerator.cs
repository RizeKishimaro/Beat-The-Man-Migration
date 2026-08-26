using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 149 — after 148 lands an FBX in Assets/Objects/{Pickable,Nodes}/&lt;Name&gt;/,
/// generate ObjectProperties, a script stub, and a URP Lit material from sibling
/// PNGs. Does not organize folders (148), does not fill judgment fields, does not
/// build prefabs. Idempotent: never overwrite an existing .asset / .cs / .mat.
/// </summary>
public class ObjectFolderGenerator : AssetPostprocessor
{
    public const string DestPickable = ObjectIncomingPostprocessor.DestPickable;
    public const string DestNodes = ObjectIncomingPostprocessor.DestNodes;

    const string LogPrefix = "[ObjectFolder149]";
    const string PickableTemplatePath = "Assets/Scripts/Editor/Templates/PickableStub.txt";
    const string NodeTemplatePath = "Assets/Scripts/Editor/Templates/NodeStub.txt";
    const string UrpLitShader = "Universal Render Pipeline/Lit";
    const float EmissionBlackMax = 2f / 255f;

    static readonly HashSet<string> QueuedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static bool generateQueued;
    static bool generating;

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.delayCall += () =>
        {
            QueueScan();
        };
    }

    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (generating)
            return;
        Collect(importedAssets);
        Collect(movedAssets);
        if (QueuedFolders.Count > 0)
            QueueGenerate();
    }

    static void Collect(string[] paths)
    {
        if (paths == null)
            return;
        for (int i = 0; i < paths.Length; i++)
        {
            if (TryGetObjectFolder(paths[i], out string folder, out _))
                QueuedFolders.Add(folder);
        }
    }

    static void QueueScan()
    {
        ScanDest(DestPickable);
        ScanDest(DestNodes);
        if (QueuedFolders.Count > 0)
            QueueGenerate();
    }

    static void ScanDest(string destRoot)
    {
        string abs = ToAbs(destRoot);
        if (!Directory.Exists(abs))
            return;
        foreach (string dir in Directory.GetDirectories(abs))
        {
            string name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name) || name.StartsWith("."))
                continue;
            string folder = Normalize(destRoot + "/" + name);
            if (FindFbx(folder) != null)
                QueuedFolders.Add(folder);
        }
    }

    static void QueueGenerate()
    {
        if (generateQueued)
            return;
        generateQueued = true;
        EditorApplication.delayCall += RunGenerate;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        QueueGenerate();
    }

    static void RunGenerate()
    {
        generateQueued = false;
        if (EditorApplication.isCompiling)
        {
            QueueGenerate();
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            return;
        }
        GenerateNow();
    }

    /// <summary>
    /// Process every queued object folder, then scan dest roots for any still missing.
    /// Safe to call from verify / batchmode.
    /// </summary>
    public static void GenerateNow()
    {
        if (generating)
            return;
        generating = true;
        EditorApplication.LockReloadAssemblies();
        try
        {
            ScanDest(DestPickable);
            ScanDest(DestNodes);
            var folders = new List<string>(QueuedFolders);
            QueuedFolders.Clear();
            for (int i = 0; i < folders.Count; i++)
                ProcessFolder(folders[i]);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorApplication.UnlockReloadAssemblies();
            generating = false;
        }
    }

    public static string ProcessFolder(string folder)
    {
        folder = Normalize(folder);
        if (string.IsNullOrEmpty(folder))
            return "skip empty folder";

        ObjectKind kind;
        if (IsUnder(folder, DestPickable) && folder != Normalize(DestPickable))
            kind = ObjectKind.Pickable;
        else if (IsUnder(folder, DestNodes) && folder != Normalize(DestNodes))
            kind = ObjectKind.Node;
        else
            return "skip not a dest object folder: " + folder;

        if (IsUnder(folder, ObjectIncomingPostprocessor.IncomingPickable)
            || IsUnder(folder, ObjectIncomingPostprocessor.IncomingNodes)
            || IsUnder(folder, "Assets/Objects/_Review"))
            return "skip incoming/review: " + folder;

        string fbx = FindFbx(folder);
        if (fbx == null)
            return "skip no FBX: " + folder;

        string name = Path.GetFileName(folder.TrimEnd('/'));
        var done = new StringBuilder();
        done.Append(EnsureProperties(folder, name, kind));
        done.Append(EnsureStub(folder, name, kind));
        done.Append(EnsureMaterial(folder, name, fbx));
        Debug.Log(LogPrefix + " " + name + ": " + done);
        return done.ToString();
    }

    static string EnsureProperties(string folder, string name, ObjectKind kind)
    {
        string path = folder + "/" + name + "_Properties.asset";
        if (AssetExists(path))
            return "properties=exists ";

        var so = ScriptableObject.CreateInstance<ObjectProperties>();
        so.objectName = name;
        so.folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folder);
        so.kind = kind;
        so.pickupable = kind == ObjectKind.Pickable;
        so.approved = false;
        so.weight = 0f;
        so.price = 0f;
        so.category = "";
        so.stackable = ObjectTriState.Unset;
        so.sizeTier = ObjectSizeTier.Unset;
        so.physicsGravity = ObjectPhysicsGravity.Unset;
        so.scaleMultiplier = 1f;
        so.pivotOffset = Vector3.zero;
        AssetDatabase.CreateAsset(so, path);
        return "properties=created ";
    }

    static string EnsureStub(string folder, string name, ObjectKind kind)
    {
        string className = SanitizeIdentifier(name);
        string path = folder + "/" + className + ".cs";
        if (AssetExists(path) || File.Exists(ToAbs(path)))
            return "stub=exists ";

        string templatePath = kind == ObjectKind.Pickable ? PickableTemplatePath : NodeTemplatePath;
        string template = ReadTemplate(templatePath, kind);
        string source = template
            .Replace("{CLASS_NAME}", className)
            .Replace("{OBJECT_NAME}", name);
        File.WriteAllText(ToAbs(path), source);
        AssetDatabase.ImportAsset(path);
        return "stub=created ";
    }

    static string ReadTemplate(string assetPath, ObjectKind kind)
    {
        string abs = ToAbs(assetPath);
        if (File.Exists(abs))
            return File.ReadAllText(abs);
        return kind == ObjectKind.Pickable ? EmbeddedPickableTemplate : EmbeddedNodeTemplate;
    }

    const string EmbeddedPickableTemplate =
        "using UnityEngine;\n\n"
        + "/// <summary>\n"
        + "/// Independent-layer stub for {OBJECT_NAME}. Template only — no behavior.\n"
        + "/// Master carry/physics stays on CarryableItem (wired at prefab-on-approve).\n"
        + "/// </summary>\n"
        + "public class {CLASS_NAME} : ImportedPickable\n"
        + "{\n"
        + "}\n";

    const string EmbeddedNodeTemplate =
        "using UnityEngine;\n\n"
        + "/// <summary>\n"
        + "/// Independent-layer stub for {OBJECT_NAME}. Template only — no behavior.\n"
        + "/// Master accept/slot stays on DefaultNode (wired at prefab-on-approve).\n"
        + "/// </summary>\n"
        + "public class {CLASS_NAME} : ImportedNode\n"
        + "{\n"
        + "}\n";

    static string EnsureMaterial(string folder, string name, string fbxPath)
    {
        string matPath = folder + "/" + name + ".mat";
        if (AssetExists(matPath))
        {
            RemapFbx(fbxPath, AssetDatabase.LoadAssetAtPath<Material>(matPath));
            return "mat=exists remap ";
        }

        SiblingMaps maps = FindSiblingMaps(folder);
        FixImporters(maps);

        string maskPath = folder + "/" + name + "_Mask.png";
        Texture2D mask = AssetExists(maskPath)
            ? AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath)
            : PackMask(maps, maskPath);

        Shader shader = Shader.Find(UrpLitShader);
        if (shader == null)
        {
            Debug.LogError(LogPrefix + " missing shader " + UrpLitShader);
            return "mat=FAIL no URP Lit ";
        }

        var mat = new Material(shader);
        mat.name = name;
        ApplyMaps(mat, maps, mask);
        AssetDatabase.CreateAsset(mat, matPath);
        EditorUtility.SetDirty(mat);
        RemapFbx(fbxPath, mat);
        return "mat=created remap ";
    }

    struct SiblingMaps
    {
        public string BaseColor;
        public string Normal;
        public string Roughness;
        public string Metallic;
        public string Emission;
    }

    static SiblingMaps FindSiblingMaps(string folder)
    {
        string abs = ToAbs(folder);
        var maps = new SiblingMaps();
        if (!Directory.Exists(abs))
            return maps;

        string[] files = Directory.GetFiles(abs, "*.png");
        for (int i = 0; i < files.Length; i++)
        {
            string fileName = Path.GetFileName(files[i]);
            if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;
            string n = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            string asset = ToAsset(files[i]);
            if (n == "meshy_basecolor" || n.EndsWith("_texture"))
            {
                if (maps.BaseColor == null)
                    maps.BaseColor = asset;
            }
            else if (n == "meshy_normal" || n.EndsWith("_normal") || n.EndsWith("_normalmap"))
            {
                if (maps.Normal == null)
                    maps.Normal = asset;
            }
            else if (n.EndsWith("_roughness"))
            {
                if (maps.Roughness == null)
                    maps.Roughness = asset;
            }
            else if (n == "meshy_metallic_smoothness" || n.EndsWith("_metallic") || n.EndsWith("_metalness"))
            {
                if (maps.Metallic == null)
                    maps.Metallic = asset;
            }
            else if (n == "meshy_emission" || n.EndsWith("_emission") || n.EndsWith("_emissive"))
            {
                if (maps.Emission == null)
                    maps.Emission = asset;
            }
        }
        return maps;
    }

    static void FixImporters(SiblingMaps maps)
    {
        ApplyTextureImporter(maps.BaseColor, TextureImporterType.Default, true);
        ApplyTextureImporter(maps.Normal, TextureImporterType.NormalMap, false);
        ApplyTextureImporter(maps.Roughness, TextureImporterType.Default, false);
        ApplyTextureImporter(maps.Metallic, TextureImporterType.Default, false);
        ApplyTextureImporter(maps.Emission, TextureImporterType.Default, true);
    }

    static void ApplyTextureImporter(string assetPath, TextureImporterType type, bool sRGB)
    {
        if (string.IsNullOrEmpty(assetPath))
            return;
        var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (ti == null)
            return;
        bool dirty = false;
        if (ti.textureType != type)
        {
            ti.textureType = type;
            dirty = true;
        }
        if (ti.sRGBTexture != sRGB)
        {
            ti.sRGBTexture = sRGB;
            dirty = true;
        }
        if (dirty)
            ti.SaveAndReimport();
    }

    static Texture2D PackMask(SiblingMaps maps, string maskAssetPath)
    {
        if (string.IsNullOrEmpty(maps.Metallic) && string.IsNullOrEmpty(maps.Roughness))
            return null;

        Texture2D metal = LoadReadable(maps.Metallic);
        Texture2D rough = LoadReadable(maps.Roughness);
        if (metal == null && rough == null)
            return null;

        int w = metal != null ? metal.width : rough.width;
        int h = metal != null ? metal.height : rough.height;
        Color[] mp = SamplePixels(metal, w, h);
        Color[] rp = SamplePixels(rough, w, h);
        var outp = new Color[w * h];
        for (int i = 0; i < outp.Length; i++)
        {
            float m = mp != null ? mp[i].r : 0f;
            float r = rp != null ? rp[i].r : 0f;
            outp[i] = new Color(m, m, m, 1f - r);
        }

        var packed = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        packed.SetPixels(outp);
        packed.Apply(false, false);
        File.WriteAllBytes(ToAbs(maskAssetPath), packed.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(packed);
        if (metal != null)
            UnityEngine.Object.DestroyImmediate(metal);
        if (rough != null)
            UnityEngine.Object.DestroyImmediate(rough);

        AssetDatabase.ImportAsset(maskAssetPath, ImportAssetOptions.ForceUpdate);
        var ti = AssetImporter.GetAtPath(maskAssetPath) as TextureImporter;
        if (ti != null)
        {
            bool dirty = false;
            if (ti.textureType != TextureImporterType.Default)
            {
                ti.textureType = TextureImporterType.Default;
                dirty = true;
            }
            if (ti.sRGBTexture)
            {
                ti.sRGBTexture = false;
                dirty = true;
            }
            if (ti.alphaIsTransparency)
            {
                ti.alphaIsTransparency = false;
                dirty = true;
            }
            if (ti.alphaSource != TextureImporterAlphaSource.FromInput)
            {
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                dirty = true;
            }
            if (dirty)
                ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(maskAssetPath);
    }

    static Texture2D LoadReadable(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;
        string abs = ToAbs(assetPath);
        if (!File.Exists(abs))
            return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!tex.LoadImage(File.ReadAllBytes(abs), false))
        {
            UnityEngine.Object.DestroyImmediate(tex);
            return null;
        }
        return tex;
    }

    static Color[] SamplePixels(Texture2D tex, int w, int h)
    {
        if (tex == null)
            return null;
        if (tex.width == w && tex.height == h)
            return tex.GetPixels();
        var arr = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                arr[y * w + x] = tex.GetPixelBilinear(u, v);
            }
        }
        return arr;
    }

    static void ApplyMaps(Material mat, SiblingMaps maps, Texture2D mask)
    {
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);

        Texture2D baseMap = LoadAssetTex(maps.BaseColor);
        if (baseMap != null)
        {
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", baseMap);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", baseMap);
        }

        Texture2D normal = LoadAssetTex(maps.Normal);
        if (normal != null && mat.HasProperty("_BumpMap"))
        {
            mat.SetTexture("_BumpMap", normal);
            if (mat.HasProperty("_BumpScale"))
                mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }
        else
        {
            mat.DisableKeyword("_NORMALMAP");
        }

        if (mask != null && mat.HasProperty("_MetallicGlossMap"))
        {
            mat.SetTexture("_MetallicGlossMap", mask);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 1f);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 1f);
            if (mat.HasProperty("_SmoothnessTextureChannel"))
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else
        {
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
        }

        Texture2D emission = LoadAssetTex(maps.Emission);
        if (emission != null && mat.HasProperty("_EmissionMap"))
        {
            mat.SetTexture("_EmissionMap", emission);
            bool lit = EmissionIsLit(maps.Emission);
            if (lit)
            {
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", Color.white);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", Color.black);
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }
        else
        {
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", Color.black);
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
    }

    static Texture2D LoadAssetTex(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    static bool EmissionIsLit(string assetPath)
    {
        Texture2D tmp = LoadReadable(assetPath);
        if (tmp == null)
            return false;
        Color[] px = tmp.GetPixels();
        UnityEngine.Object.DestroyImmediate(tmp);
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].r > EmissionBlackMax || px[i].g > EmissionBlackMax || px[i].b > EmissionBlackMax)
                return true;
        }
        return false;
    }

    static void RemapFbx(string fbxPath, Material mat)
    {
        if (mat == null)
            return;
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
            return;

        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

        var names = new HashSet<string>(StringComparer.Ordinal);
        CollectMaterialNames(fbxPath, names);
        if (names.Count == 0)
            names.Add(mat.name);

        foreach (string nm in names)
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), nm), mat);

        importer.SaveAndReimport();
    }

    static void CollectMaterialNames(string fbxPath, HashSet<string> names)
    {
        UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        if (all != null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                var m = all[i] as Material;
                if (m != null && !string.IsNullOrEmpty(m.name))
                    names.Add(StripInstance(m.name));
            }
        }

        var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (go == null)
            return;
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            Material[] sms = rs[i].sharedMaterials;
            for (int j = 0; j < sms.Length; j++)
            {
                if (sms[j] != null && !string.IsNullOrEmpty(sms[j].name))
                    names.Add(StripInstance(sms[j].name));
            }
        }
    }

    static string StripInstance(string name)
    {
        const string suffix = " (Instance)";
        if (name.EndsWith(suffix, StringComparison.Ordinal))
            return name.Substring(0, name.Length - suffix.Length);
        return name;
    }

    Material OnAssignMaterialModel(Material material, Renderer renderer)
    {
        if (!TryGetObjectFolder(assetPath, out string folder, out _))
            return null;
        string name = Path.GetFileName(folder.TrimEnd('/'));
        string matPath = folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        return mat;
    }

    static bool TryGetObjectFolder(string assetPath, out string folder, out ObjectKind kind)
    {
        folder = null;
        kind = ObjectKind.Pickable;
        if (string.IsNullOrEmpty(assetPath))
            return false;
        string n = Normalize(assetPath);
        if (IsUnder(n, ObjectIncomingPostprocessor.IncomingPickable)
            || IsUnder(n, ObjectIncomingPostprocessor.IncomingNodes)
            || IsUnder(n, "Assets/Objects/_Review")
            || IsUnder(n, "Assets/Art"))
            return false;

        folder = ChildFolder(n, DestPickable);
        if (folder != null)
        {
            kind = ObjectKind.Pickable;
            return true;
        }
        folder = ChildFolder(n, DestNodes);
        if (folder != null)
        {
            kind = ObjectKind.Node;
            return true;
        }
        return false;
    }

    static string ChildFolder(string assetPath, string destRoot)
    {
        string dest = Normalize(destRoot).TrimEnd('/');
        string n = Normalize(assetPath);
        if (!n.StartsWith(dest + "/", StringComparison.OrdinalIgnoreCase))
            return null;
        string rest = n.Substring(dest.Length + 1);
        int slash = rest.IndexOf('/');
        string name = slash < 0 ? rest : rest.Substring(0, slash);
        if (string.IsNullOrEmpty(name) || name.StartsWith("."))
            return null;
        if (slash < 0)
            return null;
        return dest + "/" + name;
    }

    static string FindFbx(string folder)
    {
        string abs = ToAbs(folder);
        if (!Directory.Exists(abs))
            return null;
        foreach (string file in Directory.GetFiles(abs, "*.fbx"))
            return Normalize(ToAsset(file));
        foreach (string file in Directory.GetFiles(abs, "*.FBX"))
            return Normalize(ToAsset(file));
        return null;
    }

    static string SanitizeIdentifier(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "ImportedObject";
        var sb = new StringBuilder(raw.Length + 1);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        if (sb.Length == 0 || char.IsDigit(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }

    static bool AssetExists(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;
        return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null
            || File.Exists(ToAbs(assetPath));
    }

    static bool IsUnder(string path, string folder)
    {
        path = Normalize(path);
        folder = Normalize(folder).TrimEnd('/');
        return path.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    }

    static string Normalize(string path)
    {
        return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
    }

    static string ToAbs(string assetPath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", Normalize(assetPath)));
    }

    static string ToAsset(string absPath)
    {
        string full = Path.GetFullPath(absPath).Replace('\\', '/');
        string data = Application.dataPath.Replace('\\', '/');
        string root = data.Substring(0, data.Length - "Assets".Length);
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return full.Substring(root.Length);
        return null;
    }

    [MenuItem("Tools/Objects/Generate Properties + Materials (149)")]
    static void MenuGenerate()
    {
        GenerateNow();
        EditorUtility.DisplayDialog("Ticket 149", "Generated missing Properties / stubs / materials under Objects/Pickable and Objects/Nodes.", "OK");
    }
}
