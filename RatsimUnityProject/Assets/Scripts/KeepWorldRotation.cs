using UnityEngine;

/// <summary>
/// Keeps the GameObject's world rotation constant, ignoring any rotation changes from its parent.
/// </summary>
public class KeepWorldRotation : MonoBehaviour
{
    public enum UpdateMode
    {
        Update,
        LateUpdate,
        FixedUpdate
    }

    [Header("Rotation Settings")]
    [Tooltip("If true, the object will keep the rotation it had when the scene started. Otherwise, it will use the Target Rotation below.")]
    [SerializeField] private bool useInitialRotation = true;

    [Tooltip("The world-space rotation to maintain, specified in Euler angles. Only used if Use Initial Rotation is false.")]
    [SerializeField] private Vector3 targetRotationEuler = Vector3.zero;

    [Header("Timing Settings")]
    [Tooltip("When to apply the rotation lock. LateUpdate is recommended to prevent jitter if parents are animated or moved in Update.")]
    [SerializeField] private UpdateMode updateTime = UpdateMode.LateUpdate;

    private Quaternion targetRotation;

    private void Start()
    {
        // Store the target rotation
        if (useInitialRotation)
        {
            targetRotation = transform.rotation;
        }
        else
        {
            targetRotation = Quaternion.Euler(targetRotationEuler);
        }
    }

    private void Update()
    {
        if (updateTime == UpdateMode.Update)
        {
            ApplyRotation();
        }
    }

    private void LateUpdate()
    {
        if (updateTime == UpdateMode.LateUpdate)
        {
            ApplyRotation();
        }
    }

    private void FixedUpdate()
    {
        if (updateTime == UpdateMode.FixedUpdate)
        {
            ApplyRotation();
        }
    }

    /// <summary>
    /// Forcefully resets the object's world rotation to the desired rotation.
    /// </summary>
    private void ApplyRotation()
    {
        transform.rotation = targetRotation;
    }

    /// <summary>
    /// Allows updating the locked rotation dynamically at runtime.
    /// </summary>
    /// <param name="newRotation">The new world-space rotation to maintain.</param>
    public void SetTargetRotation(Quaternion newRotation)
    {
        targetRotation = newRotation;
        targetRotationEuler = newRotation.eulerAngles;
    }

    /// <summary>
    /// Allows updating the locked rotation dynamically at runtime using Euler angles.
    /// </summary>
    /// <param name="newRotationEuler">The new world-space rotation in Euler angles.</param>
    public void SetTargetRotation(Vector3 newRotationEuler)
    {
        targetRotationEuler = newRotationEuler;
        targetRotation = Quaternion.Euler(newRotationEuler);
    }
}
