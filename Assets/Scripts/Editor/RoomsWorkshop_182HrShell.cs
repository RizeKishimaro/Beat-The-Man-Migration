using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 182 — empty HR shell in RoomsWorkshop (https://trello.com/c/V3yFGhbo).
/// Deletes the 178 cube comparison shell, places a clean DefaultBuilding
/// instance named Room_HR at slot origin. No furniture. Does not save
/// Alpha01 or GridFoundation.
/// Flag: Temp/rooms_workshop_182.flag
/// Report: Temp/rooms_workshop_182/setup.txt
/// Shot: Temp/rooms_workshop_182/hr_shell.png
/// </summary>
[InitializeOnLoad]
public static class RoomsWorkshop_182HrShell
{
    public const string ScenePath = RoomsWorkshopSceneSetup.ScenePath;
    public const string PrefabPath = "Assets/Prefabs/Buildings/DefaultBuilding.prefab";
    public const string HrName = "Room_HR";
    public const string StaleCubeName = RoomsWorkshopSceneSetup.V2Name;
    public const string StaleRefName = "DefaultBuilding_v1_ref";

    static readonly Vector3 CamOffset = new Vector3(-8f, 7f, -8f);
    static readonly Vector3 CamTarget = new Vector3(3f, 0f, 3f);

    const string FlagPath = "Temp/rooms_workshop_182.flag";
    const string ReportPath = "Temp/rooms_workshop_182/setup.txt";
    const string ShotPath = "Temp/rooms_workshop_182/hr_shell.png";
    const string Alpha01Path = "Assets/Scenes/Alpha01.unity";
    const string PlaygroundPath = "Assets/Scenes/GridFoundation.unity";

    static RoomsWorkshop_182HrShell()
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

    [MenuItem("Tools/Scenes/182 RoomsWorkshop HR Shell")]
    public static void MenuSetup()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("182 HR Shell", Truncate(report, 1600), "OK");
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
            Debug.Log("[RoomsWorkshop_182HrShell]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[RoomsWorkshop_182HrShell] " + e);
        }
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 182 RoomsWorkshop HR shell ===");
        sb.AppendLine("time=" + System.DateTime.UtcNow.ToString("o"));
        sb.AppendLine("does not save Alpha01 or GridFoundation");
        sb.AppendLine("no furniture");

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            sb.AppendLine("FAIL: editor is in Play mode");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            sb.AppendLine("FAIL missing " + PrefabPath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

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
            DestroyNamed(workshop, StaleCubeName, sb);
            DestroyNamed(workshop, StaleRefName, sb);
            DestroyNamed(workshop, HrName, sb);

            GameObject hr = (GameObject)PrefabUtility.InstantiatePrefab(prefab, workshop);
            hr.name = HrName;
            hr.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            sb.AppendLine(HrName + ": DefaultBuilding.prefab at " + Vector3.zero);

            GameObject camGo = FindRoot(workshop, "Main Camera");
            if (camGo == null)
                camGo = FindRoot(workshop, "MainCamera");
            Camera cam = camGo != null ? camGo.GetComponent<Camera>() : null;
            if (camGo != null)
            {
                camGo.transform.position = CamTarget + CamOffset;
                camGo.transform.LookAt(CamTarget);
                PrefabUtility.RecordPrefabInstancePropertyModifications(camGo);
                sb.AppendLine("Main Camera lookAt " + CamTarget + " pos=" + camGo.transform.position);
            }
            else
                sb.AppendLine("warn: Main Camera not found — left as-is");

            EditorSceneManager.MarkSceneDirty(workshop);
            bool saved = EditorSceneManager.SaveScene(workshop, ScenePath);
            AssetDatabase.SaveAssets();
            sb.AppendLine("saved=" + saved + " " + ScenePath);

            AppendBounds(sb, HrName, hr);
            if (cam != null)
            {
                ScreenshotIsolated(cam, workshop, Full(ShotPath), 1920, 1080);
                sb.AppendLine("shot: " + ShotPath);
            }

            bool cubeGone = FindRoot(workshop, StaleCubeName) == null;
            bool refGone = FindRoot(workshop, StaleRefName) == null;
            bool hrOk = FindRoot(workshop, HrName) != null;
            sb.AppendLine("cubeGone=" + cubeGone + " v1RefGone=" + refGone + " hrPresent=" + hrOk);

            bool ok = saved && cubeGone && refGone && hrOk;
            sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        }
        finally
        {
            // Reload from disk so the Editor matches the written scene, then
            // restore whatever was active (Alpha01 stays put).
            if (workshop.IsValid() && workshop.isLoaded)
                EditorSceneManager.CloseScene(workshop, true);
            Scene reloaded = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (previousPath == ScenePath || wasActive)
                SceneManager.SetActiveScene(reloaded);
            else
            {
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(reloaded, true);
            }
        }

        Scene after = SceneManager.GetActiveScene();
        sb.AppendLine("active after: '" + after.path + "' dirty=" + after.isDirty);
        if (after.path == previousPath && after.isDirty != previousDirtyBefore
            && after.path != ScenePath)
            sb.AppendLine("warn: previous scene dirty flag changed");
        if (after.path == Alpha01Path || after.path == PlaygroundPath)
            sb.AppendLine("left " + after.path + " as active");
        return sb.ToString();
    }

    static void DestroyNamed(Scene scene, string name, StringBuilder sb)
    {
        GameObject go = FindRoot(scene, name);
        if (go == null)
            return;
        Object.DestroyImmediate(go);
        sb.AppendLine("deleted " + name);
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root != null && root.name == name)
                return root;
        return null;
    }

    static void AppendBounds(StringBuilder sb, string label, GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0)
        {
            sb.AppendLine(label + ": no renderers");
            return;
        }
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs)
            if (r.enabled && r.gameObject.activeInHierarchy)
                b.Encapsulate(r.bounds);
        sb.AppendLine(label + ": x[" + b.min.x.ToString("F2") + "," + b.max.x.ToString("F2")
            + "] z[" + b.min.z.ToString("F2") + "," + b.max.z.ToString("F2")
            + "] y[" + b.min.y.ToString("F2") + "," + b.max.y.ToString("F2")
            + "] -> " + b.size.x.ToString("F2") + " x " + b.size.z.ToString("F2")
            + " wall-to-wall, " + b.size.y.ToString("F2") + " tall");
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
