using System;

public class ZmqMessageEnvelope
{
    public string topic { get; set; }
    public string type { get; set; }
    public Message data { get; set; }

    public ZmqMessageEnvelope(string topic, string type, Message data)
    {
        this.topic = topic;
        this.type = type;
        this.data = data;
    }
}

