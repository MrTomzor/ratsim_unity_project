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

        string lodName = $"LOD{lod}";

        // 1. Try a dedicated prefab: Resources/.../house_basic_LOD0
        string prefabName = $"{s.structureType}_{lodName}";
        GameObject source = Resources.Load<GameObject>($"{PrefabFolder}{prefabName}");

        // 2. Fall back: load base prefab and extract the named LOD child.
        //    Only the extracted child is instantiated — disabled siblings never
        //    enter the scene, so they don't cost runtime memory.
        if (source == null) {
            GameObject basePrefab = Resources.Load<GameObject>($"{PrefabFolder}{s.structureType}");
            if (basePrefab != null) {
                Transform lodChild = basePrefab.transform.Find(lodName);
                if (lodChild != null)
                    source = lodChild.gameObject;
            }
        }

        if (source == null) return; // no LOD variant — leave existing visuals alone

        GameObject content = Instantiate(source, s.transform.position, s.transform.rotation, s.transform);
        content.name = lodName;
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
