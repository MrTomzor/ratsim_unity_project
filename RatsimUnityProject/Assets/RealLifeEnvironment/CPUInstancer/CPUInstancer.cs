using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealLifeEnvironment
{
    public class CPUInstancer : MonoBehaviour
    {
        [Header("Prefabs & Compute")]
        public GameObject[] colliderPrefabs;
        public float[] defaultPrefabWeights;
        [Range(0, 31)] public int prefabRotation = 0;
        public ComputeShader computeShader;

        [Header("Scale & Rotation (Shader Match)")]
        public Vector2 baseScale = new Vector2(1, 1);
        [Range(0, 1)] public float instanceScaleRandomness = 0.25f;
        public Vector2 scaleMultiplierA = new Vector2(1, 1);
        [Range(0, 1)] public float terrainAlignment = 0.5f;

        [Header("Instancing Properties")]
        public float spacing = 0.5f;
        [Tooltip("Max distance to spawn colliders (360 degrees around camera).")]
        public float spawnDistance = 30f;
        public float fullDensityDistance = 50f;
        public BiomeMask allowedBiomes = (BiomeMask)(-1);
        
        [Header("Height Sampling")]
        public bool alignToTerrainMesh = false;
        public float terrainBaseScale = 1.0f;
        public int terrainGridResolution = 128;

        [Header("Height Fading")]
        public float heightFadeStart = 100f;
        public float heightFadeEnd = 120f;

        [Header("Performance Options")]
        public float updateInterval = 0.5f;
        public int spawnsPerFrame = 20;

        [Header("Debug")]
        [TextArea(2, 2)]
        public string debugStats = "Updating...";

        private ComputeBuffer instancePositionsBuffer;
        private ComputeBuffer biomeWeightsBuffer;
        private ComputeBuffer countBuffer;

        private float timer = 0f;
        private bool isProcessing = false;

        private Dictionary<Vector3Int, GameObject> activeColliders = new Dictionary<Vector3Int, GameObject>();
        private Queue<Vector3Int> collidersToRemove = new Queue<Vector3Int>();
        private HashSet<Vector3Int> pendingRemovals = new HashSet<Vector3Int>();
        private Queue<InstanceData> collidersToSpawn = new Queue<InstanceData>();

        struct InstanceData
        {
            public Vector3 position;
            public Vector3 normal;
            public float texIndex;
        }

        private void OnEnable()
        {
            instancePositionsBuffer = new ComputeBuffer(100000, sizeof(float) * 7, ComputeBufferType.Append);
            biomeWeightsBuffer = new ComputeBuffer(20 * 32, sizeof(float));
            countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
            
            UpdateWeights();
        }

        private void UpdateWeights()
        {
            if (colliderPrefabs == null || colliderPrefabs.Length == 0)
            {
                float[] dummyWeights = new float[20 * 32];
                for (int i = 0; i < dummyWeights.Length; i++) dummyWeights[i] = 1f;
                biomeWeightsBuffer.SetData(dummyWeights);
                return;
            }

            float[] cumulativeWeights = new float[20 * 32];
            int validCount = colliderPrefabs.Length;
            
            float totalWeight = 0;
            float[] weights = new float[32];
            for (int i = 0; i < validCount && i < 32; i++)
            {
                weights[i] = (defaultPrefabWeights != null && i < defaultPrefabWeights.Length) ? defaultPrefabWeights[i] : 1.0f;
                totalWeight += weights[i];
            }
            if (totalWeight <= 0) totalWeight = 1;

            float[] baseCumulative = new float[32];
            float currentSum = 0;
            for (int i = 0; i < 32; i++)
            {
                if (i < validCount)
                {
                    currentSum += weights[i];
                    baseCumulative[i] = currentSum / totalWeight;
                }
                else
                {
                    baseCumulative[i] = 1.0f;
                }
            }
            if (validCount > 0 && validCount <= 32)
            {
                baseCumulative[validCount - 1] = 1.0f;
            }

            for (int b = 0; b < 20; b++)
            {
                for (int i = 0; i < 32; i++)
                {
                    cumulativeWeights[b * 32 + i] = baseCumulative[i];
                }
            }

            biomeWeightsBuffer.SetData(cumulativeWeights);
        }

        private void OnDisable()
        {
            instancePositionsBuffer?.Release();
            biomeWeightsBuffer?.Release();
            countBuffer?.Release();
            
            foreach (var kvp in activeColliders)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            activeColliders.Clear();
            collidersToRemove.Clear();
            collidersToSpawn.Clear();
            pendingRemovals.Clear();
        }

        void Update()
        {
            if (colliderPrefabs == null || colliderPrefabs.Length == 0 || computeShader == null || Camera.main == null || spacing <= 0) return;

            ProcessQueues();

            timer += Time.deltaTime;
            if (timer >= updateInterval && !isProcessing)
            {
                timer = 0f;
                StartCoroutine(UpdateCollidersRoutine());
            }

            if (!debugStats.StartsWith("Error"))
            {
                debugStats = $"Active Colliders: {activeColliders.Count}\nQueued to Spawn: {collidersToSpawn.Count} | Queued to Remove: {collidersToRemove.Count}";
            }
        }

        private void ProcessQueues()
        {
            int operationsThisFrame = 0;

            while (collidersToRemove.Count > 0 && operationsThisFrame < spawnsPerFrame)
            {
                Vector3Int key = collidersToRemove.Dequeue();
                if (pendingRemovals.Contains(key))
                {
                    pendingRemovals.Remove(key);
                    if (activeColliders.TryGetValue(key, out GameObject obj))
                    {
                        if (obj != null) Destroy(obj);
                        activeColliders.Remove(key);
                    }
                    operationsThisFrame++;
                }
            }

            while (collidersToSpawn.Count > 0 && operationsThisFrame < spawnsPerFrame)
            {
                InstanceData data = collidersToSpawn.Dequeue();
                Vector3Int key = GetPositionHash(data.position);
                
                if (pendingRemovals.Contains(key))
                {
                    // It was queued for removal before we even spawned it
                    activeColliders.Remove(key);
                    pendingRemovals.Remove(key);
                    continue; // Skip without consuming an operation
                }
                
                if (activeColliders.TryGetValue(key, out GameObject existingObj) && existingObj == null)
                {
                    int prefabIndex = ((int)data.texIndex + Mathf.Max(0, prefabRotation)) % colliderPrefabs.Length;
                    GameObject newObj = Instantiate(colliderPrefabs[prefabIndex], data.position, GetRotation(data.position, data.normal));
                    newObj.transform.localScale = GetScale(data.position);
                    newObj.transform.parent = transform;
                    activeColliders[key] = newObj;
                }
                operationsThisFrame++;
            }
        }

        IEnumerator UpdateCollidersRoutine()
        {
            isProcessing = true;
            
            if (debugStats.StartsWith("Error"))
            {
                debugStats = "Updating...";
            }

            Camera cam = Camera.main;
            Vector3 camPos = cam.transform.position;

            Bounds bounds = new Bounds(camPos, Vector3.zero);
            float radius = spawnDistance + spacing + 5f;
            bounds.Encapsulate(camPos + new Vector3(radius, 0, radius));
            bounds.Encapsulate(camPos - new Vector3(radius, 0, radius));

            Vector2Int gridStartIndex = new Vector2Int(Mathf.FloorToInt(bounds.min.x / spacing), Mathf.FloorToInt(bounds.min.z / spacing));
            Vector2Int gridEndIndex = new Vector2Int(Mathf.CeilToInt(bounds.max.x / spacing), Mathf.CeilToInt(bounds.max.z / spacing));
            Vector2Int gridSize = gridEndIndex - gridStartIndex;

            computeShader.SetMatrix("_VPMatrix", Matrix4x4.identity);
            computeShader.SetFloat("_FullDensityDistance", fullDensityDistance);
            computeShader.SetVector("_CameraPosition", camPos);
            computeShader.SetFloat("_DrawDistance", 0f);
            computeShader.SetFloat("_Spacing", spacing);
            computeShader.SetInt("_AllowedBiomes", (int)allowedBiomes);
            computeShader.SetFloat("_ForceSpawnDistance", spawnDistance);
            computeShader.SetInt("_AlignToTerrainMesh", alignToTerrainMesh ? 1 : 0);
            computeShader.SetFloat("_TerrainBaseScale", terrainBaseScale);
            computeShader.SetInt("_TerrainGridResolution", terrainGridResolution);
            computeShader.SetFloat("_HeightFadeStart", heightFadeStart);
            computeShader.SetFloat("_HeightFadeEnd", heightFadeEnd);
            computeShader.SetInt("_UseNearPlanes", 0);
            computeShader.SetFloat("_FrustumBuffer", 0);
            computeShader.SetVector("_GridStartIndex", (Vector2)gridStartIndex);
            computeShader.SetVector("_GridSize", (Vector2)gridSize);

            UpdateWeights();
            computeShader.SetBuffer(0, "_BiomeCumulativeWeights", biomeWeightsBuffer);
            computeShader.SetInt("_TextureCount", colliderPrefabs == null ? 0 : colliderPrefabs.Length);

            Texture customHeightmap = Shader.GetGlobalTexture("_CustomTerrainHeightmap");
            computeShader.SetTexture(0, "_CustomTerrainHeightmap", customHeightmap != null ? customHeightmap : Texture2D.blackTexture);
            
            Texture biomeMap = Shader.GetGlobalTexture("_GlobalBiomeMap");
            computeShader.SetTexture(0, "_GlobalBiomeMap", biomeMap != null ? biomeMap : Texture2D.whiteTexture);

            computeShader.SetVector("_CustomTerrainHeightmap_Bounds", Shader.GetGlobalVector("_CustomTerrainHeightmap_Bounds"));
            computeShader.SetVector("_CustomTerrainHeightmap_TexelSize", Shader.GetGlobalVector("_CustomTerrainHeightmap_TexelSize"));
            computeShader.SetFloat("_CustomTerrainHeightmap_Multiplier", Shader.GetGlobalFloat("_CustomTerrainHeightmap_Multiplier"));
            computeShader.SetVector("_GlobalBiomeMap_Bounds", Shader.GetGlobalVector("_GlobalBiomeMap_Bounds"));
            computeShader.SetVector("_GlobalBiomeMap_TexelSize", Shader.GetGlobalVector("_GlobalBiomeMap_TexelSize"));

            instancePositionsBuffer.SetCounterValue(0);
            computeShader.SetBuffer(0, "_InstancePositions", instancePositionsBuffer);

            int threadGroupsX = Mathf.Max(1, Mathf.CeilToInt((float)gridSize.x / 8f));
            int threadGroupsY = Mathf.Max(1, Mathf.CeilToInt((float)gridSize.y / 8f));

            computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

            ComputeBuffer.CopyCount(instancePositionsBuffer, countBuffer, 0);
            
            var countRequest = AsyncGPUReadback.Request(countBuffer);
            yield return new WaitUntil(() => countRequest.done);

            if (countRequest.hasError)
            {
                debugStats = "Error: countRequest failed!";
                isProcessing = false;
                yield break;
            }

            uint count = countRequest.GetData<uint>()[0];

            if (count > 0)
            {
                var dataRequest = AsyncGPUReadback.Request(instancePositionsBuffer, (int)count * sizeof(float) * 7, 0);
                yield return new WaitUntil(() => dataRequest.done);

                if (dataRequest.hasError)
                {
                    debugStats = "Error: dataRequest failed!";
                    isProcessing = false;
                    yield break;
                }

                var instanceArray = dataRequest.GetData<InstanceData>();
                
                HashSet<Vector3Int> currentFrameKeys = new HashSet<Vector3Int>();

                for (int i = 0; i < count; i++)
                {
                    InstanceData data = instanceArray[i];
                    Vector3Int key = GetPositionHash(data.position);
                    currentFrameKeys.Add(key);

                    if (!activeColliders.ContainsKey(key))
                    {
                        collidersToSpawn.Enqueue(data);
                        activeColliders.Add(key, null); // Add null to signify it's pending
                    }
                    else
                    {
                        if (pendingRemovals.Contains(key))
                        {
                            // It came back into range before we deleted it!
                            pendingRemovals.Remove(key);
                        }
                    }
                }

                foreach (var kvp in activeColliders)
                {
                    if (!currentFrameKeys.Contains(kvp.Key))
                    {
                        if (!pendingRemovals.Contains(kvp.Key))
                        {
                            collidersToRemove.Enqueue(kvp.Key);
                            pendingRemovals.Add(kvp.Key);
                        }
                    }
                }
            }
            else
            {
                foreach (var kvp in activeColliders)
                {
                    if (!pendingRemovals.Contains(kvp.Key))
                    {
                        collidersToRemove.Enqueue(kvp.Key);
                        pendingRemovals.Add(kvp.Key);
                    }
                }
            }

            isProcessing = false;
        }

        private Vector3Int GetPositionHash(Vector3 pos)
        {
            return new Vector3Int(Mathf.RoundToInt(pos.x * 1000f), Mathf.RoundToInt(pos.y * 1000f), Mathf.RoundToInt(pos.z * 1000f));
        }

        private uint MurmurHash3(float input)
        {
            uint h = (uint)Mathf.Abs(input);
            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae3d;
            h ^= h >> 16;
            return h;
        }

        private float RandomVal(float input)
        {
            return MurmurHash3(input) / 4294967295.0f;
        }

        private Quaternion GetRotation(Vector3 pivot, Vector3 terrainNormal)
        {
            float rand1 = RandomVal(pivot.x * 391.0f + pivot.z * 10.0f);
            float angle = rand1 * 6.283185f;

            float cosA = Mathf.Cos(angle);
            float sinA = Mathf.Sin(angle);

            Vector3 up = Vector3.Normalize(Vector3.Lerp(Vector3.up, terrainNormal, terrainAlignment));
            Vector3 baseForward = new Vector3(-sinA, 0.0f, cosA);
            Vector3 right = Vector3.Normalize(Vector3.Cross(up, baseForward));
            Vector3 forward = Vector3.Cross(right, up);

            if (forward == Vector3.zero || up == Vector3.zero) return Quaternion.identity;

            return Quaternion.LookRotation(forward, up);
        }

        private Vector3 GetScale(Vector3 pivot)
        {
            float randScale = 1.0f - RandomVal(pivot.x * 950.0f + pivot.z * 10.0f) * instanceScaleRandomness;
            
            float finalScaleX = baseScale.x * randScale * scaleMultiplierA.x;
            float finalScaleY = baseScale.y * randScale * scaleMultiplierA.y;
            
            return new Vector3(finalScaleX, finalScaleY, finalScaleX);
        }
    }
}
