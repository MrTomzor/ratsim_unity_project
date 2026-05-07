using UnityEngine;
using UnityEditor;
using System.IO;

public class TextureInverter
{
    [MenuItem("Assets/Create Smoothness Map (Invert)", false, 100)]
    static void InvertSelectedTextures()
    {
        int count = 0;

        foreach (Object obj in Selection.objects)
        {
            if (obj is Texture2D)
            {
                Texture2D tex = (Texture2D)obj;
                string path = AssetDatabase.GetAssetPath(obj);

                // 1. Get the importer to temporarily allow reading the pixels
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool wasReadable = false;
                
                if (importer != null)
                {
                    wasReadable = importer.isReadable;
                    if (!wasReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                }

                // 2. Read and Invert the pixels
                Color[] pixels = tex.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i].r = 1f - pixels[i].r;
                    pixels[i].g = 1f - pixels[i].g;
                    pixels[i].b = 1f - pixels[i].b;
                    // We ignore alpha here because JPG does not support transparency
                }

                // 3. Create a new standard texture for the JPG output
                // We use RGB24 because JPG doesn't need an alpha channel
                Texture2D outTex = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
                outTex.SetPixels(pixels);
                outTex.Apply();

                // 4. Save as JPG (Quality set to 100)
                string extension = Path.GetExtension(path);
                string newPath = path.Replace(extension, "_Smoothness.jpg");
                
                File.WriteAllBytes(newPath, outTex.EncodeToJPG(100));
                count++;

                // 5. Cleanup: Revert the original file's read/write state so we don't waste memory
                if (importer != null && !wasReadable)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
            }
        }

        // Tell Unity to refresh the project window to show the new files
        AssetDatabase.Refresh();
        Debug.Log($"Successfully inverted {count} textures into JPG Smoothness maps!");
    }
}