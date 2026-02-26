using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generic WorldStructureLoader that spawns LOD-specific visual prefabs as children
/// of WorldStructure instances.
///
/// Naming convention: Resources/WorldGen/WorldStructurePrefabs/{type}_LOD{n}
/// If no LOD-specific variant exists for a given LOD, the request is silently ignored
/// (the structure's existing visuals, if any, remain).
///
/// Loaded content is parented to the WorldStructure transform and destroyed on unload.
/// </summary>
public class SimpleStructureLoader : WorldStructureLoader {

    private const string PrefabFolder = "WorldGen/WorldStructurePrefabs/";

    // structure -> currently spawned visual content
    private readonly Dictionary<WorldStructure, GameObject> _spawnedContent
        = new Dictionary<WorldStructure, GameObject>();

    // ─────────────────────────────────────────────
    //  WorldStructureLoader
    // ─────────────────────────────────────────────

    public override void OnWorldStructureLoaded(WorldStructure s, int lod) {
        // Destroy existing content first (handles LOD upgrades/downgrades).
        DestroyContent(s);

        string prefabName = $"{s.structureType}_LOD{lod}";
        GameObject prefab = Resources.Load<GameObject>($"{PrefabFolder}{prefabName}");

        if (prefab == null) return; // no LOD variant for this type — leave existing visuals alone

        GameObject content = Instantiate(prefab, s.transform.position, s.transform.rotation, s.transform);
        content.name = prefabName;
        // LOD content is not a WorldGen blocker — reset to Default layer so it
        // renders in agent cameras and is excluded from WorldGen physics queries.
        SetLayerRecursive(content, 0);
        _spawnedContent[s] = content;
    }

    public override void OnWorldStructureUnloaded(WorldStructure s, int lod) {
        DestroyContent(s);
    }

    public override void Clear() {
        foreach (var kvp in _spawnedContent)
            if (kvp.Value != null) Destroy(kvp.Value);
        _spawnedContent.Clear();
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    private static void SetLayerRecursive(GameObject go, int layer) {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private void DestroyContent(WorldStructure s) {
        if (_spawnedContent.TryGetValue(s, out GameObject old)) {
            if (old != null) Destroy(old);
            _spawnedContent.Remove(s);
        }
    }
}
