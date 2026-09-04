using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 160 fold-in — pipeline-wide FBX normals default.
///
/// WHERE THIS LIVES: this class is the default. There is no .fbx importer
/// preset and no Project Settings override. Unity's stock FBX default is
/// Import; this AssetPostprocessor replaces that for Meshy pipeline drops.
///
/// Applies on first import of files under:
///   Assets/Objects/_Incoming/{Pickable,Nodes}/
///   Assets/Objects/{Pickable,Nodes}/&lt;Name&gt;/
/// Smoothing angle 90 (the value that cleared blotches on chair + cooler).
/// Tangents stay CalculateMikk.
///
/// Does NOT touch Assets/Art, Vehicles, _Review, or _Bake152.
/// 148 still does not retouch scale/rotation/pivot (152 recipe).
/// 149 RemapFbx still only remaps materials; OnPreprocessModel fires on that
/// reimport and sets normals without fighting material remaps.
/// 152 FACE export writes Blender smoothing into the FBX; Calculate ignores
/// those vertex normals and recomputes in Unity — that is the blotch fix.
/// </summary>
public class MeshyModelImportDefaults : AssetPostprocessor
{
    public const float NormalSmoothingAngle = 90f;

    void OnPreprocessModel()
    {
        if (!IsPipelineModel(assetPath))
            return;

        var importer = assetImporter as ModelImporter;
        if (importer == null)
            return;

        importer.importNormals = ModelImporterNormals.Calculate;
        importer.normalSmoothingAngle = NormalSmoothingAngle;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
    }

    static bool IsPipelineModel(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        string n = assetPath.Replace('\\', '/');
        return IsUnder(n, ObjectIncomingPostprocessor.IncomingPickable)
            || IsUnder(n, ObjectIncomingPostprocessor.IncomingNodes)
            || IsDestChild(n, ObjectIncomingPostprocessor.DestPickable)
            || IsDestChild(n, ObjectIncomingPostprocessor.DestNodes);
    }

    static bool IsDestChild(string path, string destRoot)
    {
        string dest = destRoot.Replace('\\', '/').TrimEnd('/');
        if (!path.StartsWith(dest + "/", StringComparison.OrdinalIgnoreCase))
            return false;
        string rest = path.Substring(dest.Length + 1);
        return rest.IndexOf('/') > 0;
    }

    static bool IsUnder(string path, string folder)
    {
        path = path.Replace('\\', '/');
        folder = folder.Replace('\\', '/').TrimEnd('/');
        return path.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    }
}
