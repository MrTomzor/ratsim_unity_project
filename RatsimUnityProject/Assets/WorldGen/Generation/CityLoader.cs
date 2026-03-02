using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// WorldStructureLoader that responds to city structures being loaded and fills them
/// with procedurally placed house prefabs.
///
/// House prefabs are any WorldStructure prefabs in Resources/WorldGen/WorldStructurePrefabs/
/// whose name starts with "house" (case-insensitive).
///
/// Houses are spawned as children of the city WorldStructure, so they are automatically
/// destroyed when the city is destroyed at episode end.
/// </summary>
public class CityLoader : WorldStructureLoader {

    public bool verbose = false;

    [Header("Layout")]
    public float houseSpacing         = 5f;
    public int   maxHouses            = 40;
    public int   maxPlacementAttempts = 50;

    private WorldStructure[]                 _housePrefabs;
    private readonly HashSet<WorldStructure> _processedCities = new HashSet<WorldStructure>();

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
    //  WorldStructureLoader
    // ─────────────────────────────────────────────

    // Eagerly populate all cities that WorldLayoutLoader.Initialize() has already placed,
    // so house footprints are in WorldData before AgentLoader.Initialize() needs them.
    // The _processedCities guard prevents double-processing via OnWorldStructureLoaded later.
    public override void Initialize() {
        if (_housePrefabs.Length == 0) return;
        foreach (WorldStructure s in WorldData.GetStructures()) {
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
            WorldStructure prefab = _housePrefabs[rng.Next(_housePrefabs.Length)];
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
