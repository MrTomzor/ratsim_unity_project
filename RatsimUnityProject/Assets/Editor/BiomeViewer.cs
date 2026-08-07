using UnityEngine;
using UnityEditor;

public class BiomeViewer : EditorWindow
{
    [MenuItem("Tools/Biome Texture Viewer")]
    public static void ShowWindow()
    {
        GetWindow<BiomeViewer>("Biome Viewer");
    }

    private Texture2D selectedTexture;
    private Material previewMaterial;
    private Texture2D lutTexture;
    
    private bool isNormalized = false;

    // Based on ESA WorldCover standard classes
    private readonly int[] classes = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110 };
    private readonly string[] labels = { 
        "Trees", "Shrubland", "Grassland", "Cropland", "Built-up", 
        "Bare / sparse vegetation", "Snow and ice", "Permanent water bodies", 
        "Herbaceous wetland", "Mangroves", "Moss and lichen" 
    };
    private readonly string[] hexColors = {
        "#006400", // 10: Trees
        "#ffbb22", // 20: Shrubland
        "#ffff4c", // 30: Grassland
        "#f096ff", // 40: Cropland
        "#fa0000", // 50: Built-up
        "#b4b4b4", // 60: Bare
        "#f0f0f0", // 70: Snow
        "#0064c8", // 80: Water
        "#0096a0", // 90: Wetland
        "#00cf75", // 100: Mangroves
        "#fae6a0"  // 110: Moss
    };

    private void OnEnable()
    {
        GenerateLUT();
        Selection.selectionChanged += OnSelectionChanged;
        OnSelectionChanged();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        if (previewMaterial != null) DestroyImmediate(previewMaterial);
        if (lutTexture != null) DestroyImmediate(lutTexture);
    }

    private void GenerateLUT()
    {
        // Pass 'true' as the 5th parameter (linear) to prevent Unity from implicitly converting 
        // our hex colors to linear space in the shader, which causes them to appear darker.
        lutTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false, true);
        // STRICT point filtering so colors don't bleed into in-between numbers
        lutTexture.filterMode = FilterMode.Point; 
        lutTexture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[256];
        for (int i = 0; i < 256; i++) 
        {
            pixels[i] = Color.black; // Default background for unclassified areas
        }

        for (int i = 0; i < classes.Length; i++)
        {
            if (ColorUtility.TryParseHtmlString(hexColors[i], out Color c))
            {
                if (classes[i] >= 0 && classes[i] < 256)
                {
                    pixels[classes[i]] = c;
                }
            }
        }
        lutTexture.SetPixels(pixels);
        lutTexture.Apply();
    }

    private void OnSelectionChanged()
    {
        if (Selection.activeObject is Texture2D tex)
        {
            if (selectedTexture != tex)
            {
                selectedTexture = tex;
                AutoDetectNormalization();
            }
        }
        else
        {
            selectedTexture = null;
        }
        Repaint();
    }
    
    private void AutoDetectNormalization()
    {
        if (selectedTexture == null) return;
        
        // Typical 8-bit formats read as 0.0-1.0 in shader. 
        // Float formats read as true raw values (e.g., 10.0, 20.0).
        if (selectedTexture.format == TextureFormat.R8 ||
            selectedTexture.format == TextureFormat.Alpha8 ||
            selectedTexture.format == TextureFormat.RGB24 ||
            selectedTexture.format == TextureFormat.RGBA32 ||
            selectedTexture.format == TextureFormat.DXT1 ||
            selectedTexture.format == TextureFormat.DXT5)
        {
            isNormalized = true;
        }
        else
        {
            isNormalized = false;
        }
    }

    private void OnGUI()
    {
        if (selectedTexture == null)
        {
            GUILayout.Label("Select a Texture2D in the Project window.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        if (previewMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/BiomePreview");
            if (shader != null)
            {
                previewMaterial = new Material(shader);
                previewMaterial.SetTexture("_LUT", lutTexture);
            }
            else
            {
                EditorGUILayout.HelpBox("Could not find shader 'Hidden/BiomePreview'.", MessageType.Error);
                return;
            }
        }

        GUILayout.Label($"Viewing Biomes: {selectedTexture.name} ({selectedTexture.width}x{selectedTexture.height})", EditorStyles.boldLabel);
        
        EditorGUILayout.Space();
        
        isNormalized = EditorGUILayout.Toggle(new GUIContent("Multiply Values by 255", 
            "Check this if the texture is 8-bit and the values are normalized between 0-1. " + 
            "Uncheck if the texture is RFloat and contains raw literal values like 10, 20, 30."), isNormalized);

        previewMaterial.SetFloat("_Multiplier", isNormalized ? 255f : 1f);

        EditorGUILayout.Space();
        
        // Draw the preview scaled to fit the window width/height
        Rect rect = GUILayoutUtility.GetAspectRect((float)selectedTexture.width / selectedTexture.height);
        
        // Center the rect horizontally if there is space
        if (rect.width < position.width)
        {
            rect.x = (position.width - rect.width) / 2f;
        }

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawPreviewTexture(rect, selectedTexture, previewMaterial, ScaleMode.ScaleToFit);
        }
        
        // Draw Legend
        EditorGUILayout.Space();
        GUILayout.Label("Biome Legend", EditorStyles.boldLabel);
        
        for (int i = 0; i < classes.Length; i++)
        {
            GUILayout.BeginHorizontal();
            ColorUtility.TryParseHtmlString(hexColors[i], out Color c);
            
            // Draw a small colored box
            Rect colorRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
            EditorGUI.DrawRect(colorRect, c);
            
            GUILayout.Label($"{classes[i]}: {labels[i]}");
            GUILayout.EndHorizontal();
        }
    }
}
