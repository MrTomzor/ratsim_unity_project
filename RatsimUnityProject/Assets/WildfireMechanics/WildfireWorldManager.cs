using System.Collections.Generic;
using UnityEngine;


public class WildfireWorldManager : MonoBehaviour
{
    public string goalPosTopic = "/wildfire_goal_position";

    float defaultHeight;

    // WORLD PARAMETERS SETTABLE THRU MSGS
    public float arenaWidth = 100f;
    public float arenaHeight = 100f;
    public float treeDensity = 0.01f; // trees per square unit
    public int seed = 42;
    public int numAgents = 1;
    public float startAndGoalClearingDistance = 10f;
    public float carRoadSpawnFrequency = 3; // number of roads to spawn

    System.Random rng;

    public Vector3 startPosition;
    public Vector3 goalPosition;

    // BELOW ARE INTERNAL VARIABLES (not communicated via msgs)

    // TREES
    public List<GameObject> trees;
    public List<GameObject> agents;
    public bool treeOscillationEnabled = false;
    public Vector2 treeOscillationDirection = new Vector2(1, 0);
    public List<Vector3> treeOriginalPositions;
    public List<float> treeOscillationPhases;
    public float treeOscillationMagnitude = 0.5f;
    public float treeOscillationFrequency = 1.0f;
    public float treeOscillationTime = 0;
    public float aroundAgentDynamicsBoxSize = 120f;


    // CARS
    public float roadClearingWidth = 1.5f;
    public float roadStartGoalMargin = 20;
    public int numCarsToSpawn = 15;


    public GameObject carSpawnerPrefab;
    public GameObject roadPrefab;
    public List<GameObject> carSpawners;
    public List<GameObject> roads;

    // PREFABS
    public bool randomizeAgentDirection = true;
    public GameObject treePrefab;
    public GameObject agentPrefab;
    public GameObject goalMarkerPrefab;
    public List<GameObject> activeMarkers;

    RoslikeTCPServer conn;

    // FLAGS
    public bool worldgenRequested = false;
    



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        defaultHeight = this.transform.position.y;

        conn = RoslikeTCPServer.GetInstance();
        //conn.Subscribe<WildfireWorldGenMessage>(mapGenMsgTopic, GenerateMsgCallback);
        conn.RegisterTimerDiscrete(MainLoop, 1);

        conn.Subscribe<BoolMessage>("/worldgen/requested", (msg) => {
            worldgenRequested = msg.data;
            Debug.Log($"Worldgen requested: {worldgenRequested}");
        });

        conn.Subscribe<Int32Message>("/worldgen/seed", (msg) => {
            seed = msg.data;
            Debug.Log($"Worldgen seed set to: {seed}");
        });

       // w and h
        conn.Subscribe<Float32Message>("/worldgen/arenaWidth", (msg) => {
            arenaWidth = msg.data;
            Debug.Log($"Worldgen arena width set to: {arenaWidth}");
        });
        conn.Subscribe<Float32Message>("/worldgen/arenaHeight", (msg) => {
            arenaHeight = msg.data;
            Debug.Log($"Worldgen arena height set to: {arenaHeight}");
        });

        // trees
        conn.Subscribe<Float32Message>("/worldgen/treeDensity", (msg) => {
            treeDensity = msg.data;
            Debug.Log($"Worldgen tree density set to: {treeDensity}");
        });
    }

    public void MainLoop(TimerEvent ev)
    {
        if(worldgenRequested)
        {
            worldgenRequested = false;
            GenerateWorld();
        }

        PublishGoalPosition();
        if(treeOscillationEnabled)
            HandleTreeOscillation();
    }

    public void GenerateWorld()
    {

        // apply seed
        rng = new System.Random(seed);

        // Generate start and goal positions
        float clearingDist = startAndGoalClearingDistance;
        startPosition = new Vector3(arenaWidth / 2, defaultHeight, arenaHeight * 0.2f);
        goalPosition = new Vector3(arenaWidth / 2, defaultHeight, arenaHeight * 0.8f);

        // Generate trees
        GenerateTrees();

        // Generate roads
        GenerateRoadsSimple();

        // Generate markers
        GenerateMarkers();

        // Spawn or move agents to start position
        for (int i = 0; i < numAgents; i++)
        {
            GameObject agent;

            Vector3 spawnpos = startPosition;
            Quaternion spawnrot = Quaternion.identity;
            // make agent point towards goal at start
            Vector3 directionToGoal = (goalPosition - startPosition).normalized;
            if (directionToGoal != Vector3.zero)
            {
                spawnrot = Quaternion.LookRotation(directionToGoal);
            }

            if (i < agents.Count)
            {
                agent = agents[i];
                agent.transform.position = spawnpos;
                agent.transform.rotation = spawnrot;

                
                
            }
            else
            {
                agent = Instantiate(agentPrefab, spawnpos, spawnrot);
                agents.Add(agent);
                // TODO later -- set agent's topic prefix according to its index
            }

            if(randomizeAgentDirection)
            {
                // Randomize initial rotation a bit
                float randomYaw = (float)(rng.NextDouble() * 360.0);
                agent.transform.Rotate(0, randomYaw, 0);
            }
        }

        if(treeOscillationEnabled){
            InitializeTreeDynamics();
        }


        // Send data to python
        PublishGoalPosition();
    }
        
        

    public void GenerateMarkers()
    {
        // Clear existing markers
        foreach (var marker in activeMarkers)
        {
            Destroy(marker);
        }
        activeMarkers.Clear();

        // Create new goal marker
        GameObject goalMarker = Instantiate(goalMarkerPrefab, goalPosition, Quaternion.identity);
        activeMarkers.Add(goalMarker);
    }

    void PublishGoalPosition()
    {
        // Publish goal position
        Twist2DMessage goalMsg = new Twist2DMessage();
        goalMsg.forward = goalPosition.z;
        goalMsg.left = -goalPosition.x;
        goalMsg.radiansCounterClockwise = 0.0f; // No orientation for goal
        conn.Publish(goalPosTopic, goalMsg);
    }

    void GenerateRoadsSimple()
    {
        // delete old
        foreach (var road in roads)
        {
            Destroy(road);
        }
        roads.Clear();
        foreach (var spawner in carSpawners)
        {
            spawner.GetComponent<CarSpawner>().spawningEnabled = false;
            Destroy(spawner);
        }
        carSpawners.Clear();

        // First determine number of roads
        int numRoads = (int)carRoadSpawnFrequency; // assume its just number of roads for now
        Debug.Log($"Generating {numRoads} roads.");

        // Then for each road, determine position along the axis from start to goal (assume in Z).
        
        for(int i = 0; i < numRoads; i++){
            // Select along random pos between start and goal, with some margin from both positions
            float roadZ = (float)(rng.NextDouble() * ((goalPosition.z - roadStartGoalMargin) - (startPosition.z + roadStartGoalMargin)) + (startPosition.z + roadStartGoalMargin));

            // check if not too close to other roads, otherwise skip
            bool tooClose = false;
            foreach(var existingRoad in roads)
            {
                if(Mathf.Abs(existingRoad.transform.position.z - roadZ) < roadClearingWidth / 2)
                {
                    tooClose = true;
                    break;
                }
            }
            if(tooClose)
            {
                Debug.Log($"Skipping road at Z={roadZ} due to proximity to existing road.");
                continue;
            }

            // CLEAR ALL TREES IN THE ROAD AREA
            List<GameObject> treesToRemove = new List<GameObject>();
            foreach (var tree in trees)
            {
                if (Mathf.Abs(tree.transform.position.z - roadZ) < roadClearingWidth / 2)
                {
                    treesToRemove.Add(tree);
                }
            }
            foreach (var tree in treesToRemove)
            {
                Destroy(tree);
                trees.Remove(tree);
            }
            Debug.Log($"Cleared {treesToRemove.Count} trees for road at Z={roadZ}.");

            // Spawn the road prefab (for visualization)
            Vector3 roadPosition = new Vector3(arenaWidth / 2, 0.1f, roadZ);

            // Spawn road
            GameObject road = Instantiate(roadPrefab, roadPosition, Quaternion.identity);
            roads.Add(road);

            // Determine car direction (left or right), determines spawn
            bool directionLeftToRight = (rng.NextDouble() > 0.5);
            
            // Spawn car spawner at one end of the road 
            float spawnerX = directionLeftToRight ? 0f : arenaWidth;
            float spawnerZ = roadZ;
            Vector3 spawnerPosition = new Vector3(spawnerX, defaultHeight, spawnerZ);
            Quaternion spawnerRotation = directionLeftToRight ? Quaternion.Euler(0, 90, 0) : Quaternion.Euler(0, -90, 0);
            GameObject carSpawner = Instantiate(carSpawnerPrefab, spawnerPosition, spawnerRotation);
            carSpawners.Add(carSpawner);   

            // Generate sequence of spawn times for the car spawner (using the given seed for the world to be deterministic)
            CarSpawner spawnerScript = carSpawner.GetComponent<CarSpawner>();
            spawnerScript.spawnTimes.Clear();
            
            float minSpawnInterval = spawnerScript.minSpawnInterval;
            float maxSpawnInterval = spawnerScript.maxSpawnInterval;
            spawnerScript.roadLength = arenaWidth;
            float currentTime = 0f;
            for (int j = 0; j < numCarsToSpawn; j++)
            {
                float interval = (float)(rng.NextDouble() * (maxSpawnInterval - minSpawnInterval) + minSpawnInterval);
                currentTime += interval;
                spawnerScript.spawnTimes.Add(currentTime);
            }
                     
        }

        
    }

    void InitializeTreeDynamics()
    {

        treeOriginalPositions = new List<Vector3>();
        treeOscillationPhases = new List<float>();
        treeOscillationTime = 0;
    

        for(int i = 0; i < trees.Count; i++)
        {
            Vector3 position = trees[i].transform.position;
                
            treeOriginalPositions.Add(position);
            treeOscillationPhases.Add((float)(rng.NextDouble() * 2 * Mathf.PI));
        }

            
    }

    void GenerateTrees()
    {
        // Clear existing trees
        foreach (var tree in trees)
        {
            Destroy(tree);
        }
        trees.Clear();
        


        // Instantiate new trees based on wildfireWorldGenMessage and its seed
        float mapArea = arenaWidth * arenaHeight;
        int numTrees = (int)(treeDensity * mapArea); 
        Debug.Log($"Generating {numTrees} trees.");

        for (int i = 0; i < numTrees; i++)
        {
            float x = (float)(rng.NextDouble() * arenaWidth);
            float z = (float)(rng.NextDouble() * arenaHeight);
            Vector3 position = new Vector3(x, defaultHeight, z);

            if(Vector3.Distance(position, startPosition) < startAndGoalClearingDistance ||
               Vector3.Distance(position, goalPosition) < startAndGoalClearingDistance)
            {
                // Skip tree placement if too close to start or goal
                continue;
            }

            GameObject tree = Instantiate(treePrefab, position, Quaternion.identity);
            trees.Add(tree);
            
        }

        Debug.Log($"Total trees generated: {trees.Count}");
    }

    void HandleTreeOscillation()
    {
        treeOscillationTime += conn.physicsStepTime * treeOscillationFrequency;

        float agentX = agents[0].transform.position.x;
        float agentZ = agents[0].transform.position.z;

        float time = treeOscillationTime;
        for (int i = 0; i < trees.Count; i++)
        {
            // do not oscillate if outside of aroundAgentDynamicsBoxSize
            Vector3 treePos = trees[i].transform.position;
            if (Mathf.Abs(treePos.x - agentX) > aroundAgentDynamicsBoxSize / 2 ||
                Mathf.Abs(treePos.z - agentZ) > aroundAgentDynamicsBoxSize / 2)
            {
                continue;
            }
            
            Vector3 originalPos = treeOriginalPositions[i];
            GameObject tree = trees[i];
            float phase = treeOscillationPhases[i];

            float oscillation = Mathf.Sin(time + phase) * treeOscillationMagnitude;
            Vector3 offset = new Vector3(treeOscillationDirection.x, 0, treeOscillationDirection.y).normalized * oscillation;

            tree.transform.position = originalPos + offset;
        }
    }
}
