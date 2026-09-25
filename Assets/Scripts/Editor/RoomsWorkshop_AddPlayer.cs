using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Add Alpha01's Player to RoomsWorkshop so Sam can walk the dressed HR
/// room for scale. Wires existing Main Camera CameraFollow to the player.
/// Does not rebuild the workshop. Does not save Alpha01 or GridFoundation.
/// Flag: Temp/rooms_workshop_add_player.flag
/// Report: Temp/rooms_workshop_add_player.txt
/// </summary>
[InitializeOnLoad]
public static class RoomsWorkshop_AddPlayer
{
    const string ScenePath = RoomsWorkshopSceneSetup.ScenePath;
    const string PlaygroundPath = "Assets/Scenes/GridFoundation.unity";
    const string CharacterPath = "Assets/Art/Characters/Character_Base.fbx";
    const string FlagPath = "Temp/rooms_workshop_add_player.flag";
    const string ReportPath = "Temp/rooms_workshop_add_player.txt";
    static readonly Vector3 PlayerSpawn = new Vector3(-0.58f, 0f, -0.5f);
    static readonly Vector3 CamOffset = new Vector3(-8f, 7f, -8f);

    static RoomsWorkshop_AddPlayer()
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

    [MenuItem("Tools/Scenes/Add Player To RoomsWorkshop")]
    public static void MenuRun()
    {
        string report = Run();
        WriteReport(report);
        EditorUtility.DisplayDialog("Add Player To RoomsWorkshop", Truncate(report, 1600), "OK");
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
            Debug.Log("[RoomsWorkshop_AddPlayer]\n" + report);
        }
        catch (System.Exception e)
        {
            WriteReport("ERROR " + e);
            Debug.LogError("[RoomsWorkshop_AddPlayer] " + e);
        }
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Add Player To RoomsWorkshop ===");
        sb.AppendLine("does not save Alpha01 or GridFoundation");
        sb.AppendLine("does not rebuild or wipe dressed Room_HR");

        Scene current = SceneManager.GetActiveScene();
        if (current.path == PlaygroundPath && current.isDirty)
        {
            EditorSceneManager.OpenScene(PlaygroundPath, OpenSceneMode.Single);
            sb.AppendLine("discarded unsaved GridFoundation dirt (did not save playground)");
            current = SceneManager.GetActiveScene();
        }

        if (!File.Exists(Full(ScenePath)))
        {
            sb.AppendLine("FAIL missing " + ScenePath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        GameObject character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
        if (character == null)
        {
            sb.AppendLine("FAIL missing " + CharacterPath);
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }

        Scene scene;
        if (current.path == ScenePath)
        {
            scene = current;
            sb.AppendLine("using open workshop (kept unsaved dressing)");
        }
        else
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            sb.AppendLine("opened " + scene.path);
        }

        GameObject existing = FindRoot(scene, "Player");
        GameObject player;
        if (existing != null && existing.GetComponent<PlayerController>() != null)
        {
            player = existing;
            player.transform.SetPositionAndRotation(PlayerSpawn, Quaternion.identity);
            sb.AppendLine("reused existing Player");
        }
        else
        {
            if (existing != null)
                Object.DestroyImmediate(existing);
            player = new GameObject("Player");
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
            sb.AppendLine("created Player spawn=" + PlayerSpawn);
        }

        GameObject camGo = FindRoot(scene, "Main Camera");
        if (camGo == null)
        {
            sb.AppendLine("FAIL missing Main Camera");
            sb.AppendLine("RESULT=FAIL");
            return sb.ToString();
        }
        camGo.transform.position = PlayerSpawn + CamOffset;
        GameplayCameraPrefab.ApplyInstanceOverrides(
            camGo, player.transform, enableFollow: true, solidColorBackground: true);
        sb.AppendLine("CameraFollow ON target=Player offset=" + CamOffset);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved=" + saved);

        Scene reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject verifyPlayer = FindRoot(reopened, "Player");
        CameraFollow follow = null;
        GameObject cam = FindRoot(reopened, "Main Camera");
        bool targetOk = false;
        if (cam != null)
        {
            follow = cam.GetComponent<CameraFollow>();
            if (follow != null)
            {
                var so = new SerializedObject(follow);
                targetOk = so.FindProperty("target").objectReferenceValue == verifyPlayer;
            }
        }
        bool ok = saved && verifyPlayer != null && verifyPlayer.GetComponent<PlayerController>() != null
            && follow != null && follow.enabled && targetOk;
        sb.AppendLine("reloaded " + reopened.path);
        sb.AppendLine("Player=" + (verifyPlayer != null) + " CameraFollow.enabled=" + (follow != null && follow.enabled));
        sb.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        return sb.ToString();
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root != null && root.name == name)
                return root;
        return null;
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
