using System;

public class RawImageMessage : Message
{
    public int width { get; set; }
    public int height { get; set; }
    public string format { get; set; } // e.g. "RGB24", "RGBA32", "RFloat"
    public int channels { get; set; } // 3 for RGB, 4 for RGBA, 1 for Depth
    public float minDepth { get; set; }
    public float maxDepth { get; set; }
    public int binaryIndex { get; set; } = -1;
    public int depthBinaryIndex { get; set; } = -1;
}

public class RawLidarMessage : Message
{
    public int numRays { get; set; }
    public int descriptorDimension { get; set; }
    public float horizontalFovStart { get; set; }
    public float horizontalFovEnd { get; set; }
    public float verticalFovStart { get; set; }
    public float verticalFovEnd { get; set; }
    public float maxRange { get; set; }
    public int rangesBinaryIndex { get; set; } = -1;
    public int descriptorsBinaryIndex { get; set; } = -1;
}

