using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;

public class ZmqLidar3DSensor : MonoBehaviour
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

    [Header("Network")]
    public bool sendMessageToPython = true;

    private ZmqUnityServer conn;
    private byte[] rangesByteBuffer;
    private byte[] descByteBuffer;
    private float[] rangesArray;
    private float[] descriptorsArray;

    // --- Optimization Caches ---
    private Vector3[] localRayDirs;
    private float[] defaultZeroDescriptor;
    private Dictionary<string, float[]> precomputedNamedDescriptors = new Dictionary<string, float[]>();

    private struct CachedColliderInfo
    {
        public bool isStatic;
        public float[] staticDesc;
        public SemanticObject dynamicObj;
    }

    private Dictionary<int, CachedColliderInfo> colliderCache = new Dictionary<int, CachedColliderInfo>(2048);

    void InitializeSemanticSetData()
    {
        SemanticLidarSensor.semanticNamesToIndices = new Hashtable();
        precomputedNamedDescriptors.Clear();

        foreach (GameObject obj in activeSemanticSet.prefabs)
        {
            if (obj == null) continue;
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

        defaultZeroDescriptor = new float[descriptorDimension];

        foreach (DictionaryEntry entry in SemanticLidarSensor.semanticNamesToIndices)
        {
            string name = (string)entry.Key;
            int idx = (int)entry.Value;
            float[] desc = new float[descriptorDimension];
            desc[idx] = 1.0f;
            precomputedNamedDescriptors[name] = desc;
        }

        Debug.Log("[ZmqLidar3DSensor] Initialized Semantic Set with " + descriptorDimension + " semantic classes.");
    }

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

    void Start()
    {
        conn = ZmqUnityServer.GetInstance();

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
        else if (SemanticLidarSensor.semanticNamesToIndices != null)
        {
            descriptorDimension = SemanticLidarSensor.descriptorDimension;
            defaultZeroDescriptor = new float[descriptorDimension];
        }
        else
        {
            defaultZeroDescriptor = new float[descriptorDimension];
        }

        int totalRays = numRaysHorizontal * numRaysVertical;
        rangesArray = new float[totalRays];
        descriptorsArray = new float[totalRays * descriptorDimension];

        rangesByteBuffer = new byte[totalRays * sizeof(float)];
        descByteBuffer = new byte[totalRays * descriptorDimension * sizeof(float)];

        PrecomputeLocalRayDirections(totalRays);

        conn.RegisterTimerDiscrete(SenseAndPublish, 1);
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
            if (!string.IsNullOrEmpty(namedObj.semanticName) && precomputedNamedDescriptors.TryGetValue(namedObj.semanticName, out var desc))
            {
                info.staticDesc = desc;
            }
            else
            {
                info.staticDesc = defaultZeroDescriptor;
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
            // Position-dependent (e.g. TerrainSemanticObject)
            info.isStatic = false;
            info.staticDesc = null;
            info.dynamicObj = semObj;
        }
        else
        {
            // Non-semantic collider (e.g. ground, rock without semantic tag)
            info.isStatic = true;
            info.staticDesc = defaultZeroDescriptor;
            info.dynamicObj = null;
        }

        colliderCache[id] = info;
        return info;
    }

    public void SenseAndPublish(ZmqTimerEvent ev)
    {
        int totalRays = numRaysHorizontal * numRaysVertical;

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

        // Schedule in chunks of 256 for optimal multi-threaded job dispatch
        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 256);
        handle.Complete();

        int dim = (int)descriptorDimension;

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

                float[] desc = info.isStatic ? info.staticDesc : (info.dynamicObj != null ? info.dynamicObj.GetDescriptor(hit.point) : defaultZeroDescriptor);

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
            }
            else
            {
                rangesArray[i] = -1f;
                for (int d = 0; d < dim; d++)
                {
                    descriptorsArray[descOffset + d] = 0f;
                }
            }
        }

        commands.Dispose();
        results.Dispose();

        if (sendMessageToPython && conn != null)
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
    }
}
