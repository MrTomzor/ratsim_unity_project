using System;
using System.Collections.Generic;

public class ZmqMessageRegistry
{
    public static ZmqMessageRegistry Instance { get; } = new ZmqMessageRegistry();

    private ZmqMessageRegistry() { }

    public void RegisterMessageType(string typeName, Type type)
    {
        if (!messageTypeRegistry.ContainsKey(typeName))
        {
            messageTypeRegistry[typeName] = type;
        }
    }

    public static Type GetMessageType(string typeName)
    {
        return Instance.messageTypeRegistry.TryGetValue(typeName, out var type) ? type : null;
    }

    private Dictionary<string, Type> messageTypeRegistry = new Dictionary<string, Type>
    {
        { "Lidar2DMessage", typeof(Lidar2DMessage) },
        { "StringMessage", typeof(StringMessage) },
        { "Int32Message", typeof(Int32Message) },
        { "StepRequestMessage", typeof(StepRequestMessage) },
        { "StepFinishedMessage", typeof(StepFinishedMessage) },
        { "PoseMessage", typeof(PoseMessage) },
        { "TwistMessage", typeof(TwistMessage) },
        { "Float32Message", typeof(Float32Message) },
        { "BoolMessage", typeof(BoolMessage) },
        { "FloatArrayMessage", typeof(FloatArrayMessage) },
        { "VisualPointTrackerMessage", typeof(VisualPointTrackerMessage) },
        { "RGBDMessage", typeof(RGBDMessage) },
        { "CameraIntrinsicsMessage", typeof(CameraIntrinsicsMessage) },
        { "MapGenTemplate2D", typeof(MapGenTemplate2D) },
        { "Lidar3DMessage", typeof(Lidar3DMessage) },
        { "RawImageMessage", typeof(RawImageMessage) },
        { "RawLidarMessage", typeof(RawLidarMessage) },
    };
}

