using System.Collections.Generic;
using UnityEngine;

// TagParent handles the base registration and 2D XZ boundary checks for roads/obstacles.
public class TagParent : MonoBehaviour
{
    // Simple structure to cache our flattened 2D triangles
    private struct Triangle2D
    {
        public Vector2 p0;
        public Vector2 p1;
        public Vector2 p2;
        public Vector2 min;
        public Vector2 max;

        public Triangle2D(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            this.p0 = p0;
            this.p1 = p1;
            this.p2 = p2;
            this.min = new Vector2(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)), Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)));
            this.max = new Vector2(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)), Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)));
        }
    }

    // Global master list accessible from any script
    public static List<TagParent> Registry = new List<TagParent>();

    // Broad-phase bounding box optimization
    public Rect XZBounds { get; private set; }
    private bool _XZBoundsCalculated = false;

    // List of pre-calculated world-space triangles for exact float testing
    private Triangle2D[] cachedTriangles = new Triangle2D[0];

    protected virtual void OnEnable()
    {
        Registry.Add(this);
    }

    protected virtual void OnDisable()
    {
        Registry.Remove(this);
    }

    private void Start()
    {
        if (!_XZBoundsCalculated)
            RecalculateProjectionData();
    }

    public bool Overlaps(Rect rect)
    {
        if (!_XZBoundsCalculated)
            RecalculateProjectionData();
        return XZBounds.Overlaps(rect);
    }


    /// <summary>
    /// Checks if a precise float XZ coordinate falls directly inside the road's true mesh shape.
    /// </summary>
    public bool IsInsideXZProjection(float x, float z)
    {
        if (!_XZBoundsCalculated)
            RecalculateProjectionData();

        Vector2 point = new Vector2(x, z);

        // 1. Broad-Phase Check: Instant rejection if it's outside the main bounding box
        if (!XZBounds.Contains(point))
        {
            return false;
        }

        // 2. Narrow-Phase Check: Test against the actual cached 2D triangles
        // Using a manual for-loop here to avoid garbage collection allocation
        for (int i = 0; i < cachedTriangles.Length; i++)
        {
            Triangle2D tri = cachedTriangles[i];
            
            // Fast rejection: check if point is inside the triangle's bounding box
            if (point.x < tri.min.x || point.x > tri.max.x || point.y < tri.min.y || point.y > tri.max.y)
                continue;

            if (IsPointIn2DTriangle(point, tri))
            {
                return true; // Point is inside the road!
            }
        }

        return false;
    }

    /// <summary>
    /// Chunk-level fast filtering: checks if a rectangular chunk overlaps ANY triangle in this mesh.
    /// Uses Separating Axis Theorem (SAT) against the triangle's edges.
    /// </summary>
    public bool IntersectsChunk(Rect chunkBounds)
    {
        if (!_XZBoundsCalculated)
            RecalculateProjectionData();

        if (!XZBounds.Overlaps(chunkBounds))
            return false;

        Vector2 c0 = new Vector2(chunkBounds.xMin, chunkBounds.yMin);
        Vector2 c1 = new Vector2(chunkBounds.xMax, chunkBounds.yMin);
        Vector2 c2 = new Vector2(chunkBounds.xMin, chunkBounds.yMax);
        Vector2 c3 = new Vector2(chunkBounds.xMax, chunkBounds.yMax);

        for (int i = 0; i < cachedTriangles.Length; i++)
        {
            Triangle2D tri = cachedTriangles[i];

            // 1. Separating axes from the chunk (AABB check)
            if (chunkBounds.xMin > tri.max.x || chunkBounds.xMax < tri.min.x ||
                chunkBounds.yMin > tri.max.y || chunkBounds.yMax < tri.min.y)
                continue;

            // 2. If any corner is inside the triangle, they intersect
            if (IsPointIn2DTriangle(c0, tri)) return true;
            if (IsPointIn2DTriangle(c1, tri)) return true;
            if (IsPointIn2DTriangle(c2, tri)) return true;
            if (IsPointIn2DTriangle(c3, tri)) return true;

            // 3. Separating axes from the triangle (Vertex Area signature check)
            float inSign1 = EdgeSideSign(tri.p2, tri.p0, tri.p1);
            float d1_0 = EdgeSideSign(c0, tri.p0, tri.p1);
            float d1_1 = EdgeSideSign(c1, tri.p0, tri.p1);
            float d1_2 = EdgeSideSign(c2, tri.p0, tri.p1);
            float d1_3 = EdgeSideSign(c3, tri.p0, tri.p1);
            bool out1_0 = (d1_0 > 0 && inSign1 < 0) || (d1_0 < 0 && inSign1 > 0);
            bool out1_1 = (d1_1 > 0 && inSign1 < 0) || (d1_1 < 0 && inSign1 > 0);
            bool out1_2 = (d1_2 > 0 && inSign1 < 0) || (d1_2 < 0 && inSign1 > 0);
            bool out1_3 = (d1_3 > 0 && inSign1 < 0) || (d1_3 < 0 && inSign1 > 0);
            if (out1_0 && out1_1 && out1_2 && out1_3) continue; // All corners strictly outside edge 1

            float inSign2 = EdgeSideSign(tri.p0, tri.p1, tri.p2);
            float d2_0 = EdgeSideSign(c0, tri.p1, tri.p2);
            float d2_1 = EdgeSideSign(c1, tri.p1, tri.p2);
            float d2_2 = EdgeSideSign(c2, tri.p1, tri.p2);
            float d2_3 = EdgeSideSign(c3, tri.p1, tri.p2);
            bool out2_0 = (d2_0 > 0 && inSign2 < 0) || (d2_0 < 0 && inSign2 > 0);
            bool out2_1 = (d2_1 > 0 && inSign2 < 0) || (d2_1 < 0 && inSign2 > 0);
            bool out2_2 = (d2_2 > 0 && inSign2 < 0) || (d2_2 < 0 && inSign2 > 0);
            bool out2_3 = (d2_3 > 0 && inSign2 < 0) || (d2_3 < 0 && inSign2 > 0);
            if (out2_0 && out2_1 && out2_2 && out2_3) continue; // All corners strictly outside edge 2

            float inSign3 = EdgeSideSign(tri.p1, tri.p2, tri.p0);
            float d3_0 = EdgeSideSign(c0, tri.p2, tri.p0);
            float d3_1 = EdgeSideSign(c1, tri.p2, tri.p0);
            float d3_2 = EdgeSideSign(c2, tri.p2, tri.p0);
            float d3_3 = EdgeSideSign(c3, tri.p2, tri.p0);
            bool out3_0 = (d3_0 > 0 && inSign3 < 0) || (d3_0 < 0 && inSign3 > 0);
            bool out3_1 = (d3_1 > 0 && inSign3 < 0) || (d3_1 < 0 && inSign3 > 0);
            bool out3_2 = (d3_2 > 0 && inSign3 < 0) || (d3_2 < 0 && inSign3 > 0);
            bool out3_3 = (d3_3 > 0 && inSign3 < 0) || (d3_3 < 0 && inSign3 > 0);
            if (out3_0 && out3_1 && out3_2 && out3_3) continue; // All corners strictly outside edge 3

            // If no separating axis exists, the chunk MUST intersect the triangle!
            return true;
        }

        return false;
    }

    /// <summary>
    /// Flattens the 3D meshes into 2D world-space triangles and calculates the broad bounds.
    /// </summary>
    [ContextMenu("Recalculate Projection Data")]
    public void RecalculateProjectionData()
    {
        _XZBoundsCalculated = true;
        List<Triangle2D> tempTriangles = new List<Triangle2D>();
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();

        if (meshFilters.Length == 0)
        {
            XZBounds = new Rect(transform.position.x, transform.position.z, 0, 0);
            cachedTriangles = new Triangle2D[0];
            return;
        }

        // --- PART 1: Calculate Broad-Phase Bounds ---
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        Bounds combined3DBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined3DBounds.Encapsulate(renderers[i].bounds);
        }
        XZBounds = new Rect(combined3DBounds.min.x, combined3DBounds.min.z, combined3DBounds.size.x, combined3DBounds.size.z);

        // --- PART 2: Cache World-Space 2D Triangles ---
        foreach (MeshFilter filter in meshFilters)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Transform meshTransform = filter.transform;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                // Convert 3D local vertices to 3D world coordinates
                Vector3 wV0 = meshTransform.TransformPoint(vertices[triangles[i]]);
                Vector3 wV1 = meshTransform.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 wV2 = meshTransform.TransformPoint(vertices[triangles[i + 2]]);

                // Flatten them onto the XZ plane (Mapping World Z to Vector2 Y)
                Vector2 p0 = new Vector2(wV0.x, wV0.z);
                Vector2 p1 = new Vector2(wV1.x, wV1.z);
                Vector2 p2 = new Vector2(wV2.x, wV2.z);

                tempTriangles.Add(new Triangle2D(p0, p1, p2));
            }
        }
        cachedTriangles = tempTriangles.ToArray();
    }

    // Mathematical sub-function: Point-in-Triangle barycentric/edge-side test
    private bool IsPointIn2DTriangle(Vector2 p, Triangle2D tri)
    {
        float d1 = EdgeSideSign(p, tri.p0, tri.p1);
        float d2 = EdgeSideSign(p, tri.p1, tri.p2);
        float d3 = EdgeSideSign(p, tri.p2, tri.p0);

        bool has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(has_neg && has_pos);
    }

    private float EdgeSideSign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    // Visual Gizmo Debugger: Draws the exact cached 2D triangles onto the ground
    private void OnDrawGizmosSelected()
    {
        // Outer Bounding Box (Cyan)
        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3(XZBounds.center.x, transform.position.y, XZBounds.center.y);
        Vector3 size = new Vector3(XZBounds.width, 0.1f, XZBounds.height);
        Gizmos.DrawWireCube(center, size);

        // Exact Triangles (Yellow lines)
        Gizmos.color = Color.yellow;
        float yHeight = transform.position.y + 0.05f; // Slightly elevated to prevent clipping terrain
        
        foreach (Triangle2D tri in cachedTriangles)
        {
            Vector3 v0 = new Vector3(tri.p0.x, yHeight, tri.p0.y);
            Vector3 v1 = new Vector3(tri.p1.x, yHeight, tri.p1.y);
            Vector3 v2 = new Vector3(tri.p2.x, yHeight, tri.p2.y);

            Gizmos.DrawLine(v0, v1);
            Gizmos.DrawLine(v1, v2);
            Gizmos.DrawLine(v2, v0);
        }
    }
}

public class TagParent<T> : TagParent where T : TagParent<T>
{
    public static new List<T> Registry = new List<T>();

    protected override void OnEnable()
    {
        base.OnEnable();
        Registry.Add((T)this);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Registry.Remove((T)this);
    }
}