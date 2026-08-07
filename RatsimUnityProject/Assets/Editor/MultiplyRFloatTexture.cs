using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public class MultiplyRFloatTexture
{
    [MenuItem("Assets/Multiply Texture by 255")]
    public static void MultiplyBy255()
    {
        Object[] selectedObjects = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            if (Selection.activeObject is Texture2D activeTex)
            {
                selectedObjects = new Object[] { activeTex };
            }
        }

        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogError("Please select at least one Texture2D asset.");
            return;
        }

        int count = 0;
        foreach (Object obj in selectedObjects)
        {
            if (obj is not Texture2D tex) continue;

            string path = AssetDatabase.GetAssetPath(tex);
            TextureImporter importer = null;
            bool wasReadable = tex.isReadable;

            if (!string.IsNullOrEmpty(path))
            {
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && !wasReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
            }

            if (tex == null || !tex.isReadable)
            {
                Debug.LogError($"Texture '{(obj != null ? obj.name : "unknown")}' is not readable! If this is an imported image, please enable 'Read/Write' in its import settings.");
                continue;
            }

            bool hasMipmaps = tex.mipmapCount > 1;
            Texture2D newTex = new Texture2D(tex.width, tex.height, tex.format, hasMipmaps);
            newTex.name = tex.name + "_multiplied";

            MultiplyPixels(tex, newTex);

            newTex.Apply(hasMipmaps);

            string savePath = GetMultipliedSavePath(path, tex.name);
            SaveTexture(newTex, savePath);

            if (importer != null && !wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            count++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"Successfully multiplied {count} texture(s) by 255!");
    }

    private static void MultiplyPixels(Texture2D srcTex, Texture2D dstTex)
    {
        TextureFormat format = srcTex.format;

        if (format == TextureFormat.RFloat || format == TextureFormat.RGBAFloat)
        {
            NativeArray<float> srcData = srcTex.GetPixelData<float>(0);
            NativeArray<float> dstData = dstTex.GetPixelData<float>(0);
            Parallel.For(0, srcData.Length, i =>
            {
                dstData[i] = srcData[i] * 255f;
            });
        }
        else if (format == TextureFormat.RHalf || format == TextureFormat.RGBAHalf)
        {
            NativeArray<ushort> srcData = srcTex.GetPixelData<ushort>(0);
            NativeArray<ushort> dstData = dstTex.GetPixelData<ushort>(0);
            Parallel.For(0, srcData.Length, i =>
            {
                float val = Mathf.HalfToFloat(srcData[i]) * 255f;
                dstData[i] = Mathf.FloatToHalf(val);
            });
        }
        else
        {
            Color[] srcPixels = srcTex.GetPixels();
            Color[] dstPixels = new Color[srcPixels.Length];

            Parallel.For(0, srcPixels.Length, i =>
            {
                Color c = srcPixels[i];
                c.r *= 255f;
                c.g *= 255f;
                c.b *= 255f;
                c.a *= 255f;
                dstPixels[i] = c;
            });

            dstTex.SetPixels(dstPixels);
        }
    }

    private static string GetMultipliedSavePath(string originalPath, string textureName)
    {
        if (string.IsNullOrEmpty(originalPath))
        {
            return $"Assets/{textureName}_multiplied.asset";
        }

        string directory = Path.GetDirectoryName(originalPath);
        string fileName = Path.GetFileNameWithoutExtension(originalPath);
        string extension = Path.GetExtension(originalPath).ToLowerInvariant();

        // Unity Texture2D can only encode directly to EXR, PNG, JPG.
        // For other source file types (.tif, .tga, .bmp, etc.) or .asset files, save as a native Unity .asset file.
        if (extension != ".exr" && extension != ".png" && extension != ".jpg" && extension != ".jpeg")
        {
            extension = ".asset";
        }

        string newFileName = $"{fileName}_multiplied{extension}";
        return Path.Combine(directory, newFileName).Replace('\\', '/');
    }

    private static void SaveTexture(Texture2D newTex, string savePath)
    {
        string extension = Path.GetExtension(savePath).ToLowerInvariant();

        if (extension == ".exr")
        {
            byte[] bytes = newTex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
            File.WriteAllBytes(savePath, bytes);
            AssetDatabase.Refresh();
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(savePath);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }
        else if (extension == ".png")
        {
            byte[] bytes = newTex.EncodeToPNG();
            File.WriteAllBytes(savePath, bytes);
            AssetDatabase.Refresh();
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(savePath);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }
        else if (extension == ".jpg" || extension == ".jpeg")
        {
            byte[] bytes = newTex.EncodeToJPG(100);
            File.WriteAllBytes(savePath, bytes);
            AssetDatabase.Refresh();
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(savePath);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }
        else
        {
            if (!savePath.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
            {
                savePath = Path.ChangeExtension(savePath, ".asset");
            }
            AssetDatabase.CreateAsset(newTex, savePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(newTex);
        }
    }

    [MenuItem("Assets/Multiply Texture by 255", true)]
    public static bool ValidateMultiplyBy255()
    {
        return Selection.activeObject is Texture2D || Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length > 0;
    }
}
