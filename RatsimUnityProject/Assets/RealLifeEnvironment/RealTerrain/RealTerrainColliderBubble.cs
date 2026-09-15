using UnityEngine;
using System.Collections.Generic;

namespace RealLifeEnvironment
{
    public class RealTerrainColliderBubble : MonoBehaviour
    {
        [Header("References")]
        public Transform player;
        public Material terrainMaterial;

        [Header("Settings")]
        public float size = 32f;
        [Range(2, 256)]
        public int resolution = 32;
        public float updateThreshold = 2f;

        private MeshCollider _meshCollider;
        private GameObject _colliderObject;
        
        // Double buffering for async baking
        private Mesh[] _collisionMeshes;
        private Vector3[][] _verticesBuffers;
        private int _activeMeshIndex = 0;
        
        private int[] _triangles;
        private Vector3 _pendingColliderPosition;

        private Vector3 _lastUpdatePos = Vector3.positiveInfinity;
        
        // Tracking for live updates
        private float _lastSize;
        private int _lastResolution;
        
        // Async state
        private bool _isBaking = false;
        private volatile bool _bakeCompleted = false;

        void Start()
        {
            // Create a child object to hold the collider.
            _colliderObject = new GameObject("TerrainCollisionMesh");
            _colliderObject.transform.SetParent(transform); 
            
            _meshCollider = _colliderObject.AddComponent<MeshCollider>();

            // Attach a helper script so Gizmos don't get culled when the manager object is off-screen
            var gizmoDrawer = _colliderObject.AddComponent<ColliderGizmoDrawer>();
            gizmoDrawer.resolution = resolution;
            
            _lastSize = size;
            _lastResolution = resolution;
            
            InitializeMeshes();
        }

        void OnDestroy()
        {
            if (_colliderObject != null)
            {
                Destroy(_colliderObject);
            }
            if (_collisionMeshes != null)
            {
                foreach (var mesh in _collisionMeshes)
                {
                    if (mesh != null) Destroy(mesh);
                }
            }
        }

        void Update()
        {
            if (player == null || terrainMaterial == null) return;

            // Handle runtime settings tampering (live updates)
            if (Mathf.Abs(size - _lastSize) > 0.001f || resolution != _lastResolution)
            {
                // Wait for any current bake to finish before recreating arrays
                if (!_isBaking) 
                {
                    resolution = Mathf.Max(2, resolution);
                    _lastSize = size;
                    _lastResolution = resolution;
                    InitializeMeshes();
                    _lastUpdatePos = Vector3.positiveInfinity; // force immediate update
                }
            }

            // Assign the mesh if background baking just finished
            if (_bakeCompleted)
            {
                // Move the collider object ONLY when the new mesh is fully ready
                _colliderObject.transform.position = _pendingColliderPosition;
                _meshCollider.sharedMesh = _collisionMeshes[_activeMeshIndex];
                
                var gizmoDrawer = _colliderObject.GetComponent<ColliderGizmoDrawer>();
                if (gizmoDrawer != null)
                {
                    gizmoDrawer.vertices = _verticesBuffers[_activeMeshIndex];
                    gizmoDrawer.resolution = resolution;
                }

                _bakeCompleted = false;
                _isBaking = false;
            }

            // Only update if the player has moved significantly and we aren't currently baking
            if (!_isBaking && Vector3.Distance(player.position, _lastUpdatePos) > updateThreshold)
            {
                UpdateColliderBubble();
                _lastUpdatePos = player.position;
            }
        }

        private void InitializeMeshes()
        {
            if (_collisionMeshes != null)
            {
                foreach (var mesh in _collisionMeshes)
                {
                    if (mesh != null) Destroy(mesh);
                }
            }

            _collisionMeshes = new Mesh[2];
            _verticesBuffers = new Vector3[2][];
            for (int i = 0; i < 2; i++)
            {
                _collisionMeshes[i] = new Mesh();
                _collisionMeshes[i].name = $"Terrain Collision Bubble {i}";
                _collisionMeshes[i].MarkDynamic();
            }

            int numVertices = (resolution + 1) * (resolution + 1);
            
            List<int> tris = new List<int>();
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int bl = GetIndex(x, z);
                    int br = GetIndex(x + 1, z);
                    int tl = GetIndex(x, z + 1);
                    int tr = GetIndex(x + 1, z + 1);

                    tris.Add(bl);
                    tris.Add(tl);
                    tris.Add(br);

                    tris.Add(br);
                    tris.Add(tl);
                    tris.Add(tr);
                }
            }
            _triangles = tris.ToArray();
            
            for (int i = 0; i < 2; i++)
            {
                _verticesBuffers[i] = new Vector3[numVertices];
                _collisionMeshes[i].vertices = _verticesBuffers[i];
                _collisionMeshes[i].triangles = _triangles;
            }
            
            _pendingColliderPosition = _colliderObject.transform.position;
        }

        private void UpdateColliderBubble()
        {
            float stepSize = size / resolution;

            // Snap the bubble to step size grid to prevent floating point jitter
            float snappedX = Mathf.Round(player.position.x / stepSize) * stepSize;
            float snappedZ = Mathf.Round(player.position.z / stepSize) * stepSize;
            
            // Queue the position update for later, when the mesh is baked
            _pendingColliderPosition = new Vector3(snappedX, 0, snappedZ);

            float offset = size / 2f;

            // Swap to the inactive mesh and buffer for baking
            _activeMeshIndex = (_activeMeshIndex + 1) % 2;
            Mesh targetMesh = _collisionMeshes[_activeMeshIndex];
            Vector3[] targetVertices = _verticesBuffers[_activeMeshIndex];

            // Update vertices on main thread because GetTerrainHeight samples a Texture2D (not thread-safe)
            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    int index = GetIndex(x, z);
                    
                    float localX = (x * stepSize) - offset;
                    float localZ = (z * stepSize) - offset;
                    
                    float worldX = snappedX + localX;
                    float worldZ = snappedZ + localZ;

                    float y = RealTerrainHeight.GetTerrainHeight(new Vector2(worldX, worldZ));
                    
                    targetVertices[index] = new Vector3(localX, y, localZ);
                }
            }

            targetMesh.vertices = targetVertices;
            targetMesh.RecalculateBounds();

            // Start async baking
            _isBaking = true;
            int meshId = targetMesh.GetInstanceID();
            
            System.Threading.Tasks.Task.Run(() =>
            {
                Physics.BakeMesh(meshId, false);
                _bakeCompleted = true; // Flag main thread to assign mesh
            });
        }

        private int GetIndex(int x, int z)
        {
            return z * (resolution + 1) + x;
        }
    }
}
