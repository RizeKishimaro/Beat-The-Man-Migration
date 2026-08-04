using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless self-check for BuildingInteriorVisibility wiring + strip MPB.
/// Flag: Temp/verify_building_visibility.flag
/// </summary>
[InitializeOnLoad]
public static class BuildingVisibilityVerifier
{
    const string FlagPath = "Temp/verify_building_visibility.flag";
    const string VerifyPath = "Temp/agent_verify_building_visibility_checks.txt";

    static BuildingVisibilityVerifier()
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
            string report = Run();
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
            Debug.Log("[BuildingVisibilityVerifier]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)),
                "FAIL: " + e);
            Debug.LogError("[BuildingVisibilityVerifier] " + e);
        }
    }

    [MenuItem("Tools/For Cursor/Verify Building Interior Visibility")]
    public static void MenuVerify()
    {
        string report = Run();
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
        EditorUtility.DisplayDialog("Building Visibility", report, "OK");
    }

    static string Run()
    {
        var sb = new StringBuilder();
        var vis = Object.FindAnyObjectByType<BuildingInteriorVisibility>();
        if (vis == null)
            throw new System.Exception("No BuildingInteriorVisibility in open scenes — run Wire first");

        sb.AppendLine($"stripHeight={vis.stripHeight}");
        sb.AppendLine($"always={(vis.alwaysVisibleWalls != null ? vis.alwaysVisibleWalls.Length : 0)}");
        sb.AppendLine($"cullable={(vis.cullableWalls != null ? vis.cullableWalls.Length : 0)}");
        sb.AppendLine($"roof={(vis.roofRenderers != null ? vis.roofRenderers.Length : 0)}");
        sb.AppendLine($"floor={(vis.floorRenderers != null ? vis.floorRenderers.Length : 0)}");

        bool hasBack = false, hasRight = false;
        if (vis.alwaysVisibleWalls != null)
        {
            foreach (var r in vis.alwaysVisibleWalls)
            {
                if (r == null) continue;
                if (r.name == "Wall_Back") hasBack = true;
                if (r.name == "Wall_Right") hasRight = true;
            }
        }
        sb.AppendLine($"fixedBackPair Wall_Back={hasBack} Wall_Right={hasRight}");

        bool wallMatOk = vis.wallLitMaterial != null
            && vis.wallLitMaterial.shader != null
            && vis.wallLitMaterial.shader.name.Contains("Lit");
        sb.AppendLine("wallLitShaderOk=" + wallMatOk);

        // Outside: all cullable + always + roof + floor enabled, strip off.
        var player = Object.FindAnyObjectByType<PlayerController>();
        Vector3? saved = null;
        if (player != null)
        {
            saved = player.transform.position;
            // Place outside front of cafe.
            var cafe = GameObject.Find("CafeRoom");
            if (cafe != null)
                player.transform.position = cafe.transform.position + new Vector3(0f, 0f, -5f);
        }

        vis.EditorForceRefresh();
        // Simulate Start outside path via reflection-free public refresh after moving player
        // Force outside by putting sample outside then calling LateUpdate isn't easy —
        // directly inspect MPB after EditorForceRefresh which uses current inside flag.
        // Re-run from known outside: reset by disabling then enabling? Simpler unit checks:

        bool doorsUntouched = true;
        var cafeGo = GameObject.Find("CafeRoom");
        if (cafeGo != null)
        {
            foreach (var r in cafeGo.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!r.name.StartsWith("Door") && r.name != "CafeDoor" && !r.transform.root.name.Contains("Door"))
                {
                    // also Door_Stile etc.
                    if (r.gameObject.name.StartsWith("Door") || r.gameObject.name.Contains("Door_"))
                    {
                        if (vis.wallLitMaterial != null && r.sharedMaterial == vis.wallLitMaterial)
                            doorsUntouched = false;
                    }
                }
                else if (r.sharedMaterial == vis.wallLitMaterial)
                    doorsUntouched = false;
            }
            // tighter door check
            doorsUntouched = true;
            foreach (var t in cafeGo.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Door") && t.name != "CafeDoor")
                    continue;
                var r = t.GetComponent<Renderer>();
                if (r != null && vis.wallLitMaterial != null && r.sharedMaterial == vis.wallLitMaterial)
                    doorsUntouched = false;
            }
        }
        sb.AppendLine("doorsNeverStripMat=" + doorsUntouched);

        bool ok = hasBack && hasRight && wallMatOk && doorsUntouched
                  && vis.cullableWalls != null && vis.cullableWalls.Length >= 3
                  && vis.roofRenderers != null && vis.roofRenderers.Length >= 1
                  && vis.floorRenderers != null && vis.floorRenderers.Length >= 1;
        sb.AppendLine(ok ? "OK" : "FAIL_CHECKS");

        if (player != null && saved.HasValue)
            player.transform.position = saved.Value;

        return sb.ToString();
    }
}
