using UnityEngine;
using RealLifeEnvironment;

using UnityEngine.Rendering.PostProcessing;
using Unity.Collections;

public class SmokeDensitySampler : MonoBehaviour
{
    [SerializeField] private RealTerrain realTerrain;
    [SerializeField] private PostProcessVolume volume;

    private VolumetricSmokeEffect smokeEffect;

    // Cached terrain bounds and texture
    private Vector2 center;
    private float invSizeX = 1f;
    private float invSizeZ = 1f;
    private Texture2D waterDistanceTexture;

    // Cached smoke settings
    private float invMaxWaterDistance = 1f;
    private float smokeHeight = 0f;
    private float fadeBottom = 0f;
    private float invFadeRange = 1f;
    private float globalDensityMultiplier = 0f;

    // Cached burst data
    private NativeArray<float> waterDistanceData;
    private NativeArray<float> heightmapData;
    private Vector2Int waterDistanceRes, heightmapRes;
    private float heightMultiplier;
    private NativeArray<float> curveLutNative;

    // Precomputed 256-sample curve lookup table matching shader resolution
    private readonly float[] curveLut = new float[256];
    private bool isInitialized = false;

    public bool IsSmokeActive => isInitialized && smokeEffect != null && smokeEffect.active && globalDensityMultiplier > 0f;

    private void Awake()
    {
        Initialize();
    }

    private void Start()
    {
        if (!isInitialized)
        {
            Initialize();
        }
    }

    public void OnDestroy()
    {
        if (curveLutNative.IsCreated)
            curveLutNative.Dispose();
        if (waterDistanceData.IsCreated)
            waterDistanceData.Dispose();
        if (heightmapData.IsCreated)
            heightmapData.Dispose();
    }

    public void Initialize()
    {
        if (realTerrain == null)
        {
            realTerrain = Object.FindAnyObjectByType<RealTerrain>();
        }

        if (realTerrain != null)
        {
            center = realTerrain.center;
            invSizeX = realTerrain.sizeX > 0f ? 1f / realTerrain.sizeX : 1f;
            invSizeZ = realTerrain.sizeZ > 0f ? 1f / realTerrain.sizeZ : 1f;
            waterDistanceTexture = realTerrain.waterDistanceTexture;
        }

        if (volume == null)
        {
            volume = Object.FindAnyObjectByType<PostProcessVolume>();
        }

        if (volume != null && volume.profile != null)
        {
            volume.profile.TryGetSettings(out smokeEffect);
        }

        RefreshSettings();
        isInitialized = true;
    }

    public void RefreshSettings()
    {
        if (smokeEffect == null) return;

        float maxWaterDist = Mathf.Max(0.0001f, smokeEffect.maxWaterDistance.value);
        invMaxWaterDistance = 1f / maxWaterDist;

        smokeHeight = smokeEffect.smokeHeight.value;
        float smokeHeightFade = smokeEffect.smokeHeightFade.value;
        fadeBottom = Mathf.Max(0f, smokeHeight - smokeHeightFade);
        invFadeRange = 1f / Mathf.Max(0.0001f, smokeHeight - fadeBottom);

        globalDensityMultiplier = smokeEffect.globalDensityMultiplier.value;

        // Precompute curve lookup table matching VolumetricSmoke shader resolution (256 samples)
        if (smokeEffect.waterDistanceDensityCurve.value != null)
        {
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                curveLut[i] = smokeEffect.waterDistanceDensityCurve.value.Evaluate(t);
            }
        }
        else
        {
            for (int i = 0; i < 256; i++)
            {
                curveLut[i] = 0f;
            }
        }

        if (waterDistanceTexture != null)
        {
            waterDistanceRes = new Vector2Int(waterDistanceTexture.width, waterDistanceTexture.height);
            int totalPixels = waterDistanceRes.x * waterDistanceRes.y;

            if (!waterDistanceData.IsCreated || waterDistanceData.Length != totalPixels)
            {
                if (waterDistanceData.IsCreated) waterDistanceData.Dispose();
                waterDistanceData = new NativeArray<float>(totalPixels, Allocator.Persistent);
            }

            Color[] pixels = waterDistanceTexture.GetPixels(0);
            for (int i = 0; i < totalPixels; i++)
            {
                waterDistanceData[i] = pixels[i].r;
            }
        }

        if (realTerrain != null && realTerrain.heightmapTexture != null)
        {
            heightmapRes = new Vector2Int(realTerrain.heightmapTexture.width, realTerrain.heightmapTexture.height);
            int totalPixels = heightmapRes.x * heightmapRes.y;
            heightMultiplier = realTerrain.heightmapMultiplier;

            if (!heightmapData.IsCreated || heightmapData.Length != totalPixels)
            {
                if (heightmapData.IsCreated) heightmapData.Dispose();
                heightmapData = new NativeArray<float>(totalPixels, Allocator.Persistent);
            }

            Color[] pixels = realTerrain.heightmapTexture.GetPixels(0);
            for (int i = 0; i < totalPixels; i++)
            {
                heightmapData[i] = pixels[i].r;
            }
        }

        if (!curveLutNative.IsCreated)
            curveLutNative = new NativeArray<float>(256, Allocator.Persistent);
        curveLutNative.CopyFrom(curveLut);
        

        
    }

    private void Update()
    {
        // Keep runtime-tunable parameters up to date with negligible overhead
        if (smokeEffect != null)
        {
            float maxWaterDist = Mathf.Max(0.0001f, smokeEffect.maxWaterDistance.value);
            invMaxWaterDistance = 1f / maxWaterDist;
            smokeHeight = smokeEffect.smokeHeight.value;
            float smokeHeightFade = smokeEffect.smokeHeightFade.value;
            fadeBottom = Mathf.Max(0f, smokeHeight - smokeHeightFade);
            invFadeRange = 1f / Mathf.Max(0.0001f, smokeHeight - fadeBottom);
            globalDensityMultiplier = smokeEffect.globalDensityMultiplier.value;
        }
    }

    /// <summary>
    /// Sample smoke density at a world position (matches VolumetricSmoke.shader).
    /// Returns 0 if outside bounds, above smoke height, or no smoke present.
    /// </summary>
    public float SampleDensity(Vector3 worldPos)
    {
        if (!isInitialized)
        {
            Initialize();
        }

        if (realTerrain == null || smokeEffect == null || !smokeEffect.active || globalDensityMultiplier <= 0f) return 0f;

        // 1. World XZ to UV
        float uvX = (worldPos.x - center.x) * invSizeX + 0.5f;
        float uvY = (worldPos.z - center.y) * invSizeZ + 0.5f;
        
        if (uvX < 0f || uvX > 1f || uvY < 0f || uvY > 1f) return 0f;

        // 2. Water distance -> density curve
        float waterDist = 1.0f;
        if (waterDistanceTexture != null)
        {
            waterDist = GetPixelBilinearAccurate.Get(waterDistanceTexture, uvX, uvY).r;
        }

        float normDist = Mathf.Clamp01(waterDist * invMaxWaterDistance);
        int lutIndex = Mathf.Clamp((int)(normDist * 255f), 0, 255);
        float density = curveLut[lutIndex];

        if (density <= 0f) return 0f; // early out

        // 3. Height fade (cheap bilinear terrain height)
        float terrainH = RealTerrainHeight.GetTerrainHeightCheap(new Vector2(worldPos.x, worldPos.z));
        float above = worldPos.y - terrainH;
        
        if (above >= smokeHeight) return 0f; // above smoke layer entirely

        float heightFade = 1f - Mathf.Clamp01((above - fadeBottom) * invFadeRange);
        density *= heightFade;

        // 4. Global multiplier
        density *= globalDensityMultiplier;
        return density;
    }

        public SmokeDensityJobData GetJobData()
    {
        if (!isInitialized){
            Initialize();
        }
        return new SmokeDensityJobData
        {
            waterDistanceData = waterDistanceData,
            waterDistanceRes = waterDistanceRes,
            heightmapData = heightmapData,
            heightmapRes = heightmapRes,
            curveLut = curveLutNative,
            center = center,
            invSizeX = invSizeX,
            invSizeZ = invSizeZ,
            heightMultiplier = heightMultiplier,
            invMaxWaterDistance = invMaxWaterDistance,
            smokeHeight = smokeHeight,
            fadeBottom = fadeBottom,
            invFadeRange = invFadeRange,
            globalDensityMultiplier = globalDensityMultiplier,
            isSmokeActive = IsSmokeActive,
            
        };
    }
}


public struct SmokeDensityJobData
{
    [ReadOnly] public NativeArray<float> waterDistanceData;
    public Vector2Int waterDistanceRes;

    [ReadOnly] public NativeArray<float> heightmapData;
    public Vector2Int heightmapRes;

    [ReadOnly] public NativeArray<float> curveLut;

    public Vector2 center;
    public float invSizeX;
    public float invSizeZ;

    public float heightMultiplier;

    public float invMaxWaterDistance;
    public float smokeHeight;
    public float fadeBottom;
    public float invFadeRange;
    public float globalDensityMultiplier;
    public bool isSmokeActive;

    public static float SampleBilinear(NativeArray<float> data, int width, int height, float u, float v)
    {
        // Align GPU texel center convention like GetPixelBilinearAccurate
        u -= 0.5f / width;
        v -= 0.5f / height;

        float px = Mathf.Clamp01(u) * (width - 1);
        float py = Mathf.Clamp01(v) * (height - 1);

        int x0 = (int)px;
        int y0 = (int)py;
        int x1 = Mathf.Min(x0 + 1, width - 1);
        int y1 = Mathf.Min(y0 + 1, height - 1);

        float fx = px - x0;
        float fy = py - y0;

        float v00 = data[y0 * width + x0];
        float v10 = data[y0 * width + x1];
        float v01 = data[y1 * width + x0];
        float v11 = data[y1 * width + x1];

        float top = Mathf.Lerp(v00, v10, fx);
        float bottom = Mathf.Lerp(v01, v11, fx);
        return Mathf.Lerp(top, bottom, fy);
    }

    public float SampleDensity(Vector3 worldPos)
    {
        if (!isSmokeActive) return 0f;

        // 1. World XZ to UV
        float uvX = (worldPos.x - center.x) * invSizeX + 0.5f;
        float uvY = (worldPos.z - center.y) * invSizeZ + 0.5f;

        if (uvX < 0f || uvX > 1f || uvY < 0f || uvY > 1f) return 0f;

        // 2. Water distance to density curve
        float waterDist = 1.0f;
        if (waterDistanceData.IsCreated && waterDistanceRes.x > 0)
        {
            waterDist = SampleBilinear(waterDistanceData, waterDistanceRes.x, waterDistanceRes.y, uvX, uvY);
        }

        float normDist = Mathf.Clamp01(waterDist * invMaxWaterDistance);
        int lutIndex = Mathf.Clamp((int)(normDist * 255f), 0, 255);
        float density = curveLut[lutIndex];

        if (density <= 0f) return 0f;

        // 3. Terrain height fade
        float terrainH = 0f;
        if (heightmapData.IsCreated && heightmapRes.x > 0)
        {
            terrainH = SampleBilinear(heightmapData, heightmapRes.x, heightmapRes.y, uvX, uvY) * heightMultiplier;
        }

        float above = worldPos.y - terrainH;
        if (above >= smokeHeight) return 0f;

        float heightFade = 1f - Mathf.Clamp01((above - fadeBottom) * invFadeRange);
        density *= heightFade;

        // 4. Global multiplier
        density *= globalDensityMultiplier;
        return density;
    }
}

