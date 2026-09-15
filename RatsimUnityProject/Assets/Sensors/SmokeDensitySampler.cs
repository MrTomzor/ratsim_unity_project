using UnityEngine;
using RealLifeEnvironment;

using UnityEngine.Rendering.PostProcessing;

public class SmokeDensitySampler : MonoBehaviour
{
    [SerializeField] private RealTerrain realTerrain;
    [SerializeField] private PostProcessVolume volume;

    private VolumetricSmokeEffect smokeEffect;

    // Cached terrain bounds and texture
    private Vector2 terrainCenter;
    private float invSizeX = 1f;
    private float invSizeZ = 1f;
    private Texture2D waterDistanceTexture;

    // Cached smoke settings
    private float invMaxWaterDistance = 1f;
    private float smokeHeight = 0f;
    private float fadeBottom = 0f;
    private float invFadeRange = 1f;
    private float globalDensityMultiplier = 0f;

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

    public void Initialize()
    {
        if (realTerrain == null)
        {
            realTerrain = Object.FindAnyObjectByType<RealTerrain>();
        }

        if (realTerrain != null)
        {
            terrainCenter = realTerrain.center;
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
        float uvX = (worldPos.x - terrainCenter.x) * invSizeX + 0.5f;
        float uvY = (worldPos.z - terrainCenter.y) * invSizeZ + 0.5f;
        
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
}

