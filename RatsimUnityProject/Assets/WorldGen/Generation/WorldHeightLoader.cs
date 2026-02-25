using UnityEngine;

public class WorldHeightLoader : WorldLoadingModule {

    public static WorldHeightLoader instance;

    [Header("General")]
    public string mode = "superflat";

    [Header("Superflat")]
    public float superflatHeight = 0f;

    [Header("Perlin")]
    public float perlinScale     = 200f;
    public float perlinAmplitude = 50f;

    private float _perlinOffsetX;
    private float _perlinOffsetZ;

    private void Awake() {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    private new void OnEnable() {
        base.OnEnable();
        LoadParams();
    }

    private void LoadParams() {
        mode             = WorldLoadingController.GetParamString("height_generation/mode",           mode);
        superflatHeight  = WorldLoadingController.GetParamFloat ("height_generation/superflat_height", superflatHeight);
        perlinScale      = WorldLoadingController.GetParamFloat ("height_generation/perlin_scale",      perlinScale);
        perlinAmplitude  = WorldLoadingController.GetParamFloat ("height_generation/perlin_amplitude",  perlinAmplitude);

        int seed = WorldLoadingController.GetDerivedSeed("height");
        System.Random rng = new System.Random(seed);
        _perlinOffsetX = (float)rng.NextDouble() * 10000f;
        _perlinOffsetZ = (float)rng.NextDouble() * 10000f;
    }

    public static float GetTerrainHeight(float x, float z) {
        return instance.mode switch {
            "superflat" => instance.superflatHeight,
            "perlin"    => instance.SamplePerlin(x, z),
            _           => instance.superflatHeight
        };
    }

    private float SamplePerlin(float x, float z) {
        float sampleX = (x + _perlinOffsetX) / perlinScale;
        float sampleZ = (z + _perlinOffsetZ) / perlinScale;
        return Mathf.PerlinNoise(sampleX, sampleZ) * perlinAmplitude;
    }

    public override void OnChunkLoadRequested(int cx, int cz, int lod) { }
    public override void OnChunkUnloadRequested(int cx, int cz, int lod) { }
    public override void Clear() => LoadParams();
}