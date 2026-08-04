using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Force-reloads Assets/For Cursor.unity from disk (discarding stale in-memory
/// scene state), then verifies BuildingInteriorVisibility + Building_Wall mats.
/// Flag: Temp/force_reload_for_cursor.flag
/// (Domain-reload hook kept live for agent scene refresh.)
/// </summary>
[InitializeOnLoad]
public static class ForceReloadForCursorScene
{
    const string ScenePath = "Assets/For Cursor.unity";
    const string FlagPath = "Temp/force_reload_for_cursor.flag";
    const string ReportPath = "Temp/force_reload_for_cursor_report.txt";
    const string WallStripMatPath = "Assets/Art/Buildings/Building_Wall.mat";

    static ForceReloadForCursorScene()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.playModeStateChanged += _ => EditorApplication.delayCall += TryRun;
    }

    [InitializeOnLoadMethod]
    static void Boot() => EditorApplication.delayCall += TryRun;

    [MenuItem("Tools/For Cursor/Force Reload For Cursor Scene")]
    public static void MenuForceReload()
    {
        string report = ForceReloadAndVerify();
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath)), report);
        EditorUtility.DisplayDialog("Force Reload", report, "OK");
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

        try
        {
            // Defer OpenScene one more editor tick after play-mode exit.
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(flag))
                    return;
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                    EditorApplication.delayCall += TryRun;
                    return;
                }

                try
                {
                    File.Delete(flag);
                    string report = ForceReloadAndVerify();
                    File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath)), report);
                    Debug.Log("[ForceReloadForCursorScene]\n" + report);
                }
                catch (System.Exception e)
                {
                    if (e is System.InvalidOperationException && e.Message.Contains("play mode"))
                    {
                        File.WriteAllText(flag, "retry\n");
                        EditorApplication.isPlaying = false;
                        EditorApplication.delayCall += TryRun;
                        return;
                    }

                    File.WriteAllText(
                        Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath)),
                        "FAIL: " + e);
                    Debug.LogError("[ForceReloadForCursorScene] " + e);
                }
            };
        }
        catch (System.Exception e)
        {
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath)),
                "FAIL: " + e);
            Debug.LogError("[ForceReloadForCursorScene] " + e);
        }
    }

    public static string ForceReloadAndVerify()
    {
        var sb = new StringBuilder();

        // Discard in-memory scene and load the on-disk asset.
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("opened=" + scene.path);
        sb.AppendLine("isLoaded=" + scene.isLoaded);

        // Ensure wiring matches the approved Change 1 spec.
        string wire = BuildingVisibilitySceneWire.Wire();
        sb.AppendLine("--- wire ---");
        sb.AppendLine(wire.TrimEnd());

        GameObject cafe = GameObject.Find("CafeRoom");
        if (cafe == null)
            throw new System.Exception("CafeRoom missing after reload");

        Transform trigger = cafe.transform.Find("InteriorTrigger");
        if (trigger == null)
            throw new System.Exception("InteriorTrigger missing after reload");

        var shop = trigger.GetComponent<ShopWallVisibility>();
        var vis = trigger.GetComponent<BuildingInteriorVisibility>();
        sb.AppendLine("hasShopWallVisibility=" + (shop != null));
        sb.AppendLine("hasBuildingInteriorVisibility=" + (vis != null));
        if (vis == null)
            throw new System.Exception("BuildingInteriorVisibility missing on InteriorTrigger");

        Material strip = AssetDatabase.LoadAssetAtPath<Material>(WallStripMatPath);
        Material stripOn = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Buildings/Building_Wall_StripOn.mat");
        Material lit = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Buildings/Building_Wall_Lit.mat");
        if (strip == null)
            throw new System.Exception("Missing " + WallStripMatPath);
        if (stripOn == null)
            throw new System.Exception("Missing Building_Wall_StripOn.mat");
        if (vis.wallStripOnMaterial == null)
            vis.wallStripOnMaterial = stripOn;

        int wallOk = 0;
        int wallBad = 0;
        foreach (Transform t in cafe.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Wall_"))
                continue;
            var r = t.GetComponent<Renderer>();
            if (r == null)
                continue;
            string matName = r.sharedMaterial != null ? r.sharedMaterial.name : "null";
            string shaderName = r.sharedMaterial != null && r.sharedMaterial.shader != null
                ? r.sharedMaterial.shader.name : "null";
            bool ok;
            if (t.name == "Wall_Back")
            {
                ok = r.sharedMaterial == lit
                     || (r.sharedMaterial != null && r.sharedMaterial.shader != null
                         && r.sharedMaterial.shader.name == "Universal Render Pipeline/Lit");
            }
            else
            {
                ok = r.sharedMaterial == strip
                     || r.sharedMaterial == stripOn
                     || (r.sharedMaterial != null && r.sharedMaterial.shader != null
                         && r.sharedMaterial.shader.name == "BeatTheMan/BuildingWallStrip");
            }
            sb.AppendLine($"wall {t.name} mat={matName} shader={shaderName} ok={ok}");
            if (ok) wallOk++; else wallBad++;
        }

        sb.AppendLine($"wallMatOk={wallOk} wallMatBad={wallBad}");
        sb.AppendLine($"stripHeight={vis.stripHeight}");
        sb.AppendLine($"always={(vis.alwaysVisibleWalls != null ? vis.alwaysVisibleWalls.Length : 0)}");
        sb.AppendLine($"cullable={(vis.cullableWalls != null ? vis.cullableWalls.Length : 0)}");
        sb.AppendLine($"roof={(vis.roofRenderers != null ? vis.roofRenderers.Length : 0)}");
        sb.AppendLine($"activeScene={SceneManager.GetActiveScene().path}");

        if (shop != null)
            Object.DestroyImmediate(shop);

        if (wallBad > 0)
            throw new System.Exception("One or more walls have wrong material (Back=Lit, others=BuildingWallStrip)");

        if (vis.alwaysVisibleWalls != null && vis.alwaysVisibleWalls.Length > 0)
            throw new System.Exception("alwaysVisibleWalls must be empty — Wall_Back is outside the strip system");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("OK");
        return sb.ToString();
    }
}
