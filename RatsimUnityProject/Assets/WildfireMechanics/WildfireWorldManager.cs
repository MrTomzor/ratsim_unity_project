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
