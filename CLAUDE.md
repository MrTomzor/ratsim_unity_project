# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity simulation project for robot/agent training (rat simulator). Agents are controlled externally via TCP — an external Python process sends action commands and receives sensor observations each physics step.

## Building and Running

This is a Unity project (Unity 2022+). Building is done through the Unity Editor — open `RatsimUnityProject/` in Unity and use Build Settings. There are no standalone CLI build commands.

The main scene is `RatsimUnityProject/Assets/Scenes/Wildfire.unity`.

The simulation listens on **TCP port 9000**. External clients connect and drive the simulation step-by-step.

## Architecture Overview

The WorldGen system follows an **ECS-like pattern**:

- **Data** = JSON episode params (global, on `WorldLoadingController`) + typed MonoBehaviour data components on structures (e.g. `HouseData`, `BurnState`, `TreeModificationData`)
- **Systems** = Loaders (`WorldLoadingModule` for chunks, `WorldStructureLoader` for structures) that read data and produce world content
- **Runtime behaviours** = MonoBehaviours that tick each simulation step (fire spread, reward collection, regrowth) and mutate structure data + call `Reload()`

### TCP Communication Layer (`Assets/TCPConnector/`)

`RoslikeTCPServer` is a ROS-like pub/sub broker running on a background thread on port 9000. Protocol:

- Each step, client sends: `{"messages": [{"type": "TypeName", "topic": "/topic", "data": {...}}, ...]}`
- Server dispatches to subscribers, steps physics (`Physics.Simulate`), fires timers, then replies with all published messages plus `StepFinishedMessage` on `/sim_control/step_finished`
- Physics is deterministic and script-driven (`SimulationMode.Script`), 50Hz by default

**To add a new message type:**
1. Define a class extending `Message` in `MessageDefs.cs`
2. Register it in `MessageRegistry.cs`

**Special control topics:**
- `/sim_control/scene_select` (StringMessage) — loads a scene by name
- `/sim_control/do_step` (StepRequestMessage) — controls whether physics is stepped
- `/sim_control/step_finished` (StepFinishedMessage) — published by server each step

**Timer registration:** Components call `conn.RegisterTimerDiscrete(callback, stepsPerTick)` or `conn.RegisterTimerContinuous(callback, periodSeconds)` in Start(). The server fires these each step via `HandleTimers`.

### WorldGen System (`Assets/WorldGen/`)

A two-layer chunk-based procedural world generation pipeline.

#### Layer 1: Chunk Loading

**`WorldLoadingController`** — scene singleton. Stores key-value config params (loaded from JSON). Controls episode lifecycle: `StartEpisode()` → `ClearAllWorldData()` → `InitializeAllModules()`. Agent config is received separately on `/sim_control/agent_config` and stored for `AgentLoader` to read.

Config JSON format:
```json
{"entries": [{"key": "seed", "value": "42"}, {"key": "world_bounds/width", "value": "1000"}, ...]}
```

**`WorldLoadingModule`** — abstract base class for chunk-level generator components. Subclasses override:
- `Initialize()` — called once per episode after `Clear()`, before any chunk loading. Use for work that must happen regardless of chunk requests (e.g. spawning agents). Default is no-op.
- `OnChunkLoadRequested(cx, cz, lod)` — called when a chunk enters view range
- `OnChunkUnloadRequested(cx, cz, lod)` — called when a chunk leaves range
- `Clear()` — destroys all generated content, resets state

**`ChunkLoadingRequestor`** — attaches to an agent, ticks every sim step. Maintains a set of loaded chunks: inner radius → LOD0, outer radius → LOD1. Notifies all registered `WorldLoadingModule`s when chunks load/unload.

#### Layer 2: Structure Loading

**`StructureLoadingCoordinator`** (`WorldLoadingModule`) — bridges chunk events into per-structure events. When a chunk loads, queries `WorldData` for structures in that chunk and fires `OnWorldStructureLoaded` on all `WorldStructureLoader`s. Also subscribes to `WorldData.OnNewStructureRegistered` for structures placed dynamically mid-generation (e.g. houses spawned by CityLoader inside a city).

**`WorldStructureLoader`** — abstract base (extends `WorldLoadingModule`) for structure-level loaders. Subclasses override:
- `OnWorldStructureLoaded(WorldStructure s, int lod)` — structure becomes visible
- `OnWorldStructureUnloaded(WorldStructure s, int lod)` — structure fully out of range
- `Clear()`

**`SimpleStructureLoader`** — generic `WorldStructureLoader` that spawns `{type}_LOD{n}` prefabs from `Resources/WorldGen/WorldStructurePrefabs/` as children. Sets spawned content to Default layer (not WorldGen layer). If no LOD-specific prefab exists, does nothing.

#### Data: WorldStructure and Typed Data Components

**`WorldData`** — static spatial index of all `WorldStructure` instances, indexed by chunk. Factory: `WorldData.SpawnStructure(type, pos, rot, parent)`. Fires `OnNewStructureRegistered` event on registration.

**`WorldStructure`** — MonoBehaviour on structure prefabs. Defines 2D footprint via `BoxCollider footprintCollider`. Tracks `currentLod` (-1 = not loaded, managed by coordinator). Auto-registers with `WorldData` on Awake.

**Typed data components** — MonoBehaviours attached to structure GameObjects alongside `WorldStructure`. These hold typed, domain-specific state:
- **Data components go on the WorldStructure GO, not the LOD child** — they persist across `Reload()` cycles
- **Prefabs define defaults** — e.g. `house_basic` prefab has `HouseData { style: "suburban", floors: 1 }`
- **Loaders set/override fields** — CityLoader may set `data.style = cityPalette`
- **Loaders can add components dynamically** — e.g. add `BurnState` to structures in fire-prone areas
- Examples: `HouseData`, `BurnState`, `TreeModificationData { mode, densityReductionFactor }`, `RewardObjectData { type, ripe, rotten, collected }`

**Runtime state changes** use `WorldStructure.Reload()` — unloads then reloads at current LOD, causing loaders to regenerate content from the (now-mutated) data components.

#### Config and Data Flow

Two-layer config model:
1. **JSON params** (`WorldLoadingController`) — episode-level policy knobs. High-level overrides like `"override_house_appearance"` = `"charred"`.
2. **Typed data components** — concrete state on structures, set by loaders during generation, read by other loaders and runtime behaviours.

Flow: JSON params influence **loaders** → loaders produce **structures with typed component data** → other loaders and runtime systems read/write that data.

**Domain-specific spatial queries live on loaders, not WorldData:**
- `WorldHeightLoader.GetTerrainHeight(x, z)` — existing pattern
- `WaterLoader.IsWater(x, y)` — future example
- Each loader owns its domain data and exposes a static query API
- WorldData stays purely as the structure spatial index

#### Concrete Modules

Chunk-level (`WorldLoadingModule`):
- `WorldHeightLoader` — `GetTerrainHeight(x,z)` via "superflat" or "perlin" mode
- `TerrainMeshLoader` — generates mesh terrain per-chunk with LOD and normal stitching
- `TerrainTextureLoader` — applies textures to terrain chunks
- `WorldLayoutLoader` — places structures and road network (MST + extra edges). Runs once on first chunk load.
- `TreeLoader` — places trees per chunk at LOD0, checks for `TreeModificationData` on overlapping structures
- `StructureLoadingCoordinator` — bridges chunk → structure events
- `AgentLoader` — spawns agent prefabs from `Resources/AgentPrefabs/` based on agent config JSON. Manages sensor enable/disable and param overrides via reflection. Uses `Initialize()` (not `OnChunkLoadRequested`) because the agent carries the `ChunkLoadingRequestor`. Calls `RoslikeTCPServer.CleanupDestroyedTimersAndSubscribers()` on `Clear()` to purge stale sensor callbacks.

Structure-level (`WorldStructureLoader`):
- `SimpleStructureLoader` — spawns `{type}_LOD{n}` prefab as child
- `CityLoader` — fills "city" structures with house WorldStructures, sets `HouseData`

**Structure prefabs** live in `Resources/WorldGen/WorldStructurePrefabs/`. Named `{type}` or `{type}_LOD{n}`. Current types: `city`, `village`, `farm`, `orchard`, `road`, `house_basic`.

#### Module Registration Order (registration order matters)

Registration order is controlled by **Unity's Script Execution Order** (Edit → Project Settings → Script Execution Order), not by scene hierarchy position. `WorldLoadingModule.OnEnable()` appends to `registered`, so whichever script's `OnEnable` fires first is registered first. Set the execution order there when adding new modules.

Current required order:
1. WorldHeightLoader ← terrain heights available for all subsequent Initialize() calls
2. TerrainMeshLoader / TerrainTextureLoader
3. WorldLayoutLoader ← **Initialize() now generates all structures eagerly** (cities, roads, …)
4. StructureLoadingCoordinator ← must be AFTER WorldLayoutLoader
5. SimpleStructureLoader, CityLoader, other WorldStructureLoaders ← AFTER coordinator;
   **CityLoader.Initialize() now places houses eagerly** so AgentLoader can see their footprints
6. AgentLoader ← spawns agent in Initialize(); city/city_outskirts modes need cities+houses
   already in WorldData (satisfied by the order above)
7. TreeLoader ← OnChunkLoadRequested generates trees; respects AgentLoader-registered clear zones

`Clear()` is called in reverse order, so higher-level loaders clean up before lower-level ones destroy base structures.

#### Adding New Content

**New structure type loader:**
1. Create class extending `WorldStructureLoader`
2. Override `OnWorldStructureLoaded` (filter by `s.structureType`)
3. Override `OnWorldStructureUnloaded` and `Clear()`
4. Add to scene after `StructureLoadingCoordinator`

**New data component:**
1. Create a MonoBehaviour with plain serializable fields (no logic)
2. Add to relevant prefabs for defaults
3. Have loaders read/write it via `GetComponent<T>()`

**New domain query (e.g. WaterLoader.IsWater):**
1. Create a `WorldLoadingModule` that generates/tracks the data
2. Expose a `public static` query method
3. Other systems call it directly (like `WorldHeightLoader.GetTerrainHeight`)

**Runtime state change (e.g. fire burns a house):**
1. Mutate the structure's data component(s)
2. Call `structure.Reload()` — triggers unload + reload at current LOD
3. Loaders regenerate content from updated data

**Key world config params:**
- `seed`, `world_bounds/width`, `world_bounds/height`, `world_bounds/structures_margin`
- `layout/structures/types` (comma list), `layout/structures/{type}/min`, `/max`
- `city/house_spacing`, `city/max_houses`, `city/max_attempts`
- `tree_generation/density`
- `height_generation/mode` (`superflat` or `perlin`)
- `agents_spawn_pos`: `origin` (default, random within world bounds), `city` (inside city OBB
  outside house footprints; falls back to `city_outskirts`), or `city_outskirts` (radially
  outside city OBB, clears a tree-free zone at the spawn point)
- `agents_city_spawn_attempts`: max random tries to find an open spot inside a city (default 200)
- `agents_outskirts_margin`: extra radial distance past city half-diagonal for outskirts spawn (default 15)
- `agents_outskirts_clear_radius`: radius of tree-suppression zone registered at outskirts spawn (default 5)

**Agent config params** (sent on `/sim_control/agent_config`, read by `AgentLoader`):
- `prefab_name` — prefab loaded from `Resources/AgentPrefabs/{name}`
- `name_prefix` — agent GameObject name
- `sensors` — comma-separated sensor names: `lidar2d`, `rgbd`, `odom`, `collision`, `relative_pose`, `absolute_pose`
- `actuators` — actuator mode (e.g. `velocity`)
- `{sensor_name}/{field}` — override a public field on a sensor component (e.g. `lidar2d/maxRange`)

### Sensors and Actuators

All sensors publish via `conn.Publish(topic, msg)` on a discrete timer. All actuators subscribe via `conn.Subscribe<T>(topic, callback)`.

**Sensors** (`Assets/Sensors/`): `SemanticLidarSensor`, `RGBDSensor`, `AbsolutePose2DSensor`, `Odom2DSensor`, `RelativePoseSensor`, `CollisionSensor`, `VirtualVisualTrackerSensor`

**Actuators** (`Assets/Actuators/`): `Twist2DActuator` (subscribes to `TwistMessage` for velocity/acceleration control), `PoseTeleportActuator` (subscribes to `PoseMessage`)

**Coordinate convention:** All data crossing TCP uses ROS standard (x=forward, y=left, z=up). Unity sensors/actuators convert internally via `CoordConversion.cs` (`Assets/TCPConnector/`). Sensors publish `PoseMessage`; actuators subscribe to `TwistMessage` or `PoseMessage`.

Semantic objects implement `SemanticObject` (base class) — raycasts return descriptors from the hit object.

### Wildfire Scene (`Assets/WildfireMechanics/`)

`WildfireWorldManager` is an older, self-contained world manager with its own generation logic (trees, roads, houses, car spawners). It coexists with the newer WorldGen system in the Wildfire scene. It receives worldgen parameters via TCP topics (`/worldgen/*`) and triggers generation via `/worldgen/requested`.

## Coordinate Conventions

- Unity XZ plane is horizontal, Y is up.
- 2D positions in WorldGen use `Vector2` where `.x` = world X and `.y` = world Z.
- Rotations are stored as CCW degrees (`rotationCCW`). In Unity: `Quaternion.Euler(0f, -rotationCCW, 0f)`.
- LOD integers: lower = more detailed (LOD0 = full detail, LOD1 = reduced). LOD covers both visual and logic/physics detail — the `{type}_LOD{n}` prefabs should NOT be on the WorldGen layer (WorldGen layer is only for generation-time physics queries, not rendering).

## Layers

- **WorldGen** — used for generation-time physics queries (structure footprint colliders). Excluded from agent camera rendering. LOD content spawned by SimpleStructureLoader is set to Default layer, not WorldGen.
- **Default** — standard rendering/physics layer for LOD content visible to agents.
