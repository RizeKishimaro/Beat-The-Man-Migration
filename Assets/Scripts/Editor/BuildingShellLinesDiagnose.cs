using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 119 diagnosis ONLY — no permanent geometry fix.
/// Flag: Temp/diagnose_building_shell_lines.flag
/// Report: Temp/building_shell_lines_diagnosis.txt
/// Shots: Temp/shell_diag_*.png
/// </summary>
[InitializeOnLoad]
public static class BuildingShellLinesDiagnose
{
    const string FlagPath = "Temp/diagnose_building_shell_lines.flag";
    const string ReportPath = "Temp/building_shell_lines_diagnosis.txt";
    const string ShotDir = "Temp";

    static BuildingShellLinesDiagnose()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRun();
    }

    static void TryRun()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        File.Delete(flag);
        try
        {
            string report = Run();
            File.WriteAllText(Full(ReportPath), report);
            Debug.Log("[BuildingShellLinesDiagnose]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(Full(ReportPath), "FAIL: " + e);
            Debug.LogError("[BuildingShellLinesDiagnose] " + e);
        }
    }

    [MenuItem("Tools/Buildings/Diagnose Shell Lines (ticket 119)")]
    public static void MenuRun()
    {
        string report = Run();
        File.WriteAllText(Full(ReportPath), report);
        EditorUtility.DisplayDialog("Shell Lines Diagnosis", Truncate(report, 1200), "OK");
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 119 shell-lines diagnosis (read-only geometry intent) ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");
        sb.AppendLine("goal=confirm cause BEFORE any visible-geometry fix");
        sb.AppendLine();

        // Prefer GridFoundation if DefaultBuilding present; else For Cursor CafeRoom.
        string scenePath = "Assets/Scenes/GridFoundation.unity";
        if (!File.Exists(Full(scenePath)))
            scenePath = "Assets/For Cursor.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        sb.AppendLine($"opened={scene.path}");

        Transform building = FindBuildingRoot();
        if (building == null)
        {
            sb.AppendLine("FAIL: no DefaultBuilding / CafeRoom / DefaultBuilding_117 found");
            return sb.ToString();
        }
        sb.AppendLine($"buildingRoot={GetPath(building)}");

        // --- Geometry coincidence map (evidence, not proof) ---
        sb.AppendLine();
        sb.AppendLine("--- coincident-face map (AABB touch with area > 0) ---");
        var walls = CollectNamed(building, "Wall_");
        var floor = FindNamed(building, "RoomFloor");
        var ceil = FindNamed(building, "RoomCeiling", "Ceiling");
        DumpCoincident(sb, walls);
        if (ceil != null)
        {
            foreach (var w in walls)
                DumpPair(sb, w.name, ceil.name, w, ceil);
        }

        Camera cam = Camera.main;
        if (cam == null)
            cam = Object.FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            sb.AppendLine("FAIL: no camera");
            return sb.ToString();
        }

        // Save camera — FrameExteriorCorner must not leave Editor Game view dirty.
        Vector3 savedCamPos = cam.transform.position;
        Quaternion savedCamRot = cam.transform.rotation;
        float savedFov = cam.fieldOfView;
        bool savedOrtho = cam.orthographic;
        float savedOrthoSize = cam.orthographicSize;

        // Frame exterior corner where the artifact was reported.
        FrameExteriorCorner(cam, building);
        sb.AppendLine($"cam.pos={cam.transform.position} cam.fwd={cam.transform.forward}");

        Directory.CreateDirectory(Full(ShotDir));

        // --- Test A: shadows ON baseline ---
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        var savedShadows = new List<(Light l, LightShadows s)>();
        foreach (var l in lights)
            savedShadows.Add((l, l.shadows));

        Capture(cam, "shell_diag_01_shadows_on.png");
        sb.AppendLine("shot=Temp/shell_diag_01_shadows_on.png (baseline, shadows ON)");

        // --- Test B: shadows OFF ---
        foreach (var l in lights)
            l.shadows = LightShadows.None;
        Capture(cam, "shell_diag_02_shadows_off.png");
        sb.AppendLine("shot=Temp/shell_diag_02_shadows_off.png (all Light.shadows=None)");
        sb.AppendLine("INTERPRET: if lines GONE with shadows off → shadow acne / shadow map; if STILL PRESENT → not shadows");

        // restore shadows for remaining tests
        foreach (var (l, s) in savedShadows)
            l.shadows = s;

        // --- Test C: temporary nudges (restored). Sub-mm may be below depth
        // precision at ~5m camera distance — also try 2mm and 10mm for certainty.
        Transform wallLeft = FindNamed(building, "Wall_Left");
        Transform wallFrontL = FindNamed(building, "Wall_Front_Left");
        if (wallLeft != null)
        {
            Vector3 savedPos = wallLeft.localPosition;

            wallLeft.localPosition = savedPos + new Vector3(0.0002f, 0f, 0f);
            Capture(cam, "shell_diag_03_nudge_wall_left_x.png");
            sb.AppendLine("shot=Temp/shell_diag_03_nudge_wall_left_x.png (Wall_Left +0.2mm local X)");

            wallLeft.localPosition = savedPos + new Vector3(0f, 0f, 0.0002f);
            Capture(cam, "shell_diag_04_nudge_wall_left_z.png");
            sb.AppendLine("shot=Temp/shell_diag_04_nudge_wall_left_z.png (Wall_Left +0.2mm local Z)");

            // Depth-buffer-scale nudges (still tiny vs 8m building; temporary).
            wallLeft.localPosition = savedPos + new Vector3(0f, 0f, 0.002f);
            Capture(cam, "shell_diag_06_nudge_wall_left_z_2mm.png");
            sb.AppendLine("shot=Temp/shell_diag_06_nudge_wall_left_z_2mm.png (Wall_Left +2mm local Z — depth-precision check)");

            wallLeft.localPosition = savedPos + new Vector3(0f, 0f, 0.01f);
            Capture(cam, "shell_diag_07_nudge_wall_left_z_10mm.png");
            sb.AppendLine("shot=Temp/shell_diag_07_nudge_wall_left_z_10mm.png (Wall_Left +10mm local Z — if lines still present, NOT that Z-plane pair)");

            wallLeft.localPosition = savedPos;
            sb.AppendLine("restored Wall_Left.localPosition (no permanent geometry change)");
        }
        else
            sb.AppendLine("WARN: Wall_Left not found — skip nudge tests");

        // --- Test D: ceiling nudge (wall-ceiling coplanar Y) ---
        if (ceil != null)
        {
            Vector3 saved = ceil.localPosition;
            ceil.localPosition = saved + new Vector3(0f, -0.0002f, 0f);
            Capture(cam, "shell_diag_05_nudge_ceiling_y.png");
            sb.AppendLine("shot=Temp/shell_diag_05_nudge_ceiling_y.png (ceiling -0.2mm local Y)");

            ceil.localPosition = saved + new Vector3(0f, -0.01f, 0f);
            Capture(cam, "shell_diag_08_nudge_ceiling_y_10mm.png");
            sb.AppendLine("shot=Temp/shell_diag_08_nudge_ceiling_y_10mm.png (ceiling -10mm local Y)");

            ceil.localPosition = saved;
            sb.AppendLine("restored RoomCeiling.localPosition");
        }

        // --- Test E: disable SSAO renderer feature if present ---
        bool ssaoWasOn = false;
        object ssaoFeature = null;
        try
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp != null)
            {
                // Reflect renderer data list
                var so = new SerializedObject(urp);
                var prop = so.FindProperty("m_RendererDataList");
                if (prop != null && prop.arraySize > 0)
                {
                    var rd = prop.GetArrayElementAtIndex(0).objectReferenceValue
                        as UnityEngine.Rendering.Universal.ScriptableRendererData;
                    if (rd != null)
                    {
                        foreach (var f in rd.rendererFeatures)
                        {
                            if (f != null && f.GetType().Name.Contains("ScreenSpaceAmbientOcclusion"))
                            {
                                ssaoFeature = f;
                                ssaoWasOn = f.isActive;
                                f.SetActive(false);
                                sb.AppendLine($"SSAO feature found wasActive={ssaoWasOn} → forced OFF for shot");
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            sb.AppendLine("SSAO probe error: " + e.Message);
        }
        Capture(cam, "shell_diag_09_ssao_off.png");
        sb.AppendLine("shot=Temp/shell_diag_09_ssao_off.png (SSAO off if feature existed)");
        sb.AppendLine("INTERPRET: if lines GONE → SSAO depth-edge bleed, not mesh Z-fight");
        if (ssaoFeature is UnityEngine.Rendering.Universal.ScriptableRendererFeature feat)
            feat.SetActive(ssaoWasOn);

        // --- Test F: swap Wall_Left to URP Lit temporarily ---
        var leftR = wallLeft != null ? wallLeft.GetComponent<Renderer>() : null;
        Material savedMat = null;
        if (leftR != null)
        {
            savedMat = leftR.sharedMaterial;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var tmp = new Material(lit);
                tmp.color = new Color(0.72f, 0.70f, 0.66f, 1f);
                leftR.sharedMaterial = tmp;
                Capture(cam, "shell_diag_10_wall_left_urp_lit.png");
                sb.AppendLine("shot=Temp/shell_diag_10_wall_left_urp_lit.png (Wall_Left only → URP Lit)");
                sb.AppendLine("INTERPRET: if lines GONE on left wall → BuildingWallStrip shader; if STILL THERE → geometry/depth");
                leftR.sharedMaterial = savedMat;
                Object.DestroyImmediate(tmp);
                sb.AppendLine("restored Wall_Left material");
            }
            else
                sb.AppendLine("WARN: URP Lit shader not found");
        }

        // --- visibility state ---
        var vis = Object.FindAnyObjectByType<BuildingInteriorVisibility>();
        if (vis != null)
        {
            sb.AppendLine($"visibility.inside={vis.IsInside} stripHeight={vis.stripHeight}");
            sb.AppendLine(vis.DebugDumpStripState());
        }
        else
            sb.AppendLine("WARN: no BuildingInteriorVisibility in scene");

        // --- mesh source freshness ---
        sb.AppendLine();
        sb.AppendLine("--- mesh / prefab freshness ---");
        string fbx = "Assets/Art/Buildings/DefaultBuilding.fbx";
        if (File.Exists(Full(fbx)))
        {
            var fi = new FileInfo(Full(fbx));
            sb.AppendLine($"fbx.mtime={fi.LastWriteTimeUtc:o} size={fi.Length}");
        }
        foreach (var mf in building.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf == null || mf.sharedMesh == null)
                continue;
            if (!mf.name.StartsWith("Wall_") && mf.name != "RoomCeiling" && mf.name != "RoomFloor")
                continue;
            Mesh m = mf.sharedMesh;
            sb.AppendLine(
                $"mesh {mf.name} verts={m.vertexCount} bounds={m.bounds.size} " +
                $"asset={AssetDatabase.GetAssetPath(m)}");
        }

        sb.AppendLine();
        sb.AppendLine("sceneDirty=" + scene.isDirty + " (should be false if nudges restored)");

        cam.transform.position = savedCamPos;
        cam.transform.rotation = savedCamRot;
        cam.fieldOfView = savedFov;
        cam.orthographic = savedOrtho;
        cam.orthographicSize = savedOrthoSize;
        sb.AppendLine("restored Camera.main transform/fov (FrameExteriorCorner cleanup)");

        sb.AppendLine("RESULT=DIAGNOSIS_CAPTURED — compare Temp/shell_diag_*.png; no geometry fix applied");
        return sb.ToString();
    }

    static Transform FindBuildingRoot()
    {
        foreach (string n in new[] { "DefaultBuilding_117", "DefaultBuilding", "CafeRoom" })
        {
            var go = GameObject.Find(n);
            if (go != null)
                return go.transform;
        }
        return null;
    }

    static List<Transform> CollectNamed(Transform root, string prefix)
    {
        var list = new List<Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith(prefix))
                list.Add(t);
        }
        return list;
    }

    static Transform FindNamed(Transform root, params string[] names)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            foreach (string n in names)
            {
                if (t.name == n)
                    return t;
            }
        }
        return null;
    }

    static void DumpCoincident(StringBuilder sb, List<Transform> walls)
    {
        for (int i = 0; i < walls.Count; i++)
        {
            for (int j = i + 1; j < walls.Count; j++)
                DumpPair(sb, walls[i].name, walls[j].name, walls[i], walls[j]);
        }
    }

    static void DumpPair(StringBuilder sb, string aName, string bName, Transform a, Transform b)
    {
        Bounds? ab = RendererBounds(a);
        Bounds? bb = RendererBounds(b);
        if (!ab.HasValue || !bb.HasValue)
            return;
        Bounds A = ab.Value;
        Bounds B = bb.Value;

        // Shared face area on each axis if planes nearly touch
        float areaX = FaceAreaIfTouch(A.min.x, A.max.x, B.min.x, B.max.x, A.min.y, A.max.y, B.min.y, B.max.y, A.min.z, A.max.z, B.min.z, B.max.z);
        float areaY = FaceAreaIfTouch(A.min.y, A.max.y, B.min.y, B.max.y, A.min.x, A.max.x, B.min.x, B.max.x, A.min.z, A.max.z, B.min.z, B.max.z);
        float areaZ = FaceAreaIfTouch(A.min.z, A.max.z, B.min.z, B.max.z, A.min.x, A.max.x, B.min.x, B.max.x, A.min.y, A.max.y, B.min.y, B.max.y);

        if (areaX > 1e-4f || areaY > 1e-4f || areaZ > 1e-4f)
        {
            sb.AppendLine(
                $"COINCIDENT {aName} x {bName} " +
                $"areaX={areaX:F4} areaY={areaY:F4} areaZ={areaZ:F4}");
        }
    }

    // If primary extents touch at a plane, return secondary*tertiary overlap area.
    static float FaceAreaIfTouch(
        float a0, float a1, float b0, float b1,
        float s0a, float s1a, float s0b, float s1b,
        float t0a, float t1a, float t0b, float t1b)
    {
        const float eps = 0.002f;
        bool touch = Mathf.Abs(a1 - b0) <= eps || Mathf.Abs(b1 - a0) <= eps
                     || (a0 <= b0 + eps && a1 >= b1 - eps && Mathf.Abs(a0 - b0) <= eps && Mathf.Abs(a1 - b1) <= eps);
        // Also: identical planes when both max==min of other
        touch = Mathf.Abs(a1 - b0) <= eps || Mathf.Abs(b1 - a0) <= eps;
        if (!touch)
            return 0f;
        float s = Mathf.Max(0f, Mathf.Min(s1a, s1b) - Mathf.Max(s0a, s0b));
        float t = Mathf.Max(0f, Mathf.Min(t1a, t1b) - Mathf.Max(t0a, t0b));
        return s * t;
    }

    static Bounds? RendererBounds(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        if (r != null)
            return r.bounds;
        var mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Bounds lb = mf.sharedMesh.bounds;
            Vector3 c = t.TransformPoint(lb.center);
            Vector3 s = Vector3.Scale(lb.size, t.lossyScale);
            return new Bounds(c, s);
        }
        return null;
    }

    static void FrameExteriorCorner(Camera cam, Transform building)
    {
        Bounds? b = null;
        foreach (var r in building.GetComponentsInChildren<Renderer>())
        {
            if (b == null) b = r.bounds;
            else
            {
                Bounds bb = b.Value;
                bb.Encapsulate(r.bounds);
                b = bb;
            }
        }
        if (b == null)
            return;
        Bounds bounds = b.Value;
        // Outside near min-X / min-Z corner (typical Front-Left exterior).
        Vector3 focus = new Vector3(bounds.min.x, bounds.center.y * 0.6f, bounds.min.z);
        cam.transform.position = focus + new Vector3(-2.2f, 1.8f, -2.2f);
        cam.transform.LookAt(focus + Vector3.up * 0.8f);
    }

    static void Capture(Camera cam, string fileName)
    {
        string path = Full(Path.Combine(ShotDir, fileName));
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
        var parts = new List<string>();
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}
