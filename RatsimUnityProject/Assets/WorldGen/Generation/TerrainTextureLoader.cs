using UnityEngine;

public class TerrainTextureLoader : WorldLoadingModule {

    public static TerrainTextureLoader instance;

    [Header("Texture Settings")]
    public int textureResolution = 64;
    public bool verbose = false;

    [Header("Height Thresholds (world units)")]
    public float waterLevel  = 0f;
    public float beachLevel  = 2f;
    public float grassLevel  = 30f;
    public float rockLevel   = 60f;
    public float snowLevel   = 80f;

    [Header("Terrain Colors")]
    public Color deepWaterColor  = new Color(0.10f, 0.20f, 0.50f);
    public Color shallowWater    = new Color(0.20f, 0.40f, 0.65f);
    public Color sandColor       = new Color(0.85f, 0.80f, 0.55f);
    public Color grassColor      = new Color(0.25f, 0.55f, 0.20f);
    public Color dryGrassColor   = new Color(0.55f, 0.60f, 0.25f);
    public Color rockColor       = new Color(0.45f, 0.40f, 0.35f);
    public Color snowColor       = new Color(0.95f, 0.95f, 1.00f);

    private int   _chunkWidthInt;

    private void Awake() {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        _chunkWidthInt = (int)WorldLoadingController.GetChunkWidth();
    }

    // ─────────────────────────────────────────────
    //  WorldLoadingModule
    // ─────────────────────────────────────────────

    public override void OnChunkLoadRequested(int cx, int cz, int lod) {
        Texture2D tex = GenerateTexture(cx, cz);
        TerrainMeshLoader.instance.SetChunkTexture(cx, cz, tex);
    }

    public override void OnChunkUnloadRequested(int cx, int cz, int lod) { }

    public override void Clear() {
        _chunkWidthInt = (int)WorldLoadingController.GetChunkWidth();
    }

    // ─────────────────────────────────────────────
    //  Texture generation
    // ─────────────────────────────────────────────

    private Texture2D GenerateTexture(int cx, int cz) {
        float ox = cx * _chunkWidthInt;
        float oz = cz * _chunkWidthInt;

        Texture2D tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGB24, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;

        Color[] pixels = new Color[textureResolution * textureResolution];

        for (int z = 0; z < textureResolution; z++)
        for (int x = 0; x < textureResolution; x++) {
            float worldX = ox + ((float)x / (textureResolution - 1)) * _chunkWidthInt;
            float worldZ = oz + ((float)z / (textureResolution - 1)) * _chunkWidthInt;
            float height = WorldHeightLoader.GetTerrainHeight(worldX, worldZ);
            pixels[z * textureResolution + x] = HeightToColor(height);
            if (verbose)
            {
                Debug.Log($"Generated pixel ({x},{z}) for chunk ({cx},{cz}): world pos ({worldX:F1}, {worldZ:F1}), height {height:F1}, color {pixels[z * textureResolution + x]}");
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private Color HeightToColor(float h) {
        if (h < waterLevel)  return Color.Lerp(deepWaterColor, shallowWater,  Mathf.InverseLerp(waterLevel  - 20f, waterLevel,  h));
        if (h < beachLevel)  return Color.Lerp(shallowWater,  sandColor,      Mathf.InverseLerp(waterLevel,        beachLevel,  h));
        if (h < grassLevel)  return Color.Lerp(sandColor,     grassColor,      Mathf.InverseLerp(beachLevel,        grassLevel,  h));
        if (h < rockLevel)   return Color.Lerp(grassColor,    dryGrassColor,  Mathf.InverseLerp(grassLevel,        rockLevel,   h));
        if (h < snowLevel)   return Color.Lerp(dryGrassColor, rockColor,      Mathf.InverseLerp(rockLevel,         snowLevel,   h));
        return Color.Lerp(rockColor, snowColor, Mathf.InverseLerp(snowLevel, snowLevel + 20f, h));
    }
}