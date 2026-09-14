using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 106 step 2 — the shared two-light rig for gameplay scenes, so the
/// scene setup scripts, the Gameplay Basics template and the retrofit all bake
/// the same thing (same idea as GameplayCameraPrefab for the camera).
///
/// Values are the ones proven on GridFoundation in the 2026-08-05 pass:
/// warm soft-shadowed key at 0.75 shadow strength (full-strength shadows read
/// as harsh black on the stylised look) plus a cool shadowless fill at ~0.23×
/// the key, so shadow sides lift and pick up a slight colour contrast.
///
/// EnsureRig retrofits an existing scene and deliberately keeps the key's
/// intensity/colour/rotation — Alpha01 was hand-tuned to 0.95 (commit
/// 920175bd) and that exposure choice stays; the fill scales with it.
/// Menu: Tools/Grid/Render Quality/2b Ensure lighting rig (active scene).
/// </summary>
public static class GameplayLightingRig
{
    public const string KeyName = "Directional Light";
    public const string FillName = "Fill Light";

    public const float DefaultKeyIntensity = 1.55f;
    public const float KeyShadowStrength = 0.75f;
    /// <summary>GridFoundation's 0.35 fill against its 1.55 key.</summary>
    public const float FillToKeyRatio = 0.35f / 1.55f;

    public static readonly Color KeyColor = new Color(1f, 0.97f, 0.92f);
    public static readonly Color FillColor = new Color(0.75f, 0.85f, 1f);
    public static readonly Quaternion KeyRotation = Quaternion.Euler(50f, -30f, 0f);
    public static readonly Quaternion FillRotation = Quaternion.Euler(25f, -140f, 0f);

    /// <summary>Fresh scene (setup scripts / template): creates key + fill.</summary>
    public static Light CreateRig(Scene scene, float keyIntensity, StringBuilder sb = null)
    {
        Light key = CreateDirectional(scene, KeyName, KeyColor, keyIntensity, KeyRotation);
        key.shadows = LightShadows.Soft;
        key.shadowStrength = KeyShadowStrength;

        float fillIntensity = FillIntensityFor(keyIntensity);
        Light fill = CreateDirectional(scene, FillName, FillColor, fillIntensity, FillRotation);
        fill.shadows = LightShadows.None;

        if (sb != null)
            sb.AppendLine("lighting rig: key " + keyIntensity + " soft/" + KeyShadowStrength + " + fill " + fillIntensity);
        return key;
    }

    /// <summary>
    /// Existing scene: soften the key's shadows and add the fill if missing.
    /// Returns true if anything changed. Does not save.
    /// </summary>
    public static bool EnsureRig(Scene scene, StringBuilder sb = null)
    {
        Light key = FindKey(scene);
        if (key == null)
        {
            if (sb != null)
                sb.AppendLine("lighting rig: no directional light in " + scene.path + " — nothing done");
            return false;
        }

        bool changed = false;
        if (key.shadows != LightShadows.Soft)
        {
            key.shadows = LightShadows.Soft;
            changed = true;
        }
        if (!Mathf.Approximately(key.shadowStrength, KeyShadowStrength))
        {
            key.shadowStrength = KeyShadowStrength;
            changed = true;
        }
        if (key.GetComponent<UniversalAdditionalLightData>() == null)
        {
            key.gameObject.AddComponent<UniversalAdditionalLightData>();
            changed = true;
        }
        if (changed)
            EditorUtility.SetDirty(key);

        Light fill = FindByName(scene, FillName);
        if (fill == null)
        {
            fill = CreateDirectional(scene, FillName, FillColor, FillIntensityFor(key.intensity), FillRotation);
            fill.shadows = LightShadows.None;
            changed = true;
        }

        if (sb != null)
            sb.AppendLine("lighting rig: key '" + key.name + "' " + key.intensity + " " + key.shadows + "/" + key.shadowStrength
                + " + fill " + fill.intensity + (changed ? " (changed)" : " (already in place)"));
        return changed;
    }

    public static float FillIntensityFor(float keyIntensity)
    {
        return Mathf.Round(keyIntensity * FillToKeyRatio * 100f) / 100f;
    }

    [MenuItem("Tools/Grid/Render Quality/2b Ensure lighting rig (active scene)")]
    static void MenuEnsureActive()
    {
        Scene scene = SceneManager.GetActiveScene();
        var sb = new StringBuilder();
        if (EnsureRig(scene, sb))
            EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[GameplayLightingRig] " + scene.path + "\n" + sb);
    }

    static Light CreateDirectional(Scene scene, string name, Color color, float intensity, Quaternion rotation)
    {
        GameObject go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.rotation = rotation;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        go.AddComponent<UniversalAdditionalLightData>();
        return light;
    }

    static Light FindKey(Scene scene)
    {
        Light named = FindByName(scene, KeyName);
        if (named != null && named.type == LightType.Directional)
            return named;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (l.gameObject.scene == scene && l.type == LightType.Directional && l.gameObject.name != FillName)
                return l;
        }
        return null;
    }

    static Light FindByName(Scene scene, string name)
    {
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l.gameObject.scene == scene && l.gameObject.name == name)
                return l;
        }
        return null;
    }
}
