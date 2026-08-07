using UnityEngine;

/// <summary>
/// Toggles a 60 FPS cap on/off with a keypress.
/// Attach to any persistent GameObject in the scene.
/// </summary>
public class FPSLimiter : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Key to toggle the FPS limit")]
    public KeyCode toggleKey = KeyCode.F1;

    [Tooltip("Target frame rate when the limiter is active")]
    public int targetFPS = 60;

    public bool onByDefault = true;

    private bool isLimited = false;

    private void Start()
    {
        if (onByDefault)
        {
            isLimited = true;
            Application.targetFrameRate = targetFPS;
        } 
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            isLimited = !isLimited;
            Application.targetFrameRate = isLimited ? targetFPS : -1;
            Debug.Log($"FPS Limit: {(isLimited ? $"{targetFPS} FPS" : "Unlimited")}");
        }
    }
}
