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
        public int resolution = 32;
        public float updateThreshold = 2f;

        private MeshCollider _meshCollider;
        private GameObject _colliderObject;
        private Mesh _collisionMesh;
        private Vector3[] _vertices;
        private int[] _triangles;

        private Vector3 _lastUpdatePos = Vector3.positiveInfinity;

        void Start()
        {
            // Create an independent child object to hold the collider.
            // This prevents the script from teleporting the player if attached directly to the player.
            _colliderObject = new GameObject("TerrainCollisionMesh");
            _colliderObject.transform.SetParent(null); // Keep it detached in world space
            
            _meshCollider = _colliderObject.AddComponent<MeshCollider>();
            
            InitializeMesh();
        }

        void OnDestroy()
        {
            if (_colliderObject != null)
            {
                Destroy(_colliderObject);
            }
        }

        void Update()
        {
            if (player == null || terrainMaterial == null) return;

            // Only update if the player has moved significantly
            if (Vector3.Distance(player.position, _lastUpdatePos) > updateThreshold)
            {
                UpdateColliderBubble();
                _lastUpdatePos = player.position;
            }
        }


        private void InitializeMesh()
        {
            _collisionMesh = new Mesh();
            _collisionMesh.name = "Terrain Collision Bubble";
            _collisionMesh.MarkDynamic();

            int numVertices = (resolution + 1) * (resolution + 1);
            _vertices = new Vector3[numVertices];
            
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
            _collisionMesh.vertices = _vertices;
            _collisionMesh.triangles = _triangles;
        }

        private void UpdateColliderBubble()
        {
            // Snap the bubble to integer coordinates to prevent floating point jitter
            float snappedX = Mathf.Round(player.position.x);
            float snappedZ = Mathf.Round(player.position.z);
            
            // Move the independent collider GameObject to the snapped position
            _colliderObject.transform.position = new Vector3(snappedX, 0, snappedZ);

            float offset = resolution / 2f;

            // Update the vertices based on the absolute world position
            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    int index = GetIndex(x, z);
                    
                    float localX = x - offset;
                    float localZ = z - offset;
                    
                    float worldX = snappedX + localX;
                    float worldZ = snappedZ + localZ;

                    float y = RealTerrainHeight.GetTerrainHeight(new Vector2(worldX, worldZ));
                    
                    _vertices[index] = new Vector3(localX, y, localZ);
                }
            }

            _collisionMesh.vertices = _vertices;
            _collisionMesh.RecalculateBounds();
            
            // Assign the updated mesh to the collider
            _meshCollider.sharedMesh = _collisionMesh;
        }

        private int GetIndex(int x, int z)
        {
            return z * (resolution + 1) + x;
        }

    }
}
