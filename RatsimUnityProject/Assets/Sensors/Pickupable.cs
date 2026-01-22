using UnityEngine;



public class Pickupable : MonoBehaviour
{
    public string topicName = "/pickupable";
    public int publishedNumber = 1;

    // Start is called once before the first execution of Update after the MonoBehaviour is created

    void OnTriggerEnter(Collider other)
    {
        RoslikeTCPServer conn = RoslikeTCPServer.GetInstance();
        conn.Publish(topicName, new Int32Message { data = publishedNumber });
        Destroy(gameObject);
    }
}
