using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class AgentLoader : WorldLoadingModule {

    public static AgentLoader instance;

    private bool _spawned = false;
    private readonly List<GameObject> _spawnedAgents = new List<GameObject>();

    [Header("Spawn Settings")]
    public float spawnSafetyRadius = 2f;
    public int maxPlacementAttempts = 50;

    private static readonly Dictionary<string, Type> SensorNameToType = new Dictionary<string, Type> {
        { "lidar2d",        typeof(SemanticLidarSensor) },
        { "rgbd",           typeof(RGBDSensor) },
        { "odom",           typeof(Odom2DSensor) },
        { "collision",      typeof(CollisionSensor) },
        { "relative_pose",  typeof(RelativePoseSensor) },
        { "absolute_pose",  typeof(AbsolutePose2DSensor) },
    };

    private void Awake() {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    // ─────────────────────────────────────────────
    //  WorldLoadingModule
    // ─────────────────────────────────────────────

    public override void Initialize() {
        if (_spawned) return;
        _spawned = true;
        SpawnAgents();
    }

    public override void OnChunkLoadRequested(int cx, int cz, int lod) { }
    public override void OnChunkUnloadRequested(int cx, int cz, int lod) { }

    public override void Clear() {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        _spawnedAgents.Clear();
        _spawned = false;

        // Sensors register timer callbacks with RoslikeTCPServer in Start().
        // After destroying the agent, purge stale references so the server
        // doesn't invoke callbacks on destroyed components.
        RoslikeTCPServer.GetInstance()?.CleanupDestroyedTimersAndSubscribers();
    }

    // ─────────────────────────────────────────────
    //  Spawning
    // ─────────────────────────────────────────────

    private void SpawnAgents() {
        string json = WorldLoadingController.instance.GetAgentConfigJson();
        if (string.IsNullOrEmpty(json)) {
            Debug.LogWarning("AgentLoader: no agent config received, skipping spawn");
            return;
        }

        // Parse agent config (same key/value entries format as world config)
        var parsed = JsonUtility.FromJson<WorldConfig>(json);
        var config = new Dictionary<string, string>();
        foreach (var entry in parsed.entries)
            config[entry.key] = entry.value;

        // Load prefab
        string prefabName;
        if (!config.TryGetValue("prefab_name", out prefabName)) {
            Debug.LogError("AgentLoader: agent config missing 'prefab_name'");
            return;
        }

        GameObject prefab = Resources.Load<GameObject>($"AgentPrefabs/{prefabName}");
        if (prefab == null) {
            Debug.LogError($"AgentLoader: prefab not found at Resources/AgentPrefabs/{prefabName}");
            return;
        }

        // Find safe spawn position
        Vector3 spawnPos = FindSafeSpawnPosition(prefab);

        // Instantiate
        GameObject agent = Instantiate(prefab, spawnPos, Quaternion.identity, transform);
        string namePrefix;
        if (config.TryGetValue("name_prefix", out namePrefix))
            agent.name = namePrefix;

        // Disable all known sensor types
        foreach (var kvp in SensorNameToType) {
            var comp = agent.GetComponentInChildren(kvp.Value, true);
            if (comp != null)
                ((MonoBehaviour)comp).enabled = false;
        }

        // Enable requested sensors
        string sensorsStr;
        var enabledSensorNames = new HashSet<string>();
        if (config.TryGetValue("sensors", out sensorsStr)) {
            string[] sensorNames = sensorsStr.Split(',');
            foreach (string raw in sensorNames) {
                string sensorName = raw.Trim();
                if (string.IsNullOrEmpty(sensorName)) continue;

                Type sensorType;
                if (!SensorNameToType.TryGetValue(sensorName, out sensorType)) {
                    Debug.LogWarning($"AgentLoader: unknown sensor '{sensorName}'");
                    continue;
                }

                var comp = agent.GetComponentInChildren(sensorType, true);
                if (comp != null) {
                    ((MonoBehaviour)comp).enabled = true;
                    enabledSensorNames.Add(sensorName);
                    Debug.Log($"AgentLoader: enabled sensor '{sensorName}'");
                } else {
                    Debug.LogWarning($"AgentLoader: sensor component {sensorType.Name} not found on prefab");
                }
            }
        }

        // Override sensor params via reflection (keys like "lidar2d/maxRange")
        foreach (var kvp in config) {
            int slashIdx = kvp.Key.IndexOf('/');
            if (slashIdx < 0) continue;

            string sensorName = kvp.Key.Substring(0, slashIdx);
            string fieldName = kvp.Key.Substring(slashIdx + 1);

            Type sensorType;
            if (!SensorNameToType.TryGetValue(sensorName, out sensorType)) continue;

            var comp = agent.GetComponentInChildren(sensorType, true);
            if (comp == null) continue;

            FieldInfo field = sensorType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null) {
                Debug.LogWarning($"AgentLoader: field '{fieldName}' not found on {sensorType.Name}");
                continue;
            }

            try {
                object value = Convert.ChangeType(kvp.Value, field.FieldType);
                field.SetValue(comp, value);
                Debug.Log($"AgentLoader: set {sensorType.Name}.{fieldName} = {kvp.Value}");
            } catch (Exception e) {
                Debug.LogWarning($"AgentLoader: failed to set {sensorType.Name}.{fieldName}: {e.Message}");
            }
        }

        // Set agent reference on controller
        WorldLoadingController.instance.agentObject = agent;

        // Reset relative pose origin if enabled
        if (enabledSensorNames.Contains("relative_pose")) {
            var relPose = agent.GetComponentInChildren<RelativePoseSensor>(true);
            if (relPose != null)
                relPose.ResetOrigin();
        }

        _spawnedAgents.Add(agent);
        Debug.Log($"AgentLoader: spawned '{agent.name}' at {spawnPos}");
    }

    // ─────────────────────────────────────────────
    //  Safe position finding
    // ─────────────────────────────────────────────

    private Vector3 FindSafeSpawnPosition(GameObject prefab) {
        float worldW = WorldLoadingController.GetParamFloat("world_bounds/width", 100f);
        float worldH = WorldLoadingController.GetParamFloat("world_bounds/height", 100f);
        float margin = spawnSafetyRadius * 2f;

        // Get collider radius from prefab for height offset
        float colliderRadius = 0.5f;
        var sphereCol = prefab.GetComponentInChildren<SphereCollider>();
        if (sphereCol != null)
            colliderRadius = sphereCol.radius * Mathf.Max(
                prefab.transform.lossyScale.x,
                prefab.transform.lossyScale.y,
                prefab.transform.lossyScale.z);

        System.Random rng = new System.Random(WorldLoadingController.GetDerivedSeed("agents"));
        int worldGenLayer = LayerMask.NameToLayer("WorldGen");

        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++) {
            float x = (float)(rng.NextDouble() * (worldW - margin * 2f) + margin) - worldW * 0.5f;
            float z = (float)(rng.NextDouble() * (worldH - margin * 2f) + margin) - worldH * 0.5f;
            float y = WorldHeightLoader.GetTerrainHeight(x, z) + colliderRadius + 0.1f;

            Vector3 pos = new Vector3(x, y, z);
            Collider[] overlaps = Physics.OverlapSphere(pos, spawnSafetyRadius);

            bool blocked = false;
            foreach (var col in overlaps) {
                // Ignore terrain colliders
                if (TerrainMeshLoader.instance != null && col.transform.IsChildOf(TerrainMeshLoader.instance.transform))
                    continue;
                // Ignore WorldGen layer
                if (col.gameObject.layer == worldGenLayer)
                    continue;
                blocked = true;
                break;
            }

            if (!blocked) return pos;
        }

        // Fallback: world center
        float fallbackY = WorldHeightLoader.GetTerrainHeight(0, 0) + colliderRadius + 0.1f;
        Debug.LogWarning("AgentLoader: could not find unblocked spawn position, using world center");
        return new Vector3(0, fallbackY, 0);
    }
}
