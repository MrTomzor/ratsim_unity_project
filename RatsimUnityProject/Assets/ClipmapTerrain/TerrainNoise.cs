using UnityEngine;

namespace ClipmapTerrain
{
    public static class TerrainNoise
    {
        public const int FbmIterations = 5;

        private static float _noiseScale = 1.0f;
        private static float _heightMax = 50f;
        private static float _pa = 1.0f;
        private static float _pb = 1.0f;
        private static float _pc = 1.0f;
        private static float _pd = 1.0f;
        private static float _pe = 0.0f;

        private static Material _terrainMaterial;
        private static bool _initialized = false;

        private static void AutoUpdateProperties()
        {
            if (_initialized) return;
            _initialized = true;

            if (_terrainMaterial == null)
            {
                _terrainMaterial = Resources.Load<Material>("ClipmapTerrain");
            }

            if (_terrainMaterial != null)
            {
                if (_terrainMaterial.HasProperty("_NoiseScale"))
                    _noiseScale = _terrainMaterial.GetFloat("_NoiseScale") / 100.0f;
                if (_terrainMaterial.HasProperty("_HeightMax"))
                    _heightMax = _terrainMaterial.GetFloat("_HeightMax");
                if (_terrainMaterial.HasProperty("_Pa"))
                    _pa = _terrainMaterial.GetFloat("_Pa");
                if (_terrainMaterial.HasProperty("_Pb"))
                    _pb = _terrainMaterial.GetFloat("_Pb");
                if (_terrainMaterial.HasProperty("_Pc"))
                    _pc = _terrainMaterial.GetFloat("_Pc");
                if (_terrainMaterial.HasProperty("_Pd"))
                    _pd = _terrainMaterial.GetFloat("_Pd");
                if (_terrainMaterial.HasProperty("_Pe"))
                    _pe = _terrainMaterial.GetFloat("_Pe");
            }
        }

        public static Vector2 GetNumericalGrad(float x, float z, int iters = FbmIterations)
        {
            float eps = 0.01f;
            float hx1 = GetTerrainHeightOriginal(new Vector2(x + eps, z), iters);
            float hx2 = GetTerrainHeightOriginal(new Vector2(x - eps, z), iters);
            float hz1 = GetTerrainHeightOriginal(new Vector2(x, z + eps), iters);
            float hz2 = GetTerrainHeightOriginal(new Vector2(x, z - eps), iters);

            return new Vector2((hx1 - hx2) / (2.0f * eps), (hz1 - hz2) / (2.0f * eps));
        }

        

        public static float Hash(Vector2 p)
        {
            int ix = Mathf.FloorToInt(p.x);
            int iy = Mathf.FloorToInt(p.y);
            
            uint ux = unchecked((uint)ix);
            uint uy = unchecked((uint)iy);
            
            uint seed = ux * 73856093u ^ uy * 19349663u;
            uint state = seed * 747796405u + 2891336453u;
            uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
            uint result = (word >> 22) ^ word;
            
            return (float)result / 4294967295.0f;
        }

        public static Vector2 HashDir(Vector2 p)
        {
            float h = Hash(p) * 6.28318530718f;
            return new Vector2(Mathf.Cos(h), Mathf.Sin(h));
        }

        public static Vector3 Noised(Vector2 p)
        {
            Vector2 i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            Vector2 f = new Vector2(p.x - i.x, p.y - i.y);

            Vector2 u = new Vector2(
                f.x * f.x * f.x * (f.x * (f.x * 6.0f - 15.0f) + 10.0f),
                f.y * f.y * f.y * (f.y * (f.y * 6.0f - 15.0f) + 10.0f)
            );

            Vector2 du = new Vector2(
                30.0f * f.x * f.x * (f.x * (f.x - 2.0f) + 1.0f),
                30.0f * f.y * f.y * (f.y * (f.y - 2.0f) + 1.0f)
            );

            Vector2 ga = HashDir(i + new Vector2(0.0f, 0.0f));
            Vector2 gb = HashDir(i + new Vector2(1.0f, 0.0f));
            Vector2 gc = HashDir(i + new Vector2(0.0f, 1.0f));
            Vector2 gd = HashDir(i + new Vector2(1.0f, 1.0f));

            float va = Vector2.Dot(ga, f - new Vector2(0.0f, 0.0f));
            float vb = Vector2.Dot(gb, f - new Vector2(1.0f, 0.0f));
            float vc = Vector2.Dot(gc, f - new Vector2(0.0f, 1.0f));
            float vd = Vector2.Dot(gd, f - new Vector2(1.0f, 1.0f));

            float value = va + u.x * (vb - va) + u.y * (vc - va) + u.x * u.y * (va - vb - vc + vd);

            Vector2 grad = ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd);
            grad.x += du.x * (u.y * (va - vb - vc + vd) + vb - va);
            grad.y += du.y * (u.x * (va - vb - vc + vd) + vc - va);

            return new Vector3(value * 0.5f + 0.5f, grad.x * 0.5f, grad.y * 0.5f);
        }

        public static float Fbm(Vector2 p, float pa, float pb, float pc, uint iter=5)
        {
            float f = 0.0f;
            float w = 0.5f;
            Vector2 g_acc = new Vector2(0.0f, 0.0f);

            for (int i = 0; i < iter; i++)
            {
                Vector3 n = Noised(p);
                float val = n.x;
                Vector2 g = new Vector2(n.y, n.z);

                g_acc += g;
                float g_s = g_acc.magnitude;

                f += w * (1.0f / (1.0f + g_s * pa)) * val;

                float px = p.x;
                float py = p.y;
                p.x = (0.8f * px + 0.6f * py) * 2.0f;
                p.y = (-0.6f * px + 0.8f * py) * 2.0f;
                
                w *= 0.5f;
            }
            return Mathf.Pow(f * pc, pb);
        }

        private static Texture2D _cachedTerrainTexture1;
        private static RenderTexture _lastRenderTexture1;
        private static Vector4 _terrainTexture1Bounds;
        private static Vector4 _lastShaderBounds1;
        private static bool _isReadbackPending1 = false;

        private static Texture2D _cachedTerrainTexture2;
        private static RenderTexture _lastRenderTexture2;
        private static Vector4 _terrainTexture2Bounds;
        private static Vector4 _lastShaderBounds2;
        private static bool _isReadbackPending2 = false;

        private static void UpdateTextureCache()
        {
            Texture globalTex1 = Shader.GetGlobalTexture("_TerrainTexture1");
            Vector4 currentBounds1 = Shader.GetGlobalVector("_TerrainTexture1_Bounds");
            if (globalTex1 != null && globalTex1 is RenderTexture rt1)
            {
                if (_lastRenderTexture1 != rt1 || _lastShaderBounds1 != currentBounds1)
                {
                    _lastRenderTexture1 = rt1;
                    _lastShaderBounds1 = currentBounds1;
                    
                    if (_cachedTerrainTexture1 == null)
                    {
                        // First time initialization: do a synchronous read so we don't have a frame with 0 height
                        _cachedTerrainTexture1 = new Texture2D(rt1.width, rt1.height, TextureFormat.RFloat, false);
                        RenderTexture currentActiveRT = RenderTexture.active;
                        RenderTexture.active = rt1;
                        _cachedTerrainTexture1.ReadPixels(new Rect(0, 0, rt1.width, rt1.height), 0, 0);
                        RenderTexture.active = currentActiveRT;
                        _terrainTexture1Bounds = currentBounds1;
                    }
                    else if (!_isReadbackPending1)
                    {
                        // Subsequent updates: do asynchronously in the background to avoid lag spikes
                        _isReadbackPending1 = true;
                        Vector4 pendingBounds1 = currentBounds1;
                        UnityEngine.Rendering.AsyncGPUReadback.Request(rt1, 0, (request) => 
                        {
                            _isReadbackPending1 = false;
                            
                            if (request.hasError) return;
                            
                            if (_cachedTerrainTexture1 == null || _cachedTerrainTexture1.width != request.width || _cachedTerrainTexture1.height != request.height)
                            {
                                if (_cachedTerrainTexture1 != null)
                                    UnityEngine.Object.DestroyImmediate(_cachedTerrainTexture1);
                                _cachedTerrainTexture1 = new Texture2D(request.width, request.height, TextureFormat.RFloat, false);
                            }
                            
                            var data = request.GetData<byte>();
                            if (data.IsCreated && _cachedTerrainTexture1 != null)
                            {
                                _cachedTerrainTexture1.LoadRawTextureData(data);
                                _terrainTexture1Bounds = pendingBounds1;
                                // Not calling Apply() because we only use this on CPU via GetPixelBilinear
                            }
                        });
                    }
                }
            }

            Texture globalTex2 = Shader.GetGlobalTexture("_TerrainTexture2");
            Vector4 currentBounds2 = Shader.GetGlobalVector("_TerrainTexture2_Bounds");
            if (globalTex2 != null && globalTex2 is RenderTexture rt2)
            {
                if (_lastRenderTexture2 != rt2 || _lastShaderBounds2 != currentBounds2)
                {
                    _lastRenderTexture2 = rt2;
                    _lastShaderBounds2 = currentBounds2;
                    
                    if (_cachedTerrainTexture2 == null)
                    {
                        // First time initialization: do a synchronous read so we don't have a frame with 0 height
                        _cachedTerrainTexture2 = new Texture2D(rt2.width, rt2.height, TextureFormat.RFloat, false);
                        RenderTexture currentActiveRT = RenderTexture.active;
                        RenderTexture.active = rt2;
                        _cachedTerrainTexture2.ReadPixels(new Rect(0, 0, rt2.width, rt2.height), 0, 0);
                        RenderTexture.active = currentActiveRT;
                        _terrainTexture2Bounds = currentBounds2;
                    }
                    else if (!_isReadbackPending2)
                    {
                        // Subsequent updates: do asynchronously in the background to avoid lag spikes
                        _isReadbackPending2 = true;
                        Vector4 pendingBounds2 = currentBounds2;
                        UnityEngine.Rendering.AsyncGPUReadback.Request(rt2, 0, (request) => 
                        {
                            _isReadbackPending2 = false;
                            
                            if (request.hasError) return;
                            
                            if (_cachedTerrainTexture2 == null || _cachedTerrainTexture2.width != request.width || _cachedTerrainTexture2.height != request.height)
                            {
                                if (_cachedTerrainTexture2 != null)
                                    UnityEngine.Object.DestroyImmediate(_cachedTerrainTexture2);
                                _cachedTerrainTexture2 = new Texture2D(request.width, request.height, TextureFormat.RFloat, false);
                            }
                            
                            var data = request.GetData<byte>();
                            if (data.IsCreated && _cachedTerrainTexture2 != null)
                            {
                                _cachedTerrainTexture2.LoadRawTextureData(data);
                                _terrainTexture2Bounds = pendingBounds2;
                                // Not calling Apply() because we only use this on CPU via GetPixelBilinear
                            }
                        });
                    }
                }
            }
        }

        private static float SampleBicubicBSpline(Texture2D tex, Vector2 uv)
        {
            if (tex == null) return 0f;

            Vector2 texSize = new Vector2(tex.width, tex.height);
            Vector2 invTexSize = new Vector2(1.0f / texSize.x, 1.0f / texSize.y);

            uv = new Vector2(uv.x * texSize.x, uv.y * texSize.y) - new Vector2(0.5f, 0.5f);

            Vector2 f = new Vector2(uv.x - Mathf.Floor(uv.x), uv.y - Mathf.Floor(uv.y));
            Vector2 i = new Vector2(Mathf.Floor(uv.x), Mathf.Floor(uv.y));

            Vector2 f2 = new Vector2(f.x * f.x, f.y * f.y);
            Vector2 f3 = new Vector2(f2.x * f.x, f2.y * f.y);

            Vector2 w0 = (1.0f / 6.0f) * new Vector2((1.0f - f.x) * (1.0f - f.x) * (1.0f - f.x), (1.0f - f.y) * (1.0f - f.y) * (1.0f - f.y));
            Vector2 w1 = (1.0f / 6.0f) * new Vector2(3.0f * f3.x - 6.0f * f2.x + 4.0f, 3.0f * f3.y - 6.0f * f2.y + 4.0f);
            Vector2 w2 = (1.0f / 6.0f) * new Vector2(-3.0f * f3.x + 3.0f * f2.x + 3.0f * f.x + 1.0f, -3.0f * f3.y + 3.0f * f2.y + 3.0f * f.y + 1.0f);
            Vector2 w3 = (1.0f / 6.0f) * f3;

            Vector2 g0 = w0 + w1;
            Vector2 g1 = w2 + w3;

            Vector2 h0 = new Vector2(w1.x / g0.x - 1.0f, w1.y / g0.y - 1.0f);
            Vector2 h1 = new Vector2(w3.x / g1.x + 1.0f, w3.y / g1.y + 1.0f);

            Vector2 p0 = new Vector2((i.x + h0.x + 0.5f) * invTexSize.x, (i.y + h0.y + 0.5f) * invTexSize.y);
            Vector2 p1 = new Vector2((i.x + h1.x + 0.5f) * invTexSize.x, (i.y + h1.y + 0.5f) * invTexSize.y);

            float result = 
                g0.y * (g0.x * tex.GetPixelBilinear(p0.x, p0.y).r +
                        g1.x * tex.GetPixelBilinear(p1.x, p0.y).r) +
                g1.y * (g0.x * tex.GetPixelBilinear(p0.x, p1.y).r +
                        g1.x * tex.GetPixelBilinear(p1.x, p1.y).r);

            return result;
        }

        private static float GetTextureFlattenAmount(Vector2 worldXZ, Texture2D tex, Vector4 bounds, float innerDist, float outerDist)
        {
            Vector2 center = new Vector2(bounds.x, bounds.y);
            float size = bounds.z;
            if (size == 0.0f) size = 4000.0f;
            
            Vector2 uv = (worldXZ - center) / size + new Vector2(0.5f, 0.5f);
            float dist = SampleBicubicBSpline(tex, uv);
            
            if (outerDist <= 0.001f) {
                return dist < 0.01f ? 1.0f : 0.0f;
            }
            
            float t = Mathf.Clamp01((dist - innerDist) / (outerDist - innerDist));
            return 1.0f - Mathf.SmoothStep(0.0f, 1.0f, t);
        }

        public static float GetRiverDistance(Vector2 worldXZ)
        {
            UpdateTextureCache();
            if (_cachedTerrainTexture1 == null) return float.MaxValue;
            
            Vector4 bounds = _terrainTexture1Bounds;
            Vector2 center = new Vector2(bounds.x, bounds.y);
            float size = bounds.z;
            if (size == 0.0f) size = 4000.0f;
            
            Vector2 uv = (worldXZ - center) / size + new Vector2(0.5f, 0.5f);
            return SampleBicubicBSpline(_cachedTerrainTexture1, uv);
        }

        private static float Smax(float a, float b, float k) 
        {
            float fac = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(a, b, fac) + k * fac * (1f - fac);
        }

        private static float Smin(float a, float b, float k) 
        {
            float fac = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, fac) - k * fac * (1f - fac);
        }


        public static float GetTerrainHeightOriginal(Vector2 worldXZ, int iterations=FbmIterations)
        {
            if (!_initialized)
                AutoUpdateProperties();
            Vector2 p = worldXZ * _noiseScale;
            float h = Fbm(p, _pa, _pb, _pc, (uint)iterations) * _heightMax - _pe;
            h = Smax(h, 10f, 10f);
            h = Smin(h, _heightMax, _pd);
            return h;
        }

        public static float GetTerrainHeight(Vector2 worldXZ)
        {
            float flattenAmount1 = GetTextureFlattenAmount(worldXZ, _cachedTerrainTexture1, _terrainTexture1Bounds, 5.0f, 20.0f);
            float flattenAmount2 = GetTextureFlattenAmount(worldXZ, _cachedTerrainTexture2, _terrainTexture2Bounds, 0.0f, 100.0f);
            
            float h = GetTerrainHeightOriginal(worldXZ);
            h = Mathf.Lerp(h, 10.0f, flattenAmount2);
            h = Mathf.Lerp(h, 0.0f, flattenAmount1);
            return h;
        }
    }
}
