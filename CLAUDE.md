# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity simulation project for robot/agent training (rat simulator). Agents are controlled externally via TCP — an external Python process sends action commands and receives sensor observations each physics step.

## Building and Running

This is a Unity project (Unity 2022+). Building is done through the Unity Editor — open `RatsimUnityProject/` in Unity and use Build Settings. There are no standalone CLI build commands.

The main scene is `RatsimUnityProject/Assets/Scenes/Wildfire.unity`.

The simulation listens on **TCP port 9000**. External clients connect and drive the simulation step-by-step.

## Architecture

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

A chunk-based procedural world generation pipeline. The hierarchy:

**`WorldLoadingController`** — scene singleton. Stores key-value config params (loaded from JSON). Controls episode lifecycle: `StartEpisode()` clears all modules, `ResetEpisode(json)` reloads config and restarts.

Config JSON format:
```json
{"entries": [{"key": "seed", "value": "42"}, {"key": "world_bounds/width", "value": "1000"}, ...]}
```

**`WorldLoadingModule`** — abstract base class for all generator components. Subclasses override:
- `OnChunkLoadRequested(cx, cz, lod)` — called when a chunk enters view range
- `OnChunkUnloadRequested(cx, cz, lod)` — called when a chunk leaves range
- `Clear()` — destroys all generated content, resets state

Concrete modules (each is a MonoBehaviour on a scene GameObject):
- `WorldHeightLoader` — provides `GetTerrainHeight(x,z)` via "superflat" or "perlin" mode
- `TerrainMeshLoader` — generates mesh terrain per-chunk with LOD and normal stitching
- `TerrainTextureLoader` — applies textures to terrain chunks
- `WorldLayoutLoader` — places structures (cities, villages, etc.) and road network (MST + extra edges). Runs once on first chunk load.
- `TreeLoader` — places tree prefabs per chunk at LOD0 only, respects structure footprints
- `CityLoader` — fills each "city" structure with house prefabs

**`ChunkLoadingRequestor`** — attaches to an agent, ticks every sim step. Maintains a set of loaded chunks: inner radius → LOD0, outer radius → LOD1. Notifies all registered `WorldLoadingModule`s when chunks load/unload.

**`WorldData`** — static registry of all active `WorldStructure` instances, indexed by chunk. Use `WorldData.SpawnStructure(type, pos, rot, parent)` to instantiate structure prefabs.

**`WorldStructure`** — MonoBehaviour on structure prefabs. Requires a `BoxCollider` child as `footprintCollider` to define the 2D footprint. Auto-registers with `WorldData` on Awake. Supports LOD switching via child GameObjects named "LOD0" / "LOD1".

**Structure prefabs** live in `Resources/WorldGen/WorldStructurePrefabs/`. Named `{type}` or `{type}_LOD{n}`. Current types: `city`, `village`, `farm`, `orchard`, `road`, `house_basic`.

**Key config params for WorldLayoutLoader:**
- `layout/structures/types` — comma-separated list of structure types to place
- `layout/structures/{type}/min` and `/max` — placement count range
- `world_bounds/width` / `world_bounds/height`
- `world_bounds/structures_margin`
- `city/house_spacing`, `city/max_houses`, `city/max_attempts`
- `tree_generation/density`
- `height_generation/mode` (`superflat` or `perlin`)

### Sensors and Actuators

All sensors publish via `conn.Publish(topic, msg)` on a discrete timer. All actuators subscribe via `conn.Subscribe<T>(topic, callback)`.

**Sensors** (`Assets/Sensors/`): `SemanticLidarSensor`, `RGBDSensor`, `AbsolutePose2DSensor`, `Odom2DSensor`, `RelativePoseSensor`, `CollisionSensor`, `VirtualVisualTrackerSensor`

**Actuators** (`Assets/Actuators/`): `Twist2DActuator` (velocity or acceleration control), `PoseTeleportActuator`

Semantic objects implement `SemanticObject` (base class) — raycasts return descriptors from the hit object.

### Wildfire Scene (`Assets/WildfireMechanics/`)

`WildfireWorldManager` is an older, self-contained world manager with its own generation logic (trees, roads, houses, car spawners). It coexists with the newer WorldGen system in the Wildfire scene. It receives worldgen parameters via TCP topics (`/worldgen/*`) and triggers generation via `/worldgen/requested`.

## Coordinate Conventions

- Unity XZ plane is horizontal, Y is up.
- 2D positions in WorldGen use `Vector2` where `.x` = world X and `.y` = world Z.
- Rotations are stored as CCW degrees (`rotationCCW`). In Unity: `Quaternion.Euler(0f, -rotationCCW, 0f)`.
- LOD integers: lower = more detailed (LOD0 = full detail, LOD1 = reduced).

## Adding a New WorldLoadingModule

1. Create a MonoBehaviour subclassing `WorldLoadingModule`
2. Override `OnChunkLoadRequested`, `OnChunkUnloadRequested`, and `Clear`
3. Add the component to a scene GameObject — it auto-registers via `OnEnable`/`OnDisable`
4. Module ordering matters for `Clear()` (called in reverse registration order): place higher-level modules (e.g. CityLoader depends on WorldLayoutLoader) later in the scene hierarchy
