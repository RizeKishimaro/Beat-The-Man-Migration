using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 120 — shared Main Camera prefab path + instantiate helper.
/// Prefab is created by Alpha01_120BakeCameraGrid; setup scripts only instance it.
/// </summary>
public static class GameplayCameraPrefab
{
    public const string PrefabPath = "Assets/Prefabs/Camera/MainCamera.prefab";
    public const string GridPrefabPath = "Assets/Prefabs/Grid/GridSystem.prefab";
    public static readonly Vector3 DefaultOffset = new Vector3(-8f, 7f, -8f);
    public static readonly Color SolidBackground = new Color(0.19215687f, 0.3019608f, 0.4745098f, 0f);

    public static GameObject Load()
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    public static GameObject InstantiateLookingAt(
        Scene scene,
        Vector3 position,
        Vector3 lookAt,
        Transform followTarget,
        bool enableFollow,
        bool solidColorBackground)
    {
        GameObject prefab = Load();
        if (prefab == null)
            return null;

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.name = "Main Camera";
        go.transform.position = position;
        go.transform.LookAt(lookAt);
        ApplyInstanceOverrides(go, followTarget, enableFollow, solidColorBackground);
        return go;
    }

    public static void ApplyInstanceOverrides(
        GameObject cameraRoot,
        Transform followTarget,
        bool enableFollow,
        bool solidColorBackground)
    {
        CameraFollow follow = cameraRoot.GetComponent<CameraFollow>();
        if (follow != null)
        {
            follow.enabled = enableFollow;
            SerializedObject so = new SerializedObject(follow);
            so.FindProperty("target").objectReferenceValue = followTarget;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        if (solidColorBackground)
        {
            Camera cam = cameraRoot.GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = SolidBackground;
            }
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(cameraRoot);
        CameraFollow followRec = cameraRoot.GetComponent<CameraFollow>();
        if (followRec != null)
            PrefabUtility.RecordPrefabInstancePropertyModifications(followRec);
        Camera camRec = cameraRoot.GetComponent<Camera>();
        if (camRec != null)
            PrefabUtility.RecordPrefabInstancePropertyModifications(camRec);
    }

    public static void RevertAaToPrefab(GameObject cameraRoot)
    {
        if (cameraRoot == null || !PrefabUtility.IsPartOfPrefabInstance(cameraRoot))
            return;

        UniversalAdditionalCameraData urp = cameraRoot.GetComponent<UniversalAdditionalCameraData>();
        if (urp == null)
            return;

        SerializedObject so = new SerializedObject(urp);
        Revert(so, "m_Antialiasing");
        Revert(so, "m_AntialiasingQuality");
        Revert(so, "m_RenderPostProcessing");
        Revert(so, "m_RenderShadows");
    }

    static void Revert(SerializedObject so, string propertyPath)
    {
        SerializedProperty p = so.FindProperty(propertyPath);
        if (p != null)
            PrefabUtility.RevertPropertyOverride(p, InteractionMode.AutomatedAction);
    }
}
