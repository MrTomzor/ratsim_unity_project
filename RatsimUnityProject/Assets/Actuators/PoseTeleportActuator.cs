using System;
using UnityEngine;

public class PoseTeleportActuator : MonoBehaviour
{
    public string topic;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RoslikeTCPServer.GetInstance().Subscribe<Twist2DMessage>(topic, Teleport);
    }

    // Update is called once per frame
    void Update()
    {

    }

    void Teleport(Twist2DMessage msg)
    {
        Vector3 newpos = transform.position;
        newpos.x = -msg.left;
        newpos.z = msg.forward;
        transform.position = newpos;

        transform.rotation = Quaternion.Euler(0, -msg.radiansCounterClockwise * Mathf.Rad2Deg, 0);
    }
}
