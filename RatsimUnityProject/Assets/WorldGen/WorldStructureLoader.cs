using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Abstract base for components that react to individual WorldStructure load/unload events.
/// Extends WorldLoadingModule so it is automatically included in the episode Clear() loop
/// and can optionally react to chunk events too.
///
/// Scene ordering: WorldStructureLoader components must be registered AFTER
/// StructureLoadingCoordinator (i.e. appear later in the scene hierarchy).
/// </summary>
public abstract class WorldStructureLoader : WorldLoadingModule {

    public static new List<WorldStructureLoader> registered = new List<WorldStructureLoader>();

    protected override void OnEnable() {
        base.OnEnable();
        registered.Add(this);
    }

    protected override void OnDisable() {
        base.OnDisable();
        registered.Remove(this);
    }

    // Default no-ops for chunk callbacks — override only if needed.
    public override void OnChunkLoadRequested(int cx, int cz, int lod) { }
    public override void OnChunkUnloadRequested(int cx, int cz, int lod) { }

    /// <summary>Called when a structure becomes visible at the given LOD (or upgrades to better LOD).</summary>
    public abstract void OnWorldStructureLoaded(WorldStructure s, int lod);

    /// <summary>Called when a structure is fully out of range (no loaded chunk covers it).</summary>
    public abstract void OnWorldStructureUnloaded(WorldStructure s, int lod);
}
