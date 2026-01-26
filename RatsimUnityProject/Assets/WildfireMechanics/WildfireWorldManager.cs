using System.Collections.Generic;
using UnityEngine;

public class WildfireWorldManager : MonoBehaviour
{
    public WildfireWorldGenMessage wildfireWorldGenMessage = null;
    public string mapGenMsgTopic = "/wildfire_worldgen_input";
    public string goalPosTopic = "/wildfire_goal_position";

    float defaultHeight;

     System.Random rng;

    public Vector3 startPosition;
    public Vector3 goalPosition;

    // TREES
    public List<GameObject> trees;
    public List<GameObject> agents;

    // CARS
    public float roadClearingWidth = 1.5f;
    public float roadStartGoalMargin = 20;
    public int numCarsToSpawn = 15;


    public GameObject carSpawnerPrefab;
    public GameObject roadPrefab;
    public List<GameObject> carSpawners;
    public List<GameObject> roads;

    // PREFABS
    public GameObject treePrefab;
    public GameObject agentPrefab;

    RoslikeTCPServer conn;
    



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        defaultHeight = this.transform.position.y;

        conn = RoslikeTCPServer.GetInstance();
        conn.Subscribe<WildfireWorldGenMessage>(mapGenMsgTopic, GenerateMsgCallback);
        conn.RegisterTimerDiscrete(MainLoop, 1);
    }

    public void MainLoop(TimerEvent ev)
    {
        PublishGoalPosition();
    }

    public void GenerateMsgCallback(WildfireWorldGenMessage msg)
    {
        Debug.Log("Received WildfireWorldGenMessage, generating world.");
        wildfireWorldGenMessage = msg;

        GenerateWorld();
    }

    public void GenerateWorld()
    {
        if (wildfireWorldGenMessage == null)
        {
            Debug.LogError("WildfireWorldGenMessage is null, cannot generate world.");
            return;
        }
        
        float arenaWidth = wildfireWorldGenMessage.arenaWidth;
        float arenaHeight = wildfireWorldGenMessage.arenaHeight;

        // apply seed
        rng = new System.Random(wildfireWorldGenMessage.seed);

        // Generate start and goal positions
        float clearingDist = wildfireWorldGenMessage.startAndGoalClearingDistance;
        startPosition = new Vector3(arenaWidth / 2, defaultHeight, arenaHeight * 0.2f);
        goalPosition = new Vector3(arenaWidth / 2, defaultHeight, arenaHeight * 0.8f);

        // Generate trees
        GenerateTrees();

        // Generate roads
        GenerateRoadsSimple();

        // Spawn or move agents to start position
        for (int i = 0; i < wildfireWorldGenMessage.numAgents; i++)
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
        }


        // Send data to python
        PublishGoalPosition();
        
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
        int numRoads = (int)wildfireWorldGenMessage.carRoadSpawnFrequency; // assume its just number of roads for now
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
            Vector3 roadPosition = new Vector3(wildfireWorldGenMessage.arenaWidth / 2, 0.1f, roadZ);

            // Spawn road
            GameObject road = Instantiate(roadPrefab, roadPosition, Quaternion.identity);
            roads.Add(road);

            // Determine car direction (left or right), determines spawn
            bool directionLeftToRight = (rng.NextDouble() > 0.5);
            
            // Spawn car spawner at one end of the road 
            float spawnerX = directionLeftToRight ? 0f : wildfireWorldGenMessage.arenaWidth;
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
            spawnerScript.roadLength = wildfireWorldGenMessage.arenaWidth;
            float currentTime = 0f;
            for (int j = 0; j < numCarsToSpawn; j++)
            {
                float interval = (float)(rng.NextDouble() * (maxSpawnInterval - minSpawnInterval) + minSpawnInterval);
                currentTime += interval;
                spawnerScript.spawnTimes.Add(currentTime);
            }
                     
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
        float mapArea = wildfireWorldGenMessage.arenaWidth * wildfireWorldGenMessage.arenaHeight;
        int numTrees = (int)(wildfireWorldGenMessage.treeDensity * mapArea); 
        Debug.Log($"Generating {numTrees} trees.");

        for (int i = 0; i < numTrees; i++)
        {
            float x = (float)(rng.NextDouble() * wildfireWorldGenMessage.arenaWidth);
            float z = (float)(rng.NextDouble() * wildfireWorldGenMessage.arenaHeight);
            Vector3 position = new Vector3(x, defaultHeight, z);

            if(Vector3.Distance(position, startPosition) < wildfireWorldGenMessage.startAndGoalClearingDistance ||
               Vector3.Distance(position, goalPosition) < wildfireWorldGenMessage.startAndGoalClearingDistance)
            {
                // Skip tree placement if too close to start or goal
                continue;
            }

            GameObject tree = Instantiate(treePrefab, position, Quaternion.identity);
            trees.Add(tree);
        }

        Debug.Log($"Total trees generated: {trees.Count}");
        
        
    }
}
