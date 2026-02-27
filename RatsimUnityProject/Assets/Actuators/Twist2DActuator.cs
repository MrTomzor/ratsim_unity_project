using UnityEngine;

public class Twist2DActuator : MonoBehaviour
{
    RoslikeTCPServer conn;
    public float maxLinearVelocity = 10f;
    public float maxAngularVelocity = 5f;

    public float maxLinearAcceleration = 5f;
    public float maxAngularAcceleration = 2f;

    public string velCmdTopic = "/cmd_vel";
    public string accelCmdTopic = "/cmd_accel";

    public float accelModeLinearDrag = 0.2f;
    public float accelModeAngularDrag = 0.2f;
    public bool verbose = false;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        conn = RoslikeTCPServer.GetInstance();
        conn.Subscribe<TwistMessage>(velCmdTopic, OnVelTwistMessage);
        conn.Subscribe<TwistMessage>(accelCmdTopic, OnAccelTwistMessage);

    }

    public void OnVelTwistMessage(TwistMessage msg)
    {
        if(verbose)
            Debug.Log($"Received TwistMessage: linear_x={msg.linear_x}, linear_y={msg.linear_y}, angular_z={msg.angular_z}");
        // Apply the twist to the GameObject
        Vector3 forward = transform.forward * msg.linear_x;
        Vector3 left = -transform.right * msg.linear_y;
        Vector3 rotationRad = new Vector3(0, -msg.angular_z, 0);

        var rb = GetComponent<Rigidbody>();
        rb.linearVelocity = forward + left;
        rb.angularVelocity = rotationRad;
    }

    public void OnAccelTwistMessage(TwistMessage msg)
    {
        if(verbose)
            Debug.Log($"Received AccelTwistMessage: linear_x={msg.linear_x}, linear_y={msg.linear_y}, angular_z={msg.angular_z}");
        // Apply the twist to the GameObject
        Vector3 forward = transform.forward * msg.linear_x;
        Vector3 left = -transform.right * msg.linear_y;
        Vector3 rotationRad = new Vector3(0, -msg.angular_z, 0);

        var rb = GetComponent<Rigidbody>();

        float dt = conn.physicsStepTime;
        rb.linearVelocity += (forward + left) * dt;
        rb.angularVelocity += rotationRad * dt;

        // Apply drag to simulate acceleration control
        rb.linearVelocity *= (1.0f - accelModeLinearDrag);
        rb.angularVelocity *= (1.0f - accelModeAngularDrag);
    }

    // Update is called once per frame
    void Update()
    {

    }
}
