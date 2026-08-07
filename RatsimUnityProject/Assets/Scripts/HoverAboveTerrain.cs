using UnityEngine;
using UnityEngine.InputSystem;
using ClipmapTerrain;

[ExecuteAlways]
public class HoverAboveTerrain : MonoBehaviour
{
    [Tooltip("The distance above the terrain to hover.")]
    public float hoverHeight = 1.0f;
    [Tooltip("Speed at which Q/E keys adjust the hover height.")]
    public float heightAdjustSpeed = 5.0f;

    void Update()
    {
        if (Application.isPlaying && Keyboard.current != null)
        {
            if (Keyboard.current.eKey.isPressed)
                hoverHeight += heightAdjustSpeed * Time.deltaTime;
            if (Keyboard.current.qKey.isPressed)
                hoverHeight -= heightAdjustSpeed * Time.deltaTime;
        }

        // Get current X and Z position
        float x = transform.position.x;
        float z = transform.position.z;
        
        // Calculate the physical triangulated terrain height at this position
        float terrainHeight = RealLifeEnvironment.RealTerrainHeight.GetTriangulatedHeight(new Vector2(x, z));
        
        // Set the Y position to the terrain height plus the hover height
        transform.position = new Vector3(x, terrainHeight + hoverHeight, z);
    }
}
