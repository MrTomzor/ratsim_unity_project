using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class InfiniteGrassAssetRenderer : MonoBehaviour
{
    [Header("Internal")]
    public Material grassMaterial;
    public ComputeBuffer argsBuffer;
    public ComputeBuffer tBuffer;//Just a temp buffer to preview the visible grass count

    [Header("Grass Mesh")]
    [Tooltip("Assign a custom grass mesh here. Pivot should be at the base (ground level).")]
    public Mesh grassMesh;

    [Header("Texture Array")]
    [Tooltip("Assign all variations of grass textures here. They must have the same width, height, and format.")]
    public Texture2D[] grassTextures;
    [Tooltip("Optional relative weights for each texture. If empty or size mismatch, all textures have equal weight.")]
    public float[] textureWeights;
    private Texture2DArray grassTextureArray;
    private float[] matchedCumulativeWeights = new float[32];
    private int lastTextureHash = 0;

    [Header("Grass Properties")]
    public bool castShadows = true;
    public float spacing = 0.5f;//Spacing between blades, Please don't make it too low
    public float drawDistance = 300;
    public float fullDensityDistance = 50;//After this distance, we start removing some blades of grass in sake of performance
    public float textureUpdateThreshold = 10.0f;//The distance that the camera should move before we update the "Data Textures"
    [Tooltip("Safety margin outside the screen bounds to prevent grass from popping at the edges (e.g. 0.1 is a 10% extra margin).")]
    public float frustumBuffer = 0.2f;

    [Header("Height Fading")]
    public float heightFadeStart = 100f;
    public float heightFadeEnd = 120f;

    [Header("Max Buffer Count (Millions)")]
    public float maxBufferCount = 2;//The number we gonna use to initialize the positions buffer
    //Don't make it too high cause that gonna impact performance, usually 2 - 3 should be enough unless you are using a crazy spacing
    //Also don't make it too low cause it's gonna negativly impact the performance

    [Header("Debug (Enabling this will make the performance drop a lot)")]
    public bool previewVisibleGrassCount = false;
    public bool previewMaskTexture = false;
    public bool overrideMaskWithNoise = false;

    [Header("Built-in Prepass Fields")]
    public LayerMask heightMapLayer;
    public LayerMask maskMapLayer;

    public Material heightMapMat;
    public ComputeShader computeShader;

    [Header("Built-in Prepass Optimization")]
    public bool cacheHeightmap = true;
    public bool enableMaskPass = false;
    public bool enableColorPass = false;
    public bool enableSlopePass = false;

    private Vector2 lastCenterPos = new Vector2(float.NaN, float.NaN);
    private Vector2 activeCenterPos = new Vector2(float.NaN, float.NaN);
    private readonly Vector2 fixedHeightBounds = new Vector2(-1000f, 1000f);

    private ComputeBuffer grassPositionsBuffer;
    private MaterialPropertyBlock propertyBlock;
    private Material shadowMaterial;

    private RenderTexture heightRT;
    private RenderTexture maskRT;
    private RenderTexture colorRT;
    private RenderTexture slopeRT;
    private Texture2D noiseTexture;
    private Texture activeMaskTexture;
    
    private CommandBuffer shadowGrabCommandBuffer;
    private Light mainDirectionalLight;

    private Camera heightCamera;
    private Camera maskCamera;
    private Camera colorCamera;
    private Camera slopeCamera;

    private void OnEnable()
    {
    }

    private void OnDisable()
    {
        argsBuffer?.Release();
        tBuffer?.Release();
        grassPositionsBuffer?.Release();

        ReleaseRenderTextures();
        if (noiseTexture != null)
        {
            DestroyImmediate(noiseTexture);
            noiseTexture = null;
        }
        activeMaskTexture = null;

        if (grassTextureArray != null)
        {
            if (Application.isPlaying) Destroy(grassTextureArray);
            else DestroyImmediate(grassTextureArray);
            grassTextureArray = null;
        }

        if (heightCamera != null) { DestroyImmediate(heightCamera.gameObject); heightCamera = null; }
        if (maskCamera != null) { DestroyImmediate(maskCamera.gameObject); maskCamera = null; }
        if (colorCamera != null) { DestroyImmediate(colorCamera.gameObject); colorCamera = null; }
        if (slopeCamera != null) { DestroyImmediate(slopeCamera.gameObject); slopeCamera = null; }

        if (shadowMaterial != null)
        {
            if (Application.isPlaying) Destroy(shadowMaterial);
            else DestroyImmediate(shadowMaterial);
            shadowMaterial = null;
        }

        if (mainDirectionalLight != null && shadowGrabCommandBuffer != null)
        {
            mainDirectionalLight.RemoveCommandBuffer(LightEvent.AfterShadowMap, shadowGrabCommandBuffer);
            shadowGrabCommandBuffer.Release();
            shadowGrabCommandBuffer = null;
        }
    }

    private void OnDestroy()
    {
        OnDisable();
    }

    void LateUpdate()
    {
        argsBuffer?.Release();
        tBuffer?.Release();

        if (spacing == 0 || grassMaterial == null) return;
        if (Camera.main == null) return;
        if (grassMesh == null) return;

        Bounds cameraBounds = CalculateCameraBounds(Camera.main);
        Vector2 centerPos = new Vector2(Mathf.Floor(Camera.main.transform.position.x / textureUpdateThreshold) * textureUpdateThreshold, Mathf.Floor(Camera.main.transform.position.z / textureUpdateThreshold) * textureUpdateThreshold);
        
        //Args Buffer ---------------------------------------------------------------------------------
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        tBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);

        uint[] args = new uint[5];
        args[0] = (uint)grassMesh.GetIndexCount(0);
        args[1] = 0; // Overwritten by CopyCount inside UpdateGrassData
        args[2] = (uint)grassMesh.GetIndexStart(0);
        args[3] = (uint)grassMesh.GetBaseVertex(0);
        args[4] = 0;
        argsBuffer.SetData(args);

        // Perform the top-down prepass rendering and dispatch the compute shader
        UpdateGrassData();

        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

        // Pass mesh height to the shader so it can normalize vertex.y for wind/AO
        float meshHeight = grassMesh.bounds.max.y;
        if (meshHeight <= 0) meshHeight = 1.0f; // Safety fallback
        propertyBlock.SetFloat("_MeshHeight", meshHeight);

        // Texture Array Setup ------------------------------------------------------
        if (TexturesChanged())
        {
            GenerateTextureArray();
        }

        if (grassTextureArray != null)
        {
            propertyBlock.SetTexture("_BaseColorTextureArray", grassTextureArray);
            propertyBlock.SetFloat("_TextureCount", grassTextureArray.depth);
            propertyBlock.SetFloatArray("_CumulativeTextureWeights", matchedCumulativeWeights);
        }
        else
        {
            propertyBlock.SetFloat("_TextureCount", 0);
        }

        // Grab Shadow Map for Option A receiving
        if (mainDirectionalLight == null || !mainDirectionalLight.isActiveAndEnabled)
        {
            foreach (Light l in FindObjectsOfType<Light>())
            {
                if (l.type == LightType.Directional && l.shadows != LightShadows.None && l.isActiveAndEnabled)
                {
                    mainDirectionalLight = l;
                    break;
                }
            }
        }
        
        if (mainDirectionalLight != null && shadowGrabCommandBuffer == null)
        {
            shadowGrabCommandBuffer = new UnityEngine.Rendering.CommandBuffer();
            shadowGrabCommandBuffer.name = "Grab Directional Shadow Map";
            shadowGrabCommandBuffer.SetGlobalTexture("_GlobalShadowMap", new UnityEngine.Rendering.RenderTargetIdentifier(UnityEngine.Rendering.BuiltinRenderTextureType.CurrentActive));
            mainDirectionalLight.AddCommandBuffer(UnityEngine.Rendering.LightEvent.AfterShadowMap, shadowGrabCommandBuffer);
        }

        //Material Setup ------------------------------------------------------------
        propertyBlock.SetVector("_CenterPos", float.IsNaN(activeCenterPos.x) ? centerPos : activeCenterPos);
        propertyBlock.SetFloat("_DrawDistance", drawDistance);
        propertyBlock.SetFloat("_TextureUpdateThreshold", textureUpdateThreshold);

        propertyBlock.SetTexture("_GrassColorRT", enableColorPass && colorRT != null ? colorRT : Texture2D.blackTexture);
        propertyBlock.SetTexture("_GrassSlopeRT", enableSlopePass && slopeRT != null ? slopeRT : Texture2D.blackTexture);
        if (grassPositionsBuffer != null) propertyBlock.SetBuffer("_GrassPositions", grassPositionsBuffer);

        //Big Draw Call -------------------------------------------------------------
        Graphics.DrawMeshInstancedIndirect(grassMesh, 0, grassMaterial, cameraBounds, argsBuffer, 0, propertyBlock, ShadowCastingMode.Off, true);

        //Shadow-Only Draw Call -----------------------------------------------------
        if (castShadows)
        {
            if (shadowMaterial == null)
            {
                Shader shadowShader = Shader.Find("InfiniteGrassAsset/GrassShadowCaster");
                if (shadowShader != null)
                    shadowMaterial = new Material(shadowShader);
            }
            if (shadowMaterial != null)
            {
                // Sync properties from grassMaterial so shadow geometry matches visible geometry
                shadowMaterial.SetFloat("_GrassScale", grassMaterial.GetFloat("_GrassScale"));
                shadowMaterial.SetFloat("_GrassScaleRandomness", grassMaterial.GetFloat("_GrassScaleRandomness"));
                shadowMaterial.SetFloat("_DistanceScaleMultiplier", grassMaterial.GetFloat("_DistanceScaleMultiplier"));
                shadowMaterial.SetFloat("_WindStrength", grassMaterial.GetFloat("_WindStrength"));
                shadowMaterial.SetVector("_WindScroll", grassMaterial.GetVector("_WindScroll"));
                shadowMaterial.SetFloat("_AlphaCutoff", grassMaterial.GetFloat("_AlphaCutoff"));
                shadowMaterial.SetFloat("_Cull", grassMaterial.GetFloat("_Cull"));
                if (grassMaterial.HasProperty("_WindTexture"))
                    shadowMaterial.SetTexture("_WindTexture", grassMaterial.GetTexture("_WindTexture"));

                // Pass main camera position so distance-based scaling matches the visible pass
                propertyBlock.SetVector("_MainCameraPosition", Camera.main.transform.position);

                Graphics.DrawMeshInstancedIndirect(grassMesh, 0, shadowMaterial, cameraBounds, argsBuffer, 0, propertyBlock, ShadowCastingMode.ShadowsOnly, false);
            }
        }
    }

    void CreateNoiseTexture()
    {
        if (noiseTexture != null) return;
        
        int size = 512;
        noiseTexture = new Texture2D(size, size, TextureFormat.R8, false);
        noiseTexture.filterMode = FilterMode.Bilinear;
        noiseTexture.wrapMode = TextureWrapMode.Repeat;
        
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            float val = UnityEngine.Random.value > 0.5 ? 1f : 0f;
            pixels[i] = new Color(val, val, val, 1);
        }
        
        noiseTexture.SetPixels(pixels);
        noiseTexture.Apply();
    }

    void AllocateRenderTextures()
    {
        int textureSize = 2048;
        if (heightRT == null || heightRT.width != textureSize)
        {
            ReleaseRenderTextures();

            heightRT = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.RGFloat);
            heightRT.filterMode = FilterMode.Bilinear;
            heightRT.Create();
        }

        if ((enableMaskPass || previewMaskTexture) && (maskRT == null || maskRT.width != textureSize))
        {
            if (maskRT != null) maskRT.Release();
            maskRT = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.RFloat);
            maskRT.filterMode = FilterMode.Bilinear;
            maskRT.Create();
        }

        if (enableColorPass && (colorRT == null || colorRT.width != textureSize))
        {
            if (colorRT != null) colorRT.Release();
            colorRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGBFloat);
            colorRT.filterMode = FilterMode.Bilinear;
            colorRT.Create();
        }

        if (enableSlopePass && (slopeRT == null || slopeRT.width != textureSize))
        {
            if (slopeRT != null) slopeRT.Release();
            slopeRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGBFloat);
            slopeRT.filterMode = FilterMode.Bilinear;
            slopeRT.Create();
        }
    }

    void ReleaseRenderTextures()
    {
        if (heightRT != null) { heightRT.Release(); heightRT = null; }
        if (maskRT != null) { maskRT.Release(); maskRT = null; }
        if (colorRT != null) { colorRT.Release(); colorRT = null; }
        if (slopeRT != null) { slopeRT.Release(); slopeRT = null; }
    }

    Camera GetCamera(ref Camera camRef, string name)
    {
        string uniqueName = name + "_" + gameObject.GetInstanceID();
        if (camRef == null)
        {
            GameObject camGO = GameObject.Find(uniqueName);
            if (camGO == null)
            {
                camGO = new GameObject(uniqueName);
                camGO.hideFlags = HideFlags.HideAndDontSave;
            }
            camRef = camGO.GetComponent<Camera>();
            if (camRef == null)
            {
                camRef = camGO.AddComponent<Camera>();
            }

            camRef.enabled = false;
            camRef.clearFlags = CameraClearFlags.Color;
            camRef.backgroundColor = new Color(0, 0, 0, 0);
            camRef.orthographic = true;
            camRef.allowMSAA = false;
            camRef.allowHDR = false;
            
            if (camRef.GetComponent<DisableCameraAfterRender>() == null)
                camRef.gameObject.AddComponent<DisableCameraAfterRender>();
        }
        return camRef;
    }

    void UpdateGrassData()
    {
        if (heightMapMat == null || computeShader == null)
            return;

        AllocateRenderTextures();

        if (overrideMaskWithNoise)
        {
            CreateNoiseTexture();
            activeMaskTexture = noiseTexture;
        }
        else if (enableMaskPass || previewMaskTexture)
        {
            activeMaskTexture = maskRT;
        }
        else
        {
            activeMaskTexture = Texture2D.blackTexture;
        }

        Bounds cameraBounds = CalculateCameraBounds(Camera.main);
        Vector2 centerPos = new Vector2(
            Mathf.Floor(Camera.main.transform.position.x / textureUpdateThreshold) * textureUpdateThreshold, 
            Mathf.Floor(Camera.main.transform.position.z / textureUpdateThreshold) * textureUpdateThreshold
        );

        Vector3 camPos = new Vector3(centerPos.x, 1000f, centerPos.y);
        float orthoSize = drawDistance + textureUpdateThreshold;
        bool firstInit = float.IsNaN(lastCenterPos.x);

        if (heightCamera == null || !heightCamera.enabled)
        {
            if (!float.IsNaN(lastCenterPos.x)) activeCenterPos = lastCenterPos;
        }

        // Render Prepass 1: Heightmap & Terrain elevation (Cached based on position shift)
        bool positionShifted = (centerPos != lastCenterPos);
        bool forceRender = !cacheHeightmap || positionShifted || firstInit;
        
        if (forceRender)
        {
            Camera cam = GetCamera(ref heightCamera, "GrassHeightCamera");
            cam.transform.position = camPos;
            cam.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            cam.orthographicSize = orthoSize;
            cam.nearClipPlane = -0;
            cam.farClipPlane = 1500;
            cam.cullingMask = heightMapLayer;
            cam.targetTexture = heightRT;
            
            Shader.SetGlobalVector("_BoundsYMinMax", new Vector4(fixedHeightBounds.x, fixedHeightBounds.y, 0, 0));
            
            Shader heightShader = Shader.Find("InfiniteGrassAsset/GrassHeightMapShader");
            if (heightShader != null) cam.SetReplacementShader(heightShader, "");
            else cam.ResetReplacementShader();

            if (firstInit) {
                if (heightShader != null) cam.RenderWithShader(heightShader, "");
                else cam.Render();
                activeCenterPos = centerPos;
            } else {
                cam.enabled = true; // Non-blocking
            }

            lastCenterPos = centerPos;
        }

        // Render Prepass 2: Mask Map (Density map for trails/decal modifiers)
        if (!overrideMaskWithNoise && (enableMaskPass || previewMaskTexture))
        {
            Shader maskShader = Shader.Find("InfiniteGrassAsset/Modifiers/GrassMaskShader");
            if (maskShader != null)
            {
                Camera cam = GetCamera(ref maskCamera, "GrassMaskCamera");
                cam.transform.position = camPos;
                cam.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                cam.orthographicSize = orthoSize;
                cam.nearClipPlane = -0;
                cam.farClipPlane = 1500;
                cam.targetTexture = maskRT;
                cam.cullingMask = maskMapLayer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                
                cam.SetReplacementShader(maskShader, "");

                if (firstInit) {
                    cam.RenderWithShader(maskShader, "");
                } else {
                    cam.enabled = true; // Non-blocking
                }
            }
        }

        // Render Prepass 3: Color Map (Burns, colors, etc.)
        if (enableColorPass)
        {
            Shader coloringShader = Shader.Find("InfiniteGrassAsset/Modifiers/GrassColoringShader");
            if (coloringShader != null)
            {
                Camera cam = GetCamera(ref colorCamera, "GrassColorCamera");
                cam.transform.position = camPos;
                cam.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                cam.orthographicSize = orthoSize;
                cam.nearClipPlane = -1000;
                cam.farClipPlane = 1000;
                cam.targetTexture = colorRT;
                
                cam.SetReplacementShader(coloringShader, "LightMode");

                if (firstInit) {
                    cam.RenderWithShader(coloringShader, "LightMode");
                } else {
                    cam.enabled = true; // Non-blocking
                }
            }
        }

        // Render Prepass 4: Slope Map (Collision paths, bends, etc.)
        if (enableSlopePass)
        {
            Shader slopeShader = Shader.Find("InfiniteGrassAsset/Modifiers/GrassSlopeShader");
            if (slopeShader != null)
            {
                Camera cam = GetCamera(ref slopeCamera, "GrassSlopeCamera");
                cam.transform.position = camPos;
                cam.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                cam.orthographicSize = orthoSize;
                cam.nearClipPlane = -1000;
                cam.farClipPlane = 1000;
                cam.targetTexture = slopeRT;
                
                cam.SetReplacementShader(slopeShader, "LightMode");

                if (firstInit) {
                    cam.RenderWithShader(slopeShader, "LightMode");
                } else {
                    cam.enabled = true; // Non-blocking
                }
            }
        }

        // Compute grass positions buffer
        Vector2Int gridSize = new Vector2Int(Mathf.CeilToInt(cameraBounds.size.x / spacing), Mathf.CeilToInt(cameraBounds.size.z / spacing));
        Vector2Int gridStartIndex = new Vector2Int(Mathf.FloorToInt(cameraBounds.min.x / spacing), Mathf.FloorToInt(cameraBounds.min.z / spacing));

        grassPositionsBuffer?.Release();
        grassPositionsBuffer = new ComputeBuffer((int)(1000000 * maxBufferCount), sizeof(float) * 3, ComputeBufferType.Append);

        computeShader.SetMatrix("_VPMatrix", Camera.main.projectionMatrix * Camera.main.worldToCameraMatrix);
        computeShader.SetFloat("_FullDensityDistance", fullDensityDistance);
        computeShader.SetVector("_BoundsMin", new Vector3(cameraBounds.min.x, fixedHeightBounds.x, cameraBounds.min.z));
        computeShader.SetVector("_BoundsMax", new Vector3(cameraBounds.max.x, fixedHeightBounds.y, cameraBounds.max.z));
        computeShader.SetVector("_CameraPosition", Camera.main.transform.position);
        computeShader.SetVector("_CenterPos", float.IsNaN(activeCenterPos.x) ? centerPos : activeCenterPos);
        computeShader.SetFloat("_DrawDistance", drawDistance);
        computeShader.SetFloat("_TextureUpdateThreshold", textureUpdateThreshold);
        computeShader.SetFloat("_Spacing", spacing);
        computeShader.SetFloat("_HeightFadeStart", heightFadeStart);
        computeShader.SetFloat("_HeightFadeEnd", heightFadeEnd);
        computeShader.SetFloat("_FrustumBuffer", frustumBuffer);
        computeShader.SetVector("_GridStartIndex", (Vector2)gridStartIndex);
        computeShader.SetVector("_GridSize", (Vector2)gridSize);
        computeShader.SetBuffer(0, "_GrassPositions", grassPositionsBuffer);
        computeShader.SetTexture(0, "_GrassHeightMapRT", heightRT);
        computeShader.SetTexture(0, "_GrassMaskMapRT", activeMaskTexture != null ? activeMaskTexture : Texture2D.blackTexture);

        grassPositionsBuffer.SetCounterValue(0);
        

        // Dispatch compute shader
        int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / 8);
        int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / 8);
        computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        // Copy counter value to indirect arguments argsBuffer
        ComputeBuffer.CopyCount(grassPositionsBuffer, argsBuffer, sizeof(uint));

        if (previewVisibleGrassCount)
        {
            ComputeBuffer.CopyCount(grassPositionsBuffer, tBuffer, 0);
        }
    }

    private void OnGUI()
    {
        if (previewVisibleGrassCount)
        {
            if (Camera.main == null) return;
            GUI.contentColor = Color.black;
            GUIStyle style = new GUIStyle();
            style.fontSize = 25;

            uint[] count = new uint[1];
            tBuffer.GetData(count);//Reading back data from GPU

            //Recalculating the GridSize used for dispatching
            Bounds cameraBounds = CalculateCameraBounds(Camera.main);
            Vector2Int gridSize = new Vector2Int(Mathf.CeilToInt(cameraBounds.size.x / spacing), Mathf.CeilToInt(cameraBounds.size.z / spacing));

            GUI.Label(new Rect(50, 50, 400, 200), "Dispatch Size : " + gridSize.x + "x" + gridSize.y + " = " + (gridSize.x * gridSize.y), style);
            GUI.Label(new Rect(50, 80, 400, 200), "Visible Grass Count : " + count[0], style);

        }

        if (previewMaskTexture)
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.normal.textColor = Color.white;

            if (activeMaskTexture != null)
            {
                Rect rect = new Rect(Screen.width - 276, 20, 256, 256);
                GUI.DrawTexture(rect, activeMaskTexture, ScaleMode.ScaleToFit, false);
                GUI.Label(new Rect(Screen.width - 276, 280, 256, 25), overrideMaskWithNoise ? "Noise Texture Preview" : "maskRT Preview", labelStyle);
            }

            if (heightRT != null)
            {
                Rect rect = new Rect(Screen.width - 552, 20, 256, 256);
                GUI.DrawTexture(rect, heightRT, ScaleMode.ScaleToFit, false);
                GUI.Label(new Rect(Screen.width - 552, 280, 256, 25), "heightRT Preview", labelStyle);
            }

            if (heightCamera != null)
            {
                GUI.Label(new Rect(Screen.width - 276, 310, 256, 20), $"Cam Pos: {heightCamera.transform.position}", labelStyle);
                GUI.Label(new Rect(Screen.width - 276, 330, 256, 20), $"Ortho Size: {heightCamera.orthographicSize}", labelStyle);
                GUI.Label(new Rect(Screen.width - 276, 350, 256, 20), $"Near/Far: {heightCamera.nearClipPlane} / {heightCamera.farClipPlane}", labelStyle);
            }
        }
    }

    Bounds CalculateCameraBounds(Camera camera)
    {
        Vector3 ntopLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, camera.nearClipPlane));
        Vector3 ntopRight = camera.ViewportToWorldPoint(new Vector3(1, 1, camera.nearClipPlane));
        Vector3 nbottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, camera.nearClipPlane));
        Vector3 nbottomRight = camera.ViewportToWorldPoint(new Vector3(1, 0, camera.nearClipPlane));

        Vector3 ftopLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, drawDistance));
        Vector3 ftopRight = camera.ViewportToWorldPoint(new Vector3(1, 1, drawDistance));
        Vector3 fbottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, drawDistance));
        Vector3 fbottomRight = camera.ViewportToWorldPoint(new Vector3(1, 0, drawDistance));

        float[] xValues = new float[] { ftopLeft.x, ftopRight.x, ntopLeft.x, ntopRight.x, fbottomLeft.x, fbottomRight.x, nbottomLeft.x, nbottomRight.x };
        float startX = xValues.Max();
        float endX = xValues.Min();

        float[] yValues = new float[] { ftopLeft.y, ftopRight.y, ntopLeft.y, ntopRight.y, fbottomLeft.y, fbottomRight.y, nbottomLeft.y, nbottomRight.y };
        float startY = yValues.Max();
        float endY = yValues.Min();

        float[] zValues = new float[] { ftopLeft.z, ftopRight.z, ntopLeft.z, ntopRight.z, fbottomLeft.z, fbottomRight.z, nbottomLeft.z, nbottomRight.z };
        float startZ = zValues.Max();
        float endZ = zValues.Min();

        Vector3 center = new Vector3((startX + endX) / 2, (startY + endY) / 2, (startZ + endZ) / 2);
        Vector3 size = new Vector3(Mathf.Abs(startX - endX), Mathf.Abs(startY - endY), Mathf.Abs(startZ - endZ));

        Bounds bounds = new Bounds(center, size);
        bounds.Expand(1);
        return bounds;
    }

    bool TexturesChanged()
    {
        int hash = 17;
        int nonNullCount = 0;
        if (grassTextures != null)
        {
            foreach (var tex in grassTextures)
            {
                if (tex != null)
                {
                    hash = hash * 31 + tex.GetHashCode();
                    nonNullCount++;
                }
            }
        }
        if (textureWeights != null)
        {
            foreach (var weight in textureWeights)
            {
                hash = hash * 31 + weight.GetHashCode();
            }
        }

        // Force rebuild if we have textures but the array is missing
        bool arrayIsMissing = (nonNullCount > 0 && grassTextureArray == null);

        if (hash != lastTextureHash || arrayIsMissing)
        {
            lastTextureHash = hash;
            return true;
        }
        return false;
    }

    void GenerateTextureArray()
    {
        if (grassTextureArray != null)
        {
            if (Application.isPlaying) Destroy(grassTextureArray);
            else DestroyImmediate(grassTextureArray);
            grassTextureArray = null;
        }

        if (grassTextures == null || grassTextures.Length == 0) return;

        var textures = System.Array.FindAll(grassTextures, t => t != null);
        if (textures.Length == 0) return;

        int width = textures[0].width;
        int height = textures[0].height;
        TextureFormat format = textures[0].format;
        bool mipChain = textures[0].mipmapCount > 1;

        // Filter to only matching textures to prevent copy crashes
        var matchingTextures = System.Array.FindAll(textures, t => t.width == width && t.height == height && t.format == format);
        if (matchingTextures.Length == 0) return;

        grassTextureArray = new Texture2DArray(width, height, matchingTextures.Length, format, mipChain);
        grassTextureArray.wrapMode = TextureWrapMode.Repeat;
        grassTextureArray.filterMode = FilterMode.Bilinear;

        float totalWeight = 0;
        System.Collections.Generic.List<float> validWeights = new System.Collections.Generic.List<float>();

        for (int i = 0; i < grassTextures.Length; i++)
        {
            Texture2D tex = grassTextures[i];
            if (tex != null && tex.width == width && tex.height == height && tex.format == format)
            {
                float w = (textureWeights != null && i < textureWeights.Length) ? textureWeights[i] : 1.0f;
                validWeights.Add(w);
                totalWeight += w;
            }
        }

        if (totalWeight <= 0) totalWeight = 1;

        float currentSum = 0;
        for (int i = 0; i < 32; i++)
        {
            if (i < validWeights.Count)
            {
                currentSum += validWeights[i];
                matchedCumulativeWeights[i] = currentSum / totalWeight;
            }
            else
            {
                matchedCumulativeWeights[i] = 1.0f;
            }
        }

        // ensure the last one is 1.0 just in case of precision issues
        if (validWeights.Count > 0 && validWeights.Count <= 32)
        {
            matchedCumulativeWeights[validWeights.Count - 1] = 1.0f;
        }

        for (int i = 0; i < matchingTextures.Length; i++)
        {
            for (int mip = 0; mip < matchingTextures[i].mipmapCount; mip++)
            {
                Graphics.CopyTexture(matchingTextures[i], 0, mip, grassTextureArray, i, mip);
            }
        }
    }

}

public class DisableCameraAfterRender : MonoBehaviour
{
    private Camera cam;
    void OnEnable() 
    {
        cam = GetComponent<Camera>();
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }
    void OnDisable()
    {
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }
    void OnPostRender() 
    {
        cam.enabled = false;
    }
    void OnEndCameraRendering(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera) 
    {
        if (camera == cam) cam.enabled = false;
    }
}
