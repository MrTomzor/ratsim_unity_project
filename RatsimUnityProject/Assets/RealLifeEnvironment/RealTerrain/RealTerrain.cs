using UnityEngine;
using System.Collections.Generic;

namespace RealLifeEnvironment
{
    public enum DistanceTextureFormat
    {
        RHalf,
        RFloat
    }

    [ExecuteAlways]
    public class RealTerrain : MonoBehaviour
    {
        [Header("Clipmap Settings")]
        public Transform viewer;
        public Material terrainMaterial;
        
        [Tooltip("Must be a multiple of 4 (e.g. 128, 256)")]
        public int gridResolution = 128;
        
        public int levels = 5;
        
        [Tooltip("Scale of the highest detail (LOD 0) grid in units")]
        public float baseScale = 1f;

        [Header("Heightmap Settings")]
        [Tooltip("The RFloat texture containing the height data.")]
        public Texture2D heightmapTexture;

        [Tooltip("Multiplier for the heightmap values.")]
        [Range(0f, 10f)]
        public float heightmapMultiplier = 1f;

        [Header("Biome Settings")]
        [Tooltip("The R8 texture containing biome data.")]
        public Texture2D biomeTexture;
        private Texture2D _runtimeBiomeTexture;

        [Header("Road Mask Settings")]
        [Tooltip("RGB texture mask for roads/biomes. Must match biomeTexture dimensions.")]
        public Texture2D roadMaskTexture;
        public List<BiomeMaskMapping> roadMaskMappings = new List<BiomeMaskMapping>();

        [System.Serializable]
        public class BiomeMaskMapping
        {
            public Color maskColor = Color.white;
            public float targetBiomeValue = 1f;
            [Tooltip("Tolerance for color matching (0 to 1)")]
            public float colorTolerance = 0.05f;
        }

        [Header("Croplands Randomization Settings")]
        [Tooltip("The biome value representing croplands in the original dataset.")]
        public float originalCroplandValue = 12f; // Assuming 12, but the user can change it in the inspector
        [Tooltip("Tolerance for matching the original cropland value")]
        public float croplandValueTolerance = 0.05f;
        [Tooltip("The pixel radius to check for neighboring fields. Fields within this distance will try to get different values.")]
        public int croplandNeighborRadius = 5;
        [Tooltip("The list of possible biome values to assign to different cropland fields.")]
        public List<float> randomizedCroplandValues = new List<float>() { 160f, 170f, 180f, 190f };

        [Header("Distance Map Settings")]
        public float distanceTargetBiomeValue = 12f;
        public float distanceBiomeValueTolerance = 0.05f;
        [Tooltip("If true, calculates distance from the boundary inwards. Pixels outside the target biome will be 0.")]
        public bool distanceFromOtherBiomes = false;
        public int distanceTextureResolution = 1024;
        public DistanceTextureFormat distanceTextureFormat = DistanceTextureFormat.RHalf;

        [Header("Biome Edge Settings")]
        public float edgeBiomeToChange = 12f;
        public float edgeBiomeToChangeTo = 14f;
        public float edgeDistanceUnits = 10f;

        [Header("Water Settings")]
        [Tooltip("The texture containing water distance data.")]
        public Texture2D waterDistanceTexture;

        [Header("Bounds Settings")]
        [Tooltip("The XZ center position of the area.")]
        public Vector2 center = Vector2.zero;

        [Tooltip("The physical size of the texture area in the X direction.")]
        public float sizeX = 4000f;

        [Tooltip("The physical size of the texture area in the Z direction.")]
        public float sizeZ = 4000f;

        [Header("Debug")]
        [Tooltip("If true, draws a gizmo indicating the bounds of the heightmap in the scene view.")]
        public bool drawBoundsGizmo = true;

        private class ClipmapLevel
        {
            public GameObject go;
            public float scale;
        }

        private List<ClipmapLevel> _levels = new List<ClipmapLevel>();

        private void OnEnable()
        {
            ApplyHeightmap();
        }

        private void OnValidate()
        {
            ApplyHeightmap();
        }

        private void OnDisable()
        {
            RealTerrainHeight.SetCustomHeightmap(null, Vector2.zero, 0f, 0f, 1f);
            Shader.SetGlobalVector("_GlobalBiomeMap_Bounds", Vector4.zero);

            if (_runtimeBiomeTexture != null)
            {
                if (Application.isPlaying)
                    Destroy(_runtimeBiomeTexture);
                else
                    DestroyImmediate(_runtimeBiomeTexture);
                _runtimeBiomeTexture = null;
            }
        }

        [ContextMenu("Apply Textures")]
        public void ApplyHeightmap()
        {
            RealTerrainHeight.SetCustomHeightmap(heightmapTexture, center, sizeX, sizeZ, heightmapMultiplier);

            Texture2D texToUse = _runtimeBiomeTexture != null ? _runtimeBiomeTexture : biomeTexture;

            if (texToUse != null)
            {
                Shader.SetGlobalTexture("_GlobalBiomeMap", texToUse);
                Shader.SetGlobalVector("_GlobalBiomeMap_TexelSize", new Vector4(1f / texToUse.width, 1f / texToUse.height, texToUse.width, texToUse.height));
                Shader.SetGlobalVector("_GlobalBiomeMap_Bounds", new Vector4(center.x, center.y, sizeX, sizeZ));
            }
            else
            {
                Shader.SetGlobalVector("_GlobalBiomeMap_Bounds", Vector4.zero);
            }

            if (waterDistanceTexture != null)
            {
                Shader.SetGlobalTexture("_GlobalWaterDistanceMap", waterDistanceTexture);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (drawBoundsGizmo)
            {
                Gizmos.color = Color.magenta;
                Vector3 gizmoCenter = new Vector3(center.x, transform.position.y, center.y);
                Vector3 size = new Vector3(sizeX, 0.1f, sizeZ);
                Gizmos.DrawWireCube(gizmoCenter, size);
            }
        }

        public void ApplyBiomeModifiers()
        {
            if (biomeTexture == null) return;
            
            BiomeModifier[] modifiers = FindObjectsByType<BiomeModifier>(FindObjectsSortMode.None);
            if (modifiers.Length == 0) return;

            if (!biomeTexture.isReadable)
            {
                Debug.LogError("RealTerrain: Cannot apply BiomeModifiers because biomeTexture is not readable. Enable Read/Write in its import settings.");
                return;
            }

            if (_runtimeBiomeTexture == null)
            {
                _runtimeBiomeTexture = Instantiate(biomeTexture);
                _runtimeBiomeTexture.name = biomeTexture.name + "_Runtime";
            }

            int texWidth = _runtimeBiomeTexture.width;
            int texHeight = _runtimeBiomeTexture.height;
            bool modified = false;

            // Note: road mask logic has been moved to an offline editor script (RealTerrainEditor.cs).

            foreach (var mod in modifiers)
            {
                Vector3 worldPos = mod.transform.position;
                float uvX = (worldPos.x - center.x) / sizeX + 0.5f;
                float uvY = (worldPos.z - center.y) / sizeZ + 0.5f;
                
                int centerX_Pixel = Mathf.RoundToInt(uvX * texWidth);
                int centerY_Pixel = Mathf.RoundToInt(uvY * texHeight);

                float radiusPixelsX = (mod.radius / sizeX) * texWidth;
                float radiusPixelsY = (mod.radius / sizeZ) * texHeight;
                
                int startX = Mathf.Max(0, Mathf.FloorToInt(centerX_Pixel - radiusPixelsX));
                int endX = Mathf.Min(texWidth - 1, Mathf.CeilToInt(centerX_Pixel + radiusPixelsX));
                
                int startY = Mathf.Max(0, Mathf.FloorToInt(centerY_Pixel - radiusPixelsY));
                int endY = Mathf.Min(texHeight - 1, Mathf.CeilToInt(centerY_Pixel + radiusPixelsY));

                Color biomeColor = new Color(mod.biomeValue / 255f, 0f, 0f, 0f);

                for (int y = startY; y <= endY; y++)
                {
                    for (int x = startX; x <= endX; x++)
                    {
                        float pU = (x + 0.5f) / texWidth;
                        float pV = (y + 0.5f) / texHeight;
                        
                        float worldX = (pU - 0.5f) * sizeX + center.x;
                        float worldZ = (pV - 0.5f) * sizeZ + center.y;
                        
                        float dx = worldX - worldPos.x;
                        float dz = worldZ - worldPos.z;
                        
                        if ((dx * dx + dz * dz) <= (mod.radius * mod.radius))
                        {
                            _runtimeBiomeTexture.SetPixel(x, y, biomeColor);
                            modified = true;
                        }
                    }
                }
            }

            if (modified)
            {
                _runtimeBiomeTexture.Apply();
                Debug.Log($"RealTerrain: Applied {modifiers.Length} BiomeModifiers to the runtime biome texture.");
                
                // Update global shader property to use the modified runtime texture
                ApplyHeightmap();
            }
        }

        void Start()
        {
            if (!Application.isPlaying) return;

            ApplyBiomeModifiers();

            if (viewer == null)
            {
                if (Camera.main != null) viewer = Camera.main.transform;
                else
                {
                    Debug.LogError("RealTerrain: No viewer assigned!");
                    return;
                }
            }

            if (terrainMaterial == null)
            {
                Debug.LogWarning("RealTerrain: No material assigned. Terrain will be invisible.");
            }

            // Ensure resolution is a multiple of 4
            if (gridResolution % 4 != 0) 
                gridResolution = Mathf.CeilToInt(gridResolution / 4f) * 4;

            Mesh centerMesh = RealTerrainMeshGenerator.GenerateCenterBlock(gridResolution);
            Mesh ringMesh = RealTerrainMeshGenerator.GenerateRingBlock(gridResolution);

            for (int i = 0; i < levels; i++)
            {
                ClipmapLevel level = new ClipmapLevel();
                level.scale = baseScale * Mathf.Pow(2, i);
                
                level.go = new GameObject($"LOD_{i}");
                level.go.transform.SetParent(transform, false);
                level.go.layer = 9;
                
                MeshFilter mf = level.go.AddComponent<MeshFilter>();
                mf.sharedMesh = (i == 0) ? centerMesh : ringMesh;
                
                MeshRenderer mr = level.go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = terrainMaterial;
                
                // Pass the LOD level to the shader to prevent Z-fighting by slightly offsetting heights
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                mpb.SetFloat("_Level", i);
                mr.SetPropertyBlock(mpb);
                
                // Scale the GameObject
                level.go.transform.localScale = new Vector3(level.scale, 1f, level.scale);
                
                _levels.Add(level);
            }
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (viewer == null || _levels.Count == 0) return;

            Vector3 viewerPos = viewer.position;

            for (int i = 0; i < levels; i++)
            {
                ClipmapLevel level = _levels[i];
                float snapIncrement = level.scale;
                
                // Use Round to ensure the viewer is always within snapIncrement/2 of the center
                float snappedX = Mathf.Round(viewerPos.x / snapIncrement) * snapIncrement;
                float snappedZ = Mathf.Round(viewerPos.z / snapIncrement) * snapIncrement;
                
                level.go.transform.position = new Vector3(snappedX, 0, snappedZ);
            }
        }
    }
}
