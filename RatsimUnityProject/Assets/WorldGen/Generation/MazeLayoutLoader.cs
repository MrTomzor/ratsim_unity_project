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
///   maze/room_max_attempts       -- rejection-sampling budget (per room in r&c, total per outer attempt in memory_maze) (default 200)
///   maze/room_placement_retries  -- (memory_maze only) outer retries: if rejection sampling exhausts room_max_attempts
///                                   without filling n_rooms, we restart room placement from scratch up to this many times.
///                                   Tight packings (e.g. 9 rooms of size 3 in a 15x15 mask, near labmaze's theoretical
///                                   limit) often need many restarts because a greedy random pass can paint itself into
///                                   a corner. We keep the best (largest) attempt across restarts. Default 100.
///   maze/corridor_width_cells    -- (rooms_and_corridors only) corridor width in cells (default 3)
///   maze/extra_corridor_fraction -- (rooms_and_corridors only) [0,1] extras beyond MST as fraction of non-tree pairs (default 0.3)
///   maze/extra_connection_probability -- (memory_maze only) [0,1] per-candidate-wall extra-loop prob beyond mandatory (default 0.0)
///   maze/border_walls            -- 0/1; stamp a 1-cell-thick wall ring along the mask edge (default 1)
///   maze/edge_walls_only         -- 0/1; only spawn block structures for wall cells with at least
///                                   one 4-connected floor neighbor (default 1). Skipped interior
///                                   walls are invisible to physics, lidar, and cameras (no floor
///                                   sees them), so culling them is purely a perf win — typically
///                                   5-10x fewer GameObjects in dense mazes. Disable only for
///                                   debugging or visual top-down maps that need solid wall fill.
///                                   Force-disabled in memory_maze mode (4-connected cull would
///                                   drop diagonal corner walls and leak rays between regions).
///   maze/semantic_name           -- semantic name used for blocks (default "maze_wall")
///
/// Sectored mode (rooms_and_corridors only — memory_maze ignores these for now):
///   maze/sectors/style -- "none" (default; sectors disabled), "quadrants" or "orthogonal".
///     - "quadrants" : sectors are NE/NW/SE/SW corner quadrants, dividers along the cardinal
///                     axes from chamber edges. Chamber participates in each sector's MST
///                     and picks up extra short edges as loop extras → typically MORE than
///                     4 corridors out of the chamber. Sectors look "diagonally oriented".
///     - "orthogonal": sectors are N/E/S/W spatial wedges, but with NO walls between
///                     them. Rooms are constrained by rejection sampling to lie entirely
///                     in their assigned wedge with a buffer of (corridor_width/2 + 1)
///                     cells from the diagonal — wide enough that L-corridor strips
///                     between two same-wedge rooms keep their full perpendicular width
///                     inside the wedge, so no cross-sector floor merge can occur. The
///                     boundary diagonal area between wedges is just unclassified wall
///                     cells (mask=true) that corridors are free to carve through when
///                     routing — no walls split corridors. Per sector, ONE first room
///                     is forced axis-aligned (i=mid_x for N/S, j=mid_z for E/W) and on
///                     the correct side of the chamber, and a single straight cardinal
///                     corridor connects chamber to that first room. Chamber stays out
///                     of every sector's MST → exactly 4 chamber corridors total.
///   In both styles, loops (extra_corridor_fraction) apply per-sector only — the only path
///   between any two sectors goes through the chamber. Designed to break memoryless RL
///   policies (an agent that forgets which sector it came from has no shortcut back).
///
///   maze/sectors/central_chamber_size_cells -- chamber side in cells (default 5; min 3).
///                                              For "quadrants" style, must exceed
///                                              corridor_width_cells (cardinal-corridor
///                                              strip squeezes through the chamber's edge).
///                                              For "orthogonal" style, just larger than
///                                              corridor_width_cells.
///   When sectors are enabled, exactly one room type in maze/rooms/types must have
///   force_central=1 with min=1 and max=1 — that type's structureType is applied to the
///   chamber. The remaining sector rooms' types come from the other specs as usual.
///
/// Room labeling (optional — empty list → all rooms get structureType="maze_room"):
///   maze/rooms/types         -- comma list of structure types to apply to generated rooms.
///                                Geometry is unaffected; this just sets each room's
///                                WorldStructure.structureType so RewardObjectLoader (or any
///                                other structure-aware loader) can target specific rooms.
///   maze/rooms/{type}/min    -- minimum rooms of this type (default 0)
///   maze/rooms/{type}/max    -- maximum rooms of this type (-1 = unlimited; default -1)
///   maze/rooms/{type}/force_central -- 0/1; in sectors mode, marks this type as the central
///                                      chamber's type (must have min=1 and max=1, exactly
///                                      one type may set this). Ignored when sectors disabled.
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
    public bool edgeWallsOnly = true;
    public string semanticName = "maze_wall";

    // ─────────────────────────────────────────────
    //  Runtime state
    // ─────────────────────────────────────────────

    // true = wall, false = floor
    private bool[,] _mask;
    // true = "do not carve" — used for sector dividers in sectors mode so that L-corridors
    // pass over them as no-ops, leaving the wall intact. Null when not in sectors mode.
    private bool[,] _protectedWalls;
    private int _maskW, _maskH;       // cells along X (width) and Z (height)
    private float _worldW, _worldH;   // cached world bounds
    private float _cellSize;          // active cell size for this episode
    private float _originX, _originZ; // world coords of mask[0,0] corner

    // room footprints in world coords (for IRoomProvider)
    private readonly List<Bounds2D> _rooms = new List<Bounds2D>();
    // parallel to _rooms; cell-space rects, used to lay out per-cell rewardSpawnPositions
    private readonly List<RoomRect> _roomRects = new List<RoomRect>();
    // parallel to _rooms; -1 = not the chamber, 0..3 = sector room (NE=0, NW=1, SE=2, SW=3),
    // -2 = chamber. Used by SpawnRoomStructures/AssignRoomTypes to give the chamber its
    // force_central type without going through the regular assignment.
    private readonly List<int> _roomSectorIds = new List<int>();
    // index of chamber in _rooms when sectors are enabled; -1 otherwise.
    private int _chamberRoomIndex = -1;
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

        // Sectored layout is currently only implemented for rooms_and_corridors. Warn loudly
        // if the user pairs it with memory_maze rather than silently generating a regular maze.
        string warnStyle = WorldLoadingController.GetParamString("maze/sectors/style", "none");
        if (!warnStyle.Equals("none", System.StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(warnStyle)
            && !mazeMode.Equals("rooms_and_corridors", System.StringComparison.OrdinalIgnoreCase)) {
            Debug.LogWarning($"MazeLayoutLoader: maze/sectors/style='{warnStyle}' is only implemented for " +
                             $"maze/mode=rooms_and_corridors (got '{mazeMode}'); generating without sectors.");
        }

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
        _roomSectorIds.Clear();
        _chamberRoomIndex = -1;
        _roomStructures.Clear();
        _corridors.Clear();
        _mask = null;
        _protectedWalls = null;
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
        edgeWallsOnly         = WorldLoadingController.GetParamInt  ("maze/edge_walls_only", edgeWallsOnly ? 1 : 0) != 0;
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

        string sectorStyle = WorldLoadingController.GetParamString("maze/sectors/style", "none").ToLowerInvariant();

        List<RoomRect> rooms;
        List<int> sectorIds;
        int chamberIndex;
        switch (sectorStyle) {
            case "quadrants":
                rooms = GenerateQuadrantsSectoredLayout(rng, out sectorIds, out chamberIndex);
                break;
            case "orthogonal":
                rooms = GenerateOrthogonalSectoredLayout(rng, out sectorIds, out chamberIndex);
                break;
            case "none":
            case "":
                rooms = GenerateUniformLayout(rng);
                sectorIds = new List<int>(rooms.Count);
                for (int i = 0; i < rooms.Count; i++) sectorIds.Add(-1);
                chamberIndex = -1;
                break;
            default:
                Debug.LogWarning($"MazeLayoutLoader: unknown maze/sectors/style '{sectorStyle}'; falling back to 'none'");
                rooms = GenerateUniformLayout(rng);
                sectorIds = new List<int>(rooms.Count);
                for (int i = 0; i < rooms.Count; i++) sectorIds.Add(-1);
                chamberIndex = -1;
                break;
        }

        // store room bounds in world coords for IRoomProvider
        _rooms.Clear();
        _roomRects.Clear();
        _roomSectorIds.Clear();
        for (int i = 0; i < rooms.Count; i++) {
            RoomRect r = rooms[i];
            Vector2 center = new Vector2(
                _originX + (r.x0 + r.w * 0.5f) * _cellSize,
                _originZ + (r.z0 + r.h * 0.5f) * _cellSize
            );
            Vector2 size = new Vector2(r.w * _cellSize, r.h * _cellSize);
            _rooms.Add(new Bounds2D(center, size, 0f));
            _roomRects.Add(r);
            _roomSectorIds.Add(sectorIds[i]);
        }
        _chamberRoomIndex = chamberIndex;
    }

    /// <summary>
    /// Classic rooms-and-corridors layout: rejection-sample rooms anywhere in the world,
    /// connect them all with one MST + extras. No central chamber, no sector restrictions.
    /// </summary>
    private List<RoomRect> GenerateUniformLayout(System.Random rng) {
        List<RoomRect> rooms = new List<RoomRect>();
        int attempts = 0;
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

        foreach (RoomRect r in rooms) CarveRect(r.x0, r.z0, r.w, r.h);
        if (rooms.Count >= 2) ConnectRooms(rooms, rng);
        return rooms;
    }

    // ─────────────────────────────────────────────
    //  Sectored layout — "quadrants" style
    // ─────────────────────────────────────────────
    //
    // Topology: 4 sector quadrants (NE, NW, SE, SW) connected only via a central chamber.
    // Loops are confined to within-sector pairs — there's no extra connector between two
    // different sectors, so the only path between sector i and sector j is through the
    // chamber. (This is the property that breaks memoryless RL policies: an agent that
    // forgets which sector it came from has no shortcut back.)
    //
    // Note: chamber participates in each sector's MST, so it ends up with one MST edge per
    // sector (4 edges) PLUS any short chamber-room edges picked up as extras. To get
    // exactly 4 cardinal exits, use the "orthogonal" style instead.
    //
    // Geometry (mid_x = chamber x-center cell, mid_z = chamber z-center cell):
    //     +-------------------------+
    //     |    NW    | N |    NE    |   "N strip" = column of cells north of chamber, in
    //     |          | s |          |   chamber's x-range. Split by N-divider at i=mid_x.
    //     +----+-----+---+-----+----+
    //     | W  |    chamber    |  E |   E/W strips analogous, split by E/W-dividers at
    //     | s  |               |  s |   j=mid_z.
    //     +----+-----+---+-----+----+
    //     |    SW    | S |    SE    |
    //     |          | s |          |
    //     +-------------------------+
    //
    // The dividers (column i=mid_x outside chamber, row j=mid_z outside chamber) are
    // stamped as protected walls before any carving — CarveRect skips protected cells, so
    // L-corridors that cross a divider have a notch removed where they'd cut it. The
    // chamber-to-room corridors anchor at the chamber's sector-corner cell (e.g. NE corner
    // for an NE-sector room), so the carved strip stays in that sector's territory.
    //
    // Loop tuning: extra_corridor_fraction applies per-sector (each sector's room subgraph
    // gets its own MST + extras pass). Cross-sector loops are not configurable (would
    // violate the topology guarantee).
    private List<RoomRect> GenerateQuadrantsSectoredLayout(System.Random rng,
                                                           out List<int> sectorIds,
                                                           out int chamberIndex) {
        sectorIds = new List<int>();
        chamberIndex = -1;

        int chamberSizeCells = WorldLoadingController.GetParamInt("maze/sectors/central_chamber_size_cells", 5);
        chamberSizeCells = Mathf.Max(3, chamberSizeCells);

        int border = borderWalls ? 1 : 0;
        // Need: chamber + at least one cell on each side for divider + sector room space.
        int minMaskSide = chamberSizeCells + 2 * (border + roomMinSizeCells + 1);
        if (_maskW < minMaskSide || _maskH < minMaskSide) {
            WorldGenStatus.Error("MazeLayoutLoader",
                $"sectors mode: world too small for chamber={chamberSizeCells} + 4 sectors. " +
                $"Mask {_maskW}x{_maskH} needs >= {minMaskSide}x{minMaskSide}.");
            return new List<RoomRect>();
        }

        // Place chamber centered (or as close as parity allows). Snap so that mid_x, mid_z
        // are well-defined integers.
        int Cx = (_maskW - chamberSizeCells) / 2;
        int Cz = (_maskH - chamberSizeCells) / 2;
        RoomRect chamber = new RoomRect { x0 = Cx, z0 = Cz, w = chamberSizeCells, h = chamberSizeCells };
        int mid_x = Cx + chamberSizeCells / 2;
        int mid_z = Cz + chamberSizeCells / 2;

        // Carve the chamber, then stamp dividers as protected walls.
        CarveRect(chamber.x0, chamber.z0, chamber.w, chamber.h);
        _protectedWalls = new bool[_maskW, _maskH];
        // Vertical divider (column i=mid_x) above and below chamber.
        for (int j = 0; j < Cz; j++) { _mask[mid_x, j] = true; _protectedWalls[mid_x, j] = true; }
        for (int j = Cz + chamberSizeCells; j < _maskH; j++) { _mask[mid_x, j] = true; _protectedWalls[mid_x, j] = true; }
        // Horizontal divider (row j=mid_z) east and west of chamber.
        for (int i = 0; i < Cx; i++) { _mask[i, mid_z] = true; _protectedWalls[i, mid_z] = true; }
        for (int i = Cx + chamberSizeCells; i < _maskW; i++) { _mask[i, mid_z] = true; _protectedWalls[i, mid_z] = true; }

        // 4 sector AABBs (cell ranges where rooms may be placed, exclusive upper bounds).
        // We sample rooms strictly within these — no room may straddle a divider or the
        // chamber. Buffer of 1 cell from the divider line is implicit because the divider
        // itself is at i=mid_x / j=mid_z and AABBs stop before that.
        SectorBounds[] sectors = new SectorBounds[4] {
            // NE: i in [Cx+CW, _maskW-border), j in [Cz+CW, _maskH-border)
            new SectorBounds { id = 0, name = "NE",
                xLo = Cx + chamberSizeCells, xHi = _maskW - border,
                zLo = Cz + chamberSizeCells, zHi = _maskH - border,
                anchorX = Cx + chamberSizeCells - 1, anchorZ = Cz + chamberSizeCells - 1 },
            // NW: i in [border, Cx), j in [Cz+CW, _maskH-border)
            new SectorBounds { id = 1, name = "NW",
                xLo = border, xHi = Cx,
                zLo = Cz + chamberSizeCells, zHi = _maskH - border,
                anchorX = Cx, anchorZ = Cz + chamberSizeCells - 1 },
            // SE
            new SectorBounds { id = 2, name = "SE",
                xLo = Cx + chamberSizeCells, xHi = _maskW - border,
                zLo = border, zHi = Cz,
                anchorX = Cx + chamberSizeCells - 1, anchorZ = Cz },
            // SW
            new SectorBounds { id = 3, name = "SW",
                xLo = border, xHi = Cx,
                zLo = border, zHi = Cz,
                anchorX = Cx, anchorZ = Cz },
        };

        // Sample sector rooms. The chamber counts toward maze/n_rooms (so a config of
        // n_rooms=9 in sectors mode means 1 chamber + 8 sector rooms — round-robin gives
        // 2 per sector). Round-robin sector assignment keeps counts balanced; without this,
        // random sampling can bias toward whichever sector happens to fill up last.
        int sectorRoomBudget = Mathf.Max(0, nRooms - 1);
        List<RoomRect> sectorRooms = new List<RoomRect>();
        List<int> sectorRoomIds = new List<int>();
        int totalAttempts = 0;
        int totalBudget = Mathf.Max(1, sectorRoomBudget) * roomMaxAttempts;
        int nextSector = 0;
        while (sectorRooms.Count < sectorRoomBudget && totalAttempts < totalBudget) {
            totalAttempts++;
            SectorBounds sb = sectors[nextSector];
            int w = rng.Next(roomMinSizeCells, roomMaxSizeCells + 1);
            int h = rng.Next(roomMinSizeCells, roomMaxSizeCells + 1);
            // Room must fit entirely within sector AABB.
            int xMax = sb.xHi - w; // exclusive
            int zMax = sb.zHi - h;
            if (xMax <= sb.xLo || zMax <= sb.zLo) { nextSector = (nextSector + 1) % 4; continue; }
            int x0 = rng.Next(sb.xLo, xMax);
            int z0 = rng.Next(sb.zLo, zMax);
            RoomRect candidate = new RoomRect { x0 = x0, z0 = z0, w = w, h = h };
            if (sectorRooms.Any(r => RoomsOverlapWithSeparation(r, candidate, roomMinSeparationCells))) continue;
            // Don't bother checking chamber overlap or divider overlap — sector AABBs already
            // exclude both.
            sectorRooms.Add(candidate);
            sectorRoomIds.Add(sb.id);
            nextSector = (nextSector + 1) % 4;
        }

        if (verbose)
            Debug.Log($"MazeLayoutLoader.sectors: placed {sectorRooms.Count}/{sectorRoomBudget} sector rooms " +
                      $"+ 1 chamber ({chamberSizeCells}x{chamberSizeCells} at ({Cx},{Cz})) after {totalAttempts} attempts");

        // Carve sector rooms.
        foreach (RoomRect r in sectorRooms) CarveRect(r.x0, r.z0, r.w, r.h);

        // Per-sector connectivity: build {chamber + sector_i_rooms} graph and run MST + extras.
        // Chamber appears as the same physical room in all 4 graphs; chamber-to-room L-corridors
        // anchor at the sector-side chamber corner (sb.anchorX, sb.anchorZ) so the carved strip
        // stays in that sector's territory.
        for (int s = 0; s < 4; s++) {
            SectorBounds sb = sectors[s];
            List<RoomRect> sectorGraph = new List<RoomRect>();
            // Chamber rect uses sector-corner as its "center" for distance + corridor anchoring.
            // We hand a synthetic RoomRect to ConnectRooms whose CenterX/Z point at the sector
            // corner — this keeps L-corridors inside the sector without changing ConnectRooms.
            sectorGraph.Add(MakeSyntheticAnchorRoom(sb.anchorX, sb.anchorZ));
            for (int i = 0; i < sectorRooms.Count; i++) {
                if (sectorRoomIds[i] == s) sectorGraph.Add(sectorRooms[i]);
            }
            if (sectorGraph.Count >= 2) ConnectRooms(sectorGraph, rng);
        }

        // Assemble final room list: chamber at index 0, then sector rooms in order.
        List<RoomRect> all = new List<RoomRect>();
        all.Add(chamber);
        sectorIds.Add(-2); // chamber sentinel
        chamberIndex = 0;
        for (int i = 0; i < sectorRooms.Count; i++) {
            all.Add(sectorRooms[i]);
            sectorIds.Add(sectorRoomIds[i]);
        }
        return all;
    }

    private struct SectorBounds {
        public int id;
        public string name;
        public int xLo, xHi; // cell-space, exclusive upper
        public int zLo, zHi;
        public int anchorX, anchorZ; // chamber's sector-side corner cell (corridor anchor)
    }

    /// <summary>
    /// Build a synthetic 1x1 RoomRect whose CenterX/CenterZ resolve to the given anchor cell.
    /// Used so ConnectRooms treats the chamber as a "room" placed at its sector-side corner —
    /// the L-corridor from chamber to sector-room then carves a strip that stays inside the
    /// sector instead of running through the divider line at chamber center.
    /// </summary>
    private static RoomRect MakeSyntheticAnchorRoom(int anchorX, int anchorZ) {
        // Center = x0 + w/2 with w=1 → center = x0. So set x0 = anchorX.
        return new RoomRect { x0 = anchorX, z0 = anchorZ, w = 1, h = 1 };
    }

    // ─────────────────────────────────────────────
    //  Sectored layout — "orthogonal" style
    // ─────────────────────────────────────────────
    //
    // Topology: 4 cardinal sectors (N, E, S, W), each entered from the chamber by exactly
    // one straight cardinal corridor leading to a unique "first room". After the first
    // room, the rest of the sector branches via a within-sector MST + extras. The chamber
    // does NOT participate in any sector's MST, so it has exactly 4 corridors total.
    //
    // Sectors are spatial wedges (N / E / S / W) but with NO walls between them. Rooms
    // are constrained by rejection sampling to lie entirely in their assigned wedge,
    // with a "wedge buffer" margin from the diagonals that's wide enough that any
    // L-corridor between two same-wedge rooms keeps its full perpendicular strip width
    // inside the wedge. With buffer >= corridor_width/2 + 1 and corridor_width=3, rooms
    // must satisfy dz > |dx| + 2 for N etc. — corridor strips of width 3 between two
    // such rooms can't reach a cell in another wedge, so no cross-sector floor merge
    // can happen.
    //
    // Cells in the unbuffered "boundary" diagonal area between wedges are NOT walls.
    // They start as wall (mask=true) but corridors are free to carve through them when
    // routing — this is what makes corridors look continuous. They simply don't host
    // rooms and won't connect any sectors because no other-sector room can have a cell
    // close enough to be 4-connected to a carved boundary cell (the buffer guarantees
    // at least one column of uncarved wall sits between them).
    //
    // First room: per sector, one room is forced axis-aligned (i=mid_x for N/S, j=mid_z
    // for E/W) and on the correct side of the chamber. The cardinal corridor runs
    // straight from the chamber's cardinal-edge midpoint to this room's center, using
    // CarveLCorridor with collinear endpoints (the L collapses to a single straight leg).
    //
    // Chamber-size constraint: just that the chamber be wider than the corridor.
    private List<RoomRect> GenerateOrthogonalSectoredLayout(System.Random rng,
                                                            out List<int> sectorIds,
                                                            out int chamberIndex) {
        sectorIds = new List<int>();
        chamberIndex = -1;

        int chamberSizeCells = WorldLoadingController.GetParamInt("maze/sectors/central_chamber_size_cells", 5);
        chamberSizeCells = Mathf.Max(3, chamberSizeCells);

        int border = borderWalls ? 1 : 0;
        int minMaskSide = chamberSizeCells + 2 * (border + roomMinSizeCells + 1);
        if (_maskW < minMaskSide || _maskH < minMaskSide) {
            WorldGenStatus.Error("MazeLayoutLoader",
                $"orthogonal sectors: world too small for chamber={chamberSizeCells} + 4 sectors. " +
                $"Mask {_maskW}x{_maskH} needs >= {minMaskSide}x{minMaskSide}.");
            return new List<RoomRect>();
        }

        int Cx = (_maskW - chamberSizeCells) / 2;
        int Cz = (_maskH - chamberSizeCells) / 2;
        int mid_x = Cx + chamberSizeCells / 2;
        int mid_z = Cz + chamberSizeCells / 2;
        RoomRect chamber = new RoomRect { x0 = Cx, z0 = Cz, w = chamberSizeCells, h = chamberSizeCells };

        CarveRect(chamber.x0, chamber.z0, chamber.w, chamber.h);

        // Classify cells into wedges with a buffer wide enough that any L-corridor
        // between two same-wedge rooms keeps its perpendicular strip in the wedge —
        // preventing cross-sector floor merges without stamping any walls. No
        // _protectedWalls in this style; the boundary diagonals are just unclassified
        // wall cells (mask=true, uncarved) that corridors are free to pass through.
        int wedgeBuffer = corridorWidthCells / 2 + 1;
        int[,] sectorOf = new int[_maskW, _maskH];
        for (int i = 0; i < _maskW; i++) {
            for (int j = 0; j < _maskH; j++) {
                bool inChamber = (i >= Cx && i < Cx + chamberSizeCells &&
                                  j >= Cz && j < Cz + chamberSizeCells);
                if (inChamber) { sectorOf[i, j] = -2; continue; }
                bool inBorder = (i < border || i >= _maskW - border ||
                                 j < border || j >= _maskH - border);
                if (inBorder) { sectorOf[i, j] = -1; continue; }
                int dx = i - mid_x;
                int dz = j - mid_z;
                int adx = System.Math.Abs(dx), adz = System.Math.Abs(dz);
                if      (dz >  adx + wedgeBuffer) sectorOf[i, j] = 0; // N
                else if (dx >  adz + wedgeBuffer) sectorOf[i, j] = 1; // E
                else if (-dz > adx + wedgeBuffer) sectorOf[i, j] = 2; // S
                else if (-dx > adz + wedgeBuffer) sectorOf[i, j] = 3; // W
                else sectorOf[i, j] = -1; // boundary diagonal area, not a wall (corridors may carve through)
            }
        }

        // Per-sector cardinal axis info: which axis the first room must align to and the
        // chamber-edge midpoint where the cardinal corridor starts.
        OrthoSectorAxis[] axes = new OrthoSectorAxis[4] {
            new OrthoSectorAxis { id = 0, name = "N",
                anchorX = mid_x, anchorZ = Cz + chamberSizeCells - 1, axis = AxisDir.Vertical, sign = +1 },
            new OrthoSectorAxis { id = 1, name = "E",
                anchorX = Cx + chamberSizeCells - 1, anchorZ = mid_z, axis = AxisDir.Horizontal, sign = +1 },
            new OrthoSectorAxis { id = 2, name = "S",
                anchorX = mid_x, anchorZ = Cz, axis = AxisDir.Vertical, sign = -1 },
            new OrthoSectorAxis { id = 3, name = "W",
                anchorX = Cx, anchorZ = mid_z, axis = AxisDir.Horizontal, sign = -1 },
        };

        List<RoomRect> sectorRooms = new List<RoomRect>();
        List<int> sectorRoomIds = new List<int>();
        int[] firstRoomIdx = { -1, -1, -1, -1 };

        // Step 1: place one axis-aligned first room per sector. We try up to roomMaxAttempts
        // per sector. A fail here = no entry from chamber to that sector at all (the sector
        // ends up sealed off), so we log a hard error.
        for (int s = 0; s < 4; s++) {
            OrthoSectorAxis ax = axes[s];
            for (int attempt = 0; attempt < roomMaxAttempts; attempt++) {
                int w = rng.Next(roomMinSizeCells, roomMaxSizeCells + 1);
                int h = rng.Next(roomMinSizeCells, roomMaxSizeCells + 1);
                int x0, z0;
                if (ax.axis == AxisDir.Vertical) {
                    // Center on x = mid_x → x0 = mid_x - w/2
                    x0 = mid_x - w / 2;
                    if (ax.sign > 0) {
                        // N: room must be entirely above chamber (z0 >= Cz+CW), within mask.
                        int zLo = Cz + chamberSizeCells;
                        int zHi = _maskH - border - h;
                        if (zHi < zLo) break;
                        z0 = rng.Next(zLo, zHi + 1);
                    } else {
                        int zLo = border;
                        int zHi = Cz - h;
                        if (zHi < zLo) break;
                        z0 = rng.Next(zLo, zHi + 1);
                    }
                } else {
                    // Center on z = mid_z → z0 = mid_z - h/2
                    z0 = mid_z - h / 2;
                    if (ax.sign > 0) {
                        int xLo = Cx + chamberSizeCells;
                        int xHi = _maskW - border - w;
                        if (xHi < xLo) break;
                        x0 = rng.Next(xLo, xHi + 1);
                    } else {
                        int xLo = border;
                        int xHi = Cx - w;
                        if (xHi < xLo) break;
                        x0 = rng.Next(xLo, xHi + 1);
                    }
                }
                RoomRect candidate = new RoomRect { x0 = x0, z0 = z0, w = w, h = h };
                if (!IsRoomEntirelyInSector(candidate, s, sectorOf)) continue;
                if (RoomsOverlapWithSeparation(candidate, chamber, 1)) continue;
                if (sectorRooms.Any(r => RoomsOverlapWithSeparation(r, candidate, roomMinSeparationCells))) continue;
                sectorRooms.Add(candidate);
                sectorRoomIds.Add(s);
                firstRoomIdx[s] = sectorRooms.Count - 1;
                break;
            }
            if (firstRoomIdx[s] < 0) {
                WorldGenStatus.Error("MazeLayoutLoader",
                    $"orthogonal sectors: failed to place axis-aligned first room in sector '{axes[s].name}' " +
                    $"after {roomMaxAttempts} attempts. The sector will be unreachable from the chamber. " +
                    $"Try shrinking room sizes, growing the world, or reducing the chamber size.");
            }
        }

        // Step 2: round-robin sector assignment. Each candidate must lie entirely in the
        // current target wedge (using the buffered sectorOf classification), so corridors
        // between two same-sector rooms can't drift into a neighbouring wedge.
        int sectorRoomBudget = Mathf.Max(0, nRooms - 1);
        int totalAttempts = 0;
        int totalBudget = Mathf.Max(1, sectorRoomBudget) * roomMaxAttempts;
        int nextSector = 0;
        while (sectorRooms.Count < sectorRoomBudget && totalAttempts < totalBudget) {
            totalAttempts++;
            int s = nextSector;
            int w = rng.Next(roomMinSizeCells, roomMaxSizeCells + 1);
            int h = rng.Next(roomMinSizeCells, roomMaxSizeCells + 1);
            int xMax = _maskW - border - w;
            int zMax = _maskH - border - h;
            if (xMax < border || zMax < border) { nextSector = (nextSector + 1) % 4; continue; }
            int x0 = rng.Next(border, xMax + 1);
            int z0 = rng.Next(border, zMax + 1);
            RoomRect candidate = new RoomRect { x0 = x0, z0 = z0, w = w, h = h };
            if (!IsRoomEntirelyInSector(candidate, s, sectorOf)) continue;
            if (RoomsOverlapWithSeparation(candidate, chamber, 1)) continue;
            if (sectorRooms.Any(r => RoomsOverlapWithSeparation(r, candidate, roomMinSeparationCells))) continue;
            sectorRooms.Add(candidate);
            sectorRoomIds.Add(s);
            nextSector = (nextSector + 1) % 4;
        }

        if (verbose)
            Debug.Log($"MazeLayoutLoader.orthogonal: placed {sectorRooms.Count}/{sectorRoomBudget} sector rooms " +
                      $"+ 1 chamber ({chamberSizeCells}x{chamberSizeCells} at ({Cx},{Cz})) after {totalAttempts} attempts");

        foreach (RoomRect r in sectorRooms) CarveRect(r.x0, r.z0, r.w, r.h);

        // Step 3: cardinal corridors. CarveLCorridor with collinear endpoints (same x for
        // N/S, same z for E/W) collapses the L into a single straight leg of the right
        // axis — exactly what we want.
        for (int s = 0; s < 4; s++) {
            if (firstRoomIdx[s] < 0) continue;
            OrthoSectorAxis ax = axes[s];
            RoomRect first = sectorRooms[firstRoomIdx[s]];
            if (ax.axis == AxisDir.Vertical) {
                // Vertical corridor at i=mid_x. CarveLCorridor with ax.x=first.CenterX=mid_x
                // collapses to one vertical leg.
                CarveLCorridor(ax.anchorX, ax.anchorZ, first.CenterX, first.CenterZ, horizontalFirst: false);
            } else {
                CarveLCorridor(ax.anchorX, ax.anchorZ, first.CenterX, first.CenterZ, horizontalFirst: true);
            }
        }

        // Step 4: per-sector MST + extras over sector rooms only (chamber stays out of all
        // graphs — that's how we guarantee exactly 4 chamber corridors). The first room is
        // a regular node; the MST will connect it to its neighbors.
        for (int s = 0; s < 4; s++) {
            List<RoomRect> sectorGraph = new List<RoomRect>();
            for (int i = 0; i < sectorRooms.Count; i++) {
                if (sectorRoomIds[i] == s) sectorGraph.Add(sectorRooms[i]);
            }
            if (sectorGraph.Count >= 2) ConnectRooms(sectorGraph, rng);
        }

        List<RoomRect> all = new List<RoomRect>();
        all.Add(chamber);
        sectorIds.Add(-2);
        chamberIndex = 0;
        for (int i = 0; i < sectorRooms.Count; i++) {
            all.Add(sectorRooms[i]);
            sectorIds.Add(sectorRoomIds[i]);
        }
        return all;
    }

    private enum AxisDir { Vertical, Horizontal }

    private struct OrthoSectorAxis {
        public int id;
        public string name;
        public int anchorX, anchorZ; // chamber-edge midpoint where the cardinal corridor starts
        public AxisDir axis;         // the axis the first room must align to
        public int sign;             // +1 = north/east of chamber, -1 = south/west
    }

    private bool IsRoomEntirelyInSector(RoomRect r, int sectorId, int[,] sectorOf) {
        for (int i = r.x0; i < r.x0 + r.w; i++) {
            if (i < 0 || i >= _maskW) return false;
            for (int j = r.z0; j < r.z0 + r.h; j++) {
                if (j < 0 || j >= _maskH) return false;
                if (sectorOf[i, j] != sectorId) return false;
            }
        }
        return true;
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
            for (int j = z0; j < z1; j++) {
                // Respect sector dividers (sectors mode only) — corridors that overlap them
                // simply skip those cells, leaving the divider intact. This is what enforces
                // the "no inter-sector connectivity except through the chamber" topology
                // even when the L-corridor's perpendicular width spans across a divider line.
                if (_protectedWalls != null && _protectedWalls[i, j]) continue;
                _mask[i, j] = false;
            }
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
        // memory_maze has 1-cell-thick walls. At the corner cells where four corridors meet,
        // a wall cell can have floor neighbors only on the diagonals — its four 4-connected
        // neighbors are all walls. The 4-connected `edge_walls_only` cull would drop those
        // corners, opening diagonal pinholes through which lidar rays leak between regions.
        // Force-disable here regardless of config (mode-specific guard, not a user knob).
        if (edgeWallsOnly) {
            if (verbose) Debug.Log("MazeLayoutLoader.memory_maze: ignoring edge_walls_only=1 (would leak diagonal corner walls)");
            edgeWallsOnly = false;
        }

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

        // Phase 1: place rooms. Random placement with rejection sampling can paint itself
        // into a corner when the packing is tight (e.g. 9 rooms of size 3 in a 15x15 mask
        // is at the theoretical limit), so we wrap the inner placement loop with an outer
        // retry: if the inner loop exhausts roomMaxAttempts without filling maxRooms, we
        // restart room sampling from scratch. We keep the best (largest) attempt so that
        // even if every restart falls short, we return as many rooms as we managed.
        int outerRetries = Mathf.Max(1,
            WorldLoadingController.GetParamInt("maze/room_placement_retries", 100));
        int roomsBaseSeed = WorldLoadingController.GetDerivedSeed("maze_rooms");
        List<RoomRect> rooms = new List<RoomRect>();
        int totalInnerRetries = 0;
        int outerAttemptsUsed = 0;
        for (int outer = 0; outer < outerRetries; outer++) {
            outerAttemptsUsed++;
            // Deterministic per-attempt seed via HashCode.Combine (NOT XOR) — same pattern
            // as WorldLayoutLoader. XOR(baseSeed, attempt) has overlapping cosets across
            // adjacent base seeds (e.g. seed=43,attempt=0 produces the same rng seed as
            // seed=42,attempt=1), so a master-seed bump would converge to the same layout
            // after retries. Per-attempt fresh RNG also makes the result robust to
            // room_max_attempts changes (a failing attempt no longer shifts later ones).
            System.Random attemptRng = new System.Random(System.HashCode.Combine(roomsBaseSeed, outer));
            List<RoomRect> attempt = SampleMemoryMazeRooms(maxRooms, rMin, rMax, roomMaxAttempts, attemptRng, out int innerRetries);
            totalInnerRetries += innerRetries;
            if (attempt.Count > rooms.Count) rooms = attempt;
            if (rooms.Count >= maxRooms) break;
        }

        if (rooms.Count < maxRooms) {
            WorldGenStatus.Error("MazeLayoutLoader",
                $"memory_maze: only placed {rooms.Count}/{maxRooms} rooms after {outerAttemptsUsed} " +
                $"outer retries (each with up to {roomMaxAttempts} inner samples). " +
                $"Reduce maze/n_rooms, raise maze/room_placement_retries, or grow the world.");
        }

        // Carve the chosen rooms into the mask and assign region IDs.
        foreach (RoomRect r in rooms) {
            int rid = nextRegion++;
            for (int i = r.x0; i < r.x0 + r.w; i++)
                for (int j = r.z0; j < r.z0 + r.h; j++) {
                    _mask[i, j] = false;
                    regions[i, j] = rid;
                }
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
                      $"(outer_retries={outerAttemptsUsed}, total_inner_retries={totalInnerRetries}), " +
                      $"regions={nextRegion - 1}, mandatory={mandatory}, extras={extras}");
    }

    /// <summary>
    /// One pass of memory_maze room placement: rejection-sample odd-aligned, odd-sized rooms
    /// until we hit `maxRooms` or exhaust `innerAttempts` consecutive failures. Pure — does
    /// not mutate `_mask` or `regions`. Caller (GenerateMemoryMaze) wraps this in an outer
    /// retry to recover from greedy placements that get stuck below the target count.
    /// </summary>
    private List<RoomRect> SampleMemoryMazeRooms(int maxRooms, int rMin, int rMax,
                                                 int innerAttempts, System.Random rng,
                                                 out int retries) {
        List<RoomRect> rooms = new List<RoomRect>();
        retries = 0;
        while (rooms.Count < maxRooms && retries < innerAttempts) {
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
            rooms.Add(candidate);
        }
        return rooms;
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
                // edge_walls_only: skip wall cells whose 4-connected neighbors are all walls
                // or out-of-bounds. Such cells are invisible (no floor sees them) and don't
                // affect physics or lidar. Cuts block count ~5-10x in dense mazes.
                if (edgeWallsOnly && !IsExposedWall(i, j)) continue;
                float czw = _originZ + (j + 0.5f) * _cellSize;
                if (czw < chunkMinZ || czw >= chunkMaxZ) continue;

                WorldStructure ws = SpawnBlock(new Vector2(cxw, czw));
                if (ws != null) blocks.Add(ws);
            }
        }

        if (blocks.Count > 0) _chunkBlocks[new Vector2Int(cx, cz)] = blocks;
        if (verbose) Debug.Log($"MazeLayoutLoader: chunk ({cx},{cz}) spawned {blocks.Count} blocks");
    }

    /// <summary>
    /// True if the wall cell at (i, j) has at least one 4-connected floor neighbor
    /// inside the mask. Out-of-bounds neighbors count as walls. Used to cull
    /// invisible interior wall cells when `edge_walls_only` is enabled.
    /// </summary>
    private bool IsExposedWall(int i, int j) {
        if (i > 0           && !_mask[i - 1, j]) return true;
        if (i < _maskW - 1  && !_mask[i + 1, j]) return true;
        if (j > 0           && !_mask[i, j - 1]) return true;
        if (j < _maskH - 1  && !_mask[i, j + 1]) return true;
        return false;
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
        public int max;          // -1 = unlimited
        public bool forceCentral; // true = this type designates the central chamber in sectors mode
    }

    private List<RoomTypeSpec> LoadRoomTypeSpecs() {
        List<RoomTypeSpec> specs = new List<RoomTypeSpec>();
        string csv = WorldLoadingController.GetParamString("maze/rooms/types", "");
        if (string.IsNullOrWhiteSpace(csv)) return specs;
        foreach (string raw in csv.Split(',')) {
            string type = raw.Trim();
            if (string.IsNullOrEmpty(type)) continue;
            specs.Add(new RoomTypeSpec {
                type         = type,
                min          = WorldLoadingController.GetParamInt($"maze/rooms/{type}/min",  0),
                max          = WorldLoadingController.GetParamInt($"maze/rooms/{type}/max", -1),
                forceCentral = WorldLoadingController.GetParamInt($"maze/rooms/{type}/force_central", 0) != 0,
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

        // In sectors mode, the chamber takes the force_central type and is removed from the
        // pool used for the remaining rooms — so e.g. a config with reward_room/min=4,
        // central_chamber/min=1 over 9 rooms means: chamber gets central_chamber, then
        // AssignRoomTypes runs over the 8 sector rooms with reward_room/min=4 etc.
        string chamberType = null;
        List<RoomTypeSpec> nonChamberSpecs = specs;
        if (_chamberRoomIndex >= 0 && specs.Count > 0) {
            int forceIdx = -1;
            for (int i = 0; i < specs.Count; i++) {
                if (specs[i].forceCentral) {
                    if (forceIdx >= 0) {
                        WorldGenStatus.Error("MazeLayoutLoader",
                            $"sectors mode: more than one room type has force_central=1 " +
                            $"('{specs[forceIdx].type}' and '{specs[i].type}'). Exactly one is required.");
                        forceIdx = -1; break;
                    }
                    forceIdx = i;
                }
            }
            if (forceIdx >= 0) {
                RoomTypeSpec fc = specs[forceIdx];
                if (fc.min != 1 || fc.max != 1) {
                    WorldGenStatus.Error("MazeLayoutLoader",
                        $"sectors mode: force_central type '{fc.type}' must have min=1 and max=1 " +
                        $"(got min={fc.min}, max={fc.max}).");
                } else {
                    chamberType = fc.type;
                    nonChamberSpecs = new List<RoomTypeSpec>(specs.Count - 1);
                    for (int i = 0; i < specs.Count; i++)
                        if (i != forceIdx) nonChamberSpecs.Add(specs[i]);
                }
            } else if (specs.Count > 0) {
                WorldGenStatus.Error("MazeLayoutLoader",
                    "sectors mode: no room type has force_central=1. Add it to one of " +
                    $"maze/rooms/types ({string.Join(", ", specs.Select(s => s.type))}) " +
                    "so the central chamber has a designated type.");
            }
        }

        // Assignments for non-chamber rooms.
        int nNonChamber = (_chamberRoomIndex >= 0) ? _rooms.Count - 1 : _rooms.Count;
        string[] nonChamberTypes = AssignRoomTypes(nonChamberSpecs, nNonChamber, rng);

        int nonChamberCursor = 0;
        for (int i = 0; i < _rooms.Count; i++) {
            string type;
            if (i == _chamberRoomIndex) {
                type = chamberType ?? "central_chamber";
            } else {
                type = (nonChamberTypes != null) ? nonChamberTypes[nonChamberCursor++] : "maze_room";
            }
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
