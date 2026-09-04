using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Diagnose-only: Game-view (Camera.main) capture of live DefaultBuilding_117
/// plus BuildingInteriorVisibility / wall-ceiling AABB notes.
/// Does not save the scene. Restores Camera.main.
/// Flag: Temp/db117_gameview_capture.flag
/// Report: Temp/db117_gameview_capture.txt
/// Shots: Temp/db117_game_corner.png, Temp/db117_game_wallceil.png
/// </summary>
[InitializeOnLoad]
public static class DefaultBuilding117GameViewCapture
{
    const string FlagPath = "Temp/db117_gameview_capture.flag";
    const string ReportPath = "Temp/db117_gameview_capture.txt";
    const string ShotCorner = "Temp/db117_game_corner.png";
    const string ShotWallCeil = "Temp/db117_game_wallceil.png";

    static DefaultBuilding117GameViewCapture()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling)
            return;
        TryRun();
    }

    static void TryRun()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling)
            return;
        File.Delete(flag);
        try
        {
            string report = Run();
            File.WriteAllText(Full(ReportPath), report);
            Debug.Log("[DefaultBuilding117GameViewCapture]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(Full(ReportPath), "FAIL: " + e);
            Debug.LogError("[DefaultBuilding117GameViewCapture] " + e);
        }
    }

    [MenuItem("Tools/Buildings/Capture DefaultBuilding_117 Game View (diagnose)")]
    public static void MenuRun()
    {
        string report = Run();
        File.WriteAllText(Full(ReportPath), report);
        EditorUtility.DisplayDialog("DB117 Game View", Truncate(report, 1400), "OK");
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DefaultBuilding_117 Game-view diagnose (no fixes) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");
        sb.AppendLine($"activeScene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}");
        sb.AppendLine($"isPlaying={EditorApplication.isPlaying}");

        Transform building = FindBuilding();
        if (building == null)
        {
            sb.AppendLine("FAIL: DefaultBuilding_117 / DefaultBuilding not in loaded scenes");
            return sb.ToString();
        }
        sb.AppendLine($"building={GetPath(building)} pos={building.position}");

        var vis = building.GetComponentInChildren<BuildingInteriorVisibility>(true);
        if (vis == null)
            sb.AppendLine("BIV=MISSING");
        else
        {
            sb.AppendLine($"BIV.gameObject={GetPath(vis.transform)}");
            sb.AppendLine($"BIV.enabled={vis.enabled} BIV.behaviourEnabled={vis.isActiveAndEnabled}");
            sb.AppendLine($"BIV.IsInside={vis.IsInside}");
            sb.AppendLine(vis.DebugDumpStripState());
        }

        DumpWall(sb, building, "Wall_Left");
        DumpWall(sb, building, "Wall_Front_Left");
        DumpWall(sb, building, "Wall_Back");
        DumpWall(sb, building, "RoomCeiling");
        DumpWall(sb, building, "RoomFloor");

        Transform wallL = FindNamed(building, "Wall_Left");
        Transform frontL = FindNamed(building, "Wall_Front_Left");
        Transform ceil = FindNamed(building, "RoomCeiling");
        DumpOverlap(sb, "Wall_Left×Front_Left (corner column)", wallL, frontL);
        DumpOverlap(sb, "Wall_Left×RoomCeiling (wall-ceiling)", wallL, ceil);
        DumpOverlap(sb, "Wall_Front_Left×RoomCeiling (wall-ceiling)", frontL, ceil);

        Camera cam = Camera.main;
        if (cam == null)
            cam = Object.FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            sb.AppendLine("FAIL: no camera");
            return sb.ToString();
        }
        sb.AppendLine($"camera={cam.name} tag={cam.tag}");

        Vector3 savedPos = cam.transform.position;
        Quaternion savedRot = cam.transform.rotation;
        float savedFov = cam.fieldOfView;
        bool savedOrtho = cam.orthographic;
        float savedOrthoSize = cam.orthographicSize;

        Bounds env = Encapsulate(building);
        // Original card angle: outside min-X / min-Z (front-left) corner.
        Vector3 corner = new Vector3(env.min.x, env.min.y + env.size.y * 0.45f, env.min.z);
        cam.transform.position = corner + new Vector3(-2.2f, 1.8f, -2.2f);
        cam.transform.LookAt(corner + Vector3.up * 0.9f);
        Capture(cam, ShotCorner);
        sb.AppendLine("shot=" + ShotCorner + " (Game-view Camera.main, outside front-left corner)");

        // Same corner, raised, looking at wall-top / ceiling meeting line.
        Vector3 top = new Vector3(env.min.x, env.max.y, env.min.z);
        cam.transform.position = top + new Vector3(-1.6f, 1.1f, -1.6f);
        cam.transform.LookAt(top);
        Capture(cam, ShotWallCeil);
        sb.AppendLine("shot=" + ShotWallCeil + " (raised, wall-ceiling at same corner)");

        cam.transform.position = savedPos;
        cam.transform.rotation = savedRot;
        cam.fieldOfView = savedFov;
        cam.orthographic = savedOrtho;
        cam.orthographicSize = savedOrthoSize;
        sb.AppendLine("restored Camera.main (scene not saved)");
        return sb.ToString();
    }

    static void DumpWall(StringBuilder sb, Transform root, string name)
    {
        Transform t = FindNamed(root, name);
        if (t == null)
        {
            sb.AppendLine($"missing {name}");
            return;
        }
        var r = t.GetComponent<Renderer>();
        if (r == null)
        {
            sb.AppendLine($"{name} no renderer");
            return;
        }
        Bounds b = r.bounds;
        string mat = r.sharedMaterial != null ? r.sharedMaterial.name : "null";
        float strip = -1f;
        bool kw = false;
        if (r.sharedMaterial != null)
        {
            if (r.sharedMaterial.HasProperty("_StripEnabled"))
                strip = r.sharedMaterial.GetFloat("_StripEnabled");
            kw = r.sharedMaterial.IsKeywordEnabled("_STRIP_ON");
        }
        sb.AppendLine(
            $"{name} enabled={r.enabled} mat={mat} strip={strip} keyword_STRIP_ON={kw} " +
            $"aabb=({b.min.x:F3},{b.max.x:F3}) y=({b.min.y:F3},{b.max.y:F3}) z=({b.min.z:F3},{b.max.z:F3})");
    }

    static void DumpOverlap(StringBuilder sb, string label, Transform a, Transform b)
    {
        if (a == null || b == null)
            return;
        var ra = a.GetComponent<Renderer>();
        var rb = b.GetComponent<Renderer>();
        if (ra == null || rb == null)
            return;
        Bounds A = ra.bounds;
        Bounds B = rb.bounds;
        const float eps = 0.002f;
        bool vol =
            A.min.x < B.max.x - eps && A.max.x > B.min.x + eps &&
            A.min.y < B.max.y - eps && A.max.y > B.min.y + eps &&
            A.min.z < B.max.z - eps && A.max.z > B.min.z + eps;
        bool touchY =
            Mathf.Abs(A.max.y - B.min.y) <= 0.02f || Mathf.Abs(B.max.y - A.min.y) <= 0.02f;
        sb.AppendLine($"{label}: volumeOverlap={vol} yPlanesTouch={touchY} " +
                      $"A.y=({A.min.y:F3},{A.max.y:F3}) B.y=({B.min.y:F3},{B.max.y:F3})");
    }

    static Transform FindBuilding()
    {
        foreach (string n in new[] { "DefaultBuilding_117", "DefaultBuilding" })
        {
            var go = GameObject.Find(n);
            if (go != null)
                return go.transform;
        }
        return null;
    }

    static Transform FindNamed(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }
        return null;
    }

    static Bounds Encapsulate(Transform root)
    {
        Bounds? b = null;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (b == null) b = r.bounds;
            else
            {
                Bounds bb = b.Value;
                bb.Encapsulate(r.bounds);
                b = bb;
            }
        }
        return b ?? new Bounds(root.position, Vector3.one);
    }

    static void Capture(Camera cam, string relative)
    {
        string path = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        int w = 1280;
        int h = 720;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = prev;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }

    static string GetPath(Transform t)
    {
        var sb = new StringBuilder();
        while (t != null)
        {
            if (sb.Length > 0)
                sb.Insert(0, "/");
            sb.Insert(0, t.name);
            t = t.parent;
        }
        return sb.ToString();
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}
