using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// DefaultBuilding corner-trim verify: envelope 0–6, no wall AABB overlap,
/// doors still parented under hinges with upright local Y.
/// Flag: Temp/default_building_corner_trim.flag
/// Report: Temp/default_building_corner_trim.txt
/// Shots: Temp/corner_trim_*.png
/// </summary>
[InitializeOnLoad]
public static class DefaultBuildingCornerTrimVerify
{
    const string PrefabPath = "Assets/Prefabs/Buildings/DefaultBuilding.prefab";
    const string FbxPath = "Assets/Art/Buildings/DefaultBuilding.fbx";
    const string WallMatPath = "Assets/Art/Buildings/Building_Wall.mat";
    const string FlagPath = "Temp/default_building_corner_trim.flag";
    const string ReportPath = "Temp/default_building_corner_trim.txt";
    const string ShotDir = "Temp";

    static DefaultBuildingCornerTrimVerify()
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
            Debug.Log("[DefaultBuildingCornerTrimVerify]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(Full(ReportPath), "FAIL: " + e);
            Debug.LogError("[DefaultBuildingCornerTrimVerify] " + e);
        }
    }

    [MenuItem("Tools/Buildings/Verify Corner Trim (DefaultBuilding)")]
    public static void MenuRun()
    {
        string report = Run();
        File.WriteAllText(Full(ReportPath), report);
        EditorUtility.DisplayDialog("Corner Trim", Truncate(report, 1600), "OK");
    }

    public static void RunFromBatch()
    {
        string report = Run();
        File.WriteAllText(Full(ReportPath), report);
        Debug.Log("[DefaultBuildingCornerTrimVerify]\n" + report);
        if (!report.Contains("PASS=True"))
            EditorApplication.Exit(1);
    }

    static string Run()
    {
        var sb = new StringBuilder();
        int fails = 0;
        sb.AppendLine("=== DefaultBuilding corner trim verify ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");

        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        var wallMat = AssetDatabase.LoadAssetAtPath<Material>(WallMatPath);
        if (wallMat == null)
        {
            sb.AppendLine("FAIL: Building_Wall.mat missing");
            fails++;
        }
        else
        {
            float cull = wallMat.HasProperty("_Cull") ? wallMat.GetFloat("_Cull") : -1f;
            sb.AppendLine($"wall_cull={cull} (2=Back — must stay 2)");
            if (Mathf.Abs(cull - 2f) > 0.01f)
            {
                sb.AppendLine("FAIL: Building_Wall.mat Cull was changed");
                fails++;
            }
            else
                sb.AppendLine("PASS cull unchanged");
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            sb.AppendLine("FAIL: prefab missing " + PrefabPath);
            sb.AppendLine("PASS=False");
            return sb.ToString();
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            Transform building = instance.transform;
            // Park far above GridFoundation so the capture camera cannot see the live scene.
            building.position = new Vector3(0f, 5000f, 0f);
            Vector3 origin = building.position;
            sb.AppendLine($"prefabOrigin={origin} (bounds checked relative to this)");
            var named = IndexByName(building);

            fails += CheckNamed(sb, named, "Wall_Left", "Wall_Right", "Wall_Back",
                "Wall_Front_Left", "Wall_Front_Right", "Wall_Front_Lintel",
                "DoorHinge_Left", "DoorHinge_Right", "DoorLeaf_Left", "DoorLeaf_Right");

            Transform wallL = Find(named, "Wall_Left");
            Transform wallR = Find(named, "Wall_Right");
            Transform wallB = Find(named, "Wall_Back");
            Transform frontL = Find(named, "Wall_Front_Left");
            Transform frontR = Find(named, "Wall_Front_Right");

            if (wallL != null)
                fails += CheckAabb(sb, "Wall_Left", wallL, origin, 0f, 0.3f, 0f, 3f, 0.3f, 5.7f);
            if (wallR != null)
                fails += CheckAabb(sb, "Wall_Right", wallR, origin, 5.7f, 6f, 0f, 3f, 0.3f, 5.7f);

            Bounds? env = Encapsulate(
                wallL, wallR, wallB, frontL, frontR, Find(named, "Wall_Front_Lintel"));
            if (env == null)
            {
                sb.AppendLine("FAIL: no wall renderers for envelope");
                fails++;
            }
            else
            {
                Bounds e = env.Value;
                Vector3 min = e.min - origin;
                Vector3 max = e.max - origin;
                sb.AppendLine($"envelope xz=({min.x:F3},{max.x:F3}) ({min.z:F3},{max.z:F3}) y=({min.y:F3},{max.y:F3})");
                bool ok =
                    Approx(min.x, 0f) && Approx(max.x, 6f) &&
                    Approx(min.z, 0f) && Approx(max.z, 6f) &&
                    Approx(min.y, 0f) && Approx(max.y, 3f);
                sb.AppendLine((ok ? "PASS" : "FAIL") + " envelope 0–6 / Y 0–3");
                if (!ok) fails++;
            }

            fails += CheckNoOverlap(sb, "Wall_Left×Front_Left", wallL, frontL);
            fails += CheckNoOverlap(sb, "Wall_Left×Back", wallL, wallB);
            fails += CheckNoOverlap(sb, "Wall_Right×Front_Right", wallR, frontR);
            fails += CheckNoOverlap(sb, "Wall_Right×Back", wallR, wallB);

            Transform hingeL = Find(named, "DoorHinge_Left");
            Transform hingeR = Find(named, "DoorHinge_Right");
            Transform leafL = Find(named, "DoorLeaf_Left");
            Transform leafR = Find(named, "DoorLeaf_Right");
            fails += CheckDoorParent(sb, leafL, hingeL, "DoorLeaf_Left", "DoorHinge_Left");
            fails += CheckDoorParent(sb, leafR, hingeR, "DoorLeaf_Right", "DoorHinge_Right");
            fails += CheckHingeUpright(sb, hingeL, "DoorHinge_Left");
            fails += CheckHingeUpright(sb, hingeR, "DoorHinge_Right");

            var doorScripts = instance.GetComponentsInChildren<ShopDoorLeaf>(true);
            sb.AppendLine("shopDoorLeafCount=" + doorScripts.Length);
            if (doorScripts.Length != 2)
            {
                sb.AppendLine("FAIL: expected 2 ShopDoorLeaf on prefab");
                fails++;
            }
            else
                sb.AppendLine("PASS ShopDoorLeaf ×2 on prefab");

            Transform fbxRoot = hingeL != null ? hingeL.parent : building;
            if (fbxRoot != null)
            {
                Vector3 euler = fbxRoot.eulerAngles;
                bool ident = ApproxAngle(euler.x, 0f) && ApproxAngle(euler.y, 0f) && ApproxAngle(euler.z, 0f);
                sb.AppendLine($"fbxRoot={fbxRoot.name} euler={euler} (identity expected; 90/270 = 152-flag damage)");
                sb.AppendLine((ident ? "PASS" : "FAIL") + " fbx root identity rotation");
                if (!ident) fails++;
            }

            Directory.CreateDirectory(Full(ShotDir));
            CaptureCorners(sb, env ?? new Bounds(origin, Vector3.one));
        }
        finally
        {
            if (instance != null)
                Object.DestroyImmediate(instance);
        }

        bool pass = fails == 0;
        sb.AppendLine($"fails={fails}");
        sb.AppendLine("PASS=" + (pass ? "True" : "False"));
        return sb.ToString();
    }

    static int CheckNamed(StringBuilder sb, Dictionary<string, Transform> named, params string[] names)
    {
        int fails = 0;
        foreach (string n in names)
        {
            if (!named.ContainsKey(n))
            {
                sb.AppendLine("FAIL missing " + n);
                fails++;
            }
        }
        return fails;
    }

    static int CheckAabb(StringBuilder sb, string label, Transform t, Vector3 origin,
        float x0, float x1, float y0, float y1, float z0, float z1)
    {
        Bounds? b = RendererBounds(t);
        if (b == null)
        {
            sb.AppendLine("FAIL " + label + " no renderer bounds");
            return 1;
        }
        Vector3 min = b.Value.min - origin;
        Vector3 max = b.Value.max - origin;
        sb.AppendLine($"{label} aabb=({min.x:F3},{max.x:F3}) y=({min.y:F3},{max.y:F3}) z=({min.z:F3},{max.z:F3})");
        bool ok =
            Approx(min.x, x0) && Approx(max.x, x1) &&
            Approx(min.y, y0) && Approx(max.y, y1) &&
            Approx(min.z, z0) && Approx(max.z, z1);
        sb.AppendLine((ok ? "PASS" : "FAIL") + " " + label + " expected x " + x0 + "–" + x1 + " z " + z0 + "–" + z1);
        return ok ? 0 : 1;
    }

    static int CheckNoOverlap(StringBuilder sb, string label, Transform a, Transform b)
    {
        if (a == null || b == null)
            return 0;
        Bounds? ba = RendererBounds(a);
        Bounds? bb = RendererBounds(b);
        if (ba == null || bb == null)
            return 0;
        const float eps = 0.002f;
        Bounds A = ba.Value;
        Bounds B = bb.Value;
        bool overlap =
            A.min.x < B.max.x - eps && A.max.x > B.min.x + eps &&
            A.min.y < B.max.y - eps && A.max.y > B.min.y + eps &&
            A.min.z < B.max.z - eps && A.max.z > B.min.z + eps;
        sb.AppendLine((overlap ? "FAIL" : "PASS") + " no_overlap " + label);
        return overlap ? 1 : 0;
    }

    static int CheckDoorParent(StringBuilder sb, Transform leaf, Transform hinge, string leafName, string hingeName)
    {
        if (leaf == null || hinge == null)
            return 1;
        bool ok = leaf.parent == hinge;
        sb.AppendLine($"{leafName}.parent={leaf.parent?.name} (expect {hingeName})");
        sb.AppendLine((ok ? "PASS" : "FAIL") + " " + leafName + " parented to hinge");
        return ok ? 0 : 1;
    }

    static int CheckHingeUpright(StringBuilder sb, Transform hinge, string name)
    {
        if (hinge == null)
            return 1;
        Vector3 up = hinge.up;
        Vector3 fwd = hinge.forward;
        bool upOk = up.y > 0.98f && Mathf.Abs(up.x) < 0.05f && Mathf.Abs(up.z) < 0.05f;
        bool fwdFlat = Mathf.Abs(fwd.y) < 0.05f;
        sb.AppendLine($"{name} up={up} fwd={fwd}");
        sb.AppendLine((upOk && fwdFlat ? "PASS" : "FAIL") + " " + name + " upright (not drawbridge)");
        return upOk && fwdFlat ? 0 : 1;
    }

    static void CaptureCorners(StringBuilder sb, Bounds env)
    {
        var camGo = new GameObject("_CornerTrimCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(1f, 0f, 1f);
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 80f;
        cam.enabled = true;

        var lightGo = new GameObject("_CornerTrimLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        float y = env.min.y + env.size.y * 0.45f;
        var corners = new (string name, Vector3 focus, Vector3 offset)[]
        {
            ("fl", new Vector3(env.min.x, y, env.min.z), new Vector3(-2.2f, 1.6f, -2.2f)),
            ("fr", new Vector3(env.max.x, y, env.min.z), new Vector3(2.2f, 1.6f, -2.2f)),
            ("bl", new Vector3(env.min.x, y, env.max.z), new Vector3(-2.2f, 1.6f, 2.2f)),
            ("br", new Vector3(env.max.x, y, env.max.z), new Vector3(2.2f, 1.6f, 2.2f)),
        };

        foreach (var c in corners)
        {
            cam.transform.position = c.focus + c.offset;
            cam.transform.LookAt(c.focus + Vector3.up * 0.9f);
            string file = "corner_trim_" + c.name + ".png";
            Capture(cam, file);
            sb.AppendLine("shot=Temp/" + file);
        }

        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(lightGo);
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

    static Dictionary<string, Transform> IndexByName(Transform root)
    {
        var d = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!d.ContainsKey(t.name))
                d[t.name] = t;
        }
        return d;
    }

    static Transform Find(Dictionary<string, Transform> named, string n)
    {
        named.TryGetValue(n, out Transform t);
        return t;
    }

    static Bounds? RendererBounds(Transform t)
    {
        if (t == null)
            return null;
        var r = t.GetComponent<Renderer>();
        return r != null ? r.bounds : (Bounds?)null;
    }

    static Bounds? Encapsulate(params Transform[] ts)
    {
        Bounds? b = null;
        foreach (var t in ts)
        {
            Bounds? rb = RendererBounds(t);
            if (rb == null)
                continue;
            if (b == null) b = rb;
            else
            {
                Bounds bb = b.Value;
                bb.Encapsulate(rb.Value);
                b = bb;
            }
        }
        return b;
    }

    static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.02f;

    static bool ApproxAngle(float a, float b)
    {
        float d = Mathf.DeltaAngle(a, b);
        return Mathf.Abs(d) < 1f;
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}
