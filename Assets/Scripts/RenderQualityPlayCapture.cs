using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Play-mode Game-view capture helper for ticket 106 SMAA follow-up.
/// Enable by creating Temp/rq_play_capture.flag then entering Play.
/// Writes shots under Temp/render_quality_shots/ and a report file.
/// </summary>
public class RenderQualityPlayCapture : MonoBehaviour
{
    const string Flag = "Temp/rq_play_capture.flag";
    const string Report = "Temp/render_quality_smaa_followup_report.txt";
    const string ShotDir = "Temp/render_quality_shots";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!File.Exists(Path.Combine(root, Flag)))
            return;
        var go = new GameObject("~RenderQualityPlayCapture");
        DontDestroyOnLoad(go);
        go.AddComponent<RenderQualityPlayCapture>();
    }

    IEnumerator Start()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        File.Delete(Path.Combine(root, Flag));
        Directory.CreateDirectory(Path.Combine(root, ShotDir));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 106 SMAA follow-up (play capture) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");

        var urp = Camera.main != null ? Camera.main.GetComponent<UniversalAdditionalCameraData>() : null;
        sb.AppendLine($"cameraAA={(urp != null ? urp.antialiasing.ToString() : "null")}");

        // Shot 1: current state (should be SMAA if scene persisted)
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.5f);
        string p1 = Path.Combine(ShotDir, "11_gameview_msaa_plus_smaa.png");
        ScreenCapture.CaptureScreenshot(p1);
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.75f);
        sb.AppendLine(Describe(root, p1));

        // Toggle AA off briefly for before comparison, then restore
        if (urp != null)
        {
            var prev = urp.antialiasing;
            urp.antialiasing = AntialiasingMode.None;
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.35f);
            string p0 = Path.Combine(ShotDir, "10_gameview_msaa_only_before_smaa.png");
            ScreenCapture.CaptureScreenshot(p0);
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.75f);
            sb.AppendLine(Describe(root, p0));

            urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            urp.antialiasingQuality = AntialiasingQuality.High;
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.35f);
            string p1b = Path.Combine(ShotDir, "11b_gameview_smaa_restored.png");
            ScreenCapture.CaptureScreenshot(p1b);
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.75f);
            sb.AppendLine(Describe(root, p1b));
        }

        var overlay = FindAnyObjectByType<GridOverlay>();
        if (overlay != null)
        {
            overlay.Visible = true;
            sb.AppendLine("GridOverlay.Visible=true");
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.5f);
            string p2 = Path.Combine(ShotDir, "12_gameview_smaa_grid_on.png");
            ScreenCapture.CaptureScreenshot(p2);
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.75f);
            sb.AppendLine(Describe(root, p2));
            sb.AppendLine("GRID NOTE: MeshTopology.Lines + Unlit — if jag is worse on grid than cubes, residual is line-AA specific.");
        }
        else
            sb.AppendLine("WARN no GridOverlay");

        sb.AppendLine("RESULT=OK play captures done — exit Play and inspect PNGs");
        File.WriteAllText(Path.Combine(root, Report), sb.ToString());
        Debug.Log("[RenderQualityPlayCapture]\n" + sb);
    }

    static string Describe(string root, string rel)
    {
        string full = Path.Combine(root, rel);
        if (!File.Exists(full))
            return $"shot={rel} MISSING";
        return $"shot={rel} bytes={new FileInfo(full).Length}";
    }
}

