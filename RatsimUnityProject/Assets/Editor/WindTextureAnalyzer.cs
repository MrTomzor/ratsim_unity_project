using UnityEngine;
using UnityEditor;
using System.IO;

public static class WindTextureAnalyzer
{
    private const string DefaultWindTexturePath = "Assets/RealLifeEnvironment/GPUInstancer/Resources/Wind Texture.png";

    [MenuItem("Tools/Analyze Wind Texture")]
    public static void AnalyzeDefaultOrSelected()
    {
        Texture2D texture = Selection.activeObject as Texture2D;

        if (texture == null)
        {
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultWindTexturePath);
        }

        if (texture == null)
        {
            // Search by name if not found at default path
            string[] guids = AssetDatabase.FindAssets("Wind Texture t:Texture2D");
            if (guids.Length > 0)
            {
                string foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(foundPath);
            }
        }

        if (texture == null)
        {
            Debug.LogError("WindTextureAnalyzer: Could not find 'Wind Texture.png' at " + DefaultWindTexturePath + " and no Texture2D is selected.");
            return;
        }

        AnalyzeTexture(texture);
    }

    [MenuItem("Assets/Analyze Wind Texture (RG MinMax)", true)]
    private static bool ValidateAnalyzeSelected()
    {
        return Selection.activeObject is Texture2D;
    }

    [MenuItem("Assets/Analyze Wind Texture (RG MinMax)")]
    public static void AnalyzeSelected()
    {
        Texture2D texture = Selection.activeObject as Texture2D;
        if (texture != null)
        {
            AnalyzeTexture(texture);
        }
    }

    [MenuItem("Tools/Normalize Wind Texture (0-1, Avg 0.5)")]
    public static void NormalizeDefaultOrSelected()
    {
        Texture2D texture = Selection.activeObject as Texture2D;

        if (texture == null)
        {
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultWindTexturePath);
        }

        if (texture == null)
        {
            string[] guids = AssetDatabase.FindAssets("Wind Texture t:Texture2D");
            if (guids.Length > 0)
            {
                string foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(foundPath);
            }
        }

        if (texture == null)
        {
            Debug.LogError("WindTextureAnalyzer: Could not find 'Wind Texture.png' at " + DefaultWindTexturePath + " and no Texture2D is selected.");
            return;
        }

        NormalizeTexture(texture);
    }

    [MenuItem("Assets/Normalize Wind Texture (0-1, Avg 0.5)", true)]
    private static bool ValidateNormalizeSelected()
    {
        return Selection.activeObject is Texture2D;
    }

    [MenuItem("Assets/Normalize Wind Texture (0-1, Avg 0.5)")]
    public static void NormalizeSelected()
    {
        Texture2D texture = Selection.activeObject as Texture2D;
        if (texture != null)
        {
            NormalizeTexture(texture);
        }
    }

    public static void AnalyzeTexture(Texture2D texture)
    {
        string assetPath = AssetDatabase.GetAssetPath(texture);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        bool wasReadable = false;
        bool changedReadable = false;

        if (importer != null)
        {
            wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                changedReadable = true;
            }
        }

        try
        {
            Color[] pixels = texture.GetPixels();
            if (pixels == null || pixels.Length == 0)
            {
                Debug.LogError($"WindTextureAnalyzer: Failed to read pixels from {texture.name}.");
                return;
            }

            float minR = float.MaxValue;
            float maxR = float.MinValue;
            float minG = float.MaxValue;
            float maxG = float.MinValue;

            double sumR = 0;
            double sumG = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                float r = pixels[i].r;
                float g = pixels[i].g;

                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
                if (g < minG) minG = g;
                if (g > maxG) maxG = g;

                sumR += r;
                sumG += g;
            }

            float avgR = (float)(sumR / pixels.Length);
            float avgG = (float)(sumG / pixels.Length);

            // Shader wind logic: (windTex.rg * 2.0 - 1.0)
            float remappedMinR = minR * 2f - 1f;
            float remappedMaxR = maxR * 2f - 1f;
            float remappedMinG = minG * 2f - 1f;
            float remappedMaxG = maxG * 2f - 1f;

            string report = $"<b>[WindTextureAnalyzer] Analysis for '{texture.name}' ({texture.width}x{texture.height}, {pixels.Length} pixels)</b>\n"
                          + $"Asset Path: {assetPath}\n\n"
                          + $"<b>--- Raw Values [0.0, 1.0] ---</b>\n"
                          + $"  <b>R Channel:</b> Min: {minR:F5} (byte: {Mathf.RoundToInt(minR * 255f)}), Max: {maxR:F5} (byte: {Mathf.RoundToInt(maxR * 255f)}), Avg: {avgR:F5}\n"
                          + $"  <b>G Channel:</b> Min: {minG:F5} (byte: {Mathf.RoundToInt(minG * 255f)}), Max: {maxG:F5} (byte: {Mathf.RoundToInt(maxG * 255f)}), Avg: {avgG:F5}\n\n"
                          + $"<b>--- Shader Remapped Values (rg * 2.0 - 1.0) [-1.0, 1.0] ---</b>\n"
                          + $"  <b>Wind X (R):</b> Min: {remappedMinR:F5}, Max: {remappedMaxR:F5}\n"
                          + $"  <b>Wind Y (G):</b> Min: {remappedMinG:F5}, Max: {remappedMaxG:F5}";

            Debug.Log(report, texture);
        }
        finally
        {
            if (changedReadable && importer != null)
            {
                importer.isReadable = wasReadable;
                importer.SaveAndReimport();
            }
        }
    }

    public static void NormalizeTexture(Texture2D texture)
    {
        string assetPath = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogError("WindTextureAnalyzer: Cannot normalize texture because asset path is empty.");
            return;
        }

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        bool wasReadable = false;
        bool changedReadable = false;

        if (importer != null)
        {
            wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                changedReadable = true;
            }
        }

        try
        {
            Color[] pixels = texture.GetPixels();
            if (pixels == null || pixels.Length == 0)
            {
                Debug.LogError($"WindTextureAnalyzer: Failed to read pixels from {texture.name}.");
                return;
            }

            float minR = float.MaxValue;
            float maxR = float.MinValue;
            float minG = float.MaxValue;
            float maxG = float.MinValue;

            for (int i = 0; i < pixels.Length; i++)
            {
                float r = pixels[i].r;
                float g = pixels[i].g;

                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
                if (g < minG) minG = g;
                if (g > maxG) maxG = g;
            }

            float rangeR = maxR - minR;
            float rangeG = maxG - minG;

            // Step 1: Map channels to [0.0, 1.0] and build histograms
            float[] r01 = new float[pixels.Length];
            float[] g01 = new float[pixels.Length];
            int[] histR = new int[256];
            int[] histG = new int[256];

            for (int i = 0; i < pixels.Length; i++)
            {
                float nr = rangeR > 1e-6f ? Mathf.Clamp01((pixels[i].r - minR) / rangeR) : pixels[i].r;
                float ng = rangeG > 1e-6f ? Mathf.Clamp01((pixels[i].g - minG) / rangeG) : pixels[i].g;

                r01[i] = nr;
                g01[i] = ng;

                histR[Mathf.Clamp(Mathf.RoundToInt(nr * 255f), 0, 255)]++;
                histG[Mathf.Clamp(Mathf.RoundToInt(ng * 255f), 0, 255)]++;
            }

            // Step 2: Solve power curve (gamma) so that average is exactly 0.5 while strictly preserving [0, 1] range
            float gammaR = SolveGammaForTargetAverage(histR, pixels.Length, 0.5f);
            float gammaG = SolveGammaForTargetAverage(histG, pixels.Length, 0.5f);

            Color[] normalizedPixels = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                float finalR = Mathf.Pow(r01[i], gammaR);
                float finalG = Mathf.Pow(g01[i], gammaG);
                normalizedPixels[i] = new Color(finalR, finalG, c.b, c.a);
            }

            bool isLinear = importer != null ? !importer.sRGBTexture : false;
            Texture2D outputTex = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, isLinear);
            outputTex.SetPixels(normalizedPixels);
            outputTex.Apply();

            byte[] pngBytes = outputTex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(outputTex);

            string fullDiskPath = Path.GetFullPath(assetPath);
            File.WriteAllBytes(fullDiskPath, pngBytes);

            Debug.Log($"<b>[WindTextureAnalyzer] Successfully normalized '{texture.name}' (Range: [0.0, 1.0], Target Avg: 0.5)</b>\n"
                    + $"  R Channel: Gamma = {gammaR:F4}\n"
                    + $"  G Channel: Gamma = {gammaG:F4}\n"
                    + $"Saved to: {assetPath}");

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            // Run analysis on the newly re-imported texture
            Texture2D reloaded = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (reloaded != null)
            {
                AnalyzeTexture(reloaded);
            }
        }
        finally
        {
            if (changedReadable && importer != null)
            {
                importer.isReadable = wasReadable;
                importer.SaveAndReimport();
            }
        }
    }

    private static float SolveGammaForTargetAverage(int[] histogram, int totalPixels, float targetAvg = 0.5f)
    {
        float low = 0.001f;
        float high = 50f;

        for (int iter = 0; iter < 32; iter++)
        {
            float mid = (low + high) * 0.5f;
            double sum = 0;
            for (int k = 1; k < 256; k++)
            {
                if (histogram[k] > 0)
                {
                    sum += histogram[k] * System.Math.Pow(k / 255.0, mid);
                }
            }

            double currentAvg = sum / totalPixels;
            if (currentAvg > targetAvg)
            {
                low = mid; // Higher gamma pushes values down towards 0
            }
            else
            {
                high = mid; // Lower gamma pulls values up towards 1
            }
        }

        return (low + high) * 0.5f;
    }
}

