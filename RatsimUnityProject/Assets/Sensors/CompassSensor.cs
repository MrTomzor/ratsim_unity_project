using UnityEngine;

public class CompassSensor : MonoBehaviour
{
    public string topicName = "/compass";

    /// <summary>Latest heading in radians [-pi, pi], ROS frame. Read by visualizer.</summary>
    [HideInInspector] public float lastYawRad;

    RoslikeTCPServer conn;

    void Start()
    {
        conn = RoslikeTCPServer.GetInstance();
        conn.RegisterTimerDiscrete(SenseAndPublish, 1);
    }

    public void SenseAndPublish(TimerEvent ev)
    {
        float eulerYDeg = transform.rotation.eulerAngles.y;
        float yawRad = -eulerYDeg * Mathf.Deg2Rad;

        while (yawRad > Mathf.PI) yawRad -= 2f * Mathf.PI;
        while (yawRad < -Mathf.PI) yawRad += 2f * Mathf.PI;

        lastYawRad = yawRad;

        var msg = new Float32Message();
        msg.data = yawRad;
        conn.Publish(topicName, msg);
    }
}
