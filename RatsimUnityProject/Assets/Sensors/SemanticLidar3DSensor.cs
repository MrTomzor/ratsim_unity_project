using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;

public class SemanticLidar3DSensor : MonoBehaviour
{
    public SemanticSet activeSemanticSet;
    public string semanticSet = "full_semantic_set";

    [Header("FOV and Resolution")]
    public float horizontalFovStartDeg = -180f;
    public float horizontalFovEndDeg = 180f;
    public int numRaysHorizontal = 360; 

    public float verticalFovStartDeg = -15f;
    public float verticalFovEndDeg = 15f;
    public int numRaysVertical = 30; 

    public float maxRange = 100f;
    public static uint descriptorDimension = 3;
    public string topicName = "/lidar3d";

    [HideInInspector] public float[] lastRanges;
    [HideInInspector] public float[] lastDescriptors;

    public bool debugDrawRays = false;
    public bool verbose = false;

    [Header("Network")]
    [Tooltip("If true, the sensor will publish the pointcloud to Python/ROS.")]
    public bool sendMessageToPython = true;
    public bool useRawBinary = true;
    private byte[] rangesByteBuffer;
    private byte[] descByteBuffer;

    [Header("Faults")]
    public string occlusionRegion = "none";
    public float occlusionDistance = 0.1f;

    [Header("Volumetric Smoke")]
    public bool enableVolumetricSmoke = true;
    public SmokeDensitySampler smokeSampler;
    public int smokeRaymarchSteps = 16;

    ZmqUnityServer conn;

    void InitializeSemanticSetData()
    {
        SemanticLidarSensor.semanticNamesToIndices = new Hashtable();

        foreach(GameObject obj in activeSemanticSet.prefabs)
        {
            NamedSemanticObject namedSemanticObject = obj.GetComponentInChildren<NamedSemanticObject>();
            if (namedSemanticObject != null)
            {
                if (!SemanticLidarSensor.semanticNamesToIndices.ContainsKey(namedSemanticObject.semanticName))
                {
                    SemanticLidarSensor.semanticNamesToIndices[namedSemanticObject.semanticName] = SemanticLidarSensor.semanticNamesToIndices.Count;
                }
            }
        }

        SemanticLidarSensor.descriptorDimension = (uint)SemanticLidarSensor.semanticNamesToIndices.Count;
        descriptorDimension = SemanticLidarSensor.descriptorDimension;
        Debug.Log("3D Lidar Initialized Semantic Set with " + descriptorDimension + " semantic classes.");
    }

    void Start()
    {
        if (!string.IsNullOrEmpty(semanticSet))
        {
            SemanticSet loaded = Resources.Load<SemanticSet>("SemanticSets/" + semanticSet);
            if (loaded != null)
            {
                activeSemanticSet = loaded;
            }
        }

        if (activeSemanticSet != null)
        {
            InitializeSemanticSetData();
        }

        if (enableVolumetricSmoke && smokeSampler == null)
        {
            // Auto-find in scene since the sensor might be instantiated from a prefab
            smokeSampler = FindAnyObjectByType<SmokeDensitySampler>();
            if (smokeSampler == null)
            {
                Debug.LogWarning("SemanticLidar3DSensor: enableVolumetricSmoke is true, but no SmokeDensitySampler found in the scene.");
            }
        }

        conn = ZmqUnityServer.GetInstance();
        conn.RegisterTimerDiscrete(SenseAndPublish, 1);
    }

    public void SenseAndPublish(ZmqTimerEvent ev)
    {
        var timestart = Time.realtimeSinceStartup;

        int totalRays = numRaysHorizontal * numRaysVertical;
        Lidar3DMessage msg = new Lidar3DMessage();
        msg.horizontalFovStart = horizontalFovStartDeg;
        msg.horizontalFovEnd = horizontalFovEndDeg;
        msg.verticalFovStart = verticalFovStartDeg;
        msg.verticalFovEnd = verticalFovEndDeg;
        msg.numRaysHorizontal = numRaysHorizontal;
        msg.numRaysVertical = numRaysVertical;
        msg.maxRange = maxRange;

        msg.ranges = new float[totalRays];
        msg.descriptors = new float[totalRays * descriptorDimension];

        // 1. Prepare RaycastCommands
        NativeArray<RaycastCommand> commands = new NativeArray<RaycastCommand>(totalRays, Allocator.TempJob);
        NativeArray<RaycastHit> results = new NativeArray<RaycastHit>(totalRays, Allocator.TempJob);

        int layerMask = ~(1 << LayerMask.NameToLayer("WorldGen"));
        QueryParameters queryParams = new QueryParameters(layerMask, false, QueryTriggerInteraction.Ignore, false);

        Vector3 startPos = transform.position;
        Quaternion sensorRot = transform.rotation;

        for (int v = 0; v < numRaysVertical; v++)
        {
            float vFraction = numRaysVertical > 1 ? (float)v / (numRaysVertical - 1) : 0.5f;
            float pitch = Mathf.Lerp(verticalFovStartDeg, verticalFovEndDeg, vFraction);

            for (int h = 0; h < numRaysHorizontal; h++)
            {
                float hFraction = numRaysHorizontal > 1 ? (float)h / (numRaysHorizontal - 1) : 0.5f;
                float yaw = Mathf.Lerp(horizontalFovStartDeg, horizontalFovEndDeg, hFraction);

                // Unity rotation: Yaw is around Y axis, Pitch is around X axis.
                // Note: Spherical coordinates might need adjustments based on exact convention,
                // but this covers the required FOV arcs.
                Vector3 localDir = Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward;
                Vector3 worldDir = sensorRot * localDir;

                int index = v * numRaysHorizontal + h;
                
                commands[index] = new RaycastCommand(startPos, worldDir, queryParams, maxRange);
            }
        }

        // 2. Schedule and wait
        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 64, default(JobHandle));
        handle.Complete();

        // 3. Process results
        float[] defaultDescriptor = new float[descriptorDimension];
        for (int i = 0; i < totalRays; i++)
        {
            RaycastHit hit = results[i];

            if (hit.collider != null)
            {
                msg.ranges[i] = hit.distance;

                SemanticObject semanticObject = hit.collider.GetComponent<SemanticObject>();
                float[] desc = semanticObject != null ? semanticObject.GetDescriptor(hit.point) : defaultDescriptor;

                for (int d = 0; d < descriptorDimension; d++)
                {
                    msg.descriptors[i * descriptorDimension + d] = desc[d];
                }

                if (debugDrawRays)
                {
                    Debug.DrawLine(commands[i].from, hit.point, Color.red, 0);
                }
            }
            else
            {
                msg.ranges[i] = -1f; // No hit
                for (int d = 0; d < descriptorDimension; d++)
                {
                    msg.descriptors[i * descriptorDimension + d] = 0f;
                }

                if (debugDrawRays)
                {
                    Debug.DrawLine(commands[i].from, commands[i].from + commands[i].direction * maxRange, Color.red, 0);
                }
            }
        }

        // We will dispose commands and results later, as we need commands[i].direction for raymarching

        // 3.5 Apply Volumetric Smoke Raymarching
        if (enableVolumetricSmoke && smokeSampler != null)
        {
            float[] smokeDescriptor = SemanticLidarSensor.GetNamedSemanticObjectDescriptor("smoke");
            if (smokeDescriptor == null || smokeDescriptor.Length != descriptorDimension)
            {
                smokeDescriptor = new float[descriptorDimension]; // fallback zeroes
            }

            int stepSeed = Time.frameCount * 31;
            System.Random rng = new System.Random(stepSeed);

            for (int i = 0; i < totalRays; i++)
            {
                // Only raymarch up to the physical hit or maxRange
                float rayMaxDist = msg.ranges[i] > 0 ? msg.ranges[i] : maxRange;
                if (rayMaxDist <= 0.001f) continue;
                
                // Exponential stochastic sampling
                double targetOpticalDepth = -Math.Log(1.0 - rng.NextDouble());
                double currentOpticalDepth = 0.0;
                
                float stepSize = rayMaxDist / Mathf.Max(1, smokeRaymarchSteps);
                Vector3 worldDir = commands[i].direction;

                for (int step = 0; step < smokeRaymarchSteps; step++)
                {
                    // Sample at the center of the step
                    float currentDist = step * stepSize + (stepSize * 0.5f);
                    Vector3 samplePos = startPos + worldDir * currentDist;
                    
                    float density = smokeSampler.SampleDensity(samplePos);
                    if (density > 0)
                    {
                        double stepOpticalDepth = density * stepSize;
                        if (currentOpticalDepth + stepOpticalDepth >= targetOpticalDepth)
                        {
                            // Corrupted by smoke within this step!
                            // Continuous hit distance using exact fraction
                            double fraction = (targetOpticalDepth - currentOpticalDepth) / stepOpticalDepth;
                            float hitDist = (float)(step * stepSize + fraction * stepSize);
                            
                            msg.ranges[i] = hitDist;
                            for (int d = 0; d < descriptorDimension; d++)
                            {
                                msg.descriptors[i * descriptorDimension + d] = smokeDescriptor[d];
                            }
                            break;
                        }
                        currentOpticalDepth += stepOpticalDepth;
                    }
                }
            }
        }

        commands.Dispose();
        results.Dispose();

        // 4. Apply Occulusion Fault (simplistic 3D equivalent)
        if (!string.IsNullOrEmpty(occlusionRegion) && occlusionRegion != "none")
        {
            int third = numRaysHorizontal / 3;
            int startIdx = 0, endIdx = 0;
            switch (occlusionRegion.ToLowerInvariant())
            {
                case "left": startIdx = 0; endIdx = third; break;
                case "front": startIdx = third; endIdx = 2 * third; break;
                case "right": startIdx = 2 * third; endIdx = numRaysHorizontal; break;
            }

            if (endIdx > startIdx)
            {
                for (int v = 0; v < numRaysVertical; v++)
                {
                    for (int h = startIdx; h < endIdx; h++)
                    {
                        int index = v * numRaysHorizontal + h;
                        msg.ranges[index] = occlusionDistance;
                        for (int d = 0; d < descriptorDimension; d++)
                        {
                            msg.descriptors[index * descriptorDimension + d] = 0f;
                        }
                    }
                }
            }
        }

        lastRanges = msg.ranges;
        lastDescriptors = msg.descriptors;
        
        if (sendMessageToPython && conn != null)
        {
            if (useRawBinary)
            {
                var rawMsg = new RawLidarMessage
                {
                    numRays = totalRays,
                    descriptorDimension = (int)descriptorDimension,
                    horizontalFovStart = horizontalFovStartDeg,
                    horizontalFovEnd = horizontalFovEndDeg,
                    verticalFovStart = verticalFovStartDeg,
                    verticalFovEnd = verticalFovEndDeg,
                    maxRange = maxRange
                };

                if (rangesByteBuffer == null || rangesByteBuffer.Length != totalRays * sizeof(float))
                {
                    rangesByteBuffer = new byte[totalRays * sizeof(float)];
                }
                int descCount = totalRays * (int)descriptorDimension;
                if (descByteBuffer == null || descByteBuffer.Length != descCount * sizeof(float))
                {
                    descByteBuffer = new byte[descCount * sizeof(float)];
                }

                Buffer.BlockCopy(msg.ranges, 0, rangesByteBuffer, 0, rangesByteBuffer.Length);
                Buffer.BlockCopy(msg.descriptors, 0, descByteBuffer, 0, descByteBuffer.Length);

                conn.PublishBinaryDual(topicName, rawMsg, rangesByteBuffer, descByteBuffer);
            }
            else
            {
                conn.Publish(topicName, msg);
            }
        }

        if (verbose)
        {
            Debug.Log($"3D Lidar SenseAndPublish time: {1000f * (Time.realtimeSinceStartup - timestart):F2} ms for {totalRays} rays.");
        }
    }
}
