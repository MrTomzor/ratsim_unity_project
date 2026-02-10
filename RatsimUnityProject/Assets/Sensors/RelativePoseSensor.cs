using UnityEngine;

public class RelativePoseSensor : MonoBehaviour
{
    public string topic;

    public bool considerOriginRotation = false;

    RoslikeTCPServer conn;

    public Vector3 originPos;
    public Quaternion originRot;

    public void ResetOrigin()
    {
        originPos = transform.position;
        originRot = transform.rotation;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        conn = RoslikeTCPServer.GetInstance();
        conn.RegisterTimerDiscrete(SenseAndPublish, 1);
    }

    public void SenseAndPublish(TimerEvent ev)
    {
        var msg = new Twist2DMessage();

        if(considerOriginRotation)
        {
             Vector3 deltaVecInOrigFrame = Quaternion.Inverse(originRot) * (transform.position - originPos);
            // get rotation relative to origin
            //Quaternion relativeRot = Quaternion.Inverse(originRot) * transform.rotation;
            //msg.radiansCounterClockwise = -relativeRot.eulerAngles.y * Mathf.Deg2Rad;
        }
        else
        {
            msg.forward = (transform.position.z - originPos.z);
            msg.left = -(transform.position.x - originPos.x);
            msg.radiansCounterClockwise = -transform.rotation.eulerAngles.y * Mathf.Deg2Rad;
        }
        
        conn.Publish(topic, msg);
    }
}
