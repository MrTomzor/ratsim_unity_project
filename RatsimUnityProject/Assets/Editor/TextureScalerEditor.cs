using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class TextureScalerEditor : EditorWindow
{
    private int scaleFactor = 2;
    private int kNeighbors = 3;

    [MenuItem("Assets/Upscale Texture (k-NN)", true)]
    private static bool ValidateUpscaleTexture()
    {
        return Selection.activeObject is Texture2D;
    }

    [MenuItem("Assets/Upscale Texture (k-NN)")]
    public static void ShowWindow()
    {
        var window = GetWindow<TextureScalerEditor>("k-NN Scaler");
        window.minSize = new Vector2(300, 150);
        window.Show();
    }

    private void OnGUI()
    {
        GUILayout.Label("k-NN Texture Upscaler", EditorStyles.boldLabel);
        
        EditorGUILayout.Space();
        
        scaleFactor = EditorGUILayout.IntSlider("Scale Factor", scaleFactor, 2, 16);
        kNeighbors = EditorGUILayout.IntSlider("K (Neighbors)", kNeighbors, 1, 25);

        EditorGUILayout.Space();

        GUI.enabled = Selection.activeObject is Texture2D;

        if (GUILayout.Button("Upscale Selected Textures", GUILayout.Height(30)))
        {
            UpscaleSelectedTextures(scaleFactor, kNeighbors);
        }

        GUI.enabled = true;

        if (!(Selection.activeObject is Texture2D))
        {
            EditorGUILayout.HelpBox("Select a Texture2D in the Project window to upscale.", MessageType.Info);
        }
    }

    private static void UpscaleSelectedTextures(int scale, int k)
    {
        foreach (var obj in Selection.objects)
        {
            Texture2D source = obj as Texture2D;
            if (source == null) continue;

            string assetPath = AssetDatabase.GetAssetPath(source);
            
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            bool wasReadable = false;
            if (importer != null)
            {
                wasReadable = importer.isReadable;
                if (!wasReadable)
                {
                    importer.isReadable = true;
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                }
            }

            int newWidth = source.width * scale;
            int newHeight = source.height * scale;

            bool isLinear = false;
            if (importer != null)
            {
                isLinear = !importer.sRGBTexture;
            }
            else
            {
                // If it's an .asset file, it has no importer. We check the graphics format name.
                isLinear = !source.graphicsFormat.ToString().Contains("SRGB");
            }
            
            Texture2D result = new Texture2D(newWidth, newHeight, source.format, false, isLinear);
            
            Color[] sourcePixels = source.GetPixels();
            Color[] newPixels = new Color[newWidth * newHeight];
            int sourceWidth = source.width;
            int sourceHeight = source.height;

            float invScale = 1f / scale;
            int searchRadius = Mathf.CeilToInt(Mathf.Sqrt(k)) + 1;

            Parallel.For(0, newHeight, y =>
            {
                // Pre-allocate small arrays per thread to ensure zero GC allocation in the inner loop
                (float distSqr, Color color)[] kNearest = new (float, Color)[k];
                (Color color, int count)[] colorCounts = new (Color, int)[k];

                for (int x = 0; x < newWidth; x++)
                {
                    float cx = (x + 0.5f) * invScale;
                    float cy = (y + 0.5f) * invScale;

                    int centerPx = Mathf.FloorToInt(cx);
                    int centerPy = Mathf.FloorToInt(cy);

                    int foundCount = 0;

                    for (int dy = -searchRadius; dy <= searchRadius; dy++)
                    {
                        for (int dx = -searchRadius; dx <= searchRadius; dx++)
                        {
                            int px = centerPx + dx;
                            int py = centerPy + dy;

                            if (px >= 0 && px < sourceWidth && py >= 0 && py < sourceHeight)
                            {
                                float pixelCx = px + 0.5f;
                                float pixelCy = py + 0.5f;

                                float distSqr = (pixelCx - cx) * (pixelCx - cx) + (pixelCy - cy) * (pixelCy - cy);
                                Color c = sourcePixels[py * sourceWidth + px];

                                // Insertion sort to maintain K nearest neighbors
                                if (foundCount < k)
                                {
                                    kNearest[foundCount] = (distSqr, c);
                                    foundCount++;
                                    for (int i = foundCount - 1; i > 0 && kNearest[i].distSqr < kNearest[i - 1].distSqr; i--)
                                    {
                                        var temp = kNearest[i];
                                        kNearest[i] = kNearest[i - 1];
                                        kNearest[i - 1] = temp;
                                    }
                                }
                                else if (distSqr < kNearest[k - 1].distSqr)
                                {
                                    kNearest[k - 1] = (distSqr, c);
                                    for (int i = k - 1; i > 0 && kNearest[i].distSqr < kNearest[i - 1].distSqr; i--)
                                    {
                                        var temp = kNearest[i];
                                        kNearest[i] = kNearest[i - 1];
                                        kNearest[i - 1] = temp;
                                    }
                                }
                            }
                        }
                    }

                    // Find mode color (most frequent)
                    int uniqueColors = 0;
                    for (int i = 0; i < foundCount; i++)
                    {
                        Color c = kNearest[i].color;
                        bool found = false;
                        for (int j = 0; j < uniqueColors; j++)
                        {
                            if (colorCounts[j].color == c)
                            {
                                colorCounts[j].count++;
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            colorCounts[uniqueColors] = (c, 1);
                            uniqueColors++;
                        }
                    }

                    int maxCount = -1;
                    Color bestColor = Color.black;
                    
                    // Iterate through kNearest (which is sorted by distance) to break ties 
                    // by picking the closest color that reached maxCount
                    for (int i = 0; i < foundCount; i++)
                    {
                        Color c = kNearest[i].color;
                        int count = 0;
                        for (int j = 0; j < uniqueColors; j++)
                        {
                            if (colorCounts[j].color == c)
                            {
                                count = colorCounts[j].count;
                                break;
                            }
                        }
                        if (count > maxCount)
                        {
                            maxCount = count;
                            bestColor = c;
                        }
                    }

                    newPixels[y * newWidth + x] = bestColor;
                }
            });

            result.SetPixels(newPixels);
            result.Apply();

            string directory = Path.GetDirectoryName(assetPath);
            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string extension = Path.GetExtension(assetPath).ToLower();
            
            string newAssetPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + fileName + "_" + scale + "x_knn" + k + (string.IsNullOrEmpty(extension) ? ".asset" : extension));
            bool savedAsAsset = false;
            
            if (extension == ".asset")
            {
                AssetDatabase.CreateAsset(result, newAssetPath);
                savedAsAsset = true;
            }
            else if (extension == ".exr")
            {
                File.WriteAllBytes(newAssetPath, result.EncodeToEXR());
            }
            else if (extension == ".jpg" || extension == ".jpeg")
            {
                File.WriteAllBytes(newAssetPath, result.EncodeToJPG());
            }
            else if (extension == ".tga")
            {
                File.WriteAllBytes(newAssetPath, result.EncodeToTGA());
            }
            else 
            {
                byte[] bytes = result.EncodeToPNG();
                if (bytes != null && bytes.Length > 0)
                {
                    File.WriteAllBytes(newAssetPath, bytes);
                }
                else
                {
                    Debug.LogWarning("EncodeToPNG failed for format " + source.format + ". Saving as .asset instead.");
                    newAssetPath = Path.ChangeExtension(newAssetPath, ".asset");
                    AssetDatabase.CreateAsset(result, newAssetPath);
                    savedAsAsset = true;
                }
            }
            
            if (importer != null && !wasReadable)
            {
                importer.isReadable = false;
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
            
            if (!savedAsAsset)
            {
                // Force an immediate import so we can configure the new TextureImporter
                AssetDatabase.ImportAsset(newAssetPath);
                TextureImporter newImporter = AssetImporter.GetAtPath(newAssetPath) as TextureImporter;
                if (importer != null && newImporter != null)
                {
                    newImporter.sRGBTexture = importer.sRGBTexture;
                    newImporter.textureType = importer.textureType;
                    newImporter.alphaSource = importer.alphaSource;
                    newImporter.alphaIsTransparency = importer.alphaIsTransparency;
                    newImporter.filterMode = importer.filterMode;
                    newImporter.wrapMode = importer.wrapMode;
                    newImporter.textureCompression = importer.textureCompression;
                    
                    var platformSettings = importer.GetDefaultPlatformTextureSettings();
                    newImporter.SetPlatformTextureSettings(platformSettings);
                    
                    newImporter.SaveAndReimport();
                }

                Object.DestroyImmediate(result);
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"Finished {scale}x k-NN (K={k}) texture scaling!");
    }

    // GetModeColor is no longer used since we inline it for performance
}
