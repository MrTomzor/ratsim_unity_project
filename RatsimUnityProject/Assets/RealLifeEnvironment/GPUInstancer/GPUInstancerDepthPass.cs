using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using RealLifeEnvironment;

[ExecuteInEditMode]
public class GPUInstancerDepthPass : MonoBehaviour
{
    public Camera cam;
    private CommandBuffer depthCmd;
    private RenderTexture gpuInstancerDepthRT;
    private RenderTexture combinedDepthRT;
    private bool commandBufferAttached = false;
    private Shader depthOnlyShader;
    private Shader depthCombineShader;
    private Material depthCombineMaterial;

    private Dictionary<Material, Material> depthMaterials = new Dictionary<Material, Material>();

    static readonly BindingFlags privFlags = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly FieldInfo positionsField = typeof(GPUInstancer).GetField("instancePositionsBuffer", privFlags);
    static readonly FieldInfo propBlockField = typeof(GPUInstancer).GetField("propertyBlock", privFlags);

    public static void EnsureOnCamera(Camera targetCam)
    {
        if (targetCam != null && targetCam.GetComponent<GPUInstancerDepthPass>() == null)
        {
            targetCam.gameObject.AddComponent<GPUInstancerDepthPass>();
        }
    }

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        depthOnlyShader = Shader.Find("Custom/GPUInstancerDepthOnly");
        depthCombineShader = Shader.Find("Hidden/GPUInstancerDepthCombine");
        if (depthCombineShader != null)
        {
            depthCombineMaterial = new Material(depthCombineShader);
            depthCombineMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        depthCmd = new CommandBuffer();
        depthCmd.name = "GPUInstancer Depth Pass (Volumetric)";
    }

    void OnDisable()
    {
        RemoveCommandBuffer();

        foreach (var kvp in depthMaterials)
        {
            if (kvp.Value != null)
            {
                if (Application.isPlaying) Destroy(kvp.Value);
                else DestroyImmediate(kvp.Value);
            }
        }
        depthMaterials.Clear();

        if (depthCombineMaterial != null)
        {
            if (Application.isPlaying) Destroy(depthCombineMaterial);
            else DestroyImmediate(depthCombineMaterial);
            depthCombineMaterial = null;
        }

        if (depthCmd != null)
        {
            depthCmd.Release();
            depthCmd = null;
        }

        ReleaseRT();
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

        if (combinedDepthRT != null)
        {
            combinedDepthRT.Release();
            if (Application.isPlaying) Destroy(combinedDepthRT);
            else DestroyImmediate(combinedDepthRT);
            combinedDepthRT = null;
        }
    }

    void EnsureRT(int w, int h)
    {
        if (gpuInstancerDepthRT != null && gpuInstancerDepthRT.width == w && gpuInstancerDepthRT.height == h &&
            combinedDepthRT != null && combinedDepthRT.width == w && combinedDepthRT.height == h)
            return;

        ReleaseRT();
        gpuInstancerDepthRT = new RenderTexture(w, h, 24, RenderTextureFormat.RFloat);
        gpuInstancerDepthRT.name = "GPUInstancer Depth RT (Volumetric)";
        gpuInstancerDepthRT.filterMode = FilterMode.Point;

        combinedDepthRT = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat);
        combinedDepthRT.name = "GPUInstancer Combined Depth RT";
        combinedDepthRT.filterMode = FilterMode.Point;
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
            if (instancer.instanceMesh == null || instancer.instanceMaterial == null || instancer.argsBuffer == null) continue;

            ComputeBuffer posBuffer = (ComputeBuffer)positionsField.GetValue(instancer);
            MaterialPropertyBlock mpb = (MaterialPropertyBlock)propBlockField.GetValue(instancer);
            ComputeBuffer args = instancer.argsBuffer;
            Material srcMat = instancer.instanceMaterial;

            if (posBuffer == null || mpb == null || args == null) continue;

            Material dMat = GetDepthMaterial(srcMat);
            if (dMat == null) continue;

            // Sync all the properties
            dMat.SetVector("_BaseScale", srcMat.GetVector("_BaseScale"));
            dMat.SetFloat("_InstanceScaleRandomness", srcMat.GetFloat("_InstanceScaleRandomness"));
            dMat.SetFloat("_DistanceA", srcMat.GetFloat("_DistanceA"));
            dMat.SetVector("_ScaleMultiplierA", srcMat.GetVector("_ScaleMultiplierA"));
            dMat.SetFloat("_DistanceB", srcMat.GetFloat("_DistanceB"));
            dMat.SetVector("_ScaleMultiplierB", srcMat.GetVector("_ScaleMultiplierB"));
            if (srcMat.HasProperty("_TerrainAlignment")) dMat.SetFloat("_TerrainAlignment", srcMat.GetFloat("_TerrainAlignment"));
            dMat.SetFloat("_WindStrength", srcMat.GetFloat("_WindStrength"));
            dMat.SetVector("_WindScroll", srcMat.GetVector("_WindScroll"));
            dMat.SetFloat("_AlphaCutoff", srcMat.GetFloat("_AlphaCutoff"));
            dMat.SetFloat("_Cull", srcMat.GetFloat("_Cull"));
            if (srcMat.HasProperty("_WindTexture"))
            {
                dMat.SetTexture("_WindTexture", srcMat.GetTexture("_WindTexture"));
                dMat.SetVector("_WindTexture_ST", srcMat.GetVector("_WindTexture_ST"));
            }
            
            // Dither properties
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

            depthCmd.DrawMeshInstancedIndirect(instancer.instanceMesh, 0, dMat, -1, args, 0, mpb);
            drawnCount++;
        }

        if (drawnCount > 0)
        {
            depthCmd.SetGlobalTexture("_GPUInstancerDepthTex", gpuInstancerDepthRT);

            if (depthCombineMaterial == null && depthCombineShader != null)
            {
                depthCombineMaterial = new Material(depthCombineShader);
                depthCombineMaterial.hideFlags = HideFlags.HideAndDontSave;
            }
            else if (depthCombineShader == null)
            {
                depthCombineShader = Shader.Find("Hidden/GPUInstancerDepthCombine");
                if (depthCombineShader != null)
                {
                    depthCombineMaterial = new Material(depthCombineShader);
                    depthCombineMaterial.hideFlags = HideFlags.HideAndDontSave;
                }
            }

            if (depthCombineMaterial != null && combinedDepthRT != null)
            {
                depthCmd.Blit(gpuInstancerDepthRT, combinedDepthRT, depthCombineMaterial);
                depthCmd.SetGlobalTexture("_CameraDepthTexture", combinedDepthRT);
            }

            depthCmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            cam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, depthCmd);
            commandBufferAttached = true;
        }
        else
        {
            Shader.SetGlobalTexture("_GPUInstancerDepthTex", gpuInstancerDepthRT);
        }
    }
}
