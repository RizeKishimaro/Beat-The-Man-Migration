using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 160 step 4 — GI / lightmap inspect + temporary ContributeGI off test.
/// Does not save the scene. Restores Static flags and CameraFollow.
/// Flag: Temp/fd_gi_inspect.flag
/// </summary>
[InitializeOnLoad]
public static class Bake160GiInspect
{
    const string FlagPath = "Temp/fd_gi_inspect.flag";
    const string ReportPath = "Temp/fd_gi_inspect.txt";
    const string RestorePath = "Temp/fd_gi_static_restore.txt";
    const string CamRestorePath = "Temp/fd_gi_cam_restore.txt";
    const string ShotDir = "Temp/fd_blotch";
    const string PendingKey = "Bake160GiInspect.pending";
    const string EnteredPlayKey = "Bake160GiInspect.enteredPlay";
    const string ChairFbx = "Assets/Objects/Meshy_Chair_Unity/Meshy_Chair_Unity.fbx";
    const string CoolerFbx =
        "Assets/Objects/Pickable/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture_fbx/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture.fbx";

    static int sPhase;
    static int sWait;
    static StringBuilder sLog;

    static Bake160GiInspect()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    static void OnPlay(PlayModeStateChange c)
    {
        if (c == PlayModeStateChange.EnteredEditMode)
        {
            RestoreCam();
            RestoreStaticFromDisk();
        }
    }

    static void Poll()
    {
        if (sPhase == 1)
        {
            sWait++;
            KeepFollowOff();
            if (sWait < 50)
                return;
            GrabPlay();
            return;
        }

        if (sPhase != 0)
            return;
        if (EditorApplication.isCompiling)
            return;

        bool flag = File.Exists(Full(FlagPath));
        bool pending = SessionState.GetBool(PendingKey, false);
        bool enteredPlay = SessionState.GetBool(EnteredPlayKey, false);

        if (enteredPlay && EditorApplication.isPlaying)
        {
            if (sLog == null)
            {
                sLog = new StringBuilder();
                if (File.Exists(Full(ReportPath)))
                    sLog.Append(File.ReadAllText(Full(ReportPath)));
                sLog.AppendLine("resumed in Play after domain reload");
            }
            sPhase = 1;
            sWait = 0;
            return;
        }

        if (!flag && !pending)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;

        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        if (flag)
            File.Delete(Full(FlagPath));

        try
        {
            BeginEdit();
        }
        catch (System.Exception e)
        {
            File.WriteAllText(Full(ReportPath), "FAIL BeginEdit " + e);
            sPhase = 0;
            SessionState.SetBool(PendingKey, false);
            SessionState.SetBool(EnteredPlayKey, false);
        }
    }

    [MenuItem("Tools/Objects/160 GI / Lightmap Inspect")]
    public static void Menu()
    {
        File.WriteAllText(Full(FlagPath), "run\n");
    }

    static void BeginEdit()
    {
        sLog = new StringBuilder();
        sLog.AppendLine("=== Ticket 160 GI/lightmap inspect (revert after test) ===");
        Directory.CreateDirectory(Full(ShotDir));

        DumpLighting(sLog);
        DumpFbx(sLog, "CHAIR_FBX", ChairFbx);
        DumpFbx(sLog, "COOLER_FBX", CoolerFbx);

        var targets = FindTargets();
        sLog.AppendLine("liveTargets=" + targets.Count);
        for (int i = 0; i < targets.Count; i++)
            DumpGo(sLog, targets[i]);

        GameObject chair = FindChair();
        if (chair == null)
        {
            sLog.AppendLine("FAIL no chair in live scene");
            Finish(false);
            return;
        }

        SaveStaticTree(chair);
        bool anyGi = AnyContributeGi(chair);
            sLog.AppendLine("chair.anyContributeGI_before=" + anyGi
                + " isStatic=" + chair.isStatic
                + " rootFlags=" + (StaticEditorFlags)GameObjectUtility.GetStaticEditorFlags(chair));

        ClearStaticTree(chair);
        sLog.AppendLine("chair.rootFlags_afterClear=" + (StaticEditorFlags)GameObjectUtility.GetStaticEditorFlags(chair)
            + " isStatic=" + chair.isStatic);

        PoseCamera(chair);
        SessionState.SetBool(PendingKey, true);
        SessionState.SetBool(EnteredPlayKey, true);
        File.WriteAllText(Full(ReportPath), sLog.ToString() + "\nENTERING PLAY for GI-off shot...\n");
        sPhase = 1;
        sWait = 0;
        EditorApplication.isPlaying = true;
    }

    static void GrabPlay()
    {
        KeepFollowOff();
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        Texture2D tex = null;
        try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
        catch { }
        if (tex == null || tex.width < 32)
        {
            if (tex != null)
                Object.DestroyImmediate(tex);
            tex = GrabGameRt();
        }
        if (tex != null)
        {
            File.WriteAllBytes(Full(ShotDir + "/gi_static_off_play.png"), tex.EncodeToPNG());
            sLog.AppendLine("shot=" + ShotDir + "/gi_static_off_play.png " + tex.width + "x" + tex.height);
            Object.DestroyImmediate(tex);
        }
        else
            sLog.AppendLine("shot grab=null");

        GameObject chair = FindChair();
        if (chair != null)
        {
            sLog.AppendLine("play.chair.isStatic=" + chair.isStatic
                + " flags=" + (StaticEditorFlags)GameObjectUtility.GetStaticEditorFlags(chair));
            var rends = chair.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
                sLog.AppendLine("play.renderer " + rends[i].name
                    + " lightmapIndex=" + rends[i].lightmapIndex
                    + " realtimeIndex=" + rends[i].realtimeLightmapIndex);
        }
        Finish(true);
    }

    static void Finish(bool ok)
    {
        sLog.AppendLine(ok ? "RESULT=OK" : "RESULT=FAIL");
        File.WriteAllText(Full(ReportPath), sLog.ToString());
        Debug.Log("[Bake160GiInspect]\n" + sLog);
        sPhase = 0;
        SessionState.SetBool(PendingKey, false);
        bool entered = SessionState.GetBool(EnteredPlayKey, false);
        SessionState.SetBool(EnteredPlayKey, false);
        if (entered && EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    static List<GameObject> FindTargets()
    {
        var list = new List<GameObject>();
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null)
                continue;
            string n = all[i].name;
            bool hit = n.IndexOf("Chair", System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Cooler", System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Water_Coole", System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("DIAG_SPIKE", System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("DIAG_COOLER", System.StringComparison.OrdinalIgnoreCase) >= 0
                       || n == "WorldFloor";
            if (!hit)
                continue;
            GameObject go = all[i].gameObject;
            if (!list.Contains(go) && (all[i].parent == null || !NameHit(all[i].parent.name)))
                list.Add(go);
        }
        return list;
    }

    static bool NameHit(string n)
    {
        return n.IndexOf("Chair", System.StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Cooler", System.StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Water_Coole", System.StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("DIAG_SPIKE", System.StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("DIAG_COOLER", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static GameObject FindChair()
    {
        var spike = GameObject.Find("Meshy_Chair_Spike");
        if (spike != null)
            return spike;
        var diag = GameObject.Find("DIAG_SPIKE_CHAIR");
        if (diag != null)
            return diag;
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name.IndexOf("Chair", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return all[i].gameObject;
        }
        return null;
    }

    static void DumpLighting(StringBuilder sb)
    {
        var scene = SceneManager.GetActiveScene();
        sb.AppendLine("scene=" + scene.path);
        var lms = LightmapSettings.lightmaps;
        sb.AppendLine("lightmaps.Length=" + (lms != null ? lms.Length : -1));
        sb.AppendLine("lightmapsMode=" + LightmapSettings.lightmapsMode);
        sb.AppendLine("lightProbes=" + (LightmapSettings.lightProbes != null
            ? ("count=" + LightmapSettings.lightProbes.count) : "null"));
#if UNITY_2020_1_OR_NEWER
        try
        {
            var lda = Lightmapping.lightingDataAsset;
            sb.AppendLine("lightingDataAsset=" + (lda != null ? lda.name + " path=" + AssetDatabase.GetAssetPath(lda) : "NULL"));
        }
        catch (System.Exception e)
        {
            sb.AppendLine("lightingDataAsset THROW " + e.Message);
        }
        try
        {
            var ls = Lightmapping.lightingSettings;
            sb.AppendLine("lightingSettings=" + (ls != null ? ls.name : "NULL (scene uses defaults)"));
            if (ls != null)
            {
                sb.AppendLine("  mixedBakeMode=" + ls.mixedBakeMode);
                sb.AppendLine("  realtimeGI=" + ls.realtimeGI);
                sb.AppendLine("  bakedGI=" + ls.bakedGI);
                sb.AppendLine("  lightmapper=" + ls.lightmapper);
            }
        }
        catch (System.Exception e)
        {
            sb.AppendLine("lightingSettings THROW " + e.Message);
        }
#endif
        sb.AppendLine("QualitySettings skipped");
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        for (int i = 0; i < lights.Length; i++)
        {
            var l = lights[i];
            sb.AppendLine("light " + l.name + " type=" + l.type + " mode=" + l.lightmapBakeType
                + " shadows=" + l.shadows + " intensity=" + l.intensity);
        }
    }

    static void DumpFbx(StringBuilder sb, string label, string path)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        sb.AppendLine("--- " + label + " " + path + " ---");
        if (mi == null)
        {
            sb.AppendLine("  MISSING");
            return;
        }
        sb.AppendLine("  generateSecondaryUV=" + mi.generateSecondaryUV);
        sb.AppendLine("  secondaryUVPackMargin=" + mi.secondaryUVPackMargin);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null)
            return;
        DumpGo(sb, go);
        var filters = go.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh m = filters[i].sharedMesh;
            if (m == null)
                continue;
            int uv2 = m.uv2 != null ? m.uv2.Length : 0;
            sb.AppendLine("  mesh=" + m.name + " verts=" + m.vertexCount
                + " uv=" + (m.uv != null ? m.uv.Length : 0)
                + " uv2=" + uv2);
        }
    }

    static void DumpGo(StringBuilder sb, GameObject go)
    {
        if (go == null)
            return;
        var flags = (StaticEditorFlags)GameObjectUtility.GetStaticEditorFlags(go);
        sb.AppendLine("GO " + PathOf(go.transform) + " isStatic=" + go.isStatic
            + " flags=" + flags + " (" + (int)flags + ")"
            + " contributeGI=" + flags.HasFlag(StaticEditorFlags.ContributeGI));
        var rends = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            var f = (StaticEditorFlags)GameObjectUtility.GetStaticEditorFlags(r.gameObject);
            sb.AppendLine("  rend " + r.name
                + " isStatic=" + r.gameObject.isStatic
                + " flags=" + f
                + " contributeGI=" + f.HasFlag(StaticEditorFlags.ContributeGI)
                + " lightmapIndex=" + r.lightmapIndex
                + " realtimeLightmapIndex=" + r.realtimeLightmapIndex
                + " scaleOffset=" + r.lightmapScaleOffset
                + " receiveGI=" + GetReceiveGi(r));
        }
    }

    static string GetReceiveGi(Renderer r)
    {
        var so = new SerializedObject(r);
        var p = so.FindProperty("m_ReceiveGI");
        if (p == null)
            return "noprop";
        return p.intValue == 1 ? "Lightmaps" : (p.intValue == 2 ? "LightProbes" : p.intValue.ToString());
    }

    static bool AnyContributeGi(GameObject root)
    {
        var trs = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < trs.Length; i++)
        {
            var f = (StaticEditorFlags)GameObjectUtility.GetStaticEditorFlags(trs[i].gameObject);
            if (f.HasFlag(StaticEditorFlags.ContributeGI) || trs[i].gameObject.isStatic)
                return true;
        }
        return false;
    }

    static void SaveStaticTree(GameObject root)
    {
        var sb = new StringBuilder();
        var trs = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < trs.Length; i++)
        {
            int flags = (int)GameObjectUtility.GetStaticEditorFlags(trs[i].gameObject);
            sb.AppendLine(PathOf(trs[i]) + "=" + flags);
        }
        File.WriteAllText(Full(RestorePath), sb.ToString());
    }

    static void ClearStaticTree(GameObject root)
    {
        var trs = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < trs.Length; i++)
        {
            GameObjectUtility.SetStaticEditorFlags(trs[i].gameObject, 0);
            trs[i].gameObject.isStatic = false;
        }
    }

    static void RestoreStaticFromDisk()
    {
        string p = Full(RestorePath);
        if (!File.Exists(p))
            return;
        foreach (string line in File.ReadAllLines(p))
        {
            int eq = line.LastIndexOf('=');
            if (eq <= 0)
                continue;
            string path = line.Substring(0, eq);
            int flags;
            if (!int.TryParse(line.Substring(eq + 1), out flags))
                continue;
            GameObject go = GameObject.Find(path.Contains("/") ? path.Substring(path.LastIndexOf('/') + 1) : path);
            Transform t = FindByPath(path);
            if (t == null)
                continue;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, (StaticEditorFlags)flags);
        }
        try { File.Delete(p); } catch { }
    }

    static Transform FindByPath(string path)
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && PathOf(all[i]) == path)
                return all[i];
        }
        return null;
    }

    static void PoseCamera(GameObject chair)
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        var follow = FindFollow(cam);
        bool followOn = follow != null && follow.enabled;
        File.WriteAllText(Full(CamRestorePath),
            cam.transform.position.x + "," + cam.transform.position.y + "," + cam.transform.position.z + "|"
            + cam.transform.rotation.x + "," + cam.transform.rotation.y + ","
            + cam.transform.rotation.z + "," + cam.transform.rotation.w + "|"
            + (cam.orthographic ? cam.orthographicSize : 6f) + "|"
            + (followOn ? 1 : 0));
        if (follow != null)
            follow.enabled = false;
        Bounds b = Encapsulate(chair);
        cam.orthographic = true;
        cam.orthographicSize = 2.0f;
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(8f, -7f, 8f).normalized);
        cam.transform.position = b.center + new Vector3(-4.5f, 4.0f, -4.5f);
    }

    static void RestoreCam()
    {
        Camera cam = Camera.main;
        string p = Full(CamRestorePath);
        if (cam == null || !File.Exists(p))
            return;
        string[] parts = File.ReadAllText(p).Split('|');
        if (parts.Length < 4)
            return;
        string[] xyz = parts[0].Split(',');
        string[] q = parts[1].Split(',');
        float ortho;
        int followOn;
        if (!float.TryParse(parts[2], out ortho) || !int.TryParse(parts[3], out followOn))
            return;
        cam.transform.position = new Vector3(float.Parse(xyz[0]), float.Parse(xyz[1]), float.Parse(xyz[2]));
        cam.transform.rotation = new Quaternion(float.Parse(q[0]), float.Parse(q[1]), float.Parse(q[2]), float.Parse(q[3]));
        cam.orthographic = true;
        cam.orthographicSize = ortho;
        var follow = FindFollow(cam);
        if (follow != null)
            follow.enabled = followOn != 0;
        try { File.Delete(p); } catch { }
    }

    static void KeepFollowOff()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        var f = FindFollow(cam);
        if (f != null && f.enabled)
            f.enabled = false;
    }

    static Behaviour FindFollow(Camera cam)
    {
        foreach (var c in cam.GetComponents<Behaviour>())
        {
            if (c.GetType().Name == "CameraFollow")
                return c;
        }
        return null;
    }

    static Bounds Encapsulate(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds(go.transform.position, Vector3.one);
        bool any = false;
        for (int i = 0; i < rends.Length; i++)
        {
            if (!any)
            {
                b = rends[i].bounds;
                any = true;
            }
            else
                b.Encapsulate(rends[i].bounds);
        }
        return b;
    }

    static Texture2D GrabGameRt()
    {
        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
        {
            if (w.GetType().Name != "GameView")
                continue;
            foreach (var f in w.GetType().GetFields(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
            {
                if (!typeof(RenderTexture).IsAssignableFrom(f.FieldType))
                    continue;
                var rt = f.GetValue(w) as RenderTexture;
                if (rt == null || rt.width < 32)
                    continue;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                return tex;
            }
        }
        return null;
    }

    static string PathOf(Transform t)
    {
        string p = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            p = t.name + "/" + p;
        }
        return p;
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
