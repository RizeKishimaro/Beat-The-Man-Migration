using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 135/136 — the single source of truth for how the F1 car is assembled.
///
/// Both WorkshopRoomSceneSetup (132, builds the car into the scene) and
/// F1CarSyncTool (136, keeps the prefab in step with the parts folder) build the
/// same assembly, so the scale, the folder layout and the authored-material map
/// live here rather than in either of them. Two copies of the root scale
/// drifting apart is precisely the failure 135 exists to prevent.
/// </summary>
public static class F1CarConvention
{
    /// <summary>
    /// Ticket 135 — the car's game size. Parts are authored and exported at true
    /// 1 Blender unit = 1 metre and are already consistent with each other, so
    /// this is the ONLY scale applied anywhere: once, on the assembled car root.
    /// Never scale an individual part — that is what 135 exists to prevent.
    /// 1.6 was first chosen player-relative (~1.1m tall against the 1.56m
    /// player); doubled to 3.2 on review — it read far too small in the room.
    /// Settled at 3.
    /// </summary>
    public const float Scale = 3f;

    /// <summary>Yaw on the car root so the nose faces the isometric camera.</summary>
    public const float Yaw = 180f;

    public const string RootName = "F1_Car";

    /// <summary>
    /// One folder per part, holding an FBX named after its folder:
    ///     Objects/F1 Car/F1_Chassis/F1_Chassis.fbx
    /// The folder root also holds .blend working files and retired exports
    /// (e.g. Wheel_ChromeDrift.fbx, superseded per ticket 133), so only
    /// subfolders count as parts — never loose files at the top level.
    /// </summary>
    public const string PartsDir = "Assets/Objects/F1 Car";

    public const string PrefabDir = "Assets/Prefabs/Vehicles";
    public const string PrefabPath = PrefabDir + "/F1_Car.prefab";

    /// <summary>
    /// Blender's FBX exporter carries material slots but not their textures, so
    /// the wheel decal arrives as a blank opaque material and vanishes against
    /// the black tyre. The real materials were authored Unity-side in ticket 130
    /// (WheelChromeDriftSceneSetup) — reuse those rather than rebuilding them.
    /// Keyed on the Blender material name baked into the FBX.
    /// </summary>
    public static readonly Dictionary<string, string> AuthoredMaterials = new Dictionary<string, string>
    {
        { "Tire_Rubber", "Assets/Art/Vehicles/Wheel_ChromeDrift/Tire_Rubber.mat" },
        { "Wheel_HubWhiteChrome", "Assets/Art/Vehicles/Wheel_ChromeDrift/Wheel_HubWhiteChrome.mat" },
        { "Wheel_GlossBlack", "Assets/Art/Vehicles/Wheel_ChromeDrift/Wheel_GlossBlack.mat" },
        { "Tire_Decal_SpeedFreaks", "Assets/Art/Vehicles/Wheel_ChromeDrift/Tire_Decal.mat" },
        { "Tire_Decal", "Assets/Art/Vehicles/Wheel_ChromeDrift/Tire_Decal.mat" },
        { "F1_Body_Game", "Assets/Art/Vehicles/F1_Body_White.mat" },
        { "F1_Body", "Assets/Art/Vehicles/F1_Body_White.mat" },
    };

    /// <summary>A part FBX discovered under <see cref="PartsDir"/>.</summary>
    public struct Part
    {
        public string Name;      // folder name, which is also the part name
        public string FbxPath;   // Assets-relative path to the FBX
        public string Guid;      // FBX asset GUID — the identity the prefab is matched on
    }

    /// <summary>
    /// Every part folder that actually holds its matching FBX, name-sorted so the
    /// result is deterministic. Parts all sit at local zero, so order is
    /// cosmetic — but a stable order keeps the prefab's YAML diff quiet.
    /// </summary>
    public static List<Part> DiscoverParts(StringBuilder sb = null)
    {
        var parts = new List<Part>();
        if (!AssetDatabase.IsValidFolder(PartsDir))
        {
            sb?.AppendLine($"FAIL: parts folder missing: {PartsDir}");
            return parts;
        }

        foreach (string folder in AssetDatabase.GetSubFolders(PartsDir).OrderBy(f => f))
        {
            string name = Path.GetFileName(folder);
            string fbx = $"{folder}/{name}.fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(fbx) == null)
            {
                // A folder without its same-named FBX is either mid-export or
                // misnamed. Surface it rather than silently skipping.
                sb?.AppendLine($"WARN part folder '{name}' has no {name}.fbx — skipped");
                continue;
            }
            parts.Add(new Part
            {
                Name = name,
                FbxPath = fbx,
                Guid = AssetDatabase.AssetPathToGUID(fbx),
            });
        }
        return parts;
    }

    /// <summary>
    /// Every part is exported from the one master at 1 unit = 1m with a shared
    /// origin, so each drops in at local zero and self-assembles. Wheels are the
    /// exception: their positions are baked into the FBX and their pivots sit on
    /// their axle centres, so they also need no placing.
    /// </summary>
    public static void PlacePart(Transform part)
    {
        part.localPosition = Vector3.zero;
        part.localRotation = Quaternion.identity;
        part.localScale = Vector3.one;
    }

    public static void ReuseAuthoredMaterials(GameObject part, StringBuilder sb)
    {
        int swapped = 0;
        var unmatched = new List<string>();

        foreach (Renderer rend in part.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = rend.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                    continue;
                // Unity may suffix imported material names.
                string key = mats[i].name.Replace(" (Instance)", "").Trim();
                if (!AuthoredMaterials.TryGetValue(key, out string path))
                {
                    if (!unmatched.Contains(key))
                        unmatched.Add(key);
                    continue;
                }
                Material authored = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (authored == null)
                {
                    sb.AppendLine($"    WARN authored material missing: {path}");
                    continue;
                }
                mats[i] = authored;
                changed = true;
                swapped++;
            }
            if (changed)
                rend.sharedMaterials = mats;
        }

        if (swapped > 0)
            sb.AppendLine($"    reused {swapped} authored material slot(s) from ticket 130");
        if (unmatched.Count > 0)
            sb.AppendLine($"    left as imported: {string.Join(", ", unmatched)}");
    }

    public static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0)
            return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++)
            b.Encapsulate(rs[i].bounds);
        return b;
    }

    public static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
