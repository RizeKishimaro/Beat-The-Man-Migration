using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Procedural bouncy jump onto/off a seat (tickets 115/116). Arc + squash/stretch
/// on Character_Root.
/// </summary>
[DefaultExecutionOrder(5)]
public class ProceduralSeatJumpMotion : MonoBehaviour
{
    [SerializeField] float duration = 0.45f;
    [SerializeField] float arcHeight = 0.55f;
    [SerializeField] float squashAmount = 0.22f;
    [SerializeField] float stretchAmount = 0.18f;

    CharacterController controller;
    ProceduralBounceMotion bounce;
    Transform visualRoot;
    Vector3 visualRestLocalPos;
    Vector3 visualRestLocalScale;
    Coroutine routine;
    bool playing;

    public bool IsPlaying => playing;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        CacheVisual();
    }

    void CacheVisual()
    {
        bounce = GetComponentInChildren<ProceduralBounceMotion>();
        visualRoot = bounce != null ? bounce.transform : null;
        if (visualRoot == null)
        {
            Transform named = FindDeepChild(transform, "Character_Root");
            visualRoot = named != null ? named : null;
        }

        if (visualRoot != null)
        {
            visualRestLocalPos = visualRoot.localPosition;
            visualRestLocalScale = visualRoot.localScale;
        }
    }

    /// <param name="enableControllerOnComplete">
    /// False for jump-on (stay seated at exact offset; CC would shove us back up).
    /// True for jump-off (restore walking).
    /// </param>
    public bool TryPlay(
        Vector3 endPos,
        Quaternion endRot,
        bool enableControllerOnComplete,
        Action onComplete = null)
    {
        if (playing)
            return false;
        if (visualRoot == null)
            CacheVisual();
        routine = StartCoroutine(
            JumpRoutine(endPos, endRot, enableControllerOnComplete, onComplete));
        return true;
    }

    IEnumerator JumpRoutine(
        Vector3 endPos,
        Quaternion endRot,
        bool enableControllerOnComplete,
        Action onComplete)
    {
        playing = true;

        if (bounce != null)
            bounce.enabled = false;

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        endRot = Quaternion.Euler(0f, endRot.eulerAngles.y, 0f);

        if (controller != null)
            controller.enabled = false;

        float t = 0f;
        float dur = Mathf.Max(0.05f, duration);
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float u = Mathf.Clamp01(t);
            float s = u * u * (3f - 2f * u);

            Vector3 pos = Vector3.Lerp(startPos, endPos, s);
            pos.y += Mathf.Sin(u * Mathf.PI) * arcHeight;
            transform.position = pos;
            transform.rotation = Quaternion.Slerp(startRot, endRot, s);

            ApplySquashStretch(u);
            yield return null;
        }

        transform.SetPositionAndRotation(endPos, endRot);
        ResetVisual();

        if (controller != null && enableControllerOnComplete)
        {
            controller.enabled = true;
            controller.Move(Vector3.zero);
        }

        if (bounce != null)
            bounce.enabled = true;

        playing = false;
        routine = null;
        onComplete?.Invoke();
    }

    void ApplySquashStretch(float u)
    {
        if (visualRoot == null)
            return;

        float apex = Mathf.Sin(u * Mathf.PI);
        float stretch = apex * stretchAmount;
        float squash = (1f - apex) * squashAmount;

        Vector3 scale = visualRestLocalScale;
        scale.y *= 1f - squash + stretch;
        scale.x *= 1f + squash * 0.5f - stretch * 0.25f;
        scale.z *= 1f + squash * 0.5f - stretch * 0.25f;

        float bob = apex * 0.06f;
        visualRoot.localPosition = visualRestLocalPos + Vector3.up * bob;
        visualRoot.localScale = scale;
    }

    void ResetVisual()
    {
        if (visualRoot == null)
            return;
        visualRoot.localPosition = visualRestLocalPos;
        visualRoot.localScale = visualRestLocalScale;
    }

    static Transform FindDeepChild(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }
}
