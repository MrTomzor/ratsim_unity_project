using UnityEngine;
using UnityEditor;
using System.IO;

public class RFloatToR8Converter : EditorWindow
{
    private Texture2D sourceTexture;

    [MenuItem("Tools/Convert RFloat to R8")]
    public static void ShowWindow()
    {
        GetWindow<RFloatToR8Converter>("RFloat to R8");
    }

    private void OnGUI()
    {
        GUILayout.Label("Convert RFloat Texture to R8", EditorStyles.boldLabel);
        
        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Source RFloat Texture", sourceTexture, typeof(Texture2D), false);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Assumes the RFloat values are between 0 and 255. They will be directly mapped to the 0-255 byte values of the R8 texture.", MessageType.Info);

        if (GUILayout.Button("Convert and Save as .asset (Raw R8)"))
        {
            ConvertAndSave(true);
        }
        
        if (GUILayout.Button("Convert and Save as .png"))
        {
            ConvertAndSave(false);
        }
    }

    private void ConvertAndSave(bool saveAsAsset)
    {
        if (sourceTexture == null)
        {
            EditorUtility.DisplayDialog("Error", "Please assign a source texture.", "OK");
            return;
        }

        string path = AssetDatabase.GetAssetPath(sourceTexture);
        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("Error", "Source texture must be an asset.", "OK");
            return;
        }
        
        if (!sourceTexture.isReadable)
        {
            EditorUtility.DisplayDialog("Error", "Texture must be readable. Please enable 'Read/Write' in its import settings.", "OK");
            return;
        }

        if (sourceTexture.format != TextureFormat.RFloat)
        {
            bool proceed = EditorUtility.DisplayDialog("Warning", $"Source texture format is {sourceTexture.format}, not RFloat. Proceed anyway?", "Yes", "No");
            if (!proceed) return;
        }

        int width = sourceTexture.width;
        int height = sourceTexture.height;
        string directory = Path.GetDirectoryName(path);
        string filename = Path.GetFileNameWithoutExtension(path);

        if (saveAsAsset)
        {
            Texture2D newTex = new Texture2D(width, height, TextureFormat.R8, false, true);

            if (sourceTexture.format == TextureFormat.RFloat)
            {
                var sourceData = sourceTexture.GetPixelData<float>(0);
                var targetData = newTex.GetPixelData<byte>(0);

                for (int i = 0; i < sourceData.Length; i++)
                {
                    targetData[i] = (byte)Mathf.Clamp(sourceData[i], 0f, 255f);
                }
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float val = sourceTexture.GetPixel(x, y).r;
                        newTex.SetPixel(x, y, new Color(val / 255f, 0, 0, 1));
                    }
                }
            }

            newTex.Apply();

            string newPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(directory, filename + "_R8.asset").Replace("\\", "/"));
            AssetDatabase.CreateAsset(newTex, newPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Success", $"Texture converted and saved as raw R8 asset to:\n{newPath}", "OK");
        }
        else
        {
            // For PNG, we need to create an RGB or RGBA texture to encode properly
            Texture2D newTex = new Texture2D(width, height, TextureFormat.RGB24, false, true);

            if (sourceTexture.format == TextureFormat.RFloat)
            {
                var sourceData = sourceTexture.GetPixelData<float>(0);
                var targetData = newTex.GetPixelData<Color32>(0);

                for (int i = 0; i < sourceData.Length; i++)
                {
                    byte b = (byte)Mathf.Clamp(sourceData[i], 0f, 255f);
                    targetData[i] = new Color32(b, b, b, 255);
                }
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float val = sourceTexture.GetPixel(x, y).r;
                        byte b = (byte)Mathf.Clamp(val, 0f, 255f);
                        newTex.SetPixel(x, y, new Color32(b, b, b, 255));
                    }
                }
            }

            newTex.Apply();
            byte[] pngBytes = newTex.EncodeToPNG();
            
            // Clean up the temporary texture
            DestroyImmediate(newTex);

            string newPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(directory, filename + "_R8.png").Replace("\\", "/"));
            File.WriteAllBytes(newPath, pngBytes);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Success", $"Texture saved to:\n{newPath}\n\nMake sure to set its Texture Format to R8 in the import settings!", "OK");
        }
    }
}
