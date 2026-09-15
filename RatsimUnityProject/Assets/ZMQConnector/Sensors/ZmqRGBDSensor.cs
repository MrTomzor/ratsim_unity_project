using System;
using UnityEngine;

public class ZmqRGBDSensor : MonoBehaviour
{
    public Camera cam;
    public string rgbdTopic = "/rgbd";
    public string intrinsicsTopic = "/camera_intrinsics";

    public int imageWidth = 640;
    public int imageHeight = 480;
    public float depthImageMaxRange = 100.0f;
    public bool captureDepth = true;
    public Shader linearDepthShader;

    private ZmqUnityServer conn;
    private RenderTexture rgbRenderTexture;
    private RenderTexture depthRenderTexture;
    private Texture2D rgbTexture;
    private Texture2D depthTexture;

    private byte[] rgbBuffer;
    private byte[] depthBuffer;

    void Start()
    {
        conn = ZmqUnityServer.GetInstance();

        if (cam == null)
        {
            cam = GetComponent<Camera>();
        }

        // Preallocate persistent textures once to eliminate GC pressure
        rgbRenderTexture = new RenderTexture(imageWidth, imageHeight, 24, RenderTextureFormat.ARGB32);
        rgbRenderTexture.Create();
        rgbTexture = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);
        rgbBuffer = new byte[imageWidth * imageHeight * 3];

        if (captureDepth)
        {
            depthRenderTexture = new RenderTexture(imageWidth, imageHeight, 24, RenderTextureFormat.RFloat);
            depthRenderTexture.Create();
            depthTexture = new Texture2D(imageWidth, imageHeight, TextureFormat.RFloat, false);
            depthBuffer = new byte[imageWidth * imageHeight * sizeof(float)];
        }

        conn.RegisterTimerDiscrete(SenseAndPublish, 1);

        // Publish intrinsics
        if (!string.IsNullOrEmpty(intrinsicsTopic))
        {
            var intrinsics = RGBDSensor.CreateIntrinsicsMessage(cam, imageWidth, imageHeight);
            conn.Publish(intrinsicsTopic, intrinsics);
        }
    }

    public void SenseAndPublish(ZmqTimerEvent ev)
    {
        // 1. Capture RGB directly into raw bytes
        cam.targetTexture = rgbRenderTexture;
        cam.Render();
        RenderTexture.active = rgbRenderTexture;
        rgbTexture.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
        rgbTexture.Apply();

        var rawRgbData = rgbTexture.GetRawTextureData<byte>();
        rawRgbData.CopyTo(rgbBuffer);

        var msg = new RawImageMessage
        {
            width = imageWidth,
            height = imageHeight,
            format = "RGB24",
            channels = 3,
            minDepth = 0f,
            maxDepth = depthImageMaxRange
        };

        if (captureDepth && linearDepthShader != null)
        {
            // 2. Capture linear depth directly into raw float bytes
            cam.targetTexture = depthRenderTexture;
            cam.RenderWithShader(linearDepthShader, "");
            RenderTexture.active = depthRenderTexture;
            depthTexture.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
            depthTexture.Apply();

            var rawDepthData = depthTexture.GetRawTextureData<byte>();
            rawDepthData.CopyTo(depthBuffer);

            conn.PublishBinaryDual(rgbdTopic, msg, rgbBuffer, depthBuffer);
        }
        else
        {
            conn.PublishBinary(rgbdTopic, msg, rgbBuffer);
        }

        cam.targetTexture = null;
        RenderTexture.active = null;
    }

    void OnDestroy()
    {
        if (rgbRenderTexture != null) rgbRenderTexture.Release();
        if (depthRenderTexture != null) depthRenderTexture.Release();
        if (rgbTexture != null) Destroy(rgbTexture);
        if (depthTexture != null) Destroy(depthTexture);
    }
}

