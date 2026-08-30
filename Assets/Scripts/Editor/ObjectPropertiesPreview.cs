using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 150 — live scene preview for ObjectProperties scale/pivot overrides.
/// Wrapper root gets uniform localScale (scaleMultiplier). Mesh child gets
/// localPosition (pivotOffset). FBX / ModelImporter.globalScale untouched.
/// </summary>
public static class ObjectPropertiesPreview
{
    public const string PreviewRootName = "ObjectReviewPreview";
    const HideFlags PreviewFlags = HideFlags.DontSave | HideFlags.NotEditable;

    public static GameObject EnsurePreview(ObjectProperties props)
    {
        if (props == null)
            return null;

        string fbxPath = FindFbxFor(props);
        if (string.IsNullOrEmpty(fbxPath))
        {
            Debug.LogWarning("[ObjectPropertiesPreview] no FBX next to " + props.name);
            return null;
        }

        GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbxAsset == null)
        {
            Debug.LogWarning("[ObjectPropertiesPreview] failed to load " + fbxPath);
            return null;
        }

        GameObject root = FindOrCreateRoot(props);
        Transform wrapper = root.transform.Find("Wrapper");
        if (wrapper == null)
        {
            var w = new GameObject("Wrapper");
            w.hideFlags = PreviewFlags;
            w.transform.SetParent(root.transform, false);
            wrapper = w.transform;
        }

        Transform mesh = wrapper.Find("Mesh");
        if (mesh == null || MeshSourceMismatch(mesh.gameObject, fbxPath))
        {
            if (mesh != null)
                Object.DestroyImmediate(mesh.gameObject);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            if (instance == null)
                instance = Object.Instantiate(fbxAsset);
            instance.name = "Mesh";
            instance.hideFlags = PreviewFlags;
            instance.transform.SetParent(wrapper, false);
            mesh = instance.transform;
        }

        ApplyTransforms(root, props);
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        return root;
    }

    public static void ApplyTransforms(ObjectProperties props)
    {
        if (props == null)
            return;
        GameObject root = FindExistingRoot(props);
        if (root != null)
            ApplyTransforms(root, props);
    }

    public static void ApplyTransforms(GameObject root, ObjectProperties props)
    {
        if (root == null || props == null)
            return;

        Transform wrapper = root.transform.Find("Wrapper");
        if (wrapper == null)
            return;

        float scale = Mathf.Max(0.01f, props.scaleMultiplier);
        wrapper.localScale = Vector3.one * scale;
        wrapper.localPosition = Vector3.zero;
        wrapper.localRotation = Quaternion.identity;

        Transform mesh = wrapper.Find("Mesh");
        if (mesh == null)
            return;

        mesh.localPosition = props.pivotOffset;
        mesh.localRotation = Quaternion.identity;
        mesh.localScale = Vector3.one;
        EditorUtility.SetDirty(root);
    }

    public static GameObject FindExistingRoot(ObjectProperties props)
    {
        if (props == null)
            return null;
        string name = RootName(props);
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] != null && roots[i].name == name)
                return roots[i];
        }
        return null;
    }

    public static void ClearAllPreviews()
    {
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = roots.Length - 1; i >= 0; i--)
        {
            if (roots[i] != null && roots[i].name.StartsWith(PreviewRootName + "_"))
                Object.DestroyImmediate(roots[i]);
        }
    }

    static GameObject FindOrCreateRoot(ObjectProperties props)
    {
        GameObject existing = FindExistingRoot(props);
        if (existing != null)
            return existing;

        var root = new GameObject(RootName(props));
        root.hideFlags = PreviewFlags;
        root.transform.position = new Vector3(0f, 0f, 2f);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return root;
    }

    static string RootName(ObjectProperties props)
    {
        string n = string.IsNullOrEmpty(props.objectName) ? props.name : props.objectName;
        return PreviewRootName + "_" + n;
    }

    static bool MeshSourceMismatch(GameObject meshGo, string fbxPath)
    {
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(meshGo) as GameObject;
        if (source == null)
            return true;
        string srcPath = AssetDatabase.GetAssetPath(source);
        return !string.Equals(srcPath, fbxPath, System.StringComparison.OrdinalIgnoreCase);
    }

    public static string FindFbxFor(ObjectProperties props)
    {
        if (props == null)
            return null;

        string folder = null;
        if (props.folder != null)
            folder = AssetDatabase.GetAssetPath(props.folder);

        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
        {
            string soPath = AssetDatabase.GetAssetPath(props);
            if (!string.IsNullOrEmpty(soPath))
                folder = Path.GetDirectoryName(soPath)?.Replace('\\', '/');
        }

        if (string.IsNullOrEmpty(folder))
            return null;

        string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", folder));
        if (!Directory.Exists(abs))
            return null;

        foreach (string file in Directory.GetFiles(abs, "*.fbx"))
            return folder + "/" + Path.GetFileName(file);
        foreach (string file in Directory.GetFiles(abs, "*.FBX"))
            return folder + "/" + Path.GetFileName(file);
        return null;
    }
}
