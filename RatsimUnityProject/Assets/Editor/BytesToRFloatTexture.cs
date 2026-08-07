using UnityEngine;
using UnityEditor;
using System.IO;
using System;

public class BytesToRFloatTexture : EditorWindow
{
    private bool flipY = true;

    [MenuItem("Tools/Convert .bytes to RFloat Texture")]
    public static void ShowWindow()
    {
        GetWindow<BytesToRFloatTexture>("Bytes to RFloat");
    }

    private void OnGUI()
    {
        GUILayout.Label("Convert Raw Float (.bytes) to Texture2D", EditorStyles.boldLabel);
        
        EditorGUILayout.HelpBox("This tool assumes the .bytes file is a flat array of 32-bit floating point numbers (generated in Python, e.g., via numpy.tobytes()) forming a perfect square grid.", MessageType.Info);
        
        // Expose a toggle because Python arrays usually have (0,0) at top-left, while Unity is bottom-left
        flipY = EditorGUILayout.Toggle("Flip Y Axis", flipY);
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Select File & Convert", GUILayout.Height(30)))
        {
            string path = EditorUtility.OpenFilePanel("Select .bytes file", "", "bytes");
            if (!string.IsNullOrEmpty(path))
            {
                ConvertBytes(path, flipY);
            }
        }
    }

    private static void ConvertBytes(string filePath, bool flipY)
    {
        byte[] byteData = File.ReadAllBytes(filePath);
        
        // 4 bytes per 32-bit float
        int resolution = (int)Mathf.Sqrt(byteData.Length / 4f);

        if (resolution * resolution * 4 != byteData.Length)
        {
            Debug.LogError($"Invalid file size. Expected a perfect square of 32-bit floats. Found {byteData.Length} bytes.");
            return;
        }

        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RFloat, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        // Extremely fast direct memory copy from bytes to floats
        float[] floats = new float[resolution * resolution];
        Buffer.BlockCopy(byteData, 0, floats, 0, byteData.Length);

        // Optionally flip the Y axis 
        if (flipY)
        {
            float[] flippedFloats = new float[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                Array.Copy(floats, y * resolution, flippedFloats, (resolution - 1 - y) * resolution, resolution);
            }
            floats = flippedFloats;
        }

        texture.SetPixelData(floats, 0);
        texture.Apply();

        string defaultName = Path.GetFileNameWithoutExtension(filePath) + "_RFloat";
        string savePath = EditorUtility.SaveFilePanelInProject("Save RFloat Texture", defaultName, "asset", "Save texture as .asset");
        
        if (!string.IsNullOrEmpty(savePath))
        {
            AssetDatabase.CreateAsset(texture, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Successfully converted {filePath} to {savePath}");
            
            // Highlight the new asset in the Project window
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(savePath));
        }
    }
}
