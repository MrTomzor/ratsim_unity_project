using UnityEngine;
using UnityEditor;

public class RFloatViewer : EditorWindow
{
    [MenuItem("Tools/RFloat Texture Viewer")]
    public static void ShowWindow()
    {
        GetWindow<RFloatViewer>("RFloat Viewer");
    }

    private Texture2D selectedTexture;
    private Material previewMaterial;
    
    private float minVal = 0f;
    private float maxVal = 1f;
    
    private float trueMin = 0f;
    private float trueMax = 1f;
    
    public enum ColorMap { Grayscale, Heatmap }
    private ColorMap colorMap = ColorMap.Heatmap;
    
    private int repeatTimes = 1;

    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
        OnSelectionChanged();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        if (previewMaterial != null)
        {
            DestroyImmediate(previewMaterial);
        }
    }

    private void OnSelectionChanged()
    {
        if (Selection.activeObject is Texture2D tex)
        {
            if (selectedTexture != tex)
            {
                selectedTexture = tex;
                CalculateMinMax();
            }
        }
        else
        {
            selectedTexture = null;
        }
        Repaint();
    }
    
    private void CalculateMinMax()
    {
        if (selectedTexture == null) return;
        
        // Try to get pixel data to calculate true min/max.
        try
        {
            if (selectedTexture.format == TextureFormat.RFloat)
            {
                var data = selectedTexture.GetPixelData<float>(0);
                float min = float.MaxValue;
                float max = float.MinValue;
                for (int i = 0; i < data.Length; i++) 
                {
                    if (data[i] < min) min = data[i];
                    if (data[i] > max) max = data[i];
                }
                
                if (min >= max) 
                {
                    max = min + 1f;
                }
                
                trueMin = min;
                trueMax = max;
                minVal = trueMin;
                maxVal = trueMax;
            }
        }
        catch (System.Exception)
        {
            // Texture might not be readable (e.g. if it's compressed/not read-write enabled).
            // It will still render if we pass it to the material, we just can't auto-calc bounds.
            Debug.LogWarning("Could not read pixels to automatically calculate min/max values for sliders.");
        }
    }

    private void OnGUI()
    {
        if (selectedTexture == null)
        {
            GUILayout.Label("Select a Texture2D in the Project window to view.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        if (previewMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/RFloatPreview");
            if (shader != null)
            {
                previewMaterial = new Material(shader);
            }
            else
            {
                EditorGUILayout.HelpBox("Could not find shader 'Hidden/RFloatPreview'. Make sure RFloatPreview.shader is in your project.", MessageType.Error);
                return;
            }
        }

        GUILayout.Label($"Viewing: {selectedTexture.name} ({selectedTexture.width}x{selectedTexture.height})", EditorStyles.boldLabel);
        
        EditorGUILayout.Space();
        
        colorMap = (ColorMap)EditorGUILayout.EnumPopup("Color Map", colorMap);
        repeatTimes = EditorGUILayout.IntSlider("Colormap Repeats", repeatTimes, 1, 20);
        
        EditorGUILayout.Space();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Display Range", GUILayout.Width(90));
        
        // Allow manual overrides of min/max beyond the trueMin/trueMax bounds
        minVal = EditorGUILayout.FloatField(minVal, GUILayout.Width(60));
        EditorGUILayout.MinMaxSlider(ref minVal, ref maxVal, trueMin, trueMax);
        maxVal = EditorGUILayout.FloatField(maxVal, GUILayout.Width(60));
        GUILayout.EndHorizontal();
        
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset to True Min/Max"))
        {
            minVal = trueMin;
            maxVal = trueMax;
        }
        if (GUILayout.Button("Recalculate Min/Max"))
        {
            CalculateMinMax();
        }
        GUILayout.EndHorizontal();

        // Update Material for preview
        previewMaterial.SetFloat("_Min", minVal);
        previewMaterial.SetFloat("_Max", maxVal);
        previewMaterial.SetFloat("_ColorMap", colorMap == ColorMap.Heatmap ? 1f : 0f);
        previewMaterial.SetFloat("_Repeat", (float)repeatTimes);

        EditorGUILayout.Space();

        // Draw the preview scaled to fit the window width/height
        Rect rect = GUILayoutUtility.GetAspectRect((float)selectedTexture.width / selectedTexture.height);
        
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawPreviewTexture(rect, selectedTexture, previewMaterial, ScaleMode.ScaleToFit);
        }
    }
}
