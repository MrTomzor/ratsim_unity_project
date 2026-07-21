using UnityEngine;

//Just a parent object so that the AgentLoader.cs 
//script can handle swapping different camera following scripts

public class CameraFollowerParent : MonoBehaviour
{
    public GameObject target;
    public bool lockRotation = false; // If false, the camera rotates to match the agent's forward direction
    public bool lockHeight = false; // If false, the camera rotates to match the agent's forward direction
    public float rotationAngle = 0; // Fallback rotation around the Y-axis if rotation locking is enabled
    public float rotationAngle2 = 0; // Fallback rotation around the Y-axis if rotation locking is enabled
}
