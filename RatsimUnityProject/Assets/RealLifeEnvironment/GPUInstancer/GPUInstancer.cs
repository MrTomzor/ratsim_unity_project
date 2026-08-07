using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealLifeEnvironment
{
    [System.Flags]
    public enum BiomeMask
    {
        Trees_10 = 1 << 0,
        Shrubland_20 = 1 << 1,
        Grassland_30 = 1 << 2,
        Cropland_40 = 1 << 3,
        BuiltUp_50 = 1 << 4,
        Bare_60 = 1 << 5,
        SnowAndIce_70 = 1 << 6,
        Water_80 = 1 << 7,
        Wetland_90 = 1 << 8,
        Mangroves_100 = 1 << 9,
        MossAndLichen_110 = 1 << 10,
        Orchard_120 = 1 << 11,
        EmptyOrchard_130 = 1 << 12,
        Road_140 = 1 << 13,
        Path_150 = 1 << 14,
        Cropland2_160 = 1 << 15,
        Cropland3_170 = 1 << 16,
        Cropland4_180 = 1 << 17,
        Cropland5_190 = 1 << 18,
        WaterEdge_200 = 1 << 19
    }

    [System.Serializable]
    public struct BiomeTextureWeight
    {
        public BiomeMask biome;
        public float[] weights;
    }

    public class GPUInstancer : MonoBehaviour
    {
        [Header("Internal")]
        public Material instanceMaterial;
        public ComputeBuffer argsBuffer;
        public ComputeBuffer tBuffer;//Just a temp buffer to preview the visible instance count

        [Header("Instance Mesh")]
        [Tooltip("Assign a custom mesh here. Pivot should be at the base.")]
        public Mesh instanceMesh;

        [Header("Texture Array")]
        [Tooltip("Assign all variations of textures here. They must have the same width, height, and format.")]
        public Texture2D[] instanceTextures;
        [Tooltip("Shifts the textures by this amount. Useful to quickly try different texture-to-biome mappings.")]
        [Range(0, 31)]
        public int textureRotation = 0;
        [Tooltip("Default weights used if a biome isn't overridden below.")]
        public float[] defaultTextureWeights;
        [Tooltip("Optional overrides for texture weights per biome.")]
        public BiomeTextureWeight[] biomeTextureWeightsOverrides;
        [Tooltip("Texture tiling X for all textures.")]
        [Range(0.01f, 20f)]
        public float textureTilingX = 1.0f;
        [Tooltip("Texture tiling Y for all textures.")]
        [Range(0.01f, 20f)]
        public float textureTilingY = 1.0f;
        private Texture2DArray instanceTextureArray;
        private float[] biomeCumulativeWeights = new float[20 * 32];
        private int lastTextureHash = 0;

        [Header("Instancing Properties")]
        public bool castShadows = true;
        [Tooltip("If enabled, instances that are scaled to overlap into other biomes will be clipped pixel-perfectly at the boundary in the shader.")]
        public bool pixelAccurateBiomeClipping = false;
        public float spacing = 0.5f;//Spacing between instances
        public float drawDistance = 300;
        public float fullDensityDistance = 50;//After this distance, we start removing some instances in sake of performance
        [Tooltip("Distance from camera where instances will always spawn regardless of frustum culling.")]
        public float forceSpawnDistance = 10.0f;
        [Tooltip("Select which biomes (by index) this instance should spawn in.")]
        public BiomeMask allowedBiomes = (BiomeMask)(-1); // Default all checked
        [Tooltip("Safety margin outside the screen bounds to prevent popping at the edges (e.g. 0.1 is a 10% extra margin).")]
        public float frustumBuffer = 0.2f;

        [Header("Height Sampling")]
        [Tooltip("If enabled, samples height using triangle interpolation to perfectly match the RealTerrain mesh surface. Otherwise uses smooth bicubic interpolation.")]
        public bool alignToTerrainMesh = false;
        [Tooltip("The base scale (LOD 0 grid spacing) of the RealTerrain.")]
        public float terrainBaseScale = 1.0f;
        [Tooltip("The grid resolution of the RealTerrain.")]
        public int terrainGridResolution = 128;

        [Header("Height Fading")]
        public float heightFadeStart = 100f;
        public float heightFadeEnd = 120f;

        [Header("Near Plane Density Fade")]
        public bool useNearPlanes = false;
        public float nearPlaneStart = 0f;
        public float nearPlaneEnd = 10f;

        [Header("Max Buffer Count (Millions)")]
        public float maxBufferCount = 2; //The number we gonna use to initialize the positions buffer

        [Header("Debug")]
        public bool previewVisibleInstanceCount = false;

        [Header("Stats")]
        [Tooltip("Shows how many total places are being considered by the compute shader.")]
        [TextArea(2, 2)]
        public string instanceStats = "Updating...";

        [Header("Compute Shader")]
        public ComputeShader computeShader;

        private readonly Vector2 fixedHeightBounds = new Vector2(-1000f, 1000f);

        private ComputeBuffer instancePositionsBuffer;
        private ComputeBuffer biomeWeightsBuffer;
        private float currentBufferCount = -1f;
        private MaterialPropertyBlock propertyBlock;
        private Material shadowMaterial;
        
        private CommandBuffer shadowGrabCommandBuffer;
        private Light mainDirectionalLight;

        private void OnDisable()
        {
            argsBuffer?.Release();
            argsBuffer = null;
            tBuffer?.Release();
            tBuffer = null;
            instancePositionsBuffer?.Release();
            instancePositionsBuffer = null;
            biomeWeightsBuffer?.Release();
            biomeWeightsBuffer = null;

            if (instanceTextureArray != null)
            {
                if (Application.isPlaying) Destroy(instanceTextureArray);
                else DestroyImmediate(instanceTextureArray);
                instanceTextureArray = null;
            }

            if (shadowMaterial != null)
            {
                if (Application.isPlaying) Destroy(shadowMaterial);
                else DestroyImmediate(shadowMaterial);
                shadowMaterial = null;
            }

            if (mainDirectionalLight != null && shadowGrabCommandBuffer != null)
            {
                mainDirectionalLight.RemoveCommandBuffer(LightEvent.AfterShadowMap, shadowGrabCommandBuffer);
                shadowGrabCommandBuffer.Release();
                shadowGrabCommandBuffer = null;
            }
        }

        private void OnDestroy()
        {
            OnDisable();
        }

        void LateUpdate()
        {
            argsBuffer?.Release();
            tBuffer?.Release();

            if (spacing == 0 || instanceMaterial == null) return;
            if (Camera.main == null) return;
            if (instanceMesh == null) return;

            Bounds cameraBounds = CalculateCameraBounds(Camera.main);
            
            //Args Buffer ---------------------------------------------------------------------------------
            argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            tBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);

            uint[] args = new uint[5];
            args[0] = (uint)instanceMesh.GetIndexCount(0);
            args[1] = 0; // Overwritten by CopyCount inside UpdateInstanceData
            args[2] = (uint)instanceMesh.GetIndexStart(0);
            args[3] = (uint)instanceMesh.GetBaseVertex(0);
            args[4] = 0;
            argsBuffer.SetData(args);

            // Dispatch the compute shader
            UpdateInstanceData(cameraBounds);

            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

            // Pass mesh height to the shader so it can normalize vertex.y for wind/AO
            float meshHeight = instanceMesh.bounds.max.y;
            if (meshHeight <= 0) meshHeight = 1.0f; // Safety fallback
            propertyBlock.SetFloat("_MeshHeight", meshHeight);

            // Texture Array Setup ------------------------------------------------------
            if (TexturesChanged())
            {
                GenerateTextureArray();
            }

            if (instanceTextureArray != null)
            {
                propertyBlock.SetTexture("_BaseColorTextureArray", instanceTextureArray);
                propertyBlock.SetFloat("_TextureCount", instanceTextureArray.depth);
                propertyBlock.SetVector("_BaseColorTextureArray_ST", new Vector4(textureTilingX, textureTilingY, 0, 0));
            }
            else
            {
                propertyBlock.SetFloat("_TextureCount", 0);
            }

            // Grab Shadow Map for receiving
            if (mainDirectionalLight == null || !mainDirectionalLight.isActiveAndEnabled)
            {
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional && l.shadows != LightShadows.None && l.isActiveAndEnabled)
                    {
                        mainDirectionalLight = l;
                        break;
                    }
                }
            }
            
            if (mainDirectionalLight != null && shadowGrabCommandBuffer == null)
            {
                shadowGrabCommandBuffer = new UnityEngine.Rendering.CommandBuffer();
                shadowGrabCommandBuffer.name = "Grab Directional Shadow Map";
                shadowGrabCommandBuffer.SetGlobalTexture("_GlobalShadowMap", new UnityEngine.Rendering.RenderTargetIdentifier(UnityEngine.Rendering.BuiltinRenderTextureType.CurrentActive));
                mainDirectionalLight.AddCommandBuffer(UnityEngine.Rendering.LightEvent.AfterShadowMap, shadowGrabCommandBuffer);
            }

            //Material Setup ------------------------------------------------------------
            propertyBlock.SetFloat("_DrawDistance", drawDistance);
            if (instancePositionsBuffer != null) propertyBlock.SetBuffer("_InstancePositions", instancePositionsBuffer);

            propertyBlock.SetFloat("_UseBiomeClipping", pixelAccurateBiomeClipping ? 1f : 0f);
            if (pixelAccurateBiomeClipping)
            {
                Texture biomeMap = Shader.GetGlobalTexture("_GlobalBiomeMap");
                if (biomeMap != null)
                {
                    propertyBlock.SetTexture("_GlobalBiomeMap", biomeMap);
                    propertyBlock.SetVector("_GlobalBiomeMap_Bounds", Shader.GetGlobalVector("_GlobalBiomeMap_Bounds"));
                    propertyBlock.SetVector("_GlobalBiomeMap_TexelSize", Shader.GetGlobalVector("_GlobalBiomeMap_TexelSize"));
                    propertyBlock.SetInt("_AllowedBiomes", (int)allowedBiomes);
                }
                else
                {
                    propertyBlock.SetFloat("_UseBiomeClipping", 0f);
                }
            }

            //Big Draw Call -------------------------------------------------------------
            Graphics.DrawMeshInstancedIndirect(instanceMesh, 0, instanceMaterial, cameraBounds, argsBuffer, 0, propertyBlock, ShadowCastingMode.Off, true);

            //Shadow-Only Draw Call -----------------------------------------------------
            if (castShadows)
            {
                if (shadowMaterial == null)
                {
                    Shader shadowShader = Shader.Find("RealLifeEnvironment/GPUInstancerShadowCaster");
                    if (shadowShader != null)
                        shadowMaterial = new Material(shadowShader);
                }
                if (shadowMaterial != null)
                {
                    // Sync properties from instanceMaterial so shadow geometry matches visible geometry
                    shadowMaterial.SetVector("_BaseScale", instanceMaterial.GetVector("_BaseScale"));
                    shadowMaterial.SetFloat("_InstanceScaleRandomness", instanceMaterial.GetFloat("_InstanceScaleRandomness"));
                    shadowMaterial.SetFloat("_DistanceA", instanceMaterial.GetFloat("_DistanceA"));
                    shadowMaterial.SetVector("_ScaleMultiplierA", instanceMaterial.GetVector("_ScaleMultiplierA"));
                    shadowMaterial.SetFloat("_DistanceB", instanceMaterial.GetFloat("_DistanceB"));
                    shadowMaterial.SetVector("_ScaleMultiplierB", instanceMaterial.GetVector("_ScaleMultiplierB"));
                    if (instanceMaterial.HasProperty("_TerrainAlignment")) shadowMaterial.SetFloat("_TerrainAlignment", instanceMaterial.GetFloat("_TerrainAlignment"));
                    shadowMaterial.SetFloat("_WindStrength", instanceMaterial.GetFloat("_WindStrength"));
                    shadowMaterial.SetVector("_WindScroll", instanceMaterial.GetVector("_WindScroll"));
                    shadowMaterial.SetFloat("_AlphaCutoff", instanceMaterial.GetFloat("_AlphaCutoff"));
                    shadowMaterial.SetFloat("_Cull", instanceMaterial.GetFloat("_Cull"));
                    if (instanceMaterial.HasProperty("_WindTexture"))
                    {
                        shadowMaterial.SetTexture("_WindTexture", instanceMaterial.GetTexture("_WindTexture"));
                        shadowMaterial.SetVector("_WindTexture_ST", instanceMaterial.GetVector("_WindTexture_ST"));
                    }
                    // Pass main camera position so distance-based scaling matches the visible pass
                    propertyBlock.SetVector("_MainCameraPosition", Camera.main.transform.position);

                    Graphics.DrawMeshInstancedIndirect(instanceMesh, 0, shadowMaterial, cameraBounds, argsBuffer, 0, propertyBlock, ShadowCastingMode.ShadowsOnly, false);
                }
            }
        }

        void UpdateInstanceData(Bounds cameraBounds)
        {
            if (computeShader == null)
                return;

            Vector2Int gridStartIndex = new Vector2Int(Mathf.FloorToInt(cameraBounds.min.x / spacing), Mathf.FloorToInt(cameraBounds.min.z / spacing));
            Vector2Int gridEndIndex = new Vector2Int(Mathf.CeilToInt(cameraBounds.max.x / spacing), Mathf.CeilToInt(cameraBounds.max.z / spacing));
            Vector2Int gridSize = gridEndIndex - gridStartIndex;

            instanceStats = $"Places Considered: {gridSize.x * gridSize.y:N0} (Grid: {gridSize.x}x{gridSize.y})";

            if (instancePositionsBuffer == null || currentBufferCount != maxBufferCount)
            {
                instancePositionsBuffer?.Release();
                instancePositionsBuffer = new ComputeBuffer((int)(1000000 * maxBufferCount), sizeof(float) * 7, ComputeBufferType.Append);
                currentBufferCount = maxBufferCount;
            }

            computeShader.SetMatrix("_VPMatrix", Camera.main.projectionMatrix * Camera.main.worldToCameraMatrix);
            computeShader.SetFloat("_FullDensityDistance", fullDensityDistance);
            computeShader.SetVector("_CameraPosition", Camera.main.transform.position);
            computeShader.SetFloat("_DrawDistance", drawDistance);
            computeShader.SetFloat("_Spacing", spacing);
            computeShader.SetInt("_AllowedBiomes", (int)allowedBiomes);
            computeShader.SetFloat("_ForceSpawnDistance", forceSpawnDistance);
            computeShader.SetInt("_AlignToTerrainMesh", alignToTerrainMesh ? 1 : 0);
            computeShader.SetFloat("_TerrainBaseScale", terrainBaseScale);
            computeShader.SetInt("_TerrainGridResolution", terrainGridResolution);
            computeShader.SetFloat("_HeightFadeStart", heightFadeStart);
            computeShader.SetFloat("_HeightFadeEnd", heightFadeEnd);
            computeShader.SetInt("_UseNearPlanes", useNearPlanes ? 1 : 0);
            computeShader.SetFloat("_NearPlaneStart", nearPlaneStart);
            computeShader.SetFloat("_NearPlaneEnd", nearPlaneEnd);
            computeShader.SetFloat("_FrustumBuffer", frustumBuffer);
            computeShader.SetVector("_GridStartIndex", (Vector2)gridStartIndex);
            computeShader.SetVector("_GridSize", (Vector2)gridSize);
            
            if (biomeWeightsBuffer == null || biomeWeightsBuffer.count != 20 * 32)
            {
                biomeWeightsBuffer?.Release();
                biomeWeightsBuffer = new ComputeBuffer(20 * 32, sizeof(float));
            }
            biomeWeightsBuffer.SetData(biomeCumulativeWeights);
            computeShader.SetBuffer(0, "_BiomeCumulativeWeights", biomeWeightsBuffer);
            
            computeShader.SetInt("_TextureCount", instanceTextureArray != null ? instanceTextureArray.depth : 0);
            
            // Set terrain heightmap and biome map properties
            Texture customHeightmap = Shader.GetGlobalTexture("_CustomTerrainHeightmap");
            if (customHeightmap != null)
                computeShader.SetTexture(0, "_CustomTerrainHeightmap", customHeightmap);
            else
                computeShader.SetTexture(0, "_CustomTerrainHeightmap", Texture2D.blackTexture);

            Texture biomeMap = Shader.GetGlobalTexture("_GlobalBiomeMap");
            if (biomeMap != null)
                computeShader.SetTexture(0, "_GlobalBiomeMap", biomeMap);
            else
                computeShader.SetTexture(0, "_GlobalBiomeMap", Texture2D.whiteTexture);
                
            computeShader.SetVector("_CustomTerrainHeightmap_Bounds", Shader.GetGlobalVector("_CustomTerrainHeightmap_Bounds"));
            computeShader.SetVector("_CustomTerrainHeightmap_TexelSize", Shader.GetGlobalVector("_CustomTerrainHeightmap_TexelSize"));
            computeShader.SetFloat("_CustomTerrainHeightmap_Multiplier", Shader.GetGlobalFloat("_CustomTerrainHeightmap_Multiplier"));
            computeShader.SetVector("_GlobalBiomeMap_Bounds", Shader.GetGlobalVector("_GlobalBiomeMap_Bounds"));
            computeShader.SetVector("_GlobalBiomeMap_TexelSize", Shader.GetGlobalVector("_GlobalBiomeMap_TexelSize"));
            
            computeShader.SetBuffer(0, "_InstancePositions", instancePositionsBuffer);

            instancePositionsBuffer.SetCounterValue(0);
            
            // Dispatch compute shader
            int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / 8f);
            int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / 8f);
            
            // Ensure thread groups are at least 1x1x1
            threadGroupsX = Mathf.Max(1, threadGroupsX);
            threadGroupsY = Mathf.Max(1, threadGroupsY);
            
            computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

            // Copy counter value to indirect arguments argsBuffer
            ComputeBuffer.CopyCount(instancePositionsBuffer, argsBuffer, sizeof(uint));

            if (previewVisibleInstanceCount)
            {
                ComputeBuffer.CopyCount(instancePositionsBuffer, tBuffer, 0);
            }
        }

        private void OnGUI()
        {
            if (previewVisibleInstanceCount)
            {
                if (Camera.main == null) return;
                GUI.contentColor = Color.black;
                GUIStyle style = new GUIStyle();
                style.fontSize = 25;

                uint[] count = new uint[1];
                tBuffer.GetData(count);//Reading back data from GPU

                Bounds cameraBounds = CalculateCameraBounds(Camera.main);
                Vector2Int gridSize = new Vector2Int(Mathf.CeilToInt(cameraBounds.size.x / spacing), Mathf.CeilToInt(cameraBounds.size.z / spacing));

                GUI.Label(new Rect(50, 50, 400, 200), "Dispatch Size : " + gridSize.x + "x" + gridSize.y + " = " + (gridSize.x * gridSize.y), style);
                GUI.Label(new Rect(50, 80, 400, 200), "Visible Instance Count : " + count[0], style);
            }
        }

        Bounds CalculateCameraBounds(Camera camera)
        {
            Vector3 ntopLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, camera.nearClipPlane));
            Vector3 ntopRight = camera.ViewportToWorldPoint(new Vector3(1, 1, camera.nearClipPlane));
            Vector3 nbottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, camera.nearClipPlane));
            Vector3 nbottomRight = camera.ViewportToWorldPoint(new Vector3(1, 0, camera.nearClipPlane));

            Vector3 ftopLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, drawDistance));
            Vector3 ftopRight = camera.ViewportToWorldPoint(new Vector3(1, 1, drawDistance));
            Vector3 fbottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, drawDistance));
            Vector3 fbottomRight = camera.ViewportToWorldPoint(new Vector3(1, 0, drawDistance));

            float[] xValues = new float[] { ftopLeft.x, ftopRight.x, ntopLeft.x, ntopRight.x, fbottomLeft.x, fbottomRight.x, nbottomLeft.x, nbottomRight.x };
            float startX = xValues.Max();
            float endX = xValues.Min();

            float[] yValues = new float[] { ftopLeft.y, ftopRight.y, ntopLeft.y, ntopRight.y, fbottomLeft.y, fbottomRight.y, nbottomLeft.y, nbottomRight.y };
            float startY = yValues.Max();
            float endY = yValues.Min();

            float[] zValues = new float[] { ftopLeft.z, ftopRight.z, ntopLeft.z, ntopRight.z, fbottomLeft.z, fbottomRight.z, nbottomLeft.z, nbottomRight.z };
            float startZ = zValues.Max();
            float endZ = zValues.Min();

            Vector3 center = new Vector3((startX + endX) / 2, (startY + endY) / 2, (startZ + endZ) / 2);
            Vector3 size = new Vector3(Mathf.Abs(startX - endX), Mathf.Abs(startY - endY), Mathf.Abs(startZ - endZ));

            Bounds bounds = new Bounds(center, size);
            
            // Ensure the grid explicitly covers a square around the camera matching the forceSpawnDistance, plus spacing
            Vector3 camPos = camera.transform.position;
            float spawnBoxRadius = forceSpawnDistance + spacing + 5f; // Extra 5f safety margin
            bounds.Encapsulate(camPos + new Vector3(spawnBoxRadius, 0, spawnBoxRadius));
            bounds.Encapsulate(camPos - new Vector3(spawnBoxRadius, 0, spawnBoxRadius));

            // Add general padding proportional to draw distance and frustum buffer for edge cases
            bounds.Expand(drawDistance * frustumBuffer + 10f);
            
            return bounds;
        }

        bool TexturesChanged()
        {
            int hash = 17;
            hash = hash * 31 + textureRotation.GetHashCode();
            int nonNullCount = 0;
            if (instanceTextures != null)
            {
                foreach (var tex in instanceTextures)
                {
                    if (tex != null)
                    {
                        hash = hash * 31 + tex.GetHashCode();
                        nonNullCount++;
                    }
                }
            }
            if (defaultTextureWeights != null)
            {
                foreach (var weight in defaultTextureWeights)
                {
                    hash = hash * 31 + weight.GetHashCode();
                }
            }
            if (biomeTextureWeightsOverrides != null)
            {
                foreach (var overrideWeight in biomeTextureWeightsOverrides)
                {
                    hash = hash * 31 + overrideWeight.biome.GetHashCode();
                    if (overrideWeight.weights != null)
                    {
                        foreach (var weight in overrideWeight.weights)
                        {
                            hash = hash * 31 + weight.GetHashCode();
                        }
                    }
                }
            }

            bool arrayIsMissing = (nonNullCount > 0 && instanceTextureArray == null);

            if (hash != lastTextureHash || arrayIsMissing)
            {
                lastTextureHash = hash;
                return true;
            }
            return false;
        }

        void GenerateTextureArray()
        {
            if (biomeCumulativeWeights == null || biomeCumulativeWeights.Length != 20 * 32)
            {
                biomeCumulativeWeights = new float[20 * 32];
            }

            if (instanceTextureArray != null)
            {
                if (Application.isPlaying) Destroy(instanceTextureArray);
                else DestroyImmediate(instanceTextureArray);
                instanceTextureArray = null;
            }

            if (instanceTextures == null || instanceTextures.Length == 0) return;

            var textures = System.Array.FindAll(instanceTextures, t => t != null);
            if (textures.Length == 0) return;

            int width = textures[0].width;
            int height = textures[0].height;
            TextureFormat format = textures[0].format;
            bool mipChain = textures[0].mipmapCount > 1;

            var matchingTextures = System.Array.FindAll(textures, t => t.width == width && t.height == height && t.format == format);
            if (matchingTextures.Length == 0) return;

            instanceTextureArray = new Texture2DArray(width, height, matchingTextures.Length, format, mipChain);
            instanceTextureArray.wrapMode = TextureWrapMode.Repeat;
            instanceTextureArray.filterMode = FilterMode.Bilinear;

            float[] validDefaultWeights = new float[32];
            int validTexCount = 0;

            for (int i = 0; i < instanceTextures.Length; i++)
            {
                Texture2D tex = instanceTextures[i];
                if (tex != null && tex.width == width && tex.height == height && tex.format == format)
                {
                    validDefaultWeights[validTexCount] = (defaultTextureWeights != null && i < defaultTextureWeights.Length) ? defaultTextureWeights[i] : 1.0f;
                    validTexCount++;
                }
            }

            for (int b = 0; b < 20; b++)
            {
                BiomeMask currentBiomeMask = (BiomeMask)(1 << b);
                
                float[] weightsToUse = validDefaultWeights;
                
                if (biomeTextureWeightsOverrides != null)
                {
                    foreach (var ov in biomeTextureWeightsOverrides)
                    {
                        if ((ov.biome & currentBiomeMask) != 0 && ov.weights != null)
                        {
                            weightsToUse = new float[32];
                            int idx = 0;
                            for (int i = 0; i < instanceTextures.Length; i++)
                            {
                                Texture2D tex = instanceTextures[i];
                                if (tex != null && tex.width == width && tex.height == height && tex.format == format)
                                {
                                    weightsToUse[idx] = (i < ov.weights.Length) ? ov.weights[i] : 1.0f;
                                    idx++;
                                }
                            }
                            break;
                        }
                    }
                }

                float totalWeight = 0;
                for (int i = 0; i < validTexCount; i++) totalWeight += weightsToUse[i];
                if (totalWeight <= 0) totalWeight = 1;

                float currentSum = 0;
                for (int i = 0; i < 32; i++)
                {
                    if (i < validTexCount)
                    {
                        currentSum += weightsToUse[i];
                        biomeCumulativeWeights[b * 32 + i] = currentSum / totalWeight;
                    }
                    else
                    {
                        biomeCumulativeWeights[b * 32 + i] = 1.0f;
                    }
                }

                if (validTexCount > 0 && validTexCount <= 32)
                {
                    biomeCumulativeWeights[b * 32 + validTexCount - 1] = 1.0f;
                }
            }

            for (int i = 0; i < matchingTextures.Length; i++)
            {
                int rotatedIndex = (i + Mathf.Max(0, textureRotation)) % matchingTextures.Length;
                int mipsToCopy = Mathf.Min(matchingTextures[rotatedIndex].mipmapCount, instanceTextureArray.mipmapCount);
                for (int mip = 0; mip < mipsToCopy; mip++)
                {
                    Graphics.CopyTexture(matchingTextures[rotatedIndex], 0, mip, instanceTextureArray, i, mip);
                }
            }
        }
    }
}
