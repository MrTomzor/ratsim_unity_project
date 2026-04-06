using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// WorldStructureProvider that spawns reward objects via two parallel modes:
///
/// 1. Uniform mode: spawns reward objects randomly across the world at terrain height,
///    per-chunk (like TreeLoader). Controlled by reward_objects/uniform_density.
///
/// 2. Structure mode: spawns reward objects at rewardSpawnPositions inside structures
///    whose type is listed in reward_objects/allowed_structures. Each structure type has a
///    per-position spawn probability and a skip probability (chance of no rewards at all
///    in that structure).
///
/// Both modes can run simultaneously. Spawned objects use a single configurable prefab.
///
/// Tracks all spawned rewards and publishes a BoolMessage on /all_rewards_collected
/// when every reward in every visitable chunk and structure has been collected.
///
/// Config params (all under "reward_objects/" prefix):
///   prefab_name                          -- prefab in Resources/WorldGen/RewardObjectPrefabs/ (default "reward_obj1")
///   uniform_density                      -- objects per unit^2 for uniform mode (default 0; 0 = disabled)
///   boundary_buffer                      -- units inset from world boundary for uniform spawning (default 5)
///   allowed_structures                   -- comma list of structure types for structure mode (default ""; empty = disabled)
///   {structure_type}/spawn_probability   -- 0.0-1.0 per spawn position (default 1)
///   {structure_type}/skip_probability    -- 0.0-1.0 chance to skip the entire structure (default 0)
/// </summary>
public class RewardObjectLoader : WorldStructureProvider {

    public static RewardObjectLoader Instance { get; private set; }

    public override WorldDataType[] Provides => new[] { WorldDataType.Rewards };
    public override WorldDataType[] DependsOn => new[] { WorldDataType.Height, WorldDataType.StructureEvents };

    public bool verbose = true;

    private const string PrefabFolder = "WorldGen/RewardObjectPrefabs/";
    private const string ContainerName = "_RewardLoaderContent";
    private const string AllCollectedTopic = "/all_rewards_collected";

    // ─────────────────────────────────────────────
    //  Editor-configurable defaults (overridden by episode params)
    // ─────────────────────────────────────────────

    [Header("General")]
    public string prefabName = "reward_obj1";

    [Header("Uniform Mode")]
    public float uniformDensity = 0f;
    public float boundaryBuffer = 5f;

    [Header("Structure Mode")]
    public string allowedStructures = "";

    // ─────────────────────────────────────────────
    //  Runtime state
    // ─────────────────────────────────────────────

    private struct StructureEntry {
        public string type;
        public float spawnProbability;
        public float skipProbability;
    }

    private GameObject _rewardPrefab;
    private List<StructureEntry> _structureEntries = new List<StructureEntry>();
    private int _rewardSeed;
    private bool _paramsLoaded;
    private float _chunkWidth;

    // World boundary rect (for filtering spawns and determining visitable chunks)
    private bool _hasBoundary;
    private float _boundaryMinX, _boundaryMaxX;
    private float _boundaryMinZ, _boundaryMaxZ;

    // Uniform mode: chunk-based spawning (like TreeLoader)
    private Dictionary<Vector2Int, GameObject> _chunkObjects = new Dictionary<Vector2Int, GameObject>();
    private HashSet<Vector2Int> _generatedChunks = new HashSet<Vector2Int>();

    // ── Reward tracking ──
    private int _totalSpawned;
    private int _totalCollected;
    private bool _allCollectedPublished;

    // ─────────────────────────────────────────────
    //  Singleton
    // ─────────────────────────────────────────────

    protected override void OnEnable() {
        base.OnEnable();
        Instance = this;
    }

    protected override void OnDisable() {
        base.OnDisable();
        if (Instance == this) Instance = null;
    }

    // ─────────────────────────────────────────────
    //  Param loading
    // ─────────────────────────────────────────────

    private void LoadParams() {
        _rewardSeed = WorldLoadingController.GetDerivedSeed("reward");
        _chunkWidth = WorldLoadingController.GetChunkWidth();

        prefabName = WorldLoadingController.GetParamString("reward_objects/prefab_name", prefabName);
        _rewardPrefab = Resources.Load<GameObject>(PrefabFolder + prefabName);
        if (_rewardPrefab == null)
            Debug.LogWarning($"RewardObjectLoader: prefab not found at Resources/{PrefabFolder}{prefabName}");

        uniformDensity = WorldLoadingController.GetParamFloat("reward_objects/uniform_density", uniformDensity);
        boundaryBuffer = WorldLoadingController.GetParamFloat("reward_objects/boundary_buffer", boundaryBuffer);

        // Read world boundary params
        string boundaryType = WorldLoadingController.GetParamString("world_bounds/boundary_type", "none");
        _hasBoundary = boundaryType.Equals("visible_wall", System.StringComparison.OrdinalIgnoreCase);
        if (_hasBoundary) {
            float worldW = WorldLoadingController.GetParamFloat("world_bounds/width", 100f);
            float worldH = WorldLoadingController.GetParamFloat("world_bounds/height", 100f);
            _boundaryMinX = -worldW * 0.5f;
            _boundaryMaxX =  worldW * 0.5f;
            _boundaryMinZ = -worldH * 0.5f;
            _boundaryMaxZ =  worldH * 0.5f;
        }

        // Structure entries
        _structureEntries.Clear();
        allowedStructures = WorldLoadingController.GetParamString("reward_objects/allowed_structures", allowedStructures);
        if (!string.IsNullOrEmpty(allowedStructures)) {
            foreach (string raw in allowedStructures.Split(',')) {
                string type = raw.Trim();
                if (string.IsNullOrEmpty(type)) continue;
                _structureEntries.Add(new StructureEntry {
                    type             = type,
                    spawnProbability = WorldLoadingController.GetParamFloat($"reward_objects/{type}/spawn_probability", 1f),
                    skipProbability  = WorldLoadingController.GetParamFloat($"reward_objects/{type}/skip_probability", 0f)
                });
            }
        }

        _paramsLoaded = true;

        Debug.Log($"RewardObjectLoader: params loaded — " +
            $"prefab={(prefabName)}, " +
            $"uniformDensity={uniformDensity:F4}, " +
            $"boundaryBuffer={boundaryBuffer:F1}, " +
            $"hasBoundary={_hasBoundary}, " +
            $"structureTypes={_structureEntries.Count}");
        if (verbose) {
            foreach (var e in _structureEntries)
                Debug.Log($"  structure '{e.type}': spawnProb={e.spawnProbability:F2}, skipProb={e.skipProbability:F2}");
        }
    }

    // ─────────────────────────────────────────────
    //  Boundary helpers
    // ─────────────────────────────────────────────

    /// <summary>Returns true if (x, z) is inside the world boundary with the buffer inset.</summary>
    private bool IsInsideBoundaryBuffer(float x, float z) {
        if (!_hasBoundary) return true; // no boundary → everything allowed
        return x >= _boundaryMinX + boundaryBuffer &&
               x <= _boundaryMaxX - boundaryBuffer &&
               z >= _boundaryMinZ + boundaryBuffer &&
               z <= _boundaryMaxZ - boundaryBuffer;
    }

    /// <summary>Returns true if the chunk overlaps the world boundary rect at all.</summary>
    private bool IsChunkVisitable(Vector2Int chunkID) {
        if (!_hasBoundary) return false; // no boundary → can't determine visitability
        float cMinX = chunkID.x * _chunkWidth;
        float cMaxX = cMinX + _chunkWidth;
        float cMinZ = chunkID.y * _chunkWidth;
        float cMaxZ = cMinZ + _chunkWidth;
        // AABB overlap test
        return cMaxX > _boundaryMinX && cMinX < _boundaryMaxX &&
               cMaxZ > _boundaryMinZ && cMinZ < _boundaryMaxZ;
    }

    // ─────────────────────────────────────────────
    //  Pickup notification (called by Pickupable)
    // ─────────────────────────────────────────────

    /// <summary>Called by Pickupable when a reward object is collected.</summary>
    public void NotifyRewardCollected() {
        _totalCollected++;
        if (verbose)
            Debug.Log($"RewardObjectLoader: reward collected ({_totalCollected}/{_totalSpawned})");
        CheckAllCollected();
    }

    private void CheckAllCollected() {
        if (_allCollectedPublished) return;
        if (!_hasBoundary) return;
        if (_totalSpawned <= 0) return;

        // Check that all visitable chunks have been generated (loaded at LOD0)
        if (!AllVisitableChunksGenerated()) return;

        if (_totalCollected >= _totalSpawned) {
            _allCollectedPublished = true;
            RoslikeTCPServer conn = RoslikeTCPServer.GetInstance();
            conn.Publish(AllCollectedTopic, new BoolMessage { data = true });
            Debug.Log($"RewardObjectLoader: ALL REWARDS COLLECTED ({_totalCollected}/{_totalSpawned}) — published on {AllCollectedTopic}");
        }
    }

    /// <summary>
    /// Returns true if every chunk that overlaps the world boundary has been generated at LOD0.
    /// This ensures we know the total spawn count before declaring "all collected".
    /// </summary>
    private bool AllVisitableChunksGenerated() {
        // Compute the range of chunks that could overlap the boundary
        int minCX = Mathf.FloorToInt(_boundaryMinX / _chunkWidth);
        int maxCX = Mathf.FloorToInt(_boundaryMaxX / _chunkWidth);
        int minCZ = Mathf.FloorToInt(_boundaryMinZ / _chunkWidth);
        int maxCZ = Mathf.FloorToInt(_boundaryMaxZ / _chunkWidth);

        for (int cx = minCX; cx <= maxCX; cx++) {
            for (int cz = minCZ; cz <= maxCZ; cz++) {
                Vector2Int id = new Vector2Int(cx, cz);
                if (IsChunkVisitable(id) && !_generatedChunks.Contains(id))
                    return false;
            }
        }
        return true;
    }

    // ─────────────────────────────────────────────
    //  Chunk events (uniform mode)
    // ─────────────────────────────────────────────

    public override void GenerateChunk(int cx, int cz, int lod) {
        if (lod != 0) return;
        if (!_paramsLoaded) LoadParams();
        if (_rewardPrefab == null || uniformDensity <= 0f) return;

        Vector2Int chunkID = new Vector2Int(cx, cz);

        if (_generatedChunks.Contains(chunkID)) {
            if (_chunkObjects.TryGetValue(chunkID, out GameObject chunkObj))
                chunkObj.SetActive(true);
            return;
        }

        GenerateUniformChunk(chunkID);

        // A new chunk was generated — recheck all-collected (we now know more of the world)
        CheckAllCollected();
    }

    public override void ClearChunk(int cx, int cz, int lod) {
        if (lod != 0) return;

        Vector2Int chunkID = new Vector2Int(cx, cz);
        if (_chunkObjects.TryGetValue(chunkID, out GameObject obj))
            obj.SetActive(false);
    }

    private void GenerateUniformChunk(Vector2Int chunkID) {
        float originX = chunkID.x * _chunkWidth;
        float originZ = chunkID.y * _chunkWidth;

        int chunkSeed = _rewardSeed ^ (chunkID.x * 1000003) ^ (chunkID.y * 999983);
        System.Random rng = new System.Random(chunkSeed);

        int count = Mathf.RoundToInt(uniformDensity * _chunkWidth * _chunkWidth);

        GameObject chunkObj = new GameObject($"RewardChunk_{chunkID.x}_{chunkID.y}");
        chunkObj.transform.SetParent(transform);
        _chunkObjects[chunkID] = chunkObj;
        _generatedChunks.Add(chunkID);

        int placed = 0;
        for (int i = 0; i < count; i++) {
            float x = originX + (float)rng.NextDouble() * _chunkWidth;
            float z = originZ + (float)rng.NextDouble() * _chunkWidth;

            // Skip positions outside the boundary buffer zone
            if (!IsInsideBoundaryBuffer(x, z)) continue;

            float y = WorldServices.Get<IHeightProvider>().GetTerrainHeight(x, z);

            Instantiate(_rewardPrefab, new Vector3(x, y, z),
                Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), chunkObj.transform);
            placed++;
        }

        _totalSpawned += placed;

        if (verbose && placed > 0)
            Debug.Log($"RewardObjectLoader: uniform chunk ({chunkID.x},{chunkID.y}) placed {placed} (total spawned: {_totalSpawned})");
    }

    // ─────────────────────────────────────────────
    //  Structure events (structure mode)
    // ─────────────────────────────────────────────

    public override void OnWorldStructureLoaded(WorldStructure s, int lod) {
        if (!_paramsLoaded) LoadParams();
        if (_rewardPrefab == null || _structureEntries.Count == 0) return;

        StructureEntry? entry = FindEntry(s.structureType);
        if (!entry.HasValue) return;

        // Destroy previous container (handles LOD change / double-fire).
        DestroyContainer(s);

        Transform lodRoot = s.transform.Find($"LOD{lod}");
        if (lodRoot == null) return;

        Transform group = lodRoot.Find("rewardSpawnPositions");
        if (group == null || group.childCount == 0) {
            if (verbose) Debug.Log($"RewardObjectLoader: '{s.name}' LOD{lod} has no rewardSpawnPositions");
            return;
        }

        System.Random rng = MakeStructureRng(s);

        // Per-structure skip chance
        if ((float)rng.NextDouble() < entry.Value.skipProbability) {
            if (verbose) Debug.Log($"RewardObjectLoader: '{s.name}' skipped (skipProb={entry.Value.skipProbability:F2})");
            return;
        }

        GameObject container = new GameObject(ContainerName);
        container.transform.SetParent(s.transform, false);
        container.transform.SetPositionAndRotation(s.transform.position, s.transform.rotation);

        int placed = 0;
        foreach (Transform spawnPoint in group) {
            if ((float)rng.NextDouble() > entry.Value.spawnProbability) continue;
            Instantiate(_rewardPrefab, spawnPoint.position, spawnPoint.rotation, container.transform);
            placed++;
        }

        _totalSpawned += placed;

        if (verbose)
            Debug.Log($"RewardObjectLoader: '{s.name}' LOD{lod} — " +
                $"{placed}/{group.childCount} rewards placed (total spawned: {_totalSpawned})");

        // Remove empty container
        if (placed == 0)
            DestroyImmediate(container);
    }

    public override void OnWorldStructureUnloaded(WorldStructure s, int lod) {
        if (_structureEntries.Count == 0) return;
        if (!FindEntry(s.structureType).HasValue) return;
        DestroyContainer(s);
    }

    public override void Clear() {
        foreach (var kvp in _chunkObjects)
            if (kvp.Value != null) Destroy(kvp.Value);
        _chunkObjects.Clear();
        _generatedChunks.Clear();
        // Structure containers are children of structure GOs — destroyed with them.
        _paramsLoaded = false;
        _totalSpawned = 0;
        _totalCollected = 0;
        _allCollectedPublished = false;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    private StructureEntry? FindEntry(string structureType) {
        for (int i = 0; i < _structureEntries.Count; i++) {
            if (structureType.StartsWith(_structureEntries[i].type, System.StringComparison.OrdinalIgnoreCase))
                return _structureEntries[i];
        }
        return null;
    }

    private System.Random MakeStructureRng(WorldStructure s) {
        Vector2 center = s.GetCenter2D();
        int seed = _rewardSeed
            ^ (Mathf.RoundToInt(center.x * 100f) * 1000003)
            ^ (Mathf.RoundToInt(center.y * 100f) * 999983);
        return new System.Random(seed);
    }

    private static void DestroyContainer(WorldStructure s) {
        Transform existing = s.transform.Find(ContainerName);
        if (existing != null)
            DestroyImmediate(existing.gameObject);
    }
}
