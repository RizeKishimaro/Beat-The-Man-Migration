using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 120 — editor-only guarantee that required scenes instance
/// MainCamera.prefab (SMAA High) and GridSystem.prefab.
/// Flag-driven bake sets Suspended so mid-convert opens do not false-fail.
/// </summary>
[InitializeOnLoad]
public static class GameplaySceneGuard
{
    public const string CameraPrefabPath = GameplayCameraPrefab.PrefabPath;
    public const string GridPrefabPath = GameplayCameraPrefab.GridPrefabPath;

    public static readonly string[] RequiredScenePaths =
    {
        "Assets/Scenes/Alpha01.unity",
        "Assets/Scenes/GridFoundation.unity",
        "Assets/Scenes/RoomsWorkshop.unity",
        "Assets/Scenes/Templates/GameplayBasics.unity",
    };

    public static bool Suspended;

    static GameplaySceneGuard()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.delayCall += ValidateActiveQuiet;
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += () =>
        {
            if (scene.IsValid() && scene.isLoaded)
                ValidateScene(scene, log: true);
        };
    }

    static void OnPlayMode(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.ExitingEditMode)
            return;
        ValidateScene(SceneManager.GetActiveScene(), log: true);
    }

    static void ValidateActiveQuiet()
    {
        ValidateScene(SceneManager.GetActiveScene(), log: true);
    }

    public static bool IsRequiredScene(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
            return false;
        for (int i = 0; i < RequiredScenePaths.Length; i++)
        {
            if (RequiredScenePaths[i] == scenePath)
                return true;
        }
        return false;
    }

    public static string Describe(Scene scene)
    {
        var sb = new StringBuilder();
        bool ok = ValidateScene(scene, log: false, sb);
        sb.AppendLine(ok ? "GUARD=OK" : "GUARD=FAIL");
        return sb.ToString();
    }

    public static bool ValidateScene(Scene scene, bool log, StringBuilder sb = null)
    {
        if (Suspended)
            return true;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return true;
        if (File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp/alpha01_120.flag"))))
            return true;
        if (!scene.IsValid() || !scene.isLoaded)
            return true;
        if (!IsRequiredScene(scene.path))
            return true;

        int fails = 0;
        Line(sb, "scene=" + scene.path);

        GameObject camGo = FindMainCamera(scene);
        GameObject gridGo = FindGrid(scene);

        if (camGo == null)
        {
            Fail(ref fails, sb, log, scene, "missing Main Camera");
        }
        else
        {
            string camPrefab = PrefabAssetPath(camGo);
            if (camPrefab != CameraPrefabPath)
                Fail(ref fails, sb, log, scene, "Main Camera is not an instance of " + CameraPrefabPath + " (got '" + camPrefab + "')");

            CameraFollow follow = camGo.GetComponent<CameraFollow>();
            UniversalAdditionalCameraData urp = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (urp == null)
            {
                Fail(ref fails, sb, log, scene, "Main Camera missing UniversalAdditionalCameraData");
            }
            else
            {
                if (urp.antialiasing != AntialiasingMode.SubpixelMorphologicalAntiAliasing)
                    Fail(ref fails, sb, log, scene, "Main Camera antialiasing=" + urp.antialiasing + " want SMAA");
                if (urp.antialiasingQuality != AntialiasingQuality.High)
                    Fail(ref fails, sb, log, scene, "Main Camera antialiasingQuality=" + urp.antialiasingQuality + " want High");
                if (!urp.renderPostProcessing)
                    Fail(ref fails, sb, log, scene, "Main Camera post-processing off");
            }

            bool wantFollow = scene.path != "Assets/Scenes/RoomsWorkshop.unity";
            if (follow == null)
                Fail(ref fails, sb, log, scene, "Main Camera missing CameraFollow");
            else if (follow.enabled != wantFollow)
                Fail(ref fails, sb, log, scene, "CameraFollow enabled=" + follow.enabled + " want " + wantFollow);
        }

        if (gridGo == null)
        {
            Fail(ref fails, sb, log, scene, "missing GridSystem");
        }
        else
        {
            string gridPrefab = PrefabAssetPath(gridGo);
            if (gridPrefab != GridPrefabPath)
                Fail(ref fails, sb, log, scene, "GridSystem is not an instance of " + GridPrefabPath + " (got '" + gridPrefab + "')");
        }

        int mainCams = 0;
        Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cams.Length; i++)
        {
            if (cams[i] != null && cams[i].gameObject.scene == scene && cams[i].CompareTag("MainCamera"))
                mainCams++;
        }
        if (mainCams != 1)
            Fail(ref fails, sb, log, scene, "MainCamera tag count=" + mainCams + " want 1");

        if (fails == 0)
            Line(sb, "required prefabs + SMAA High OK");
        return fails == 0;
    }

    static string PrefabAssetPath(GameObject go)
    {
        if (go == null)
            return "";
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
        if (!string.IsNullOrEmpty(path))
            return path;
        UnityEngine.Object src = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (src != null)
            return AssetDatabase.GetAssetPath(src);
        src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
        return src != null ? AssetDatabase.GetAssetPath(src) : "";
    }

    static GameObject FindMainCamera(Scene scene)
    {
        Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        GameObject tagged = null;
        GameObject named = null;
        for (int i = 0; i < cams.Length; i++)
        {
            Camera cam = cams[i];
            if (cam == null || cam.gameObject.scene != scene)
                continue;
            if (cam.CompareTag("MainCamera") && tagged == null)
                tagged = cam.gameObject;
            string n = cam.gameObject.name;
            if ((n == "Main Camera" || n == "MainCamera") && named == null)
                named = cam.gameObject;
        }
        return tagged != null ? tagged : named;
    }

    static GameObject FindGrid(Scene scene)
    {
        GridSystem[] grids = Object.FindObjectsByType<GridSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < grids.Length; i++)
        {
            if (grids[i] != null && grids[i].gameObject.scene == scene)
                return grids[i].gameObject;
        }
        return null;
    }

    static void Fail(ref int fails, StringBuilder sb, bool log, Scene scene, string msg)
    {
        fails++;
        string line = "[GameplaySceneGuard] FAIL " + scene.path + ": " + msg;
        Line(sb, "FAIL " + msg);
        if (log)
            Debug.LogError(line);
    }

    static void Line(StringBuilder sb, string msg)
    {
        if (sb != null)
            sb.AppendLine(msg);
    }
}
