// TreeGeneratorModule.cs
using UnityEngine;

public class TreeGeneratorModule : MonoBehaviour, IWorldgenNodeListener
{
    [SerializeField] private GameObject[] treePrefabs;
    [SerializeField] private float treeDensity = 0.05f;
    [SerializeField] private LayerMask planningLayerMask;

    public void OnNodeLoaded(HierarchicalWorldgenNode node)
    {
        if (node.Level != 2) return;

        BoxWorldgenNode boxNode = (BoxWorldgenNode)node;
        System.Random rng = new System.Random(node.Seed ^ 999983); // offset seed so trees don't correlate with houses

        float area = boxNode.Bbox.size.x * boxNode.Bbox.size.z;
        int treeCount = (int)(area * treeDensity);

        // parent all trees to an empty child of the node for easy cleanup
        GameObject treeContainer = new GameObject("Trees");
        treeContainer.transform.SetParent(node.transform);

        for (int i = 0; i < treeCount; i++)
        {
            Vector3 position = WorldgenUtils.RandomPointInBounds(boxNode.Bbox, rng);

            if (Physics.CheckSphere(position, 0.5f, planningLayerMask))
                continue;

            int prefabIndex = rng.Next(0, treePrefabs.Length);
            Instantiate(
                treePrefabs[prefabIndex],
                position,
                Quaternion.Euler(0, (float)(rng.NextDouble() * 360f), 0),
                treeContainer.transform
            );
        }
    }

    public void OnNodeUnloaded(HierarchicalWorldgenNode node)
    {
        if (node.Level != 2) return;
        // treeContainer is parented to node.transform, destroyed automatically
    }
}