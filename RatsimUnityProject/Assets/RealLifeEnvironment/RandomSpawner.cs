using UnityEngine;
using System.Collections.Generic;

public class RandomSpawner : MonoBehaviour
{
    [Tooltip("Fixed height at which objects will be spawned")]
    public float y = 0f;

    [Tooltip("Size of the spawning area (X and Z axis). The spawner's position is the center.")]
    public Vector2 xzBounds = new Vector2(10f, 10f);

    [Tooltip("Number of objects to spawn")]
    public int n_spawns = 10;

    [Tooltip("List of objects to spawn")]
    public List<GameObject> objectsToSpawn;

    [Tooltip("List of weights for probability to spawn each object. Must match the length of objectsToSpawn.")]
    public List<float> spawnWeights;

    void Awake()
    {
        SpawnObjects();
    }

    public void SpawnObjects()
    {
        if (objectsToSpawn == null || objectsToSpawn.Count == 0)
        {
            Debug.LogWarning("RandomSpawner: No objects to spawn.");
            return;
        }

        if (spawnWeights == null || spawnWeights.Count != objectsToSpawn.Count)
        {
            Debug.LogWarning("RandomSpawner: Length of spawnWeights does not match length of objectsToSpawn.");
            return;
        }

        float totalWeight = 0f;
        foreach (float weight in spawnWeights)
        {
            totalWeight += weight;
        }

        if (totalWeight <= 0f)
        {
            Debug.LogWarning("RandomSpawner: Total weight must be greater than 0.");
            return;
        }

        for (int i = 0; i < n_spawns; i++)
        {
            GameObject objToSpawn = GetRandomObjectByWeight(totalWeight);
            if (objToSpawn != null)
            {
                Vector3 spawnPos = GetRandomPosition();
                // Spawning with a random Y rotation for variety
                Instantiate(objToSpawn, spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0), transform);
            }
        }
    }

    private GameObject GetRandomObjectByWeight(float totalWeight)
    {
        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        for (int i = 0; i < objectsToSpawn.Count; i++)
        {
            currentWeight += spawnWeights[i];
            if (randomValue <= currentWeight)
            {
                return objectsToSpawn[i];
            }
        }

        return objectsToSpawn[objectsToSpawn.Count - 1]; // Fallback in case of floating point inaccuracies
    }

    private Vector3 GetRandomPosition()
    {
        // Spawning centered around the transform's X and Z position
        float spawnX = transform.position.x + Random.Range(-xzBounds.x / 2f, xzBounds.x / 2f);
        float spawnZ = transform.position.z + Random.Range(-xzBounds.y / 2f, xzBounds.y / 2f);
        
        return new Vector3(spawnX, y, spawnZ);
    }

    private void OnDrawGizmosSelected()
    {
        // Draw a visual representation of the spawn area in the editor
        Gizmos.color = new Color(0, 1, 0, 0.5f);
        Vector3 center = new Vector3(transform.position.x, y, transform.position.z);
        Vector3 size = new Vector3(xzBounds.x, 0.1f, xzBounds.y);
        Gizmos.DrawWireCube(center, size);
    }
}
