using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WorldGen.RiverGen
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [ExecuteAlways]
    public class SplineMeshGenerator : MonoBehaviour
    {
        [Header("References")]
        public SplineContainer splineContainer;
        public Material material;

        [Header("Mesh Settings")]
        public float width = 2f;
        public float distancePerSegment = 2f;
        public float textureTiling = 1f;
        public bool autoUpdate = false;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;

        private void OnEnable()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
        }

        private void Update()
        {
            if (autoUpdate)
            {
                GenerateMesh();
            }
        }

        public void GenerateMesh()
        {
            if (splineContainer == null) return;
            
            if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
            if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
            
            if (material != null)
            {
                _meshRenderer.sharedMaterial = material;
            }
            
            Mesh mesh = _meshFilter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "SplineMesh";
                _meshFilter.sharedMesh = mesh;
            }
            
            mesh.Clear();
            
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            
            foreach (Spline spline in splineContainer.Splines)
            {
                AppendSplineMeshData(spline, width, distancePerSegment, textureTiling, vertices, triangles, uvs);
            }
            
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Generates and returns a mesh for a given spline using the specified parameters.
        /// </summary>
        public Mesh GenerateMeshForSpline(Spline spline, Material mat, float customWidth, float customDistancePerSegment, float customTiling)
        {
            Mesh mesh = new Mesh();
            mesh.name = "GeneratedSplineMesh";
            
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            
            AppendSplineMeshData(spline, customWidth, customDistancePerSegment, customTiling, vertices, triangles, uvs);
            
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }

        /// <summary>
        /// Static utility to generate a mesh for a given spline.
        /// </summary>
        public static Mesh CreateMeshForSpline(Spline spline, float splineWidth, float distPerSeg, float tiling)
        {
            Mesh mesh = new Mesh();
            mesh.name = "SplineMesh";
            
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            
            AppendSplineMeshData(spline, splineWidth, distPerSeg, tiling, vertices, triangles, uvs);
            
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }

        public static void AppendSplineMeshData(Spline spline, float w, float distPerSeg, float tiling, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            if (spline == null || spline.Count < 2) return;
            
            int startIndex = vertices.Count;
            float length = spline.GetLength();
            
            // Calculate number of segments based on length and target distance per segment
            int segs = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.1f, distPerSeg)));
            
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                
                // Evaluate position and tangent
                float3 pos3 = SplineUtility.EvaluatePosition(spline, t);
                float3 tan3 = SplineUtility.EvaluateTangent(spline, t);
                
                Vector3 pos = pos3;
                Vector3 tan = math.normalize(tan3);
                
                // Calculate right vector (flat on XZ plane)
                // Cross product of global Up and tangent gives a vector pointing right
                Vector3 up = Vector3.up;
                Vector3 right = Vector3.Cross(up, tan).normalized;
                
                // Fallback if spline goes straight up
                if (right.sqrMagnitude < 0.001f)
                {
                    right = Vector3.right;
                }
                
                Vector3 leftVert = pos - right * (w * 0.5f);
                Vector3 rightVert = pos + right * (w * 0.5f);
                
                vertices.Add(leftVert);
                vertices.Add(rightVert);
                
                float vCoord = t * tiling * (length / w); // Keep texture aspect ratio roughly square
                uvs.Add(new Vector2(0, vCoord));
                uvs.Add(new Vector2(1, vCoord));
                
                if (i < segs)
                {
                    int root = startIndex + i * 2;
                    
                    // Triangle 1
                    triangles.Add(root);
                    triangles.Add(root + 2);
                    triangles.Add(root + 1);
                    
                    // Triangle 2
                    triangles.Add(root + 1);
                    triangles.Add(root + 2);
                    triangles.Add(root + 3);
                }
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(SplineMeshGenerator))]
    public class SplineMeshGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SplineMeshGenerator generator = (SplineMeshGenerator)target;
            
            GUILayout.Space(10);
            if (GUILayout.Button("Generate Mesh"))
            {
                generator.GenerateMesh();
            }
        }
    }
#endif
}
