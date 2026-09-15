using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using RealLifeEnvironment;

[RequireComponent(typeof(Camera))]
[ExecuteInEditMode]
public class DepthVisualizer : MonoBehaviour
{
    [Tooltip("Key to toggle depth view on/off.")]
    public KeyCode toggleKey = KeyCode.F5;

    [Tooltip("Enable to show depth instead of RGB")]
    public bool showDepth = false;

    public enum DepthCurveMode { Linear, Power, Logarithmic }
    public enum ColorMapMode { Grayscale, Turbo, Viridis, Magma, Custom }

    [Header("Visualization Settings")]
    [Tooltip("If true, multiplies the actual scene colors by the depth gradient instead of fully replacing them.")]
    public bool multiplyWithSceneColor = false;

    public ColorMapMode colorMap = ColorMapMode.Turbo;
    
    [Tooltip("If true, the colormap direction is reversed.")]
    public bool flipColorMap = false;

    [Tooltip("Used when Color Map Mode is set to Custom.")]
    public Gradient customGradient = new Gradient();
    private Texture2D customGradientTex;

    public DepthCurveMode depthMode = DepthCurveMode.Logarithmic;

    [Tooltip("Distance from camera (in meters) where the gradient starts (brightest color).")]
    public float nearDistance = 1.0f;

    [Tooltip("Distance from camera (in meters) where the gradient ends (darkest color).")]
    public float farDistance = 100.0f;

    [Tooltip("Multiplier 'x' for the x/dist calculation. Scales the output directly.")]
    public float depthMultiplier = 10.0f;

    [Tooltip("(Power Mode Only) A value of 1 is linear. Higher values compress far distances.")]
    [Range(0.1f, 10f)]
    public float depthPower = 2.0f;

    [Tooltip("(Logarithmic Mode Only) Extremely high precision for near objects, extreme compression for far. Higher values = more near precision.")]
    public float logCompression = 10000.0f;

    [Header("Banding / Contour Lines")]
    [Tooltip("If true, the colormap repeats infinitely. This creates 'contour lines' that let you see tiny depth changes even very far away.")]
    public bool repeatColorMap = false;
    
    [Tooltip("How tightly the bands repeat. Higher values = more bands.")]
    public float repeatFrequency = 10.0f;

    [Tooltip("If true, uses true radial (Euclidean) distance from the camera lens. If false, uses flat planar Z-depth.")]
    public bool useRadialDistance = true;


    [Tooltip("If true, the depth range is dynamically stretched to the absolute minimum and maximum values visible on screen, giving you maximum color definition regardless of distance.")]
    public bool normalizeOnScreen = false;

    private Material depthVisualizeMaterial;
    private Camera cam;
    private CommandBuffer depthCmd;
    private RenderTexture gpuInstancerDepthRT;
    private bool commandBufferAttached = false;
    private Shader depthOnlyShader;
    
    private ComputeShader minMaxCompute;
    private ComputeBuffer minMaxBuffer;
    private uint[] minMaxData = new uint[2];

    // Cache a unique depth material for each unique instance material to avoid property clobbering
    private Dictionary<Material, Material> depthMaterials = new Dictionary<Material, Material>();

    // Reflection fields to access private data in GPUInstancer
    static readonly BindingFlags privFlags = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly FieldInfo positionsField = typeof(GPUInstancer).GetField("instancePositionsBuffer", privFlags);
    static readonly FieldInfo propBlockField = typeof(GPUInstancer).GetField("propertyBlock", privFlags);

    void OnValidate()
    {
        if (colorMap == ColorMapMode.Custom && customGradient != null)
        {
            UpdateCustomGradient();
        }
    }

    void UpdateCustomGradient()
    {
        if (customGradientTex == null)
        {
            customGradientTex = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            customGradientTex.wrapMode = TextureWrapMode.Clamp;
            customGradientTex.hideFlags = HideFlags.HideAndDontSave;
        }
        for (int i = 0; i < 256; i++)
        {
            customGradientTex.SetPixel(i, 0, customGradient.Evaluate(i / 255f));
        }
        customGradientTex.Apply();
    }

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.Depth;

        Shader visShader = Shader.Find("Custom/DepthVisualize");
        if (visShader != null)
            depthVisualizeMaterial = new Material(visShader);

        depthOnlyShader = Shader.Find("Custom/GPUInstancerDepthOnly");

        depthCmd = new CommandBuffer();
        depthCmd.name = "GPUInstancer Depth Draw";

        minMaxCompute = Resources.Load<ComputeShader>("DepthMinMax");
        if (minMaxBuffer == null)
        {
            minMaxBuffer = new ComputeBuffer(2, sizeof(uint), ComputeBufferType.Structured);
        }

        UpdateCustomGradient();
    }

    void OnDisable()
    {
        RemoveCommandBuffer();

        if (depthVisualizeMaterial != null)
        {
            if (Application.isPlaying) Destroy(depthVisualizeMaterial);
            else DestroyImmediate(depthVisualizeMaterial);
            depthVisualizeMaterial = null;
        }

        foreach (var kvp in depthMaterials)
        {
            if (kvp.Value != null)
            {
                if (Application.isPlaying) Destroy(kvp.Value);
                else DestroyImmediate(kvp.Value);
            }
        }
        depthMaterials.Clear();

        if (depthCmd != null)
        {
            depthCmd.Release();
            depthCmd = null;
        }

        if (minMaxBuffer != null)
        {
            minMaxBuffer.Release();
            minMaxBuffer = null;
        }

        ReleaseRT();
    }

    void OnDestroy()
    {
        if (customGradientTex != null)
        {
            if (Application.isPlaying) Destroy(customGradientTex);
            else DestroyImmediate(customGradientTex);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            showDepth = !showDepth;
        }
    }

    void RemoveCommandBuffer()
    {
        if (commandBufferAttached && cam != null && depthCmd != null)
        {
            cam.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, depthCmd);
            commandBufferAttached = false;
        }
    }

    void ReleaseRT()
    {
        if (gpuInstancerDepthRT != null)
        {
            gpuInstancerDepthRT.Release();
            if (Application.isPlaying) Destroy(gpuInstancerDepthRT);
            else DestroyImmediate(gpuInstancerDepthRT);
            gpuInstancerDepthRT = null;
        }
    }

    void EnsureRT(int w, int h)
    {
        if (gpuInstancerDepthRT != null && gpuInstancerDepthRT.width == w && gpuInstancerDepthRT.height == h)
            return;

        ReleaseRT();
        gpuInstancerDepthRT = new RenderTexture(w, h, 24, RenderTextureFormat.RFloat);
        gpuInstancerDepthRT.name = "GPUInstancer Depth RT";
        gpuInstancerDepthRT.filterMode = FilterMode.Point;
    }

    Material GetDepthMaterial(Material srcMat)
    {
        if (srcMat == null || depthOnlyShader == null) return null;
        
        if (!depthMaterials.TryGetValue(srcMat, out Material dMat) || dMat == null)
        {
            dMat = new Material(depthOnlyShader);
            dMat.hideFlags = HideFlags.HideAndDontSave;
            depthMaterials[srcMat] = dMat;
        }
        return dMat;
    }

    void OnPreRender()
    {
        if (cam == null || depthCmd == null) return;

        RemoveCommandBuffer();
        depthCmd.Clear();

        if (!showDepth) return;

        int w = cam.pixelWidth;
        int h = cam.pixelHeight;
        if (w <= 0 || h <= 0) return;
        EnsureRT(w, h);

        depthCmd.SetRenderTarget(gpuInstancerDepthRT);
        depthCmd.ClearRenderTarget(true, true, SystemInfo.usesReversedZBuffer ? Color.black : Color.white);

        GPUInstancer[] instancers = FindObjectsByType<GPUInstancer>(FindObjectsSortMode.None);
        int drawnCount = 0;
        foreach (GPUInstancer instancer in instancers)
        {
            if (!instancer.isActiveAndEnabled || !instancer.showInDepthMap) continue;

            if (instancer.instanceMesh == null || instancer.instanceMaterial == null || instancer.argsBuffer == null)
                continue;

            ComputeBuffer posBuffer = (ComputeBuffer)positionsField.GetValue(instancer);
            MaterialPropertyBlock mpb = (MaterialPropertyBlock)propBlockField.GetValue(instancer);
            ComputeBuffer args = instancer.argsBuffer;
            Mesh mesh = instancer.instanceMesh;
            Material srcMat = instancer.instanceMaterial;

            if (posBuffer == null || mpb == null || args == null) continue;

            Material dMat = GetDepthMaterial(srcMat);
            if (dMat == null) continue;

            dMat.SetVector("_BaseScale", srcMat.GetVector("_BaseScale"));
            dMat.SetFloat("_InstanceScaleRandomness", srcMat.GetFloat("_InstanceScaleRandomness"));
            dMat.SetFloat("_DistanceA", srcMat.GetFloat("_DistanceA"));
            dMat.SetVector("_ScaleMultiplierA", srcMat.GetVector("_ScaleMultiplierA"));
            dMat.SetFloat("_DistanceB", srcMat.GetFloat("_DistanceB"));
            dMat.SetVector("_ScaleMultiplierB", srcMat.GetVector("_ScaleMultiplierB"));
            if (srcMat.HasProperty("_TerrainAlignment"))
                dMat.SetFloat("_TerrainAlignment", srcMat.GetFloat("_TerrainAlignment"));
            dMat.SetFloat("_WindStrength", srcMat.GetFloat("_WindStrength"));
            dMat.SetVector("_WindScroll", srcMat.GetVector("_WindScroll"));
            dMat.SetFloat("_AlphaCutoff", srcMat.GetFloat("_AlphaCutoff"));
            dMat.SetFloat("_Cull", srcMat.GetFloat("_Cull"));
            if (srcMat.HasProperty("_WindTexture"))
            {
                dMat.SetTexture("_WindTexture", srcMat.GetTexture("_WindTexture"));
                dMat.SetVector("_WindTexture_ST", srcMat.GetVector("_WindTexture_ST"));
            }
            
            // Sync Dither Settings
            if (srcMat.HasProperty("_DitherTransparency")) dMat.SetFloat("_DitherTransparency", srcMat.GetFloat("_DitherTransparency"));
            if (srcMat.HasProperty("_UseDitherTexture")) dMat.SetFloat("_UseDitherTexture", srcMat.GetFloat("_UseDitherTexture"));
            if (srcMat.HasProperty("_DitherTexture")) dMat.SetTexture("_DitherTexture", srcMat.GetTexture("_DitherTexture"));
            if (srcMat.HasProperty("_DitherTextureScale")) dMat.SetFloat("_DitherTextureScale", srcMat.GetFloat("_DitherTextureScale"));
            if (srcMat.HasProperty("_DitherMeshUVInfluence")) dMat.SetFloat("_DitherMeshUVInfluence", srcMat.GetFloat("_DitherMeshUVInfluence"));
            if (srcMat.HasProperty("_DitherInstanceInfluence")) dMat.SetFloat("_DitherInstanceInfluence", srcMat.GetFloat("_DitherInstanceInfluence"));
            if (srcMat.HasProperty("_DitherAnimationSpeed")) dMat.SetFloat("_DitherAnimationSpeed", srcMat.GetFloat("_DitherAnimationSpeed"));
            if (srcMat.HasProperty("_DitherSolidThreshold")) dMat.SetFloat("_DitherSolidThreshold", srcMat.GetFloat("_DitherSolidThreshold"));
            if (srcMat.HasProperty("_GlobalAlphaMultiplier")) dMat.SetFloat("_GlobalAlphaMultiplier", srcMat.GetFloat("_GlobalAlphaMultiplier"));
            
            if (srcMat.IsKeywordEnabled("_DITHERED_TRANSPARENCY")) dMat.EnableKeyword("_DITHERED_TRANSPARENCY");
            else dMat.DisableKeyword("_DITHERED_TRANSPARENCY");
            
            if (srcMat.IsKeywordEnabled("_USE_DITHER_TEXTURE")) dMat.EnableKeyword("_USE_DITHER_TEXTURE");
            else dMat.DisableKeyword("_USE_DITHER_TEXTURE");
            
            dMat.SetVector("_MainCameraPosition", cam.transform.position);

            depthCmd.DrawMeshInstancedIndirect(mesh, 0, dMat, -1, args, 0, mpb);
            drawnCount++;
        }

        if (drawnCount > 0)
        {
            depthCmd.SetGlobalTexture("_GPUInstancerDepthTex", gpuInstancerDepthRT);
            depthCmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            cam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, depthCmd);
            commandBufferAttached = true;
        }
        else
        {
            Shader.SetGlobalTexture("_GPUInstancerDepthTex", gpuInstancerDepthRT);
        }
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (showDepth && depthVisualizeMaterial != null)
        {
            depthVisualizeMaterial.SetFloat("_NearDist", nearDistance);
            depthVisualizeMaterial.SetFloat("_FarDist", farDistance);
            depthVisualizeMaterial.SetFloat("_DepthMultiplier", depthMultiplier);
            depthVisualizeMaterial.SetFloat("_DepthPower", depthPower);
            depthVisualizeMaterial.SetFloat("_LogCompression", logCompression);
            depthVisualizeMaterial.SetInt("_DepthMode", (int)depthMode);
            depthVisualizeMaterial.SetInt("_ColorMapMode", (int)colorMap);
            depthVisualizeMaterial.SetInt("_RepeatColorMap", repeatColorMap ? 1 : 0);
            depthVisualizeMaterial.SetFloat("_RepeatFrequency", repeatFrequency);
            depthVisualizeMaterial.SetInt("_UseRadialDistance", useRadialDistance ? 1 : 0);
            depthVisualizeMaterial.SetMatrix("_CamProj", cam.projectionMatrix);
            depthVisualizeMaterial.SetInt("_FlipColorMap", flipColorMap ? 1 : 0);
            depthVisualizeMaterial.SetInt("_MultiplyWithSceneColor", multiplyWithSceneColor ? 1 : 0);

            if (colorMap == ColorMapMode.Custom)
            {
                if (customGradientTex == null) UpdateCustomGradient();
                depthVisualizeMaterial.SetTexture("_CustomColorMap", customGradientTex);
            }

            if (normalizeOnScreen && minMaxCompute != null && minMaxBuffer != null)
            {
                minMaxData[0] = 0xFFFFFFFF; // Max uint (positive infinity for asfloat)
                minMaxData[1] = 0;          // Min uint (0.0 for asfloat)
                minMaxBuffer.SetData(minMaxData);

                int kernel = minMaxCompute.FindKernel("CSMain");
                Texture camDepthTex = Shader.GetGlobalTexture("_CameraDepthTexture");
                if (camDepthTex != null)
                {
                    minMaxCompute.SetTexture(kernel, "_CameraDepthTexture", camDepthTex);
                    minMaxCompute.SetTexture(kernel, "_GPUInstancerDepthTex", gpuInstancerDepthRT);
                    minMaxCompute.SetBuffer(kernel, "_MinMaxBuffer", minMaxBuffer);
                    minMaxCompute.SetMatrix("_CamProj", cam.projectionMatrix);
                    minMaxCompute.SetInt("_UseRadialDistance", useRadialDistance ? 1 : 0);
                    minMaxCompute.SetFloat("_DepthMultiplier", depthMultiplier);
                    minMaxCompute.SetFloat("_FarPlane", cam.farClipPlane);
                    minMaxCompute.SetInt("_IsReversedZ", SystemInfo.usesReversedZBuffer ? 1 : 0);

                    int w = cam.pixelWidth;
                    int h = cam.pixelHeight;
                    minMaxCompute.Dispatch(kernel, Mathf.CeilToInt(w / 8.0f), Mathf.CeilToInt(h / 8.0f), 1);

                    depthVisualizeMaterial.SetBuffer("_MinMaxBuffer", minMaxBuffer);
                }
            }
            depthVisualizeMaterial.SetInt("_NormalizeOnScreen", normalizeOnScreen ? 1 : 0);

            Graphics.Blit(source, destination, depthVisualizeMaterial);
        }
        else
        {
            Graphics.Blit(source, destination);
        }
    }
}
