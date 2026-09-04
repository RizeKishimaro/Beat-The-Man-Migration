using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 160 — disposable printer material comparison row inside Bake160_CompareRoom.
/// Does not save GridFoundation. Does not write printer.prefab / printer.mat.
/// Test mats: Assets/Materials/Test/MaterialCompare/ (DISPOSABLE).
/// Flag: Temp/bake160_printer_row.flag
/// </summary>
[InitializeOnLoad]
public static class Bake160PrinterCompareRow
{
    const string ScenePath = "Assets/Scenes/GridFoundation.unity";
    const string ParentName = "Bake160_InRoom_DO_NOT_SAVE";
    const string RoomName = "Bake160_CompareRoom";
    const string RowName = "Bake160_PrinterRow";
    const string BuildingPrefab = "Assets/Prefabs/Buildings/DefaultBuilding.prefab";
    const string PrinterPrefab = "Assets/Objects/Pickable/printer/printer.prefab";
    const string ProductionMat = "Assets/Objects/Pickable/printer/printer.mat";
    const string MatDir = "Assets/Materials/Test/MaterialCompare";
    const string FlagPath = "Temp/bake160_printer_row.flag";
    const string ReportPath = "Temp/bake160_printer_row.txt";
    const string ShotPath = "Temp/bake160/printer_row_play.png";
    const string PendingKey = "Bake160PrinterCompareRow.pending";
    const float InteriorOrtho = 4.5f;
    static readonly Vector3 CompareRoomOrigin = new Vector3(22f, 0f, 4f);
    const float WantedSpacing = 1.5f;

    static int playFrames;

    static Bake160PrinterCompareRow()
    {
        // Leftover SessionState from a prior capture must never re-hijack CameraFollow.
        SessionState.SetBool(PendingKey, false);
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += PollFlag;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    static void PollFlag()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRun();
    }

    [MenuItem("Tools/Objects/160 Printer Material Compare Row")]
    public static void Menu()
    {
        string report = Run();
        File.WriteAllText(Full(ReportPath), report);
        EditorUtility.DisplayDialog("Ticket 160 printer row", Truncate(report, 1600), "OK");
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
            Debug.Log("[Bake160PrinterCompareRow]\n" + report);
        }
        catch (Exception e)
        {
            File.WriteAllText(Full(ReportPath), "ERROR " + e);
            Debug.LogError("[Bake160PrinterCompareRow] " + e);
        }
    }

    static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Ticket 160 printer material compare row ===");
        sb.AppendLine("ROW_BUILD=6");
        sb.AppendLine("DISPOSABLE scaffolding. Production printer.prefab / printer.mat not written.");

        string prodGuidBefore = AssetDatabase.AssetPathToGUID(ProductionMat);
        string prefabGuidBefore = AssetDatabase.AssetPathToGUID(PrinterPrefab);

        if (EditorApplication.isPlaying)
        {
            sb.AppendLine("FAIL still playing");
            return sb.ToString();
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        sb.AppendLine("scene=" + SceneManager.GetActiveScene().path);

        EnsureFolders();
        WriteDisposableNote();
        Material[] mats = EnsureTestMaterials(sb);

        GameObject parent = GameObject.Find(ParentName);
        Transform building = parent != null ? FindNamedUnder(parent.transform, RoomName) : null;
        if (building == null)
        {
            if (parent != null)
                UnityEngine.Object.DestroyImmediate(parent);
            parent = new GameObject(ParentName);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingPrefab);
            if (prefab == null)
            {
                sb.AppendLine("FAIL missing " + BuildingPrefab);
                return sb.ToString();
            }
            GameObject roomGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            roomGo.name = RoomName;
            roomGo.transform.SetParent(parent.transform, true);
            roomGo.transform.SetPositionAndRotation(CompareRoomOrigin, Quaternion.identity);
            building = roomGo.transform;
            sb.AppendLine("recreated " + RoomName + " at " + CompareRoomOrigin);
        }
        else
        {
            sb.AppendLine("reused existing " + RoomName + " at " + building.position);
            for (int i = parent.transform.childCount - 1; i >= 0; i--)
            {
                Transform ch = parent.transform.GetChild(i);
                if (ch.name != RoomName && ch.name != RowName)
                    UnityEngine.Object.DestroyImmediate(ch.gameObject);
            }
        }

        Transform existing = FindNamed("DefaultBuilding_117");
        Bounds? existingB = existing != null ? CollectBounds(existing.gameObject) : null;
        Bounds? newB = CollectBounds(building.gameObject);
        if (existingB.HasValue && newB.HasValue)
        {
            bool overlap = existingB.Value.Intersects(newB.Value);
            sb.AppendLine("DefaultBuilding_117 vs compareRoom overlap=" + overlap);
            if (overlap)
            {
                sb.AppendLine("FAIL rooms overlap");
                return sb.ToString();
            }
        }

        Transform oldRow = FindNamedUnder(parent.transform, RowName);
        if (oldRow != null)
            UnityEngine.Object.DestroyImmediate(oldRow.gameObject);

        Bounds? floorB = CollectBounds(building.gameObject);
        Transform floor = FindNamedUnder(building, "RoomFloor");
        float floorTop = 0.05f;
        if (floor != null)
        {
            Bounds? fb = CollectBounds(floor.gameObject);
            if (fb.HasValue)
                floorTop = fb.Value.max.y;
        }
        if (!floorB.HasValue)
        {
            sb.AppendLine("FAIL no floor/bounds");
            return sb.ToString();
        }

        float usable = Mathf.Min(floorB.Value.size.x, floorB.Value.size.z) - 1.2f;
        float spacing = Mathf.Min(WantedSpacing, usable / 6f);
        sb.AppendLine("bldgBounds=" + floorB.Value + " spacing=" + spacing
            + " (1.5m does not fit ~6.6m interior; used max even spacing)");

        Vector3 origin = new Vector3(floorB.Value.center.x, floorTop, floorB.Value.center.z);
        var row = new GameObject(RowName);
        row.transform.SetParent(parent.transform, true);

        string[] labels =
        {
            "1 NO MAT",
            "2 CURRENT 1.0",
            "3 MUL 0.5",
            "4 MUL 0.6",
            "5 MUL 0.7",
            "6 MUL 0.8",
            "7 FLAT 0.5"
        };

        float startX = origin.x - spacing * 3f;
        for (int i = 0; i < 7; i++)
        {
            Vector3 pos = new Vector3(startX + spacing * i, origin.y, origin.z);
            SpawnPrinter(row.transform, pos, labels[i], mats[i], sb);
        }

        SitChildrenOnFloor(row, floorTop);
        HideForInteriorView(building, sb);

        string prodGuidAfter = AssetDatabase.AssetPathToGUID(ProductionMat);
        string prefabGuidAfter = AssetDatabase.AssetPathToGUID(PrinterPrefab);
        bool prodUntouched = prodGuidBefore == prodGuidAfter && prefabGuidBefore == prefabGuidAfter;
        sb.AppendLine("production GUIDs unchanged: " + prodUntouched);
        sb.AppendLine("production mat path still " + ProductionMat);

        Directory.CreateDirectory(Full("Temp/bake160"));
        sb.AppendLine("camera/player untouched (row is disposable scenery only)");
        sb.AppendLine("RESULT=OK");
        return sb.ToString();
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= TickPlay;
            SessionState.SetBool(PendingKey, false);
        }
    }

    static void TickPlay()
    {
        EditorApplication.update -= TickPlay;
        SessionState.SetBool(PendingKey, false);
    }

    static int CountPrinters()
    {
        Transform row = GameObject.Find(RowName) != null ? GameObject.Find(RowName).transform : null;
        return row != null ? row.childCount : 0;
    }

    static Material[] EnsureTestMaterials(StringBuilder sb)
    {
        Material src = AssetDatabase.LoadAssetAtPath<Material>(ProductionMat);
        if (src == null)
            throw new Exception("missing production mat " + ProductionMat);

        sb.AppendLine("src " + ProductionMat
            + " _Smoothness=" + src.GetFloat("_Smoothness")
            + " mask=" + (src.GetTexture("_MetallicGlossMap") != null)
            + " emissionKw=" + src.IsKeywordEnabled("_EMISSION"));

        var specs = new[]
        {
            new MatSpec("160_Printer_01_NoMat", 0f, false, true),
            new MatSpec("160_Printer_02_Current149", 1f, false, false),
            new MatSpec("160_Printer_03_Mul05", 0.5f, false, false),
            new MatSpec("160_Printer_04_Mul06", 0.6f, false, false),
            new MatSpec("160_Printer_05_Mul07", 0.7f, false, false),
            new MatSpec("160_Printer_06_Mul08", 0.8f, false, false),
            new MatSpec("160_Printer_07_FlatSpike", 0.5f, true, false)
        };

        var mats = new Material[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            string path = MatDir + "/" + specs[i].Name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(src);
                m.name = specs[i].Name;
                AssetDatabase.CreateAsset(m, path);
                m = AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            else
            {
                m.CopyPropertiesFromMaterial(src);
            }

            if (specs[i].NoMat)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit == null)
                    unlit = Shader.Find("Sprites/Default");
                m.shader = unlit;
                if (m.HasProperty("_BaseColor"))
                    m.SetColor("_BaseColor", new Color(1f, 0f, 1f, 1f));
                if (m.HasProperty("_Color"))
                    m.SetColor("_Color", new Color(1f, 0f, 1f, 1f));
            }
            else if (specs[i].Flat)
            {
                m.SetTexture("_MetallicGlossMap", null);
                m.DisableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.5f);
                m.SetTexture("_EmissionMap", null);
                m.SetColor("_EmissionColor", Color.black);
                m.DisableKeyword("_EMISSION");
            }
            else
            {
                m.SetFloat("_Smoothness", specs[i].Smooth);
            }

            EditorUtility.SetDirty(m);
            mats[i] = m;
            sb.AppendLine("test mat " + path
                + " shader=" + m.shader.name
                + " smooth=" + (m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness").ToString() : "n/a")
                + " mask=" + (m.HasProperty("_MetallicGlossMap") && m.GetTexture("_MetallicGlossMap") != null)
                + " flat=" + specs[i].Flat + " noMat=" + specs[i].NoMat);
        }
        AssetDatabase.SaveAssets();
        return mats;
    }

    struct MatSpec
    {
        public string Name;
        public float Smooth;
        public bool Flat;
        public bool NoMat;
        public MatSpec(string name, float smooth, bool flat, bool noMat)
        {
            Name = name;
            Smooth = smooth;
            Flat = flat;
            NoMat = noMat;
        }
    }

    static void SpawnPrinter(Transform parent, Vector3 pos, string label, Material mat, StringBuilder sb)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrinterPrefab);
        GameObject go = UnityEngine.Object.Instantiate(prefab);
        go.name = "160_Printer_" + label.Replace(" ", "_");
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 35f, 0f));

        // Display-only clone: strip gameplay so Play-mode physics cannot hide the mesh.
        foreach (var c in go.GetComponents<MonoBehaviour>())
        {
            if (c != null)
                UnityEngine.Object.DestroyImmediate(c);
        }
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
            UnityEngine.Object.DestroyImmediate(rb);
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.DestroyImmediate(col);

        MeshRenderer[] rends = go.GetComponentsInChildren<MeshRenderer>(true);
        var mf = go.GetComponentInChildren<MeshFilter>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            rends[i].forceRenderingOff = false;
            rends[i].enabled = true;
            rends[i].allowOcclusionWhenDynamic = false;
            if (mat != null)
                rends[i].sharedMaterial = mat;
        }
        sb.AppendLine("spawned " + go.name + " pos=" + pos
            + " meshRends=" + rends.Length
            + " mesh=" + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " verts=" + mf.sharedMesh.vertexCount : "NULL")
            + " mat=" + (mat != null ? mat.name : "null"));
        AddLabel(go, label);
    }

    static void AddLabel(GameObject go, string text)
    {
        var label = new GameObject("label");
        label.transform.SetParent(go.transform, false);
        Bounds? b = CollectBounds(go);
        float h = b.HasValue ? b.Value.size.y : 1f;
        label.transform.position = go.transform.position + Vector3.up * (h + 0.15f);
        var tm = label.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 48;
        tm.characterSize = 0.035f;
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            tm.font = font;
        // Face the iso camera (previous LookRotation used camera-forward, so text was mirrored).
        label.transform.rotation = Quaternion.LookRotation(-new Vector3(8f, -7f, 8f).normalized);
    }

    static void SitChildrenOnFloor(GameObject parent, float floorTop)
    {
        for (int i = 0; i < parent.transform.childCount; i++)
        {
            Transform t = parent.transform.GetChild(i);
            Bounds? b = CollectMeshBounds(t.gameObject);
            if (!b.HasValue)
                continue;
            Vector3 p = t.position;
            p.y += floorTop - b.Value.min.y;
            t.position = p;
        }
    }

    static void HideForInteriorView(Transform building, StringBuilder sb)
    {
        string[] names = { "Wall_Front_Left", "Wall_Front_Right", "Wall_Front_Lintel", "RoomCeiling" };
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

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/Materials/Test"))
            AssetDatabase.CreateFolder("Assets/Materials", "Test");
        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/Materials/Test", "MaterialCompare");
    }

    static void WriteDisposableNote()
    {
        string path = MatDir + "/_DISPOSABLE_TICKET160.txt";
        File.WriteAllText(Full(path),
            "DISPOSABLE Ticket 160 comparison scaffolding. Not shipping content.\n"
            + "Do not use these materials on production prefabs.\n"
            + "Safe to delete this folder after the smoothness decision.\n");
        AssetDatabase.ImportAsset(path);
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

    static Bounds? CollectMeshBounds(GameObject root)
    {
        MeshRenderer[] rends = root.GetComponentsInChildren<MeshRenderer>(true);
        Bounds? b = null;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null || rends[i].GetComponent<TextMesh>() != null)
                continue;
            if (!b.HasValue)
                b = rends[i].bounds;
            else
            {
                Bounds nb = b.Value;
                nb.Encapsulate(rends[i].bounds);
                b = nb;
            }
        }
        return b;
    }

    static Transform FindNamed(string name)
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t.name == name)
                return t;
        }
        return null;
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

    static bool SetGameViewScale1x(StringBuilder sb)
    {
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return false;
        EditorWindow gv = EditorWindow.GetWindow(gvType, false, null, true);
        if (gv == null)
            return false;
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object za = gvType.GetField("m_ZoomArea", inst)?.GetValue(gv);
        if (za != null)
        {
            var scaleField = za.GetType().GetField("m_Scale", inst);
            if (scaleField != null && scaleField.FieldType == typeof(Vector2))
            {
                scaleField.SetValue(za, Vector2.one);
                sb.AppendLine("GameView 1x=True");
                gv.Repaint();
                return true;
            }
        }
        sb.AppendLine("GameView 1x=False");
        return false;
    }

    static void FocusGameView()
    {
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return;
        EditorWindow.GetWindow(gvType, false, "Game", true).Focus();
    }

    static void CaptureGameView(string rel, StringBuilder sb)
    {
        Texture2D tex = GrabGameViewRt();
        if (tex == null)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                CaptureFromCamera(cam, rel, sb);
                sb.AppendLine("captureSource=Camera.Render fallback");
                return;
            }
            sb.AppendLine("FAIL no game view pixels");
            return;
        }
        File.WriteAllBytes(Full(rel), tex.EncodeToPNG());
        sb.AppendLine("capture=" + rel + " " + tex.width + "x" + tex.height + " source=GameViewRT");
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static Texture2D GrabGameViewRt()
    {
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null)
            return null;
        foreach (var obj in Resources.FindObjectsOfTypeAll(gvType))
        {
            var w = obj as EditorWindow;
            if (w == null)
                continue;
            w.Repaint();
            foreach (var f in gvType.GetFields(inst))
            {
                if (!typeof(RenderTexture).IsAssignableFrom(f.FieldType))
                    continue;
                var rt = f.GetValue(w) as RenderTexture;
                if (rt != null && rt.width > 32 && rt.height > 32)
                    return ReadRt(rt);
            }
            foreach (var p in gvType.GetProperties(inst))
            {
                if (p.GetIndexParameters().Length > 0)
                    continue;
                if (!typeof(RenderTexture).IsAssignableFrom(p.PropertyType))
                    continue;
                RenderTexture rt;
                try { rt = p.GetValue(w, null) as RenderTexture; }
                catch { continue; }
                if (rt != null && rt.width > 32 && rt.height > 32)
                    return ReadRt(rt);
            }
        }
        try
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex != null && tex.width > 32)
                return tex;
        }
        catch { }
        return null;
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
        sb.AppendLine("capture=" + rel + " " + w + "x" + h);
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
