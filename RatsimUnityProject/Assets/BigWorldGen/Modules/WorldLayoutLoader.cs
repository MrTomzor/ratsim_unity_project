using UnityEngine;
using System.Collections.Generic;

public class WorldLayoutLoader : WorldLoadingModule {
    public bool verbose = false;
    public static WorldLayoutLoader instance;
    public WorldLayout currentLayout = null;

    private bool _generated = false;

    private void Awake() {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    // ─────────────────────────────────────────────
    //  WorldLoadingModule
    // ─────────────────────────────────────────────

    public override void OnChunkLoadRequested(int cx, int cz, int lod) {
        if (_generated) return;
        _generated = true;
        Generate();
    }

    public override void OnChunkUnloadRequested(int cx, int cz, int lod) { }

    public override void Clear() {
        _generated = false;
        currentLayout = null;
    }

    // ─────────────────────────────────────────────
    //  Generation
    // ─────────────────────────────────────────────

    private void Generate() {
        System.Random rng = new System.Random(WorldLoadingController.GetDerivedSeed("layout"));

        float worldW  = WorldLoadingController.GetParamFloat("world_bounds/width");
        float worldH  = WorldLoadingController.GetParamFloat("world_bounds/height");
        float margin  = WorldLoadingController.GetParamFloat("world_bounds/structures_margin", 100f);

        List<StructureData> structures = PlaceStructures(rng, worldW, worldH, margin);
        List<RoadNode>      nodes      = BuildRoadNodes(structures);
        List<RoadEdge>      edges      = BuildRoadNetwork(nodes, rng);

        WorldLayout layout = new WorldLayout {
            structures = structures,
            roadNodes  = nodes,
            roadEdges  = edges
        };

        currentLayout = layout;

        if (debugVisualizeLayoutOnGen)
        {
            Debug_VisualizeLayout();
            Debug.Log("Automatically visualized generated layout due to debugVisualizeLayoutOnGen=true");
        }

        if (verbose) Debug.Log("WorldLayoutLoader: Generated layout with " + structures.Count + " structures, " + nodes.Count + " nodes, and " + edges.Count + " edges.");
    }

    // ─────────────────────────────────────────────
    //  Structure placement — pure 2D, no height queries
    // ─────────────────────────────────────────────

    private List<StructureData> PlaceStructures(System.Random rng, float worldW, float worldH, float margin) {
        List<StructureData> placed = new List<StructureData>();

        foreach (string type in WorldLayoutLoader.StructureSizes.Keys) {
            int min = WorldLoadingController.GetParamInt($"layout/structures/{type}/min", 0);
            int max = WorldLoadingController.GetParamInt($"layout/structures/{type}/max", 0);
            if (max == 0){
                if(verbose) Debug.Log($"Skipping {type} placement since max=0");
                continue;
            }

            int count       = rng.Next(min, max + 1);
            int placedCount = 0;
            int attempts    = 0;

            while (placedCount < count && attempts < 200) {
                attempts++;

                Vector2 size   = StructureSizes[type];
                float   rot    = (float)rng.NextDouble() * 360f;
                Vector2 center = new Vector2(
                    margin + (float)rng.NextDouble() * (worldW - margin * 2f) - worldW * 0.5f,
                    margin + (float)rng.NextDouble() * (worldH - margin * 2f) - worldH * 0.5f
                );

                StructureData candidate = new StructureData {
                    type     = type,
                    center   = center,
                    size     = size,
                    rotation = rot
                };

                if (OverlapsAny(candidate, placed)) continue;

                placed.Add(candidate);
                placedCount++;
                
                if(verbose) Debug.Log($"Placed {type} #{placedCount}/{count} at {center} with size {size} and rotation {rot:F1}°. Attempts: {attempts}");
            }

            if (placedCount < min)
                Debug.LogWarning($"WorldLayoutLoader: could not place required {type} (min={min}, placed={placedCount})");
        }

        return placed;
    }

    // ─────────────────────────────────────────────
    //  Road network — pure 2D graph, no height queries
    // ─────────────────────────────────────────────

    private List<RoadNode> BuildRoadNodes(List<StructureData> structures) {
        List<RoadNode> nodes = new List<RoadNode>();
        int id = 0;
        foreach (var s in structures)
            nodes.Add(new RoadNode {
                id        = id++,
                position  = s.center,
                structure = s,
                edges     = new List<RoadEdge>()
            });
        return nodes;
    }

    private List<RoadEdge> BuildRoadNetwork(List<RoadNode> nodes, System.Random rng) {
        List<RoadEdge> edges = new List<RoadEdge>();
        if (nodes.Count < 2) return edges;

        // MST for guaranteed connectivity
        List<RoadEdge> mst = BuildMST(nodes);
        edges.AddRange(mst);

        // extra edges for loops (~30% of node count)
        int extraEdges = Mathf.Max(1, Mathf.RoundToInt(nodes.Count * 0.3f));
        List<(RoadNode, RoadNode)> candidates = GetNonMSTCandidates(nodes, mst);

        for (int i = 0; i < extraEdges && candidates.Count > 0; i++) {
            int idx    = rng.Next(candidates.Count);
            var (a, b) = candidates[idx];
            candidates.RemoveAt(idx);
            RoadEdge edge = CreateEdge(a, b);
            edges.Add(edge);
            a.edges.Add(edge);
            b.edges.Add(edge);
        }

        // mark crossroads
        foreach (var node in nodes)
            node.isCrossroad = node.edges.Count > 2;

        // waypoints are straight lines — height-following happens later in RoadMeshLoader
        foreach (var edge in edges)
            edge.waypoints = StraightPath(edge.from.position, edge.to.position);

        return edges;
    }

    private List<RoadEdge> BuildMST(List<RoadNode> nodes) {
        List<RoadEdge>   mst   = new List<RoadEdge>();
        HashSet<int>     inMST = new HashSet<int> { nodes[0].id };

        while (inMST.Count < nodes.Count) {
            float    bestDist = float.MaxValue;
            RoadNode bestA    = null;
            RoadNode bestB    = null;

            foreach (var a in nodes) {
                if (!inMST.Contains(a.id)) continue;
                foreach (var b in nodes) {
                    if (inMST.Contains(b.id)) continue;
                    float dist = Vector2.Distance(a.position, b.position);
                    if (dist < bestDist) {
                        bestDist = dist;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            if (bestA == null) break;
            RoadEdge edge = CreateEdge(bestA, bestB);
            mst.Add(edge);
            bestA.edges.Add(edge);
            bestB.edges.Add(edge);
            inMST.Add(bestB.id);
        }

        return mst;
    }

    private List<(RoadNode, RoadNode)> GetNonMSTCandidates(List<RoadNode> nodes, List<RoadEdge> mst) {
        HashSet<(int, int)> existing = new HashSet<(int, int)>();
        foreach (var e in mst) {
            existing.Add((e.from.id, e.to.id));
            existing.Add((e.to.id, e.from.id));
        }
        List<(RoadNode, RoadNode)> candidates = new List<(RoadNode, RoadNode)>();
        for (int i = 0; i < nodes.Count; i++)
        for (int j = i + 1; j < nodes.Count; j++)
            if (!existing.Contains((nodes[i].id, nodes[j].id)))
                candidates.Add((nodes[i], nodes[j]));
        return candidates;
    }

    private RoadEdge CreateEdge(RoadNode a, RoadNode b) {
        RoadEdge edge = new RoadEdge { from = a, to = b, width = 8f, waypoints = new List<Vector2>() };
        a.edges.Add(edge);
        b.edges.Add(edge);
        return edge;
    }

    // straight line subdivided into waypoints — RoadMeshLoader will project these onto terrain later
    private List<Vector2> StraightPath(Vector2 from, Vector2 to) {
        List<Vector2> waypoints = new List<Vector2>();
        int steps = Mathf.Max(2, Mathf.RoundToInt(Vector2.Distance(from, to) / 50f));
        for (int i = 0; i <= steps; i++)
            waypoints.Add(Vector2.Lerp(from, to, i / (float)steps));
        return waypoints;
    }

    // ─────────────────────────────────────────────
    //  OBB overlap
    // ─────────────────────────────────────────────

    private bool OverlapsAny(StructureData candidate, List<StructureData> placed) {
        foreach (var s in placed)
            if (OBBOverlap(candidate, s)) return true;
        return false;
    }

    private bool OBBOverlap(StructureData a, StructureData b) {
        Vector2[] axes  = new Vector2[] {
            GetAxis(a.rotation, 0), GetAxis(a.rotation, 1),
            GetAxis(b.rotation, 0), GetAxis(b.rotation, 1)
        };
        Vector2[] vertsA = GetVertices(a);
        Vector2[] vertsB = GetVertices(b);

        foreach (var axis in axes) {
            Project(vertsA, axis, out float minA, out float maxA);
            Project(vertsB, axis, out float minB, out float maxB);
            if (maxA < minB || maxB < minA) return false;
        }
        return true;
    }

    private Vector2 GetAxis(float rotDeg, int index) {
        float rad = rotDeg * Mathf.Deg2Rad;
        return index == 0
            ? new Vector2( Mathf.Cos(rad), Mathf.Sin(rad))
            : new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));
    }

    private Vector2[] GetVertices(StructureData s) {
        float hw = s.size.x * 0.5f, hh = s.size.y * 0.5f;
        Vector2[] local = { new Vector2(-hw,-hh), new Vector2(hw,-hh),
                            new Vector2( hw, hh), new Vector2(-hw, hh) };
        Vector2[] world = new Vector2[4];
        for (int i = 0; i < 4; i++)
            world[i] = s.center + Rotate(local[i], s.rotation);
        return world;
    }

    private void Project(Vector2[] verts, Vector2 axis, out float min, out float max) {
        min = max = Vector2.Dot(verts[0], axis);
        for (int i = 1; i < verts.Length; i++) {
            float p = Vector2.Dot(verts[i], axis);
            if (p < min) min = p;
            if (p > max) max = p;
        }
    }

    private Vector2 Rotate(Vector2 v, float deg) {
        float rad = deg * Mathf.Deg2Rad;
        return new Vector2(
            v.x * Mathf.Cos(rad) - v.y * Mathf.Sin(rad),
            v.x * Mathf.Sin(rad) + v.y * Mathf.Cos(rad)
        );
    }

    // ─────────────────────────────────────────────
    //  Structure size registry
    // ─────────────────────────────────────────────

    public static readonly Dictionary<string, Vector2> StructureSizes = new Dictionary<string, Vector2> {
        { "city",    new Vector2(300f, 300f) },
        { "village", new Vector2(100f, 100f) },
        { "orchard", new Vector2(150f,  80f) },
        { "farm",    new Vector2(200f, 120f) },
    };

    [Header("Debug")]
    public Material debugStructureMaterial;
    public Material debugRoadMaterial;
    public bool debugVisualizeLayoutOnGen = false;

    private GameObject _debugRoot;

    [ContextMenu("Visualize Layout")]
    private void Debug_VisualizeLayout() {
        WorldLayout layout = currentLayout;
        if (layout == null) { Debug.LogWarning("No layout generated yet"); return; }

        // clear previous debug visualization
        if (_debugRoot != null) Destroy(_debugRoot);
        _debugRoot = new GameObject("DEBUG_Layout");
        _debugRoot.transform.SetParent(transform);

        // structures — flat boxes showing OBB footprint
        foreach (var s in layout.structures) {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"DEBUG_{s.type}_{s.center.x:F0}_{s.center.y:F0}";
            go.transform.SetParent(_debugRoot.transform);
            go.transform.position    = new Vector3(s.center.x, 2f, s.center.y);
            go.transform.rotation    = Quaternion.Euler(0f, s.rotation, 0f);
            go.transform.localScale  = new Vector3(s.size.x, 0.5f, s.size.y);
            if (debugStructureMaterial != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = debugStructureMaterial;
            Destroy(go.GetComponent<Collider>());
        }

        // roads — small boxes along each edge's waypoints
        foreach (var edge in layout.roadEdges) {
            for (int i = 0; i < edge.waypoints.Count - 1; i++) {
                Vector2 a   = edge.waypoints[i];
                Vector2 b   = edge.waypoints[i + 1];
                Vector2 mid = (a + b) * 0.5f;
                float   len = Vector2.Distance(a, b);
                float   ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;

                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"DEBUG_road_{i}";
                go.transform.SetParent(_debugRoot.transform);
                go.transform.position   = new Vector3(mid.x, 1f, mid.y);
                go.transform.rotation   = Quaternion.Euler(0f, -ang, 0f);
                go.transform.localScale = new Vector3(len, 0.2f, edge.width);
                if (debugRoadMaterial != null)
                    go.GetComponent<MeshRenderer>().sharedMaterial = debugRoadMaterial;
                Destroy(go.GetComponent<Collider>());
            }
        }

        // road nodes — small vertical pillars, taller if crossroad
        foreach (var node in layout.roadNodes) {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = $"DEBUG_node_{node.id}{(node.isCrossroad ? "_CROSSROAD" : "")}";
            go.transform.SetParent(_debugRoot.transform);
            go.transform.position   = new Vector3(node.position.x, 5f, node.position.y);
            go.transform.localScale = new Vector3(5f, node.isCrossroad ? 5f : 2f, 5f);
            Destroy(go.GetComponent<Collider>());
        }

        Debug.Log($"Layout visualized: {layout.structures.Count} structures, {layout.roadEdges.Count} road edges");
    }

    [ContextMenu("Clear Layout Visualization")]
    private void Debug_ClearVisualization() {
        if (_debugRoot != null) Destroy(_debugRoot);
    }
}

public class StructureData {
    public string  type;
    public Vector2 center;
    public Vector2 size;
    public float   rotation;
}

public class RoadNode {
    public int           id;
    public Vector2       position;
    public StructureData structure;
    public bool          isCrossroad;
    public List<RoadEdge> edges;
}

public class RoadEdge {
    public RoadNode       from;
    public RoadNode       to;
    public float          width;
    public List<Vector2>  waypoints;
}

public class WorldLayout {
    public List<StructureData> structures;
    public List<RoadNode>      roadNodes;
    public List<RoadEdge>      roadEdges;
}