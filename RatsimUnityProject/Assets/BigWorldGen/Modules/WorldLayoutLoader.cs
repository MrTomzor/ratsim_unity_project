using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class WorldLayoutLoader : WorldLoadingModule {

    public bool verbose = false;
    public static WorldLayoutLoader instance;
    public WorldLayout currentLayout = null;
    private bool _generated = false;

    [Header("Road Generation")]
    public int   perimeterSamples = 20;   // candidates per structure perimeter
    public float extraEdgeRatio   = 0.3f; // extra loop edges beyond MST

    private void Awake() {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

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

        float worldW = WorldLoadingController.GetParamFloat("world_bounds/width");
        float worldH = WorldLoadingController.GetParamFloat("world_bounds/height");
        float margin = WorldLoadingController.GetParamFloat("world_bounds/structures_margin", 100f);

        List<StructureData> structures = PlaceStructures(rng, worldW, worldH, margin);
        List<RoadEdge>      edges      = BuildRoadNetwork(structures);

        // collect all unique entry points and nodes from edges
        Dictionary<Vector2, RoadNode> nodeByPos = new Dictionary<Vector2, RoadNode>();
        int nextId = 0;
        RoadNode GetOrCreateNode(EntryPoint ep) {
            if (!nodeByPos.TryGetValue(ep.position, out RoadNode node)) {
                node = new RoadNode { id = nextId++, position = ep.position, entryPoint = ep, edges = new List<RoadEdge>() };
                nodeByPos[ep.position] = node;
            }
            return node;
        }

        List<EntryPoint> entryPoints = new List<EntryPoint>();
        foreach (var edge in edges) {
            if (!entryPoints.Contains(edge.fromEP)) entryPoints.Add(edge.fromEP);
            if (!entryPoints.Contains(edge.toEP))   entryPoints.Add(edge.toEP);
            edge.fromNode = GetOrCreateNode(edge.fromEP);
            edge.toNode   = GetOrCreateNode(edge.toEP);
            edge.fromNode.edges.Add(edge);
            edge.toNode.edges.Add(edge);
        }

        List<RoadNode> nodes = nodeByPos.Values.ToList();
        foreach (var node in nodes)
            node.isCrossroad = node.edges.Count > 2;

        currentLayout = new WorldLayout {
            structures  = structures,
            entryPoints = entryPoints,
            roadNodes   = nodes,
            roadEdges   = edges
        };

        if (debugVisualizeLayoutOnGen) Debug_VisualizeLayout();
        if (verbose) Debug.Log($"WorldLayoutLoader: {structures.Count} structures, {entryPoints.Count} entry points, {edges.Count} edges.");
    }

    // ─────────────────────────────────────────────
    //  Structure placement
    // ─────────────────────────────────────────────

    private List<StructureData> PlaceStructures(System.Random rng, float worldW, float worldH, float margin) {
        List<StructureData> placed = new List<StructureData>();

        foreach (string type in StructureSizes.Keys) {
            int min = WorldLoadingController.GetParamInt($"layout/structures/{type}/min", 0);
            int max = WorldLoadingController.GetParamInt($"layout/structures/{type}/max", 0);
            if (max == 0) continue;

            int count = rng.Next(min, max + 1);
            int placedCount = 0, attempts = 0;

            while (placedCount < count && attempts < 200) {
                attempts++;
                Vector2 size   = StructureSizes[type];
                float   rot    = (float)rng.NextDouble() * 360f;
                Vector2 center = new Vector2(
                    margin + (float)rng.NextDouble() * (worldW - margin * 2f) - worldW * 0.5f,
                    margin + (float)rng.NextDouble() * (worldH - margin * 2f) - worldH * 0.5f
                );
                StructureData candidate = new StructureData { type = type, center = center, size = size, rotation = rot };
                if (OverlapsAny(candidate, placed)) continue;
                placed.Add(candidate);
                placedCount++;
                if (verbose) Debug.Log($"Placed {type} at {center}, rot={rot:F1}°");
            }

            if (placedCount < min)
                Debug.LogWarning($"WorldLayoutLoader: could not place required {type} (min={min}, placed={placedCount})");
        }

        return placed;
    }

    // ─────────────────────────────────────────────
    //  Road network
    // ─────────────────────────────────────────────

    private List<RoadEdge> BuildRoadNetwork(List<StructureData> structures) {
        List<RoadEdge> edges = new List<RoadEdge>();
        if (structures.Count < 2) return edges;

        // union-find for connectivity tracking
        int[] parent = Enumerable.Range(0, structures.Count).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }
        bool Connected(int a, int b) => Find(a) == Find(b);
        bool AllConnected() => structures.Select((_, i) => Find(i)).Distinct().Count() == 1;

        // all structure pairs sorted by center distance
        List<(int iA, int iB, float dist)> pairs = new List<(int, int, float)>();
        for (int i = 0; i < structures.Count; i++)
        for (int j = i+1; j < structures.Count; j++)
            pairs.Add((i, j, Vector2.Distance(structures[i].center, structures[j].center)));
        pairs.Sort((a, b) => a.dist.CompareTo(b.dist));

        int extraEdges    = Mathf.Max(1, Mathf.RoundToInt(structures.Count * extraEdgeRatio));
        int extraAdded    = 0;
        bool mstComplete  = false;

        foreach (var (iA, iB, _) in pairs) {
            // once MST is complete, only add up to extraEdges more
            if (mstComplete && extraAdded >= extraEdges) break;
            // skip pairs that are already connected once MST is done (avoid redundant edges)
            if (mstComplete && Connected(iA, iB)) continue;

            StructureData sA = structures[iA];
            StructureData sB = structures[iB];

            RoadEdge edge = TryConnectStructures(sA, sB, structures);
            if (edge == null) {
                Debug.LogWarning($"WorldLayoutLoader: could not find unblocked path between {sA.type} and {sB.type}");
                continue;
            }

            edges.Add(edge);

            if (!Connected(iA, iB)) {
                Union(iA, iB);
                if (AllConnected()) mstComplete = true;
            } else {
                extraAdded++;
            }
        }

        // generate waypoints
        foreach (var edge in edges)
            edge.waypoints = StraightPath(edge.fromEP.position, edge.toEP.position);

        return edges;
    }

    // find shortest unblocked perimeter-to-perimeter connection between two structures
    private RoadEdge TryConnectStructures(StructureData sA, StructureData sB, List<StructureData> all) {
        Vector2[] vertsA = GetVertices(sA);
        Vector2[] vertsB = GetVertices(sB);
        float[]   elA    = EdgeLengths(vertsA);
        float[]   elB    = EdgeLengths(vertsB);
        float     totA   = elA.Sum();
        float     totB   = elB.Sum();

        float      bestDist = float.MaxValue;
        EntryPoint bestEpA  = null;
        EntryPoint bestEpB  = null;

        for (int i = 0; i < perimeterSamples; i++)
        for (int j = 0; j < perimeterSamples; j++) {
            float tA = (i + 0.5f) / perimeterSamples * totA;
            float tB = (j + 0.5f) / perimeterSamples * totB;

            Vector2 pA = SamplePerimeter(vertsA, elA, totA, tA, out Vector2 nA);
            Vector2 pB = SamplePerimeter(vertsB, elB, totB, tB, out Vector2 nB);

            float d = Vector2.Distance(pA, pB);
            if (d >= bestDist) continue;

            // check visibility — exclude sA and sB from blocking
            if (LineIntersectsAnyStructure(pA, pB, all, sA, sB)) continue;

            bestDist = d;
            bestEpA  = new EntryPoint { structure = sA, position = pA, outwardNormal = nA };
            bestEpB  = new EntryPoint { structure = sB, position = pB, outwardNormal = nB };
        }

        if (bestEpA == null) return null;

        return new RoadEdge {
            fromEP    = bestEpA,
            toEP      = bestEpB,
            width     = 8f,
            waypoints = new List<Vector2>()
        };
    }

    // ─────────────────────────────────────────────
    //  Perimeter sampling
    // ─────────────────────────────────────────────

    private float[] EdgeLengths(Vector2[] verts) {
        float[] el = new float[4];
        for (int i = 0; i < 4; i++)
            el[i] = Vector2.Distance(verts[i], verts[(i+1)%4]);
        return el;
    }

    private Vector2 SamplePerimeter(Vector2[] verts, float[] el, float total, float t, out Vector2 normal) {
        t = t % total;
        for (int i = 0; i < 4; i++) {
            if (t <= el[i]) {
                float   frac = t / el[i];
                Vector2 pos  = Vector2.Lerp(verts[i], verts[(i+1)%4], frac);
                Vector2 edge = (verts[(i+1)%4] - verts[i]) / el[i];
                normal = new Vector2(edge.y, -edge.x);
                return pos;
            }
            t -= el[i];
        }
        normal = Vector2.up;
        return verts[0];
    }

    // ─────────────────────────────────────────────
    //  Visibility
    // ─────────────────────────────────────────────

    private bool LineIntersectsAnyStructure(Vector2 a, Vector2 b,
            List<StructureData> structures, StructureData ownerA, StructureData ownerB) {
        foreach (var s in structures) {
            if (s == ownerA || s == ownerB) continue;
            if (LineIntersectsOBB(a, b, s)) return true;
        }
        return false;
    }

    private bool LineIntersectsOBB(Vector2 a, Vector2 b, StructureData s) {
        Vector2 localA   = WorldToOBBLocal(a, s);
        Vector2 localB   = WorldToOBBLocal(b, s);
        Vector2 halfSize = s.size * 0.5f;
        return SegmentIntersectsAABB(localA, localB, -halfSize, halfSize);
    }

    private Vector2 WorldToOBBLocal(Vector2 p, StructureData s) {
        Vector2 delta = p - s.center;
        float   rad   = -s.rotation * Mathf.Deg2Rad;
        return new Vector2(
            delta.x * Mathf.Cos(rad) - delta.y * Mathf.Sin(rad),
            delta.x * Mathf.Sin(rad) + delta.y * Mathf.Cos(rad)
        );
    }

    private bool SegmentIntersectsAABB(Vector2 a, Vector2 b, Vector2 min, Vector2 max) {
        Vector2 d    = b - a;
        float   tMin = 0f, tMax = 1f;

        for (int axis = 0; axis < 2; axis++) {
            float da   = axis == 0 ? d.x   : d.y;
            float aVal = axis == 0 ? a.x   : a.y;
            float minV = axis == 0 ? min.x : min.y;
            float maxV = axis == 0 ? max.x : max.y;

            if (Mathf.Abs(da) < 1e-6f) {
                if (aVal < minV || aVal > maxV) return false;
            } else {
                float t1 = (minV - aVal) / da;
                float t2 = (maxV - aVal) / da;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
        }
        return true;
    }

    // ─────────────────────────────────────────────
    //  OBB geometry
    // ─────────────────────────────────────────────

    private bool OverlapsAny(StructureData candidate, List<StructureData> placed) {
        foreach (var s in placed) if (OBBOverlap(candidate, s)) return true;
        return false;
    }

    private bool OBBOverlap(StructureData a, StructureData b) {
        Vector2[] axes = { GetAxis(a.rotation,0), GetAxis(a.rotation,1),
                           GetAxis(b.rotation,0), GetAxis(b.rotation,1) };
        Vector2[] vA = GetVertices(a);
        Vector2[] vB = GetVertices(b);
        foreach (var axis in axes) {
            Project(vA, axis, out float minA, out float maxA);
            Project(vB, axis, out float minB, out float maxB);
            if (maxA < minB || maxB < minA) return false;
        }
        return true;
    }

    private Vector2 GetAxis(float rotDeg, int index) {
        float rad = rotDeg * Mathf.Deg2Rad;
        return index == 0 ? new Vector2( Mathf.Cos(rad), Mathf.Sin(rad))
                          : new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));
    }

    public Vector2[] GetVertices(StructureData s) {
        float hw = s.size.x * 0.5f, hh = s.size.y * 0.5f;
        Vector2[] local = { new Vector2(-hw,-hh), new Vector2(hw,-hh),
                            new Vector2( hw, hh), new Vector2(-hw, hh) };
        Vector2[] world = new Vector2[4];
        for (int i = 0; i < 4; i++) world[i] = s.center + Rotate(local[i], s.rotation);
        return world;
    }

    private void Project(Vector2[] verts, Vector2 axis, out float min, out float max) {
        min = max = Vector2.Dot(verts[0], axis);
        for (int i = 1; i < verts.Length; i++) {
            float p = Vector2.Dot(verts[i], axis);
            if (p < min) min = p; if (p > max) max = p;
        }
    }

    private Vector2 Rotate(Vector2 v, float deg) {
        float rad = deg * Mathf.Deg2Rad;
        return new Vector2(v.x*Mathf.Cos(rad)-v.y*Mathf.Sin(rad),
                           v.x*Mathf.Sin(rad)+v.y*Mathf.Cos(rad));
    }

    private List<Vector2> StraightPath(Vector2 from, Vector2 to) {
        List<Vector2> pts   = new List<Vector2>();
        int           steps = Mathf.Max(2, Mathf.RoundToInt(Vector2.Distance(from, to) / 50f));
        for (int i = 0; i <= steps; i++)
            pts.Add(Vector2.Lerp(from, to, i / (float)steps));
        return pts;
    }

    // ─────────────────────────────────────────────
    //  Registries
    // ─────────────────────────────────────────────

    public static readonly Dictionary<string, Vector2> StructureSizes = new Dictionary<string, Vector2> {
        { "city",    new Vector2(300f, 300f) },
        { "village", new Vector2(100f, 100f) },
        { "orchard", new Vector2(150f,  80f) },
        { "farm",    new Vector2(200f, 120f) },
    };

    // ─────────────────────────────────────────────
    //  Debug
    // ─────────────────────────────────────────────

    [Header("Debug")]
    public Material debugStructureMaterial;
    public Material debugRoadMaterial;
    public bool     debugVisualizeLayoutOnGen = false;
    private GameObject _debugRoot;

    [ContextMenu("Visualize Layout")]
    public void Debug_VisualizeLayout() {
        if (currentLayout == null) { Debug.LogWarning("No layout generated yet"); return; }
        if (_debugRoot != null) Destroy(_debugRoot);
        _debugRoot = new GameObject("DEBUG_Layout");
        _debugRoot.transform.SetParent(transform);

        float debugY  = 2f;
        float epSize  = WorldLoadingController.GetParamFloat("world_bounds/width", 1000f) * 0.01f;

        foreach (var s in currentLayout.structures) {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"DEBUG_{s.type}";
            go.transform.SetParent(_debugRoot.transform);
            go.transform.position   = new Vector3(s.center.x, debugY, s.center.y);
            go.transform.rotation   = Quaternion.Euler(0f, -s.rotation, 0f);
            go.transform.localScale = new Vector3(s.size.x, 1f, s.size.y);
            if (debugStructureMaterial != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = debugStructureMaterial;
            Destroy(go.GetComponent<Collider>());
        }

        foreach (var ep in currentLayout.entryPoints) {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"DEBUG_ep_{ep.structure.type}";
            go.transform.SetParent(_debugRoot.transform);
            go.transform.position   = new Vector3(ep.position.x, debugY + 2f, ep.position.y);
            go.transform.localScale = Vector3.one * epSize;
            Destroy(go.GetComponent<Collider>());

            // outward normal indicator
            Vector2    tip = ep.position + ep.outwardNormal * epSize * 2f;
            Vector2    mid = (ep.position + tip) * 0.5f;
            GameObject arr = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arr.transform.SetParent(_debugRoot.transform);
            arr.transform.position   = new Vector3(mid.x, debugY + 2f, mid.y);
            arr.transform.localScale = new Vector3(epSize * 0.3f, epSize, epSize * 0.3f);
            float ang = Mathf.Atan2(ep.outwardNormal.x, ep.outwardNormal.y) * Mathf.Rad2Deg;
            arr.transform.rotation = Quaternion.Euler(90f, ang, 0f);
            Destroy(arr.GetComponent<Collider>());
        }

        foreach (var edge in currentLayout.roadEdges) {
            for (int i = 0; i < edge.waypoints.Count - 1; i++) {
                Vector2 a = edge.waypoints[i], b = edge.waypoints[i+1];
                Vector2 mid = (a + b) * 0.5f;
                float   len = Vector2.Distance(a, b);
                float   ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"DEBUG_road_{i}";
                go.transform.SetParent(_debugRoot.transform);
                go.transform.position   = new Vector3(mid.x, debugY + 0.5f, mid.y);
                go.transform.rotation   = Quaternion.Euler(0f, -ang, 0f);
                go.transform.localScale = new Vector3(len, 0.5f, edge.width);
                if (debugRoadMaterial != null)
                    go.GetComponent<MeshRenderer>().sharedMaterial = debugRoadMaterial;
                Destroy(go.GetComponent<Collider>());
            }
        }

        foreach (var node in currentLayout.roadNodes) {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = $"DEBUG_node_{node.id}{(node.isCrossroad ? "_X" : "")}";
            go.transform.SetParent(_debugRoot.transform);
            go.transform.position   = new Vector3(node.position.x, debugY + 4f, node.position.y);
            go.transform.localScale = new Vector3(epSize * 0.8f, node.isCrossroad ? epSize : epSize * 0.5f, epSize * 0.8f);
            Destroy(go.GetComponent<Collider>());
        }

        Debug.Log($"Layout: {currentLayout.structures.Count} structures, " +
                  $"{currentLayout.entryPoints.Count} entry points, " +
                  $"{currentLayout.roadEdges.Count} edges");
    }

    [ContextMenu("Clear Layout Visualization")]
    private void Debug_ClearVisualization() {
        if (_debugRoot != null) Destroy(_debugRoot);
    }
}

// ─────────────────────────────────────────────
//  Data classes
// ─────────────────────────────────────────────

public class EntryPoint {
    public StructureData structure;
    public Vector2       position;
    public Vector2       outwardNormal;
}

public class StructureData {
    public string  type;
    public Vector2 center;
    public Vector2 size;
    public float   rotation;
}

public class RoadNode {
    public int            id;
    public Vector2        position;
    public EntryPoint     entryPoint;
    public bool           isCrossroad;
    public List<RoadEdge> edges;
}

public class RoadEdge {
    public EntryPoint    fromEP;
    public EntryPoint    toEP;
    public RoadNode      fromNode;
    public RoadNode      toNode;
    public float         width;
    public List<Vector2> waypoints;
}

public class WorldLayout {
    public List<StructureData> structures;
    public List<EntryPoint>    entryPoints;
    public List<RoadNode>      roadNodes;
    public List<RoadEdge>      roadEdges;
}