using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.SceneTemplate;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 120 Step 2 — create MainCamera.prefab, convert Alpha01 / GridFoundation /
/// RoomsWorkshop cameras to instances, build GameplayBasics scene template.
/// Opens required scenes from disk (discards unsaved dirt), then saves them.
/// Flag: Temp/alpha01_120.flag
/// Report: Temp/alpha01_120/report.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_120BakeCameraGrid
{
    const string FlagPath = "Temp/alpha01_120.flag";
    const string ReportPath = "Temp/alpha01_120/report.txt";
    const string TemplateScenePath = "Assets/Scenes/Templates/GameplayBasics.unity";
    const string TemplateAssetPath = "Assets/Scenes/Templates/GameplayBasics.scenetemplate";
    static readonly Vector3 CamOffset = GameplayCameraPrefab.DefaultOffset;

    static Alpha01_120BakeCameraGrid()
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

    [MenuItem("Tools/Scenes/120 Bake Camera Prefab + Template")]
    public static void MenuRun()
    {
        File.WriteAllText(Full(FlagPath), "run\n");
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
            WriteReport(report);
            Debug.Log("[Alpha01_120BakeCameraGrid]\n" + report);
        }
        catch (Exception e)
        {
            WriteReport("FAIL: " + e);
            Debug.LogError("[Alpha01_120BakeCameraGrid] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 120 camera prefab + template bake ===");
        sb.AppendLine("time=" + DateTime.UtcNow.ToString("o"));

        GameplaySceneGuard.Suspended = true;
        string previous = SceneManager.GetActiveScene().path;
        sb.AppendLine("previous=" + previous);

        try
        {
            EnsureFolder("Assets/Prefabs", "Camera");
            EnsureFolder("Assets/Scenes", "Templates");

            GameObject prefab = EnsureCameraPrefab(sb);
            if (prefab == null)
            {
                sb.AppendLine("RESULT=FAIL");
                return sb.ToString();
            }

            ConvertSceneCamera(
                sb,
                "Assets/Scenes/GridFoundation.unity",
                followEnabled: true,
                solidColor: false,
                wirePlayerTarget: true);

            ConvertSceneCamera(
                sb,
                "Assets/Scenes/Alpha01.unity",
                followEnabled: true,
                solidColor: true,
                wirePlayerTarget: true);

            ConvertSceneCamera(
                sb,
                "Assets/Scenes/RoomsWorkshop.unity",
                followEnabled: false,
                solidColor: true,
                wirePlayerTarget: false);

            CreateTemplate(sb, prefab);

            GameplaySceneGuard.Suspended = false;
            sb.AppendLine("--- guard ---");
            AppendGuard(sb, "Assets/Scenes/GridFoundation.unity");
            AppendGuard(sb, "Assets/Scenes/Alpha01.unity");
            AppendGuard(sb, "Assets/Scenes/RoomsWorkshop.unity");
            AppendGuard(sb, TemplateScenePath);
        }
        finally
        {
            GameplaySceneGuard.Suspended = false;
            if (!string.IsNullOrEmpty(previous) && File.Exists(Full(previous)))
            {
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                sb.AppendLine("restored=" + previous);
            }
        }

        bool ok = sb.ToString().Contains("GUARD=FAIL") == false
            && sb.ToString().Contains("FAIL ") == false
            && File.Exists(Full(GameplayCameraPrefab.PrefabPath))
            && File.Exists(Full(TemplateScenePath))
            && File.Exists(Full(TemplateAssetPath));
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static GameObject EnsureCameraPrefab(StringBuilder sb)
    {
        GameObject go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        Camera cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.backgroundColor = GameplayCameraPrefab.SolidBackground;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 1000f;
        cam.depth = 0;
        cam.allowHDR = true;
        cam.allowMSAA = true;
        go.AddComponent<AudioListener>();
        UniversalAdditionalCameraData urp = go.AddComponent<UniversalAdditionalCameraData>();
        urp.renderPostProcessing = true;
        urp.renderShadows = true;
        urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        urp.antialiasingQuality = AntialiasingQuality.High;
        CameraFollow follow = go.AddComponent<CameraFollow>();
        SerializedObject cso = new SerializedObject(follow);
        cso.FindProperty("target").objectReferenceValue = null;
        cso.FindProperty("offset").vector3Value = CamOffset;
        cso.FindProperty("outdoorOrthographicSize").floatValue = 6f;
        cso.FindProperty("interiorOrthographicSize").floatValue = 4.5f;
        cso.FindProperty("roomTransitionDuration").floatValue = 0.3f;
        cso.ApplyModifiedPropertiesWithoutUndo();
        go.transform.position = CamOffset;
        go.transform.LookAt(Vector3.zero);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, GameplayCameraPrefab.PrefabPath);
        UnityEngine.Object.DestroyImmediate(go);
        if (prefab == null)
        {
            sb.AppendLine("FAIL SaveAsPrefabAsset " + GameplayCameraPrefab.PrefabPath);
            return null;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(GameplayCameraPrefab.PrefabPath);
        try
        {
            contents.name = "Main Camera";
            PrefabUtility.SaveAsPrefabAsset(contents, GameplayCameraPrefab.PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(GameplayCameraPrefab.PrefabPath);
        prefab = GameplayCameraPrefab.Load();
        UniversalAdditionalCameraData check = prefab.GetComponent<UniversalAdditionalCameraData>();
        sb.AppendLine("prefab=" + GameplayCameraPrefab.PrefabPath
            + " aa=" + check.antialiasing
            + " q=" + check.antialiasingQuality
            + " pp=" + check.renderPostProcessing
            + " follow=" + (prefab.GetComponent<CameraFollow>() != null));
        return prefab;
    }

    static void ConvertSceneCamera(
        StringBuilder sb,
        string scenePath,
        bool followEnabled,
        bool solidColor,
        bool wirePlayerTarget)
    {
        sb.AppendLine("--- " + scenePath + " ---");
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        GameObject prefab = GameplayCameraPrefab.Load();
        GameObject camGo = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;
            if (root.name == "Main Camera" || root.name == "MainCamera" || root.CompareTag("MainCamera"))
            {
                camGo = root;
                break;
            }
        }

        if (camGo == null)
        {
            sb.AppendLine("FAIL no Main Camera root");
            return;
        }

        Vector3 pos = camGo.transform.position;
        Quaternion rot = camGo.transform.rotation;
        Transform player = wirePlayerTarget ? FindPlayerTransform(scene) : null;

        string existingPrefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(camGo);
        if (existingPrefab == GameplayCameraPrefab.PrefabPath)
        {
            sb.AppendLine("already instance of MainCamera.prefab");
        }
        else
        {
            bool converted = false;
            try
            {
                var settings = new ConvertToPrefabInstanceSettings
                {
                    objectMatchMode = ObjectMatchMode.ByName,
                    recordPropertyOverridesOfMatches = true,
                    changeRootNameToAssetName = false,
                    componentsNotMatchedBecomesOverride = true,
                    logInfo = true,
                };
                PrefabUtility.ConvertToPrefabInstance(camGo, prefab, settings, InteractionMode.AutomatedAction);
                converted = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(camGo) == GameplayCameraPrefab.PrefabPath;
            }
            catch (Exception e)
            {
                sb.AppendLine("ConvertToPrefabInstance threw: " + e.Message);
            }

            if (!converted)
            {
                sb.AppendLine("fallback: destroy + InstantiatePrefab");
                UnityEngine.Object.DestroyImmediate(camGo);
                camGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                camGo.name = "Main Camera";
                camGo.transform.SetPositionAndRotation(pos, rot);
            }
            else
            {
                sb.AppendLine("converted via ConvertToPrefabInstance");
                camGo.transform.SetPositionAndRotation(pos, rot);
            }
        }

        GameplayCameraPrefab.RevertAaToPrefab(camGo);
        camGo.name = "Main Camera";
        GameplayCameraPrefab.ApplyInstanceOverrides(camGo, player, followEnabled, solidColor);

        UniversalAdditionalCameraData urp = camGo.GetComponent<UniversalAdditionalCameraData>();
        CameraFollow follow = camGo.GetComponent<CameraFollow>();
        Camera cam = camGo.GetComponent<Camera>();
        sb.AppendLine("prefabPath=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(camGo));
        sb.AppendLine("aa=" + urp.antialiasing + " q=" + urp.antialiasingQuality + " pp=" + urp.renderPostProcessing);
        sb.AppendLine("follow=" + (follow != null && follow.enabled)
            + " targetSet=" + (follow != null && SerializedTarget(follow) != null));
        sb.AppendLine("clearFlags=" + cam.clearFlags + " pos=" + camGo.transform.position);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        sb.AppendLine("saved=" + saved);
    }

    static Transform SerializedTarget(CameraFollow follow)
    {
        SerializedObject so = new SerializedObject(follow);
        return so.FindProperty("target").objectReferenceValue as Transform;
    }

    static Transform FindPlayerTransform(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != null && root.name == "Player")
                return root.transform;
        }
        return null;
    }

    static void CreateTemplate(StringBuilder sb, GameObject cameraPrefab)
    {
        sb.AppendLine("--- template ---");
        GameObject gridPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameplayCameraPrefab.GridPrefabPath);
        if (gridPrefab == null)
        {
            sb.AppendLine("FAIL missing " + GameplayCameraPrefab.GridPrefabPath);
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject cam = (GameObject)PrefabUtility.InstantiatePrefab(cameraPrefab, scene);
        cam.name = "Main Camera";

        GameObject grid = (GameObject)PrefabUtility.InstantiatePrefab(gridPrefab, scene);
        grid.name = "GridSystem";
        grid.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Ticket 106 — shared key + fill rig (template used to carry a lone key light).
        GameplayLightingRig.CreateRig(scene, GameplayLightingRig.DefaultKeyIntensity);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, TemplateScenePath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("templateScene saved=" + saved + " " + TemplateScenePath);

        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplateScenePath);
        if (sceneAsset == null)
        {
            sb.AppendLine("FAIL SceneAsset missing after save");
            return;
        }

        if (File.Exists(Full(TemplateAssetPath)))
            AssetDatabase.DeleteAsset(TemplateAssetPath);

        SceneTemplateAsset template = SceneTemplateService.CreateTemplateFromScene(sceneAsset, TemplateAssetPath);
        if (template == null)
        {
            sb.AppendLine("FAIL CreateTemplateFromScene");
            return;
        }

        template.templateName = "Gameplay Basics";
        template.description = "Beat The Man: MainCamera.prefab (SMAA High + CameraFollow) + GridSystem.prefab + directional light.";
        template.addToDefaults = true;
        ForcePrefabDependenciesReference(template, sb);
        EditorUtility.SetDirty(template);
        AssetDatabase.SaveAssets();
        sb.AppendLine("templateAsset=" + TemplateAssetPath + " addToDefaults=" + template.addToDefaults + " valid=" + template.isValid);
        PinTemplate(template, sb);
    }

    static void ForcePrefabDependenciesReference(SceneTemplateAsset template, StringBuilder sb)
    {
        SerializedObject so = new SerializedObject(template);
        SerializedProperty deps = so.FindProperty("dependencies");
        if (deps == null || !deps.isArray)
        {
            sb.AppendLine("warn: template.dependencies not found as serialized array");
            return;
        }

        for (int i = 0; i < deps.arraySize; i++)
        {
            SerializedProperty d = deps.GetArrayElementAtIndex(i);
            SerializedProperty dep = d.FindPropertyRelative("dependency");
            SerializedProperty mode = d.FindPropertyRelative("instantiationMode");
            UnityEngine.Object obj = dep != null ? dep.objectReferenceValue : null;
            string path = obj != null ? AssetDatabase.GetAssetPath(obj) : "";
            bool isOurPrefab = path == GameplayCameraPrefab.PrefabPath || path == GameplayCameraPrefab.GridPrefabPath;
            if (isOurPrefab && mode != null)
            {
                mode.intValue = (int)TemplateInstantiationMode.Reference;
                sb.AppendLine("dep reference " + path);
            }
            else
            {
                sb.AppendLine("dep " + path + " mode=" + (mode != null ? mode.intValue.ToString() : "?"));
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void PinTemplate(SceneTemplateAsset template, StringBuilder sb)
    {
        try
        {
            Type settingsType = Type.GetType("UnityEditor.SceneTemplate.SceneTemplateProjectSettings, UnityEditor.SceneTemplateModule");
            if (settingsType == null)
            {
                sb.AppendLine("warn: SceneTemplateProjectSettings type not found — addToDefaults still set");
                return;
            }

            object settings = null;
            PropertyInfo instanceProp = settingsType.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? settingsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceProp != null)
                settings = instanceProp.GetValue(null);
            if (settings == null)
            {
                MethodInfo get = settingsType.GetMethod("Get", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (get != null)
                    settings = get.Invoke(null, null);
            }
            if (settings == null)
            {
                sb.AppendLine("warn: could not get SceneTemplateProjectSettings instance");
                return;
            }

            FieldInfo overrideField = settingsType.GetField("newSceneOverride", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (overrideField != null)
            {
                sb.AppendLine("newSceneOverride before=" + overrideField.GetValue(settings));
            }

            FieldInfo pins = settingsType.GetField("templatePinStates", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            string guid = AssetDatabase.AssetPathToGUID(TemplateAssetPath);
            sb.AppendLine("templateGuid=" + guid);
            if (pins != null)
            {
                sb.AppendLine("templatePinStates type=" + pins.FieldType.FullName);
            }

            EditorUtility.SetDirty((UnityEngine.Object)settings);
            sb.AppendLine("pin: addToDefaults=1 (New Scene dialog). newSceneOverride left unchanged so we do not skip the dialog onto Unity's builtin.");
        }
        catch (Exception e)
        {
            sb.AppendLine("warn pin: " + e.Message);
        }
    }

    static void AppendGuard(StringBuilder sb, string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        sb.Append(GameplaySceneGuard.Describe(scene));
    }

    static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    static string Full(string rel)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
    }

    static void WriteReport(string text)
    {
        string full = Full(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, text ?? "");
    }
}
