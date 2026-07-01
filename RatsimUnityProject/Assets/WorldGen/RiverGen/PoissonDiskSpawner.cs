using UnityEngine;
using System.Collections.Generic;

public class PoissonDiskSpawner : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("The minimum distance between any two points.")]
    public float minDistance = 1.5f;
    [Tooltip("The initial point where the cluster starts growing.")]
    public Vector2 startingXZ = Vector2.zero;
    [Tooltip("The point that the cluster will try to grow towards first.")]
    public Vector2 gravitateTowardsPoint = Vector2.zero;
    [Tooltip("Number of top closest points to randomly select from (adds organic variation to the pathfinding).")]
    public int topNRandomness = 3;
    [Tooltip("The exact number of points to generate.")]
    public int pointsToGenerate = 100;
    [Tooltip("Number of attempts to find a neighbor before giving up (30 is standard).")]
    public int maxAttempts = 30;

    [Header("Visuals")]
    [Tooltip("Radius of the generated spheres.")]
    public float sphereRadius = 0.5f;

    [HideInInspector]
    public List<Vector3> generatedPoints = new List<Vector3>();
    [HideInInspector]
    public List<int> generatedParents = new List<int>();

    [ContextMenu("Generate Points")]
    public void GenerateAndSpawn()
    {
        // Clear old spheres if any still exist from the old version of the script
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        // Store points instead of instantiating game objects
        generatedPoints = Generate(minDistance, startingXZ.x, startingXZ.y, pointsToGenerate, maxAttempts, gravitateTowardsPoint, topNRandomness, out generatedParents);
    }

    [ContextMenu("Optimize Points")]
    public void OptimizePoints()
    {
        if (generatedPoints == null || generatedPoints.Count == 0) return;

        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        WorldGen.RiverGen.LocalMinimumOptimizer optimizer = GetComponent<WorldGen.RiverGen.LocalMinimumOptimizer>();

        for (int i = 0; i < generatedPoints.Count; i++)
        {
            Vector3 worldPos = transform.position + generatedPoints[i];
            Vector2 worldXZ = new Vector2(worldPos.x, worldPos.z);
            Vector2 optWorldXZ;

            if (optimizer != null)
            {
                optWorldXZ = optimizer.GetOptimizedXZ(worldXZ);
            }
            else
            {
                optWorldXZ = WorldGen.RiverGen.LocalMinimumOptimizer.GetOptimizedXZ(
                    worldXZ, 63.5f, 0.99f, 1000, 0.001f, 1, 0.94f, 0.999f, 0.1f
                );
            }

            float newHeight = ClipmapTerrain.TerrainNoise.GetTerrainHeightOriginal(new Vector2(optWorldXZ.x, optWorldXZ.y));
            Vector3 optWorldPos = new Vector3(optWorldXZ.x, newHeight, optWorldXZ.y);
            
            generatedPoints[i] = optWorldPos - transform.position;
        }

        sw.Stop();
        Debug.Log($"Optimized {generatedPoints.Count} points to their local minimums in {sw.ElapsedMilliseconds} ms.");
#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif
    }

    [Header("Gizmo View")]
    [Tooltip("How large the points should appear on your screen (independent of zoom distance).")]
    public float markerScreenSize = 0.05f;

    private void OnDrawGizmos()
    {
        if (generatedPoints == null || generatedPoints.Count == 0) return;

        for (int i = 0; i < generatedPoints.Count; i++)
        {
            Vector3 worldPos = transform.position + generatedPoints[i];
            
            Gizmos.color = new Color(0f, 1f, 1f, 0.7f); // Cyan, slightly transparent
#if UNITY_EDITOR
            // GetHandleSize scales the size dynamically based on distance to the Scene View camera
            float size = UnityEditor.HandleUtility.GetHandleSize(worldPos) * markerScreenSize;
            Gizmos.DrawSphere(worldPos, size);
#else
            Gizmos.DrawSphere(worldPos, sphereRadius);
#endif

            // Draw red line to parent
            if (generatedParents != null && i < generatedParents.Count)
            {
                int parentIndex = generatedParents[i];
                if (parentIndex >= 0 && parentIndex < generatedPoints.Count)
                {
                    Gizmos.color = Color.red;
                    Vector3 parentWorldPos = transform.position + generatedPoints[parentIndex];
                    Gizmos.DrawLine(parentWorldPos, worldPos);
                }
            }
        }
    }

    /// <summary>
    /// Generates points using Bridson's expanding algorithm.
    /// Uses a custom Min-Heap Priority Queue for blazing fast O(N log N) circular expansion.
    /// </summary>
    private static List<Vector3> Generate(float minDistance, float startX, float startZ, int nPoints, int k, Vector2 gravitateTowards, int topNRandomness, out List<int> parents)
    {
        List<Vector3> points = new List<Vector3>();
        parents = new List<int>();
        if (nPoints <= 0) return points;

        float cellSize = minDistance / Mathf.Sqrt(2f);
        Dictionary<Vector2Int, int> spatialGrid = new Dictionary<Vector2Int, int>();
        
        // Use our custom Min-Heap instead of a List
        MinHeap activeIndices = new MinHeap();

        Vector3 startPoint = new Vector3(startX, 0f, startZ);
        Vector3 gravitatePoint = new Vector3(gravitateTowards.x, 0f, gravitateTowards.y);
        
        points.Add(startPoint);
        parents.Add(-1); // Root has no parent
        
        float initialDist = (startPoint - gravitatePoint).sqrMagnitude;
        activeIndices.Enqueue(0, initialDist); // distance to gravitate point
        spatialGrid[GetGridCoords(startPoint, cellSize)] = 0;

        float minSq = minDistance * minDistance;

        topNRandomness = Mathf.Max(1, topNRandomness);

        while (activeIndices.Count > 0 && points.Count < nPoints)
        {
            int nToExtract = Mathf.Min(topNRandomness, activeIndices.Count);
            List<MinHeap.HeapNode> topNodes = new List<MinHeap.HeapNode>();
            for (int i = 0; i < nToExtract; i++)
            {
                topNodes.Add(activeIndices.DequeueNode());
            }

            int chosenIndex = Random.Range(0, topNodes.Count);
            MinHeap.HeapNode chosenNode = topNodes[chosenIndex];

            // Put the unchosen ones back immediately
            for (int i = 0; i < topNodes.Count; i++)
            {
                if (i != chosenIndex)
                {
                    activeIndices.Enqueue(topNodes[i].Item, topNodes[i].Priority);
                }
            }

            int pointIndex = chosenNode.Item;
            Vector3 center = points[pointIndex];

            bool foundValidNeighbor = false;

            for (int i = 0; i < k; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float radius = minDistance; // Exactly at minDistance

                Vector3 candidate = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    0f,
                    center.z + Mathf.Sin(angle) * radius
                );

                if (IsValid(candidate, spatialGrid, points, cellSize, minSq))
                {
                    points.Add(candidate);
                    parents.Add(pointIndex); // Record the parent who spawned this point
                    
                    // Enqueue the new candidate with its distance as the priority. O(log A) operation!
                    float distToGravitate = (candidate - gravitatePoint).sqrMagnitude;
                    activeIndices.Enqueue(points.Count - 1, distToGravitate);
                    
                    spatialGrid[GetGridCoords(candidate, cellSize)] = points.Count - 1;
                    
                    foundValidNeighbor = true;
                    break;
                }
            }

            // If we found a neighbor, this point might still have room for more, so we put it back in the active queue.
            // If we didn't find a neighbor, it's surrounded, so we simply don't put it back (it was already dequeued).
            if (foundValidNeighbor)
            {
                activeIndices.Enqueue(chosenNode.Item, chosenNode.Priority);
            }
        }

        return points;
    }

    private static bool IsValid(Vector3 candidate, Dictionary<Vector2Int, int> grid, List<Vector3> points, float cellSize, float minSqDist)
    {
        Vector2Int cell = GetGridCoords(candidate, cellSize);

        for (int x = cell.x - 2; x <= cell.x + 2; x++)
        {
            for (int y = cell.y - 2; y <= cell.y + 2; y++)
            {
                if (grid.TryGetValue(new Vector2Int(x, y), out int existingPointIndex))
                {
                    Vector3 existing = points[existingPointIndex];
                    float dx = candidate.x - existing.x;
                    float dz = candidate.z - existing.z;
                    float sqDist = (dx * dx) + (dz * dz);

                    if (sqDist < minSqDist) return false;
                }
            }
        }
        return true;
    }

    private static Vector2Int GetGridCoords(Vector3 point, float cellSize)
    {
        return new Vector2Int(
            Mathf.FloorToInt(point.x / cellSize),
            Mathf.FloorToInt(point.z / cellSize)
        );
    }

    // ---------------------------------------------------------
    // CUSTOM MIN-HEAP PRIORITY QUEUE
    // Embedded here to guarantee compilation on all Unity versions
    // ---------------------------------------------------------
    private class MinHeap
    {
        public struct HeapNode
        {
            public int Item;
            public float Priority;
            public HeapNode(int item, float priority) { Item = item; Priority = priority; }
        }

        private List<HeapNode> elements = new List<HeapNode>();

        public int Count => elements.Count;

        public void Enqueue(int item, float priority)
        {
            elements.Add(new HeapNode(item, priority));
            BubbleUp(elements.Count - 1);
        }

        public int Peek()
        {
            return elements[0].Item;
        }

        public int Dequeue()
        {
            return DequeueNode().Item;
        }

        public HeapNode DequeueNode()
        {
            HeapNode result = elements[0];
            elements[0] = elements[elements.Count - 1];
            elements.RemoveAt(elements.Count - 1);
            if (elements.Count > 0) BubbleDown(0);
            return result;
        }

        private void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;
                if (elements[parentIndex].Priority <= elements[index].Priority) break;
                
                HeapNode temp = elements[index];
                elements[index] = elements[parentIndex];
                elements[parentIndex] = temp;
                
                index = parentIndex;
            }
        }

        private void BubbleDown(int index)
        {
            int lastIndex = elements.Count - 1;
            while (true)
            {
                int leftChildIndex = index * 2 + 1;
                if (leftChildIndex > lastIndex) break;

                int minChildIndex = leftChildIndex;
                int rightChildIndex = leftChildIndex + 1;

                if (rightChildIndex <= lastIndex && elements[rightChildIndex].Priority < elements[leftChildIndex].Priority)
                {
                    minChildIndex = rightChildIndex;
                }

                if (elements[index].Priority <= elements[minChildIndex].Priority) break;

                HeapNode temp = elements[index];
                elements[index] = elements[minChildIndex];
                elements[minChildIndex] = temp;

                index = minChildIndex;
            }
        }
    }
}
