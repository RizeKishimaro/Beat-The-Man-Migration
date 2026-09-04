using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 164 — create Assets/Scenes/Alpha01.unity with playground camera /
/// player / grid parity. Does not open or save GridFoundation.
/// Flag: Temp/alpha01_setup.flag
/// Report: Temp/alpha01/setup.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01SceneSetup
{
    public const string ScenePath = "Assets/Scenes/Alpha01.unity";
    const string GridPrefabPath = "Assets/Prefabs/Grid/GridSystem.prefab";
    const string CharacterPath = "Assets/Art/Characters/Character_Base.fbx";
    const string FlagPath = "Temp/alpha01_setup.flag";
    const string ReportPath = "Temp/alpha01/setup.txt";

    static readonly Vector3 PlayerSpawn = new Vector3(-0.58f, 0f, -0.5f);
    static readonly Vector3 CamOffset = new Vector3(-8f, 7f, -8f);
    static readonly Vector3 SeatPos = new Vector3(1.5f, 0f, -0.5f);

    static Alpha01SceneSetup()
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

    [MenuItem("Tools/Scenes/164 Setup Alpha01")]
    public static void MenuSetup()
    {
        string report = RunSetup();
        WriteReport(report);
        EditorUtility.DisplayDialog("Alpha01 Setup", Truncate(report, 1600), "OK");
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
            string report = RunSetup();
            WriteReport(report);
            Debug.Log("[Alpha01SceneSetup]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[Alpha01SceneSetup] " + e);
        }
    }

    public static string RunSetup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 164 Alpha01 scene setup ===");
        sb.AppendLine("does not save GridFoundation");

        GameObject gridPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GridPrefabPath);
        GameObject character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
        if (gridPrefab == null)
        {
            sb.AppendLine("FAIL missing " + GridPrefabPath);
            return sb.ToString();
        }
        if (character == null)
        {
            sb.AppendLine("FAIL missing " + CharacterPath);
            return sb.ToString();
        }

        // If playground is dirty in memory, discard without saving it, then create Alpha01.
        Scene current = SceneManager.GetActiveScene();
        if (current.path == "Assets/Scenes/GridFoundation.unity" && current.isDirty)
        {
            EditorSceneManager.OpenScene(current.path, OpenSceneMode.Single);
            sb.AppendLine("discarded unsaved GridFoundation dirt (did not save playground)");
        }

        // New empty scene — do not SaveScene on whatever was open (playground).
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "Alpha01";

        GameObject gridInst = (GameObject)PrefabUtility.InstantiatePrefab(gridPrefab, scene);
        gridInst.name = "GridSystem";
        gridInst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        GridSystem gs = gridInst.GetComponent<GridSystem>();
        gs.originWorld = Vector3.zero;
        gs.cellSize = 1f;
        sb.AppendLine("GridSystem prefab origin=0 cellSize=1 overlay=" + (gridInst.GetComponent<GridOverlay>() != null));

        GameObject player = new GameObject("Player");
        SceneManager.MoveGameObjectToScene(player, scene);
        player.transform.SetPositionAndRotation(PlayerSpawn, Quaternion.identity);
        CharacterController cc = player.AddComponent<CharacterController>();
        cc.height = 1.56f;
        cc.radius = 0.28f;
        cc.center = new Vector3(0f, 0.78f, 0f);
        cc.skinWidth = 0.08f;
        cc.stepOffset = 0.3f;
        PlayerController pc = player.AddComponent<PlayerController>();
        var pso = new SerializedObject(pc);
        pso.FindProperty("characterPrefab").objectReferenceValue = character;
        pso.FindProperty("hidePlaceholderVisual").boolValue = true;
        pso.FindProperty("moveSpeed").floatValue = 5.5f;
        pso.FindProperty("turnSpeed").floatValue = 720f;
        pso.ApplyModifiedPropertiesWithoutUndo();
        player.AddComponent<PlayerJumpOnSeat>();
        player.AddComponent<ProceduralSeatJumpMotion>();
        sb.AppendLine("Player Character_Base assigned spawn=" + PlayerSpawn);

        GameObject camGo = new GameObject("Main Camera");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f, 0f);
        camGo.AddComponent<AudioListener>();
        var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
        urp.renderPostProcessing = true;
        urp.renderShadows = true;
        CameraFollow follow = camGo.AddComponent<CameraFollow>();
        var cso = new SerializedObject(follow);
        cso.FindProperty("target").objectReferenceValue = player.transform;
        cso.FindProperty("offset").vector3Value = CamOffset;
        cso.FindProperty("outdoorOrthographicSize").floatValue = 6f;
        cso.FindProperty("interiorOrthographicSize").floatValue = 4.5f;
        cso.FindProperty("roomTransitionDuration").floatValue = 0.3f;
        cso.ApplyModifiedPropertiesWithoutUndo();
        camGo.transform.position = PlayerSpawn + CamOffset;
        camGo.transform.LookAt(player.transform);
        sb.AppendLine("Main Camera + CameraFollow offset=" + CamOffset + " ortho=6");

        GameObject lightGo = new GameObject("Directional Light");
        SceneManager.MoveGameObjectToScene(lightGo, scene);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.97f, 0.92f);
        light.intensity = 1.55f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        if (lightGo.GetComponent<UniversalAdditionalLightData>() == null)
            lightGo.AddComponent<UniversalAdditionalLightData>();
        sb.AppendLine("Directional Light intensity=1.55 (playground values)");

        // Jump-on target — same JumpOnSeatPoint + Seat child as playground, not CafeRoom.
        GameObject seatRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(seatRoot, scene);
        seatRoot.name = "SeatTarget";
        seatRoot.transform.SetPositionAndRotation(new Vector3(SeatPos.x, 0.25f, SeatPos.z), Quaternion.identity);
        seatRoot.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
        Rigidbody seatRb = seatRoot.GetComponent<Rigidbody>();
        if (seatRb != null)
            Object.DestroyImmediate(seatRb);
        JumpOnSeatPoint seatPoint = seatRoot.AddComponent<JumpOnSeatPoint>();
        var sso = new SerializedObject(seatPoint);
        sso.FindProperty("defaultSeatLocalPosition").vector3Value = new Vector3(0f, 0.38f, 0.05f);
        sso.FindProperty("sitOffset").vector3Value = new Vector3(0f, 0.05f, 0.1f);
        sso.FindProperty("freezeRigidbodyOnMount").boolValue = false;
        sso.ApplyModifiedPropertiesWithoutUndo();
        sb.AppendLine("SeatTarget + JumpOnSeatPoint at " + SeatPos);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved=" + saved + " " + ScenePath);
        sb.AppendLine("active=" + SceneManager.GetActiveScene().path);
        sb.AppendLine(saved && SceneManager.GetActiveScene().path == ScenePath ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
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

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max) + "\n…";
    }
}
