using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Ticket 160 — side-by-side Game View of old spike chair vs new CreamChair_Raw153
/// at smoothness candidates. Does not write 149 materials. Does not save the scene.
/// Flag: Temp/bake160_gloss_compare.flag
/// Report: Temp/bake160_gloss_compare.txt
/// Shots: Temp/bake160/*.png
/// </summary>
[InitializeOnLoad]
public static class Bake160GlossCompare
{
    const string OldFbx = "Assets/Objects/Meshy_Chair_Unity/Meshy_Chair_Unity.fbx";
    const string NewFbx =
        "Assets/Objects/Pickable/CreamChair_Raw153/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture.fbx";
    const string OldMatPath =
        "Assets/Objects/Meshy_Chair_Unity/Materials/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture.mat";
    const string NewMatPath = "Assets/Objects/Pickable/CreamChair_Raw153/CreamChair_Raw153.mat";
    const string FlagPath = "Temp/bake160_gloss_compare.flag";
    const string ReportPath = "Temp/bake160_gloss_compare.txt";
    const string ShotDir = "Temp/bake160";
    const float OriginY = 8000f;

    static Bake160GlossCompare()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRun();
    }

    [MenuItem("Tools/Objects/Capture 160 Gloss Comparison")]
    public static void MenuCapture()
    {
        string report = Run();
        File.WriteAllText(Full(ReportPath), report);
        EditorUtility.DisplayDialog("Ticket 160 gloss compare", Truncate(report, 1600), "OK");
    }

    static void TryRun()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += TryRun;
            return;
        }
        if (EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += TryRun;
            return;
        }

        try
        {
            File.Delete(flag);
            string report = Run();
            File.WriteAllText(Full(ReportPath), report);
            Debug.Log("[Bake160GlossCompare]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(Full(ReportPath), "ERROR " + e);
            Debug.LogError("[Bake160GlossCompare] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 160 cream vs spike gloss compare ===");
        sb.AppendLine("time=" + System.DateTime.UtcNow.ToString("o"));
        sb.AppendLine("LEFT = old spike chair (no map, _Smoothness=0.5). RIGHT = new cream.");
        sb.AppendLine("Materials cloned in-memory — disk .mat files not written.");

        GameObject oldSrc = AssetDatabase.LoadAssetAtPath<GameObject>(OldFbx);
        GameObject newSrc = AssetDatabase.LoadAssetAtPath<GameObject>(NewFbx);
        Material oldMat = AssetDatabase.LoadAssetAtPath<Material>(OldMatPath);
        Material newMat = AssetDatabase.LoadAssetAtPath<Material>(NewMatPath);
        if (oldSrc == null || newSrc == null || oldMat == null || newMat == null)
        {
            sb.AppendLine("FAIL missing fbx/mat");
            sb.AppendLine("oldFbx=" + (oldSrc != null) + " newFbx=" + (newSrc != null)
                + " oldMat=" + (oldMat != null) + " newMat=" + (newMat != null));
            return sb.ToString();
        }

        DumpMat(sb, "SPIKE", oldMat);
        DumpMat(sb, "CREAM_149", newMat);

        Directory.CreateDirectory(Full(ShotDir));
        var temps = new List<Object>();
        try
        {
            GameObject oldGo = Object.Instantiate(oldSrc);
            oldGo.name = "Bake160_spike";
            oldGo.hideFlags = HideFlags.HideAndDontSave;
            temps.Add(oldGo);
            PlaceAndFit(oldGo, new Vector3(-0.85f, OriginY, 0f));

            GameObject newGo = Object.Instantiate(newSrc);
            newGo.name = "Bake160_cream";
            newGo.hideFlags = HideFlags.HideAndDontSave;
            temps.Add(newGo);
            PlaceAndFit(newGo, new Vector3(0.85f, OriginY, 0f));

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Bake160_floor";
            floor.hideFlags = HideFlags.HideAndDontSave;
            floor.transform.position = new Vector3(0f, OriginY, 0f);
            floor.transform.localScale = new Vector3(0.45f, 1f, 0.45f);
            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.SetColor("_BaseColor", new Color(0.22f, 0.22f, 0.24f, 1f));
            floorMat.SetFloat("_Smoothness", 0.1f);
            floorMat.SetFloat("_Metallic", 0f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            temps.Add(floor);
            temps.Add(floorMat);

            var lightGo = new GameObject("Bake160_key");
            lightGo.hideFlags = HideFlags.HideAndDontSave;
            Light key = lightGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.35f;
            key.color = Color.white;
            key.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(38f, 42f, 0f);
            temps.Add(lightGo);

            var fillGo = new GameObject("Bake160_fill");
            fillGo.hideFlags = HideFlags.HideAndDontSave;
            Light fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.35f;
            fill.color = new Color(0.75f, 0.82f, 1f, 1f);
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(20f, -50f, 0f);
            temps.Add(fillGo);

            var camGo = new GameObject("Bake160_cam");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            cam.allowHDR = true;
            var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;
            camGo.transform.position = new Vector3(0f, OriginY + 1.05f, -2.55f);
            camGo.transform.LookAt(new Vector3(0f, OriginY + 0.48f, 0.05f));
            temps.Add(camGo);

            CaptureSolo(cam, oldGo, newGo, "00_original_spike_only", sb);
            CapturePair(cam, oldGo, newGo, newMat, "00_mul_0.0", m => ApplyMultiplier(m, 0f), sb);
            CapturePair(cam, oldGo, newGo, newMat, "01_current_mul_1.0", ApplyCurrent, sb);
            CapturePair(cam, oldGo, newGo, newMat, "02_mul_0.5", m => ApplyMultiplier(m, 0.5f), sb);
            CapturePair(cam, oldGo, newGo, newMat, "03_mul_0.6", m => ApplyMultiplier(m, 0.6f), sb);
            CapturePair(cam, oldGo, newGo, newMat, "04_mul_0.7", m => ApplyMultiplier(m, 0.7f), sb);
            CapturePair(cam, oldGo, newGo, newMat, "05_mul_0.8", m => ApplyMultiplier(m, 0.8f), sb);
            CapturePair(cam, oldGo, newGo, newMat, "06_flat_0.5_nomap", ApplyFlatSpikeMatch, sb);
            CapturePair(cam, oldGo, newGo, newMat, "07_cream_wears_spike_mat", m => { }, sb, assignSpikeMat: oldMat);

            StitchContact(sb);
            sb.AppendLine("RESULT=OK");
            return sb.ToString();
        }
        finally
        {
            for (int i = 0; i < temps.Count; i++)
            {
                if (temps[i] != null)
                    Object.DestroyImmediate(temps[i]);
            }
        }
    }

    static void DumpMat(StringBuilder sb, string label, Material mat)
    {
        sb.AppendLine("--- " + label + " ---");
        sb.AppendLine("shader=" + mat.shader.name);
        sb.AppendLine("keywords=" + string.Join(",", mat.shaderKeywords));
        sb.AppendLine("_MetallicGlossMap=" + TexName(mat.GetTexture("_MetallicGlossMap")));
        sb.AppendLine("_Metallic=" + mat.GetFloat("_Metallic"));
        sb.AppendLine("_Smoothness=" + mat.GetFloat("_Smoothness"));
        sb.AppendLine("_EmissionMap=" + TexName(mat.GetTexture("_EmissionMap")));
        sb.AppendLine("_EmissionColor=" + mat.GetColor("_EmissionColor"));
        sb.AppendLine("keyword _EMISSION=" + mat.IsKeywordEnabled("_EMISSION"));
        sb.AppendLine("keyword _METALLICSPECGLOSSMAP=" + mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"));
    }

    static void CaptureSolo(Camera cam, GameObject oldGo, GameObject newGo, string name, StringBuilder sb)
    {
        bool wasActive = newGo.activeSelf;
        Vector3 savedPos = cam.transform.position;
        Quaternion savedRot = cam.transform.rotation;
        try
        {
            newGo.SetActive(false);
            cam.transform.position = new Vector3(-0.85f, OriginY + 1.0f, -2.2f);
            cam.transform.LookAt(new Vector3(-0.85f, OriginY + 0.48f, 0f));
            string rel = ShotDir + "/" + name + ".png";
            Capture(cam, rel);
            sb.AppendLine("shot=" + rel + " ORIGINAL spike only (flat _Smoothness=0.5, no map)");
        }
        finally
        {
            newGo.SetActive(wasActive);
            cam.transform.position = savedPos;
            cam.transform.rotation = savedRot;
        }
    }

    static void CapturePair(
        Camera cam,
        GameObject oldGo,
        GameObject newGo,
        Material creamSrc,
        string name,
        System.Action<Material> setup,
        StringBuilder sb,
        Material assignSpikeMat = null)
    {
        Material inst = new Material(creamSrc);
        inst.name = "Bake160_" + name;
        try
        {
            setup(inst);
            AssignMat(newGo, assignSpikeMat != null ? assignSpikeMat : inst);
            string rel = ShotDir + "/" + name + ".png";
            Capture(cam, rel);
            sb.AppendLine("shot=" + rel
                + " creamSmooth=" + inst.GetFloat("_Smoothness")
                + " mask=" + TexName(inst.GetTexture("_MetallicGlossMap"))
                + " metalKw=" + inst.IsKeywordEnabled("_METALLICSPECGLOSSMAP")
                + " emissionKw=" + inst.IsKeywordEnabled("_EMISSION")
                + (assignSpikeMat != null ? " ASSIGNED_SPIKE_MAT" : ""));
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }
    }

    static void ApplyCurrent(Material m)
    {
        // Leave 149 values (mask + smoothness 1 + emission).
    }

    static void ApplyMultiplier(Material m, float mul)
    {
        if (m.HasProperty("_Smoothness"))
            m.SetFloat("_Smoothness", mul);
    }

    static void ApplyFlatSpikeMatch(Material m)
    {
        if (m.HasProperty("_MetallicGlossMap"))
            m.SetTexture("_MetallicGlossMap", null);
        m.DisableKeyword("_METALLICSPECGLOSSMAP");
        if (m.HasProperty("_Metallic"))
            m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("_Smoothness"))
            m.SetFloat("_Smoothness", 0.5f);
        if (m.HasProperty("_EmissionMap"))
            m.SetTexture("_EmissionMap", null);
        if (m.HasProperty("_EmissionColor"))
            m.SetColor("_EmissionColor", Color.black);
        m.DisableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
    }

    static void AssignMat(GameObject root, Material mat)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
            rends[i].sharedMaterial = mat;
    }

    static void PlaceAndFit(GameObject go, Vector3 origin)
    {
        go.transform.position = origin;
        go.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
        go.transform.localScale = Vector3.one;
        Bounds? b = CollectBounds(go);
        if (!b.HasValue)
            return;
        float h = Mathf.Max(0.01f, b.Value.size.y);
        float s = 1.05f / h;
        go.transform.localScale = Vector3.one * s;
        b = CollectBounds(go);
        if (!b.HasValue)
            return;
        Vector3 p = go.transform.position;
        p.y += origin.y - b.Value.min.y;
        p.x += origin.x - b.Value.center.x;
        p.z += origin.z - b.Value.center.z;
        go.transform.position = p;
    }

    static Bounds? CollectBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
            return null;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
        {
            if (rends[i] != null)
                b.Encapsulate(rends[i].bounds);
        }
        return b;
    }

    static void Capture(Camera cam, string relative)
    {
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
        File.WriteAllBytes(Full(relative), tex.EncodeToPNG());
        cam.targetTexture = prev;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }

    static void StitchContact(StringBuilder sb)
    {
        string[] names =
        {
            "02_mul_0.5", "03_mul_0.6",
            "04_mul_0.7", "05_mul_0.8",
            "01_current_mul_1.0", "06_flat_0.5_nomap"
        };
        Texture2D[] tiles = new Texture2D[names.Length];
        int tw = 0;
        int th = 0;
        for (int i = 0; i < names.Length; i++)
        {
            string path = Full(ShotDir + "/" + names[i] + ".png");
            if (!File.Exists(path))
                return;
            var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
            t.LoadImage(File.ReadAllBytes(path), false);
            tiles[i] = t;
            tw = t.width;
            th = t.height;
        }

        int cols = 2;
        int rows = 3;
        var sheet = new Texture2D(tw * cols, th * rows, TextureFormat.RGB24, false);
        for (int i = 0; i < names.Length; i++)
        {
            int col = i % cols;
            int row = rows - 1 - (i / cols);
            sheet.SetPixels(col * tw, row * th, tw, th, tiles[i].GetPixels());
        }
        sheet.Apply();
        string outRel = ShotDir + "/contact_sheet.png";
        File.WriteAllBytes(Full(outRel), sheet.EncodeToPNG());
        sb.AppendLine("shot=" + outRel + " (2x3: 0.5,0.6 / 0.7,0.8 / current1.0, flat-nomap)");
        Object.DestroyImmediate(sheet);
        for (int i = 0; i < tiles.Length; i++)
            Object.DestroyImmediate(tiles[i]);
    }

    static string TexName(Texture t) => t != null ? t.name : "(null)";

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}
