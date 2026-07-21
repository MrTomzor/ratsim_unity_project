using UnityEngine;
using System.Collections;
using System.IO;
using ClipmapTerrain;

[RequireComponent(typeof(Camera))]
public class DatasetSampler : MonoBehaviour
{
    public Vector3 targetPosition;
    public bool heightIsRelative;
    public Vector3 targetRotation;
    public bool rotationIsFlushWithTerrain;
    public uint waitNFrames;
    public float offsetX;
    public float offsetZ;
    public uint NOffsetX;
    public uint NOffsetZ;
    public Vector2Int imageResolution = new Vector2Int(1920, 1080);
    public string saveFolder = "Dataset";

    private Vector3 _lastTargetPosition;
    private Vector3 _lastTargetRotation;

    void Start()
    {
        transform.position = targetPosition;
        transform.eulerAngles = targetRotation;
        
        _lastTargetPosition = targetPosition;
        _lastTargetRotation = targetRotation;
    }

    void Update()
    {
        // Only force camera position/rotation if they are explicitly changed in the inspector
        if (targetPosition != _lastTargetPosition)
        {
            transform.position = targetPosition;
            _lastTargetPosition = targetPosition;
        }

        if (targetRotation != _lastTargetRotation)
        {
            transform.eulerAngles = targetRotation;
            _lastTargetRotation = targetRotation;
        }
    }

    public void Sample()
    {
        StartCoroutine(SampleGridRoutine());
    }

    private IEnumerator SampleGridRoutine()
    {
        string fullPath = Path.Combine(Application.dataPath, saveFolder);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        Camera cam = GetComponent<Camera>();

        for (uint z = 0; z <= NOffsetZ; z++)
        {
            bool isEvenRow = (z % 2 == 0);
            
            for (uint i = 0; i <= NOffsetX; i++)
            {
                // Traverse forwards on even rows, backwards on odd rows
                uint x = isEvenRow ? i : (NOffsetX - i);
                

                float finalX = targetPosition.x + x * offsetX;
                float finalZ = targetPosition.z + z * offsetZ;
                
                Vector3 pos, rot;
                GetPose(finalX, finalZ, out pos, out rot);
                transform.position = pos;
                transform.eulerAngles = rot;
                
                // Wait for the specified number of frames
                for (uint f = 0; f < waitNFrames; f++)
                {
                    yield return null;
                }
                
                // Let Unity naturally render the frame into our RenderTexture
                RenderTexture rt = new RenderTexture(imageResolution.x, imageResolution.y, 24);
                cam.targetTexture = rt;
                
                // Wait for the rendering pipeline to finish for this frame
                yield return new WaitForEndOfFrame();
                
                Texture2D screenShot = new Texture2D(imageResolution.x, imageResolution.y, TextureFormat.RGB24, false);
                
                RenderTexture.active = rt;
                screenShot.ReadPixels(new Rect(0, 0, imageResolution.x, imageResolution.y), 0, 0);
                
                cam.targetTexture = null;
                RenderTexture.active = null;
                Destroy(rt);

                byte[] bytes = screenShot.EncodeToPNG();
                string filename = Path.Combine(fullPath, $"sample_{z}_{x}.png");
                File.WriteAllBytes(filename, bytes);
                Destroy(screenShot);
                
                Debug.Log($"Dataset sampled and saved at {transform.position} to {filename}");
            }
        }
    }

    private void GetPose(float x, float z, out Vector3 pos, out Vector3 rot)
    {
        float h0 = TerrainNoise.GetTerrainHeight(new Vector2(x, z));
        pos = new Vector3(x, h0 + targetPosition.y, z);

        if (!rotationIsFlushWithTerrain)
        {
            rot = targetRotation;
            return;
        }

        float eps = 0.01f;
        float yawRad = targetRotation.y * Mathf.Deg2Rad;
        Vector2 fwdDir = new Vector2(Mathf.Sin(yawRad), Mathf.Cos(yawRad));
        Vector2 rightDir = new Vector2(Mathf.Cos(yawRad), -Mathf.Sin(yawRad));

        float hFwd = TerrainNoise.GetTerrainHeight(new Vector2(x + fwdDir.x * eps, z + fwdDir.y * eps));
        float hRight = TerrainNoise.GetTerrainHeight(new Vector2(x + rightDir.x * eps, z + rightDir.y * eps));

        float gradFwd = (hFwd - h0) / eps;
        float gradRight = (hRight - h0) / eps;

        float pitchOffset = -Mathf.Atan(gradFwd) * Mathf.Rad2Deg;
        float rollOffset = Mathf.Atan(gradRight) * Mathf.Rad2Deg;

        rot = new Vector3(targetRotation.x + pitchOffset, targetRotation.y, targetRotation.z + rollOffset);
    }

    public void GenerateMatrices()
    {
        string fullPath = Path.Combine(Application.dataPath, saveFolder);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        string filepath = Path.Combine(fullPath, "camera_matrices.txt");
        Camera cam = GetComponent<Camera>();

        // Store original to restore later
        Vector3 originalPos = transform.position;
        Vector3 originalRot = transform.eulerAngles;

        using (StreamWriter writer = new StreamWriter(filepath))
        {
            for (uint z = 0; z <= NOffsetZ; z++)
            {
                bool isEvenRow = (z % 2 == 0);
                
                for (uint i = 0; i <= NOffsetX; i++)
                {
                    uint x = isEvenRow ? i : (NOffsetX - i);
                    
                    float finalX = targetPosition.x + x * offsetX;
                    float finalZ = targetPosition.z + z * offsetZ;
                    
                    Vector3 pos, rot;
                    GetPose(finalX, finalZ, out pos, out rot);
                    transform.position = pos;
                    transform.eulerAngles = rot;
                    
                    // Force transform update just in case
                    transform.hasChanged = true;
                    
                    Matrix4x4 c2w = cam.cameraToWorldMatrix;
                    
                    writer.WriteLine($"sample_{z}_{x}.png");
                    writer.WriteLine(c2w.ToString());
                    writer.WriteLine(); // separator
                }
            }
        }

        // Restore
        transform.position = originalPos;
        transform.eulerAngles = originalRot;
        
        Debug.Log($"Camera matrices saved to {filepath}");
    }
}
