using UnityEngine;

/// <summary>
/// Runtime visualization of 3D lidar raycasts using GL lines.
/// Works in builds (unlike Debug.DrawLine/Gizmos). Toggle with G key.
/// Attach to the same GameObject as SemanticLidar3DSensor.
/// </summary>
[RequireComponent(typeof(SemanticLidar3DSensor))]
public class Lidar3DRaycastVisualizer : MonoBehaviour
{
    public bool showRays = true;
    public bool colorBySemantics = false;
    public Color hitColor = Color.red;
    public Color missColor = new Color(1f, 0.3f, 0.3f, 0.3f);
    public KeyCode toggleKey = KeyCode.H;
    
    Color[] semanticColors;

    SemanticLidar3DSensor sensor;
    Material lineMaterial;

    void Start()
    {
        sensor = GetComponent<SemanticLidar3DSensor>();
        CreateLineMaterial();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            showRays = !showRays;
    }

    void CreateLineMaterial()
    {
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        lineMaterial = new Material(shader);
        lineMaterial.hideFlags = HideFlags.HideAndDontSave;
        lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        lineMaterial.SetInt("_ZWrite", 0);
        lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    void OnRenderObject()
    {
        if (!showRays || sensor == null || sensor.lastRanges == null)
            return;

        lineMaterial.SetPass(0);

        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);
        GL.Begin(GL.LINES);

        Vector3 origin = transform.position;
        Quaternion sensorRot = transform.rotation;
        
        int numRaysVertical = sensor.numRaysVertical;
        int numRaysHorizontal = sensor.numRaysHorizontal;

        if (sensor.lastRanges.Length != numRaysVertical * numRaysHorizontal)
        {
            GL.End();
            GL.PopMatrix();
            return;
        }

        try
        {
            for (int v = 0; v < numRaysVertical; v++)
            {
                float vFraction = numRaysVertical > 1 ? (float)v / (numRaysVertical - 1) : 0.5f;
                float pitch = Mathf.Lerp(sensor.verticalFovStartDeg, sensor.verticalFovEndDeg, vFraction);

                for (int h = 0; h < numRaysHorizontal; h++)
                {
                    float hFraction = numRaysHorizontal > 1 ? (float)h / (numRaysHorizontal - 1) : 0.5f;
                    float yaw = Mathf.Lerp(sensor.horizontalFovStartDeg, sensor.horizontalFovEndDeg, hFraction);

                    Vector3 localDir = Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward;
                    Vector3 worldDir = sensorRot * localDir;

                    int index = v * numRaysHorizontal + h;
                    float range = sensor.lastRanges[index];
                    
                    if (range >= 0)
                    {
                        Color currentColor = hitColor;
                        if (colorBySemantics && sensor.lastDescriptors != null)
                        {
                            int maxIndex = -1;
                            float maxVal = 0;
                            uint dim = SemanticLidar3DSensor.descriptorDimension;
                            int descOffset = index * (int)dim;
                            
                            if (descOffset + dim <= sensor.lastDescriptors.Length)
                            {
                                for (int d = 0; d < dim; d++)
                                {
                                    float val = sensor.lastDescriptors[descOffset + d];
                                    if (val > maxVal)
                                    {
                                        maxVal = val;
                                        maxIndex = d;
                                    }
                                }
                                
                                if (maxIndex >= 0 && dim > 0)
                                {
                                    if (semanticColors == null || semanticColors.Length != dim)
                                    {
                                        semanticColors = new Color[dim];
                                        for (int i = 0; i < dim; i++)
                                        {
                                            semanticColors[i] = Color.HSVToRGB((float)i / dim, 1f, 1f);
                                        }
                                    }
                                    currentColor = semanticColors[maxIndex];
                                }
                            }
                        }

                        GL.Color(currentColor);
                        GL.Vertex(origin);
                        GL.Vertex(origin + worldDir * range);
                    }
                    else
                    {
                        GL.Color(missColor);
                        GL.Vertex(origin);
                        GL.Vertex(origin + worldDir * sensor.maxRange);
                    }
                }
            }
        }
        finally
        {
            GL.End();
            GL.PopMatrix();
        }
    }

    void OnDestroy()
    {
        if (lineMaterial != null)
            DestroyImmediate(lineMaterial);
    }
}
