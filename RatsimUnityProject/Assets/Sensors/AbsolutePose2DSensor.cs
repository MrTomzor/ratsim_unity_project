using UnityEngine;

public class AbsolutePose2DSensor : MonoBehaviour
{
    public string topic;

    RoslikeTCPServer conn;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        conn = RoslikeTCPServer.GetInstance();
        conn.RegisterTimerDiscrete(SenseAndPublish, 1);
    }

    public void SenseAndPublish(TimerEvent ev)
    {
        var msg = new Twist2DMessage();
        msg.forward = transform.position.z;
        msg.left = -transform.position.x;
        msg.radiansCounterClockwise = -transform.rotation.eulerAngles.y * Mathf.Deg2Rad;
        conn.Publish(topic, msg);
    }
}
