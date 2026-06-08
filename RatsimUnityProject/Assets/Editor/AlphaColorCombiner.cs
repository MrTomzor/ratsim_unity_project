using UnityEngine;
using UnityEditor;
using System.IO;

public class TextureCombinerMenuItem
{
    [MenuItem("Assets/Tools/Combine Color and Opacity Textures", false, 20)]
    private static void CombineSelectedTextures()
    {
        // 1. Get the currently selected objects in the Project view
        Object[] selectedObjects = Selection.objects;

        if (selectedObjects.Length != 2)
        {
            EditorUtility.DisplayDialog("Error", "Please select exactly TWO textures (one Color, one Opacity).", "OK");
            return;
        }

        Texture2D colorTex = null;
        Texture2D alphaTex = null;

        // 2. Identify which texture is which by prompting the user or checking names
        // To be safe and simple, we'll ask the user to confirm which one is the Color Map
        Texture2D tex1 = selectedObjects[0] as Texture2D;
        Texture2D tex2 = selectedObjects[1] as Texture2D;

        if (tex1 == null || tex2 == null)
        {
            EditorUtility.DisplayDialog("Error", "Both selected objects must be 2D textures.", "OK");
            return;
        }

        int choice = EditorUtility.DisplayDialogComplex(
            "Select Roles",
            $"Which texture should be used for the COLOR (RGB)?\n\nOption A: {tex1.name}\nOption B: {tex2.name}",
            tex1.name, // Option 0
            tex2.name, // Option 1
            "Cancel"   // Option 2
        );

        if (choice == 2) return; // Cancelled

        if (choice == 0)
        {
            colorTex = tex1;
            alphaTex = tex2;
        }
        else
        {
            colorTex = tex2;
            alphaTex = tex1;
        }

        // 3. Temporarily enable Read/Write on both textures if not already enabled
        string colorPath = AssetDatabase.GetAssetPath(colorTex);
        string alphaPath = AssetDatabase.GetAssetPath(alphaTex);

        EnsureTextureIsReadable(colorPath);
        EnsureTextureIsReadable(alphaPath);

        // 4. Verify dimensions match
        if (colorTex.width != alphaTex.width || colorTex.height != alphaTex.height)
        {
            EditorUtility.DisplayDialog("Error", "Texture dimensions do not match! They must be the same resolution.", "OK");
            return;
        }

        // 5. Read and Combine pixels
        Color[] colorPixels = colorTex.GetPixels();
        Color[] alphaPixels = alphaTex.GetPixels();
        Color[] packedPixels = new Color[colorPixels.Length];

        for (int i = 0; i < colorPixels.Length; i++)
        {
            packedPixels[i] = new Color(
                colorPixels[i].r,
                colorPixels[i].g,
                colorPixels[i].b,
                alphaPixels[i].r // Uses Red channel of opacity map as Alpha
            );
        }

        // 6. Create and Encode the new texture
        Texture2D resultTex = new Texture2D(colorTex.width, colorTex.height, TextureFormat.RGBA32, true);
        resultTex.SetPixels(packedPixels);
        resultTex.Apply();

        byte[] pngData = resultTex.EncodeToPNG();
        Object.DestroyImmediate(resultTex); // Clean up RAM

        // 7. Save to the same folder
        string directory = Path.GetDirectoryName(colorPath);
        string newFileName = colorTex.name + "_Combined.png";
        string finalPath = Path.Combine(directory, newFileName);

        File.WriteAllBytes(finalPath, pngData);

        // 8. Refresh AssetDatabase so it appears instantly in Unity
        AssetDatabase.Refresh();

        // 9. Configure the newly created texture to use standard alpha settings
        TextureImporter newImporter = AssetImporter.GetAtPath(finalPath) as TextureImporter;
        if (newImporter != null)
        {
            newImporter.alphaSource = TextureImporterAlphaSource.FromInput;
            newImporter.alphaIsTransparency = true;
            newImporter.SaveAndReimport();
        }

        Debug.Log($"Successfully created combined texture at: {finalPath}");
    }

    // Helper method to automatically toggle "Read/Write" so you don't get errors
    private static void EnsureTextureIsReadable(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
    }
}