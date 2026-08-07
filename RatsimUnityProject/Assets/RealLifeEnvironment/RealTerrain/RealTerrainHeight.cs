using UnityEngine;

namespace RealLifeEnvironment
{
    public static class RealTerrainHeight
    {
        private static Texture2D _customHeightmap;
        private static Vector4 _customHeightmapBounds;
        private static float _heightmapMultiplier = 1f;

        public static void SetCustomHeightmap(Texture2D tex, Vector2 center, float sizeX, float sizeZ, float heightMultiplier = 1f)
        {
            _customHeightmap = tex;
            _customHeightmapBounds = new Vector4(center.x, center.y, sizeX, sizeZ);
            _heightmapMultiplier = heightMultiplier;

            if (tex != null)
            {
                Shader.SetGlobalTexture("_CustomTerrainHeightmap", tex);
                Shader.SetGlobalVector("_CustomTerrainHeightmap_TexelSize", new Vector4(1f / tex.width, 1f / tex.height, tex.width, tex.height));
                Shader.SetGlobalVector("_CustomTerrainHeightmap_Bounds", _customHeightmapBounds);
                Shader.SetGlobalFloat("_CustomTerrainHeightmap_Multiplier", _heightmapMultiplier);
            }
            else
            {
                Shader.SetGlobalVector("_CustomTerrainHeightmap_Bounds", Vector4.zero);
                Shader.SetGlobalFloat("_CustomTerrainHeightmap_Multiplier", 1f);
            }
        }

        private static float SampleBicubicBSpline(Texture2D tex, Vector2 uv)
        {
            if (tex == null) return 0f;

            Vector2 texSize = new Vector2(tex.width, tex.height);
            uv = new Vector2(uv.x * texSize.x, uv.y * texSize.y) - new Vector2(0.5f, 0.5f);

            int ix = Mathf.FloorToInt(uv.x);
            int iy = Mathf.FloorToInt(uv.y);
            float fx = uv.x - ix;
            float fy = uv.y - iy;

            float fx2 = fx * fx;
            float fx3 = fx2 * fx;
            float fy2 = fy * fy;
            float fy3 = fy2 * fy;

            float[] wx = new float[4] {
                (1.0f / 6.0f) * (1.0f - fx) * (1.0f - fx) * (1.0f - fx),
                (1.0f / 6.0f) * (3.0f * fx3 - 6.0f * fx2 + 4.0f),
                (1.0f / 6.0f) * (-3.0f * fx3 + 3.0f * fx2 + 3.0f * fx + 1.0f),
                (1.0f / 6.0f) * fx3
            };

            float[] wy = new float[4] {
                (1.0f / 6.0f) * (1.0f - fy) * (1.0f - fy) * (1.0f - fy),
                (1.0f / 6.0f) * (3.0f * fy3 - 6.0f * fy2 + 4.0f),
                (1.0f / 6.0f) * (-3.0f * fy3 + 3.0f * fy2 + 3.0f * fy + 1.0f),
                (1.0f / 6.0f) * fy3
            };

            float result = 0f;
            for (int y = -1; y <= 2; y++)
            {
                int py = Mathf.Clamp(iy + y, 0, tex.height - 1);
                for (int x = -1; x <= 2; x++)
                {
                    int px = Mathf.Clamp(ix + x, 0, tex.width - 1);
                    result += wx[x + 1] * wy[y + 1] * tex.GetPixel(px, py).r;
                }
            }

            return result;
        }

        public static float GetTerrainHeightOriginal(Vector2 worldXZ)
        {
            if (_customHeightmap != null && _customHeightmapBounds.z > 0.0f)
            {
                Vector2 center = new Vector2(_customHeightmapBounds.x, _customHeightmapBounds.y);
                Vector2 size = new Vector2(_customHeightmapBounds.z, _customHeightmapBounds.w);
                Vector2 uv = new Vector2(
                    (worldXZ.x - center.x) / size.x + 0.5f,
                    (worldXZ.y - center.y) / size.y + 0.5f
                );
                return SampleBicubicBSpline(_customHeightmap, uv) * _heightmapMultiplier;
            }

            return 0f;
        }

        public static float GetTerrainHeight(Vector2 worldXZ)
        {
            return GetTerrainHeightOriginal(worldXZ);
        }

        // The terrain mesh uses triangle interpolation between integer coordinates, not continuous bicubic interpolation.
        // To prevent objects from clipping through the flat triangles, we must triangulate exactly like the shader and collider.
        public static float GetTriangulatedHeight(Vector2 worldXZ, float gridSpacing = 1.0f)
        {
            float gridX = Mathf.Floor(worldXZ.x / gridSpacing) * gridSpacing;
            float gridZ = Mathf.Floor(worldXZ.y / gridSpacing) * gridSpacing;
            
            float hA = GetTerrainHeightOriginal(new Vector2(gridX, gridZ));
            float hB = GetTerrainHeightOriginal(new Vector2(gridX + gridSpacing, gridZ));
            float hC = GetTerrainHeightOriginal(new Vector2(gridX, gridZ + gridSpacing));
            float hD = GetTerrainHeightOriginal(new Vector2(gridX + gridSpacing, gridZ + gridSpacing));
            
            float fracX = (worldXZ.x - gridX) / gridSpacing;
            float fracZ = (worldXZ.y - gridZ) / gridSpacing;
            
            if (fracX + fracZ < 1.0f)
            {
                // Bottom-Left triangle
                return hA + fracX * (hB - hA) + fracZ * (hC - hA);
            }
            else
            {
                // Top-Right triangle
                return hD + (1.0f - fracX) * (hC - hD) + (1.0f - fracZ) * (hB - hD);
            }
        }
    }
}
