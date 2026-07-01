using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine.Splines;
using Unity.Mathematics;
using UnityEngine.Profiling;

namespace WorldGen.RiverGen
{
    public class DynamicRiverGraph : MonoBehaviour
    {
        [Header("References")]
        public PoissonDiskSpawner spawner;
        public Transform player;

        [Header("Settings")]
        public float activationSquareSize = 50f;
        public float pathfindingStepSize = 1f;
        public float uphillPenalty = 10f;
        public int splineSamplePoints = 10;

        [Header("Mesh Settings")]
        public float meshWidth = 2f;
        public float distancePerSegment = 2f;
        public Material riverMaterial;

        [Header("Culling Settings")]
        public Material cullingMaterial;
        public float cullingWidthExtension = 2f;

        [Header("Texture Capture")]
        public GlobalTextureSetter globalTextureSetter;

        public struct PathResult
        {
            public int childIndex;
            public List<Vector3> path3D;
            public Spline spline;
            public List<Vector3> vertices;
            public List<int> triangles;
            public List<Vector2> uvs;
        }

        // Path from child index to its parent. Key = child index.
        private Dictionary<int, List<Vector3>> _optimizedPaths = new Dictionary<int, List<Vector3>>();
        
        // Node index -> List of child indices
        private Dictionary<int, List<int>> _childrenMap = new Dictionary<int, List<int>>();

        // Async pathfinding collections
        private ConcurrentQueue<PathResult> _resultsQueue = new ConcurrentQueue<PathResult>();
        private HashSet<int> _computingPaths = new HashSet<int>();

        // Splines
        private SplineContainer _splineContainer;
        private Dictionary<int, Spline> _splines = new Dictionary<int, Spline>();
        
        // Mesh objects
        private Dictionary<int, GameObject> _riverMeshes = new Dictionary<int, GameObject>();

        private void Start()
        {
            if (spawner == null)
            {
                spawner = GetComponent<PoissonDiskSpawner>();
            }

            _splineContainer = GetComponent<SplineContainer>();
            if (_splineContainer == null)
            {
                _splineContainer = gameObject.AddComponent<SplineContainer>();
                if (_splineContainer.Splines.Count > 0)
                {
                    _splineContainer.RemoveSpline(_splineContainer.Splines[0]);
                }
            }

            // Remove SplineMeshGenerator if it exists, since we manage individual mesh objects now
            SplineMeshGenerator meshGen = GetComponent<SplineMeshGenerator>();
            if (meshGen != null)
            {
                if (Application.isPlaying) Destroy(meshGen);
                else DestroyImmediate(meshGen);
            }

            // Warm up TerrainNoise on the main thread so background tasks don't crash
            ClipmapTerrain.TerrainNoise.GetTerrainHeightOriginal(new Vector2(0, 0));

            BuildChildrenMap();
        }

        private void BuildChildrenMap()
        {
            _childrenMap.Clear();
            if (spawner == null || spawner.generatedParents == null) return;

            for (int i = 0; i < spawner.generatedParents.Count; i++)
            {
                if (!_childrenMap.ContainsKey(i))
                {
                    _childrenMap[i] = new List<int>();
                }

                int parentIndex = spawner.generatedParents[i];
                if (parentIndex >= 0)
                {
                    if (!_childrenMap.ContainsKey(parentIndex))
                    {
                        _childrenMap[parentIndex] = new List<int>();
                    }
                    _childrenMap[parentIndex].Add(i);
                }
            }
        }

        private void Update()
        {
            if (spawner == null || spawner.generatedPoints == null || player == null) return;

            // Rebuild map if it was lost or points were regenerated
            if (_childrenMap.Count == 0 && spawner.generatedPoints.Count > 0)
            {
                BuildChildrenMap();
                _optimizedPaths.Clear(); // Clear cached paths if regenerated
                _computingPaths.Clear();
                ClearSplines();
                while (_resultsQueue.TryDequeue(out _)) { } // Clear queue
            }

            float halfSize = activationSquareSize * 0.5f;
            HashSet<int> activeEdges = new HashSet<int>();

            for (int i = 0; i < spawner.generatedPoints.Count; i++)
            {
                Vector3 worldPos = spawner.transform.position + spawner.generatedPoints[i];

                float dx = Mathf.Abs(player.position.x - worldPos.x);
                float dz = Mathf.Abs(player.position.z - worldPos.z);

                if (dx <= halfSize && dz <= halfSize)
                {
                    activeEdges.Add(i);

                    if (_childrenMap.TryGetValue(i, out List<int> children))
                    {
                        foreach (int childIndex in children)
                        {
                            activeEdges.Add(childIndex);
                        }
                    }
                }
            }

            bool processedAny = false;

            // Process completed pathfinding tasks
            while (_resultsQueue.TryDequeue(out var result))
            {
                processedAny = true;
                _computingPaths.Remove(result.childIndex);
                
                // If this path is no longer needed, discard it
                if (!activeEdges.Contains(result.childIndex))
                {
                    continue;
                }

                _optimizedPaths[result.childIndex] = result.path3D;
                
                if (result.path3D != null && result.path3D.Count >= 2)
                {
                    // Create Spline
                    Spline spline = new Spline();
                    
                    float totalLength = 0f;
                    float[] distances = new float[result.path3D.Count];
                    distances[0] = 0f;
                    for (int i = 1; i < result.path3D.Count; i++)
                    {
                        totalLength += Vector3.Distance(result.path3D[i - 1], result.path3D[i]);
                        distances[i] = totalLength;
                    }

                    spline.Add(new BezierKnot(new float3(result.path3D[0].x, result.path3D[0].y, result.path3D[0].z)));

                    int samples = splineSamplePoints;
                    for (int i = 1; i <= samples; i++)
                    {
                        float targetDist = (totalLength * i) / (samples + 1f);
                        Vector3 pt = GetPointAtDistanceStatic(targetDist, distances, result.path3D);
                        spline.Add(new BezierKnot(new float3(pt.x, 0f, pt.z)));
                    }

                    Vector3 lastPt = result.path3D[result.path3D.Count - 1];
                    spline.Add(new BezierKnot(new float3(lastPt.x, 0f, lastPt.z)));

                    for (int i = 0; i < spline.Count; i++)
                    {
                        spline.SetTangentMode(i, TangentMode.AutoSmooth);
                    }

                    // Generate Mesh Data
                    List<Vector3> verts = new List<Vector3>();
                    List<int> tris = new List<int>();
                    List<Vector2> uvs = new List<Vector2>();

                    SplineMeshGenerator.AppendSplineMeshData(spline, meshWidth, distancePerSegment, 1f, verts, tris, uvs);

                    List<Vector3> cullVerts = new List<Vector3>();
                    List<int> cullTris = new List<int>();
                    List<Vector2> cullUvs = new List<Vector2>();

                    SplineMeshGenerator.AppendSplineMeshData(spline, meshWidth + cullingWidthExtension, distancePerSegment, 1f, cullVerts, cullTris, cullUvs);

                    // Add to SplineContainer
                    if (_splines.TryGetValue(result.childIndex, out Spline oldSpline))
                    {
                        _splineContainer.RemoveSpline(oldSpline);
                    }
                    _splineContainer.AddSpline(spline);
                    _splines[result.childIndex] = spline;

                    // Apply to Mesh
                    if (!_riverMeshes.TryGetValue(result.childIndex, out GameObject meshObj) || meshObj == null)
                    {
                        meshObj = new GameObject("RiverSegment_" + result.childIndex);
                        meshObj.layer = 13; // Set to the requested layer
                        meshObj.transform.SetParent(this.transform);
                        meshObj.transform.localPosition = Vector3.zero;
                        meshObj.AddComponent<MeshFilter>();
                        meshObj.AddComponent<MeshRenderer>();
                        _riverMeshes[result.childIndex] = meshObj;
                    }

                    Mesh mesh = new Mesh();
                    mesh.name = "RiverMesh_" + result.childIndex;
                    mesh.SetVertices(verts);
                    mesh.SetTriangles(tris, 0);
                    mesh.SetUVs(0, uvs);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();

                    meshObj.GetComponent<MeshFilter>().sharedMesh = mesh;
                    meshObj.GetComponent<MeshRenderer>().sharedMaterial = riverMaterial;

                    // Apply to Culling Mesh
                    Transform cullTransform = meshObj.transform.Find("CullingMesh");
                    GameObject cullObj;
                    if (cullTransform == null)
                    {
                        cullObj = new GameObject("CullingMesh");
                        cullObj.transform.SetParent(meshObj.transform);
                        cullObj.transform.localPosition = Vector3.zero;
                        cullObj.layer = 12; // Mask map layer
                        cullObj.AddComponent<MeshFilter>();
                        cullObj.AddComponent<MeshRenderer>();
                    }
                    else
                    {
                        cullObj = cullTransform.gameObject;
                    }

                    Mesh cullMesh = new Mesh();
                    cullMesh.name = "RiverCullingMesh_" + result.childIndex;
                    cullMesh.SetVertices(cullVerts);
                    cullMesh.SetTriangles(cullTris, 0);
                    cullMesh.SetUVs(0, cullUvs);
                    cullMesh.RecalculateNormals();
                    cullMesh.RecalculateBounds();

                    cullObj.GetComponent<MeshFilter>().sharedMesh = cullMesh;
                    if (cullingMaterial != null)
                    {
                        cullObj.GetComponent<MeshRenderer>().sharedMaterial = cullingMaterial;
                    }
                }
            }

            foreach (int edgeIndex in activeEdges)
            {
                EnsurePathOptimized(edgeIndex);
            }

            // Unload any loaded edges that are no longer active
            List<int> edgesToRemove = new List<int>();
            foreach (int loadedEdge in _optimizedPaths.Keys)
            {
                if (!activeEdges.Contains(loadedEdge))
                {
                    edgesToRemove.Add(loadedEdge);
                }
            }

            foreach (int edgeIndex in edgesToRemove)
            {
                RemovePath(edgeIndex);
            }

            if (processedAny && _computingPaths.Count == 0)
            {
                if (globalTextureSetter != null)
                {
                    globalTextureSetter.CaptureTexture();
                }
            }
        }

        /// <summary>
        /// Computes the path from a child node to its parent, if it doesn't already exist.
        /// </summary>
        /// <param name="childIndex">The index of the child node.</param>
        private void EnsurePathOptimized(int childIndex)
        {
            if (_optimizedPaths.ContainsKey(childIndex) || _computingPaths.Contains(childIndex)) return; // Already computed or computing

            int parentIndex = spawner.generatedParents[childIndex];
            if (parentIndex < 0 || parentIndex >= spawner.generatedPoints.Count) return; // No valid parent

            _computingPaths.Add(childIndex);

            Vector3 childPos = spawner.transform.position + spawner.generatedPoints[childIndex];
            Vector3 parentPos = spawner.transform.position + spawner.generatedPoints[parentIndex];

            Vector2 start2D = new Vector2(childPos.x, childPos.z);
            Vector2 end2D = new Vector2(parentPos.x, parentPos.z);

            float stepSize = pathfindingStepSize;
            float penalty = uphillPenalty;
            float width = meshWidth;
            float distPerSeg = distancePerSegment;
            int samples = splineSamplePoints;

            Task.Run(() =>
            {
                List<Vector2> path2D = RiverPathfinder.FindPath(start2D, end2D, stepSize, penalty);
                
                List<Vector3> path3D = new List<Vector3>();
                if (path2D != null && path2D.Count > 0)
                {
                    path3D.Capacity = path2D.Count;
                    for (int i = 0; i < path2D.Count; i++)
                    {
                        float y = ClipmapTerrain.TerrainNoise.GetTerrainHeightOriginal(new Vector2(path2D[i].x, path2D[i].y));
                        path3D.Add(new Vector3(path2D[i].x, y, path2D[i].y));
                    }
                }
                
                _resultsQueue.Enqueue(new PathResult
                {
                    childIndex = childIndex,
                    path3D = path3D,
                    spline = null,
                    vertices = null,
                    triangles = null,
                    uvs = null
                });
            });
        }

        private void RemovePath(int childIndex)
        {
            _optimizedPaths.Remove(childIndex);
            
            if (_splines.TryGetValue(childIndex, out Spline spline))
            {
                _splineContainer.RemoveSpline(spline);
                _splines.Remove(childIndex);
            }
            
            if (_riverMeshes.TryGetValue(childIndex, out GameObject meshObj))
            {
                if (meshObj != null)
                {
                    if (Application.isPlaying) Destroy(meshObj);
                    else DestroyImmediate(meshObj);
                }
                _riverMeshes.Remove(childIndex);
            }
        }

        public void ClearCache()
        {
            _optimizedPaths.Clear();
            _childrenMap.Clear();
            ClearSplines();
            BuildChildrenMap();
        }

        private void ClearSplines()
        {
            _splines.Clear();
            if (_splineContainer != null)
            {
                for (int i = _splineContainer.Splines.Count - 1; i >= 0; i--)
                {
                    _splineContainer.RemoveSpline(_splineContainer.Splines[i]);
                }
            }

            foreach (var kvp in _riverMeshes)
            {
                if (kvp.Value != null)
                {
                    if (Application.isPlaying) Destroy(kvp.Value);
                    else DestroyImmediate(kvp.Value);
                }
            }
            _riverMeshes.Clear();
        }

        private static Vector3 GetPointAtDistanceStatic(float distance, float[] distances, List<Vector3> pathPoints)
        {
            for (int i = 1; i < distances.Length; i++)
            {
                if (distances[i] >= distance)
                {
                    float t = (distance - distances[i - 1]) / (distances[i] - distances[i - 1]);
                    return Vector3.Lerp(pathPoints[i - 1], pathPoints[i], t);
                }
            }
            return pathPoints[pathPoints.Count - 1];
        }
    }
}
