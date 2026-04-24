using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Alternate layout provider that generates simple rooms-and-corridors mazes on
/// a 2D binary mask and materializes wall cells as chunk-local "maze_block"
/// WorldStructures. Active when "layout/mode" = "maze".
///
/// Generation pipeline (all in Generate()):
///   1. Allocate a binary mask sized (worldWidth / cell_size) x (worldHeight / cell_size).
///      All cells start as walls (true).
///   2. Rejection-sample N rectangular rooms with a configurable minimum
///      separation, then carve their interiors to floor (false).
///   3. Build a complete graph over room centers, connect via MST + a fraction
///      of extra edges (for loopy mazes). Each edge is an L-shaped corridor
///      (one bend) of a fixed cell-width carved through the mask.
///   4. Spawn a "maze_room" WorldStructure per room (no physics, footprint-only,
///      exposed for spawn-constraint queries via IRoomProvider).
///
/// Chunk loading (GenerateChunk / ClearChunk at LOD0):
///   - For each wall cell whose center falls inside the requested chunk, spawn
///     a "maze_block" WorldStructure at the cell center, scaled to (cell_size,
///     wall_height, cell_size). The block has a single BoxCollider that serves
///     as both physics collider (Default layer) and WorldStructure footprint,
///     and a NamedSemanticObject("maze_wall") for lidar.
///   - Blocks are tracked per chunk and destroyed in ClearChunk.
///
/// The "mode" param lets us add alternate maze flavors later without touching
/// the switching logic or the chunk spawning machinery.
///
/// Config params (all under "maze/" unless noted):
///   layout/mode                  -- "default" (disables this loader) or "maze" (default "default")
///   maze/mode                    -- maze generator variant (default "rooms_and_corridors")
///   maze/cell_size               -- world units per mask cell (default 1)
///   maze/wall_height             -- Y scale of each block (default 3)
///   maze/n_rooms                 -- target number of rooms (default 8)
///   maze/room_min_size_cells     -- min room side in cells (default 6)
///   maze/room_max_size_cells     -- max room side in cells (default 12)
///   maze/room_min_separation_cells -- min gap between room rects, in cells (default 2)
///   maze/room_max_attempts       -- rejection-sampling budget per room (default 200)
///   maze/corridor_width_cells    -- corridor width in cells (default 3)
///   maze/extra_corridor_fraction -- [0,1]; extras beyond MST as fraction of non-tree pairs (default 0.3)
///   maze/border_walls            -- 0/1; stamp a 1-cell-thick wall ring along the mask edge (default 1)
///   maze/semantic_name           -- semantic name used for blocks (default "maze_wall")
///
/// Corridor structure spawning (optional — empty list disables the whole feature):
///   maze/corridor_structures/types               -- comma list of prefab names to attempt per corridor segment
///   maze/corridor_structures/max_per_corridor    -- global attempt budget per corridor segment (default 0 = off)
///   maze/corridor_structures/{type}/chance                    -- per-attempt spawn chance [0,1] (default 1)
///   maze/corridor_structures/{type}/scale_width_with_corridor -- 0/1; size.y (perpendicular to corridor) = corridor_width (default 0)
///   maze/corridor_structures/{type}/scale_length_with_corridor-- 0/1; size.x (along corridor) = segment length, centered, consumes segment (default 0)
///   maze/corridor_structures/{type}/align_with_corridor       -- 0/1; rotate so size.x is along corridor direction (default 1)
///   maze/corridor_structures/{type}/randomize_direction       -- 0/1; randomly flip 180° along corridor axis (default 0)
///
/// Per attempt (max_per_corridor times per corridor segment), entries are tried in list order and the first
/// one whose `chance` roll succeeds is placed — so list order is priority order. Placed structures occupy an
/// interval along the corridor and won't overlap other already-placed structures in the same segment.
/// </summary>
public class MazeLayoutLoader : WorldDataProvider, ILayoutProvider, IRoomProvider {

    public override WorldDataType[] Provides => new[] { WorldDataType.Layout };
    public override WorldDataType[] DependsOn => new[] { WorldDataType.Height };

    [Header("Debug")]
    public bool verbose = false;

    [Header("Defaults (overridden by config params)")]
    public float cellSize = 1f;
    public float wallHeight = 3f;
    public int nRooms = 8;
    public int roomMinSizeCells = 6;
    public int roomMaxSizeCells = 12;
    public int roomMinSeparationCells = 2;
    public int roomMaxAttempts = 200;
    public int corridorWidthCells = 3;
    public float extraCorridorFraction = 0.3f;
    public bool borderWalls = true;
    public string semanticName = "maze_wall";

    // ─────────────────────────────────────────────
    //  Runtime state
    // ─────────────────────────────────────────────

    // true = wall, false = floor
    private bool[,] _mask;
    private int _maskW, _maskH;       // cells along X (width) and Z (height)
    private float _worldW, _worldH;   // cached world bounds
    private float _cellSize;          // active cell size for this episode
    private float _originX, _originZ; // world coords of mask[0,0] corner

    // room footprints in world coords (for IRoomProvider)
    private readonly List<Bounds2D> _rooms = new List<Bounds2D>();
    private readonly List<WorldStructure> _roomStructures = new List<WorldStructure>();

    // corridor segments, filled by CarveLCorridor → RecordSegment
    private readonly List<CorridorSegment> _corridors = new List<CorridorSegment>();

    // per-chunk spawned blocks, for ClearChunk
    private readonly Dictionary<Vector2Int, List<WorldStructure>> _chunkBlocks
        = new Dictionary<Vector2Int, List<WorldStructure>>();

    // chunks we've already generated blocks for (guards against double-spawn on LOD upgrades)
    private readonly HashSet<Vector2Int> _generatedChunks = new HashSet<Vector2Int>();

    private bool _active;       // this loader owns the Layout for this episode
    private bool _generated;    // mask + rooms built this episode

    // ─────────────────────────────────────────────
    //  WorldDataProvider
    // ─────────────────────────────────────────────

    protected override void OnEnable() {
        base.OnEnable();
        // IRoomProvider: safe to register unconditionally — GetRooms()/IsInRoom()
        // return empty when we're not the active layout provider.
        // ILayoutProvider: register only on Generate() when active, to avoid
        // stomping WorldLayoutLoader's registration in default mode.
        WorldServices.Register<IRoomProvider>(this);
    }

    public override void Generate() {
        if (_generated) return;

        string layoutMode = WorldLoadingController.GetParamString("layout/mode", "default");
        _active = layoutMode.Equals("maze", System.StringComparison.OrdinalIgnoreCase);
        if (!_active) {
            if (verbose) Debug.Log($"MazeLayoutLoader: layout/mode='{layoutMode}', inactive");
            return;
        }

        // Take over as the active layout provider for this episode.
        WorldServices.Register<ILayoutProvider>(this);

        LoadParams();

        string mazeMode = WorldLoadingController.GetParamString("maze/mode", "rooms_and_corridors");
        System.Random rng = new System.Random(WorldLoadingController.GetDerivedSeed("maze"));

        switch (mazeMode.ToLowerInvariant()) {
            case "rooms_and_corridors":
                GenerateRoomsAndCorridors(rng);
                break;
            default:
                Debug.LogWarning($"MazeLayoutLoader: unknown maze/mode '{mazeMode}', falling back to rooms_and_corridors");
                GenerateRoomsAndCorridors(rng);
                break;
        }

        SpawnRoomStructures();
        SpawnCorridorStructures(rng);
        _generated = true;

        Physics.SyncTransforms();
        WorldServices.Get<IHeightProvider>().ProcessTerrainModifications();

        if (verbose)
            Debug.Log($"MazeLayoutLoader: mask={_maskW}x{_maskH} cells, rooms={_rooms.Count}");
    }

    public override void GenerateChunk(int cx, int cz, int lod) {
        if (!_active || !_generated) return;
        if (lod != 0) return; // blocks are LOD0-only

        Vector2Int key = new Vector2Int(cx, cz);
        if (_generatedChunks.Contains(key)) return;
        _generatedChunks.Add(key);

        SpawnBlocksInChunk(cx, cz);
    }

    public override void ClearChunk(int cx, int cz, int lod) {
        if (lod != 0) return;
        Vector2Int key = new Vector2Int(cx, cz);
        if (!_chunkBlocks.TryGetValue(key, out var list)) return;

        foreach (WorldStructure ws in list)
            if (ws != null) DestroyImmediate(ws.gameObject);
        _chunkBlocks.Remove(key);
        _generatedChunks.Remove(key);
    }

    public override void Clear() {
        // Destroy all children (rooms + any stray blocks parented directly to us).
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        _chunkBlocks.Clear();
        _generatedChunks.Clear();
        _rooms.Clear();
        _roomStructures.Clear();
        _corridors.Clear();
        _mask = null;
        _generated = false;
        _active = false;
    }

    // ─────────────────────────────────────────────
    //  ILayoutProvider / IRoomProvider
    // ─────────────────────────────────────────────

    public List<EntryPoint> GetEntryPoints(WorldStructure s) {
        // Mazes don't expose road-network entry points. Return empty so CityLoader
        // (and any future consumers) don't crash.
        return new List<EntryPoint>();
    }

    public bool IsInRoom(float x, float z) {
        Vector2 p = new Vector2(x, z);
        for (int i = 0; i < _rooms.Count; i++)
            if (_rooms[i].Contains(p)) return true;
        return false;
    }

    public List<Bounds2D> GetRooms() => new List<Bounds2D>(_rooms);

    // ─────────────────────────────────────────────
    //  Params
    // ─────────────────────────────────────────────

    private void LoadParams() {
        _cellSize             = WorldLoadingController.GetParamFloat("maze/cell_size",                cellSize);
        wallHeight            = WorldLoadingController.GetParamFloat("maze/wall_height",              wallHeight);
        nRooms                = WorldLoadingController.GetParamInt  ("maze/n_rooms",                  nRooms);
        roomMinSizeCells      = WorldLoadingController.GetParamInt  ("maze/room_min_size_cells",      roomMinSizeCells);
        roomMaxSizeCells      = WorldLoadingController.GetParamInt  ("maze/room_max_size_cells",      roomMaxSizeCells);
        roomMinSeparationCells= WorldLoadingController.GetParamInt  ("maze/room_min_separation_cells",roomMinSeparationCells);
        roomMaxAttempts       = WorldLoadingController.GetParamInt  ("maze/room_max_attempts",        roomMaxAttempts);
        corridorWidthCells    = WorldLoadingController.GetParamInt  ("maze/corridor_width_cells",     corridorWidthCells);
        extraCorridorFraction = WorldLoadingController.GetParamFloat("maze/extra_corridor_fraction",  extraCorridorFraction);
        borderWalls           = WorldLoadingController.GetParamInt  ("maze/border_walls", borderWalls ? 1 : 0) != 0;
        semanticName          = WorldLoadingController.GetParamString("maze/semantic_name",           semanticName);

        _worldW = WorldLoadingController.GetParamFloat("world_bounds/width",  100f);
        _worldH = WorldLoadingController.GetParamFloat("world_bounds/height", 100f);

        if (_cellSize <= 0f) { Debug.LogError("MazeLayoutLoader: maze/cell_size must be > 0"); _cellSize = 1f; }

        _maskW = Mathf.Max(1, Mathf.FloorToInt(_worldW / _cellSize));
        _maskH = Mathf.Max(1, Mathf.FloorToInt(_worldH / _cellSize));
        // Align mask so cell [i,j] center maps to a world position — origin is the
        // bottom-left corner of the mask, centered on world (0,0).
        _originX = -_maskW * _cellSize * 0.5f;
        _originZ = -_maskH * _cellSize * 0.5f;

        // Clamp room size vs mask
        int maxSide = Mathf.Max(1, Mathf.Min(_maskW, _maskH) - 2);
        roomMinSizeCells = Mathf.Clamp(roomMinSizeCells, 1, maxSide);
        roomMaxSizeCells = Mathf.Clamp(roomMaxSizeCells, roomMinSizeCells, maxSide);
    }

    // ─────────────────────────────────────────────
    //  Mask generation: rooms + corridors
    // ─────────────────────────────────────────────

    private struct RoomRect {
        public int x0, z0, w, h; // cell-space, inclusive corner at (x0,z0), size (w,h)
        public int CenterX => x0 + w / 2;
        public int CenterZ => z0 + h / 2;
    }

    public struct CorridorSegment {
        public Vector2 start;     // world-space center of the starting cell
        public Vector2 end;       // world-space center of the ending cell
        public Vector2 direction; // unit vector start→end
        public float width;       // world units perpendicular to direction
        public float length;      // world units along direction
    }

    private void GenerateRoomsAndCorridors(System.Random rng) {
        _mask = new bool[_maskW, _maskH];
        for (int i = 0; i < _maskW; i++)
            for (int j = 0; j < _maskH; j++)
                _mask[i, j] = true; // start all wall

        // 1. place rooms via rejection sampling
        List<RoomRect> rooms = new List<RoomRect>();
        int attempts = 0;
        // reserve at least a 1-cell border so rooms don't touch world edge
        int border = borderWalls ? 1 : 0;
        while (rooms.Count < nRooms && attempts < nRooms * roomMaxAttempts) {
            attempts++;
            int w = rng.Next(roomMinSizeCells, roomMaxSizeCells + 1);
            int h = rng.Next(roomMinSizeCells, roomMaxSizeCells + 1);
            if (w >= _maskW - 2 * border || h >= _maskH - 2 * border) continue;
            int x0 = rng.Next(border, _maskW - border - w);
            int z0 = rng.Next(border, _maskH - border - h);
            RoomRect candidate = new RoomRect { x0 = x0, z0 = z0, w = w, h = h };
            if (rooms.Any(r => RoomsOverlapWithSeparation(r, candidate, roomMinSeparationCells))) continue;
            rooms.Add(candidate);
        }

        if (verbose) Debug.Log($"MazeLayoutLoader: placed {rooms.Count}/{nRooms} rooms after {attempts} attempts");

        // carve rooms
        foreach (RoomRect r in rooms) CarveRect(r.x0, r.z0, r.w, r.h);

        // 2. connect rooms: MST by distance + extras
        if (rooms.Count >= 2) ConnectRooms(rooms, rng);

        // 3. store room bounds in world coords for IRoomProvider
        _rooms.Clear();
        foreach (RoomRect r in rooms) {
            Vector2 center = new Vector2(
                _originX + (r.x0 + r.w * 0.5f) * _cellSize,
                _originZ + (r.z0 + r.h * 0.5f) * _cellSize
            );
            Vector2 size = new Vector2(r.w * _cellSize, r.h * _cellSize);
            _rooms.Add(new Bounds2D(center, size, 0f));
        }
    }

    private static bool RoomsOverlapWithSeparation(RoomRect a, RoomRect b, int sep) {
        // Expand a by sep on all sides; check AABB overlap with b.
        int ax0 = a.x0 - sep, az0 = a.z0 - sep;
        int ax1 = a.x0 + a.w + sep, az1 = a.z0 + a.h + sep;
        int bx0 = b.x0, bz0 = b.z0;
        int bx1 = b.x0 + b.w, bz1 = b.z0 + b.h;
        return ax0 < bx1 && ax1 > bx0 && az0 < bz1 && az1 > bz0;
    }

    private void CarveRect(int x0, int z0, int w, int h) {
        int x1 = Mathf.Min(_maskW, x0 + w);
        int z1 = Mathf.Min(_maskH, z0 + h);
        x0 = Mathf.Max(0, x0); z0 = Mathf.Max(0, z0);
        for (int i = x0; i < x1; i++)
            for (int j = z0; j < z1; j++)
                _mask[i, j] = false;
    }

    // L-corridor: horizontal segment then vertical, of width `corridorWidthCells`,
    // connecting (ax,az) to (bx,bz). Bend direction chosen randomly. Each straight
    // leg is also recorded as a CorridorSegment for later structure spawning.
    private void CarveLCorridor(int ax, int az, int bx, int bz, bool horizontalFirst) {
        int half = corridorWidthCells / 2;
        int lo = -half;
        int hi = corridorWidthCells - half; // exclusive — gives exactly corridorWidthCells wide
        float corridorW = corridorWidthCells * _cellSize;

        if (horizontalFirst) {
            CarveRect(Mathf.Min(ax, bx), az + lo, Mathf.Abs(bx - ax) + 1, hi - lo);
            RecordSegment(CellCenterToWorld(ax, az), CellCenterToWorld(bx, az), corridorW);
            CarveRect(bx + lo, Mathf.Min(az, bz), hi - lo, Mathf.Abs(bz - az) + 1);
            RecordSegment(CellCenterToWorld(bx, az), CellCenterToWorld(bx, bz), corridorW);
        } else {
            CarveRect(ax + lo, Mathf.Min(az, bz), hi - lo, Mathf.Abs(bz - az) + 1);
            RecordSegment(CellCenterToWorld(ax, az), CellCenterToWorld(ax, bz), corridorW);
            CarveRect(Mathf.Min(ax, bx), bz + lo, Mathf.Abs(bx - ax) + 1, hi - lo);
            RecordSegment(CellCenterToWorld(ax, bz), CellCenterToWorld(bx, bz), corridorW);
        }
    }

    private Vector2 CellCenterToWorld(int i, int j) => new Vector2(
        _originX + (i + 0.5f) * _cellSize,
        _originZ + (j + 0.5f) * _cellSize);

    private void RecordSegment(Vector2 start, Vector2 end, float width) {
        float length = Vector2.Distance(start, end);
        if (length < 1e-4f) return; // degenerate (room centers coincided on this axis)
        _corridors.Add(new CorridorSegment {
            start     = start,
            end       = end,
            direction = (end - start) / length,
            width     = width,
            length    = length
        });
    }

    private void ConnectRooms(List<RoomRect> rooms, System.Random rng) {
        int n = rooms.Count;
        // build edge list sorted by squared distance
        var edges = new List<(int i, int j, int d2)>();
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++) {
            int dx = rooms[i].CenterX - rooms[j].CenterX;
            int dz = rooms[i].CenterZ - rooms[j].CenterZ;
            edges.Add((i, j, dx*dx + dz*dz));
        }
        edges.Sort((a, b) => a.d2.CompareTo(b.d2));

        int[] parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }

        int mstAdded = 0, extrasAdded = 0;
        int nonTreePairs = Mathf.Max(0, edges.Count - (n - 1));
        int extraBudget = Mathf.Clamp(Mathf.RoundToInt(extraCorridorFraction * nonTreePairs), 0, edges.Count);

        foreach (var e in edges) {
            bool sameTree = Find(e.i) == Find(e.j);
            bool takeAsMst = !sameTree;
            bool takeAsExtra = sameTree && extrasAdded < extraBudget;
            if (!takeAsMst && !takeAsExtra) continue;

            CarveLCorridor(
                rooms[e.i].CenterX, rooms[e.i].CenterZ,
                rooms[e.j].CenterX, rooms[e.j].CenterZ,
                rng.Next(2) == 0
            );

            if (takeAsMst) { Union(e.i, e.j); mstAdded++; }
            else           { extrasAdded++; }
        }

        if (verbose)
            Debug.Log($"MazeLayoutLoader: corridors MST={mstAdded}, extras={extrasAdded}/{extraBudget}");
    }

    // ─────────────────────────────────────────────
    //  Chunk-level block spawning
    // ─────────────────────────────────────────────

    private void SpawnBlocksInChunk(int cx, int cz) {
        float cw = WorldLoadingController.GetChunkWidth();
        float chunkMinX = cx * cw, chunkMaxX = (cx + 1) * cw;
        float chunkMinZ = cz * cw, chunkMaxZ = (cz + 1) * cw;

        // Map chunk bounds to mask cell ranges. Cells whose center falls in
        // [chunkMin, chunkMax) belong to this chunk.
        int iMin = Mathf.Max(0, Mathf.FloorToInt((chunkMinX - _originX) / _cellSize));
        int iMax = Mathf.Min(_maskW - 1, Mathf.FloorToInt((chunkMaxX - _originX) / _cellSize));
        int jMin = Mathf.Max(0, Mathf.FloorToInt((chunkMinZ - _originZ) / _cellSize));
        int jMax = Mathf.Min(_maskH - 1, Mathf.FloorToInt((chunkMaxZ - _originZ) / _cellSize));

        // exclusive upper bounds: don't include cells whose center is >= chunkMax
        List<WorldStructure> blocks = new List<WorldStructure>();
        for (int i = iMin; i <= iMax; i++) {
            float cxw = _originX + (i + 0.5f) * _cellSize;
            if (cxw < chunkMinX || cxw >= chunkMaxX) continue;
            for (int j = jMin; j <= jMax; j++) {
                if (!_mask[i, j]) continue;
                float czw = _originZ + (j + 0.5f) * _cellSize;
                if (czw < chunkMinZ || czw >= chunkMaxZ) continue;

                WorldStructure ws = SpawnBlock(new Vector2(cxw, czw));
                if (ws != null) blocks.Add(ws);
            }
        }

        if (blocks.Count > 0) _chunkBlocks[new Vector2Int(cx, cz)] = blocks;
        if (verbose) Debug.Log($"MazeLayoutLoader: chunk ({cx},{cz}) spawned {blocks.Count} blocks");
    }

    // Builds a single-GameObject WorldStructure with a cube mesh+collider.
    // SetActive(false) before AddComponent<WorldStructure>() so Awake's
    // auto-register fires AFTER we've wired up footprintCollider and set the
    // final position/scale.
    private WorldStructure SpawnBlock(Vector2 center) {
        float terrainY = WorldServices.Get<IHeightProvider>().GetTerrainHeight(center.x, center.y);

        GameObject root = new GameObject("maze_block");
        root.SetActive(false);

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "visible_and_physics";
        cube.transform.SetParent(root.transform, false);

        NamedSemanticObject sem = cube.AddComponent<NamedSemanticObject>();
        sem.semanticName = semanticName;

        BoxCollider col = cube.GetComponent<BoxCollider>();
        WorldStructure ws = root.AddComponent<WorldStructure>();
        ws.structureType = "maze_block";
        ws.footprintCollider = col;

        root.transform.SetParent(transform);
        root.transform.position = new Vector3(center.x, terrainY + wallHeight * 0.5f, center.y);
        root.transform.localScale = new Vector3(_cellSize, wallHeight, _cellSize);

        root.SetActive(true);
        return ws;
    }

    // ─────────────────────────────────────────────
    //  Room structures (empty footprints for queries)
    // ─────────────────────────────────────────────

    private void SpawnRoomStructures() {
        foreach (Bounds2D b in _rooms) {
            WorldStructure ws = SpawnRoomStructure(b.center, b.size);
            if (ws != null) _roomStructures.Add(ws);
        }
    }

    private WorldStructure SpawnRoomStructure(Vector2 center, Vector2 size) {
        float terrainY = WorldServices.Get<IHeightProvider>().GetTerrainHeight(center.x, center.y);

        GameObject root = new GameObject("maze_room");
        root.SetActive(false);

        // Footprint-only child: disabled BoxCollider on WorldGen layer. Disabled
        // means no physics queries (CheckBox, Raycast) ever hit it, so rooms
        // don't block agents or other spawners by accident — they remain
        // queryable only via WorldStructure.GetBoundingBox2D() / IRoomProvider.
        GameObject footprint = new GameObject("Footprint");
        footprint.transform.SetParent(root.transform, false);
        int wgLayer = LayerMask.NameToLayer("WorldGen");
        if (wgLayer >= 0) footprint.layer = wgLayer;

        BoxCollider col = footprint.AddComponent<BoxCollider>();
        col.size = Vector3.one;
        col.isTrigger = true;
        col.enabled = false;

        WorldStructure ws = root.AddComponent<WorldStructure>();
        ws.structureType = "maze_room";
        ws.footprintCollider = col;

        root.transform.SetParent(transform);
        root.transform.position = new Vector3(center.x, terrainY, center.y);
        // Scale only the footprint child so WorldStructure.GetSize() (size × lossyScale) returns `size`.
        footprint.transform.localScale = new Vector3(size.x, 1f, size.y);

        root.SetActive(true);
        return ws;
    }

    // ─────────────────────────────────────────────
    //  Corridor structure spawning
    // ─────────────────────────────────────────────

    private struct CorridorEntry {
        public string type;
        public float chance;
        public bool scaleWidthWithCorridor;
        public bool scaleLengthWithCorridor;
        public bool alignWithCorridor;
        public bool randomizeDirection;
        public Vector2 prefabFootprintSize; // cached from prefab on load
    }

    private struct PlacedInterval {
        public float t;      // normalized position along segment [0,1]
        public float halfT;  // half-length along segment in normalized units
    }

    private void SpawnCorridorStructures(System.Random rng) {
        int maxPerCorridor = WorldLoadingController.GetParamInt("maze/corridor_structures/max_per_corridor", 0);
        if (maxPerCorridor <= 0) return;

        string typesCsv = WorldLoadingController.GetParamString("maze/corridor_structures/types", "");
        if (string.IsNullOrWhiteSpace(typesCsv)) return;

        List<CorridorEntry> entries = new List<CorridorEntry>();
        foreach (string raw in typesCsv.Split(',')) {
            string type = raw.Trim();
            if (string.IsNullOrEmpty(type)) continue;

            Vector2 footprint = GetPrefabFootprintSize(type);
            if (footprint.sqrMagnitude < 1e-6f) {
                Debug.LogWarning($"MazeLayoutLoader: corridor structure '{type}' has no footprint; skipping");
                continue;
            }

            entries.Add(new CorridorEntry {
                type                   = type,
                chance                 = Mathf.Clamp01(WorldLoadingController.GetParamFloat($"maze/corridor_structures/{type}/chance", 1f)),
                scaleWidthWithCorridor = WorldLoadingController.GetParamInt($"maze/corridor_structures/{type}/scale_width_with_corridor",  0) != 0,
                scaleLengthWithCorridor= WorldLoadingController.GetParamInt($"maze/corridor_structures/{type}/scale_length_with_corridor", 0) != 0,
                alignWithCorridor      = WorldLoadingController.GetParamInt($"maze/corridor_structures/{type}/align_with_corridor",       1) != 0,
                randomizeDirection     = WorldLoadingController.GetParamInt($"maze/corridor_structures/{type}/randomize_direction",       0) != 0,
                prefabFootprintSize    = footprint
            });
        }

        if (entries.Count == 0) return;

        int totalSpawned = 0;
        foreach (CorridorSegment seg in _corridors) {
            List<PlacedInterval> placed = new List<PlacedInterval>();
            float segAngleDeg = Mathf.Atan2(seg.direction.y, seg.direction.x) * Mathf.Rad2Deg;

            for (int attempt = 0; attempt < maxPerCorridor; attempt++) {
                // Pick a single position for this attempt. All entries compete for this slot in
                // priority order; first one whose chance passes AND that fits without overlap wins.
                // Entries that fail chance, don't fit, or overlap are skipped silently (they don't
                // consume the attempt — the loop moves to the next entry).
                float tRaw = (float)rng.NextDouble();

                foreach (CorridorEntry e in entries) {
                    if (rng.NextDouble() >= e.chance) continue;

                    // Size semantics follow WorldLayoutLoader's road convention:
                    //   sizeOverride.x = along local forward (corridor direction when aligned)
                    //   sizeOverride.y = perpendicular (corridor width direction when aligned)
                    float alongLen  = e.scaleLengthWithCorridor ? seg.length : e.prefabFootprintSize.x;
                    float perpWidth = e.scaleWidthWithCorridor  ? seg.width  : e.prefabFootprintSize.y;

                    // Can't fit even at the full segment length (would require negative margin) — skip.
                    if (alongLen > seg.length) continue;

                    float halfT, t;
                    if (e.scaleLengthWithCorridor) {
                        // The structure covers the entire segment — always center it and claim the whole
                        // interval so no other structure can share this segment.
                        halfT = 0.5f;
                        t     = 0.5f;
                    } else {
                        halfT = (alongLen * 0.5f) / seg.length;
                        // remap tRaw into the feasible interval [halfT, 1-halfT] so the structure fits end-to-end
                        t = halfT + tRaw * (1f - 2f * halfT);
                    }

                    bool overlap = false;
                    foreach (PlacedInterval p in placed) {
                        if (Mathf.Abs(p.t - t) < (p.halfT + halfT)) { overlap = true; break; }
                    }
                    if (overlap) continue;

                    Vector2 pos = Vector2.Lerp(seg.start, seg.end, t);
                    float rotCCW = e.alignWithCorridor ? segAngleDeg : 0f;
                    if (e.randomizeDirection && rng.Next(2) == 0) rotCCW += 180f;

                    WorldStructure ws = WorldData.SpawnStructure(
                        e.type, pos, rotCCW, transform,
                        sizeOverride: new Vector2(alongLen, perpWidth)
                    );
                    if (ws == null) continue;

                    placed.Add(new PlacedInterval { t = t, halfT = halfT });
                    totalSpawned++;
                    break; // attempt consumed
                }
            }
        }

        if (verbose)
            Debug.Log($"MazeLayoutLoader: corridor structures spawned {totalSpawned} across {_corridors.Count} segments " +
                      $"(max/corridor={maxPerCorridor}, types={entries.Count})");
    }

    private Vector2 GetPrefabFootprintSize(string type) {
        WorldStructure prefab = Resources.Load<WorldStructure>($"WorldGen/WorldStructurePrefabs/{type}");
        if (prefab == null || prefab.footprintCollider == null) return Vector2.zero;
        Vector3 s  = prefab.footprintCollider.size;
        Vector3 ls = prefab.footprintCollider.transform.lossyScale;
        return new Vector2(s.x * ls.x, s.z * ls.z);
    }
}
