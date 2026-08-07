using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class TextureRGBPadding : EditorWindow
{
    private Texture2D sourceTexture;
    private float alphaCutoff = 0.1f;
    private bool saveAsNew = true;

    [MenuItem("Tools/Texture/RGB Padding")]
    public static void ShowWindow()
    {
        GetWindow<TextureRGBPadding>("RGB Padding");
    }

    private void OnGUI()
    {
        GUILayout.Label("Texture RGB Padding", EditorStyles.boldLabel);

        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Source Texture", sourceTexture, typeof(Texture2D), false);
        alphaCutoff = EditorGUILayout.Slider("Alpha Cutoff", alphaCutoff, 0f, 1f);
        saveAsNew = EditorGUILayout.Toggle("Save As New File", saveAsNew);

        GUILayout.Space(10);

        if (GUILayout.Button("Pad RGB"))
        {
            if (sourceTexture != null)
            {
                PadTexture();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please select a source texture.", "OK");
            }
        }
    }

    private void PadTexture()
    {
        string path = AssetDatabase.GetAssetPath(sourceTexture);
        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("Error", "Selected texture is not an asset.", "OK");
            return;
        }

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        bool wasReadable = false;
        TextureImporterCompression originalCompression = TextureImporterCompression.Uncompressed;

        if (importer != null)
        {
            wasReadable = importer.isReadable;
            originalCompression = importer.textureCompression;

            bool needsReimport = false;

            if (!importer.isReadable)
            {
                importer.isReadable = true;
                needsReimport = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                needsReimport = true;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
            }
        }

        int width = sourceTexture.width;
        int height = sourceTexture.height;
        Color[] pixels = sourceTexture.GetPixels();
        Color[] resultPixels = new Color[pixels.Length];

        float[,] dist = new float[width, height];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                resultPixels[i] = pixels[i];
                if (pixels[i].a >= alphaCutoff)
                {
                    dist[x, y] = 0;
                    queue.Enqueue(new Vector2Int(x, y));
                }
                else
                {
                    dist[x, y] = float.MaxValue;
                }
            }
        }

        if (queue.Count == 0)
        {
            EditorUtility.DisplayDialog("Warning", "No pixels found above alpha cutoff. Result will be unmodified.", "OK");
        }

        Vector2Int[] neighbors = {
            new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
            new Vector2Int(-1, 0),                         new Vector2Int(1, 0),
            new Vector2Int(-1, 1),  new Vector2Int(0, 1),  new Vector2Int(1, 1)
        };

        float[] neighborDist = {
            1.414f, 1f, 1.414f,
            1f,         1f,
            1.414f, 1f, 1.414f
        };

        EditorUtility.DisplayProgressBar("Padding Texture", "Expanding RGB values...", 0.5f);

        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            int pIndex = p.y * width + p.x;

            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2Int n = p + neighbors[i];
                if (n.x >= 0 && n.x < width && n.y >= 0 && n.y < height)
                {
                    float newDist = dist[p.x, p.y] + neighborDist[i];
                    if (newDist < dist[n.x, n.y])
                    {
                        dist[n.x, n.y] = newDist;
                        int nIndex = n.y * width + n.x;

                        // Keep original alpha, but inherit RGB from nearest
                        resultPixels[nIndex].r = resultPixels[pIndex].r;
                        resultPixels[nIndex].g = resultPixels[pIndex].g;
                        resultPixels[nIndex].b = resultPixels[pIndex].b;

                        queue.Enqueue(n);
                    }
                }
            }
        }

        EditorUtility.ClearProgressBar();

        Texture2D paddedTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        paddedTex.SetPixels(resultPixels);
        paddedTex.Apply();

        byte[] bytes = paddedTex.EncodeToPNG();
        DestroyImmediate(paddedTex);

        string savePath = path;
        if (saveAsNew)
        {
            string extension = Path.GetExtension(path);
            savePath = path.Substring(0, path.Length - extension.Length) + "_Padded.png";
        }
        else
        {
            string extension = Path.GetExtension(path);
            if (extension.ToLower() != ".png")
            {
                savePath = path.Substring(0, path.Length - extension.Length) + ".png";
                Debug.LogWarning("Overwriting non-PNG file with PNG. Saved as " + savePath);
            }
        }

        File.WriteAllBytes(savePath, bytes);
        AssetDatabase.ImportAsset(savePath);

        // Restore original texture settings if we changed them
        if (importer != null)
        {
            bool needsReimport = false;

            if (importer.isReadable != wasReadable)
            {
                importer.isReadable = wasReadable;
                needsReimport = true;
            }

            if (importer.textureCompression != originalCompression)
            {
                importer.textureCompression = originalCompression;
                needsReimport = true;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
            }
        }

        EditorUtility.DisplayDialog("Success", $"Texture padded successfully and saved to:\n{savePath}", "OK");
    }
}
