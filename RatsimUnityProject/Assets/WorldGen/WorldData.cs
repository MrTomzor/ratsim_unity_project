using UnityEngine;
using System.Collections.Generic;
using System.Linq;


public static class WorldData {

    private static readonly List<WorldStructure> _structures = new List<WorldStructure>();
    private static readonly Dictionary<Vector2Int, List<WorldStructure>> _chunkToStructures 
        = new Dictionary<Vector2Int, List<WorldStructure>>();

    public static void RegisterStructure(WorldStructure s) {
        if (_structures.Contains(s)) return; // idempotent
        _structures.Add(s);
        RegisterInChunkDict(s);
    }

    public static void UnregisterStructure(WorldStructure s) {
        _structures.Remove(s);
        // remove from all chunk buckets
        foreach (var list in _chunkToStructures.Values)
            list.Remove(s);
    }

    public static IReadOnlyList<WorldStructure> GetStructures() => _structures;

    public static List<WorldStructure> GetStructuresInChunk(int cx, int cz) {
        _chunkToStructures.TryGetValue(new Vector2Int(cx, cz), out var list);
        return list;
    }

    public static List<WorldStructure> GetStructuresOfTypeInChunk(string type, int cx, int cz) {
        var list = GetStructuresInChunk(cx, cz);
        return list?.Where(s => s.structureType == type).ToList();
    }

    private static void RegisterInChunkDict(WorldStructure s) {
        int cw = (int)WorldLoadingController.GetChunkWidth();
        Bounds2D bb = s.GetBoundingBox2D();

        // get AABB of OBB
        Vector2[] verts = bb.GetVertices();
        float minX = verts.Min(v => v.x), maxX = verts.Max(v => v.x);
        float minZ = verts.Min(v => v.y), maxZ = verts.Max(v => v.y);

        int cxMin = Mathf.FloorToInt(minX / cw), cxMax = Mathf.FloorToInt(maxX / cw);
        int czMin = Mathf.FloorToInt(minZ / cw), czMax = Mathf.FloorToInt(maxZ / cw);

        for (int cx = cxMin; cx <= cxMax; cx++)
        for (int cz = czMin; cz <= czMax; cz++) {
            if (!s.GetBoundingBox2D().Overlaps(GetChunkBounds(cx, cz, cw))) continue;
            var key = new Vector2Int(cx, cz);
            if (!_chunkToStructures.TryGetValue(key, out var list)) {
                list = new List<WorldStructure>();
                _chunkToStructures[key] = list;
            }
            if (!list.Contains(s)) list.Add(s);
        }
    }

    private static Bounds2D GetChunkBounds(int cx, int cz, int cw) {
        float ox = cx * cw, oz = cz * cw;
        return new Bounds2D(
            new Vector2(ox + cw * 0.5f, oz + cw * 0.5f),
            new Vector2(cw, cw), 0f
        );
    }

    public static void Clear() {
        _structures.Clear();
        _chunkToStructures.Clear();
        // ... rest of clear
    }
}
