using UnityEngine;
using System.Collections.Generic;

namespace ClipmapTerrain
{
    public static class ClipmapMeshGenerator
    {
        /// <summary>
        /// Generates a solid NxN grid centered at the origin.
        /// </summary>
        public static Mesh GenerateCenterBlock(int resolution)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();

            // Center at origin
            float offset = resolution / 2f;

            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    vertices.Add(new Vector3(x - offset, 0, z - offset));
                    uvs.Add(new Vector2((float)x / resolution, (float)z / resolution));
                }
            }

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int i = z * (resolution + 1) + x;
                    triangles.Add(i);
                    triangles.Add(i + resolution + 1);
                    triangles.Add(i + 1);

                    triangles.Add(i + 1);
                    triangles.Add(i + resolution + 1);
                    triangles.Add(i + resolution + 2);
                }
            }

            Mesh mesh = new Mesh
            {
                name = "Clipmap Center Block",
                vertices = vertices.ToArray(),
                triangles = triangles.ToArray(),
                uv = uvs.ToArray()
            };
            mesh.RecalculateNormals();
            
            // Expand the bounds significantly on the Y axis because the vertex shader 
            // will displace the vertices far outside the original flat geometry.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(resolution, 10000f, resolution));
            
            return mesh;
        }

        /// <summary>
        /// Generates an NxN grid with a hole in the center of size (N/2)x(N/2).
        /// </summary>
        public static Mesh GenerateRingBlock(int resolution)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();

            float offset = resolution / 2f;
            
            // The hole goes from 1/4 to 3/4 of the way across, plus an overlap margin
            // This overlap ensures that when adjacent LODs drift independently, they don't expose gaps.
            int holeStart = (resolution / 4) + 2;
            int holeEnd = resolution - holeStart;

            // Generate vertices
            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    // Skip vertices strictly inside the hole
                    if (x > holeStart && x < holeEnd && z > holeStart && z < holeEnd)
                        continue;

                    vertices.Add(new Vector3(x - offset, 0, z - offset));
                    uvs.Add(new Vector2((float)x / resolution, (float)z / resolution));
                }
            }

            // Function to get vertex index
            int GetIndex(int px, int pz)
            {
                // Reconstruct index accounting for the skipped vertices
                int idx = 0;
                for (int z = 0; z <= pz; z++)
                {
                    for (int x = 0; x <= resolution; x++)
                    {
                        if (x > holeStart && x < holeEnd && z > holeStart && z < holeEnd)
                            continue;
                        
                        if (x == px && z == pz) return idx;
                        idx++;
                    }
                }
                return -1;
            }

            // Generate triangles
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    // Skip quads that fall into the hole
                    if (x >= holeStart && x < holeEnd && z >= holeStart && z < holeEnd)
                        continue;

                    int bl = GetIndex(x, z);
                    int br = GetIndex(x + 1, z);
                    int tl = GetIndex(x, z + 1);
                    int tr = GetIndex(x + 1, z + 1);

                    triangles.Add(bl);
                    triangles.Add(tl);
                    triangles.Add(br);

                    triangles.Add(br);
                    triangles.Add(tl);
                    triangles.Add(tr);
                }
            }

            Mesh mesh = new Mesh
            {
                name = "Clipmap Ring Block",
                vertices = vertices.ToArray(),
                triangles = triangles.ToArray(),
                uv = uvs.ToArray()
            };
            mesh.RecalculateNormals();
            
            // Expand the bounds significantly on the Y axis because the vertex shader 
            // will displace the vertices far outside the original flat geometry.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(resolution, 10000f, resolution));
            
            return mesh;
        }
    }
}
