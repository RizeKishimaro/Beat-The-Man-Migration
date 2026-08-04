using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires BuildingInteriorVisibility on CafeRoom: Wall_Back uses Lit (outside strip
/// system); cullable fronts/sides use BuildingWallStrip; roof hide-when-inside.
/// Flag: Temp/wire_building_visibility.flag
/// </summary>
[InitializeOnLoad]
public static class BuildingVisibilitySceneWire
{
    const string ScenePath = "Assets/For Cursor.unity";
    const string WallLitMatPath = "Assets/Art/Buildings/Building_Wall_Lit.mat";
    const string WallStripMatPath = "Assets/Art/Buildings/Building_Wall.mat";
    const string WallStripOnMatPath = "Assets/Art/Buildings/Building_Wall_StripOn.mat";
    const string RoofMatPath = "Assets/Art/Buildings/Building_Roof.mat";
    const string FlagPath = "Temp/wire_building_visibility.flag";
    const string VerifyPath = "Temp/agent_verify_building_visibility.txt";

    static BuildingVisibilitySceneWire()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.playModeStateChanged += _ => EditorApplication.delayCall += TryRun;
    }

    [InitializeOnLoadMethod]
    static void Boot() => EditorApplication.delayCall += TryRun;

    static void TryRun()
    {
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += TryRun;
            return;
        }

        try
        {
            File.Delete(flag);
            string report = Wire();
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
            Debug.Log("[BuildingVisibilitySceneWire]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)),
                "FAIL: " + e);
            Debug.LogError("[BuildingVisibilitySceneWire] " + e);
        }
    }

    [MenuItem("Tools/For Cursor/Wire Building Interior Visibility")]
    public static void MenuWire()
    {
        string report = Wire();
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
        EditorUtility.DisplayDialog("Building Visibility", report, "OK");
    }

    public static string Wire()
    {
        var sb = new System.Text.StringBuilder();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Material wallLit = AssetDatabase.LoadAssetAtPath<Material>(WallLitMatPath);
        Material wallStrip = AssetDatabase.LoadAssetAtPath<Material>(WallStripMatPath);
        Material wallStripOn = AssetDatabase.LoadAssetAtPath<Material>(WallStripOnMatPath);
        Material roofMat = AssetDatabase.LoadAssetAtPath<Material>(RoofMatPath);
        if (wallLit == null)
            throw new System.Exception("Missing " + WallLitMatPath);
        if (wallStripOn == null)
            throw new System.Exception("Missing " + WallStripOnMatPath);
        if (roofMat == null)
            throw new System.Exception("Missing " + RoofMatPath);

        GameObject cafe = GameObject.Find("CafeRoom");
        if (cafe == null)
            throw new System.Exception("CafeRoom not found in scene");

        Transform triggerT = FindDeep(cafe.transform, "InteriorTrigger");
        if (triggerT == null)
            throw new System.Exception("InteriorTrigger missing");

        // Remove legacy all-or-nothing hide.
        var legacy = triggerT.GetComponent("ShopWallVisibility") as Component;
        if (legacy != null)
        {
            Object.DestroyImmediate(legacy);
            sb.AppendLine("removed ShopWallVisibility");
        }

        var vis = triggerT.GetComponent<BuildingInteriorVisibility>();
        if (vis == null)
            vis = triggerT.gameObject.AddComponent<BuildingInteriorVisibility>();

        var always = new List<Renderer>();
        var cullable = new List<Renderer>();
        var roofs = new List<Renderer>();
        var floors = new List<Renderer>();
        Renderer wallBack = null;

        // Wall_Back: Lit only, never in strip arrays / MPB. Front + sides cullable.
        foreach (var t in cafe.GetComponentsInChildren<Transform>(true))
        {
            var r = t.GetComponent<Renderer>();
            if (r == null)
                continue;
            string n = t.name;
            if (n == "Wall_Back")
                wallBack = r;
            else if (n.StartsWith("Wall_Front") || n == "Wall_Left" || n == "Wall_Right")
                cullable.Add(r);
            else if (n == "RoomCeiling")
                roofs.Add(r);
            else if (n == "RoomFloor")
                floors.Add(r);
            // Door* meshes intentionally skipped — keep their own materials.
        }

        vis.interiorVolume = triggerT.GetComponent<Collider>();
        vis.alwaysVisibleWalls = always.ToArray(); // intentionally empty
        vis.cullableWalls = cullable.ToArray();
        vis.roofRenderers = roofs.ToArray();
        vis.floorRenderers = floors.ToArray();
        vis.wallLitMaterial = wallLit;
        vis.wallStripMaterial = wallStrip;
        vis.wallStripOnMaterial = wallStripOn;
        vis.stripHeight = 0.3f;

        if (wallBack != null)
        {
            wallBack.sharedMaterial = wallLit;
            wallBack.SetPropertyBlock(null);
            wallBack.enabled = true;
        }
        foreach (var r in cullable)
            r.sharedMaterial = wallStrip != null ? wallStrip : wallLit;
        foreach (var r in roofs)
            r.sharedMaterial = roofMat;

        // Ensure door meshes were not assigned the wall material.
        int doorOk = 0;
        foreach (var t in cafe.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Door") && t.name != "CafeDoor")
                continue;
            var r = t.GetComponent<Renderer>();
            if (r == null)
                continue;
            if (r.sharedMaterial == wallLit || r.sharedMaterial == wallStrip)
                throw new System.Exception("Door mesh incorrectly assigned Building wall mat: " + t.name);
            doorOk++;
        }

        try
        {
            vis.EditorForceRefresh();
        }
        catch (System.Exception refreshEx)
        {
            sb.AppendLine("EditorForceRefresh warning: " + refreshEx.Message);
        }

        sb.AppendLine($"always={always.Count} cullable={cullable.Count} roof={roofs.Count} floor={floors.Count} doorsChecked={doorOk}");
        sb.AppendLine("alwaysNames=" + Names(always) + " (Wall_Back excluded — uses Lit)");
        sb.AppendLine("wallBackMat=" + (wallBack != null && wallBack.sharedMaterial != null ? wallBack.sharedMaterial.name : "null"));
        sb.AppendLine("cullableNames=" + Names(cullable));
        sb.AppendLine("stripHeight=" + vis.stripHeight);
        sb.AppendLine("wallLitShader=" + (wallLit.shader != null ? wallLit.shader.name : "null"));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("OK");
        return sb.ToString();
    }

    static string Names(List<Renderer> list)
    {
        var parts = new List<string>();
        foreach (var r in list)
            if (r != null)
                parts.Add(r.name);
        return string.Join(",", parts);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, name);
            if (found != null)
                return found;
        }
        return null;
    }
}
