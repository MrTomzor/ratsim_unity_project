using UnityEngine;

public class AgentPOVCameraFollower : CameraFollowerParent
{
    //public GameObject target;
    public Vector3 positionOffset = new Vector3(0f, 0.5f, 0.2f);
    
    [Header("Mouse Look Settings")]
    [Range(0.1f, 10f)]
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f;
    private float verticalRotation = 0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    void Update()
    {
        if (!lockRotation)
        {
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
            verticalRotation -= mouseY;
            verticalRotation = Mathf.Clamp(verticalRotation, -maxLookAngle, maxLookAngle);
        }
    }

    // LateUpdate is called after all Update functions have been called
    void LateUpdate()
    {
        if (target != null)
        {
            // Calculate the offset in world coordinates relative to the target's local orientation
            Vector3 worldOffset = target.transform.TransformDirection(positionOffset);
            transform.position = target.transform.position + worldOffset;
            if (lockHeight)
                transform.position = new Vector3(transform.position.x, positionOffset.y, transform.position.z);

            if (!lockRotation)
            {
                // Follow the target's rotation so the camera looks in the direction the agent is facing,
                // and tilt up/down based on mouse movement.
                transform.rotation = target.transform.rotation * Quaternion.Euler(verticalRotation, 0, 0);
            }
            else
            {
                // Use a locked custom Y rotation (similar to TopdownCameraFollower)
                transform.rotation = Quaternion.Euler(rotationAngle2, rotationAngle, 0);
            }
        }
    }
}
