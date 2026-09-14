using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ticket 182 — build-time twin of GameplaySceneGuard (120). The editor guard
/// only fires on scene open / Play; this runs the same checks when a player
/// build starts.
///
/// Two lists are involved and they are deliberately separate:
///  - GUARDED scenes = GameplaySceneGuard.RequiredScenePaths (Alpha01,
///    GridFoundation, RoomsWorkshop, the GameplayBasics template). Every one
///    of them is validated from disk on every build.
///  - SHIP list = the enabled scenes in Build Profiles > Scene List
///    (EditorBuildSettings.scenes). Today: Alpha01, GridFoundation.
/// A broken guarded scene that SHIPS fails the build (BuildFailedException).
/// A broken guarded scene that does not ship (test/template scenes) is
/// reported as a warning and the build continues — a broken disposable
/// RoomsWorkshop must not block an Alpha01 build. A warning also fires if no
/// guarded scene ships at all (a scratch-only scene list).
///
/// It runs in OnPreprocessBuild, before the pipeline touches any scene: each
/// guarded scene is opened additively from disk (or checked in place if
/// already open), validated, and closed again. Deliberately NOT an
/// IProcessSceneWithReport: verified 2026-09-13 with a real StandaloneOSX
/// build that the scene copy handed to OnProcessScene has its prefab
/// connections stripped (PrefabUtility reports '' for the camera and grid
/// instances), so the prefab-lineage checks can never pass there.
///
/// Uses GameplaySceneGuard.RunChecks, not ValidateScene: the editor-side
/// bypasses (Suspended, mid-bake flag file) must never let a build through.
/// Menu: Tools/Grid/Validate Guarded Scenes (build check) runs the same pass
/// on demand without building, with the same shipping/non-shipping split.
/// </summary>
public class GameplaySceneBuildGuard : IPreprocessBuildWithReport
{
    const string Tag = "[GameplaySceneBuildGuard]";

    public sealed class Result
    {
        public int Checked;
        public int Shipping;
        public readonly List<string> ShippingFailures = new List<string>();
        public readonly List<string> NonShippingFailures = new List<string>();
    }

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        WarnIfNoGuardedSceneInBuild();

        Result r = ValidateGuardedScenesOnDisk();
        if (r.NonShippingFailures.Count > 0)
            Debug.LogWarning(NonShippingMessage(r));
        if (r.ShippingFailures.Count > 0)
            throw new BuildFailedException(FailureMessage(r));

        Debug.Log(OkMessage(r, "validated before build"));
    }

    [MenuItem("Tools/Grid/Validate Guarded Scenes (build check)")]
    public static void MenuValidate()
    {
        WarnIfNoGuardedSceneInBuild();
        Result r = ValidateGuardedScenesOnDisk();
        if (r.NonShippingFailures.Count > 0)
            Debug.LogWarning(NonShippingMessage(r));
        if (r.ShippingFailures.Count > 0)
            Debug.LogError(FailureMessage(r));
        else
            Debug.Log(OkMessage(r, "pass the build check"));
    }

    /// <summary>
    /// Runs the guard checks against every scene in
    /// GameplaySceneGuard.RequiredScenePaths as saved on disk, sorting
    /// failures by whether the scene is in the build's enabled scene list.
    /// Scenes already open in the editor are checked in place and left open
    /// (a warning notes when that copy has unsaved changes, since the build
    /// bakes the saved file); the rest are opened additively and closed
    /// without saving.
    /// </summary>
    public static Result ValidateGuardedScenesOnDisk()
    {
        var r = new Result();
        HashSet<string> ship = EnabledBuildScenes();

        foreach (string path in GameplaySceneGuard.RequiredScenePaths)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning(Tag + " guarded scene not on disk, skipped: " + path);
                continue;
            }

            bool ships = ship.Contains(path);
            Scene scene = SceneManager.GetSceneByPath(path);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (wasLoaded && scene.isDirty)
                Debug.LogWarning(Tag + " " + path + " is open with unsaved changes — validating the in-memory copy; save it so the build bakes what was checked.");
            if (!wasLoaded)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            try
            {
                var sb = new StringBuilder();
                if (!GameplaySceneGuard.RunChecks(scene, log: false, sb))
                    (ships ? r.ShippingFailures : r.NonShippingFailures).Add(sb.ToString().TrimEnd());
                r.Checked++;
                if (ships)
                    r.Shipping++;
            }
            finally
            {
                if (!wasLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        return r;
    }

    public static HashSet<string> EnabledBuildScenes()
    {
        var set = new HashSet<string>();
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (s.enabled)
                set.Add(s.path);
        }
        return set;
    }

    static void WarnIfNoGuardedSceneInBuild()
    {
        var enabled = new List<string>();
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (!s.enabled)
                continue;
            enabled.Add(s.path);
            if (GameplaySceneGuard.IsRequiredScene(s.path))
                return;
        }
        Debug.LogWarning(Tag + " no guarded gameplay scene (Alpha01/GridFoundation/RoomsWorkshop) is in the build's scene list — "
            + "the build ships only: " + (enabled.Count == 0 ? "(none)" : string.Join(", ", enabled))
            + ". Guarded scenes are still validated from disk, but check File > Build Profiles > Scene List.");
    }

    static string OkMessage(Result r, string verb)
    {
        return Tag + " OK — " + r.Checked + " guarded scene(s) " + verb + " (" + r.Shipping + " in the build's scene list).";
    }

    static string FailureMessage(Result r)
    {
        var sb = new StringBuilder();
        sb.Append(Tag).Append(" BUILD FAILED — ").Append(r.ShippingFailures.Count)
          .AppendLine(" guarded scene(s) in the build's scene list failed validation. Fix the scene(s) below (or open them in the editor for the same report), then build again:");
        for (int i = 0; i < r.ShippingFailures.Count; i++)
            sb.AppendLine(r.ShippingFailures[i]);
        return sb.ToString().TrimEnd();
    }

    static string NonShippingMessage(Result r)
    {
        var sb = new StringBuilder();
        sb.Append(Tag).Append(" WARNING — ").Append(r.NonShippingFailures.Count)
          .AppendLine(" guarded scene(s) NOT in the build's scene list failed validation. The build continues because they do not ship; fix them before relying on them:");
        for (int i = 0; i < r.NonShippingFailures.Count; i++)
            sb.AppendLine(r.NonShippingFailures[i]);
        return sb.ToString().TrimEnd();
    }
}
