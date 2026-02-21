using UnityEngine;
using System.Collections.Generic;

public class NavsimWorldLoader : HierarchicalWorldLoader
{

    [SerializeField] private MegachunkPlannerModule megachunkPlanner;
    [SerializeField] private CityPlannerModule cityPlanner;
    [SerializeField] private HouseGeneratorModule houseGenerator;
    [SerializeField] private TreeGeneratorModule treGenerator;

    [SerializeField] private int masterSeed = 42;
    [SerializeField] public Vector3 worldSize = new Vector3(10000f, 100f, 10000f); 

    protected override Dictionary<int, List<IWorldgenNodeListener>> BuildDispatchTable()
    {
        return new Dictionary<int, List<IWorldgenNodeListener>>
        {
            { 1, new List<IWorldgenNodeListener> { megachunkPlanner }},
            { 2, new List<IWorldgenNodeListener> { cityPlanner }},
            { 3, new List<IWorldgenNodeListener> { houseGenerator, treGenerator }}
        };
    }

    protected override HierarchicalWorldgenNode BuildRootNode()
    {
       // pass config down to all modules before building
        //megachunkPlanner.Initialize(config);
        //cityPlanner.Initialize(config);
        //treeGenerator.Initialize(config);
        //elevationModule.Initialize(config);

        GameObject go = new GameObject("RootNode");
        go.transform.SetParent(transform);
        BoxCollider col = go.AddComponent<BoxCollider>();
        col.size = worldSize;
        col.isTrigger = true;
        BoxWorldgenNode root = go.AddComponent<BoxWorldgenNode>();
        root.Level = 0;
        root.Seed = masterSeed;
        root.Parent = null;
        return root;
    }

    public void InitializeWithSeed(int seed)
    {
        masterSeed = seed;
        BuildRootNode();
    }
}