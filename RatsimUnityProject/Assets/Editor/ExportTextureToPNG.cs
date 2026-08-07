using UnityEngine;
using UnityEditor;
using System.IO;

public class ExportTextureToPNG
{
    [MenuItem("Assets/Export to PNG")]
    public static void ExportSelectedTexture()
    {
        // Get the currently selected texture
        Texture2D selectedTexture = Selection.activeObject as Texture2D;

        if (selectedTexture == null)
        {
            Debug.LogWarning("Please select a Texture2D to export.");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(selectedTexture);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        // 1. Temporarily make the texture readable if it isn't already
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

        // 2. Encode to PNG
        // Note: EncodeToPNG natively supports TextureFormat.R8 and will output an 8-bit grayscale PNG.
        byte[] bytes = selectedTexture.EncodeToPNG();

        if (bytes != null && bytes.Length > 0)
        {
            // 3. Save the PNG next to the original asset
            string directory = Path.GetDirectoryName(assetPath);
            string filename = Path.GetFileNameWithoutExtension(assetPath) + "_exported.png";
            string fullPath = Path.Combine(directory, filename);

            File.WriteAllBytes(fullPath, bytes);
            Debug.Log($"Successfully exported texture to PNG: {fullPath}");
        }
        else
        {
            Debug.LogError("Failed to encode texture to PNG. Ensure the format is supported.");
        }

        // 4. Revert the texture back to non-readable if we changed it
        if (importer != null && !wasReadable)
        {
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        AssetDatabase.Refresh();
    }

    // This ensures the menu item is only clickable when a Texture2D is selected
    [MenuItem("Assets/Export to PNG", true)]
    public static bool ExportSelectedTextureValidation()
    {
        return Selection.activeObject is Texture2D;
    }
}
