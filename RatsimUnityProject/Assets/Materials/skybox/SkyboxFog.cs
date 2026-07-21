using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

[Serializable]
[PostProcess(typeof(SkyboxFogRenderer), PostProcessEvent.BeforeTransparent, "Custom/SkyboxFog")]
public sealed class SkyboxFog : PostProcessEffectSettings
{
    [Tooltip("The skybox cubemap to blend into. If left empty, it will use the global environment reflection probe.")]
    public TextureParameter skybox = new TextureParameter { value = null };

    [Tooltip("Check this to automatically use Unity's Lighting settings for Start/End distances.")]
    public BoolParameter useRenderSettings = new BoolParameter { value = true };
    
    [Tooltip("Distance from the camera where the fog starts (only used if UseRenderSettings is false).")]
    public FloatParameter startDistance = new FloatParameter { value = 10f };

    [Tooltip("Distance from the camera where the fog reaches maximum density (only used if UseRenderSettings is false).")]
    public FloatParameter endDistance = new FloatParameter { value = 1000f };

    [Range(0f, 1f), Tooltip("Maximum density of the fog.")]
    public FloatParameter maxDensity = new FloatParameter { value = 1f };

    public override bool IsEnabledAndSupported(PostProcessRenderContext context)
    {
        return enabled.value;
    }
}

public sealed class SkyboxFogRenderer : PostProcessEffectRenderer<SkyboxFog>
{
    public override void Render(PostProcessRenderContext context)
    {
        var sheet = context.propertySheets.Get(Shader.Find("Hidden/Custom/SkyboxFog"));
        
        if (settings.skybox.value != null)
        {
            sheet.properties.SetTexture("_Skybox", settings.skybox.value);
            sheet.properties.SetFloat("_UseSkybox", 1f);
        }
        else
        {
            sheet.properties.SetFloat("_UseSkybox", 0f);
        }

        // Match Unity's default fog settings if requested
        float startDist = settings.useRenderSettings.value ? RenderSettings.fogStartDistance : settings.startDistance.value;
        float endDist = settings.useRenderSettings.value ? RenderSettings.fogEndDistance : settings.endDistance.value;

        sheet.properties.SetFloat("_StartDistance", startDist);
        sheet.properties.SetFloat("_EndDistance", endDist);
        sheet.properties.SetFloat("_MaxDensity", settings.maxDensity);
        
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(context.camera.projectionMatrix, false);
        Matrix4x4 view = context.camera.worldToCameraMatrix;
        Matrix4x4 viewProj = proj * view;
        sheet.properties.SetMatrix("_InverseViewProj", viewProj.inverse);
        sheet.properties.SetVector("_CameraPos", context.camera.transform.position);

        context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
    }
}
