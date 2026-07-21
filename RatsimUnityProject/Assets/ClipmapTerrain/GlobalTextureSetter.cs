using UnityEngine;

[ExecuteAlways]
public class GlobalTextureSetter : MonoBehaviour
{
    [Tooltip("The name of the global shader property.")]
    public string globalTextureName = "_TerrainTexture1";

    [Tooltip("The name of the second global shader property.")]
    public string globalTextureName2 = "_TerrainTexture2";

    [Header("Capture Settings")]
    [Tooltip("The XZ center position of the capture area.")]
    public Vector2 captureCenter = Vector2.zero;

    [Tooltip("The size of the captured area (width/height).")]
    public float captureSize = 4000f;

    [Tooltip("The resolution of the generated texture.")]
    public int captureResolution = 4096;

    [Tooltip("The layer mask to capture.")]
    public LayerMask captureLayer;

    [Header("Distance Field Settings")]
    [Tooltip("If assigned, the captured texture will be converted to a distance field using JFA.")]
    public ComputeShader jfaShader;

    [Header("Automatic Capture Settings")]
    [Tooltip("If assigned, the capture center will automatically follow this transform.")]
    public Transform followTarget;

    [Tooltip("The distance grid size to snap to. Captures only when moving to a new grid cell to prevent constant rendering.")]
    public float updateThreshold = 50f;

    [Header("Debug")]
    [Tooltip("If true, draws the currently active texture onscreen.")]
    public bool debugDrawTexture = false;



    private RenderTexture captureRT;
    private RenderTexture distanceRT;
    private RenderTexture distanceRT2;
    private Vector2 lastCenterPos = new Vector2(float.NaN, float.NaN);
    private Camera captureCamera;

    void Update()
    {
        if (Application.isPlaying && followTarget != null && updateThreshold > 0f)
        {
            Vector2 targetPos = new Vector2(followTarget.position.x, followTarget.position.z);
            bool firstInit = float.IsNaN(lastCenterPos.x);

            if (firstInit || Vector2.Distance(targetPos, lastCenterPos) > updateThreshold || captureRT == null || !captureRT.IsCreated())
            {
                float pixelSize = captureSize / captureResolution;
                Vector2 snappedPos = new Vector2(
                    Mathf.Round(targetPos.x / pixelSize) * pixelSize,
                    Mathf.Round(targetPos.y / pixelSize) * pixelSize
                );

                lastCenterPos = snappedPos;
                captureCenter = snappedPos;
                CaptureTexture(firstInit);
            }
            else
            {
                UpdateGlobalTexture();
            }
        }
        else
        {
            UpdateGlobalTexture();
        }
    }
    
    void OnEnable()
    {
        UpdateGlobalTexture();
    }

    void OnValidate()
    {
        UpdateGlobalTexture();
    }

    void OnDisable()
    {
        if (captureRT != null)
        {
            captureRT.Release();
            captureRT = null;
        }
        if (distanceRT != null)
        {
            distanceRT.Release();
            distanceRT = null;
        }
        if (distanceRT2 != null)
        {
            distanceRT2.Release();
            distanceRT2 = null;
        }
    }

    private Vector4 capturedBounds = Vector4.zero;

    private void UpdateGlobalTexture()
    {
        RenderTexture finalRT = (jfaShader != null && distanceRT != null) ? distanceRT : captureRT;

        if (finalRT != null && !string.IsNullOrEmpty(globalTextureName))
        {
            Shader.SetGlobalTexture(globalTextureName, finalRT);
            Shader.SetGlobalVector(globalTextureName + "_TexelSize", new Vector4(1f / finalRT.width, 1f / finalRT.height, finalRT.width, finalRT.height));
            
            if (capturedBounds != Vector4.zero)
            {
                Shader.SetGlobalVector(globalTextureName + "_Bounds", capturedBounds);
            }
        }

        RenderTexture finalRT2 = (jfaShader != null && distanceRT2 != null) ? distanceRT2 : captureRT;

        if (finalRT2 != null && !string.IsNullOrEmpty(globalTextureName2))
        {
            Shader.SetGlobalTexture(globalTextureName2, finalRT2);
            Shader.SetGlobalVector(globalTextureName2 + "_TexelSize", new Vector4(1f / finalRT2.width, 1f / finalRT2.height, finalRT2.width, finalRT2.height));
            
            if (capturedBounds != Vector4.zero)
            {
                Shader.SetGlobalVector(globalTextureName2 + "_Bounds", capturedBounds);
            }
        }
    }

    [ContextMenu("Capture Texture")]
    public void CaptureTextureMenu()
    {
        CaptureTexture(true);
    }

    public void CaptureTexture(bool forceSync = false)
    {
        if (Application.isPlaying && forceSync) 
            Debug.LogWarning("Stall Warning: Forced synchronous capture during gameplay!");

        if (captureCamera == null)
        {
            if (Application.isPlaying)
                Debug.LogWarning("Stall Warning: Capture camera was null, using GameObject.Find!");
            GameObject camGO = GameObject.Find("GlobalTextureCaptureCamera");
            if (camGO == null)
            {
                camGO = new GameObject("GlobalTextureCaptureCamera");
                camGO.hideFlags = HideFlags.HideAndDontSave;
            }
            captureCamera = camGO.GetComponent<Camera>();
            if (captureCamera == null) captureCamera = camGO.AddComponent<Camera>();
        }

        Camera cam = captureCamera;

        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.Color;
        cam.backgroundColor = new Color(0, 0, 0, 0); 
        cam.orthographic = true;
        cam.allowMSAA = false;
        cam.allowHDR = false;

        cam.transform.position = new Vector3(captureCenter.x, 1000f, captureCenter.y);
        cam.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
        cam.orthographicSize = captureSize / 2f;
        cam.nearClipPlane = -2000;
        cam.farClipPlane = 2000;
        cam.cullingMask = captureLayer;

        if (captureRT == null || captureRT.width != captureResolution)
        {
            if (Application.isPlaying)
                Debug.LogWarning("Stall Warning: RenderTexture was lost or changed resolution and re-allocated!");
            if (captureRT != null) captureRT.Release();
            captureRT = new RenderTexture(captureResolution, captureResolution, 24, RenderTextureFormat.ARGB32);
            captureRT.filterMode = FilterMode.Bilinear;
            captureRT.wrapMode = TextureWrapMode.Repeat;
            captureRT.Create();
        }
        
        cam.targetTexture = captureRT;

        if (forceSync || !Application.isPlaying)
        {
            bool oldFog = RenderSettings.fog;
            RenderSettings.fog = false;
            cam.Render();
            RenderSettings.fog = oldFog;
            OnCaptureComplete();
        }
        else
        {
            var callback = cam.GetComponent<AsyncCaptureCallback>();
            if (callback == null) callback = cam.gameObject.AddComponent<AsyncCaptureCallback>();
            callback.setter = this;
            cam.enabled = true; // Async render via pipeline
        }
    }

    public void OnCaptureComplete()
    {
        if (jfaShader != null)
        {
            if (distanceRT != null) distanceRT.Release();
            distanceRT = ClipmapTerrain.JumpFloodGenerator.GenerateDistanceTexture(jfaShader, captureRT, captureSize, 0);

            if (distanceRT2 != null) distanceRT2.Release();
            distanceRT2 = ClipmapTerrain.JumpFloodGenerator.GenerateDistanceTexture(jfaShader, captureRT, captureSize, 1);
        }

        capturedBounds = new Vector4(captureCenter.x, captureCenter.y, captureSize, 0);
        UpdateGlobalTexture();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector3 center = new Vector3(captureCenter.x, transform.position.y, captureCenter.y);
        Vector3 size = new Vector3(captureSize, 0.1f, captureSize);
        Gizmos.DrawWireCube(center, size);
    }

    void OnGUI()
    {
        if (debugDrawTexture)
        {
            float size = Mathf.Min(Screen.width, Screen.height) * 0.4f;
            float padding = 10f;
            float currentX = padding;

            if (captureRT != null)
            {
                GUI.DrawTexture(new Rect(currentX, padding, size, size), captureRT, ScaleMode.ScaleToFit, false);
                currentX += size + padding;
            }

            if (distanceRT != null)
            {
                GUI.DrawTexture(new Rect(currentX, padding, size, size), distanceRT, ScaleMode.ScaleToFit, false);
                currentX += size + padding;
            }

            if (distanceRT2 != null)
            {
                GUI.DrawTexture(new Rect(currentX, padding, size, size), distanceRT2, ScaleMode.ScaleToFit, false);
            }
        }
    }
}

public class AsyncCaptureCallback : MonoBehaviour
{
    public GlobalTextureSetter setter;
    private Camera cam;
    private bool oldFog;

    void OnEnable() 
    {
        cam = GetComponent<Camera>();
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }
    
    void OnDisable()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    void OnPreRender()
    {
        oldFog = RenderSettings.fog;
        RenderSettings.fog = false;
    }

    void OnPostRender() 
    {
        RenderSettings.fog = oldFog;
        cam.enabled = false;
        if (setter != null) setter.OnCaptureComplete();
    }

    void OnBeginCameraRendering(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera) 
    {
        if (camera == cam)
        {
            oldFog = RenderSettings.fog;
            RenderSettings.fog = false;
        }
    }

    void OnEndCameraRendering(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera) 
    {
        if (camera == cam)
        {
            RenderSettings.fog = oldFog;
            cam.enabled = false;
            if (setter != null) setter.OnCaptureComplete();
        }
    }
}
