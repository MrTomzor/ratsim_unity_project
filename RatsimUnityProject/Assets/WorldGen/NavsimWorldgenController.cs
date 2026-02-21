using UnityEngine;

public class NavsimWorldgenController : MonoBehaviour
{
    [SerializeField] private NavsimWorldLoader worldLoader;
    [SerializeField] private Transform agent;
    [SerializeField] private LayerMask collisionLayerMask;
    [SerializeField] private float agentNudgeRadius = 2f;

    public int seed;

    private RoslikeTCPServer conn;
    private bool episodeReady = false;
    private bool worldgenRequested = false;

    void Start()
    {
        conn = RoslikeTCPServer.GetInstance();
        conn.RegisterTimerDiscrete(MainLoop, 1);

        conn.Subscribe<BoolMessage>("/worldgen/requested", (msg) => {
            worldgenRequested = msg.data;
            Debug.Log($"Worldgen requested: {worldgenRequested}");
        });
    }

    public void MainLoop(TimerEvent ev)
    {
        if (worldgenRequested)
        {
            StartEpisode();
            worldgenRequested = false;
        }
    }

    private void StartEpisode()
    {
        episodeReady = false;
        worldgenRequested = true;

        Debug.Log($"Received seed {seed}, starting new episode. Worldgen loading started.");

        worldLoader.ClearAll();
        worldLoader.InitializeWithSeed(seed);
        agent.position = Vector3.zero;
        // loading requestor on agent will trigger chunk loads next tick

        Debug.Log("Episode started.");
    }

    private void NudgeAgentIfColliding()
    {
        Collider[] hits = Physics.OverlapSphere(agent.position, agentNudgeRadius, collisionLayerMask);
        if (hits.Length == 0) return;

        for (float r = agentNudgeRadius; r < 20f; r += agentNudgeRadius)
        {
            for (int angle = 0; angle < 360; angle += 45)
            {
                float rad = angle * Mathf.Deg2Rad;
                Vector3 candidate = new Vector3(Mathf.Cos(rad) * r, 0f, Mathf.Sin(rad) * r);
                if (Physics.OverlapSphere(candidate, 0.5f, collisionLayerMask).Length == 0)
                {
                    agent.position = candidate;
                    return;
                }
            }
        }
    }
}
