using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using static Unity.Burst.Intrinsics.X86.Avx;

[ExecuteAlways]
public class InfiniteGrassRenderer : MonoBehaviour
{
    [HideInInspector] public static InfiniteGrassRenderer instance;//Global ref of the script

    [Header("Internal")]
    public Material grassMaterial;
    public ComputeBuffer argsBuffer;
    public ComputeBuffer tBuffer;//Just a temp buffer to preview the visible grass count

    [Header("Grass Properties")]
    public float spacing = 0.5f;//Spacing between blades, Please don't make it too low
    public float drawDistance = 300;
    public float fullDensityDistance = 50;//After this distance, we start removing some blades of grass in sake of performance
    public int grassMeshSubdivision = 5;//How many sections you will have in your grass blade mesh, 0 will give a triangle, having more sections will make the wind animation and the curvature looks better
    public float textureUpdateThreshold = 10.0f;//The distance that the camera should move before we update the "Data Textures"
    [Tooltip("Safety margin outside the screen bounds to prevent grass from popping at the edges (e.g. 0.1 is a 10% extra margin).")]
    public float frustumBuffer = 0.2f;

    [Header("Max Buffer Count (Millions)")]
    public float maxBufferCount = 2;//The number we gonna use to initialize the positions buffer
    //Don't make it too high cause that gonna impact performance, usually 2 - 3 should be enough unless you are using a crazy spacing
    //Also don't make it too low cause it's gonna negativly impact the performance

    [Header("Debug (Enabling this will make the performance drop a lot)")]
    public bool previewVisibleGrassCount = false;

    [Header("Built-in Prepass Fields")]
    public LayerMask heightMapLayer;
    public Material heightMapMat;
    public ComputeShader computeShader;

    [Header("Built-in Prepass Optimization")]
    public bool cacheHeightmap = true;
    public bool enableMaskPass = false;
    public bool enableColorPass = false;
    public bool enableSlopePass = false;

    private Vector2 lastCenterPos = new Vector2(float.NaN, float.NaN);

    private Mesh cachedGrassMesh;
    private ComputeBuffer grassPositionsBuffer;

    private RenderTexture heightRT;
    private RenderTexture maskRT;
    private RenderTexture colorRT;
    private RenderTexture slopeRT;

    private Camera prepassCamera;

    private void OnEnable()
    {
        instance = this;
    }

    private void OnDisable()
    {
        instance = null;

        argsBuffer?.Release();
        tBuffer?.Release();
        grassPositionsBuffer?.Release();

        ReleaseRenderTextures();

        if (prepassCamera != null)
        {
            DestroyImmediate(prepassCamera.gameObject);
            prepassCamera = null;
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

        Bounds cameraBounds = CalculateCameraBounds(Camera.main);
        Vector2 centerPos = new Vector2(Mathf.Floor(Camera.main.transform.position.x / textureUpdateThreshold) * textureUpdateThreshold, Mathf.Floor(Camera.main.transform.position.z / textureUpdateThreshold) * textureUpdateThreshold);
        
        //Args Buffer ---------------------------------------------------------------------------------
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        tBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);

        uint[] args = new uint[5];
        args[0] = (uint)GetGrassMeshCache().GetIndexCount(0);
        args[1] = 0; // Overwritten by CopyCount inside UpdateGrassData
        args[2] = (uint)GetGrassMeshCache().GetIndexStart(0);
        args[3] = (uint)GetGrassMeshCache().GetBaseVertex(0);
        args[4] = 0;
        argsBuffer.SetData(args);

        // Perform the top-down prepass rendering and dispatch the compute shader
        UpdateGrassData();

        //Material Setup ------------------------------------------------------------
        grassMaterial.SetVector("_CenterPos", centerPos);
        grassMaterial.SetFloat("_DrawDistance", drawDistance);
        grassMaterial.SetFloat("_TextureUpdateThreshold", textureUpdateThreshold);

        //Big Draw Call -------------------------------------------------------------
        Graphics.DrawMeshInstancedIndirect(GetGrassMeshCache(), 0, grassMaterial, cameraBounds, argsBuffer);
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

        if (enableMaskPass && (maskRT == null || maskRT.width != textureSize))
        {
            if (maskRT != null) maskRT.Release();
            maskRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.RFloat);
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

    Camera GetPrepassCamera()
    {
        if (prepassCamera == null)
        {
            GameObject camGO = GameObject.Find("GrassDataCamera");
            if (camGO == null)
            {
                camGO = new GameObject("GrassDataCamera");
                camGO.hideFlags = HideFlags.HideAndDontSave;
            }
            prepassCamera = camGO.GetComponent<Camera>();
            if (prepassCamera == null)
            {
                prepassCamera = camGO.AddComponent<Camera>();
            }

            prepassCamera.enabled = false;
            prepassCamera.clearFlags = CameraClearFlags.Color;
            prepassCamera.backgroundColor = new Color(0, 0, 0, 0);
            prepassCamera.orthographic = true;
            prepassCamera.allowMSAA = false;
            prepassCamera.allowHDR = false;
        }
        return prepassCamera;
    }

    void UpdateGrassData()
    {
        if (heightMapMat == null || computeShader == null)
            return;

        AllocateRenderTextures();
        Camera cam = GetPrepassCamera();

        Bounds cameraBounds = CalculateCameraBounds(Camera.main);
        Vector2 centerPos = new Vector2(
            Mathf.Floor(Camera.main.transform.position.x / textureUpdateThreshold) * textureUpdateThreshold, 
            Mathf.Floor(Camera.main.transform.position.z / textureUpdateThreshold) * textureUpdateThreshold
        );

        // Position camera above the area looking straight down
        cam.transform.position = new Vector3(centerPos.x, cameraBounds.max.y, centerPos.y);
        cam.transform.rotation = Quaternion.LookRotation(-Vector3.up);

        // Set orthographic bounds
        cam.orthographicSize = drawDistance + textureUpdateThreshold;
        cam.nearClipPlane = 0;
        cam.farClipPlane = cameraBounds.size.y;

        // Render Prepass 1: Heightmap & Terrain elevation (Cached based on position shift)
        bool positionShifted = (centerPos != lastCenterPos);
        bool forceRender = !cacheHeightmap || positionShifted || heightRT == null || !heightRT.IsCreated();
        
        if (forceRender)
        {
            cam.cullingMask = heightMapLayer;
            cam.targetTexture = heightRT;
            Shader.SetGlobalVector("_BoundsYMinMax", new Vector4(cameraBounds.min.y, cameraBounds.max.y, 0, 0));
            
            Shader heightShader = Shader.Find("InfiniteGrass/GrassHeightMapShader");
            if (heightShader != null)
            {
                cam.RenderWithShader(heightShader, "");
            }
            else
            {
                cam.Render();
            }

            lastCenterPos = centerPos;
        }

        // Render Prepass 2: Mask Map (Density map for trails/decal modifiers)
        if (enableMaskPass)
        {
            cam.targetTexture = maskRT;
            cam.cullingMask = -1; // Render everything in that volume that has the GrassMask LightMode
            Shader maskShader = Shader.Find("InfiniteGrass/Modifiers/GrassMaskShader");
            if (maskShader != null)
            {
                cam.RenderWithShader(maskShader, "LightMode");
            }
        }

        // Render Prepass 3: Color Map (Burns, colors, etc.)
        if (enableColorPass)
        {
            cam.targetTexture = colorRT;
            Shader coloringShader = Shader.Find("InfiniteGrass/Modifiers/GrassColoringShader");
            if (coloringShader != null)
            {
                cam.RenderWithShader(coloringShader, "LightMode");
            }
        }

        // Render Prepass 4: Slope Map (Collision paths, bends, etc.)
        if (enableSlopePass)
        {
            cam.targetTexture = slopeRT;
            Shader slopeShader = Shader.Find("InfiniteGrass/Modifiers/GrassSlopeShader");
            if (slopeShader != null)
            {
                cam.RenderWithShader(slopeShader, "LightMode");
            }
        }

        // Global textures for the grass blade shader
        Shader.SetGlobalTexture("_GrassColorRT", enableColorPass ? colorRT : Texture2D.blackTexture);
        Shader.SetGlobalTexture("_GrassSlopeRT", enableSlopePass ? slopeRT : Texture2D.blackTexture);

        // Compute grass positions buffer
        Vector2Int gridSize = new Vector2Int(Mathf.CeilToInt(cameraBounds.size.x / spacing), Mathf.CeilToInt(cameraBounds.size.z / spacing));
        Vector2Int gridStartIndex = new Vector2Int(Mathf.FloorToInt(cameraBounds.min.x / spacing), Mathf.FloorToInt(cameraBounds.min.z / spacing));

        grassPositionsBuffer?.Release();
        grassPositionsBuffer = new ComputeBuffer((int)(1000000 * maxBufferCount), sizeof(float) * 3, ComputeBufferType.Append);

        computeShader.SetMatrix("_VPMatrix", Camera.main.projectionMatrix * Camera.main.worldToCameraMatrix);
        computeShader.SetFloat("_FullDensityDistance", fullDensityDistance);
        computeShader.SetVector("_BoundsMin", cameraBounds.min);
        computeShader.SetVector("_BoundsMax", cameraBounds.max);
        computeShader.SetVector("_CameraPosition", Camera.main.transform.position);
        computeShader.SetVector("_CenterPos", centerPos);
        computeShader.SetFloat("_DrawDistance", drawDistance);
        computeShader.SetFloat("_TextureUpdateThreshold", textureUpdateThreshold);
        computeShader.SetFloat("_Spacing", spacing);
        computeShader.SetFloat("_FrustumBuffer", frustumBuffer);
        computeShader.SetVector("_GridStartIndex", (Vector2)gridStartIndex);
        computeShader.SetVector("_GridSize", (Vector2)gridSize);
        computeShader.SetBuffer(0, "_GrassPositions", grassPositionsBuffer);
        computeShader.SetTexture(0, "_GrassHeightMapRT", heightRT);
        computeShader.SetTexture(0, "_GrassMaskMapRT", enableMaskPass ? maskRT : Texture2D.blackTexture);

        grassPositionsBuffer.SetCounterValue(0);
        

        // Dispatch compute shader
        int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / 8);
        int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / 8);
        computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        // Set global buffer for the grass blade shader
        Shader.SetGlobalBuffer("_GrassPositions", grassPositionsBuffer);

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
    }

    int oldSubdivision = -1;
    public Mesh GetGrassMeshCache() //Code to generate the grass blade mesh based on the subdivision value
    {
        if (!cachedGrassMesh || oldSubdivision != grassMeshSubdivision)//Dont update unless its necessary
        {
            cachedGrassMesh = new Mesh();

            Vector3[] vertices = new Vector3[3 + 4 * grassMeshSubdivision];//Total number of vertices
            int[] triangles = new int[(1 + 2 * grassMeshSubdivision) * 3];//(Total number of faces) * 3

            for (int i = 0; i < grassMeshSubdivision; i++)
            {
                float y1 = (float)i / (grassMeshSubdivision + 1);
                float y2 = (float)(i + 1) / (grassMeshSubdivision + 1);

                Vector3 bottomLeft = new Vector3(-0.25f, y1);
                Vector3 bottomRight = new Vector3(0.25f, y1);
                Vector3 topLeft = new Vector3(-0.25f, y2);
                Vector3 topRight = new Vector3(0.25f, y2);

                int bottomLeftIndex = i * 4;
                int bottomRightIndex = i * 4 + 1;
                int topLeftIndex = i * 4 + 2;
                int topRightIndex = i * 4 + 3;

                vertices[bottomLeftIndex] = bottomLeft;
                vertices[bottomRightIndex] = bottomRight;
                vertices[topLeftIndex] = topLeft;
                vertices[topRightIndex] = topRight;

                //First Face
                triangles[i * 6] = bottomLeftIndex;
                triangles[i * 6 + 1] = topRightIndex;
                triangles[i * 6 + 2] = bottomRightIndex;
                //Second Face
                triangles[i * 6 + 3] = bottomLeftIndex;
                triangles[i * 6 + 4] = topLeftIndex;
                triangles[i * 6 + 5] = topRightIndex;
            }

            //Finally the last triangle on top
            vertices[grassMeshSubdivision * 4] = new Vector3(-0.25f, (float)grassMeshSubdivision / (grassMeshSubdivision + 1));
            vertices[grassMeshSubdivision * 4 + 1] = new Vector3(0, 1);
            vertices[grassMeshSubdivision * 4 + 2] = new Vector3(0.25f, (float)grassMeshSubdivision / (grassMeshSubdivision + 1));

            triangles[grassMeshSubdivision * 6] = grassMeshSubdivision * 4;
            triangles[grassMeshSubdivision * 6 + 1] = grassMeshSubdivision * 4 + 1;
            triangles[grassMeshSubdivision * 6 + 2] = grassMeshSubdivision * 4 + 2;

            cachedGrassMesh.SetVertices(vertices);
            cachedGrassMesh.SetTriangles(triangles, 0);

            oldSubdivision = grassMeshSubdivision;
        }
        
        return cachedGrassMesh;
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

}
