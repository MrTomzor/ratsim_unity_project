// HouseGeneratorModule.cs
using UnityEngine;

public class HouseGeneratorModule : MonoBehaviour, IWorldgenNodeListener
{
    [SerializeField] private GameObject[] housePrefabs;

    public void OnNodeLoaded(HierarchicalWorldgenNode node)
    {
        if (node.Level != 2) return;

        BoxWorldgenNode houseNode = (BoxWorldgenNode)node;
        System.Random rng = new System.Random(node.Seed);

        int prefabIndex = rng.Next(0, housePrefabs.Length);
        GameObject go = Instantiate(
            housePrefabs[prefabIndex],
            houseNode.Bbox.center,
            houseNode.transform.rotation,
            node.transform   // parented to node so destroy is automatic
        );

        go.transform.localScale = new Vector3(
            houseNode.Bbox.size.x,
            houseNode.Bbox.size.y,
            houseNode.Bbox.size.z
        );
    }

    public void OnNodeUnloaded(HierarchicalWorldgenNode node)
    {
        if (node.Level != 2) return;
        // geometry is parented to node.transform so it gets destroyed
        // when the node GameObject is destroyed by CityPlannerModule
    }
}