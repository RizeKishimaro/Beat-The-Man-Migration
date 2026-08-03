using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Renders rest vs raised hand poses for Character_Base (same camera angle).
/// Menu: Tools/For Cursor/Capture Hand Carry Comparison
/// Flag: Temp/capture_hand_carry.flag
/// </summary>
[InitializeOnLoad]
public static class HandCarryScreenshotCapture
{
    const string FlagPath = "Temp/capture_hand_carry.flag";
    const string VerifyPath = "Temp/agent_verify_hand_screenshots.txt";
    const string CharacterPath = "Assets/Art/Characters/Character_Base.fbx";
    const string OutDir = "Assets/Screenshots";

    static HandCarryScreenshotCapture()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.playModeStateChanged += _ => EditorApplication.delayCall += TryRun;
    }

    [InitializeOnLoadMethod]
    static void Boot() => EditorApplication.delayCall += TryRun;

    static void TryRun()
    {
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += TryRun;
            return;
        }

        try
        {
            File.Delete(flag);
            string report = Capture();
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
            Debug.Log("[HandCarryScreenshotCapture]\n" + report);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)),
                "FAIL: " + e);
            Debug.LogError("[HandCarryScreenshotCapture] " + e);
        }
    }

    [MenuItem("Tools/For Cursor/Capture Hand Carry Comparison")]
    public static void MenuCapture()
    {
        string report = Capture();
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath)), report);
        EditorUtility.DisplayDialog("Hand Carry Screenshots", report, "OK");
    }

    static string Capture()
    {
        var sb = new System.Text.StringBuilder();
        Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutDir)));

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
        if (prefab == null)
            throw new System.Exception("Missing character FBX: " + CharacterPath);

        var root = new GameObject("HandCapture_Root");
        try
        {
            GameObject character = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (character == null)
                character = Object.Instantiate(prefab);
            character.name = "Character_Base";
            character.transform.SetParent(root.transform, false);
            // Match PlayerController presentation.
            character.transform.localPosition = Vector3.zero;
            character.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            character.transform.localScale = Vector3.one;

            Transform handL = FindDeep(character.transform, "Character_HandL");
            Transform handR = FindDeep(character.transform, "Character_HandR");
            if (handL == null || handR == null)
                throw new System.Exception("Hands not found under character");

            Vector3 restLocal = handL.localPosition;
            Vector3 restWorld = handL.position;

            handL.localPosition = restLocal + Vector3.up * 0.55f;
            Vector3 dY = handL.position - restWorld;
            handL.localPosition = restLocal + Vector3.forward * 0.55f;
            Vector3 dZ = handL.position - restWorld;
            handL.localPosition = restLocal + Vector3.right * 0.55f;
            Vector3 dX = handL.position - restWorld;
            handL.localPosition = restLocal;

            sb.AppendLine($"axisProbe restWorld={restWorld}");
            sb.AppendLine($"axisProbe +localY→Δworld={dY} (upComponent={dY.y:F3})");
            sb.AppendLine($"axisProbe +localZ→Δworld={dZ} (upComponent={dZ.y:F3})");
            sb.AppendLine($"axisProbe +localX→Δworld={dX} (upComponent={dX.y:F3})");

            // Palm normal probe: mesh avg not available; use Transform direction of -local Z (Blender palm).
            Vector3 palmDir = handL.TransformDirection(Vector3.back); // -local Z
            sb.AppendLine($"palmDirWorld(-localZ)={palmDir} upDot={Vector3.Dot(palmDir.normalized, Vector3.up):F3}");

            Transform charRoot = FindDeep(character.transform, "Character_Root") ?? character.transform;
            var hands = charRoot.gameObject.AddComponent<ProceduralHandCarryMotion>();
            hands.BindHands();

            sb.AppendLine($"offset={hands.EditorRaiseLocalOffset} euler={hands.EditorRaiseLocalEuler}");

            // Frame camera from character bounds (upper body).
            Bounds b = new Bounds(character.transform.position, Vector3.one * 0.1f);
            foreach (var r in character.GetComponentsInChildren<Renderer>())
                b.Encapsulate(r.bounds);

            var camGo = new GameObject("HandCapture_Cam");
            camGo.transform.SetParent(root.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.62f, 0.7f, 1f);
            cam.orthographic = false;
            cam.fieldOfView = 40f;
            Vector3 focus = b.center + Vector3.up * (b.extents.y * 0.25f);
            camGo.transform.position = focus + new Vector3(1.8f, 0.6f, -2.4f);
            camGo.transform.LookAt(focus);

            // Isolate from scene clutter: only render our capture root via layer.
            int layer = 31;
            SetLayerRecursive(root.transform, layer);
            cam.cullingMask = 1 << layer;

            string restPath = Path.Combine(OutDir, "hand-carry-rest.png");
            string raisedPath = Path.Combine(OutDir, "hand-carry-raised.png");

            hands.EditorSnapRest();
            RenderToPng(cam, restPath);
            sb.AppendLine("rest=" + restPath);
            if (handL != null)
                sb.AppendLine($"restHandL local={handL.localPosition} world={handL.position} palmUpDot={Vector3.Dot(handL.TransformDirection(Vector3.back).normalized, Vector3.up):F3}");

            hands.NotifyPickedUp();
            hands.EditorSnapRaised();
            RenderToPng(cam, raisedPath);
            sb.AppendLine("raised=" + raisedPath);
            if (handL != null)
                sb.AppendLine($"raisedHandL local={handL.localPosition} world={handL.position} palmUpDot={Vector3.Dot(handL.TransformDirection(Vector3.back).normalized, Vector3.up):F3}");

            AssetDatabase.Refresh();
            sb.AppendLine("OK");
            return sb.ToString();
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i), layer);
    }

    static void RenderToPng(Camera cam, string assetPath)
    {
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        Directory.CreateDirectory(Path.GetDirectoryName(full));

        int w = 1024;
        int h = 1024;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(full, tex.EncodeToPNG());
        cam.targetTexture = prev;
        RenderTexture.active = prevActive;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
    }

    static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform f = FindDeep(parent.GetChild(i), name);
            if (f != null)
                return f;
        }
        return null;
    }
}
