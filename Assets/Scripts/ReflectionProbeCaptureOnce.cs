using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ticket 174 — one-shot Realtime probe capture. RefreshMode is ViaScripting,
/// so the probe never updates unless something calls RenderProbe().
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ReflectionProbe))]
public class ReflectionProbeCaptureOnce : MonoBehaviour
{
    void Start()
    {
        ReflectionProbe probe = GetComponent<ReflectionProbe>();
        if (probe == null || !probe.enabled)
            return;
        if (probe.mode != ReflectionProbeMode.Realtime)
            return;
        probe.RenderProbe();
    }
}
