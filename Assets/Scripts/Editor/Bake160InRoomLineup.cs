using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ticket 160 — place spike chair, cream variants, and other Meshy objects in
/// GridFoundation as a second DefaultBuilding (not overlapping DefaultBuilding_117)
/// interior zoom (ortho 4.5). Game View forced to 1x. Does not save the scene.
/// Flag: Temp/bake160_in_room.flag
/// </summary>
[InitializeOnLoad]
public static class Bake160InRoomLineup
{
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string ParentName = "Bake160_InRoom_DO_NOT_SAVE";
    const string FlagPath = "Temp/bake160_in_room.flag";
    const string ReportPath = "Temp/bake160_in_room.txt";
    const string ShotPath = "Temp/bake160/in_room_gameview.png";
    const string BuildingPrefab = "Assets/Prefabs/Buildings/DefaultBuilding.prefab";
    const float InteriorOrtho = 4.5f;
    // Existing DefaultBuilding_117 sits at (8,0,4), ~6.6m footprint (x 8.7–15.3, z 4.7–11.3).
    static readonly Vector3 CompareRoomOrigin = new Vector3(22f, 0f, 4f);

    const string SpikeFbx = "Assets/Objects/Meshy_Chair_Unity/Meshy_Chair_Unity.fbx";
    const string CreamFbx =
        "Assets/Objects/Pickable/CreamChair_Raw153/Meshy_AI_Cream_Cushioned_Woode_0805155347_texture.fbx";
    const string CreamMatPath = "Assets/Objects/Pickable/CreamChair_Raw153/CreamChair_Raw153.mat";
    const string PrinterPrefab = "Assets/Objects/Pickable/printer/printer.prefab";
    const string CoolerFbx =
        "Assets/Objects/Pickable/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture_fbx/Meshy_AI_Tiny_Blue_Water_Coole_0901110901_texture.fbx";
    const string OrangeFbx =
        "Assets/Objects/Pickable/Meshy_AI_Orange_Office_Chair_0826075021_texture_fbx/Meshy_AI_Orange_Office_Chair_0826075021_texture.fbx";

    static Bake160InRoomLineup()
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

    [MenuItem("Tools/Objects/160 Gloss Lineup In Cafe (Game View 1x)")]
    public static void Menu()
    {
        string report = Run();
        File.WriteAllText(Full(ReportPath), report);
        EditorUtility.DisplayDialog("Ticket 160 in-room", Truncate(report, 1600), "OK");
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
            Debug.Log("[Bake160InRoomLineup]\n" + report);
        }
        catch (Exception e)
        {
            File.WriteAllText(Full(ReportPath), "ERROR " + e);
            Debug.LogError("[Bake160InRoomLineup] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 160 in-room Game View 1x ===");
        sb.AppendLine("Does NOT save GridFoundation. Parent=" + ParentName);

        if (EditorApplication.isPlaying)
        {
            sb.AppendLine("FAIL still playing");
            return sb.ToString();
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("scene=" + scene.path);

        GameObject old = GameObject.Find(ParentName);
        if (old != null)
            UnityEngine.Object.DestroyImmediate(old);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingPrefab);
        if (prefab == null)
        {
            sb.AppendLine("FAIL missing " + BuildingPrefab);
            return sb.ToString();
        }

        var parent = new GameObject(ParentName);
        GameObject roomGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        roomGo.name = "Bake160_CompareRoom";
        roomGo.transform.SetParent(parent.transform, true);
        roomGo.transform.SetPositionAndRotation(CompareRoomOrigin, Quaternion.identity);
        Transform building = roomGo.transform;

        Transform existing = FindNamed("DefaultBuilding_117");
        Bounds? existingB = existing != null ? CollectBounds(existing.gameObject) : null;
        Bounds? newB = CollectBounds(roomGo);
        if (existingB.HasValue && newB.HasValue)
        {
            bool overlap = existingB.Value.Intersects(newB.Value);
            sb.AppendLine("existing=" + existingB.Value + " new=" + newB.Value + " overlap=" + overlap);
            if (overlap)
            {
                sb.AppendLine("FAIL rooms overlap");
                return sb.ToString();
            }
        }

        Transform floor = FindNamedUnder(building, "RoomFloor");
        Bounds? bb = newB;
        Bounds? fb = floor != null ? CollectBounds(floor.gameObject) : null;
        float floorTop;
        Vector3 origin;
        if (fb.HasValue && fb.Value.size.x > 2f && fb.Value.size.z > 2f)
        {
            floorTop = fb.Value.max.y;
            origin = new Vector3(fb.Value.center.x, floorTop, fb.Value.center.z);
        }
        else if (bb.HasValue)
        {
            floorTop = fb.HasValue ? fb.Value.max.y : bb.Value.min.y + 0.02f;
            origin = new Vector3(bb.Value.center.x, floorTop, bb.Value.center.z);
        }
        else
        {
            sb.AppendLine("FAIL compare room has no renderers");
            return sb.ToString();
        }
        sb.AppendLine("compareRoom pos=" + building.position + " origin=" + origin);

        Material creamSrc = AssetDatabase.LoadAssetAtPath<Material>(CreamMatPath);

        Transform p = parent.transform;
        SpawnLabeled(p, SpikeFbx, null, origin + new Vector3(-2.0f, 0f, -2.2f), "SPIKE orig", null);
        SpawnLabeled(p, CreamFbx, creamSrc, origin + new Vector3(-1.0f, 0f, -2.2f), "CREAM 1.0", null);
        SpawnLabeled(p, CreamFbx, creamSrc, origin + new Vector3(0f, 0f, -2.2f), "CREAM 0.5",
            m => SetSmooth(m, 0.5f));
        SpawnLabeled(p, CreamFbx, creamSrc, origin + new Vector3(1.0f, 0f, -2.2f), "CREAM 0.0",
            m => SetSmooth(m, 0f));
        SpawnLabeled(p, CreamFbx, creamSrc, origin + new Vector3(2.0f, 0f, -2.2f), "CREAM flat0.5",
            ApplyFlat);

        SpawnPrefab(p, PrinterPrefab, origin + new Vector3(-1.0f, 0f, -0.7f), "PRINTER");
        SpawnLabeled(p, CoolerFbx, null, origin + new Vector3(0f, 0f, -0.7f), "COOLER", null);
        SpawnLabeled(p, OrangeFbx, null, origin + new Vector3(1.0f, 0f, -0.7f), "ORANGE", null);

        SitChildrenOnFloor(parent, floorTop);

        HideForInteriorView(building, sb);

        Camera cam = Camera.main;
        if (cam == null)
        {
            sb.AppendLine("FAIL no Main Camera");
            return sb.ToString();
        }

        cam.orthographic = true;
        cam.orthographicSize = InteriorOrtho;
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(8f, -7f, 8f).normalized);
        cam.transform.position = origin + new Vector3(-8f, 7f, -8f);
        sb.AppendLine("cam pos=" + cam.transform.position + " ortho=" + cam.orthographicSize
            + " (in-room 4.5, same iso as CameraFollow)");

        Vector2 gvSize = Handles.GetMainGameViewSize();
        sb.AppendLine("GameView size=" + gvSize);
        bool scaleOk = SetGameViewScale1x(sb);
        sb.AppendLine("GameView 1x=" + scaleOk);

        FocusGameView();
        SceneView.RepaintAll();

        Directory.CreateDirectory(Full("Temp/bake160"));
        CaptureFromCamera(cam, ShotPath, sb);

        sb.AppendLine("LOOK: Unity Game View at scale 1x (100%). Same iso + ortho 4.5 as in-room play.");
        sb.AppendLine("Front walls/ceiling hidden so you can see inside (edit-mode walls are solid).");
        sb.AppendLine("Do not save GridFoundation. Delete " + ParentName + " or reload the scene when done.");
        sb.AppendLine("shot=" + ShotPath);
        sb.AppendLine("RESULT=OK");
        return sb.ToString();
    }

    static void SpawnLabeled(
        Transform parent,
        string fbxPath,
        Material creamSrc,
        Vector3 pos,
        string label,
        Action<Material> setup)
    {
        GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (src == null)
        {
            Debug.LogWarning("[Bake160InRoomLineup] missing " + fbxPath);
            return;
        }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        if (go == null)
            go = UnityEngine.Object.Instantiate(src);
        go.name = "160_" + label;
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 35f, 0f));
        go.transform.localScale = Vector3.one;

        if (setup != null && creamSrc != null)
        {
            Material inst = new Material(creamSrc);
            inst.name = "160mat_" + label;
            setup(inst);
            AssignMat(go, inst);
        }

        AddLabel(go, label);
    }

    static void SpawnPrefab(Transform parent, string prefabPath, Vector3 pos, string label)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[Bake160InRoomLineup] missing " + prefabPath);
            return;
        }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "160_" + label;
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 35f, 0f));
        AddLabel(go, label);
    }

    static void SetSmooth(Material m, float mul)
    {
        if (m.HasProperty("_Smoothness"))
            m.SetFloat("_Smoothness", mul);
    }

    static void ApplyFlat(Material m)
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
    }

    static void AssignMat(GameObject root, Material mat)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
            rends[i].sharedMaterial = mat;
    }

    static void SitChildrenOnFloor(GameObject parent, float floorTop)
    {
        for (int i = 0; i < parent.transform.childCount; i++)
        {
            Transform t = parent.transform.GetChild(i);
            if (t.name == "Bake160_CompareRoom")
                continue;
            Bounds? b = CollectBounds(t.gameObject);
            if (!b.HasValue)
                continue;
            Vector3 p = t.position;
            p.y += floorTop - b.Value.min.y;
            t.position = p;
        }
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

    static void AddLabel(GameObject go, string text)
    {
        var label = new GameObject("label");
        label.transform.SetParent(go.transform, false);
        Bounds? b = CollectBounds(go);
        float h = b.HasValue ? b.Value.size.y : 1f;
        label.transform.position = go.transform.position + Vector3.up * (h + 0.12f);
        var tm = label.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = 0.035f;
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            tm.font = font;
        label.transform.rotation = Quaternion.LookRotation(new Vector3(8f, -7f, 8f).normalized);
    }

    static Transform FindNamedUnder(Transform root, string name)
    {
        if (root == null)
            return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }
        return null;
    }

    static Transform FindNamed(string name)
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name == name)
                return t;
        }
        return null;
    }

    static bool SetGameViewScale1x(StringBuilder sb)
    {
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
        {
            sb.AppendLine("no GameView type");
            return false;
        }
        EditorWindow gv = EditorWindow.GetWindow(gvType, false, null, true);
        if (gv == null)
            return false;

        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object za = gvType.GetField("m_ZoomArea", inst)?.GetValue(gv);
        if (za != null)
        {
            Type zaType = za.GetType();
            var scaleProp = zaType.GetProperty("scale", inst);
            if (scaleProp != null && scaleProp.CanWrite)
            {
                object cur = scaleProp.GetValue(za, null);
                if (cur is Vector2)
                {
                    scaleProp.SetValue(za, Vector2.one, null);
                    sb.AppendLine("set ZoomableArea.scale=1");
                    gv.Repaint();
                    return true;
                }
            }
            var scaleField = zaType.GetField("m_Scale", inst);
            if (scaleField != null && scaleField.FieldType == typeof(Vector2))
            {
                scaleField.SetValue(za, Vector2.one);
                sb.AppendLine("set ZoomableArea.m_Scale=1");
                gv.Repaint();
                return true;
            }
        }

        var sizeField = gvType.GetField("m_Size", inst);
        if (sizeField != null && sizeField.FieldType == typeof(float))
        {
            sizeField.SetValue(gv, 1f);
            sb.AppendLine("set GameView.m_Size=1");
            gv.Repaint();
            return true;
        }

        sb.AppendLine("WARN could not set Game View scale via reflection");
        return false;
    }

    static void FocusGameView()
    {
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return;
        EditorWindow gv = EditorWindow.GetWindow(gvType, false, "Game", true);
        gv.Focus();
        gv.Repaint();
    }

    static void HideForInteriorView(Transform building, StringBuilder sb)
    {
        string[] names =
        {
            "Wall_Front_Left", "Wall_Front_Right", "Wall_Front_Lintel", "RoomCeiling"
        };
        for (int i = 0; i < names.Length; i++)
        {
            Transform t = FindNamedUnder(building, names[i]);
            if (t == null)
                continue;
            Renderer[] rends = t.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < rends.Length; r++)
                rends[r].enabled = false;
            sb.AppendLine("hid " + names[i]);
        }
    }

    static void CaptureFromCamera(Camera cam, string rel, StringBuilder sb)
    {
        Vector2 gv = Handles.GetMainGameViewSize();
        int w = Mathf.Max(16, Mathf.RoundToInt(gv.x));
        int h = Mathf.Max(16, Mathf.RoundToInt(gv.y));
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        Texture2D tex = ReadRt(rt);
        cam.targetTexture = prev;
        UnityEngine.Object.DestroyImmediate(rt);
        File.WriteAllBytes(Full(rel), tex.EncodeToPNG());
        sb.AppendLine("capture=Camera.main RT " + w + "x" + h + " bytes=" + new FileInfo(Full(rel)).Length);
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static Texture2D ReadRt(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        return tex;
    }

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "\n…";
}
