// MegachunkPlannerModule.cs
using UnityEngine;
using System.Collections.Generic;

public class MegachunkPlannerModule : MonoBehaviour, IWorldgenNodeListener
{
    [SerializeField] private GameObject cityNodePrefab;
    [SerializeField] private int minCities = 3;
    [SerializeField] private int maxCities = 8;
    [SerializeField] private Vector2 cityMinSize = new Vector2(30f, 30f);
    [SerializeField] private Vector2 cityMaxSize = new Vector2(80f, 80f);
    [SerializeField] private int placementAttempts = 10;
    [SerializeField] private LayerMask planningLayerMask;

    public void OnNodeLoaded(HierarchicalWorldgenNode node)
    {
        if (node.Level != 0) return;

        System.Random rng = new System.Random(node.Seed);
        BoxWorldgenNode megachunk = (BoxWorldgenNode)node;
        node.Children = new List<HierarchicalWorldgenNode>();

        int cityCount = rng.Next(minCities, maxCities);

        for (int i = 0; i < cityCount; i++)
        {
            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                Vector3 cityCenter = WorldgenUtils.RandomPointInBounds(megachunk.Bbox, rng);
                Vector3 citySize = new Vector3(
                    (float)(cityMinSize.x + rng.NextDouble() * (cityMaxSize.x - cityMinSize.x)),
                    megachunk.Bbox.size.y,
                    (float)(cityMinSize.y + rng.NextDouble() * (cityMaxSize.y - cityMinSize.y))
                );

                if (Physics.OverlapBox(cityCenter, citySize * 0.5f, Quaternion.identity, planningLayerMask).Length > 0)
                    continue;

                GameObject go = Instantiate(cityNodePrefab, cityCenter, Quaternion.identity, node.transform);
                go.GetComponent<BoxCollider>().size = citySize;

                BoxWorldgenNode cityNode = go.GetComponent<BoxWorldgenNode>();
                cityNode.Level = 1;
                cityNode.Seed = WorldgenUtils.DeriveChildSeed(node.Seed, i, node.Level);
                cityNode.Parent = node;

                node.Children.Add(cityNode);
                break;
            }
        }
    }

    public void OnNodeUnloaded(HierarchicalWorldgenNode node)
    {
        if (node.Level != 0 || node.Children == null) return;

        foreach (var child in node.Children)
            Destroy(child.gameObject);

        node.Children = null;
    }
}