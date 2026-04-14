using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WorldDataProvider that populates loaded chunks with "chaotic walkers" — capsule
/// NPCs that wander using a deterministic seeded RNG.
///
/// Lifecycle differs from DynamicObjectLoader: walkers are NOT persistent. They are
/// spawned when a chunk first enters view range and destroyed when the chunk fully
/// leaves range. Because each (chunk, walker index) seed is derived from the world
/// seed, revisiting a chunk respawns the same walker at the same position with the
/// same wander sequence — so determinism holds without tracking walker state between
/// unload/reload cycles.
///
/// Spawn density is uniform across the world. Per-walker properties (speed, whether
/// it avoids agents, spawn position) are sampled from the chunk RNG so each chunk is
/// reproducible independently.
///
/// Config params (all under "chaotic_walkers/" prefix):
///   enabled                     -- 1 to enable, 0 to disable (default 0)
///   prefab_name                 -- prefab in Resources/WorldGen/WalkerPrefabs/ (default "walker_capsule")
///   density                     -- walkers per unit^2 (default 0)
///   avoidance_probability       -- weight for "avoidant" mode (default 0.5)
///   aggression_probability      -- weight for "aggressive" mode (default 0)
///                                  Remaining weight (clamped so total ≤ 1) becomes "default".
///                                  If avoidance+aggression > 1, both are renormalised as weights.
///   reaction_radius             -- radius at which avoidant walkers flee and aggressive walkers
///                                  chase the agent (default 5)
///   reaction_velocity           -- m/s used while reacting (flee or chase); overrides the
///                                  walker's wander speed for avoidant/aggressive motion (default 2)
///   min_velocity, max_velocity  -- m/s range; each walker gets one value for life (default 0.5 / 1.5)
///   walk_duration_min_sec, walk_duration_max_sec   -- per-leg walk duration (default 1.0 / 3.0)
///   pause_duration_min_sec, pause_duration_max_sec -- per-leg pause duration (default 0.5 / 2.0)
///   bounded                     -- 1 to confine walkers near spawn point, 0 otherwise (default 0)
///   bound_radius                -- radius of the confinement disk (default 15)
///   inward_bias_strength        -- 0..+ weight of the inward pull when sampling directions (default 2)
/// </summary>
public class ChaoticWalkersLoader : WorldDataProvider {

    public override WorldDataType[] Provides => new[] { WorldDataType.DynamicObjects };
    public override WorldDataType[] DependsOn => new[] { WorldDataType.Height, WorldDataType.StructureContent };

    private const string PrefabFolder = "WorldGen/WalkerPrefabs/";
    private const float StepsPerSecond = 50f; // physics is 50Hz; see RoslikeTCPServer.SimulationMode

    public bool verbose = false;

    [Header("General")]
    public bool walkersEnabled = false;
    public string prefabName = "walker_capsule";
    public float density = 0f;

    [Header("Reaction to agent")]
    [Range(0f, 1f)] public float avoidanceProbability = 0.5f;
    [Range(0f, 1f)] public float aggressionProbability = 0f;
    public float reactionRadius = 5f;
    public float reactionVelocity = 2f;

    [Header("Velocity (per-walker, fixed at spawn)")]
    public float minVelocity = 0.5f;
    public float maxVelocity = 1.5f;

    [Header("Walk / Pause Durations (seconds)")]
    public float walkDurationMinSec = 1.0f;
    public float walkDurationMaxSec = 3.0f;
    public float pauseDurationMinSec = 0.5f;
    public float pauseDurationMaxSec = 2.0f;

    [Header("Bounding")]
    public bool bounded = false;
    public float boundRadius = 15f;
    public float inwardBiasStrength = 2f;

    // Runtime
    private GameObject _prefab;
    private int _seed;
    private float _chunkWidth;
    private bool _paramsLoaded;

    // spawnChunk → list of walker GameObjects spawned there (GC'd when chunk unloads)
    private readonly Dictionary<Vector2Int, List<GameObject>> _chunkWalkers = new Dictionary<Vector2Int, List<GameObject>>();
    // Track reference count per chunk (LOD0 and LOD1 both trigger GenerateChunk).
    private readonly HashSet<Vector2Int> _spawnedChunks = new HashSet<Vector2Int>();

    // ─────────────────────────────────────────────

    private void LoadParams() {
        _seed = WorldLoadingController.GetDerivedSeed("chaotic_walkers");
        _chunkWidth = WorldLoadingController.GetChunkWidth();

        walkersEnabled        = WorldLoadingController.GetParamInt("chaotic_walkers/enabled", walkersEnabled ? 1 : 0) != 0;
        prefabName            = WorldLoadingController.GetParamString("chaotic_walkers/prefab_name", prefabName);
        density               = WorldLoadingController.GetParamFloat("chaotic_walkers/density", density);
        avoidanceProbability  = WorldLoadingController.GetParamFloat("chaotic_walkers/avoidance_probability", avoidanceProbability);
        aggressionProbability = WorldLoadingController.GetParamFloat("chaotic_walkers/aggression_probability", aggressionProbability);
        reactionRadius        = WorldLoadingController.GetParamFloat("chaotic_walkers/reaction_radius", reactionRadius);
        reactionVelocity      = WorldLoadingController.GetParamFloat("chaotic_walkers/reaction_velocity", reactionVelocity);
        minVelocity           = WorldLoadingController.GetParamFloat("chaotic_walkers/min_velocity", minVelocity);
        maxVelocity           = WorldLoadingController.GetParamFloat("chaotic_walkers/max_velocity", maxVelocity);
        walkDurationMinSec    = WorldLoadingController.GetParamFloat("chaotic_walkers/walk_duration_min_sec", walkDurationMinSec);
        walkDurationMaxSec    = WorldLoadingController.GetParamFloat("chaotic_walkers/walk_duration_max_sec", walkDurationMaxSec);
        pauseDurationMinSec   = WorldLoadingController.GetParamFloat("chaotic_walkers/pause_duration_min_sec", pauseDurationMinSec);
        pauseDurationMaxSec   = WorldLoadingController.GetParamFloat("chaotic_walkers/pause_duration_max_sec", pauseDurationMaxSec);
        bounded               = WorldLoadingController.GetParamInt("chaotic_walkers/bounded", bounded ? 1 : 0) != 0;
        boundRadius           = WorldLoadingController.GetParamFloat("chaotic_walkers/bound_radius", boundRadius);
        inwardBiasStrength    = WorldLoadingController.GetParamFloat("chaotic_walkers/inward_bias_strength", inwardBiasStrength);

        _prefab = walkersEnabled ? Resources.Load<GameObject>(PrefabFolder + prefabName) : null;
        if (walkersEnabled && _prefab == null)
            Debug.LogWarning($"ChaoticWalkersLoader: prefab not found at Resources/{PrefabFolder}{prefabName}");

        _paramsLoaded = true;

        Debug.Log($"ChaoticWalkersLoader: params loaded — enabled={walkersEnabled}, " +
            $"prefab={prefabName}, density={density:F4}, " +
            $"avoid_p={avoidanceProbability:F2}, aggro_p={aggressionProbability:F2}, reaction_r={reactionRadius:F1}, reaction_v={reactionVelocity:F2}, " +
            $"velocity=[{minVelocity:F2},{maxVelocity:F2}], " +
            $"walk=[{walkDurationMinSec:F1},{walkDurationMaxSec:F1}]s, " +
            $"pause=[{pauseDurationMinSec:F1},{pauseDurationMaxSec:F1}]s, " +
            $"bounded={bounded} r={boundRadius:F1} bias={inwardBiasStrength:F1}");
    }

    // ─────────────────────────────────────────────

    public override void Generate() {
        if (!_paramsLoaded) LoadParams();
    }

    public override void GenerateChunk(int cx, int cz, int lod) {
        if (!_paramsLoaded) LoadParams();
        if (!walkersEnabled || _prefab == null || density <= 0f) return;

        Vector2Int key = new Vector2Int(cx, cz);
        if (_spawnedChunks.Contains(key)) return;
        _spawnedChunks.Add(key);

        SpawnChunk(key);
    }

    public override void ClearChunk(int cx, int cz, int lod) {
        Vector2Int key = new Vector2Int(cx, cz);
        if (!_spawnedChunks.Remove(key)) return;

        if (_chunkWalkers.TryGetValue(key, out var list)) {
            foreach (var go in list)
                if (go != null) Destroy(go);
            _chunkWalkers.Remove(key);
        }
        // Purge any stale timer callbacks registered by the destroyed walkers.
        RoslikeTCPServer.GetInstance()?.CleanupDestroyedTimersAndSubscribers();
    }

    public override void Clear() {
        foreach (var kvp in _chunkWalkers)
            foreach (var go in kvp.Value)
                if (go != null) Destroy(go);
        _chunkWalkers.Clear();
        _spawnedChunks.Clear();
        _paramsLoaded = false;
        RoslikeTCPServer.GetInstance()?.CleanupDestroyedTimersAndSubscribers();
    }

    // ─────────────────────────────────────────────

    private void SpawnChunk(Vector2Int chunkID) {
        int chunkSeed = _seed ^ (chunkID.x * 1000003) ^ (chunkID.y * 999983);
        System.Random rng = new System.Random(chunkSeed);

        int count = Mathf.RoundToInt(density * _chunkWidth * _chunkWidth);
        if (count <= 0) return;

        float originX = chunkID.x * _chunkWidth;
        float originZ = chunkID.y * _chunkWidth;

        int walkMinSteps  = Mathf.Max(1, Mathf.RoundToInt(walkDurationMinSec  * StepsPerSecond));
        int walkMaxSteps  = Mathf.Max(walkMinSteps, Mathf.RoundToInt(walkDurationMaxSec * StepsPerSecond));
        int pauseMinSteps = Mathf.Max(1, Mathf.RoundToInt(pauseDurationMinSec * StepsPerSecond));
        int pauseMaxSteps = Mathf.Max(pauseMinSteps, Mathf.RoundToInt(pauseDurationMaxSec * StepsPerSecond));

        var list = new List<GameObject>(count);
        _chunkWalkers[chunkID] = list;

        var heightProvider = WorldServices.Get<IHeightProvider>();

        for (int i = 0; i < count; i++) {
            float x = originX + (float)rng.NextDouble() * _chunkWidth;
            float z = originZ + (float)rng.NextDouble() * _chunkWidth;
            float y = heightProvider.GetTerrainHeight(x, z) + 1.0f; // leave a small drop margin

            float speed = Mathf.Lerp(minVelocity, maxVelocity, (float)rng.NextDouble());
            ChaoticWalker.Mode mode = SampleMode(rng);
            int walkerSeed = chunkSeed ^ (i * 265447); // per-walker seed for walk sequence and other properties

            GameObject go = Instantiate(_prefab, new Vector3(x, y, z), Quaternion.identity, transform);
            go.name = $"Walker_{chunkID.x}_{chunkID.y}_{i}";

            var walker = go.GetComponent<ChaoticWalker>();
            if (walker == null) walker = go.AddComponent<ChaoticWalker>();

            walker.speed = speed;
            walker.mode = mode;
            walker.reactionRadius = reactionRadius;
            walker.reactionVelocity = reactionVelocity;
            walker.walkMinSteps = walkMinSteps;
            walker.walkMaxSteps = walkMaxSteps;
            walker.pauseMinSteps = pauseMinSteps;
            walker.pauseMaxSteps = pauseMaxSteps;
            walker.bounded = bounded;
            walker.boundCenter = new Vector2(x, z);
            walker.boundRadius = boundRadius;
            walker.inwardBiasStrength = inwardBiasStrength;
            walker.Init(walkerSeed);

            list.Add(go);
        }

        if (verbose)
            Debug.Log($"ChaoticWalkersLoader: chunk ({chunkID.x},{chunkID.y}) spawned {count} walkers");
    }

    /// <summary>
    /// Sample a behaviour mode for one walker. The two probabilities are treated as
    /// weights: if their sum ≤ 1, the remainder is "default"; if > 1, they are
    /// renormalised (default becomes unreachable).
    /// </summary>
    private ChaoticWalker.Mode SampleMode(System.Random rng) {
        float pAvoid = Mathf.Max(0f, avoidanceProbability);
        float pAggro = Mathf.Max(0f, aggressionProbability);
        float sum = pAvoid + pAggro;
        if (sum <= 0f) return ChaoticWalker.Mode.Default;

        float wAvoid, wAggro;
        if (sum > 1f) {
            wAvoid = pAvoid / sum;
            wAggro = pAggro / sum;
        } else {
            wAvoid = pAvoid;
            wAggro = pAggro;
        }

        double r = rng.NextDouble();
        if (r < wAvoid) return ChaoticWalker.Mode.Avoidant;
        if (r < wAvoid + wAggro) return ChaoticWalker.Mode.Aggressive;
        return ChaoticWalker.Mode.Default;
    }
}
