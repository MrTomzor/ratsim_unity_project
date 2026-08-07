using UnityEngine;
using System.IO;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CameraTransformLogger : MonoBehaviour
{
    [Tooltip("The path to the text file. Can be absolute or relative to the Unity project folder.")]
    public string logFilePath = "camera_log.txt";

    void Update()
    {
        bool enterPressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
        {
            enterPressed = true;
        }
#else
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            enterPressed = true;
        }
#endif

        if (enterPressed)
        {
            LogCameraData();
        }
    }

    private void LogCameraData()
    {
        try
        {
            // Get current timestamp
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            
            // Format position and rotation
            Vector3 pos = transform.position;
            Vector3 rot = transform.eulerAngles;
            
            string logLine = $"[{timestamp}] Position: {pos.ToString("F3")} | Rotation (Euler): {rot.ToString("F3")}\n";
            
            // Append to file (creates the file if it does not exist)
            File.AppendAllText(logFilePath, logLine);
            
            Debug.Log($"[CameraTransformLogger] Successfully appended to {logFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[CameraTransformLogger] Failed to write to file: {e.Message}");
        }
    }
}
