using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 184 — apply OfficeCarpet to RoomsWorkshop Room_HR / RoomFloor
/// as a scene instance override. Does not save Alpha01 or GridFoundation.
/// Does not modify DefaultBuilding.prefab or Room_HR.prefab.
/// Flag: Temp/office_carpet_184.flag
/// Report: Temp/office_carpet_184/apply.txt
/// Shot: Temp/office_carpet_184/hr_floor.png
/// </summary>
[InitializeOnLoad]
public static class OfficeCarpet_184
{
    public const string GraphPath = "Assets/Art/Buildings/OfficeCarpet.shadergraph";
    public const string HlslPath = "Assets/Art/Buildings/OfficeCarpet.hlsl";
    public const string MatPath = "Assets/Art/Buildings/OfficeCarpet_HR.mat";
    public const string ScenePath = RoomsWorkshopSceneSetup.ScenePath;
    public const string RoomName = "Room_HR";
    public const string FloorName = "RoomFloor";

    const string FlagPath = "Temp/office_carpet_184.flag";
    const string ReportPath = "Temp/office_carpet_184/apply.txt";
    const string ShotPath = "Temp/office_carpet_184/hr_floor.png";
    const string Alpha01Path = "Assets/Scenes/Alpha01.unity";
    const string PlaygroundPath = "Assets/Scenes/GridFoundation.unity";
    const string DefaultBuildingPath = "Assets/Prefabs/Buildings/DefaultBuilding.prefab";
    const string RoomHrPrefabPath = "Assets/Prefabs/Buildings/Room_HR.prefab";

    static readonly Vector3 CamOffset = new Vector3(-8f, 7f, -8f);
    static readonly Vector3 CamTarget = new Vector3(3f, 0.2f, 3f);

    static OfficeCarpet_184()
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

    [MenuItem("Tools/Art/184 Apply Office Carpet To HR Floor")]
    public static void MenuApply()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("184 Office Carpet", Truncate(report, 1600), "OK");
    }

    static void TryRun()
    {
        if (!File.Exists(Full(FlagPath)))
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
            File.Delete(Full(FlagPath));
            string report = Run();
            WriteReport(report);
            Debug.Log("[OfficeCarpet_184]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[OfficeCarpet_184] " + e);
        }
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 184 OfficeCarpet on RoomsWorkshop RoomFloor ===");
        sb.AppendLine("time=" + System.DateTime.UtcNow.ToString("o"));
        sb.AppendLine("scene instance override only — not DefaultBuilding.prefab, not Room_HR.prefab");

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            sb.AppendLine("FAIL: editor is in Play mode");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        AssetDatabase.ImportAsset(HlslPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath);
        if (shader == null)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            shader = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath);
        }
        if (shader == null)
        {
            sb.AppendLine("FAIL: shadergraph did not import a Shader at " + GraphPath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }
        sb.AppendLine("shader=" + shader.name + " supported=" + shader.isSupported);

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            mat.name = "OfficeCarpet_HR";
            AssetDatabase.CreateAsset(mat, MatPath);
            mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            sb.AppendLine("created " + MatPath);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
            sb.AppendLine("reassigned shader on existing material");
        }

        ApplyDefaults(mat);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        sb.AppendLine("material=" + mat.name + " shader=" + (mat.shader != null ? mat.shader.name : "null"));

        string dbFloor = PrefabFloorMatName(DefaultBuildingPath);
        string hrFloor = PrefabFloorMatName(RoomHrPrefabPath);
        sb.AppendLine("DefaultBuilding.prefab RoomFloor mat before=" + dbFloor);
        sb.AppendLine("Room_HR.prefab RoomFloor mat before=" + hrFloor);

        Scene previous = SceneManager.GetActiveScene();
        bool previousDirtyBefore = previous.isDirty;
        string previousPath = previous.path;
        sb.AppendLine("active before: '" + previousPath + "' dirty=" + previousDirtyBefore);

        Scene workshop = SceneManager.GetSceneByPath(ScenePath);
        bool wasLoaded = workshop.IsValid() && workshop.isLoaded;
        bool wasActive = wasLoaded && previous.path == ScenePath;
        if (!wasLoaded)
        {
            if (!File.Exists(Full(ScenePath)))
            {
                sb.AppendLine("FAIL missing " + ScenePath);
                sb.AppendLine("RESULT=FAIL");
                return sb.ToString();
            }
            workshop = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }

        SceneManager.SetActiveScene(workshop);
        try
        {
            GameObject hr = FindRoot(workshop, RoomName);
            if (hr == null)
            {
                sb.AppendLine("FAIL: " + RoomName + " not in " + ScenePath);
                sb.AppendLine("RESULT=FAIL");
                return sb.ToString();
            }

            Transform floor = FindNamed(hr.transform, FloorName);
            if (floor == null)
            {
                sb.AppendLine("FAIL: " + FloorName + " not under " + RoomName);
                sb.AppendLine("RESULT=FAIL");
                return sb.ToString();
            }

            Renderer rend = floor.GetComponent<Renderer>();
            if (rend == null)
            {
                sb.AppendLine("FAIL: " + FloorName + " has no Renderer");
                sb.AppendLine("RESULT=FAIL");
                return sb.ToString();
            }

            string before = rend.sharedMaterial != null ? rend.sharedMaterial.name : "null";
            rend.sharedMaterial = mat;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rend);
            EditorSceneManager.MarkSceneDirty(workshop);
            string after = rend.sharedMaterial != null ? rend.sharedMaterial.name : "null";
            sb.AppendLine(RoomName + "/" + FloorName + " mat " + before + " -> " + after);
            sb.AppendLine("path=" + GetPath(floor));

            GameObject camGo = FindRoot(workshop, "Main Camera");
            if (camGo == null)
                camGo = FindRoot(workshop, "MainCamera");
            Camera cam = camGo != null ? camGo.GetComponent<Camera>() : null;
            if (camGo != null)
            {
                camGo.transform.position = CamTarget + CamOffset;
                camGo.transform.LookAt(CamTarget);
            }

            bool saved = EditorSceneManager.SaveScene(workshop, ScenePath);
            AssetDatabase.SaveAssets();
            sb.AppendLine("saved=" + saved + " " + ScenePath);

            if (cam != null)
            {
                ScreenshotIsolated(cam, workshop, Full(ShotPath), 1920, 1080);
                sb.AppendLine("shot: " + ShotPath);
            }
            else
                sb.AppendLine("warn: no camera for screenshot");

            string dbAfter = PrefabFloorMatName(DefaultBuildingPath);
            string hrAfter = PrefabFloorMatName(RoomHrPrefabPath);
            sb.AppendLine("DefaultBuilding.prefab RoomFloor mat after=" + dbAfter);
            sb.AppendLine("Room_HR.prefab RoomFloor mat after=" + hrAfter);

            GameObject worldFloor = FindRoot(workshop, "WorldFloor");
            string wfMat = "missing";
            if (worldFloor != null)
            {
                Renderer wr = worldFloor.GetComponent<Renderer>();
                wfMat = wr != null && wr.sharedMaterial != null ? wr.sharedMaterial.name : "null";
            }
            sb.AppendLine("WorldFloor mat=" + wfMat);

            PropertyModification[] mods = PrefabUtility.GetPropertyModifications(hr);
            int floorMatMods = 0;
            if (mods != null)
            {
                for (int i = 0; i < mods.Length; i++)
                {
                    if (mods[i] != null && mods[i].propertyPath != null
                        && mods[i].propertyPath.Contains("m_Materials"))
                        floorMatMods++;
                }
            }
            sb.AppendLine("Room_HR instance material property mods=" + floorMatMods);

            bool ok = saved
                && after == "OfficeCarpet_HR"
                && dbAfter == dbFloor
                && hrAfter == hrFloor
                && wfMat != "OfficeCarpet_HR"
                && shader.isSupported;
            if (dbAfter != dbFloor)
                sb.AppendLine("FAIL: DefaultBuilding.prefab floor material changed");
            if (hrAfter != hrFloor)
                sb.AppendLine("FAIL: Room_HR.prefab floor material changed");
            sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        }
        finally
        {
            if (!wasLoaded && workshop.IsValid() && workshop.isLoaded)
            {
                EditorSceneManager.CloseScene(workshop, true);
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
            }
            else if (!wasActive && previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
        }

        Scene afterScene = SceneManager.GetActiveScene();
        sb.AppendLine("active after: '" + afterScene.path + "' dirty=" + afterScene.isDirty);
        if (afterScene.path == previousPath && afterScene.isDirty != previousDirtyBefore
            && afterScene.path != ScenePath)
            sb.AppendLine("warn: previous scene dirty flag changed");
        if (afterScene.path == Alpha01Path || afterScene.path == PlaygroundPath)
            sb.AppendLine("left " + afterScene.path + " as active");
        return sb.ToString();
    }

    static void ApplyDefaults(Material mat)
    {
        if (mat.HasProperty("_CarpetBaseColor"))
            mat.SetColor("_CarpetBaseColor", new Color(0.145f, 0.176f, 0.255f, 1f));
        if (mat.HasProperty("_CarpetPatchColor"))
            mat.SetColor("_CarpetPatchColor", new Color(0.220f, 0.255f, 0.310f, 1f));
        if (mat.HasProperty("_PatchContrast"))
            mat.SetFloat("_PatchContrast", 0.22f);
        if (mat.HasProperty("_PatchScale"))
            mat.SetFloat("_PatchScale", 0.13f);
        if (mat.HasProperty("_FibreAmount"))
            mat.SetFloat("_FibreAmount", 0.14f);
        if (mat.HasProperty("_FibreScale"))
            mat.SetFloat("_FibreScale", 43f);
        if (mat.HasProperty("_SeamVisibility"))
            mat.SetFloat("_SeamVisibility", 0.055f);
        if (mat.HasProperty("_SeamTileSize"))
            mat.SetFloat("_SeamTileSize", 0.50f);
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.08f);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0f);
    }

    static string PrefabFloorMatName(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return "missing-prefab";
        Transform floor = FindNamed(prefab.transform, FloorName);
        if (floor == null)
            return "missing-RoomFloor";
        Renderer r = floor.GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null)
            return "null";
        return r.sharedMaterial.name;
    }

    static Transform FindNamed(Transform t, string name)
    {
        if (t.name == name)
            return t;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform f = FindNamed(t.GetChild(i), name);
            if (f != null)
                return f;
        }
        return null;
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root != null && root.name == name)
                return root;
        return null;
    }

    static string GetPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            p = t.name + "/" + p;
        }
        return p;
    }

    static void ScreenshotIsolated(Camera cam, Scene keep, string file, int w, int h)
    {
        var hidden = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (!s.IsValid() || !s.isLoaded || s == keep)
                continue;
            foreach (GameObject root in s.GetRootGameObjects())
            {
                if (root != null && root.activeSelf)
                {
                    root.SetActive(false);
                    hidden.Add(root);
                }
            }
        }

        var rt = new RenderTexture(w, h, 24);
        RenderTexture prevTarget = cam.targetTexture;
        RenderTexture prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
            for (int i = 0; i < hidden.Count; i++)
            {
                if (hidden[i] != null)
                    hidden[i].SetActive(true);
            }
        }
    }

    static void WriteReport(string report)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, report ?? "");
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max) + "\n…";
    }
}
