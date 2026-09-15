using UnityEngine;
using UnityEditor;
using System.IO;

[System.Serializable]
public class ImageEditorCurveData 
{
    public AnimationCurve master = AnimationCurve.Linear(0, 0, 1, 1);
    public AnimationCurve r = AnimationCurve.Linear(0, 0, 1, 1);
    public AnimationCurve g = AnimationCurve.Linear(0, 0, 1, 1);
    public AnimationCurve b = AnimationCurve.Linear(0, 0, 1, 1);
}

public class ImageEditorWindow : EditorWindow
{
    private Texture2D sourceImage;
    private Texture2D previewImage;
    
    private float transparency = 1.0f;
    private Color tintColor = Color.white;
    private float hueShift = 0.0f;
    private float saturationMult = 1.0f;
    private float valueMult = 1.0f;

    private ImageEditorCurveData curves = new ImageEditorCurveData();

    // Keys for saving settings
    private const string PREF_TRANS = "ImgEd_Trans";
    private const string PREF_TINT_R = "ImgEd_TintR";
    private const string PREF_TINT_G = "ImgEd_TintG";
    private const string PREF_TINT_B = "ImgEd_TintB";
    private const string PREF_TINT_A = "ImgEd_TintA";
    private const string PREF_HUE = "ImgEd_Hue";
    private const string PREF_SAT = "ImgEd_Sat";
    private const string PREF_VAL = "ImgEd_Val";
    private const string PREF_CURVES = "ImgEd_Curves";

    [MenuItem("Tools/Image Editor")]
    public static void ShowWindow()
    {
        GetWindow<ImageEditorWindow>("Image Editor");
    }

    private void OnEnable()
    {
        // Load preferences so the same settings are kept
        transparency = EditorPrefs.GetFloat(PREF_TRANS, 1.0f);
        tintColor = new Color(
            EditorPrefs.GetFloat(PREF_TINT_R, 1.0f),
            EditorPrefs.GetFloat(PREF_TINT_G, 1.0f),
            EditorPrefs.GetFloat(PREF_TINT_B, 1.0f),
            EditorPrefs.GetFloat(PREF_TINT_A, 1.0f)
        );
        hueShift = EditorPrefs.GetFloat(PREF_HUE, 0.0f);
        saturationMult = EditorPrefs.GetFloat(PREF_SAT, 1.0f);
        valueMult = EditorPrefs.GetFloat(PREF_VAL, 1.0f);

        string curvesJson = EditorPrefs.GetString(PREF_CURVES, "");
        if (!string.IsNullOrEmpty(curvesJson))
        {
            try
            {
                curves = JsonUtility.FromJson<ImageEditorCurveData>(curvesJson);
            }
            catch
            {
                curves = new ImageEditorCurveData();
            }
        }
    }

    private void SavePrefs()
    {
        EditorPrefs.SetFloat(PREF_TRANS, transparency);
        EditorPrefs.SetFloat(PREF_TINT_R, tintColor.r);
        EditorPrefs.SetFloat(PREF_TINT_G, tintColor.g);
        EditorPrefs.SetFloat(PREF_TINT_B, tintColor.b);
        EditorPrefs.SetFloat(PREF_TINT_A, tintColor.a);
        EditorPrefs.SetFloat(PREF_HUE, hueShift);
        EditorPrefs.SetFloat(PREF_SAT, saturationMult);
        EditorPrefs.SetFloat(PREF_VAL, valueMult);
        
        string curvesJson = JsonUtility.ToJson(curves);
        EditorPrefs.SetString(PREF_CURVES, curvesJson);
    }

    private Vector2 scrollPos;

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("Basic Image Editor", EditorStyles.boldLabel);
        GUILayout.Space(5);

        EditorGUI.BeginChangeCheck();
        sourceImage = (Texture2D)EditorGUILayout.ObjectField("Source Image", sourceImage, typeof(Texture2D), false);
        
        if (EditorGUI.EndChangeCheck())
        {
            UpdatePreview();
        }

        if (sourceImage != null)
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("RGB Curves", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            curves.master = EditorGUILayout.CurveField("Master Curve", curves.master, Color.white, new Rect(0,0,1,1));
            curves.r = EditorGUILayout.CurveField("Red Curve", curves.r, Color.red, new Rect(0,0,1,1));
            curves.g = EditorGUILayout.CurveField("Green Curve", curves.g, Color.green, new Rect(0,0,1,1));
            curves.b = EditorGUILayout.CurveField("Blue Curve", curves.b, Color.blue, new Rect(0,0,1,1));

            if (GUILayout.Button("Reset Curves", GUILayout.Width(100)))
            {
                curves = new ImageEditorCurveData();
                GUI.FocusControl(null); // Remove focus to ensure visual update
            }

            EditorGUILayout.Space(10);
            GUILayout.Label("Color & Hue Adjustments", EditorStyles.boldLabel);
            
            transparency = EditorGUILayout.Slider("Transparency (Alpha)", transparency, 0f, 1f);
            tintColor = EditorGUILayout.ColorField("Tint Color", tintColor);
            hueShift = EditorGUILayout.Slider("Hue Shift", hueShift, -180f, 180f);
            saturationMult = EditorGUILayout.Slider("Saturation", saturationMult, 0f, 3f);
            valueMult = EditorGUILayout.Slider("Lightness", valueMult, 0f, 3f);

            if (EditorGUI.EndChangeCheck())
            {
                SavePrefs();
                UpdatePreview();
            }

            if (previewImage != null)
            {
                EditorGUILayout.Space(10);
                GUILayout.Label("Preview:", EditorStyles.boldLabel);
                
                // Draw preview image maintaining aspect ratio
                float aspect = (float)previewImage.width / previewImage.height;
                Rect rect = GUILayoutUtility.GetAspectRect(aspect, GUILayout.MaxHeight(400));
                
                // Draw a checkerboard or standard background for transparency
                EditorGUI.DrawTextureTransparent(rect, previewImage, ScaleMode.ScaleToFit);

                EditorGUILayout.Space(10);
                GUILayout.BeginHorizontal();
                
                if (GUILayout.Button("Save As New", GUILayout.Height(30)))
                {
                    SaveImageAsNew();
                }
                
                if (GUILayout.Button("Overwrite Original", GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog("Overwrite Original?", 
                        "Are you sure you want to overwrite the original image?\nThis cannot be undone.", 
                        "Overwrite", "Cancel"))
                    {
                        OverwriteImage(sourceImage, previewImage);
                    }
                }

                GUILayout.EndHorizontal();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Please assign a Source Image to begin editing.", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    private void UpdatePreview()
    {
        if (sourceImage == null) return;

        EnsureReadable(sourceImage);

        if (previewImage != null)
        {
            DestroyImmediate(previewImage);
        }

        previewImage = ApplyEdits(sourceImage, transparency, tintColor, hueShift, saturationMult, valueMult, curves);
    }

    private void SaveImageAsNew()
    {
        if (previewImage == null || sourceImage == null) return;

        string defaultName = sourceImage.name + "_edited.png";
        string path = EditorUtility.SaveFilePanelInProject("Save Edited Image", defaultName, "png", "Select where to save the edited image");
        
        if (string.IsNullOrEmpty(path)) return;

        byte[] pngData = previewImage.EncodeToPNG();
        if (pngData != null)
        {
            File.WriteAllBytes(path, pngData);
            AssetDatabase.Refresh();
            Debug.Log($"[ImageEditor] Image successfully saved to: {path}");
            
            // Highlight the new asset in the project window
            Object savedAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (savedAsset != null)
            {
                EditorGUIUtility.PingObject(savedAsset);
            }
        }
    }

    public static void OverwriteImage(Texture2D original, Texture2D modified)
    {
        if (original == null || modified == null) return;

        string assetPath = AssetDatabase.GetAssetPath(original);
        if (string.IsNullOrEmpty(assetPath)) return;

        string extension = Path.GetExtension(assetPath).ToLower();
        byte[] bytes = null;

        if (extension == ".jpg" || extension == ".jpeg") bytes = modified.EncodeToJPG(100);
        else if (extension == ".tga") bytes = modified.EncodeToTGA();
        else if (extension == ".exr") bytes = modified.EncodeToEXR();
        else bytes = modified.EncodeToPNG(); // default and .png

        if (bytes != null)
        {
            File.WriteAllBytes(assetPath, bytes);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[ImageEditor] Overwrote image at: {assetPath}");
        }
        else
        {
            Debug.LogError($"[ImageEditor] Failed to encode image for overwriting: {assetPath}");
        }
    }

    public static void EnsureReadable(Texture2D tex)
    {
        string assetPath = AssetDatabase.GetAssetPath(tex);
        if (!string.IsNullOrEmpty(assetPath))
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
        }
    }

    public static Texture2D ApplyEdits(Texture2D source, float trans, Color tint, float hueSh, float satMult, float valMult, ImageEditorCurveData curveData)
    {
        Texture2D result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        result.filterMode = FilterMode.Bilinear;
        
        Color[] pixels = source.GetPixels();
        Color[] newPixels = new Color[pixels.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            
            // 1. Apply RGB Curves
            c.r = curveData.r.Evaluate(curveData.master.Evaluate(c.r));
            c.g = curveData.g.Evaluate(curveData.master.Evaluate(c.g));
            c.b = curveData.b.Evaluate(curveData.master.Evaluate(c.b));

            // Clamp colors just in case the curve goes out of bounds
            c.r = Mathf.Clamp01(c.r);
            c.g = Mathf.Clamp01(c.g);
            c.b = Mathf.Clamp01(c.b);

            // 2. Apply Tint
            c.r *= tint.r;
            c.g *= tint.g;
            c.b *= tint.b;
            c.a *= tint.a;

            // 3. Apply Hue, Saturation, Value
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            
            h += hueSh / 360f;
            // Wrap hue between 0 and 1
            if (h < 0f) h += 1f;
            if (h > 1f) h -= 1f;

            s = Mathf.Clamp01(s * satMult);
            v = Mathf.Clamp01(v * valMult);

            Color finalColor = Color.HSVToRGB(h, s, v);
            
            // 4. Apply final transparency combining original alpha, tint alpha, and our slider
            finalColor.a = Mathf.Clamp01(c.a * trans);

            newPixels[i] = finalColor;
        }

        result.SetPixels(newPixels);
        result.Apply();
        return result;
    }

    private void OnDestroy()
    {
        // Cleanup preview texture to avoid memory leaks in the editor
        if (previewImage != null)
        {
            DestroyImmediate(previewImage);
        }
    }

    // =========================================================
    // RIGHT-CLICK MENU CONTEXT
    // =========================================================

    [MenuItem("Assets/Apply Last Image Edit", true)]
    private static bool ApplyLastEditValidation()
    {
        // Only enable if at least one Texture2D is selected
        foreach (var obj in Selection.objects)
        {
            if (obj is Texture2D)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path)) return true;
            }
        }
        return false;
    }

    [MenuItem("Assets/Apply Last Image Edit", false, 2000)]
    private static void ApplyLastEdit()
    {
        // Load the last settings
        float trans = EditorPrefs.GetFloat(PREF_TRANS, 1.0f);
        Color tint = new Color(
            EditorPrefs.GetFloat(PREF_TINT_R, 1.0f),
            EditorPrefs.GetFloat(PREF_TINT_G, 1.0f),
            EditorPrefs.GetFloat(PREF_TINT_B, 1.0f),
            EditorPrefs.GetFloat(PREF_TINT_A, 1.0f)
        );
        float hueSh = EditorPrefs.GetFloat(PREF_HUE, 0.0f);
        float satMult = EditorPrefs.GetFloat(PREF_SAT, 1.0f);
        float valMult = EditorPrefs.GetFloat(PREF_VAL, 1.0f);

        ImageEditorCurveData loadedCurves = new ImageEditorCurveData();
        string curvesJson = EditorPrefs.GetString(PREF_CURVES, "");
        if (!string.IsNullOrEmpty(curvesJson))
        {
            try
            {
                loadedCurves = JsonUtility.FromJson<ImageEditorCurveData>(curvesJson);
            }
            catch { }
        }

        int count = 0;
        foreach (var obj in Selection.objects)
        {
            if (obj is Texture2D tex)
            {
                string path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path))
                {
                    EnsureReadable(tex);
                    Texture2D modified = ApplyEdits(tex, trans, tint, hueSh, satMult, valMult, loadedCurves);
                    OverwriteImage(tex, modified);
                    
                    // Cleanup memory
                    DestroyImmediate(modified);
                    count++;
                }
            }
        }

        if (count > 0)
        {
            Debug.Log($"[ImageEditor] Applied last edit configuration to {count} image(s).");
        }
    }
}
