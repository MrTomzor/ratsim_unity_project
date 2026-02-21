// CityPlannerModule.cs
using UnityEngine;
using System.Collections.Generic;

public class CityPlannerModule : MonoBehaviour, IWorldgenNodeListener
{
    [SerializeField] private GameObject houseNodePrefab;
    [SerializeField] private int minHouses = 5;
    [SerializeField] private int maxHouses = 20;
    [SerializeField] private Vector2 houseMinSize = new Vector2(4f, 4f);
    [SerializeField] private Vector2 houseMaxSize = new Vector2(10f, 10f);
    [SerializeField] private int placementAttempts = 10;
    [SerializeField] private LayerMask planningLayerMask;

    public void OnNodeLoaded(HierarchicalWorldgenNode node)
    {
        if (node.Level != 1) return;

        System.Random rng = new System.Random(node.Seed);
        BoxWorldgenNode city = (BoxWorldgenNode)node;
        node.Children = new List<HierarchicalWorldgenNode>();

        int houseCount = rng.Next(minHouses, maxHouses);

        for (int i = 0; i < houseCount; i++)
        {
            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                Vector3 houseCenter = WorldgenUtils.RandomPointInBounds(city.Bbox, rng);
                Vector3 houseSize = new Vector3(
                    (float)(houseMinSize.x + rng.NextDouble() * (houseMaxSize.x - houseMinSize.x)),
                    4f,
                    (float)(houseMinSize.y + rng.NextDouble() * (houseMaxSize.y - houseMinSize.y))
                );
                Quaternion houseRot = Quaternion.Euler(0, (float)(rng.NextDouble() * 360f), 0);

                if (Physics.OverlapBox(houseCenter, houseSize * 0.5f, houseRot, planningLayerMask).Length > 0)
                    continue;

                GameObject go = Instantiate(houseNodePrefab, houseCenter, houseRot, node.transform);
                go.GetComponent<BoxCollider>().size = houseSize;

                BoxWorldgenNode houseNode = go.GetComponent<BoxWorldgenNode>();
                houseNode.Level = 2;
                houseNode.Seed = WorldgenUtils.DeriveChildSeed(node.Seed, i, node.Level);
                houseNode.Parent = node;

                node.Children.Add(houseNode);
                break;
            }
        }
    }

    public void OnNodeUnloaded(HierarchicalWorldgenNode node)
    {
        if (node.Level != 1 || node.Children == null) return;

        foreach (var child in node.Children)
            Destroy(child.gameObject);

        node.Children = null;
    }
}