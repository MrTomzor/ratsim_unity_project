#ifndef CLIPMAP_DISPLACEMENT_INCLUDED
#define CLIPMAP_DISPLACEMENT_INCLUDED

// Declare clipmap/noise variables so they don't need to be redeclared in standard shaders
half _HeightMin;
float _Level;

half _HeightMax;

#include "RealTerrainHeight.cginc"



// Displaces the vertices of the clipmap rings on the GPU and outputs the height ratio for coloring/blending.
void DisplaceClipmapVertex(inout appdata_full v, out float heightRatio)
{
    // Calculate true world position of the vertex
    float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
    
    float h_fine = GetTerrainHeight(worldPos.xz);
    
    // Calculate scale of the current LOD from the transform matrix
    float scale = length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x));
    
    // Calculate coordinates for the coarser grid (which has 2x the local spacing).
    // To be completely immune to LOD drifting, we evaluate the coarse grid in pure world space.
    // Since coarser LODs snap to multiples of 2*scale, their vertices always lie exactly on this world grid.
    float C = 2.0 * scale;
    float2 worldA = floor(worldPos.xz / C) * C;
    float2 worldB = worldA + float2(C, 0.0);
    float2 worldC = worldA + float2(0.0, C);
    float2 worldD = worldA + float2(C, C);
    
    // Sample heights at the coarser grid points
    float hA = GetTerrainHeight(worldA);
    float hB = GetTerrainHeight(worldB);
    float hC = GetTerrainHeight(worldC);
    float hD = GetTerrainHeight(worldD);
    
    // Instead of bilinear interpolation (which curves the quad), we must use exact triangle 
    // interpolation to perfectly match the physical geometry of the coarser LOD block.
    // Our mesh generator splits quads along the diagonal: Bottom-Left to Top-Right (x+y=1).
    float2 fracPos = frac(worldPos.xz / C);
    float h_coarse;
    if (fracPos.x + fracPos.y < 1.0)
    {
        // Bottom-Left triangle (A, B, C)
        // Height = hA + x*(hB - hA) + y*(hC - hA)
        h_coarse = hA + fracPos.x * (hB - hA) + fracPos.y * (hC - hA);
    }
    else
    {
        // Top-Right triangle (D, C, B)
        // Height = hD + (1-x)*(hC - hD) + (1-y)*(hB - hD)
        h_coarse = hD + (1.0 - fracPos.x) * (hC - hD) + (1.0 - fracPos.y) * (hB - hD);
    }
    
    // Create a transition region t to smoothly blend from high-res geometry to coarse geometry.
    // To completely prevent Z-fighting in the overlap, the finer LOD MUST be 100% h_coarse 
    // anywhere it physically overlaps the coarser LOD.
    // Max overlap due to drift is 5 quads. We force the outer 6 quads to be t=1.
    // 6 quads / 64 half-width = 0.09375. So t=1 starts at d = 0.90625.
    // We use the 10 quads inside that (d=0.75 to 0.90625) to smoothly morph.
    float d = max(abs(v.texcoord.x - 0.5), abs(v.texcoord.y - 0.5)) * 2.0;
    float t = saturate((d - 0.75) / 0.15625);
    
    // Snap the overlapping and edge vertices to the coarse height
    float h = lerp(h_fine, h_coarse, t);

    // Apply a microscopic downward offset based on the LOD level.
    // This prevents Z-fighting in the overlap region between rings.
    // The higher res inner ring will sit perfectly 0.05 units above the coarser outer ring.
    h -= _Level * 0.05;
    
    // Displace vertex
    // Note: We're applying the displacement in object space Y
    // Assuming the object's local Y is unscaled and points up
    v.vertex.y = h;
    
    // Calculate Central Differences for accurate normals independent of LOD geometry stitching
    float delta = 0.5;
    
    float2 posLeft = worldPos.xz + float2(-delta, 0.0);
    float2 posRight = worldPos.xz + float2(delta, 0.0);
    float2 posDown = worldPos.xz + float2(0.0, -delta);
    float2 posUp = worldPos.xz + float2(0.0, delta);

    float hLeft = GetTerrainHeight(posLeft);
    float hRight = GetTerrainHeight(posRight);
    float hDown = GetTerrainHeight(posDown);
    float hUp = GetTerrainHeight(posUp);
    
    // The normal vector based on central difference slopes is a WORLD space normal.
    // Because the GameObjects have non-uniform scale (S, 1, S), Unity will apply the 
    // inverse-transpose of the scale matrix (1/S, 1, 1/S) when converting to world space.
    // To cancel this out and maintain identical shadows across all LODs, we pre-multiply X and Z by scale.
    v.normal = normalize(float3((hLeft - hRight) * scale, 2.0 * delta, (hDown - hUp) * scale));
    
    heightRatio = saturate((h - _HeightMin) / (_HeightMax - _HeightMin));
}

#endif // CLIPMAP_DISPLACEMENT_INCLUDED
