using UnityEngine;

public class Twist2DActuator : MonoBehaviour
{
    RoslikeTCPServer conn;
    public float maxLinearVelocity = 10f;
    public float maxAngularVelocity = 5f;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        conn = RoslikeTCPServer.GetInstance();
        conn.Subscribe<Twist2DMessage>("/cmd_vel", OnTwist2DMessage);

    }

    public void OnTwist2DMessage(Twist2DMessage msg)
    {
        // Apply the twist to the GameObject
        Vector3 forward = transform.forward * msg.forward;
        Vector3 left = -transform.right * msg.left;
        Vector3 rotationRad = new Vector3(0, -msg.radiansCounterClockwise, 0);

        var rb = GetComponent<Rigidbody>();
        rb.linearVelocity = forward + left;
        rb.angularVelocity = rotationRad;


    }

    // Update is called once per frame
    void Update()
    {

    }
}
