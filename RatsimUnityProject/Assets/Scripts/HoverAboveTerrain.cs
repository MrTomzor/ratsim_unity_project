using UnityEngine;
using ClipmapTerrain;

[ExecuteAlways]
public class HoverAboveTerrain : MonoBehaviour
{
    [Tooltip("The distance above the terrain to hover.")]
    public float hoverHeight = 1.0f;

    void Update()
    {
        // Get current X and Z position
        float x = transform.position.x;
        float z = transform.position.z;
        
        // Calculate the terrain height at this position
        float terrainHeight = TerrainNoise.GetTerrainHeight(new Vector2(x, z));
        
        // Set the Y position to the terrain height plus the hover height
        transform.position = new Vector3(x, terrainHeight + hoverHeight, z);
    }
}
