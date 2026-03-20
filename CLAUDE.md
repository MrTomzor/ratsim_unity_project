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
- **Systems** = Providers (`WorldDataProvider` for chunk-level generation, `WorldStructureProvider` for per-structure reactions) that declare dependencies and produce world content
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

**`WorldLoadingController`** — scene singleton. Stores key-value config params (loaded from JSON). Controls episode lifecycle: `StartEpisode()` → `ClearAllWorldData()` → `InitializeAllModules()`. Uses topological sort (Kahn's algorithm) on provider dependency declarations — `Generate()` runs in dependency order, `Clear()` runs in reverse. Agent config is received separately on `/sim_control/agent_config` and stored for `AgentLoader` to read.

Config JSON format:
```json
{"entries": [{"key": "seed", "value": "42"}, {"key": "world_bounds/width", "value": "1000"}, ...]}
```

**`WorldDataType`** — enum defining all data products in the pipeline: `Height`, `Layout`, `Boundaries`, `StructureEvents`, `StructureContent`, `Rewards`, `Agents`, `Vegetation`, `TerrainMesh`, `TerrainTexture`, `Lighting`.

**`WorldDataProvider`** — abstract base class for all world generation components. Each provider declares:
- `Provides` — `WorldDataType[]` of data products it creates
- `DependsOn` — `WorldDataType[]` of data products it requires (determines initialization order)
- `Generate()` — called once per episode in dependency order, for global (non-spatial) work
- `GenerateChunk(cx, cz, lod)` — called when a chunk enters view range
- `ClearChunk(cx, cz, lod)` — called when a chunk leaves range
- `Clear()` — destroys all generated content, resets state

**`WorldServices`** — static service locator for cross-provider queries. Providers register typed interfaces in `OnEnable()` (e.g. `WorldServices.Register<IHeightProvider>(this)`). Consumers query without knowing concrete classes: `WorldServices.Get<IHeightProvider>().GetTerrainHeight(x, z)`.

Service interfaces:
- `IHeightProvider` — terrain height queries, terrain modification processing. Implemented by `WorldHeightLoader`.
- `ITerrainMeshProvider` — mesh resolution queries, chunk texturing, terrain collider identification. Implemented by `TerrainMeshLoader`.
- `ILayoutProvider` — entry point queries for road connections. Implemented by `WorldLayoutLoader`.

**`ChunkLoadingRequestor`** — attaches to an agent, ticks every sim step. Maintains a set of loaded chunks: inner radius → LOD0, outer radius → LOD1. Notifies all registered `WorldDataProvider`s when chunks load/unload.

#### Layer 2: Structure Loading

**`StructureLoadingCoordinator`** (`WorldDataProvider`, provides `StructureEvents`, depends on `Layout`) — bridges chunk events into per-structure events. When a chunk loads, queries `WorldData` for structures in that chunk and fires `OnWorldStructureLoaded` on all `WorldStructureProvider`s. Also subscribes to `WorldData.OnNewStructureRegistered` for structures placed dynamically mid-generation (e.g. houses spawned by CityLoader inside a city).

**`WorldStructureProvider`** — abstract base (extends `WorldDataProvider`) for structure-level providers. Subclasses override:
- `OnWorldStructureLoaded(WorldStructure s, int lod)` — structure becomes visible
- `OnWorldStructureUnloaded(WorldStructure s, int lod)` — structure fully out of range
- `Clear()`

**`SimpleStructureLoader`** — generic `WorldStructureProvider` that manages LOD children (named `LOD0`, `LOD1`, etc.) on WorldStructure instances. Enables matching LOD child, disables others, sets Default layer.

#### Data: WorldStructure and Typed Data Components

**`WorldData`** — static spatial index of all `WorldStructure` instances, indexed by chunk. Factory: `WorldData.SpawnStructure(type, pos, rot, parent)`. Fires `OnNewStructureRegistered` event on registration.

**`WorldStructure`** — MonoBehaviour on structure prefabs. Defines 2D footprint via `BoxCollider footprintCollider`. Tracks `currentLod` (-1 = not loaded, managed by coordinator). Auto-registers with `WorldData` on Awake.

**Typed data components** — MonoBehaviours attached to structure GameObjects alongside `WorldStructure`. These hold typed, domain-specific state:
- **Data components go on the WorldStructure GO, not the LOD child** — they persist across `Reload()` cycles
- **Prefabs define defaults** — e.g. `house_basic` prefab has `HouseData { style: "suburban", floors: 1 }`
- **Loaders set/override fields** — CityLoader may set `data.style = cityPalette`
- **Loaders can add components dynamically** — e.g. add `BurnState` to structures in fire-prone areas
- Examples: `HouseData`, `BurnState`, `TreeModificationData { mode, densityReductionFactor }`, `RewardObjectData { type, ripe, rotten, collected }`, `TerrainModification { mode: Flatten|SetHeight|AddHeight, targetHeight, heightDelta, blendMargin }`

**Runtime state changes** use `WorldStructure.Reload()` — unloads then reloads at current LOD, causing loaders to regenerate content from the (now-mutated) data components.

#### Config and Data Flow

Two-layer config model:
1. **JSON params** (`WorldLoadingController`) — episode-level policy knobs. High-level overrides like `"override_house_appearance"` = `"charred"`.
2. **Typed data components** — concrete state on structures, set by loaders during generation, read by other loaders and runtime behaviours.

Flow: JSON params influence **loaders** → loaders produce **structures with typed component data** → other loaders and runtime systems read/write that data.

**Editor-default convention:** All loader config values must be `public` serialized fields on the MonoBehaviour, editable in the Unity Inspector. `LoadParams()` uses the field's current value as the default fallback: `field = WorldLoadingController.GetParamFloat("key", field)`. This way the Inspector values work standalone, and episode JSON overrides them at runtime.

**Domain-specific spatial queries use WorldServices interfaces, not static singletons:**
- `WorldServices.Get<IHeightProvider>().GetTerrainHeight(x, z)`
- `WorldServices.Get<ITerrainMeshProvider>().IsTerrainCollider(col)`
- `WorldServices.Get<ILayoutProvider>().GetEntryPoints(structure)`
- Each provider owns its domain data and registers a typed interface
- WorldData stays purely as the structure spatial index

#### Concrete Modules

Global providers (run in `Generate()`, dependency-ordered):
- `WorldHeightLoader` (provides `Height`) — `IHeightProvider`: terrain height via "superflat" or "perlin" mode. Supports terrain modification influence zones: structures with `TerrainModification` component register OBB-shaped zones that flatten, set, or offset terrain height with smoothstep blending. `ProcessTerrainModifications()` is called by `WorldLayoutLoader` after structure placement. `GetBaseTerrainHeight(x,z)` returns unmodified height.
- `WorldLayoutLoader` (provides `Layout`, depends on `Height`) — `ILayoutProvider`: places structures and road network (MST + extra edges). Runs eagerly in `Generate()`.
- `WorldBoundaryLoader` (provides `Boundaries`, depends on `Height`, `Layout`) — spawns four wall WorldStructures enclosing the world when `world_bounds/boundary_type` = `visible_wall`. Prefab: `Resources/WorldGen/WorldStructurePrefabs/world_boundary`.
- `LightingAndFogLoader` (provides `Lighting`) — applies lighting/fog settings each episode, optionally advances time-of-day.
- `AgentLoader` (provides `Agents`, depends on `Height`, `StructureContent`) — spawns agent prefabs from `Resources/AgentPrefabs/` based on agent config JSON. Manages sensor enable/disable and param overrides via reflection. Calls `RoslikeTCPServer.CleanupDestroyedTimersAndSubscribers()` on `Clear()` to purge stale sensor callbacks.

Chunk-level providers (run in `GenerateChunk()`/`ClearChunk()`):
- `TerrainMeshLoader` (provides `TerrainMesh`, depends on `Height`) — `ITerrainMeshProvider`: generates mesh terrain per-chunk with LOD and normal stitching.
- `TerrainTextureLoader` (provides `TerrainTexture`, depends on `TerrainMesh`, `Height`) — applies textures to terrain chunks.
- `StructureLoadingCoordinator` (provides `StructureEvents`, depends on `Layout`) — bridges chunk → structure events.
- `TreeLoader` (provides `Vegetation`, depends on `Height`, `StructureContent`, `Agents`) — places trees per chunk at LOD0, checks for `VegetationModification` on overlapping structures; also checks `RegisterClearZone()` zones added by AgentLoader.

Structure-level providers (`WorldStructureProvider`, respond to structure load/unload events):
- `SimpleStructureLoader` (provides `StructureContent`, depends on `StructureEvents`) — manages LOD children (named `LOD0`, `LOD1`, etc.) on WorldStructure instances; enables the matching LOD child, disables others, sets Default layer.
- `CityLoader` (provides `StructureContent`, depends on `StructureEvents`) — fills "city" structures with house WorldStructures.
- `HouseLoader` (provides `StructureContent`, depends on `StructureEvents`) — configures house interiors/exteriors (doors, cars, roofs, breakable walls, clutter, layout variants) using global params + per-house seeded RNG.
- `RewardObjectLoader` (provides `Rewards`, depends on `Height`, `StructureEvents`) — spawns reward objects via two parallel modes: uniform (per-chunk density) and structure-based (at `rewardSpawnPositions` in allowed structure types).

**Structure prefabs** live in `Resources/WorldGen/WorldStructurePrefabs/`. Named `{type}`. Each prefab has the `WorldStructure` component and children named `LOD0`, `LOD1`, etc. for each detail level. `SimpleStructureLoader` enables/disables these children based on the requested LOD. Current types: `city`, `village`, `farm`, `orchard`, `road`, `house_basic`.

#### Provider Ordering (automatic via dependency graph)

Ordering is determined automatically by `WorldLoadingController`'s topological sort based on each provider's `Provides` and `DependsOn` declarations. No manual script execution order or scene hierarchy ordering is needed.

The dependency graph produces this order:
```
Height → Layout → Boundaries
Height → TerrainMesh → TerrainTexture
Layout → StructureEvents → StructureContent (SimpleStructureLoader, CityLoader, HouseLoader)
Height + StructureEvents → Rewards
Height + StructureContent → Agents
Height + StructureContent + Agents → Vegetation
Lighting (no deps, runs early)
```

`Generate()` runs in dependency order. `Clear()` runs in reverse, so higher-level providers clean up before lower-level ones destroy base structures.

#### Adding New Content

**New structure type loader:**
1. Create class extending `WorldStructureProvider`
2. Declare `Provides` (e.g. `StructureContent`) and `DependsOn` (at least `StructureEvents`)
3. Override `OnWorldStructureLoaded` (filter by `s.structureType`)
4. Override `OnWorldStructureUnloaded` and `Clear()`
5. Add to scene — ordering is automatic via dependency graph

**New data component:**
1. Create a MonoBehaviour with plain serializable fields (no logic)
2. Add to relevant prefabs for defaults
3. Have providers read/write it via `GetComponent<T>()`

**New domain query (e.g. WaterLoader.IsWater):**
1. Create a `WorldDataProvider` that generates/tracks the data
2. Define an interface (e.g. `IWaterProvider`) in `WorldServices.cs`
3. Register it in `OnEnable()`: `WorldServices.Register<IWaterProvider>(this)`
4. Consumers query: `WorldServices.Get<IWaterProvider>().IsWater(x, z)`

**Runtime state change (e.g. fire burns a house):**
1. Mutate the structure's data component(s)
2. Call `structure.Reload()` — triggers unload + reload at current LOD
3. Loaders regenerate content from updated data

**Key world config params:**
- `seed`, `world_bounds/width`, `world_bounds/height`, `world_bounds/structures_margin`
- `layout/structures/types` (comma list), `layout/structures/{type}/min`, `/max`
- `city/house_spacing`, `city/max_houses`, `city/max_attempts`
- `city/layout_mode`: `"random"` (default, scatter) or `"grid"` (US-style street grid)
- `city/grid_road_spacing`: default target distance between grid lines (default 30); overridden per-axis by `_x`/`_z` variants
- `city/grid_road_spacing_x`: N-S road separation (block width); defaults to `city/grid_road_spacing`
- `city/grid_road_spacing_z`: E-W road separation (block height); defaults to `city/grid_road_spacing`
- `city/grid_margin`: inset from city OBB edge to inner grid boundary (default 15)
- `city/grid_road_width`: width of inner-city grid roads (default 6)
- `lighting/time_of_day`: initial hour 0–24 (default 12 = noon)
- `lighting/time_advance_rate`: in-game hours per simulated second; 0 = static (default 0)
- `lighting/max_light_intensity`: directional light intensity at noon (default 1.2)
- `lighting/max_ambient_intensity`: ambient intensity at noon (default 1)
- `lighting/sun_azimuth`: Y rotation of directional light (default 45)
- `fog/enabled`: 0 or 1 (default 0)
- `fog/color_preset`: `"gray"` | `"ocean"` | `""` (falls back to `fog/color_r/g/b`)
- `fog/color_r`, `fog/color_g`, `fog/color_b`: fog RGB components 0–1
- `fog/density`: fog density (default 0.02)
- `fog/mode`: `"linear"` | `"exponential"` | `"exponential_squared"` (default)
- `house/allowed_door_prefabs`: comma list of door prefab names in `Resources/WorldGen/HouseModulePrefabs/`
- `house/{door_name}/probability`: relative weight for a door type (default 1)
- `house/enable_roofs`: 0 or 1 (default 1)
- `house/chance_wall_broken`: 0.0–1.0 per breakable wall (default 0)
- `house/rubble_prefab`: prefab name for wall rubble replacement
- `house/clutter_density`: 0.0–1.0, fraction of clutter objects enabled (default 1)
- `house/allowed_car_prefabs`: comma list of car prefab names in `Resources/WorldGen/HouseModulePrefabs/`
- `house/car_spawn_chance`: 0.0–1.0 per car spawn position (default 0)
- `reward_objects/prefab_name`: reward prefab in `Resources/WorldGen/RewardObjectPrefabs/` (default `"reward_obj1"`)
- `reward_objects/uniform_density`: objects per unit² for uniform world spawning (default 0 = disabled)
- `reward_objects/allowed_structures`: comma list of structure types for structure-based spawning (default `""` = disabled)
- `reward_objects/{type}/spawn_probability`: 0.0–1.0 per spawn position within a structure (default 1)
- `reward_objects/{type}/skip_probability`: 0.0–1.0 chance to skip an entire structure (default 0)
- `tree_generation/density`
- `height_generation/mode` (`superflat` or `perlin`)
- `meta_height_generation/mode`: `"disabled"` (default) or `"valley"` (terrain rises towards world edges)
- `meta_height_generation/valley_edge_height`: max height added at world edge (default 50)
- `meta_height_generation/valley_exponent`: curve power — 1=linear, 2=quadratic (default 2)
- `world_bounds/boundary_type`: `none` (default, no walls) or `visible_wall` (spawn four wall structures)
- `world_bounds/boundary_height`: Y scale of each wall (default 10)
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
