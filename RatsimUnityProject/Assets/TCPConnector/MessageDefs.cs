using UnityEngine;

public class Message
{

}

public class StepRequestMessage : Message
{
    public bool physicsEnabled { get; set; }
}

public class StepFinishedMessage : Message
{
    public bool success { get; set; }
}

public class StringMessage : Message
{
    public string data { get; set; }
}

public class Int32Message : Message
{
    public int data { get; set; }
}

public class Float32Message : Message
{
    public float data { get; set; }
}

public class BoolMessage : Message
{
    public bool data { get; set; }
}

public class Lidar2DMessage : Message
{
    public float[] ranges { get; set; }
    public float[] descriptors { get; set; }
    public int angleIncrementDeg { get; set; }
    public int angleStartDeg { get; set; }
    public float maxRange { get; set; }
}

public class VisualPointTrackerMessage : Message
{
    public float[] trackedPointsEgocentricFLU { get; set; }
    public float[] trackedPointDescriptors { get; set; }
    public float scaleFactor { get; set; }
}

public class Twist2DMessage : Message
{
    public float forward { get; set; }
    public float left { get; set; }
    public float radiansCounterClockwise { get; set; }
}

public class RGBDMessage : Message
{
    public string rgbImageBase64 { get; set; }
    public string depthImageBase64 { get; set; }
    public float minDepth { get; set; }
    public float maxDepth { get; set; }
}

public class CameraIntrinsicsMessage : Message
{
    public int imageWidth { get; set; }
    public int imageHeight { get; set; }
    
    public float fx { get; set; }  // focal length in pixels (x-axis)
    public float fy { get; set; }  // focal length in pixels (y-axis)
    public float cx { get; set; }  // principal point x
    public float cy { get; set; }  // principal point y

    public float nearClip { get; set; }
    public float farClip { get; set; }
    public float verticalFOV { get; set; }
}

public class MapGenTemplate2D : Message
{
    // Dimensions of the masks
    public int width{ get; set; }
    public int height{ get; set; }

    // Meters per pixel scale
    public float meters_per_pixel{ get; set; }  

    // Flattened binary masks: 1 = true, 0 = false
    public int[] obstacles{ get; set; }
    public int[] spawnMask{ get; set; }
    public int[] poiMask{ get; set; }
    public int[] forbiddenMask{ get; set; }
    public int[] growableMask{ get; set; }
}

public class WildfireWorldGenMessage : Message
{
    // Seed for generating
    public int seed{ get; set; }
    public int numAgents{ get; set; }
    public float startAndGoalClearingDistance{ get; set; }
    public int arenaWidth{ get; set; }
    public int arenaHeight{ get; set; }
    public float treeDensity{ get; set; }
    public string topology{ get; set; }
    public float treesSwayingFactor{ get; set; }
    public float debrisTriggerzoneSpawnFrequency{ get; set; }
    public float debrisGroupSizeModifier{ get; set; }
    public float carRoadSpawnFrequency{ get; set; }
    public float carVelocityMin{ get; set; }
    public float carVelocityMax{ get; set; }
    public float fireSpawnFrequency{ get; set; }
    public float fireGlobalSpreadModifier{ get; set; }
    public float fireSmokeGenerationModifier{ get; set; }
    public bool fireSpreadsAcrossGround{ get; set; }
    public float staticWindXVel{ get; set; }
    public float staticWindYVel{ get; set; }
    public float windFluctuationModifier{ get; set; }

}