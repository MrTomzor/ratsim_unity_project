using UnityEngine;

[ExecuteAlways] // Runs in the editor so you can see changes without pressing Play
public class MaterialBinder : MonoBehaviour
{
    [Header("Target Mesh Material")]
    [Tooltip("The material using the Custom/Terrain4WayMaterialBlend shader.")]
    public Material blendedTerrainMaterial;

    [Header("Input Layers (Standard Materials)")]
    public Material materialRedChannel;   // e.g., Grass
    public Material materialGreenChannel; // e.g., Dirt
    public Material materialBlueChannel;  // e.g., Rock
    public Material materialAlphaChannel; // e.g., Sand

    void OnValidate()
    {
        // This runs automatically whenever you change a material slot in the Inspector
        UpdateDelayedProperties();
    }

    public void UpdateDelayedProperties()
    {
        if (blendedTerrainMaterial == null) return;

        // Push settings from individual materials into the master blending shader
        ApplyMaterialToLayer(materialRedChannel, "R");
        ApplyMaterialToLayer(materialGreenChannel, "G");
        ApplyMaterialToLayer(materialBlueChannel, "B");
        ApplyMaterialToLayer(materialAlphaChannel, "A");
    }

    private void ApplyMaterialToLayer(Material sourceMat, string layerSuffix)
    {
        // If a slot is empty, we skip it to prevent errors
        if (sourceMat == null) return;

        // Built-In Render Pipeline Standard Shader property names:
        // Albedo = "_MainTex", Normal = "_BumpMap", Smoothness = "_Glossiness", Metallic = "_Metallic"

        // 1. Extract and pass Albedo Texture
        if (sourceMat.HasProperty("_MainTex"))
            blendedTerrainMaterial.SetTexture($"_Layer{layerSuffix}", sourceMat.GetTexture("_MainTex"));

        // 2. Extract and pass Normal Map
        if (sourceMat.HasProperty("_BumpMap"))
            blendedTerrainMaterial.SetTexture($"_Normal{layerSuffix}", sourceMat.GetTexture("_BumpMap"));

        // 3. Extract and pass Smoothness/Gloss
        float smoothness = 0.5f;
        if (sourceMat.HasProperty("_GlossMapScale") && 
            ((sourceMat.HasProperty("_MetallicGlossMap") && sourceMat.GetTexture("_MetallicGlossMap") != null) || 
             (sourceMat.HasProperty("_SpecGlossMap") && sourceMat.GetTexture("_SpecGlossMap") != null)))
        {
            smoothness = sourceMat.GetFloat("_GlossMapScale");
        }
        else if (sourceMat.HasProperty("_Glossiness"))
        {
            smoothness = sourceMat.GetFloat("_Glossiness");
        }
        else if (sourceMat.HasProperty("_Smoothness"))
        {
            smoothness = sourceMat.GetFloat("_Smoothness");
        }
        blendedTerrainMaterial.SetFloat($"_Gloss{layerSuffix}", smoothness);

        // 4. Extract and pass Metallic
        if (sourceMat.HasProperty("_Metallic"))
            blendedTerrainMaterial.SetFloat($"_Metal{layerSuffix}", sourceMat.GetFloat("_Metallic"));

        // 5. Extract and pass Ambient Occlusion
        if (sourceMat.HasProperty("_OcclusionMap"))
            blendedTerrainMaterial.SetTexture($"_Occlusion{layerSuffix}", sourceMat.GetTexture("_OcclusionMap"));

        if (sourceMat.HasProperty("_OcclusionStrength"))
            blendedTerrainMaterial.SetFloat($"_OcclusionStrength{layerSuffix}", sourceMat.GetFloat("_OcclusionStrength"));
    }
}