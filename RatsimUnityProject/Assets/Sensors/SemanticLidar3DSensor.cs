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
    private float[] rangesArray;
    private float[] descriptorsArray;
    private Vector3[] localRayDirs;

    private struct CachedColliderInfo
    {
        public bool isStatic;
        public float[] staticDesc;
        public SemanticObject dynamicObj;
    }

    private Dictionary<int, CachedColliderInfo> colliderCache = new Dictionary<int, CachedColliderInfo>(2048);

    [Header("Faults")]
    public string occlusionRegion = "none";
    public float occlusionDistance = 0.1f;

    [Header("Volumetric Smoke")]
    public bool enableVolumetricSmoke = true;
    public SmokeDensitySampler smokeSampler;
    public int smokeRaymarchSteps = 16;

    ZmqUnityServer conn;

    void PrecomputeLocalRayDirections(int totalRays)
    {
        localRayDirs = new Vector3[totalRays];
        for (int v = 0; v < numRaysVertical; v++)
        {
            float vFraction = numRaysVertical > 1 ? (float)v / (numRaysVertical - 1) : 0.5f;
            float pitch = Mathf.Lerp(verticalFovStartDeg, verticalFovEndDeg, vFraction);

            for (int h = 0; h < numRaysHorizontal; h++)
            {
                float hFraction = numRaysHorizontal > 1 ? (float)h / (numRaysHorizontal - 1) : 0.5f;
                float yaw = Mathf.Lerp(horizontalFovStartDeg, horizontalFovEndDeg, hFraction);

                Vector3 localDir = Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward;
                int index = v * numRaysHorizontal + h;
                localRayDirs[index] = localDir;
            }
        }
    }

    private CachedColliderInfo GetOrAddColliderInfo(Collider col)
    {
        int id = col.GetInstanceID();
        if (colliderCache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        SemanticObject semObj = col.GetComponent<SemanticObject>();
        CachedColliderInfo info;

        if (semObj is NamedSemanticObject namedObj)
        {
            info.isStatic = true;
            if (!string.IsNullOrEmpty(namedObj.semanticName) && SemanticLidarSensor.precomputedNamedDescriptors.TryGetValue(namedObj.semanticName, out var desc))
            {
                info.staticDesc = desc;
            }
            else
            {
                info.staticDesc = SemanticLidarSensor.defaultZeroDescriptor;
            }
            info.dynamicObj = null;
        }
        else if (semObj is ColorSemanticObject colorObj)
        {
            info.isStatic = true;
            Color c = colorObj.color;
            float[] desc = new float[descriptorDimension];
            if (descriptorDimension >= 3)
            {
                desc[0] = c.r;
                desc[1] = c.g;
                desc[2] = c.b;
            }
            info.staticDesc = desc;
            info.dynamicObj = null;
        }
        else if (semObj != null)
        {
            info.isStatic = false;
            info.staticDesc = null;
            info.dynamicObj = semObj;
        }
        else
        {
            info.isStatic = true;
            info.staticDesc = SemanticLidarSensor.defaultZeroDescriptor;
            info.dynamicObj = null;
        }

        colliderCache[id] = info;
        return info;
    }

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

        SemanticLidarSensor.defaultZeroDescriptor = new float[descriptorDimension];
        SemanticLidarSensor.precomputedNamedDescriptors.Clear();
        foreach (DictionaryEntry entry in SemanticLidarSensor.semanticNamesToIndices)
        {
            string name = (string)entry.Key;
            int idx = (int)entry.Value;
            float[] desc = new float[descriptorDimension];
            desc[idx] = 1.0f;
            SemanticLidarSensor.precomputedNamedDescriptors[name] = desc;
        }

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

        int totalRays = numRaysHorizontal * numRaysVertical;
        rangesArray = new float[totalRays];
        descriptorsArray = new float[totalRays * descriptorDimension];

        rangesByteBuffer = new byte[totalRays * sizeof(float)];
        descByteBuffer = new byte[totalRays * descriptorDimension * sizeof(float)];

        PrecomputeLocalRayDirections(totalRays);

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
        int dim = (int)descriptorDimension;

        // 1. Prepare RaycastCommands using precomputed directions
        NativeArray<RaycastCommand> commands = new NativeArray<RaycastCommand>(totalRays, Allocator.TempJob);
        NativeArray<RaycastHit> results = new NativeArray<RaycastHit>(totalRays, Allocator.TempJob);

        int layerMask = ~(1 << LayerMask.NameToLayer("WorldGen"));
        QueryParameters queryParams = new QueryParameters(layerMask, false, QueryTriggerInteraction.Ignore, false);

        Vector3 startPos = transform.position;
        Quaternion sensorRot = transform.rotation;

        for (int i = 0; i < totalRays; i++)
        {
            Vector3 worldDir = sensorRot * localRayDirs[i];
            commands[i] = new RaycastCommand(startPos, worldDir, queryParams, maxRange);
        }

        // 2. Schedule in chunks of 256 for optimal multi-threaded job dispatch
        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 256, default(JobHandle));
        handle.Complete();

        // 3. Process results with collider cache
        for (int i = 0; i < totalRays; i++)
        {
            RaycastHit hit = results[i];
            Collider col = hit.collider;
            int descOffset = i * dim;

            if (col != null)
            {
                rangesArray[i] = hit.distance;

                int colId = col.GetInstanceID();
                if (!colliderCache.TryGetValue(colId, out var info))
                {
                    info = GetOrAddColliderInfo(col);
                }

                float[] desc = info.isStatic ? info.staticDesc : (info.dynamicObj != null ? info.dynamicObj.GetDescriptor(hit.point) : SemanticLidarSensor.defaultZeroDescriptor);

                if (desc != null && desc.Length == dim)
                {
                    Array.Copy(desc, 0, descriptorsArray, descOffset, dim);
                }
                else if (desc != null)
                {
                    int copyCount = Math.Min(dim, desc.Length);
                    Array.Copy(desc, 0, descriptorsArray, descOffset, copyCount);
                    for (int d = copyCount; d < dim; d++)
                    {
                        descriptorsArray[descOffset + d] = 0f;
                    }
                }
                else
                {
                    for (int d = 0; d < dim; d++)
                    {
                        descriptorsArray[descOffset + d] = 0f;
                    }
                }

                if (debugDrawRays)
                {
                    Debug.DrawLine(commands[i].from, hit.point, Color.red, 0);
                }
            }
            else
            {
                rangesArray[i] = -1f; // No hit
                for (int d = 0; d < dim; d++)
                {
                    descriptorsArray[descOffset + d] = 0f;
                }

                if (debugDrawRays)
                {
                    Debug.DrawLine(commands[i].from, commands[i].from + commands[i].direction * maxRange, Color.red, 0);
                }
            }
        }

        // 3.5 Apply Volumetric Smoke Raymarching via Job System
        if (enableVolumetricSmoke && smokeSampler != null && smokeSampler.IsSmokeActive)
        {
            float[] smokeDescManaged = SemanticLidarSensor.GetNamedSemanticObjectDescriptor("smoke");
            if (smokeDescManaged == null || smokeDescManaged.Length != dim)
            {
                smokeDescManaged = new float[dim];
            }

            NativeArray<float> smokeDescNative = new NativeArray<float>(smokeDescManaged, Allocator.TempJob);
            NativeArray<float> rangesNative = new NativeArray<float>(rangesArray, Allocator.TempJob);
            NativeArray<float> descriptorsNative = new NativeArray<float>(descriptorsArray, Allocator.TempJob);

            var smokeJob = new SmokeRaymarchJob
            {
                ranges = rangesNative,
                descriptors = descriptorsNative,
                commands = commands,
                smokeDescriptor = smokeDescNative,
                smokeJobData = smokeSampler.GetJobData(),
                maxRange = maxRange,
                smokeRaymarchSteps = smokeRaymarchSteps,
                descriptorDimension = dim,
                baseSeed = (uint)(Time.frameCount * 31)
            };

            JobHandle smokeHandle = smokeJob.Schedule(totalRays, 64);
            smokeHandle.Complete();

            rangesNative.CopyTo(rangesArray);
            descriptorsNative.CopyTo(descriptorsArray);

            rangesNative.Dispose();
            descriptorsNative.Dispose();
            smokeDescNative.Dispose();
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
                        rangesArray[index] = occlusionDistance;
                        for (int d = 0; d < dim; d++)
                        {
                            descriptorsArray[index * dim + d] = 0f;
                        }
                    }
                }
            }
        }

        lastRanges = rangesArray;
        lastDescriptors = descriptorsArray;

        if (sendMessageToPython && conn != null)
        {
            if (useRawBinary)
            {
                var rawMsg = new RawLidarMessage
                {
                    numRays = totalRays,
                    descriptorDimension = dim,
                    horizontalFovStart = horizontalFovStartDeg,
                    horizontalFovEnd = horizontalFovEndDeg,
                    verticalFovStart = verticalFovStartDeg,
                    verticalFovEnd = verticalFovEndDeg,
                    maxRange = maxRange
                };

                Buffer.BlockCopy(rangesArray, 0, rangesByteBuffer, 0, rangesByteBuffer.Length);
                Buffer.BlockCopy(descriptorsArray, 0, descByteBuffer, 0, descByteBuffer.Length);

                conn.PublishBinaryDual(topicName, rawMsg, rangesByteBuffer, descByteBuffer);
            }
            else
            {
                Lidar3DMessage msg = new Lidar3DMessage
                {
                    horizontalFovStart = horizontalFovStartDeg,
                    horizontalFovEnd = horizontalFovEndDeg,
                    verticalFovStart = verticalFovStartDeg,
                    verticalFovEnd = verticalFovEndDeg,
                    numRaysHorizontal = numRaysHorizontal,
                    numRaysVertical = numRaysVertical,
                    maxRange = maxRange,
                    ranges = rangesArray,
                    descriptors = descriptorsArray
                };
                conn.Publish(topicName, msg);
            }
        }

        if (verbose)
        {
            Debug.Log($"3D Lidar SenseAndPublish time: {1000f * (Time.realtimeSinceStartup - timestart):F2} ms for {totalRays} rays.");
        }
    }
}


public struct SmokeRaymarchJob : IJobParallelFor
{
    public NativeArray<float> ranges;
    [NativeDisableParallelForRestriction] public NativeArray<float> descriptors;

    [ReadOnly] public NativeArray<RaycastCommand> commands;
    [ReadOnly] public NativeArray<float> smokeDescriptor;

    public SmokeDensityJobData smokeJobData;

    public float maxRange;
    public int smokeRaymarchSteps;
    public int descriptorDimension;
    public uint baseSeed;

    private float Random(int index)
    {
        // Xorshift32 PRNG: Fast, deterministic, and thread-safe
        uint state = baseSeed + (uint)index * 2654435761u;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        // Uniform float in (0, 1] to avoid Log(0)
        float rnd = ((state & 0x00FFFFFF) + 1.0f) / 16777217.0f;
        return rnd;
    }

    public void Execute(int index)
    {
        float rayMaxDist = ranges[index] > 0 ? ranges[index] : maxRange;
        if (rayMaxDist <= 0.001f) return;

        float targetOpticalDepth = -Mathf.Log(1.0f - Random(index));
        float currentOpticalDepth = 0.0f;

        float stepSize = rayMaxDist / Mathf.Max(1, smokeRaymarchSteps);
        Vector3 startPos = commands[index].from;
        Vector3 worldDir = commands[index].direction;

        for (int step = 0; step < smokeRaymarchSteps; step++)
        {
            float currentDist = step * stepSize + (stepSize * 0.5f);
            Vector3 samplePos = startPos + worldDir * currentDist;

            float density = smokeJobData.SampleDensity(samplePos);
            if (density > 0f)
            {
                float stepOpticalDepth = density * stepSize;
                if (currentOpticalDepth + stepOpticalDepth >= targetOpticalDepth)
                {
                    float fraction = (targetOpticalDepth - currentOpticalDepth) / stepOpticalDepth;
                    float hitDist = step * stepSize + fraction * stepSize;

                    ranges[index] = hitDist;
                    int descOffset = index * descriptorDimension;
                    for (int d = 0; d < descriptorDimension; d++)
                    {
                        descriptors[descOffset + d] = smokeDescriptor[d];
                    }
                    break;
                }
                currentOpticalDepth += stepOpticalDepth;
            }
        }
    }
}