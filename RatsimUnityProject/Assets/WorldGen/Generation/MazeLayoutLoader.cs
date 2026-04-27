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
///   4. Spawn one WorldStructure per room (no physics, footprint-only, exposed
///      for spawn-constraint queries via IRoomProvider). Default structureType
///      is "maze_room"; configure maze/rooms/types with per-type min/max counts
///      (see below) to label rooms differently (e.g. "reward_room", "empty_room")
///      so other loaders can target them. Each room carries a
///      LOD0/rewardSpawnPositions/cell_i_j hierarchy with one Transform per cell
///      at that cell's world center, so RewardObjectLoader can use the standard
///      structure-mode pipeline (allowed_structures=<room_type>,
///      min/max_per_structure) to drop a deterministic reward count per room.
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
///   maze/mode                    -- "rooms_and_corridors" (default) | "memory_maze"
///   maze/cell_size               -- world units per mask cell (default 1)
///   maze/wall_height             -- Y scale of each block (default 3)
///   maze/n_rooms                 -- target room count (rooms_and_corridors) / max-rooms budget (memory_maze) (default 8)
///   maze/room_min_size_cells     -- min room side in cells (default 6; memory_maze rounds up to odd)
///   maze/room_max_size_cells     -- max room side in cells (default 12; memory_maze rounds up to odd)
///   maze/room_min_separation_cells -- min gap between room rects, in cells (default 2; ignored in memory_maze, fixed at 1)
///   maze/room_max_attempts       -- rejection-sampling budget (per room in r&c, total in memory_maze) (default 200)
///   maze/corridor_width_cells    -- (rooms_and_corridors only) corridor width in cells (default 3)
///   maze/extra_corridor_fraction -- (rooms_and_corridors only) [0,1] extras beyond MST as fraction of non-tree pairs (default 0.3)
///   maze/extra_connection_probability -- (memory_maze only) [0,1] per-candidate-wall extra-loop prob beyond mandatory (default 0.0)
///   maze/border_walls            -- 0/1; stamp a 1-cell-thick wall ring along the mask edge (default 1)
///   maze/semantic_name           -- semantic name used for blocks (default "maze_wall")
///
/// Room labeling (optional — empty list → all rooms get structureType="maze_room"):
///   maze/rooms/types         -- comma list of structure types to apply to generated rooms.
///                                Geometry is unaffected; this just sets each room's
///                                WorldStructure.structureType so RewardObjectLoader (or any
///                                other structure-aware loader) can target specific rooms.
///   maze/rooms/{type}/min    -- minimum rooms of this type (default 0)
///   maze/rooms/{type}/max    -- maximum rooms of this type (-1 = unlimited; default -1)
/// Validation: sum(min) <= rooms_generated, max >= min, and either some type has max=-1
/// or sum(max) >= rooms_generated. Failures emit WorldGenStatus.Error and the loader
/// falls back to all-rooms = "maze_room".
/// Assignment: seeded RNG fills mins first, then distributes remainder uniformly among
/// types still under their max, then shuffles so types are mixed across room indices.
/// Deterministic for a given seed.
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
///
/// Note: corridor structure spawning is rooms_and_corridors only (memory_maze produces 1-cell DFS
/// corridors that don't have well-defined "segments" for elongated structures).
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
    // parallel to _rooms; cell-space rects, used to lay out per-cell rewardSpawnPositions
    private readonly List<RoomRect> _roomRects = new List<RoomRect>();
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
            case "memory_maze":
                GenerateMemoryMaze(rng);
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
        _roomRects.Clear();
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
        _roomRects.Clear();
        foreach (RoomRect r in rooms) {
            Vector2 center = new Vector2(
                _originX + (r.x0 + r.w * 0.5f) * _cellSize,
                _originZ + (r.z0 + r.h * 0.5f) * _cellSize
            );
            Vector2 size = new Vector2(r.w * _cellSize, r.h * _cellSize);
            _rooms.Add(new Bounds2D(center, size, 0f));
            _roomRects.Add(r);
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
    //  Mask generation: memory-maze style (labmaze algorithm)
    // ─────────────────────────────────────────────
    //
    // Mirrors DeepMind labmaze (which memory-maze uses verbatim). Operates on
    // an odd-sized grid where walls live on even coords and floors on odd
    // coords — that's what makes corridors come out 1 cell wide and rooms
    // odd-sized.
    //
    //   1. Rejection-sample odd-aligned, odd-sized rooms (each gets a region id).
    //   2. Iterative DFS backtracker fills every remaining odd cell with
    //      1-wide corridors. Each connected blob gets its own region id.
    //   3. For each unordered pair of adjacent regions, collect every wall
    //      cell that separates them. Knock out exactly one (mandatory connector,
    //      guarantees connectivity) and then knock out each remaining one with
    //      probability `extra_connection_probability` (loops).
    //
    // Corridor segment recording is skipped — DFS corridors don't have clean
    // "segments" for the corridor-structures feature.

    private void GenerateMemoryMaze(System.Random rng) {
        // Force mask dims odd (labmaze invariant). Recenter origin on whatever we end up with.
        int oddW = (_maskW % 2 == 1) ? _maskW : _maskW - 1;
        int oddH = (_maskH % 2 == 1) ? _maskH : _maskH - 1;
        if (oddW < 5 || oddH < 5) {
            Debug.LogError($"MazeLayoutLoader.memory_maze: mask too small ({oddW}x{oddH}); need >=5x5");
            return;
        }
        _maskW = oddW;
        _maskH = oddH;
        _originX = -_maskW * _cellSize * 0.5f;
        _originZ = -_maskH * _cellSize * 0.5f;

        _mask = new bool[_maskW, _maskH];
        int[,] regions = new int[_maskW, _maskH]; // 0 = wall (initial)
        for (int i = 0; i < _maskW; i++)
            for (int j = 0; j < _maskH; j++)
                _mask[i, j] = true;

        float extraProb = Mathf.Clamp01(
            WorldLoadingController.GetParamFloat("maze/extra_connection_probability", 0f));
        int maxRooms = nRooms;

        // Round room sizes up to odd (labmaze convention).
        int rMin = roomMinSizeCells | 1;
        int rMax = roomMaxSizeCells | 1;
        if (rMin < 3) rMin = 3;
        if (rMax < rMin) rMax = rMin;

        int nextRegion = 1;
        List<RoomRect> rooms = new List<RoomRect>();

        // Phase 1: place rooms. Counter increments only on placement failure (matches labmaze retry semantics).
        int retries = 0;
        while (rooms.Count < maxRooms && retries < roomMaxAttempts) {
            int wHalf = (rMax - rMin) / 2;
            int w = rMin + 2 * rng.Next(0, wHalf + 1);
            int h = rMin + 2 * rng.Next(0, wHalf + 1);
            // Position must be odd; leave a 1-cell border so outer walls stay intact.
            int xMaxOdd = _maskW - 1 - w;
            int zMaxOdd = _maskH - 1 - h;
            if (xMaxOdd < 1 || zMaxOdd < 1) { retries++; continue; }
            int x0 = 1 + 2 * rng.Next(0, (xMaxOdd - 1) / 2 + 1);
            int z0 = 1 + 2 * rng.Next(0, (zMaxOdd - 1) / 2 + 1);

            RoomRect candidate = new RoomRect { x0 = x0, z0 = z0, w = w, h = h };
            // Min-1 separation guarantees a wall between rooms; odd alignment makes that automatic
            // when rooms don't overlap, but explicit check is cheap insurance.
            if (rooms.Any(r => RoomsOverlapWithSeparation(r, candidate, 1))) {
                retries++;
                continue;
            }

            int rid = nextRegion++;
            for (int i = x0; i < x0 + w; i++)
                for (int j = z0; j < z0 + h; j++) {
                    _mask[i, j] = false;
                    regions[i, j] = rid;
                }
            rooms.Add(candidate);
        }

        // Phase 2: fill the rest with 1-wide DFS corridors. Each blob is a new region.
        Vector2Int[] cardinals = {
            new Vector2Int( 2,  0), new Vector2Int(-2,  0),
            new Vector2Int( 0,  2), new Vector2Int( 0, -2)
        };
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        List<Vector2Int> dirBuf = new List<Vector2Int>(4);

        for (int si = 1; si < _maskW; si += 2) {
            for (int sj = 1; sj < _maskH; sj += 2) {
                if (regions[si, sj] != 0) continue;
                int rid = nextRegion++;
                stack.Clear();
                stack.Push(new Vector2Int(si, sj));
                _mask[si, sj] = false;
                regions[si, sj] = rid;

                while (stack.Count > 0) {
                    Vector2Int cur = stack.Peek();
                    dirBuf.Clear();
                    foreach (Vector2Int d in cardinals) {
                        int ni = cur.x + d.x, nj = cur.y + d.y;
                        if (ni <= 0 || ni >= _maskW - 1 || nj <= 0 || nj >= _maskH - 1) continue;
                        if (regions[ni, nj] != 0) continue;
                        dirBuf.Add(d);
                    }
                    if (dirBuf.Count == 0) { stack.Pop(); continue; }
                    Vector2Int dd = dirBuf[rng.Next(dirBuf.Count)];
                    int wi = cur.x + dd.x / 2, wj = cur.y + dd.y / 2; // wall between
                    int ti = cur.x + dd.x,     tj = cur.y + dd.y;     // target cell
                    _mask[wi, wj] = false; regions[wi, wj] = rid;
                    _mask[ti, tj] = false; regions[ti, tj] = rid;
                    stack.Push(new Vector2Int(ti, tj));
                }
            }
        }

        // Phase 3: collect candidate connectors (walls separating two different regions).
        // Only scan +x and +z directions to avoid double-counting.
        Dictionary<long, List<Vector2Int>> connectors = new Dictionary<long, List<Vector2Int>>();
        for (int i = 1; i < _maskW; i += 2) {
            for (int j = 1; j < _maskH; j += 2) {
                int r0 = regions[i, j];
                if (r0 == 0) continue;
                // +x neighbor
                if (i + 2 < _maskW) {
                    int r1 = regions[i + 2, j];
                    if (r1 != 0 && r1 != r0) {
                        long key = PairKey(r0, r1);
                        if (!connectors.TryGetValue(key, out var list)) {
                            list = new List<Vector2Int>();
                            connectors[key] = list;
                        }
                        list.Add(new Vector2Int(i + 1, j));
                    }
                }
                // +z neighbor
                if (j + 2 < _maskH) {
                    int r1 = regions[i, j + 2];
                    if (r1 != 0 && r1 != r0) {
                        long key = PairKey(r0, r1);
                        if (!connectors.TryGetValue(key, out var list)) {
                            list = new List<Vector2Int>();
                            connectors[key] = list;
                        }
                        list.Add(new Vector2Int(i, j + 1));
                    }
                }
            }
        }

        // Mandatory: knock out exactly one wall per region pair.
        int extras = 0, mandatory = 0;
        foreach (var kv in connectors) {
            var locs = kv.Value;
            if (locs.Count == 0) continue;
            Vector2Int chosen = locs[rng.Next(locs.Count)];
            _mask[chosen.x, chosen.y] = false;
            mandatory++;
        }
        // Extras: each non-chosen connector with probability extraProb.
        if (extraProb > 0f) {
            foreach (var kv in connectors) {
                foreach (Vector2Int c in kv.Value) {
                    if (!_mask[c.x, c.y]) continue; // already opened
                    if (rng.NextDouble() <= extraProb) {
                        _mask[c.x, c.y] = false;
                        extras++;
                    }
                }
            }
        }

        // Store room bounds for IRoomProvider.
        _rooms.Clear();
        _roomRects.Clear();
        foreach (RoomRect r in rooms) {
            Vector2 center = new Vector2(
                _originX + (r.x0 + r.w * 0.5f) * _cellSize,
                _originZ + (r.z0 + r.h * 0.5f) * _cellSize
            );
            Vector2 size = new Vector2(r.w * _cellSize, r.h * _cellSize);
            _rooms.Add(new Bounds2D(center, size, 0f));
            _roomRects.Add(r);
        }

        if (verbose)
            Debug.Log($"MazeLayoutLoader.memory_maze: {_maskW}x{_maskH} mask, rooms={rooms.Count}/{maxRooms} " +
                      $"(retries={retries}), regions={nextRegion - 1}, mandatory={mandatory}, extras={extras}");
    }

    // Pack two ints into a long for use as a dict key (order-independent).
    private static long PairKey(int a, int b) {
        int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
        return ((long)lo << 32) | (uint)hi;
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

    private struct RoomTypeSpec {
        public string type;
        public int min;
        public int max; // -1 = unlimited
    }

    private List<RoomTypeSpec> LoadRoomTypeSpecs() {
        List<RoomTypeSpec> specs = new List<RoomTypeSpec>();
        string csv = WorldLoadingController.GetParamString("maze/rooms/types", "");
        if (string.IsNullOrWhiteSpace(csv)) return specs;
        foreach (string raw in csv.Split(',')) {
            string type = raw.Trim();
            if (string.IsNullOrEmpty(type)) continue;
            specs.Add(new RoomTypeSpec {
                type = type,
                min  = WorldLoadingController.GetParamInt($"maze/rooms/{type}/min",  0),
                max  = WorldLoadingController.GetParamInt($"maze/rooms/{type}/max", -1),
            });
        }
        return specs;
    }

    /// <summary>
    /// Returns one structureType string per generated room, satisfying the per-type min/max
    /// constraints. Returns null on overconstrained input (caller falls back to "maze_room").
    /// </summary>
    private string[] AssignRoomTypes(List<RoomTypeSpec> specs, int n, System.Random rng) {
        if (specs.Count == 0) return null;

        // Validate per-type bounds.
        for (int i = 0; i < specs.Count; i++) {
            RoomTypeSpec s = specs[i];
            if (s.min < 0) {
                WorldGenStatus.Error("MazeLayoutLoader",
                    $"room type '{s.type}': min ({s.min}) must be >= 0");
                return null;
            }
            if (s.max >= 0 && s.max < s.min) {
                WorldGenStatus.Error("MazeLayoutLoader",
                    $"room type '{s.type}': max ({s.max}) < min ({s.min})");
                return null;
            }
        }

        // Validate aggregate feasibility against actual room count.
        int sumMin = 0;
        bool hasUnlimited = false;
        int sumMaxFinite = 0;
        foreach (RoomTypeSpec s in specs) {
            sumMin += s.min;
            if (s.max < 0) hasUnlimited = true;
            else           sumMaxFinite += s.max;
        }
        if (sumMin > n) {
            WorldGenStatus.Error("MazeLayoutLoader",
                $"room types overconstrained: sum of mins ({sumMin}) > rooms generated ({n}). " +
                $"Increase maze/n_rooms or relax mins.");
            return null;
        }
        if (!hasUnlimited && sumMaxFinite < n) {
            WorldGenStatus.Error("MazeLayoutLoader",
                $"room types underconstrained: sum of maxes ({sumMaxFinite}) < rooms generated ({n}). " +
                $"Raise a max, leave one unset (-1 = unlimited), or reduce maze/n_rooms.");
            return null;
        }

        // Phase 1: required mins.
        List<string> assignments = new List<string>(n);
        int[] counts = new int[specs.Count];
        for (int i = 0; i < specs.Count; i++) {
            for (int k = 0; k < specs[i].min; k++) {
                assignments.Add(specs[i].type);
                counts[i]++;
            }
        }

        // Phase 2: distribute remaining slots uniformly among eligible types.
        List<int> eligible = new List<int>();
        for (int i = 0; i < specs.Count; i++) {
            if (specs[i].max < 0 || counts[i] < specs[i].max) eligible.Add(i);
        }
        int remaining = n - assignments.Count;
        for (int k = 0; k < remaining; k++) {
            if (eligible.Count == 0) {
                // Shouldn't happen given the aggregate check above, but guard anyway.
                WorldGenStatus.Error("MazeLayoutLoader", "no eligible room type for remaining slots");
                return null;
            }
            int pickIdx = rng.Next(eligible.Count);
            int specIdx = eligible[pickIdx];
            assignments.Add(specs[specIdx].type);
            counts[specIdx]++;
            if (specs[specIdx].max >= 0 && counts[specIdx] >= specs[specIdx].max) {
                eligible.RemoveAt(pickIdx);
            }
        }

        // Phase 3: shuffle so types are mixed across room indices.
        for (int i = n - 1; i > 0; i--) {
            int j = rng.Next(i + 1);
            (assignments[i], assignments[j]) = (assignments[j], assignments[i]);
        }

        if (verbose) {
            string summary = string.Join(", ", System.Linq.Enumerable.Range(0, specs.Count)
                .Select(i => $"{specs[i].type}={counts[i]}"));
            Debug.Log($"MazeLayoutLoader: room type assignment ({n} rooms): {summary}");
        }

        return assignments.ToArray();
    }

    private void SpawnRoomStructures() {
        List<RoomTypeSpec> specs = LoadRoomTypeSpecs();
        System.Random rng = new System.Random(WorldLoadingController.GetDerivedSeed("maze_room_types"));
        string[] types = AssignRoomTypes(specs, _rooms.Count, rng);

        for (int i = 0; i < _rooms.Count; i++) {
            string type = (types != null) ? types[i] : "maze_room";
            WorldStructure ws = SpawnRoomStructure(_rooms[i].center, _rooms[i].size, _roomRects[i], type);
            if (ws != null) _roomStructures.Add(ws);
        }
    }

    private WorldStructure SpawnRoomStructure(Vector2 center, Vector2 size, RoomRect rect, string structureType) {
        IHeightProvider heights = WorldServices.Get<IHeightProvider>();
        float terrainY = heights.GetTerrainHeight(center.x, center.y);

        GameObject root = new GameObject(structureType);
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
        ws.structureType = structureType;
        ws.footprintCollider = col;

        root.transform.SetParent(transform);
        root.transform.position = new Vector3(center.x, terrainY, center.y);
        // Scale only the footprint child so WorldStructure.GetSize() (size × lossyScale) returns `size`.
        footprint.transform.localScale = new Vector3(size.x, 1f, size.y);

        // LOD0/rewardSpawnPositions/cell_i_j — one Transform at the center of every cell in the
        // room. Built for every room regardless of label so any structure-aware loader can use
        // them; RewardObjectLoader picks from these when reward_objects/allowed_structures
        // includes this room's type, with min/max_per_structure controlling reward count.
        // Must be in place BEFORE SetActive(true), which fires WorldStructure.Awake and the
        // OnWorldStructureLoaded callback that reads this hierarchy.
        GameObject lod0 = new GameObject("LOD0");
        lod0.transform.SetParent(root.transform, false);
        GameObject group = new GameObject("rewardSpawnPositions");
        group.transform.SetParent(lod0.transform, false);
        for (int i = 0; i < rect.w; i++) {
            for (int j = 0; j < rect.h; j++) {
                float wx = _originX + (rect.x0 + i + 0.5f) * _cellSize;
                float wz = _originZ + (rect.z0 + j + 0.5f) * _cellSize;
                float wy = heights.GetTerrainHeight(wx, wz);
                GameObject sp = new GameObject($"cell_{i}_{j}");
                sp.transform.SetParent(group.transform, false);
                sp.transform.position = new Vector3(wx, wy, wz);
            }
        }

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
