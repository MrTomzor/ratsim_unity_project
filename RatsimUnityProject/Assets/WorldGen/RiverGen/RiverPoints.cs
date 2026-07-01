using UnityEngine;
using System.Collections.Generic;
using ClipmapTerrain;
using UnityEngine.Splines;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class RiverPoints : MonoBehaviour
{

    [Header("Settings")]
    public Vector2 A = new Vector2(0f, 0f);
    public Vector2 B = new Vector2(100f, 100f);
    public uint betweens = 10;
    public float pathfindingStepSize = 1f;
    public float uphillPenalty = 10f;
    public int splineSamplePoints = 10;

    private GameObject _sphereA;
    private GameObject _sphereB;
    private List<Vector3> _pathPoints = new List<Vector3>();
    private uint _lastBetweens;
    private Vector2 _lastA;
    private Vector2 _lastB;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        UpdateSpherePosition();
    }

    public void ResetGenerator()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying) Destroy(transform.GetChild(i).gameObject);
            else DestroyImmediate(transform.GetChild(i).gameObject);
        }
        
        _sphereA = null;
        _sphereB = null;
        _pathPoints.Clear();
        
        UpdateSpherePosition();
    }

    public void UpdateSpherePosition()
    {
        UpdateMarker(ref _sphereA, A, "Point A Marker", Color.red);
        UpdateMarker(ref _sphereB, B, "Point B Marker", Color.red);
        UpdateBetweenSpheres();
    }

    public void RunPathfinding()
    {
        List<Vector2> path = RiverPathfinder.FindPath(A, B, pathfindingStepSize, uphillPenalty);
        if (path == null || path.Count < 2) return;

        _pathPoints.Clear();

        for (int i = 0; i < path.Count; i++)
        {
            float y = TerrainNoise.GetTerrainHeightOriginal(new Vector2(path[i].x, path[i].y));
            _pathPoints.Add(new Vector3(path[i].x, y, path[i].y));
        }

        // Removed parameter sync so dragging A or B later instantly reverts to the original betweens count in a straight line
    }

    public void PathToSpline()
    {
        if (_pathPoints == null || _pathPoints.Count < 2)
        {
            Debug.LogWarning("Path points are empty or less than 2. Run pathfinding first.");
            return;
        }

        SplineContainer splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null)
        {
            splineContainer = gameObject.AddComponent<SplineContainer>();
        }

        Spline spline = splineContainer.Spline;
        if (spline == null)
        {
            splineContainer.AddSpline(new Spline());
            spline = splineContainer.Spline;
        }
        
        spline.Clear();

        // Calculate distances for uniform sampling
        float totalLength = 0f;
        float[] distances = new float[_pathPoints.Count];
        distances[0] = 0f;
        for (int i = 1; i < _pathPoints.Count; i++)
        {
            totalLength += Vector3.Distance(_pathPoints[i - 1], _pathPoints[i]);
            distances[i] = totalLength;
        }

        // Add start knot
        spline.Add(new BezierKnot(new float3(_pathPoints[0].x, _pathPoints[0].y, _pathPoints[0].z)));

        // Add intermediate knots
        for (int i = 1; i <= splineSamplePoints; i++)
        {
            float targetDist = (totalLength * i) / (splineSamplePoints + 1f);
            Vector3 pt = GetPointAtDistance(targetDist, distances);
            spline.Add(new BezierKnot(new float3(pt.x, pt.y, pt.z)));
        }

        // Add end knot
        Vector3 lastPt = _pathPoints[_pathPoints.Count - 1];
        spline.Add(new BezierKnot(new float3(lastPt.x, lastPt.y, lastPt.z)));

        // Set auto smooth tangent mode for all knots
        for (int i = 0; i < spline.Count; i++)
        {
            spline.SetTangentMode(i, TangentMode.AutoSmooth);
        }
    }

    private Vector3 GetPointAtDistance(float distance, float[] distances)
    {
        for (int i = 1; i < distances.Length; i++)
        {
            if (distances[i] >= distance)
            {
                float t = (distance - distances[i - 1]) / (distances[i] - distances[i - 1]);
                return Vector3.Lerp(_pathPoints[i - 1], _pathPoints[i], t);
            }
        }
        return _pathPoints[_pathPoints.Count - 1];
    }

    private void UpdateMarker(ref GameObject sphere, Vector2 point, string name, Color color)
    {
        if (sphere == null)
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.GetComponent<Renderer>().sharedMaterial.color = color;
            sphere.name = name;
            sphere.transform.SetParent(transform);
        }

        sphere.transform.localScale = new Vector3(5f, 5f, 5f);
        float y = TerrainNoise.GetTerrainHeightOriginal(new Vector2(point.x, point.y));
        sphere.transform.position = new Vector3(point.x, y, point.y);
    }

    // Update is called once per frame
    void Update()
    {
        bool movedA = CheckMarkerMoved(ref _sphereA, ref A);
        bool movedB = CheckMarkerMoved(ref _sphereB, ref B);

        if (movedA || movedB || _lastBetweens != betweens || A != _lastA || B != _lastB)
        {
            UpdateBetweenSpheres();
            _lastA = A;
            _lastB = B;
        }
    }

    private bool CheckMarkerMoved(ref GameObject sphere, ref Vector2 point)
    {
        if (sphere != null && sphere.transform.hasChanged)
        {
            if (point.x != transform.position.x || point.y != transform.position.z) // Minor fix: Use point for comparison
            {
                // To avoid drift, we only check sphere's actual position vs what we expect
            }

            if (point.x != sphere.transform.position.x || point.y != sphere.transform.position.z)
            {
                point.x = sphere.transform.position.x;
                point.y = sphere.transform.position.z;
                
                float y = TerrainNoise.GetTerrainHeightOriginal(new Vector2(point.x, point.y));
                sphere.transform.position = new Vector3(point.x, y, point.y);
                sphere.transform.hasChanged = false;
                return true;
            }
            sphere.transform.hasChanged = false;
        }
        return false;
    }

    private void UpdateBetweenSpheres()
    {
        _pathPoints.Clear();

        // Add start point A
        float yA = TerrainNoise.GetTerrainHeightOriginal(new Vector2(A.x, A.y));
        _pathPoints.Add(new Vector3(A.x, yA, A.y));

        // Add intermediate points to trace the terrain
        for (int i = 0; i < betweens; i++)
        {
            float t = (i + 1) / (float)(betweens + 1);
            Vector2 point = Vector2.Lerp(A, B, t);
            float y = TerrainNoise.GetTerrainHeightOriginal(new Vector2(point.x, point.y));
            _pathPoints.Add(new Vector3(point.x, y, point.y));
        }

        // Add end point B
        float yB = TerrainNoise.GetTerrainHeightOriginal(new Vector2(B.x, B.y));
        _pathPoints.Add(new Vector3(B.x, yB, B.y));

        _lastBetweens = betweens;
    }

    void OnValidate()
    {
        if (_sphereA != null)
        {
            float y = TerrainNoise.GetTerrainHeightOriginal(new Vector2(A.x, A.y));
            _sphereA.transform.position = new Vector3(A.x, y, A.y);
            _sphereA.transform.hasChanged = false;
        }
        if (_sphereB != null)
        {
            float y = TerrainNoise.GetTerrainHeightOriginal(new Vector2(B.x, B.y));
            _sphereB.transform.position = new Vector3(B.x, y, B.y);
            _sphereB.transform.hasChanged = false;
        }
    }

    void OnDrawGizmos()
    {
        if (_pathPoints == null || _pathPoints.Count < 2) return;

        Gizmos.color = Color.white;
        for (int i = 0; i < _pathPoints.Count - 1; i++)
        {
            Gizmos.DrawLine(_pathPoints[i], _pathPoints[i + 1]);
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(RiverPoints))]
public class RiverPointsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        RiverPoints script = (RiverPoints)target;
        if (GUILayout.Button("Update Sphere Position"))
        {
            script.UpdateSpherePosition();
        }

        if (GUILayout.Button("Reset Generator"))
        {
            script.ResetGenerator();
        }

        if (GUILayout.Button("Run A* Pathfinding"))
        {
            script.RunPathfinding();
        }

        if (GUILayout.Button("Path to Spline"))
        {
            script.PathToSpline();
        }
    }
}
#endif
