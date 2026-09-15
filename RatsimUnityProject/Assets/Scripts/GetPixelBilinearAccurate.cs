using UnityEngine;

/// <summary>
/// Globally accessible utility for bilinear texture sampling on the CPU
/// with an automatic 0.5 pixel (texel) shift to align with GPU texture sampling conventions.
/// </summary>
public static class GetPixelBilinearAccurate
{
    /// <summary>
    /// Samples a Texture2D using bilinear filtering with an automatic 0.5 pixel (texel) shift
    /// to align CPU UV coordinates with GPU texture sampling conventions.
    /// </summary>
    /// <param name="tex">The texture to sample.</param>
    /// <param name="u">Horizontal UV coordinate in [0, 1] range (GPU convention).</param>
    /// <param name="v">Vertical UV coordinate in [0, 1] range (GPU convention).</param>
    public static Color Get(Texture2D tex, float u, float v)
    {
        if (tex == null)
            throw new System.ArgumentNullException(nameof(tex), "Cannot sample a null Texture2D.");

        // Unity's GetPixelBilinear treats (0, 0) as the center of the first pixel,
        // whereas GPU texture samplers treat (0, 0) as the bottom-left corner of the first pixel
        // and (0.5/w, 0.5/h) as its center.
        // Subtract half a texel so CPU UVs match the GPU coordinate space.
        u -= 0.5f / tex.width;
        v -= 0.5f / tex.height;

        return tex.GetPixelBilinear(u, v);
    }

    /// <summary>
    /// Samples a Texture2D using bilinear filtering with an automatic 0.5 pixel (texel) shift
    /// to align CPU UV coordinates with GPU texture sampling conventions.
    /// </summary>
    /// <param name="tex">The texture to sample.</param>
    /// <param name="uv">UV coordinates in [0, 1] range (GPU convention).</param>
    /// <returns>Interpolated Color at uv.</returns>
    public static Color Get(Texture2D tex, Vector2 uv)
    {
        return Get(tex, uv.x, uv.y);
    }

    /// <summary>
    /// Alias for Get(tex, u, v).
    /// </summary>
    public static Color Sample(Texture2D tex, float u, float v)
    {
        return Get(tex, u, v);
    }

    /// <summary>
    /// Alias for Get(tex, uv).
    /// </summary>
    public static Color Sample(Texture2D tex, Vector2 uv)
    {
        return Get(tex, uv.x, uv.y);
    }
}

/// <summary>
/// Extension methods for Texture2D to allow calling tex.GetPixelBilinearAccurate(u, v) directly.
/// </summary>
public static class GetPixelBilinearAccurateExtensions
{
    /// <summary>
    /// Samples a Texture2D using bilinear filtering with an automatic 0.5 pixel (texel) shift
    /// to align CPU UV coordinates with GPU texture sampling conventions.
    /// </summary>
    /// <param name="tex">The texture to sample.</param>
    /// <param name="u">Horizontal UV coordinate in [0, 1] range (GPU convention).</param>
    /// <param name="v">Vertical UV coordinate in [0, 1] range (GPU convention).</param>
    /// <returns>Interpolated Color at (u, v).</returns>
    public static Color GetPixelBilinearAccurate(this Texture2D tex, float u, float v)
    {
        return global::GetPixelBilinearAccurate.Get(tex, u, v);
    }

    /// <summary>
    /// Samples a Texture2D using bilinear filtering with an automatic 0.5 pixel (texel) shift
    /// to align CPU UV coordinates with GPU texture sampling conventions.
    /// </summary>
    /// <param name="tex">The texture to sample.</param>
    /// <param name="uv">UV coordinates in [0, 1] range (GPU convention).</param>
    /// <returns>Interpolated Color at uv.</returns>
    public static Color GetPixelBilinearAccurate(this Texture2D tex, Vector2 uv)
    {
        return global::GetPixelBilinearAccurate.Get(tex, uv.x, uv.y);
    }
}

