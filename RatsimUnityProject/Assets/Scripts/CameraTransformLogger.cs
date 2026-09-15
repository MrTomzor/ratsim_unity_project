using UnityEngine;
using System.IO;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CameraTransformLogger : MonoBehaviour
{
    public enum LoggingMode
    {
        ManualSingleShot,
        Continuous
    }

    [Header("General Logging Settings")]
    public LoggingMode loggingMode = LoggingMode.ManualSingleShot;
    
    [Tooltip("Time interval in seconds between logs in Continuous mode.")]
    public float continuousInterval = 1f;

    [Tooltip("The path to the text file. Can be absolute or relative to the Unity project folder.")]
    public string logFilePath = "camera_log.txt";

    [Header("Circle Logging Options")]
    [Tooltip("If true, raycasts forward to find a pivot, and logs N positions in a circle around it.")]
    public bool logCircleAroundTarget = false;
    
    [Tooltip("Number of equidistant points to log around the circle.")]
    public int circlePointsCount = 8;

    [Tooltip("The starting angle in global space (0 = Global +Z).")]
    public float globalStartAngle = 0f;

    [Tooltip("Layers to consider for the raycast.")]
    public LayerMask raycastLayerMask = ~0;

    [Header("Gizmo Visualization")]
    [Tooltip("Show the circle and points in the Scene view.")]
    public bool showGizmo = true;
    
    [Tooltip("How often (in seconds) to update the raycast for the gizmo. 0 = every frame.")]
    public float gizmoUpdateFrequency = 0.1f;
    
    private float lastGizmoUpdateTime = -1f;
    private bool gizmoHitValid = false;
    private Vector3 gizmoPivot;
    private Vector3 gizmoCenter;
    private float gizmoRadius;
    
    private bool isContinuousRecording = false;
    private float continuousTimer = 0f;

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

        if (loggingMode == LoggingMode.ManualSingleShot)
        {
            if (enterPressed)
            {
                LogCameraData();
            }
        }
        else if (loggingMode == LoggingMode.Continuous)
        {
            if (enterPressed)
            {
                isContinuousRecording = !isContinuousRecording;
                Debug.Log($"[CameraTransformLogger] Continuous Recording: {(isContinuousRecording ? "STARTED" : "STOPPED")}");
                
                // Set timer to interval so it logs immediately upon starting
                if (isContinuousRecording)
                {
                    continuousTimer = continuousInterval;
                }
            }

            if (isContinuousRecording)
            {
                continuousTimer += Time.deltaTime;
                if (continuousTimer >= continuousInterval)
                {
                    continuousTimer = 0f;
                    LogCameraData();
                }
            }
        }
    }

    private void LogCameraData()
    {
        string logContent = "";
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (logCircleAroundTarget)
        {
            if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, Mathf.Infinity, raycastLayerMask))
            {
                Vector3 pivot = hit.point;
                Vector3 originalPos = transform.position;
                
                // The center of the circle on the XZ plane at the camera's original height
                Vector3 circleCenter = new Vector3(pivot.x, originalPos.y, pivot.z);
                
                // The radius of the circle
                float radius = (originalPos - circleCenter).magnitude;
                
                // Base vector aligned with global +Z axis
                Vector3 baseVector = Vector3.forward * radius;

                for (int i = 0; i < circlePointsCount; i++)
                {
                    float angle = globalStartAngle + (i * (360f / circlePointsCount));
                    Quaternion yRotation = Quaternion.Euler(0, angle, 0);
                    
                    // Rotate the base vector to get the offset from the center
                    Vector3 newPos = circleCenter + (yRotation * baseVector);
                    
                    // Camera looks directly at the pivot point
                    Vector3 lookDirection = pivot - newPos;
                    Vector3 newRot = lookDirection != Vector3.zero ? Quaternion.LookRotation(lookDirection).eulerAngles : Vector3.zero;

                    logContent += $"[{timestamp}] Position: {newPos.ToString("F3")} | Rotation (Euler): {newRot.ToString("F3")}\n";
                }
            }
            else
            {
                Debug.LogWarning("[CameraTransformLogger] Raycast didn't hit anything. Cannot log circle poses.");
                return;
            }
        }
        else
        {
            logContent += $"[{timestamp}] Position: {transform.position.ToString("F3")} | Rotation (Euler): {transform.eulerAngles.ToString("F3")}\n";
        }

        try
        {
            File.AppendAllText(logFilePath, logContent);
            int count = logCircleAroundTarget ? circlePointsCount : 1;
            Debug.Log($"[CameraTransformLogger] Successfully appended {count} pose(s) to {logFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[CameraTransformLogger] Failed to write to file: {e.Message}");
        }
    }

    private void OnDrawGizmos()
    {
        if (!showGizmo || !logCircleAroundTarget) return;

        // Use realtimeSinceStartup to work in both Edit and Play mode
        if (Time.realtimeSinceStartup - lastGizmoUpdateTime >= gizmoUpdateFrequency || gizmoUpdateFrequency <= 0f)
        {
            UpdateGizmoData();
            lastGizmoUpdateTime = Time.realtimeSinceStartup;
        }

        if (gizmoHitValid)
        {
            // Draw center and pivot line
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(gizmoPivot, gizmoCenter);
            Gizmos.DrawSphere(gizmoCenter, 0.05f);

            // Draw pivot point
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(gizmoPivot, 0.1f);

            // Draw circle points and outline
            Vector3 baseVector = Vector3.forward * gizmoRadius;
            Vector3 prevPoint = Vector3.zero;

            for (int i = 0; i <= circlePointsCount; i++) // <= to close the circle loop
            {
                float angle = globalStartAngle + ((i % circlePointsCount) * (360f / circlePointsCount));
                Quaternion yRotation = Quaternion.Euler(0, angle, 0);
                Vector3 pointPos = gizmoCenter + (yRotation * baseVector);
                
                if (i < circlePointsCount)
                {
                    // Draw point
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawSphere(pointPos, 0.1f);
                    
                    // Draw look direction (blue line)
                    Gizmos.color = Color.blue;
                    Vector3 lookDir = gizmoPivot - pointPos;
                    if (lookDir != Vector3.zero)
                    {
                        Gizmos.DrawRay(pointPos, lookDir.normalized * (gizmoRadius * 0.2f + 0.1f)); 
                    }
                }
                
                // Draw circle outline
                if (i > 0)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(prevPoint, pointPos);
                }
                prevPoint = pointPos;
            }
        }
    }

    private void UpdateGizmoData()
    {
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, Mathf.Infinity, raycastLayerMask))
        {
            gizmoHitValid = true;
            gizmoPivot = hit.point;
            gizmoCenter = new Vector3(gizmoPivot.x, transform.position.y, gizmoPivot.z);
            gizmoRadius = (transform.position - gizmoCenter).magnitude;
        }
        else
        {
            gizmoHitValid = false;
        }
    }
}
