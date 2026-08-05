using UnityEngine;

/// <summary>
/// Confirmed empirically for the Universal Grid System ticket (see the
/// reference asset at Assets/_GridVerification/GridAxisTestCube.fbx and its
/// documented export recipe): exporting from Blender with
/// axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', and
/// bake_space_transform=True produces a correct 1:1 scale, zero-rotation
/// import — but the conversion mirrors both horizontal axes. This is an
/// unavoidable consequence of Blender being right-handed and Unity being
/// left-handed; the flip has to land somewhere.
///
///   Blender +X                       -> Unity -X
///   Blender +Y (ground-plane forward) -> Unity -Z
///   Blender +Z (up)                   -> Unity +Y
///
/// This is the single, centralized place that quirk is handled. Nothing
/// else in the project (artists, importers, other tools) should need to
/// re-derive or manually reapply it — GridSystem's Blender-facing helpers
/// call through to this automatically.
/// </summary>
public static class BlenderUnityAxisConversion
{
    public static Vector3 BlenderToUnity(Vector3 blenderPosition)
    {
        return new Vector3(-blenderPosition.x, blenderPosition.z, -blenderPosition.y);
    }

    public static Vector3 UnityToBlender(Vector3 unityPosition)
    {
        return new Vector3(-unityPosition.x, -unityPosition.z, unityPosition.y);
    }
}
