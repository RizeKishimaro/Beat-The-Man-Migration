using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 148 — watch Assets/Objects/_Incoming/{Pickable,Nodes} for a baked FBX
/// (+ sibling textures), then MOVE them into Assets/Objects/{Pickable,Nodes}/&lt;Name&gt;/.
///
    /// Detection is Unity AssetPostprocessor (Editor must be open). Does not guess
    /// Pickable vs Node: drop folder is the classification. Ticket 161: a re-drop
    /// into the other Incoming kind removes the stale dest folder (same name or
    /// same FBX filename) so two stubs cannot share a class name. Does not retouch
    /// FBX importer scale/rotation/pivot — Blender recipe is assumed already applied.
    /// Ticket 160 normals default (Calculate, 90°) lives in MeshyModelImportDefaults.
    /// Hash-mismatch never overwrites. Out of scope: 149/150, prefabs, Airtable.
/// </summary>
public class ObjectIncomingPostprocessor : AssetPostprocessor
{
    public const string IncomingPickable = "Assets/Objects/_Incoming/Pickable";
    public const string IncomingNodes = "Assets/Objects/_Incoming/Nodes";
    public const string DestPickable = "Assets/Objects/Pickable";
    public const string DestNodes = "Assets/Objects/Nodes";

    const string LogPrefix = "[ObjectIncoming]";
    const int StableMs = 500;

    static readonly string[] TextureExts =
    {
        ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".psd", ".bmp"
    };

    static readonly HashSet<string> WarnedThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    static bool organizing;
    static bool organizeQueued;

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.delayCall += () =>
        {
            EnsureFolders();
            QueueOrganize();
        };
    }

    [MenuItem("Tools/Objects/Organize Incoming Now")]
    public static void MenuOrganizeNow()
    {
        Debug.Log(LogPrefix + " menu OrganizeNow");
        OrganizeNow();
    }

    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (AnyIncoming(importedAssets) || AnyIncoming(movedAssets))
            QueueOrganize();
    }

    static bool AnyIncoming(string[] paths)
    {
        if (paths == null)
            return false;
        for (int i = 0; i < paths.Length; i++)
        {
            if (IsIncomingAsset(paths[i]))
                return true;
        }
        return false;
    }

    static bool IsIncomingAsset(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;
        string n = Normalize(assetPath);
        return IsUnder(n, IncomingPickable) || IsUnder(n, IncomingNodes);
    }

    static void QueueOrganize()
    {
        if (organizeQueued)
            return;
        organizeQueued = true;
        EditorApplication.delayCall += RunOrganize;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        QueueOrganize();
    }

    static void RunOrganize()
    {
        organizeQueued = false;
        if (EditorApplication.isCompiling)
        {
            Debug.Log(LogPrefix + " defer organize — compiling");
            QueueOrganize();
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log(LogPrefix + " defer organize — playmode");
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            return;
        }

        if (organizing)
            return;
        organizing = true;
        try
        {
            EnsureFolders();
            OrganizeKind(IncomingPickable, DestPickable, "Pickable");
            OrganizeKind(IncomingNodes, DestNodes, "Nodes");
        }
        finally
        {
            organizing = false;
        }
    }

    /// <summary>
    /// Ticket 153 handoff — run organize immediately after files land in
    /// _Incoming (batchmode cannot rely on delayCall before -quit).
    /// </summary>
    public static void OrganizeNow()
    {
        organizeQueued = false;
        RunOrganize();
    }

    static void EnsureFolders()
    {
        EnsureFolder("Assets/Objects", "_Incoming");
        EnsureFolder("Assets/Objects/_Incoming", "Pickable");
        EnsureFolder("Assets/Objects/_Incoming", "Nodes");
        EnsureFolder("Assets/Objects", "Pickable");
        EnsureFolder("Assets/Objects", "Nodes");
    }

    static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (AssetDatabase.IsValidFolder(path))
            return;
        if (!AssetDatabase.IsValidFolder(parent))
            return;
        AssetDatabase.CreateFolder(parent, name);
    }

    static void OrganizeKind(string incomingRoot, string destRoot, string kindLabel)
    {
        string absIncoming = ToAbs(incomingRoot);
        if (!Directory.Exists(absIncoming))
            return;

        var fbxAssets = new List<string>();
        foreach (string file in Directory.GetFiles(absIncoming, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                continue;
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;
            string asset = ToAsset(file);
            if (!string.IsNullOrEmpty(asset))
                fbxAssets.Add(Normalize(asset));
        }

        if (fbxAssets.Count == 0)
            return;

        // Unstable copy (Finder still writing) — retry shortly, do not process a partial FBX.
        foreach (string asset in fbxAssets)
        {
            if (!IsFileStable(ToAbs(asset)))
            {
                QueueOrganize();
                return;
            }
        }

        var folderFbxs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var looseFbxs = new List<string>();
        string incomingNorm = Normalize(incomingRoot);

        foreach (string fbx in fbxAssets)
        {
            string parent = ParentAssetFolder(fbx);
            if (string.Equals(parent, incomingNorm, StringComparison.OrdinalIgnoreCase))
                looseFbxs.Add(fbx);
            else
            {
                if (!folderFbxs.TryGetValue(parent, out List<string> list))
                {
                    list = new List<string>();
                    folderFbxs[parent] = list;
                }
                list.Add(fbx);
            }
        }

        foreach (string fbx in looseFbxs)
        {
            string name = SanitizeName(Path.GetFileNameWithoutExtension(fbx));
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning($"{LogPrefix} skip unnamed FBX: {fbx}");
                continue;
            }
            ProcessDrop(kindLabel, fbx, name, destRoot, CollectLooseSiblings(fbx, incomingNorm));
        }

        foreach (var kvp in folderFbxs)
        {
            if (kvp.Value.Count != 1)
            {
                Debug.LogWarning(
                    $"{LogPrefix} skip folder with {kvp.Value.Count} FBXs (want exactly 1): {kvp.Key}");
                continue;
            }
            string fbx = kvp.Value[0];
            string name = SanitizeName(Path.GetFileName(kvp.Key));
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning($"{LogPrefix} skip unnamed folder drop: {kvp.Key}");
                continue;
            }
            ProcessDrop(kindLabel, fbx, name, destRoot, CollectFolderSiblings(kvp.Key, fbx));
        }
    }

    static List<string> CollectLooseSiblings(string fbxAsset, string incomingRoot)
    {
        var siblings = new List<string>();
        string stem = Path.GetFileNameWithoutExtension(fbxAsset);
        string absDir = ToAbs(incomingRoot);
        if (!Directory.Exists(absDir))
            return siblings;

        foreach (string file in Directory.GetFiles(absDir))
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;
            if (file.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsTextureFile(file))
                continue;
            string fileStem = Path.GetFileNameWithoutExtension(file);
            if (fileStem.Equals(stem, StringComparison.OrdinalIgnoreCase)
                || fileStem.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase))
            {
                siblings.Add(Normalize(ToAsset(file)));
            }
        }

        string fbm = incomingRoot + "/" + stem + ".fbm";
        if (AssetDatabase.IsValidFolder(fbm))
            siblings.Add(Normalize(fbm));

        return siblings;
    }

    static List<string> CollectFolderSiblings(string folderAsset, string fbxAsset)
    {
        var siblings = new List<string>();
        string absDir = ToAbs(folderAsset);
        if (!Directory.Exists(absDir))
            return siblings;

        string fbxNorm = Normalize(fbxAsset);
        foreach (string file in Directory.GetFiles(absDir))
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;
            string asset = ToAsset(file);
            if (string.IsNullOrEmpty(asset))
                continue;
            asset = Normalize(asset);
            if (asset.Equals(fbxNorm, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsTextureFile(file))
                siblings.Add(asset);
        }

        foreach (string dir in Directory.GetDirectories(absDir))
        {
            string name = Path.GetFileName(dir);
            if (name.EndsWith(".fbm", StringComparison.OrdinalIgnoreCase))
                siblings.Add(Normalize(ToAsset(dir)));
        }

        return siblings;
    }

    static void ProcessDrop(string kindLabel, string fbxAsset, string name, string destRoot, List<string> siblings)
    {
        string destDir = destRoot + "/" + name;
        string destFbx = destDir + "/" + Path.GetFileName(fbxAsset);
        string absIncomingFbx = ToAbs(fbxAsset);
        RemoveStaleCrossKindFolders(destRoot, name, Path.GetFileName(fbxAsset));

        if (AssetDatabase.IsValidFolder(destDir) || Directory.Exists(ToAbs(destDir)))
        {
            string existingFbx = FindDestFbx(destDir, Path.GetFileName(fbxAsset));
            if (string.IsNullOrEmpty(existingFbx))
            {
                WarnOnce(
                    fbxAsset + "|nofbx",
                    $"{LogPrefix} dest exists without an FBX — skip, will not overwrite {destDir}");
                return;
            }

            string incomingHash = HashFile(absIncomingFbx);
            string destHash = HashFile(ToAbs(existingFbx));
            if (string.IsNullOrEmpty(incomingHash) || string.IsNullOrEmpty(destHash))
            {
                WarnOnce(
                    fbxAsset + "|hashfail",
                    $"{LogPrefix} could not hash FBX — skip {fbxAsset}");
                return;
            }

            if (incomingHash == destHash)
            {
                Debug.Log(
                    $"{LogPrefix} skip (same hash) {kindLabel}/{name} — already at {existingFbx}");
                DeleteIncomingDrop(fbxAsset, siblings);
                return;
            }

            WarnOnce(
                fbxAsset + "|" + incomingHash,
                $"{LogPrefix} dest exists with a different FBX hash — skip, will not overwrite {existingFbx}. Incoming left at {fbxAsset}");
            return;
        }

        if (!AssetDatabase.IsValidFolder(destRoot))
            EnsureFolders();
        if (!AssetDatabase.IsValidFolder(destDir))
        {
            string created = AssetDatabase.CreateFolder(destRoot, name);
            if (string.IsNullOrEmpty(created))
            {
                Debug.LogError($"{LogPrefix} could not create {destDir}");
                return;
            }
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            string err = AssetDatabase.MoveAsset(fbxAsset, destFbx);
            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogError($"{LogPrefix} move FBX failed {fbxAsset} → {destFbx}: {err}");
                return;
            }

            foreach (string sibling in siblings)
            {
                if (string.IsNullOrEmpty(sibling) || !AssetExists(sibling))
                    continue;
                string destSibling = destDir + "/" + Path.GetFileName(sibling);
                if (AssetExists(destSibling))
                {
                    Debug.LogWarning($"{LogPrefix} sibling already at dest, skip move: {destSibling}");
                    continue;
                }
                string sErr = AssetDatabase.MoveAsset(sibling, destSibling);
                if (!string.IsNullOrEmpty(sErr))
                    Debug.LogWarning($"{LogPrefix} move sibling failed {sibling} → {destSibling}: {sErr}");
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        DeleteIncomingMaterials(fbxAsset);
        CleanupEmptyIncomingFolder(fbxAsset);
        Debug.Log($"{LogPrefix} moved {kindLabel}/{name} → {destFbx}");
    }

    /// <summary>
    /// Ticket 161 — reclassification re-drop. Remove dest folders in either
    /// Pickable or Nodes that share this drop's folder name or FBX filename,
    /// except the folder we are about to land in.
    /// </summary>
    static void RemoveStaleCrossKindFolders(string destRoot, string name, string fbxFileName)
    {
        string keep = Normalize(destRoot + "/" + name);
        var stale = new List<string>();
        CollectStaleDestFolders(DestPickable, name, fbxFileName, keep, stale);
        CollectStaleDestFolders(DestNodes, name, fbxFileName, keep, stale);
        for (int i = 0; i < stale.Count; i++)
        {
            Debug.LogWarning($"{LogPrefix} reclassification: removing stale {stale[i]} (landing {keep})");
            if (!AssetDatabase.DeleteAsset(stale[i]))
                Debug.LogError($"{LogPrefix} failed to delete stale {stale[i]}");
        }
    }

    static void CollectStaleDestFolders(
        string scanRoot, string newName, string fbxFileName, string keep, List<string> stale)
    {
        string abs = ToAbs(scanRoot);
        if (!Directory.Exists(abs))
            return;

        foreach (string dir in Directory.GetDirectories(abs))
        {
            string folderName = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(folderName) || folderName.StartsWith("."))
                continue;

            string folder = Normalize(scanRoot + "/" + folderName);
            if (string.Equals(folder, keep, StringComparison.OrdinalIgnoreCase))
                continue;

            bool nameMatch = folderName.Equals(newName, StringComparison.OrdinalIgnoreCase);
            bool fbxMatch = false;
            if (!string.IsNullOrEmpty(fbxFileName))
            {
                string candidate = folder + "/" + fbxFileName;
                if (File.Exists(ToAbs(candidate)))
                    fbxMatch = true;
            }

            if (nameMatch || fbxMatch)
                stale.Add(folder);
        }
    }

    static void DeleteIncomingMaterials(string fbxAsset)
    {
        string parent = ParentAssetFolder(fbxAsset);
        if (!IsIncomingAsset(parent))
            return;
        string materials = parent + "/Materials";
        if (AssetDatabase.IsValidFolder(materials))
            AssetDatabase.DeleteAsset(materials);
    }

    static void DeleteIncomingDrop(string fbxAsset, List<string> siblings)
    {
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string sibling in siblings)
            {
                if (AssetExists(sibling) && IsIncomingAsset(sibling))
                    AssetDatabase.DeleteAsset(sibling);
            }
            if (AssetExists(fbxAsset))
                AssetDatabase.DeleteAsset(fbxAsset);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        CleanupEmptyIncomingFolder(fbxAsset);
    }

    static void CleanupEmptyIncomingFolder(string fbxAsset)
    {
        string parent = ParentAssetFolder(fbxAsset);
        if (string.Equals(parent, IncomingPickable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parent, IncomingNodes, StringComparison.OrdinalIgnoreCase))
            return;
        if (!IsIncomingAsset(parent) || !AssetDatabase.IsValidFolder(parent))
            return;

        string abs = ToAbs(parent);
        if (!Directory.Exists(abs))
            return;
        var leftover = Directory.GetFileSystemEntries(abs);
        bool onlyMeta = true;
        foreach (string entry in leftover)
        {
            if (!entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(entry).Equals(".gitkeep", StringComparison.OrdinalIgnoreCase))
            {
                onlyMeta = false;
                break;
            }
        }
        if (onlyMeta)
            AssetDatabase.DeleteAsset(parent);
    }

    static string FindDestFbx(string destDir, string preferredFileName)
    {
        string preferred = destDir + "/" + preferredFileName;
        if (AssetExists(preferred))
            return Normalize(preferred);

        string abs = ToAbs(destDir);
        if (!Directory.Exists(abs))
            return null;
        foreach (string file in Directory.GetFiles(abs, "*.fbx"))
            return Normalize(ToAsset(file));
        foreach (string file in Directory.GetFiles(abs, "*.FBX"))
            return Normalize(ToAsset(file));
        return null;
    }

    static bool IsFileStable(string absPath)
    {
        if (!File.Exists(absPath))
            return false;
        var info = new FileInfo(absPath);
        if (info.Length <= 0)
            return false;
        double ageMs = (DateTime.UtcNow - info.LastWriteTimeUtc).TotalMilliseconds;
        return ageMs >= StableMs;
    }

    static bool IsTextureFile(string path)
    {
        string ext = Path.GetExtension(path);
        for (int i = 0; i < TextureExts.Length; i++)
        {
            if (ext.Equals(TextureExts[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static string SanitizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        string name = raw.Trim();
        if (name == "." || name == ".." || name == ".gitkeep")
            return null;
        char[] bad = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
        foreach (char c in bad)
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    static string HashFile(string absPath)
    {
        if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath))
            return null;
        try
        {
            using (var sha = SHA256.Create())
            using (var fs = File.Open(absPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
        catch (IOException)
        {
            return null;
        }
    }

    static void WarnOnce(string key, string message)
    {
        if (!WarnedThisSession.Add(key))
            return;
        Debug.LogWarning(message);
    }

    static bool AssetExists(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;
        return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null
            || File.Exists(ToAbs(assetPath))
            || Directory.Exists(ToAbs(assetPath));
    }

    static bool IsUnder(string path, string folder)
    {
        path = Normalize(path);
        folder = Normalize(folder).TrimEnd('/');
        return path.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    }

    static string ParentAssetFolder(string assetPath)
    {
        string n = Normalize(assetPath).TrimEnd('/');
        int slash = n.LastIndexOf('/');
        return slash <= 0 ? n : n.Substring(0, slash);
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
}
