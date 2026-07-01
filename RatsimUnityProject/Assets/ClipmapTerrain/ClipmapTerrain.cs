using UnityEngine;
using System.Collections.Generic;

namespace ClipmapTerrain
{
    public class ClipmapTerrain : MonoBehaviour
    {
        [Header("Clipmap Settings")]
        public Transform viewer;
        public Material terrainMaterial;
        
        [Tooltip("Must be a multiple of 4 (e.g. 128, 256)")]
        public int gridResolution = 128;
        
        public int levels = 5;
        
        [Tooltip("Scale of the highest detail (LOD 0) grid in units")]
        public float baseScale = 1f;

        private class ClipmapLevel
        {
            public GameObject go;
            public float scale;
        }

        private List<ClipmapLevel> _levels = new List<ClipmapLevel>();

        void Start()
        {
            if (viewer == null)
            {
                if (Camera.main != null) viewer = Camera.main.transform;
                else
                {
                    Debug.LogError("ClipmapTerrain: No viewer assigned!");
                    return;
                }
            }

            if (terrainMaterial == null)
            {
                Debug.LogWarning("ClipmapTerrain: No material assigned. Terrain will be invisible.");
            }

            // Ensure resolution is a multiple of 4
            if (gridResolution % 4 != 0) 
                gridResolution = Mathf.CeilToInt(gridResolution / 4f) * 4;

            Mesh centerMesh = ClipmapMeshGenerator.GenerateCenterBlock(gridResolution);
            Mesh ringMesh = ClipmapMeshGenerator.GenerateRingBlock(gridResolution);

            for (int i = 0; i < levels; i++)
            {
                ClipmapLevel level = new ClipmapLevel();
                level.scale = baseScale * Mathf.Pow(2, i);
                
                level.go = new GameObject($"LOD_{i}");
                level.go.transform.SetParent(transform, false);
                level.go.layer = 9;
                
                MeshFilter mf = level.go.AddComponent<MeshFilter>();
                mf.sharedMesh = (i == 0) ? centerMesh : ringMesh;
                
                MeshRenderer mr = level.go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = terrainMaterial;
                
                // Pass the LOD level to the shader to prevent Z-fighting by slightly offsetting heights
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                mpb.SetFloat("_Level", i);
                mr.SetPropertyBlock(mpb);
                
                // Scale the GameObject
                level.go.transform.localScale = new Vector3(level.scale, 1f, level.scale);
                
                _levels.Add(level);
            }
        }

        void Update()
        {
            if (viewer == null || _levels.Count == 0) return;

            Vector3 viewerPos = viewer.position;

            for (int i = 0; i < levels; i++)
            {
                ClipmapLevel level = _levels[i];
                float snapIncrement = level.scale;
                
                // Use Round to ensure the viewer is always within snapIncrement/2 of the center
                float snappedX = Mathf.Round(viewerPos.x / snapIncrement) * snapIncrement;
                float snappedZ = Mathf.Round(viewerPos.z / snapIncrement) * snapIncrement;
                
                level.go.transform.position = new Vector3(snappedX, 0, snappedZ);
            }
        }
    }
}
