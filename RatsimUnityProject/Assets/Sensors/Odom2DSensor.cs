using UnityEngine;

public class Odom2DSensor : MonoBehaviour
{
    public string topic;

    RoslikeTCPServer conn;
    Vector3 lastPos;
    Quaternion lastRot;
    public bool verbose = false;

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
            Debug.Log("odom data: forward=" + msg.forward + ", left=" + msg.left + ", radiansCCW=" + msg.radiansCounterClockwise);
        }
        conn.Publish(topic, msg);
    }
}
