using System.Collections.Generic;
using UnityEngine;

public class TreeLoader : WorldLoadingModule {

    public static TreeLoader instance;

    [SerializeField] private GameObject treePrefab;

    private int _worldGenLayer;
    public float _density;
    private float _chunkWidth;
    private int _treeSeed;

    public bool verbose;

    // chunkID → chunk parent GameObject
    private Dictionary<Vector2Int, GameObject> _chunkObjects = new Dictionary<Vector2Int, GameObject>();
    // chunkID → whether it has been generated (to distinguish "never generated" from "disabled")
    private HashSet<Vector2Int> _generatedChunks = new HashSet<Vector2Int>();

    private void Awake() {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        LoadParams();
    }

    private void LoadParams() {
        _worldGenLayer = LayerMask.GetMask("WorldGen");
        _density       = WorldLoadingController.GetParamFloat("tree_generation/density", _density);
        _chunkWidth    = WorldLoadingController.GetChunkWidth();
        _treeSeed      = WorldLoadingController.GetDerivedSeed("trees");
    }

    // --- WorldLoadingModule ---

    public override void OnChunkLoadRequested(int cx, int cz, int lod) {
        // trees only at LOD0
        //Debug.Log($"TreeLoader: Load requested for chunk ({cx}, {cz}) at LOD {lod}");

        if (lod != 0) return;

        Vector2Int chunkID = new Vector2Int(cx, cz);

        if (_generatedChunks.Contains(chunkID)) {
            // already generated — just re-enable
            if (_chunkObjects.TryGetValue(chunkID, out GameObject chunkObj))
                chunkObj.SetActive(true);
            return;
        }

        GenerateChunk(chunkID);
    }

    public override void OnChunkUnloadRequested(int cx, int cz, int lod) {
        if (lod != 0) return;

        Vector2Int chunkID = new Vector2Int(cx, cz);
        if (_chunkObjects.TryGetValue(chunkID, out GameObject chunkObj))
            chunkObj.SetActive(false);
    }

    public override void Clear() {
        foreach (var kvp in _chunkObjects)
            if (kvp.Value != null) Destroy(kvp.Value);
        _chunkObjects.Clear();
        _generatedChunks.Clear();
    }

    // --- Generation ---

    private void GenerateChunk(Vector2Int chunkID) {
        // chunk bounds in world space
        float originX = chunkID.x * _chunkWidth;
        float originZ = chunkID.y * _chunkWidth;

        // collect blocking colliders in this chunk area
        Vector3 chunkCenter = new Vector3(originX + _chunkWidth * 0.5f, 0f, originZ + _chunkWidth * 0.5f);
        Collider[] blockers = Physics.OverlapBox(
            chunkCenter,
            new Vector3(_chunkWidth * 0.5f, 1000f, _chunkWidth * 0.5f),
            Quaternion.identity,
            _worldGenLayer
        );

        // create chunk parent
        GameObject chunkObj = new GameObject($"TreeChunk_{chunkID.x}_{chunkID.y}");
        chunkObj.transform.SetParent(transform);
        _chunkObjects[chunkID] = chunkObj;
        _generatedChunks.Add(chunkID);

        // deterministic rng for this chunk
        int chunkSeed = _treeSeed ^ (chunkID.x * 1000003) ^ (chunkID.y * 999983);
        System.Random rng = new System.Random(chunkSeed);

        // how many trees to attempt
        int treeCount = Mathf.RoundToInt(_density * _chunkWidth * _chunkWidth);
        if(verbose) Debug.Log("Chunk w: " + _chunkWidth + ", tree count: " + treeCount);
        if(verbose) Debug.Log($"TreeLoader: Generating chunk ({chunkID.x}, {chunkID.y}) with seed {chunkSeed}, attempting to place {treeCount} trees. Num blockers: {blockers.Length}");

        for (int i = 0; i < treeCount; i++) {
            float x = originX + (float)rng.NextDouble() * _chunkWidth;
            float z = originZ + (float)rng.NextDouble() * _chunkWidth;

            if (IsBlocked(new Vector2(x, z), blockers)) continue;

            float y = WorldHeightLoader.GetTerrainHeight(x, z);
            GameObject tree = Instantiate(treePrefab, new Vector3(x, y, z), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            tree.transform.SetParent(chunkObj.transform);
        }
    }

    // --- Blocking ---

    private bool IsBlocked(Vector2 point, Collider[] blockers) {
        foreach (var col in blockers) {
            if (col is BoxCollider box && IsBlockedByBox(point, box)) return true;
            if (col is CapsuleCollider cap && IsBlockedByCapsule(point, cap)) return true;
        }
        return false;
    }

    private bool IsBlockedByBox(Vector2 point, BoxCollider box) {
        Vector3 center3 = box.transform.TransformPoint(box.center);
        Vector2 center = new Vector2(center3.x, center3.z);
        Vector2 size = new Vector2(
            box.size.x * box.transform.lossyScale.x,
            box.size.z * box.transform.lossyScale.z
        );
        float yRot = box.transform.eulerAngles.y;

        Vector2 delta = point - center;
        float rad = -yRot * Mathf.Deg2Rad;
        Vector2 local = new Vector2(
            delta.x * Mathf.Cos(rad) - delta.y * Mathf.Sin(rad),
            delta.x * Mathf.Sin(rad) + delta.y * Mathf.Cos(rad)
        );
        return Mathf.Abs(local.x) <= size.x * 0.5f &&
               Mathf.Abs(local.y) <= size.y * 0.5f;
    }

    private bool IsBlockedByCapsule(Vector2 point, CapsuleCollider cap) {
        Vector3 center3 = cap.transform.TransformPoint(cap.center);
        Vector2 center = new Vector2(center3.x, center3.z);
        float radius = cap.radius * Mathf.Max(cap.transform.lossyScale.x, cap.transform.lossyScale.z);
        return (point - center).sqrMagnitude <= radius * radius;
    }
}