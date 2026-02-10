using UnityEngine;

public class Odom2DSensor : MonoBehaviour
{
    public string topic;

    RoslikeTCPServer conn;
    Vector3 lastPos;
    Quaternion lastRot;
    public bool verbose = false;

    public float calculatedDT =0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        conn = RoslikeTCPServer.GetInstance();
        conn.RegisterTimerDiscrete(SenseAndPublish, 1);

        lastPos = transform.position;
        lastRot = transform.rotation;
    }

    public void SenseAndPublish(TimerEvent ev)
    {
        Quaternion deltaRot = Quaternion.Inverse(lastRot) * transform.rotation;

        Vector3 deltaVecAbsolute = transform.position - lastPos;
        Vector3 deltaVecInPrevFrame = Quaternion.Inverse(lastRot) * (transform.position - lastPos);

        lastPos = transform.position;
        lastRot = transform.rotation;



        var msg = new Twist2DMessage();
        msg.forward = deltaVecInPrevFrame.z;
        msg.left = -deltaVecInPrevFrame.x;
        msg.radiansCounterClockwise = -deltaRot.eulerAngles.y * Mathf.Deg2Rad;
        if (verbose)
        {
            float distTraveled = deltaVecInPrevFrame.magnitude;
            Debug.Log("odom data: forward=" + msg.forward + ", left=" + msg.left + ", radiansCCW=" + msg.radiansCounterClockwise);
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)            {
                Debug.Log("velocity: " + rb.linearVelocity + ", angularVelocity: " + rb.angularVelocity);
                float velocityMagnitude = rb.linearVelocity.magnitude;
                // output (debugging) calculated DT based on velocity and distance:
                if (velocityMagnitude > 0.001f)
                {
                    // debug traveled dist and velocity magnitudes
                    Debug.Log("distTraveled: " + distTraveled + ", velocityMagnitude: " + velocityMagnitude);
                    calculatedDT = distTraveled / velocityMagnitude;
                    float physicsDT = Time.fixedDeltaTime;
                    float unityDT = Time.deltaTime;
                    float serverDT = conn.physicsStepTime;
                    Debug.Log("calculatedDT: " + calculatedDT + " (physicsDT: " + physicsDT + ", unityDT: " + unityDT + ", serverDT: " + serverDT + ")");
                }
            }
        }
        conn.Publish(topic, msg);
    }
}
