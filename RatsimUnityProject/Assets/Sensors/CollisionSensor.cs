using System.Collections.Generic;
using UnityEngine;

public class CollisionSensor : MonoBehaviour
{

    RoslikeTCPServer conn;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public bool hasCollided = false;
    public float collisionVel = 0;
    public string outTopic = "/collisions";

    public List<string> ignoreTags = new List<string> { "reward" };
    
    void Start()
    {
        conn = RoslikeTCPServer.GetInstance();
        conn.RegisterTimerDiscrete(MainTimer, 1);
    }

    public void MainTimer(TimerEvent ev)
    {
        if (hasCollided)
        {
            Float32Message msg = new Float32Message();
            msg.data = collisionVel;
            conn.Publish(outTopic, msg);
            hasCollided = false;
            collisionVel = 0;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (ignoreTags.Contains(collision.gameObject.tag))
        {
            return;
        }
        hasCollided = true;
        // save max magnitude incase of many cols
        collisionVel = Mathf.Max(collisionVel, collision.relativeVelocity.magnitude);
        
    }
}