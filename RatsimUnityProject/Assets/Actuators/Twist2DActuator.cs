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


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        conn = RoslikeTCPServer.GetInstance();
        conn.Subscribe<Twist2DMessage>(velCmdTopic, OnVelTwist2DMessage);
        conn.Subscribe<Twist2DMessage>(accelCmdTopic, OnAccelTwist2DMessage);

    }

    public void OnVelTwist2DMessage(Twist2DMessage msg)
    {
        // Apply the twist to the GameObject
        Vector3 forward = transform.forward * msg.forward;
        Vector3 left = -transform.right * msg.left;
        Vector3 rotationRad = new Vector3(0, -msg.radiansCounterClockwise, 0);

        var rb = GetComponent<Rigidbody>();
        rb.linearVelocity = forward + left;
        rb.angularVelocity = rotationRad;
    }

    public void OnAccelTwist2DMessage(Twist2DMessage msg)
    {
        // Apply the twist to the GameObject
        Vector3 forward = transform.forward * msg.forward;
        Vector3 left = -transform.right * msg.left;
        Vector3 rotationRad = new Vector3(0, -msg.radiansCounterClockwise, 0);

        var rb = GetComponent<Rigidbody>();
        rb.linearVelocity = forward + left;
        rb.angularVelocity = rotationRad;

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
