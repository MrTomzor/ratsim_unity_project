using UnityEngine;

public class AgentPOVCameraFollower : CameraFollowerParent
{
    //public GameObject target;
    public Vector3 positionOffset = new Vector3(0f, 0.5f, 0.2f);
    //public bool lockRotation = false;
    //public float rotationAngle = 0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (target != null)
        {
            // Calculate the offset in world coordinates relative to the target's local orientation
            Vector3 worldOffset = target.transform.TransformDirection(positionOffset);
            transform.position = target.transform.position + worldOffset;

            if (!lockRotation)
            {
                // Follow the target's rotation so the camera looks in the direction the agent is facing
                transform.rotation = target.transform.rotation;
            }
            else
            {
                // Use a locked custom Y rotation (similar to TopdownCameraFollower)
                transform.rotation = Quaternion.Euler(0, rotationAngle, 0);
            }
        }
    }
}
