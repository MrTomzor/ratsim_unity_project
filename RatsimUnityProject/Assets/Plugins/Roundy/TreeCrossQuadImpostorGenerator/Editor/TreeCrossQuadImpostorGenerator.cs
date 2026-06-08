using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System;


public class TreeCrossQuadImpostorGenerator : EditorWindow
{
    private GameObject sourceTree;
    private int textureSize = 512;
    private readonly int[] textureSizeOptions = { 128, 256, 512, 1024, 2048 };
    private int selectedTextureSizeIndex = 2; // Default to 512
    private bool createLOD = true;
    private float lodTransitionHeight = 0.3f;
    private Color backgroundColor = new Color(0, 0, 0, 0);

    private const int BakingLayer = 31;

    private Dictionary<GameObject, int> originalLayers = new Dictionary<GameObject, int>();

    // Offset controls for each face
    private float frontQuadOffsetX = 0f;
    private float frontQuadOffsetZ = 0f;
    private float backQuadOffsetX = 0f;
    private float backQuadOffsetZ = 0f;
    private float rightQuadOffsetX = 0f;
    private float rightQuadOffsetZ = 0f;
    private float leftQuadOffsetX = 0f;
    private float leftQuadOffsetZ = 0f;

    // v2 Mode additions
    private bool isV2Mode = false;
    private float plane3FrontOffsetX = 0f;
    private float plane3FrontOffsetZ = 0f;
    private float plane3BackOffsetX = 0f;
    private float plane3BackOffsetZ = 0f;
    private float horizontalOffsetX = 0f;
    private float horizontalOffsetY = 0f;
    private float horizontalOffsetZ = 0f;

    // Preview system
    private bool isInPreviewMode = false;
    private GameObject previewContainer;
    private Material previewMaterial;
    private GameObject[] previewQuads = null;
    private float previewOffset = 10f; // Default offset
    private bool showSideBySide = true;

    [MenuItem("Tools/Roundy/Tree Cross Quad Impostor Generator")]
    public static void ShowWindow()
    {
        GetWindow<TreeCrossQuadImpostorGenerator>("Tree Cross Quad Impostor Generator");
    }

    private enum RenderPipelineType
    {
        BuiltIn,
        URP
    }

    private RenderPipelineType selectedPipeline = RenderPipelineType.BuiltIn;
    private readonly GUIContent pipelineLabel = new("Render Pipeline", "Select the render pipeline used in your project");
    private Vector2 scrollPosition;
    private GUIStyle headerStyle;
    private GUIStyle sectionStyle;
    private Color headerColor = new(0.8f, 0.8f, 0.8f, 1f);

    private void InitializeStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 10, 10)
            };
        }

        if (sectionStyle == null)
        {
            sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(0, 0, 8, 4)
            };
        }
    }


    private void OnGUI()
    {
        InitializeStyles();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        // Header Section
        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("Tree Cross Quad Impostor Generator v0.1", headerStyle);
            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.Space(10);

        // Main Settings Section
        DrawSection("Main Settings", () =>
        {
            selectedPipeline = (RenderPipelineType)EditorGUILayout.EnumPopup(pipelineLabel, selectedPipeline);

            sourceTree = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Source Tree", "The tree prefab to create an impostor for"),
                sourceTree, typeof(GameObject), true);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Atlas Texture Size", "Size of the output texture atlas"));
            selectedTextureSizeIndex = EditorGUILayout.Popup(selectedTextureSizeIndex,
                System.Array.ConvertAll(textureSizeOptions, x => x.ToString()));

            EditorGUILayout.EndHorizontal();
        });
        DrawSection("Preview Settings", () =>
        {
            showSideBySide = EditorGUILayout.Toggle(
                new GUIContent("Side by Side View", "Show source tree and impostor side by side"),
                showSideBySide);

            if (showSideBySide)
            {
                previewOffset = EditorGUILayout.Slider(
                    new GUIContent("Preview Offset", "Distance between source tree and impostor"),
                    previewOffset, 0f, 10f);
            }
        });

        // Quad Adjustments Section
        DrawSection("Quad Adjustments", () =>
        {
            DrawQuadAdjustments("Plane 1 Front Face (0°)", ref frontQuadOffsetX, ref frontQuadOffsetZ);
            DrawQuadAdjustments("Plane 1 Back Face (180°)", ref backQuadOffsetX, ref backQuadOffsetZ);
            
            DrawQuadAdjustments("Plane 2 Front Face (120°)", ref rightQuadOffsetX, ref rightQuadOffsetZ);
            DrawQuadAdjustments("Plane 2 Back Face (300°)", ref leftQuadOffsetX, ref leftQuadOffsetZ);

            if (isInPreviewMode ? isV2Mode : true)
            {
                DrawQuadAdjustments("Plane 3 Front Face (240°)", ref plane3FrontOffsetX, ref plane3FrontOffsetZ);
                DrawQuadAdjustments("Plane 3 Back Face (60°)", ref plane3BackOffsetX, ref plane3BackOffsetZ);

                EditorGUILayout.LabelField("Horizontal Plane", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                horizontalOffsetX = EditorGUILayout.Slider("X Offset", horizontalOffsetX, -1f, 1f);
                horizontalOffsetY = EditorGUILayout.Slider("Y Offset", horizontalOffsetY, -1f, 1f);
                horizontalOffsetZ = EditorGUILayout.Slider("Z Offset", horizontalOffsetZ, -1f, 1f);
                EditorGUI.indentLevel--;
            }
        });

        // LOD Settings Section
        DrawSection("LOD Settings", () =>
        {
            createLOD = EditorGUILayout.Toggle(
                new GUIContent("Create LOD", "Creates LOD setup with original tree and impostor"),
                createLOD);

            if (createLOD)
            {
                EditorGUI.indentLevel++;
                lodTransitionHeight = EditorGUILayout.Slider(
                    new GUIContent("LOD Transition Height", "Screen height % when to switch to impostor"),
                    lodTransitionHeight, 0.1f, 0.5f);
                EditorGUI.indentLevel--;
            }
        });

        // Action Buttons Section
        EditorGUILayout.Space(20);
        DrawActionButtons();

        EditorGUILayout.EndScrollView();

        if (GUI.changed && isInPreviewMode)
        {
            UpdatePreviewQuads();
        }
    }

    private void DrawSection(string title, System.Action drawContent)
    {
        EditorGUILayout.Space(10);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, sectionStyle);
            }
            EditorGUILayout.Space(2);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                drawContent();
            }
        }
    }

    private void DrawQuadAdjustments(string faceName, ref float offsetX, ref float offsetZ)
    {
        using (new EditorGUILayout.VerticalScope())
        {
            EditorGUILayout.LabelField(faceName, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            offsetX = EditorGUILayout.Slider("X Offset", offsetX, -1f, 1f);
            offsetZ = EditorGUILayout.Slider("Z Offset", offsetZ, -1f, 1f);
            EditorGUI.indentLevel--;
        }
    }

    private void DrawActionButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (!isInPreviewMode)
            {
                if (GUILayout.Button("Preview Impostor", GUILayout.Width(150), GUILayout.Height(30)))
                {
                    if (sourceTree == null)
                    {
                        EditorUtility.DisplayDialog("Error", "Please assign a Source Tree", "OK");
                        return;
                    }
                    isV2Mode = false;
                    StartPreviewMode();
                }
                GUILayout.Space(10);
                if (GUILayout.Button("Preview Impostor v2", GUILayout.Width(150), GUILayout.Height(30)))
                {
                    if (sourceTree == null)
                    {
                        EditorUtility.DisplayDialog("Error", "Please assign a Source Tree", "OK");
                        return;
                    }
                    isV2Mode = true;
                    StartPreviewMode();
                }
            }
            else
            {
                if (GUILayout.Button("Generate Final Impostor", GUILayout.Width(150), GUILayout.Height(30)))
                {
                    EndPreviewMode(true);
                }
                GUILayout.Space(10);
                if (GUILayout.Button("Cancel", GUILayout.Width(150), GUILayout.Height(30)))
                {
                    EndPreviewMode(false);
                }
            }
            GUILayout.FlexibleSpace();
        }
    }

    private string GetSelectedShaderPath()
    {
        return selectedPipeline switch
        {
            RenderPipelineType.URP => "Roundy/Vegetation/ImpostorCrossURP",
            _ => "Roundy/Vegetation/ImpostorCrossBIRP"
        };
    }

    // Modify your CreateImpostorMaterial method to use the selected pipeline
    private Material CreateImpostorMaterial(Texture2D atlas)
    {
        string shaderName = GetSelectedShaderPath();
        Shader shader = Shader.Find(shaderName);

        if (shader == null)
        {
            Debug.LogError($"Could not find shader: {shaderName}. Please ensure the shader is included in your project.");
            return null;
        }

        Material material = new Material(shader);
        material.renderQueue = 2450;
        material.SetTexture("_MainTex", atlas);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Cutoff", 0.5f);
        material.SetFloat("_AlphaCoverageStrength", 1.0f);
        material.enableInstancing = true;
        material.doubleSidedGI = true;

        if (material.mainTexture == null)
        {
            Debug.LogWarning("Manual texture reassignment needed");
            material.mainTexture = atlas;
        }

        return material;
    }
    private void CleanupHiddenBakingObjects()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var rootObjects = scene.GetRootGameObjects();
        List<GameObject> objectsToDestroy = new List<GameObject>();

        // Skip any objects that belong to our source tree
        HashSet<GameObject> sourceTreeObjects = new HashSet<GameObject>();
        if (sourceTree != null)
        {
            foreach (var transform in sourceTree.GetComponentsInChildren<Transform>(true))
            {
                sourceTreeObjects.Add(transform.gameObject);
            }
        }

        // Collect objects to destroy, excluding source tree objects
        foreach (var root in rootObjects)
        {
            var objects = root.GetComponentsInChildren<Transform>(true);
            foreach (var transform in objects)
            {
                if (transform.gameObject.layer == BakingLayer && !sourceTreeObjects.Contains(transform.gameObject))
                {
                    objectsToDestroy.Add(transform.gameObject);
                }
            }
        }

        foreach (var obj in objectsToDestroy)
        {
            if (obj != null)
            {
                Debug.Log($"Cleaning up leftover baking object: {obj.name}");
                DestroyImmediate(obj);
            }
        }
    }


    private void StartPreviewMode()
    {
        CleanupHiddenBakingObjects();
        isInPreviewMode = true;
        textureSize = textureSizeOptions[selectedTextureSizeIndex];

        StoreOriginalLayers(sourceTree);
        SetLayerRecursively(sourceTree, BakingLayer);

        try
        {
            Texture2D atlas = BakeTreeAtlas();

            previewContainer = new GameObject("ImpostorPreview");

            if (showSideBySide)
            {
                // Move impostor to the right of the source tree
                previewContainer.transform.position = sourceTree.transform.position + Vector3.right * previewOffset;
            }
            else
            {
                previewContainer.transform.position = sourceTree.transform.position;
            }

            previewMaterial = CreateImpostorMaterial(atlas);
            CreatePreviewQuads();

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }

            EditorApplication.update += OnEditorUpdate;
        }
        finally
        {
            RestoreOriginalLayers();
        }
    }


    private struct QuadConfig
    {
        public Vector2 size;
        public Vector3 position;
        public Quaternion rotation;
        public int viewIndex;
        public bool isHorizontal;
    }

    private QuadConfig[] GetQuadConfigurations(Vector3 size)
    {
        if (!isV2Mode)
        {
            return new QuadConfig[]
            {
                new QuadConfig { size = new Vector2(size.x, size.y), position = new Vector3(frontQuadOffsetX * size.x, 0, frontQuadOffsetZ * size.z), rotation = Quaternion.Euler(0, 0, 0), viewIndex = 0, isHorizontal = false },
                new QuadConfig { size = new Vector2(size.x, size.y), position = new Vector3(backQuadOffsetX * size.x, 0, backQuadOffsetZ * size.z), rotation = Quaternion.Euler(0, 180, 0), viewIndex = 1, isHorizontal = false },
                new QuadConfig { size = new Vector2(size.z, size.y), position = new Vector3(rightQuadOffsetX * size.x, 0, rightQuadOffsetZ * size.z), rotation = Quaternion.Euler(0, -90, 0), viewIndex = 2, isHorizontal = false },
                new QuadConfig { size = new Vector2(size.z, size.y), position = new Vector3(-leftQuadOffsetX * size.x, 0, leftQuadOffsetZ * size.z), rotation = Quaternion.Euler(0, 90, 0), viewIndex = 3, isHorizontal = false }
            };
        }
        else
        {
            return new QuadConfig[]
            {
                // Plane 1 Front (0°)
                new QuadConfig { size = new Vector2(size.x, size.y), position = new Vector3(frontQuadOffsetX * size.x, 0, frontQuadOffsetZ * size.z), rotation = Quaternion.Euler(0, 0, 0), viewIndex = 0, isHorizontal = false },
                // Plane 1 Back (180°)
                new QuadConfig { size = new Vector2(size.x, size.y), position = new Vector3(backQuadOffsetX * size.x, 0, backQuadOffsetZ * size.z), rotation = Quaternion.Euler(0, 180, 0), viewIndex = 1, isHorizontal = false },
                // Plane 2 Front (120°)
                new QuadConfig { size = new Vector2(size.x, size.y), position = new Vector3(rightQuadOffsetX * size.x, 0, rightQuadOffsetZ * size.z), rotation = Quaternion.Euler(0, 120, 0), viewIndex = 2, isHorizontal = false },
                // Plane 2 Back (300°)
                new QuadConfig { size = new Vector2(size.x, size.y), position = new Vector3(leftQuadOffsetX * size.x, 0, leftQuadOffsetZ * size.z), rotation = Quaternion.Euler(0, 300, 0), viewIndex = 3, isHorizontal = false },
                // Plane 3 Front (240°)
                new QuadConfig { size = new Vector2(size.x, size.y), position = new Vector3(plane3FrontOffsetX * size.x, 0, plane3FrontOffsetZ * size.z), rotation = Quaternion.Euler(0, 240, 0), viewIndex = 4, isHorizontal = false },
                // Plane 3 Back (60°)
                new QuadConfig { size = new Vector2(size.x, size.y), position = new Vector3(plane3BackOffsetX * size.x, 0, plane3BackOffsetZ * size.z), rotation = Quaternion.Euler(0, 60, 0), viewIndex = 5, isHorizontal = false },
                // Horizontal Top
                new QuadConfig { size = new Vector2(size.x, size.z), position = new Vector3(horizontalOffsetX * size.x, size.y * 0.5f + horizontalOffsetY * size.y, horizontalOffsetZ * size.z), rotation = Quaternion.Euler(90, 0, 0), viewIndex = 6, isHorizontal = true },
                // Horizontal Bottom
                new QuadConfig { size = new Vector2(size.x, size.z), position = new Vector3(horizontalOffsetX * size.x, size.y * 0.5f + horizontalOffsetY * size.y, -horizontalOffsetZ * size.z), rotation = Quaternion.Euler(-90, 0, 0), viewIndex = 7, isHorizontal = true }
            };
        }
    }

    private void CreatePreviewQuads()
    {
        Bounds bounds = CalculateBounds(sourceTree);
        Vector3 size = bounds.size;

        string[] quadNames = isV2Mode 
            ? new string[] { "Front1", "Back1", "Front2", "Back2", "Front3", "Back3", "HorizontalTop", "HorizontalBottom" }
            : new string[] { "Front", "Back", "Right", "Left" };

        int numQuads = isV2Mode ? 8 : 4;
        previewQuads = new GameObject[numQuads];

        for (int i = 0; i < numQuads; i++)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"Preview_{quadNames[i]}";
            quad.transform.SetParent(previewContainer.transform);

            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = previewMaterial;

            UpdateQuadUVs(quad, i);
            previewQuads[i] = quad;
        }

        UpdatePreviewQuads();
    }

    private void UpdateQuadUVs(GameObject quad, int quadIndex)
    {
        if (!viewBounds.TryGetValue(quadIndex, out ViewData viewData))
            return;

        Mesh mesh = quad.GetComponent<MeshFilter>().sharedMesh;
        Mesh newMesh = new Mesh();
        newMesh.vertices = mesh.vertices;
        newMesh.triangles = mesh.triangles;
        newMesh.normals = mesh.normals;

        float colWidth = isV2Mode ? 0.25f : 0.5f;
        float rowHeight = 0.5f;

        float uvLeft = viewData.atlasX * colWidth + viewData.bounds.x * colWidth;
        float uvRight = uvLeft + viewData.bounds.width * colWidth;
        float uvBottom = viewData.atlasY * rowHeight + viewData.bounds.y * rowHeight;
        float uvTop = uvBottom + viewData.bounds.height * rowHeight;

        Vector2[] uvs = new Vector2[4] {
            new Vector2(uvLeft, uvBottom),
            new Vector2(uvRight, uvBottom),
            new Vector2(uvLeft, uvTop),
            new Vector2(uvRight, uvTop)
        };

        newMesh.uv = uvs;
        quad.GetComponent<MeshFilter>().mesh = newMesh;
    }

    private void UpdatePreviewQuads()
    {
        if (previewContainer == null) return;

        Bounds bounds = CalculateBounds(sourceTree);
        Vector3 size = bounds.size;

        QuadConfig[] configs = GetQuadConfigurations(size);

        for (int i = 0; i < previewQuads.Length; i++)
        {
            GameObject quad = previewQuads[i];
            if (quad == null) continue;

            QuadConfig config = configs[i];

            Vector3 containerPos = previewContainer.transform.position;
            Vector3 position;
            if (!config.isHorizontal)
            {
                position = containerPos + new Vector3(config.position.x, config.position.y + config.size.y * 0.5f, config.position.z);
            }
            else
            {
                position = containerPos + config.position;
            }

            quad.transform.position = position;
            quad.transform.rotation = config.rotation;
            quad.transform.localScale = new Vector3(config.size.x, config.size.y, 1f);

            UpdateQuadUVs(quad, i);
        }

        SceneView.RepaintAll();
    }

    private void EndPreviewMode(bool finalize)
    {
        isInPreviewMode = false;
        EditorApplication.update -= OnEditorUpdate;

        if (finalize)
        {
            GenerateImpostor();
        }

        if (previewContainer != null)
        {
            DestroyImmediate(previewContainer);
        }
        if (previewMaterial != null)
        {
            DestroyImmediate(previewMaterial);
        }

        previewContainer = null;
        previewMaterial = null;
        previewQuads = new GameObject[4];

        RestoreOriginalLayers();
    }
    private void OnEditorUpdate()
    {
        if (isInPreviewMode && previewContainer != null)
        {
            UpdatePreviewQuads();
        }
    }



    private void GenerateImpostor()
    {
        // Store original layers before anything else
        StoreOriginalLayers(sourceTree);
        SetLayerRecursively(sourceTree, BakingLayer);

        try
        {
            textureSize = textureSizeOptions[selectedTextureSizeIndex];
            Texture2D atlas = BakeTreeAtlas();
            Mesh crossMesh = CreateCrossMesh();
            Material impostorMaterial = CreateImpostorMaterial(atlas);
            GameObject impostor = CreateImpostorGameObject(crossMesh, impostorMaterial);

            if (createLOD)
            {
                SetupLODGroup(impostor);
            }

            SaveAssets(atlas, crossMesh, impostorMaterial, impostor);
        }
        finally
        {
            // Restore original layers in finally block
            RestoreOriginalLayers();
        }
    }




    private class ViewData
    {
        public Rect bounds; // Normalized bounds (0-1) of non-transparent area
        public int atlasX;  // Position in atlas
        public int atlasY;
    }

    private Dictionary<int, ViewData> viewBounds = new Dictionary<int, ViewData>();

    private Texture2D BakeTreeAtlas()
    {
        viewBounds.Clear();

        GameObject cameraGO = new GameObject("TempCamera");
        Camera camera = cameraGO.AddComponent<Camera>();
        ConfigureCamera(camera);

        try
        {
            Bounds treeBounds = CalculateBounds(sourceTree);

            int gridCols = isV2Mode ? 4 : 2;
            int gridRows = 2;

            int atlasWidth = textureSize * gridCols;
            int atlasHeight = textureSize * gridRows;
            Texture2D atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false);

            Color[] clearPixels = new Color[atlasWidth * atlasHeight];
            for (int i = 0; i < clearPixels.Length; i++)
                clearPixels[i] = Color.clear;
            atlas.SetPixels(clearPixels);
            atlas.Apply();

            (Vector3 direction, Vector3 upVector, int atlasX, int atlasY)[] views;
            if (!isV2Mode)
            {
                views = new (Vector3, Vector3, int, int)[]
                {
                    (Vector3.forward, Vector3.up, 0, 0),
                    (Vector3.back, Vector3.up, 1, 0),
                    (Vector3.left, Vector3.up, 0, 1),
                    (Vector3.right, Vector3.up, 1, 1),
                };
            }
            else
            {
                views = new (Vector3, Vector3, int, int)[]
                {
                    (Vector3.forward, Vector3.up, 0, 0),                                         // Plane 1 Front (0°)
                    (Vector3.back, Vector3.up, 1, 0),                                            // Plane 1 Back (180°)
                    (Quaternion.Euler(0, 120, 0) * Vector3.forward, Vector3.up, 2, 0),           // Plane 2 Front (120°)
                    (Quaternion.Euler(0, 300, 0) * Vector3.forward, Vector3.up, 3, 0),           // Plane 2 Back (300°)
                    (Quaternion.Euler(0, 240, 0) * Vector3.forward, Vector3.up, 0, 1),           // Plane 3 Front (240°)
                    (Quaternion.Euler(0, 60, 0) * Vector3.forward, Vector3.up, 1, 1),            // Plane 3 Back (60°)
                    (Vector3.down, Vector3.forward, 2, 1),                                       // Horizontal Top (looking down)
                    (Vector3.up, Vector3.back, 3, 1),                                            // Horizontal Bottom (looking up)
                };
            }

            for (int i = 0; i < views.Length; i++)
            {
                var view = views[i];
                float distance = CalculateCameraDistance(camera, treeBounds);
                camera.transform.position = treeBounds.center - view.direction * distance;
                camera.transform.LookAt(treeBounds.center, view.upVector);

                RenderTexture rt = null;
                Texture2D snapshot = null;

                try
                {
                    rt = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.ARGB32);
                    rt.Create();

                    camera.targetTexture = rt;
                    camera.Render();

                    snapshot = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
                    RenderTexture.active = rt;
                    snapshot.ReadPixels(new Rect(0, 0, textureSize, textureSize), 0, 0);
                    snapshot.Apply();

                    // Clear active render texture before processing
                    RenderTexture.active = null;

                    ProcessTextureTransparency(snapshot, backgroundColor);

                    Rect contentBounds = FindTextureContentBounds(snapshot);
                    viewBounds[i] = new ViewData
                    {
                        bounds = contentBounds,
                        atlasX = view.atlasX,
                        atlasY = view.atlasY
                    };

                    int xPos = view.atlasX * textureSize;
                    int yPos = view.atlasY * textureSize;
                    Color[] snapshotPixels = snapshot.GetPixels();
                    atlas.SetPixels(xPos, yPos, textureSize, textureSize, snapshotPixels);
                }
                finally
                {
                    // Clear camera target first
                    if (camera != null)
                        camera.targetTexture = null;

                    // Clear active render texture
                    RenderTexture.active = null;

                    // Clean up snapshot
                    if (snapshot != null)
                        DestroyImmediate(snapshot);

                    // Clean up render texture
                    if (rt != null)
                    {
                        rt.Release();
                        DestroyImmediate(rt);
                    }
                }
            }

            atlas.Apply();
            return atlas;
        }
        finally
        {
            if (cameraGO != null)
            {
                DestroyImmediate(cameraGO);
            }
        }
    }



    private Rect FindTextureContentBounds(Texture2D texture)
    {
        Color[] pixels = texture.GetPixels();
        int width = texture.width;
        int height = texture.height;

        int xMin = width, xMax = 0;
        int yMin = height, yMax = 0;

        // Find bounds of non-transparent pixels
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixel = pixels[y * width + x];
                if (pixel.a > 0.01f)
                {
                    xMin = Mathf.Min(xMin, x);
                    xMax = Mathf.Max(xMax, x);
                    yMin = Mathf.Min(yMin, y);
                    yMax = Mathf.Max(yMax, y);
                }
            }
        }



        // Convert to normalized coordinates (0-1)
        return new Rect(
            (float)xMin / width,
            (float)yMin / height,
            (float)(xMax - xMin) / width,
            (float)(yMax - yMin) / height
        );
    }

    private void AddQuad(
        List<Vector3> vertices, 
        List<Vector2> uvs, 
        List<int> triangles, 
        QuadConfig config, 
        ViewData viewData)
    {
        float width = config.size.x;
        float height = config.size.y;

        Vector3[] quadVerts = new Vector3[4];
        if (!config.isHorizontal)
        {
            // Vertical quad
            quadVerts[0] = new Vector3(-width * 0.5f, 0, 0);
            quadVerts[1] = new Vector3(width * 0.5f, 0, 0);
            quadVerts[2] = new Vector3(-width * 0.5f, height, 0);
            quadVerts[3] = new Vector3(width * 0.5f, height, 0);
        }
        else
        {
            // Horizontal quad
            quadVerts[0] = new Vector3(-width * 0.5f, -height * 0.5f, 0);
            quadVerts[1] = new Vector3(width * 0.5f, -height * 0.5f, 0);
            quadVerts[2] = new Vector3(-width * 0.5f, height * 0.5f, 0);
            quadVerts[3] = new Vector3(width * 0.5f, height * 0.5f, 0);
        }

        // Apply rotation and position
        for (int j = 0; j < 4; j++)
        {
            quadVerts[j] = config.rotation * quadVerts[j] + config.position;
        }

        float colWidth = isV2Mode ? 0.25f : 0.5f;
        float rowHeight = 0.5f;

        float uvLeft = viewData.atlasX * colWidth + viewData.bounds.x * colWidth;
        float uvRight = uvLeft + viewData.bounds.width * colWidth;
        float uvBottom = viewData.atlasY * rowHeight + viewData.bounds.y * rowHeight;
        float uvTop = uvBottom + viewData.bounds.height * rowHeight;

        Vector2[] quadUVs = new Vector2[4]
        {
            new Vector2(uvLeft, uvBottom),
            new Vector2(uvRight, uvBottom),
            new Vector2(uvLeft, uvTop),
            new Vector2(uvRight, uvTop)
        };

        int baseIndex = vertices.Count;
        vertices.AddRange(quadVerts);
        uvs.AddRange(quadUVs);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 1);
        
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 3);
        triangles.Add(baseIndex + 1);
    }

    private Mesh CreateCrossMesh()
    {
        Bounds bounds = CalculateBounds(sourceTree);
        Vector3 size = bounds.size;

        Mesh mesh = new Mesh();
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        QuadConfig[] configs = GetQuadConfigurations(size);

        for (int i = 0; i < configs.Length; i++)
        {
            QuadConfig config = configs[i];
            if (!viewBounds.TryGetValue(config.viewIndex, out ViewData viewData))
                continue;

            AddQuad(vertices, uvs, triangles, config, viewData);
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles.ToArray(), 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }



    private GameObject CreateImpostorGameObject(Mesh mesh, Material material)
    {
        GameObject impostor = new GameObject(sourceTree.name + "_Impostor");
        impostor.transform.position = sourceTree.transform.position;
        impostor.transform.rotation = sourceTree.transform.rotation;

        MeshFilter meshFilter = impostor.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = impostor.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;

        return impostor;
    }

    private void SetupLODGroup(GameObject impostor)
    {
        LODGroup lodGroup = sourceTree.GetComponent<LODGroup>();
        if (lodGroup == null)
        {
            lodGroup = sourceTree.AddComponent<LODGroup>();
        }

        // Get renderers
        Renderer[] originalRenderers = sourceTree.GetComponentsInChildren<Renderer>();
        Renderer impostorRenderer = impostor.GetComponent<Renderer>();

        // Create LOD levels
        LOD[] lods = new LOD[2];
        lods[0] = new LOD(lodTransitionHeight, originalRenderers);
        lods[1] = new LOD(0.01f, new Renderer[] { impostorRenderer });

        lodGroup.SetLODs(lods);
        lodGroup.RecalculateBounds();

        // Parent impostor to source tree
        impostor.transform.SetParent(sourceTree.transform);
    }

    private void SaveAssets(Texture2D atlas, Mesh mesh, Material material, GameObject impostor)
    {
        string uniqueID = GenerateUniqueID();

        // Create and setup directories
        string rootPath = "Assets/TreeImpostors";
        string texturePath = Path.Combine(rootPath, "Textures");
        string meshPath = Path.Combine(rootPath, "Meshes");
        string materialPath = Path.Combine(rootPath, "Materials");
        string prefabPath = Path.Combine(rootPath, "Prefabs");

        CreateDirectoryIfNeeded(rootPath);
        CreateDirectoryIfNeeded(texturePath);
        CreateDirectoryIfNeeded(meshPath);
        CreateDirectoryIfNeeded(materialPath);
        CreateDirectoryIfNeeded(prefabPath);

        // Save atlas texture
        string atlasAssetPath = Path.Combine(texturePath, $"{sourceTree.name}_Atlas_{uniqueID}.png");
        File.WriteAllBytes(atlasAssetPath, atlas.EncodeToPNG());
        AssetDatabase.ImportAsset(atlasAssetPath);

        // Configure texture import settings
        TextureImporter importer = AssetImporter.GetAtPath(atlasAssetPath) as TextureImporter;
        if (importer != null)
        {
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        // Save mesh
        string meshAssetPath = Path.Combine(meshPath, $"{sourceTree.name}_Mesh_{uniqueID}.asset");
        AssetDatabase.CreateAsset(mesh, meshAssetPath);

        // Save material and ensure texture is assigned
        string materialAssetPath = Path.Combine(materialPath, $"{sourceTree.name}_Material_{uniqueID}.mat");
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasAssetPath);
        AssetDatabase.CreateAsset(material, materialAssetPath);

        // Save prefab
        string prefabAssetPath = Path.Combine(prefabPath, $"{sourceTree.name}_Impostor_{uniqueID}.prefab");
        PrefabUtility.SaveAsPrefabAsset(impostor, prefabAssetPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
    private string GenerateUniqueID()
    {
        return DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
    }

    #region Helper Methods



    private void StoreOriginalLayers(GameObject obj)
    {
        originalLayers.Clear();
        foreach (Transform child in obj.GetComponentsInChildren<Transform>(true))
        {
            originalLayers[child.gameObject] = child.gameObject.layer;
        }
    }

    private void RestoreOriginalLayers()
    {
        foreach (var kvp in originalLayers)
        {
            if (kvp.Key != null)
            {
                kvp.Key.layer = kvp.Value;
            }
        }
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private Bounds CalculateBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(obj.transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    private void ProcessTextureTransparency(Texture2D texture, Color bgColor)
    {
        Color[] pixels = texture.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            if (ColorMatch(pixel, bgColor))
            {
                pixels[i] = Color.clear;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
    }

    private bool ColorMatch(Color a, Color b, float tolerance = 0.01f)
    {
        return Mathf.Abs(a.r - b.r) < tolerance &&
               Mathf.Abs(a.g - b.g) < tolerance &&
               Mathf.Abs(a.b - b.b) < tolerance;
    }

    private float CalculateCameraDistance(Camera camera, Bounds bounds)
    {
        // Calculate the size needed to fit the object
        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        camera.orthographicSize = maxSize * 0.5f;

        // Add padding to ensure the whole tree is visible
        return bounds.extents.magnitude * 2.5f;
    }

    private void ConfigureCamera(Camera camera)
    {
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = backgroundColor;
        camera.orthographic = true;
        camera.cullingMask = 1 << BakingLayer;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 1000f;
    }


    private void CreateDirectoryIfNeeded(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }
    }

    private void OnDestroy()
    {
        if (isInPreviewMode)
        {
            EndPreviewMode(false);
        }

        if (originalLayers != null)
        {
            RestoreOriginalLayers();
        }

        CleanupHiddenBakingObjects();
    }

    #endregion

    #region Preview Handling

    private PreviewRenderUtility previewUtility;
    private Vector2 previewScrollPosition;
    private float previewRotation = 0f;
    private GameObject previewInstance;

    private void OnEnable()
    {
        if (previewUtility == null)
        {
            previewUtility = new PreviewRenderUtility();
            previewUtility.camera.transform.position = new Vector3(0, 3, -5);
            previewUtility.camera.transform.rotation = Quaternion.Euler(30, 0, 0);
            previewUtility.lights[0].intensity = 1f;
            previewUtility.lights[0].transform.rotation = Quaternion.Euler(30, 30, 0);
            previewUtility.ambientColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        }
    }

    private void OnDisable()
    {
        if (previewUtility != null)
        {
            previewUtility.Cleanup();
            previewUtility = null;
        }

        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
        }
    }

    public bool HasPreviewGUI()
    {
        return sourceTree != null;
    }

    public void OnPreviewGUI(Rect r, GUIStyle background)
    {
        if (sourceTree == null || previewUtility == null)
            return;

        // Handle preview rotation
        previewRotation += HandlePreviewRotation(r, previewRotation);

        // Setup preview
        previewUtility.BeginPreview(r, background);

        // Create preview instance if needed
        if (previewInstance == null)
        {
            previewInstance = Instantiate(sourceTree);
            previewInstance.hideFlags = HideFlags.HideAndDontSave;
        }

        // Update rotation
        previewInstance.transform.rotation = Quaternion.Euler(0, previewRotation, 0);

        // Ensure camera can see the whole tree
        Bounds bounds = CalculateBounds(previewInstance);
        float objectSize = bounds.size.magnitude;
        Vector3 cameraPosition = bounds.center - previewUtility.camera.transform.forward * objectSize * 2f;
        previewUtility.camera.transform.position = cameraPosition;

        // Render preview
        previewUtility.camera.Render();

        // Draw the preview
        Texture resultRender = previewUtility.EndPreview();
        GUI.DrawTexture(r, resultRender, ScaleMode.StretchToFill, false);
    }

    private float HandlePreviewRotation(Rect previewRect, float currentRotation)
    {
        Event evt = Event.current;
        if (evt.type == EventType.MouseDrag && previewRect.Contains(evt.mousePosition))
        {
            if (evt.button == 0)
            {
                return evt.delta.x;
            }
        }
        return 0f;
    }

    public void OnPreviewSettings()
    {
        if (sourceTree == null)
            return;

        GUILayout.Label("Rotate: Left Mouse Button");
    }

    #endregion

    #region Additional Settings Window

    private class AdvancedSettingsWindow : EditorWindow
    {
        public TreeCrossQuadImpostorGenerator parent;

        private void OnGUI()
        {
            if (parent == null)
            {
                Close();
                return;
            }

            GUILayout.Label("Advanced Settings", EditorStyles.boldLabel);

            EditorGUILayout.Space();


        }
    }

    private void ShowAdvancedSettings()
    {
        AdvancedSettingsWindow window = EditorWindow.GetWindow<AdvancedSettingsWindow>("Advanced Settings");
        window.parent = this;
    }

    #endregion

    #region Validation and Error Handling

    private bool ValidateTree(GameObject tree)
    {
        if (tree == null)
            return false;

        // Check for required components
        Renderer[] renderers = tree.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            EditorUtility.DisplayDialog("Invalid Tree",
                "The selected object has no renderers. Please select a valid tree object.", "OK");
            return false;
        }

        // Check for valid materials
        foreach (Renderer renderer in renderers)
        {
            if (renderer.sharedMaterial == null)
            {
                EditorUtility.DisplayDialog("Invalid Materials",
                    "One or more renderers have missing materials. Please check the tree's materials.", "OK");
                return false;
            }
        }

        return true;
    }

    private void HandleError(string message, System.Exception ex = null)
    {
        string errorMessage = message;
        if (ex != null)
        {
            errorMessage += $"\n\nError details: {ex.Message}";
            Debug.LogError($"{message}\n{ex}");
        }
        EditorUtility.DisplayDialog("Error", errorMessage, "OK");
    }

    #endregion
}