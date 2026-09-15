using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

[Serializable]
public sealed class AnimationCurveParameter : ParameterOverride<AnimationCurve>
{
    public override void Interp(AnimationCurve from, AnimationCurve to, float t)
    {
        value = t > 0.5f ? to : from;
    }
}

[Serializable]
[PostProcess(typeof(VolumetricSmokeEffectRenderer), PostProcessEvent.BeforeTransparent, "Custom/Volumetric Smoke")]
public sealed class VolumetricSmokeEffect : PostProcessEffectSettings
{
    [UnityEngine.Min(1)]
    [Tooltip("Downsample factor (e.g. 2 means half resolution). Higher is faster but blockier.")]
    public IntParameter downsample = new IntParameter { value = 2 };
    
    [Tooltip("Enable to automatically scale the downsample factor so the performance cost remains constant even if the game's resolution changes.")]
    public BoolParameter useReferenceResolution = new BoolParameter { value = false };
    
    [Tooltip("The screen resolution you are currently tuning the downsample factor for.")]
    public Vector2Parameter referenceResolution = new Vector2Parameter { value = new Vector2(1920, 1080) };
    
    [Header("Raymarching Settings")]
    [Tooltip("Number of steps the ray takes. Higher = better quality but slower.")]
    public IntParameter stepCount = new IntParameter { value = 10 };
    
    [Tooltip("Distance in meters to advance the ray before beginning to sample smoke. Useful for skipping over near-camera geometry or clipping artifacts.")]
    public FloatParameter preliminaryRayStep = new FloatParameter { value = 3f };
    
    [Tooltip("Uses Interleaved Gradient Noise to prevent banding. Disable if strict upsampling looks too pixelated.")]
    public BoolParameter useDither = new BoolParameter { value = true };
    
    [Tooltip("Minimum accepted density based on the number of unskipped steps. X-axis = step count, Y-axis = min density. Skipped steps do not increment the counter.")]
    public AnimationCurveParameter minDensityByStepCurve = new AnimationCurveParameter { value = AnimationCurve.Linear(0, 0, 128, 0.5f) };
    
    [Tooltip("Maximum draw distance for the near phase.")]
    public FloatParameter maxDrawDistance = new FloatParameter { value = 300f };
    
    [Header("Phase 2 Raymarching (Distant)")]
    [Tooltip("Number of steps for the distant phase. (No shadow oversampling here)")]
    [Range(0, 512)]
    public IntParameter stepCount2 = new IntParameter { value = 96 };
    
    [Tooltip("Maximum draw distance for the distant phase.")]
    public FloatParameter maxDrawDistance2 = new FloatParameter { value = 10000f };
    
    [Tooltip("Enable shadow sampling in the distant phase. Disabling saves performance at the cost of unshadowed distant smoke.")]
    public BoolParameter phase2Shadows = new BoolParameter { value = true };
    
    [Header("Density Settings")]
    [Tooltip("Maximum height of the smoke above the terrain in meters.")]
    public FloatParameter smokeHeight = new FloatParameter { value = 67.7f };
    
    [Tooltip("How many meters below the max height the smoke begins to fade out to avoid a harsh cut.")]
    public FloatParameter smokeHeightFade = new FloatParameter { value = 54.1f };
    
    [Tooltip("Distance from camera where smoke begins to fade out.")]
    public FloatParameter densityFadeStart = new FloatParameter { value = 5000f };
    
    [Tooltip("Distance from camera where smoke is completely faded out.")]
    public FloatParameter densityFadeEnd = new FloatParameter { value = 10000f };
    
    [Tooltip("The distance from water where density becomes 0.")]
    public FloatParameter maxWaterDistance = new FloatParameter { value = 300f };
    
    [Tooltip("Curve defining how density drops off as distance from water increases. (0,1) is at water, (1,0) is at max distance.")]
    public AnimationCurveParameter waterDistanceDensityCurve = new AnimationCurveParameter { value = AnimationCurve.Linear(0f, 1f, 1f, 0f) };
    
    [Header("Density Settings")]
    [Tooltip("Overall thickness multiplier for the smoke.")]
    [Range(0f, 10f)]
    public FloatParameter globalDensityMultiplier = new FloatParameter { value = 0.02f };
    
    [Header("Upsampling Settings")]
    [Tooltip("Number of adjacent pixels to evaluate for depth-aware weighted upsampling. 1 = hard, 4 = 2x2, 9 = 3x3, etc.")]
    [Range(1, 25)]
    public IntParameter upsampleSamples = new IntParameter { value = 4 };
    
    [Header("Color Settings")]
    public ColorParameter smokeColor = new ColorParameter { value = Color.white };
    
    [Tooltip("Smoke color at the distant color distance. The smoke lerps from Smoke Color to this over distance.")]
    public ColorParameter smokeColorDistant = new ColorParameter { value = new Color(0.415f, 0.525f, 0.678f, 1f) };
    
    [Tooltip("Distance from camera at which the smoke fully transitions to the distant color.")]
    public FloatParameter smokeColorDistance = new FloatParameter { value = 14903f };
    
    [Tooltip("How much the main directional light color tints the lit smoke. 0 = no tint, 1 = fully tinted. Does not affect the distant color.")]
    [Range(0f, 1f)]
    public FloatParameter sunColorInfluence = new FloatParameter { value = 0.399f };
    
    [Header("Shadow Settings")]
    [Tooltip("How many times to sample the shadow map per ray step. 1 = standard, 2+ = catches thinner shadows.")]
    [Range(1, 16)]
    public IntParameter shadowOversample = new IntParameter { value = 1 };
    
    [Tooltip("The step index at which to start oversampling. Steps before this will use 1 sample to save performance.")]
    [UnityEngine.Min(0)]
    public IntParameter shadowOversampleStartStep = new IntParameter { value = 1 };
    
    [Tooltip("Adds a random 3D offset (in meters) to shadow samples to create soft volumetric shadows and hide banding.")]
    public FloatParameter shadowJitter = new FloatParameter { value = 0.1f };
    
    [Tooltip("How many jittered samples to take and blend per evaluation. 1 = fast/hard, 2+ = softer shadows.")]
    [Range(1, 16)]
    public IntParameter shadowJitterSamples = new IntParameter { value = 1 };
    
    [Tooltip("Maximum number of ray steps that will use jitter multisampling. Steps beyond this will only use 1 sample to save performance.")]
    [UnityEngine.Min(0)]
    public IntParameter shadowJitterMaxSteps = new IntParameter { value = 0 };
    
    [Tooltip("If the shadow map attenuation is below this value, the smoke is considered in shadow.")]
    [Range(0f, 1f)]
    public FloatParameter shadowThreshold = new FloatParameter { value = 0.5f };
    
    [Tooltip("How much shadow to apply if only the absolute edge of the step is in shadow. (1 = hard shadows, 0 = very soft falloff)")]
    [Range(0f, 1f)]
    public FloatParameter edgeShadowWeight = new FloatParameter { value = 0.411f };
    
    [Tooltip("Color of the smoke when in shadow.")]
    public ColorParameter shadowColor = new ColorParameter { value = Color.black };
    
    [Tooltip("Density multiplier when the smoke is in shadow.")]
    [Range(0f, 10f)]
    public FloatParameter shadowDensityMultiplier = new FloatParameter { value = 1f };
    
    [Header("Smoke Self-Shadow")]
    [Tooltip("Enable smoke-on-smoke shadowing. Samples smoke density along the sun direction to darken occluded areas.")]
    public BoolParameter smokeSelfShadow = new BoolParameter { value = true };
    
    [Tooltip("How far along the sun direction to sample for self-shadowing (meters) per step.")]
    public FloatParameter selfShadowStepSize = new FloatParameter { value = 5f };
    
    [Tooltip("Number of steps to take for self-shadowing in Phase 1.")]
    [Range(1, 16)]
    public IntParameter selfShadowStepsPhase1 = new IntParameter { value = 1 };
    
    [Tooltip("Strength multiplier for the self-shadow effect in Phase 1.")]
    [Range(0f, 5f)]
    public FloatParameter selfShadowStrengthPhase1 = new FloatParameter { value = 1f };
    
    [Tooltip("Number of steps to take for self-shadowing in Phase 2.")]
    [Range(1, 16)]
    public IntParameter selfShadowStepsPhase2 = new IntParameter { value = 1 };
    
    [Tooltip("Strength multiplier for the self-shadow effect in Phase 2.")]
    [Range(0f, 5f)]
    public FloatParameter selfShadowStrengthPhase2 = new FloatParameter { value = 1f };

    public enum DebugMode { None, SmokeDepth, SmokeRaymarch }

    [Serializable]
    public sealed class DebugModeParameter : ParameterOverride<DebugMode> { }

    [Header("Debugging")]
    [Tooltip("Visualize intermediate render targets.")]
    public DebugModeParameter debugMode = new DebugModeParameter { value = DebugMode.None };
}

public sealed class VolumetricSmokeEffectRenderer : PostProcessEffectRenderer<VolumetricSmokeEffect>
{
    private Texture2D densityCurveTexture;
    private Vector4[] minDensityArray = new Vector4[64];

    public override void Init()
    {
        base.Init();
    }

    public override void Release()
    {
        if (densityCurveTexture != null)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(densityCurveTexture);
            else
                UnityEngine.Object.DestroyImmediate(densityCurveTexture);
            densityCurveTexture = null;
        }
        base.Release();
    }

    private void UpdateCurves(VolumetricSmokeEffect settings)
    {
        if (settings.minDensityByStepCurve.value != null)
        {
            for (int i = 0; i < 256; i++)
            {
                float val = settings.minDensityByStepCurve.value.Evaluate(i);
                int vecIndex = i / 4;
                int compIndex = i % 4;
                
                Vector4 vec = minDensityArray[vecIndex];
                vec[compIndex] = val;
                minDensityArray[vecIndex] = vec;
            }
        }

        if (settings.waterDistanceDensityCurve.value == null) return;
        if (densityCurveTexture == null)
        {
            densityCurveTexture = new Texture2D(256, 1, TextureFormat.R8, false, true);
            densityCurveTexture.wrapMode = TextureWrapMode.Clamp;
            densityCurveTexture.filterMode = FilterMode.Bilinear;
            densityCurveTexture.hideFlags = HideFlags.HideAndDontSave;
        }
        
        Color32[] pixels = new Color32[256];
        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f;
            float val = Mathf.Clamp01(settings.waterDistanceDensityCurve.value.Evaluate(t));
            byte b = (byte)(val * 255f);
            pixels[i] = new Color32(b, b, b, 255);
        }
        densityCurveTexture.SetPixels32(pixels);
        densityCurveTexture.Apply(false);
    }

    public override void Render(PostProcessRenderContext context)
    {
        Shader shader = Shader.Find("Hidden/VolumetricSmoke");
        if (shader == null) return;
        
        PropertySheet sheet = context.propertySheets.Get(shader);
        Camera cam = context.camera;
        
        cam.depthTextureMode |= DepthTextureMode.Depth;
        UpdateCurves(settings);

        Transform camTr = cam.transform;
        float far = cam.farClipPlane;
        Vector3[] corners = new Vector3[4];
        cam.CalculateFrustumCorners(new Rect(0, 0, 1, 1), far, Camera.MonoOrStereoscopicEye.Mono, corners);
        
        sheet.properties.SetVector("_FrustumBL", camTr.TransformVector(corners[0]));
        sheet.properties.SetVector("_FrustumTL", camTr.TransformVector(corners[1]));
        sheet.properties.SetVector("_FrustumTR", camTr.TransformVector(corners[2]));
        sheet.properties.SetVector("_FrustumBR", camTr.TransformVector(corners[3]));
        
        sheet.properties.SetVector("_CameraWorldPos", camTr.position);
        
        Light sun = RenderSettings.sun;
        Color tintedSmokeColor = settings.smokeColor.value;
        if (sun != null && settings.sunColorInfluence.value > 0f)
        {
            Color sunCol = sun.color * sun.intensity;
            tintedSmokeColor = Color.Lerp(settings.smokeColor.value, settings.smokeColor.value * sunCol, settings.sunColorInfluence.value);
        }
        sheet.properties.SetColor("_SmokeColor", tintedSmokeColor);
        sheet.properties.SetColor("_FarSmokeColor", settings.smokeColorDistant.value);
        sheet.properties.SetFloat("_FarSmokeColorDist", Mathf.Max(0.001f, settings.smokeColorDistance.value));
        
        sheet.properties.SetInt("_ShadowOversample", settings.shadowOversample.value);
        sheet.properties.SetInt("_ShadowOversampleStartStep", settings.shadowOversampleStartStep.value);
        sheet.properties.SetFloat("_ShadowJitter", settings.shadowJitter.value);
        sheet.properties.SetInt("_ShadowJitterSamples", settings.shadowJitterSamples.value);
        sheet.properties.SetInt("_ShadowJitterMaxSteps", settings.shadowJitterMaxSteps.value);
        sheet.properties.SetFloat("_ShadowThreshold", settings.shadowThreshold.value);
        sheet.properties.SetFloat("_EdgeShadowWeight", settings.edgeShadowWeight.value);
        sheet.properties.SetColor("_ShadowColor", settings.shadowColor.value);
        sheet.properties.SetFloat("_ShadowDensityMultiplier", settings.shadowDensityMultiplier.value);
        
        sheet.properties.SetInt("_SmokeSelfShadow", settings.smokeSelfShadow.value ? 1 : 0);
        sheet.properties.SetFloat("_SelfShadowStepSize", settings.selfShadowStepSize.value);
        sheet.properties.SetInt("_SelfShadowStepsPhase1", settings.selfShadowStepsPhase1.value);
        sheet.properties.SetFloat("_SelfShadowStrengthPhase1", settings.selfShadowStrengthPhase1.value);
        sheet.properties.SetInt("_SelfShadowStepsPhase2", settings.selfShadowStepsPhase2.value);
        sheet.properties.SetFloat("_SelfShadowStrengthPhase2", settings.selfShadowStrengthPhase2.value);
        if (sun != null)
            sheet.properties.SetVector("_SunDirection", -sun.transform.forward);
        else
            sheet.properties.SetVector("_SunDirection", Vector3.up);
        
        sheet.properties.SetInt("_StepCount", settings.stepCount.value);
        sheet.properties.SetInt("_FrameCount", Time.frameCount % 64);
        sheet.properties.SetVectorArray("_MinDensityByStep", minDensityArray);
        sheet.properties.SetFloat("_PreliminaryRayStep", settings.preliminaryRayStep.value);
        sheet.properties.SetFloat("_MaxDrawDistance", settings.maxDrawDistance.value);
        sheet.properties.SetInt("_StepCount2", settings.stepCount2.value);
        sheet.properties.SetFloat("_MaxDrawDistance2", settings.maxDrawDistance2.value);
        sheet.properties.SetFloat("_DensityFadeStart", settings.densityFadeStart.value);
        sheet.properties.SetFloat("_DensityFadeEnd", Mathf.Max(settings.densityFadeStart.value + 0.001f, settings.densityFadeEnd.value));
        sheet.properties.SetFloat("_MaxDistance", settings.maxWaterDistance.value);
        
        sheet.properties.SetFloat("_SmokeHeight", settings.smokeHeight.value);
        sheet.properties.SetFloat("_SmokeHeightFade", settings.smokeHeightFade.value);
        
        sheet.properties.SetFloat("_GlobalDensityMultiplier", settings.globalDensityMultiplier.value);
        if (densityCurveTexture != null)
            sheet.properties.SetTexture("_DensityCurveMap", densityCurveTexture);
        
        sheet.properties.SetFloat("_UseDither", settings.useDither.value ? 1f : 0f);
        sheet.properties.SetInt("_Phase2Shadows", settings.phase2Shadows.value ? 1 : 0);
        
        sheet.properties.SetInt("_UpsampleSamples", settings.upsampleSamples.value);
        sheet.properties.SetFloat("_FarClipPlane", cam.farClipPlane);

        int resWidth = context.screenWidth;
        int resHeight = context.screenHeight;
        
        if (settings.useReferenceResolution.value && settings.referenceResolution.value.x > 0 && settings.referenceResolution.value.y > 0)
        {
            float currentPixels = context.screenWidth * context.screenHeight;
            float referencePixels = settings.referenceResolution.value.x * settings.referenceResolution.value.y;
            float dynamicDownscale = Mathf.Sqrt(currentPixels / referencePixels) * settings.downsample.value;
            
            resWidth = Mathf.Max(1, Mathf.RoundToInt(context.screenWidth / dynamicDownscale));
            resHeight = Mathf.Max(1, Mathf.RoundToInt(context.screenHeight / dynamicDownscale));
        }
        else
        {
            resWidth = Mathf.Max(1, context.screenWidth / settings.downsample.value);
            resHeight = Mathf.Max(1, context.screenHeight / settings.downsample.value);
        }

        int smokeDepthRT = Shader.PropertyToID("_SmokeDepthRT");
        int smokeRT = Shader.PropertyToID("_SmokeRT");
        
        context.command.GetTemporaryRT(smokeDepthRT, resWidth, resHeight, 0, FilterMode.Point, RenderTextureFormat.RFloat);
        context.command.GetTemporaryRT(smokeRT, resWidth, resHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

        context.command.BlitFullscreenTriangle(context.source, smokeDepthRT, sheet, 2);
        context.command.SetGlobalTexture("_SmokeDepthTex", smokeDepthRT);

        context.command.BlitFullscreenTriangle(context.source, smokeRT, sheet, 0); 
        context.command.SetGlobalTexture("_SmokeTex", smokeRT);
        
        sheet.properties.SetVector("_SmokeTex_TexelSize", new Vector4(1f / resWidth, 1f / resHeight, resWidth, resHeight));
        
        if (settings.debugMode.value == VolumetricSmokeEffect.DebugMode.SmokeDepth)
        {
            context.command.BlitFullscreenTriangle(smokeDepthRT, context.destination, sheet, 2); 
            // Wait, pass 2 is depth calculation. We just want to copy it to screen for debug.
            // But we don't have a copy pass. Let's just Blit it with default command.Blit since it's just for debug.
            context.command.Blit(smokeDepthRT, context.destination);
        }
        else if (settings.debugMode.value == VolumetricSmokeEffect.DebugMode.SmokeRaymarch)
        {
            context.command.Blit(smokeRT, context.destination);
        }
        else
        {
            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 1);
        }

        context.command.ReleaseTemporaryRT(smokeRT);
        context.command.ReleaseTemporaryRT(smokeDepthRT);
    }
}
