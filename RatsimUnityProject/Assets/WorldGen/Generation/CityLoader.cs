using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// WorldStructureProvider that responds to city structures being loaded and fills them
/// with procedurally placed house prefabs.
///
/// House prefabs are any WorldStructure prefabs in Resources/WorldGen/WorldStructurePrefabs/
/// whose name starts with "house" (case-insensitive).
///
/// Houses are spawned as children of the city WorldStructure, so they are automatically
/// destroyed when the city is destroyed at episode end.
/// </summary>
public class CityLoader : WorldStructureProvider {

    public override WorldDataType[] Provides => new[] { WorldDataType.StructureContent };
    public override WorldDataType[] DependsOn => new[] { WorldDataType.StructureEvents };

    public bool verbose = false;

    [Header("Layout")]
    public float houseSpacing         = 5f;
    public int   maxHouses            = 40;
    public int   maxPlacementAttempts = 50;

    [Header("Grid Layout")]
    public string layoutMode      = "random";
    public float  gridRoadSpacing = 30f;
    public float  gridSpacingX    = 0f;   // 0 = use gridRoadSpacing
    public float  gridSpacingZ    = 0f;   // 0 = use gridRoadSpacing
    public float  gridMargin      = 15f;
    public float  gridRoadWidth   = 6f;

    [Header("House Selection")]
    // Comma list of house prefab names to use (must exist in Resources/WorldGen/WorldStructurePrefabs/
    // and start with "house"). Empty = use all discovered "house*" prefabs.
    public string allowedHousePrefabs = "";

    private WorldStructure[]                 _housePrefabs;
    private readonly HashSet<WorldStructure> _processedCities = new HashSet<WorldStructure>();

    private struct WeightedHouse {
        public WorldStructure prefab;
        public float          weight;
    }
    private readonly List<WeightedHouse> _activeHouseEntries = new List<WeightedHouse>();
    private float _houseWeightSum;
    private bool  _paramsLoaded;

    private const string HousePrefabPath   = "WorldGen/WorldStructurePrefabs/";
    private const string HousePrefabPrefix = "house";

    private void Awake() {
        _housePrefabs = Resources.LoadAll<WorldStructure>(HousePrefabPath)
            .Where(p => p.name.StartsWith(HousePrefabPrefix, System.StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (_housePrefabs.Length == 0)
            Debug.LogWarning($"CityLoader: no house prefabs found in Resources/{HousePrefabPath}");
        else if (verbose)
            Debug.Log($"CityLoader: loaded {_housePrefabs.Length} house prefab(s): " +
                      string.Join(", ", _housePrefabs.Select(p => p.name)));
    }

    // ─────────────────────────────────────────────
    //  WorldStructureProvider
    // ─────────────────────────────────────────────

    // Eagerly populate all cities that WorldLayoutLoader.Generate() has already placed,
    // so house footprints are in WorldData before AgentLoader.Generate() needs them.
    // The _processedCities guard prevents double-processing via OnWorldStructureLoaded later.
    public override void Generate() {
        if (_housePrefabs.Length == 0) return;
        if (!_paramsLoaded) LoadParams();
        if (_activeHouseEntries.Count == 0) return;
        foreach (WorldStructure s in WorldData.GetStructures().ToList()) {
            if (s.structureType != "city") continue;
            if (_processedCities.Contains(s)) continue;
            _processedCities.Add(s);
            GenerateCityHouses(s);
        }
    }

    public override void OnWorldStructureLoaded(WorldStructure s, int lod) {
        if (s.structureType != "city") return;
        if (_housePrefabs.Length == 0) return;
        if (_processedCities.Contains(s)) return;
        if (!_paramsLoaded) LoadParams();
        if (_activeHouseEntries.Count == 0) return;

        _processedCities.Add(s);
        GenerateCityHouses(s);
    }

    public override void OnWorldStructureUnloaded(WorldStructure s, int lod) {
        // Houses are children of the city GO — they are destroyed automatically when
        // the city is destroyed (episode clear). Just remove from the processed set
        // so the city can be re-populated if loaded again.
        _processedCities.Remove(s);
    }

    public override void Clear() {
        // Houses are children of city GOs — destroyed when WorldLayoutLoader destroys cities.
        _processedCities.Clear();
        _activeHouseEntries.Clear();
        _houseWeightSum = 0f;
        _paramsLoaded   = false;
    }

    // ─────────────────────────────────────────────
    //  Param loading (weighted house selection)
    // ─────────────────────────────────────────────

    private void LoadParams() {
        _activeHouseEntries.Clear();
        _houseWeightSum = 0f;

        string allowed = WorldLoadingController.GetParamString("city/allowed_houses", allowedHousePrefabs);

        IEnumerable<WorldStructure> candidates;
        if (string.IsNullOrWhiteSpace(allowed)) {
            candidates = _housePrefabs;
        } else {
            List<WorldStructure> list = new List<WorldStructure>();
            foreach (string raw in allowed.Split(',')) {
                string name = raw.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                WorldStructure prefab = _housePrefabs.FirstOrDefault(p => p.name == name);
                if (prefab == null) {
                    Debug.LogWarning($"CityLoader: house prefab '{name}' not found in Resources/{HousePrefabPath} (must start with '{HousePrefabPrefix}')");
                    continue;
                }
                list.Add(prefab);
            }
            candidates = list;
        }

        foreach (WorldStructure prefab in candidates) {
            float weight = WorldLoadingController.GetParamFloat($"city/{prefab.name}/probability", 1f);
            if (weight <= 0f) continue;
            _activeHouseEntries.Add(new WeightedHouse { prefab = prefab, weight = weight });
            _houseWeightSum += weight;
        }

        _paramsLoaded = true;

        if (_activeHouseEntries.Count == 0) {
            Debug.LogWarning("CityLoader: no active house prefabs after applying 'city/allowed_houses' / 'city/*/probability' filters — cities will be empty");
        } else if (verbose) {
            Debug.Log($"CityLoader: active house prefabs — " +
                string.Join(", ", _activeHouseEntries.Select(e => $"{e.prefab.name}(w={e.weight:F2})")) +
                $" (totalWeight={_houseWeightSum:F2})");
        }
    }

    private WorldStructure PickHouse(System.Random rng) {
        float r   = (float)(rng.NextDouble() * _houseWeightSum);
        float acc = 0f;
        for (int i = 0; i < _activeHouseEntries.Count; i++) {
            acc += _activeHouseEntries[i].weight;
            if (r <= acc) return _activeHouseEntries[i].prefab;
        }
        return _activeHouseEntries[_activeHouseEntries.Count - 1].prefab;
    }

    // ─────────────────────────────────────────────
    //  House generation
    // ─────────────────────────────────────────────

    private void GenerateCityHouses(WorldStructure city) {
        int seed = WorldLoadingController.GetDerivedSeed("city")
                   ^ (Mathf.RoundToInt(city.GetCenter2D().x) * 1000003)
                   ^ (Mathf.RoundToInt(city.GetCenter2D().y) * 999983);
        System.Random rng = new System.Random(seed);

        Bounds2D cityBounds = city.GetBoundingBox2D();
        float    cityRotCCW = city.GetRotationCCW();

        string layoutMode = WorldLoadingController.GetParamString("city/layout_mode", this.layoutMode);
        if (layoutMode == "grid") {
            GenerateGridLayout(city, rng, cityBounds, cityRotCCW);
            return;
        }

        float spacing  = WorldLoadingController.GetParamFloat("city/house_spacing",  houseSpacing);
        int   maxCount = WorldLoadingController.GetParamInt  ("city/max_houses",     maxHouses);
        int   maxTries = WorldLoadingController.GetParamInt  ("city/max_attempts",   maxPlacementAttempts);

        // Snapshot global obstacles once to avoid repeated WorldData queries.
        List<Bounds2D> globalObstacles = WorldData.GetStructures()
            .Where(s => s != city)
            .Select(s => s.GetBoundingBox2D())
            .ToList();

        List<Bounds2D> placedHouseBounds = new List<Bounds2D>();
        int placed = 0;

        for (int attempt = 0; attempt < maxCount * maxTries && placed < maxCount; attempt++) {
            WorldStructure prefab = PickHouse(rng);
            Vector2        hSize  = GetPrefabSize(prefab);
            if (hSize.sqrMagnitude < 0.01f) continue;

            float houseRotCCW = cityRotCCW + rng.Next(4) * 90f;

            float halfW = cityBounds.size.x * 0.5f - hSize.x * 0.5f - spacing;
            float halfH = cityBounds.size.y * 0.5f - hSize.y * 0.5f - spacing;
            if (halfW <= 0f || halfH <= 0f) {
                Debug.LogWarning($"CityLoader: city '{city.name}' is too small for house prefab '{prefab.name}' with spacing {spacing}");
                break;
            }

            float   localX   = (float)(rng.NextDouble() * 2.0 - 1.0) * halfW;
            float   localZ   = (float)(rng.NextDouble() * 2.0 - 1.0) * halfH;
            Vector2 worldPos = CityLocalToWorld(new Vector2(localX, localZ), cityBounds);

            Bounds2D candidate = new Bounds2D(worldPos, hSize + Vector2.one * spacing * 2f, houseRotCCW);

            if (placedHouseBounds.Any(b => b.Overlaps(candidate))) continue;
            if (globalObstacles.Any(b => b.Overlaps(candidate))) continue;

            // Spawn parented to city — auto-destroyed when city is cleared.
            WorldStructure house = WorldData.SpawnStructure(
                prefab.name, worldPos, houseRotCCW, city.transform
            );
            if (house == null) continue;

            placedHouseBounds.Add(new Bounds2D(worldPos, hSize, houseRotCCW));
            placed++;

            if (verbose) Debug.Log($"CityLoader: placed '{prefab.name}' at {worldPos}, rot={houseRotCCW:F1}°");
        }

        if (verbose || placed < 1)
            Debug.Log($"CityLoader: placed {placed}/{maxCount} houses in '{city.name}'");
    }

    // ─────────────────────────────────────────────
    //  Grid layout mode
    // ─────────────────────────────────────────────

    private void GenerateGridLayout(WorldStructure city, System.Random rng, Bounds2D cityBounds, float cityRotCCW) {
        float spacingDefault = WorldLoadingController.GetParamFloat("city/grid_road_spacing", gridRoadSpacing);
        float rawSpacingX    = WorldLoadingController.GetParamFloat("city/grid_road_spacing_x", gridSpacingX);
        float rawSpacingZ    = WorldLoadingController.GetParamFloat("city/grid_road_spacing_z", gridSpacingZ);
        float spacingX  = rawSpacingX > 0f ? rawSpacingX : spacingDefault;
        float spacingZ  = rawSpacingZ > 0f ? rawSpacingZ : spacingDefault;
        float margin    = WorldLoadingController.GetParamFloat("city/grid_margin",     gridMargin);
        float roadWidth = WorldLoadingController.GetParamFloat("city/grid_road_width", gridRoadWidth);

        float innerW = cityBounds.size.x - 2f * margin;
        float innerH = cityBounds.size.y - 2f * margin;
        if (innerW <= 0f || innerH <= 0f) {
            Debug.LogWarning($"CityLoader: city '{city.name}' too small for grid layout (margin={margin})");
            return;
        }

        // 3a. Compute grid lines (spacingX → N-S road separation, spacingZ → E-W road separation)
        int nX = Mathf.Max(2, Mathf.FloorToInt(innerW / spacingX) + 1);
        int nZ = Mathf.Max(2, Mathf.FloorToInt(innerH / spacingZ) + 1);
        float actualSpacingX = innerW / (nX - 1);
        float actualSpacingZ = innerH / (nZ - 1);

        float[] gridX = new float[nX];
        float[] gridZ = new float[nZ];
        for (int i = 0; i < nX; i++) gridX[i] = -innerW / 2f + i * actualSpacingX;
        for (int j = 0; j < nZ; j++) gridZ[j] = -innerH / 2f + j * actualSpacingZ;

        // 3b. Spawn grid roads
        // E-W roads (one per gridZ value)
        for (int j = 0; j < nZ; j++) {
            Vector2 localMid = new Vector2(0f, gridZ[j]);
            Vector2 worldMid = CityLocalToWorld(localMid, cityBounds);
            WorldData.SpawnStructure(
                "road", worldMid, cityRotCCW, city.transform,
                sizeOverride: new Vector2(innerW, roadWidth)
            );
        }

        // N-S roads (one per gridX value)
        for (int i = 0; i < nX; i++) {
            Vector2 localMid = new Vector2(gridX[i], 0f);
            Vector2 worldMid = CityLocalToWorld(localMid, cityBounds);
            WorldData.SpawnStructure(
                "road", worldMid, cityRotCCW + 90f, city.transform,
                sizeOverride: new Vector2(innerH, roadWidth)
            );
        }

        if (verbose)
            Debug.Log($"CityLoader grid: {nX} N-S roads, {nZ} E-W roads in '{city.name}'");

        // 3c. Connect entry point stubs
        if (WorldServices.Has<ILayoutProvider>()) {
            List<EntryPoint> entryPoints = WorldServices.Get<ILayoutProvider>().GetEntryPoints(city);
            float halfW = cityBounds.size.x * 0.5f;
            float halfH = cityBounds.size.y * 0.5f;

            foreach (EntryPoint ep in entryPoints) {
                Vector2 localEP = WorldToLocal(ep.position, cityBounds);

                // Determine which OBB edge the EP is on
                float ratioX = Mathf.Abs(localEP.x) / halfW;
                float ratioZ = Mathf.Abs(localEP.y) / halfH;

                Vector2 target;
                if (ratioX > ratioZ) {
                    // On E or W edge → connect to nearest gridZ at ±innerW/2
                    float nearestZ = FindNearest(gridZ, localEP.y);
                    float edgeX = Mathf.Sign(localEP.x) * innerW / 2f;
                    target = new Vector2(edgeX, nearestZ);
                } else {
                    // On N or S edge → connect to nearest gridX at ±innerH/2
                    float nearestX = FindNearest(gridX, localEP.x);
                    float edgeZ = Mathf.Sign(localEP.y) * innerH / 2f;
                    target = new Vector2(nearestX, edgeZ);
                }

                Vector2 targetWorld = CityLocalToWorld(target, cityBounds);
                Vector2 stubMid     = (ep.position + targetWorld) * 0.5f;
                float   stubLength  = Vector2.Distance(ep.position, targetWorld);
                float   stubAngle   = Mathf.Atan2(
                    targetWorld.y - ep.position.y,
                    targetWorld.x - ep.position.x) * Mathf.Rad2Deg;

                if (stubLength > 0.1f) {
                    WorldData.SpawnStructure(
                        "road", stubMid, stubAngle, city.transform,
                        sizeOverride: new Vector2(stubLength, roadWidth)
                    );
                }
            }

            if (verbose && entryPoints.Count > 0)
                Debug.Log($"CityLoader grid: connected {entryPoints.Count} entry point stubs in '{city.name}'");
        }

        // 3d. Place houses in blocks
        List<Bounds2D> placedHouseBounds = new List<Bounds2D>();
        int totalPlaced = 0;

        for (int i = 0; i < nX - 1; i++) {
            for (int j = 0; j < nZ - 1; j++) {
                float blockXMin = gridX[i]     + roadWidth / 2f;
                float blockXMax = gridX[i + 1] - roadWidth / 2f;
                float blockZMin = gridZ[j]     + roadWidth / 2f;
                float blockZMax = gridZ[j + 1] - roadWidth / 2f;

                float blockW = blockXMax - blockXMin;
                float blockH = blockZMax - blockZMin;
                if (blockW < 4f || blockH < 4f) continue;

                // Place houses along each edge of the block
                // North row (near blockZMax): door faces +Z → rot offset 0
                totalPlaced += PlaceHouseRow(rng, city, cityBounds, cityRotCCW,
                    blockXMin, blockXMax, blockZMax, true, 0f,
                    placedHouseBounds);

                // South row (near blockZMin): door faces -Z → rot offset 180
                totalPlaced += PlaceHouseRow(rng, city, cityBounds, cityRotCCW,
                    blockXMin, blockXMax, blockZMin, true, 180f,
                    placedHouseBounds);

                // East row (near blockXMax): door faces +X → rot offset 270
                // Inset by one house depth to avoid corner overlap
                float cornerInset = GetSmallestHouseDepth();
                float rowZMin = blockZMin + cornerInset;
                float rowZMax = blockZMax - cornerInset;
                if (rowZMax > rowZMin) {
                    totalPlaced += PlaceHouseRow(rng, city, cityBounds, cityRotCCW,
                        rowZMin, rowZMax, blockXMax, false, 270f,
                        placedHouseBounds);
                }

                // West row (near blockXMin): door faces -X → rot offset 90
                if (rowZMax > rowZMin) {
                    totalPlaced += PlaceHouseRow(rng, city, cityBounds, cityRotCCW,
                        rowZMin, rowZMax, blockXMin, false, 90f,
                        placedHouseBounds);
                }
            }
        }

        if (verbose || totalPlaced < 1)
            Debug.Log($"CityLoader grid: placed {totalPlaced} houses in '{city.name}'");
    }

    /// <summary>
    /// Place a row of houses along one edge of a block.
    /// </summary>
    /// <param name="horizontal">true = row runs along X axis (N/S edge), false = along Z axis (E/W edge)</param>
    /// <param name="rotOffset">Rotation offset from city rotation for door direction</param>
    private const float GridHouseGap = 0.5f; // tight packing gap between houses in grid mode

    private int PlaceHouseRow(System.Random rng, WorldStructure city, Bounds2D cityBounds, float cityRotCCW,
        float lineMin, float lineMax, float perpCoord, bool horizontal, float rotOffset,
        List<Bounds2D> placedHouseBounds) {

        int placed = 0;
        float cursor = lineMin;
        float houseRotCCW = cityRotCCW + rotOffset;

        while (cursor < lineMax) {
            WorldStructure prefab = PickHouse(rng);
            Vector2 hSize = GetPrefabSize(prefab);
            if (hSize.sqrMagnitude < 0.01f) continue;

            // For horizontal rows, house width runs along X; for vertical, along Z
            float houseWidth = horizontal ? hSize.x : hSize.y;
            float houseDepth = horizontal ? hSize.y : hSize.x;

            if (cursor + houseWidth > lineMax) break;

            float alongCenter = cursor + houseWidth / 2f;

            // Offset house center inward from the road edge by half its depth
            float perpCenter;
            if (rotOffset == 0f)       perpCenter = perpCoord - houseDepth / 2f;  // North: shift -Z
            else if (rotOffset == 180f) perpCenter = perpCoord + houseDepth / 2f; // South: shift +Z
            else if (rotOffset == 270f) perpCenter = perpCoord - houseDepth / 2f; // East: shift -X
            else                         perpCenter = perpCoord + houseDepth / 2f; // West: shift +X

            Vector2 localPos = horizontal
                ? new Vector2(alongCenter, perpCenter)
                : new Vector2(perpCenter, alongCenter);

            Vector2 worldPos = CityLocalToWorld(localPos, cityBounds);
            Bounds2D candidate = new Bounds2D(worldPos, hSize, houseRotCCW);

            // Block edges are already inset by roadWidth/2, so no road overlap check needed.
            if (!placedHouseBounds.Any(b => b.Overlaps(candidate))) {
                WorldStructure house = WorldData.SpawnStructure(
                    prefab.name, worldPos, houseRotCCW, city.transform
                );
                if (house != null) {
                    placedHouseBounds.Add(candidate);
                    placed++;
                }
            }

            cursor += houseWidth + GridHouseGap;
        }

        return placed;
    }

    private float GetSmallestHouseDepth() {
        float minDepth = float.MaxValue;
        IEnumerable<WorldStructure> source = _activeHouseEntries.Count > 0
            ? _activeHouseEntries.Select(e => e.prefab)
            : (IEnumerable<WorldStructure>)_housePrefabs;
        foreach (var prefab in source) {
            Vector2 size = GetPrefabSize(prefab);
            if (size.sqrMagnitude > 0.01f)
                minDepth = Mathf.Min(minDepth, Mathf.Min(size.x, size.y));
        }
        return minDepth == float.MaxValue ? 0f : minDepth;
    }

    private float FindNearest(float[] values, float target) {
        float best = values[0];
        float bestDist = Mathf.Abs(target - best);
        for (int i = 1; i < values.Length; i++) {
            float d = Mathf.Abs(target - values[i]);
            if (d < bestDist) { best = values[i]; bestDist = d; }
        }
        return best;
    }

    private Vector2 WorldToLocal(Vector2 worldPos, Bounds2D cityBounds) {
        Vector2 delta = worldPos - cityBounds.center;
        float   rad   = -cityBounds.rotation * Mathf.Deg2Rad;
        float   cosR  = Mathf.Cos(rad), sinR = Mathf.Sin(rad);
        return new Vector2(
            delta.x * cosR - delta.y * sinR,
            delta.x * sinR + delta.y * cosR
        );
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    private Vector2 CityLocalToWorld(Vector2 localPos, Bounds2D cityBounds) {
        float   rad  = cityBounds.rotation * Mathf.Deg2Rad;
        float   cosR = Mathf.Cos(rad), sinR = Mathf.Sin(rad);
        Vector2 rot  = new Vector2(
            localPos.x * cosR - localPos.y * sinR,
            localPos.x * sinR + localPos.y * cosR
        );
        return cityBounds.center + rot;
    }

    private Vector2 GetPrefabSize(WorldStructure prefab) {
        if (prefab == null || prefab.footprintCollider == null) return Vector2.zero;
        Vector3 s  = prefab.footprintCollider.size;
        Vector3 ls = prefab.footprintCollider.transform.lossyScale;
        return new Vector2(s.x * ls.x, s.z * ls.z);
    }
}
