using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class AgentLoader : WorldLoadingModule {

    public TopdownCameraFollower agentCameraFollower;

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

        // Find safe spawn position (mode driven by world config param "agents_spawn_pos")
        Vector3 spawnPos = FindSafeSpawnPosition(prefab);

        // Instantiate
        GameObject agent = Instantiate(prefab, spawnPos, Quaternion.identity, transform);
        string namePrefix;
        if (config.TryGetValue("name_prefix", out namePrefix))
            agent.name = namePrefix;

        if (agentCameraFollower != null)
            agentCameraFollower.target = agent; // Clear camera target before destroying old agent

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
    //  Spawn position routing
    //
    //  World config param "agents_spawn_pos":
    //    "origin"        (default) – random position within world bounds
    //    "city"          – inside a city, outside of any house footprint;
    //                      falls back to "city_outskirts" if no open spot found
    //    "city_outskirts"– radially outside the city OBB; clears a tree-free
    //                      zone at the chosen position via TreeLoader
    //
    //  Requires scene registration order:
    //    WorldLayoutLoader → StructureLoadingCoordinator → CityLoader → AgentLoader → TreeLoader
    //  WorldLayoutLoader and CityLoader now generate eagerly in Initialize() so
    //  city + house footprints are available in WorldData when this runs.
    // ─────────────────────────────────────────────

    private Vector3 FindSafeSpawnPosition(GameObject prefab) {
        float colliderRadius = GetPrefabColliderRadius(prefab);
        System.Random rng = new System.Random(WorldLoadingController.GetDerivedSeed("agents"));
        string mode = WorldLoadingController.GetParamString("agents_spawn_pos", "origin");

        switch (mode.ToLowerInvariant()) {
            case "city":           return FindSpawnInCity(colliderRadius, rng);
            case "city_outskirts": return FindSpawnAtCityOutskirts(colliderRadius, rng);
            default:               return FindSpawnRandom(colliderRadius, rng);
        }
    }

    private float GetPrefabColliderRadius(GameObject prefab) {
        float radius = 0.5f;
        var sphereCol = prefab.GetComponentInChildren<SphereCollider>();
        if (sphereCol != null)
            radius = sphereCol.radius * Mathf.Max(
                prefab.transform.lossyScale.x,
                prefab.transform.lossyScale.y,
                prefab.transform.lossyScale.z);
        return radius;
    }

    // ─────────────────────────────────────────────
    //  Spawn strategies
    // ─────────────────────────────────────────────

    private Vector3 FindSpawnRandom(float colliderRadius, System.Random rng) {
        float worldW = WorldLoadingController.GetParamFloat("world_bounds/width", 100f);
        float worldH = WorldLoadingController.GetParamFloat("world_bounds/height", 100f);
        float margin = spawnSafetyRadius * 2f;
        int worldGenLayer = LayerMask.NameToLayer("WorldGen");

        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++) {
            float x = (float)(rng.NextDouble() * (worldW - margin * 2f) + margin) - worldW * 0.5f;
            float z = (float)(rng.NextDouble() * (worldH - margin * 2f) + margin) - worldH * 0.5f;
            float y = WorldHeightLoader.GetTerrainHeight(x, z) + colliderRadius + 0.1f;
            Vector3 pos = new Vector3(x, y, z);
            if (!IsPhysicsBlocked(pos, worldGenLayer)) return pos;
        }

        float fallbackY = WorldHeightLoader.GetTerrainHeight(0, 0) + colliderRadius + 0.1f;
        Debug.LogWarning("AgentLoader: could not find unblocked spawn position, using world center");
        return new Vector3(0, fallbackY, 0);
    }

    /// <summary>
    /// Tries to find an open spawn point inside the city OBB that does not overlap
    /// any house footprint. Falls back to FindSpawnAtCityOutskirts if all attempts fail.
    /// </summary>
    private Vector3 FindSpawnInCity(float colliderRadius, System.Random rng) {
        List<WorldStructure> cities = WorldData.GetStructures()
            .Where(s => s.structureType == "city").ToList();

        if (cities.Count == 0) {
            Debug.LogWarning("AgentLoader: no cities in WorldData for 'city' spawn; falling back to random");
            return FindSpawnRandom(colliderRadius, rng);
        }

        WorldStructure city = cities[rng.Next(cities.Count)];
        Bounds2D cityBounds = city.GetBoundingBox2D();

        // Collect house footprints to avoid spawning inside buildings.
        // CityLoader.Initialize() places houses before AgentLoader runs (scene order requirement).
        List<Bounds2D> houseBounds = WorldData.GetStructures()
            .Where(s => s.structureType.StartsWith("house", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.GetBoundingBox2D())
            .ToList();

        int maxAttempts  = WorldLoadingController.GetParamInt("agents_city_spawn_attempts", 200);
        int worldGenLayer = LayerMask.NameToLayer("WorldGen");

        float rotRad = cityBounds.rotation * Mathf.Deg2Rad;
        float cosR = Mathf.Cos(rotRad), sinR = Mathf.Sin(rotRad);
        float halfW = cityBounds.size.x * 0.5f, halfH = cityBounds.size.y * 0.5f;

        for (int attempt = 0; attempt < maxAttempts; attempt++) {
            // Random point in city OBB local space, then rotate to world space
            float lx = (float)(rng.NextDouble() * 2.0 - 1.0) * halfW;
            float lz = (float)(rng.NextDouble() * 2.0 - 1.0) * halfH;
            Vector2 pos2D = cityBounds.center + new Vector2(lx * cosR - lz * sinR,
                                                             lx * sinR + lz * cosR);

            // Reject if inside any house footprint
            bool inHouse = false;
            foreach (Bounds2D hb in houseBounds) {
                if (hb.Contains(pos2D)) { inHouse = true; break; }
            }
            if (inHouse) continue;

            float y = WorldHeightLoader.GetTerrainHeight(pos2D.x, pos2D.y) + colliderRadius + 0.1f;
            Vector3 pos = new Vector3(pos2D.x, y, pos2D.y);

            if (!IsPhysicsBlocked(pos, worldGenLayer)) {
                Debug.Log($"AgentLoader: city spawn at {pos} after {attempt + 1} attempt(s)");
                return pos;
            }
        }

        Debug.LogWarning($"AgentLoader: no open spot inside city after {maxAttempts} attempts; " +
                         "falling back to city_outskirts");
        return FindSpawnAtCityOutskirts(colliderRadius, rng);
    }

    /// <summary>
    /// Spawns radially outside the city OBB. Registers a tree-free zone at the
    /// chosen position via TreeLoader so that trees do not generate on top of the agent.
    /// Has a guaranteed fallback so this never fails.
    /// </summary>
    private Vector3 FindSpawnAtCityOutskirts(float colliderRadius, System.Random rng) {
        List<WorldStructure> cities = WorldData.GetStructures()
            .Where(s => s.structureType == "city").ToList();

        if (cities.Count == 0) {
            Debug.LogWarning("AgentLoader: no cities for 'city_outskirts' spawn; falling back to random");
            return FindSpawnRandom(colliderRadius, rng);
        }

        WorldStructure city = cities[rng.Next(cities.Count)];
        Bounds2D cityBounds = city.GetBoundingBox2D();

        // World config params
        float outskirtsDist = WorldLoadingController.GetParamFloat("agents_outskirts_margin", 15f);
        float clearRadius   = WorldLoadingController.GetParamFloat("agents_outskirts_clear_radius", 5f);
        int   worldGenLayer = LayerMask.NameToLayer("WorldGen");

        // Half-diagonal: max distance from city center to any corner of the OBB.
        // Spawn positions are placed between cityHalfDiag and cityHalfDiag+outskirtsDist from center.
        float cityHalfDiag = new Vector2(cityBounds.size.x, cityBounds.size.y).magnitude * 0.5f;

        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++) {
            float   angle      = (float)(rng.NextDouble() * 2.0 * Math.PI);
            float   radialDist = cityHalfDiag + (float)(rng.NextDouble() * outskirtsDist);
            Vector2 dir        = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 pos2D      = cityBounds.center + dir * radialDist;

            float   y   = WorldHeightLoader.GetTerrainHeight(pos2D.x, pos2D.y) + colliderRadius + 0.1f;
            Vector3 pos = new Vector3(pos2D.x, y, pos2D.y);

            if (!IsPhysicsBlocked(pos, worldGenLayer)) {
                TreeLoader.RegisterClearZone(pos2D, clearRadius);
                Debug.Log($"AgentLoader: city_outskirts spawn at {pos} after {attempt + 1} attempt(s); " +
                          $"tree clear zone r={clearRadius}");
                return pos;
            }
        }

        // Guaranteed fallback: fixed bearing (+X from city center), always clears trees
        Vector2 fallback2D = cityBounds.center + new Vector2(cityHalfDiag + 5f, 0f);
        float fallbackY = WorldHeightLoader.GetTerrainHeight(fallback2D.x, fallback2D.y) + colliderRadius + 0.1f;
        TreeLoader.RegisterClearZone(fallback2D, clearRadius);
        Debug.LogWarning("AgentLoader: outskirts fallback position used; tree clearing applied");
        return new Vector3(fallback2D.x, fallbackY, fallback2D.y);
    }

    // ─────────────────────────────────────────────
    //  Physics helpers
    // ─────────────────────────────────────────────

    private bool IsPhysicsBlocked(Vector3 pos, int worldGenLayer) {
        Collider[] overlaps = Physics.OverlapSphere(pos, spawnSafetyRadius);
        foreach (var col in overlaps) {
            if (TerrainMeshLoader.instance != null &&
                col.transform.IsChildOf(TerrainMeshLoader.instance.transform))
                continue;
            if (col.gameObject.layer == worldGenLayer)
                continue;
            return true;
        }
        return false;
    }
}
