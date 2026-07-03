using UnityEngine;

/// <summary>
/// Caps frame rate and enables VSync to prevent screen tearing.
/// Attach to a persistent GameObject in the scene.
/// </summary>
public class FrameRateLimiter : MonoBehaviour
{
    [Header("Frame Rate")]
    [Tooltip("Target frame rate. Ignored when VSync is enabled, but acts as fallback.")]
    [Range(15, 120)]
    public int targetFPS = 60;

    [Header("VSync")]
    [Tooltip("0 = Off (may tear), 1 = Every VBlank (recommended), 2 = Every 2nd VBlank (half refresh rate)")]
    [Range(0, 2)]
    public int vSyncCount = 1;

    void Awake()
    {
        Apply();
    }

    /// <summary>
    /// Re-apply settings at runtime if Inspector values change.
    /// </summary>
    void OnValidate()
    {
        if (Application.isPlaying)
            Apply();
    }

    void Apply()
    {
        QualitySettings.vSyncCount = vSyncCount;
        Application.targetFrameRate = targetFPS;
    }
}
