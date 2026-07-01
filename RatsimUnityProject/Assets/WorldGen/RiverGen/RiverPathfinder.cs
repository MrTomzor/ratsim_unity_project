using UnityEngine;
using System.Collections.Generic;
using ClipmapTerrain;

public static class RiverPathfinder
{
    private class Node
    {
        public Vector2 Position;
        public float G; // Cost from start
        public float F; // G + Heuristic
        public Node Parent;
        public float Height;

        public Node(Vector2 pos)
        {
            Position = pos;
            Height = TerrainNoise.GetTerrainHeightOriginal(new Vector2(pos.x, pos.y));
        }
    }

    public static List<Vector2> FindPath(Vector2 start, Vector2 end, float stepSize = 1f, float uphillPenalty = 10f)
    {
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        List<Node> openSet = new List<Node>();
        HashSet<Vector2> closedSet = new HashSet<Vector2>();

        Node startNode = new Node(start);
        startNode.G = 0;
        startNode.F = Vector2.Distance(start, end);
        openSet.Add(startNode);

        Vector2 AB = end - start;
        
        // 8 directions for neighbors
        Vector2[] directions = {
            new Vector2(stepSize, 0), new Vector2(-stepSize, 0),
            new Vector2(0, stepSize), new Vector2(0, -stepSize),
            //new Vector2(stepSize, stepSize), new Vector2(-stepSize, stepSize),
            //new Vector2(stepSize, -stepSize), new Vector2(-stepSize, -stepSize)
        };

        int maxIterations = 100000;
        int iterations = 0;

        while (openSet.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            // Get lowest F cost node.
            int bestIndex = 0;
            float bestF = openSet[0].F;
            for (int i = 1; i < openSet.Count; i++)
            {
                if (openSet[i].F < bestF)
                {
                    bestF = openSet[i].F;
                    bestIndex = i;
                }
            }

            Node current = openSet[bestIndex];
            openSet.RemoveAt(bestIndex);

            // Check if we are close enough to the end
            if (Vector2.Distance(current.Position, end) <= stepSize * 1.5f)
            {
                sw.Stop();
                List<Vector2> path = RetracePath(startNode, current, end);
                Debug.Log($"[RiverPathfinder] Path found in {sw.ElapsedMilliseconds}ms. Iterations: {iterations}, Explored Nodes: {closedSet.Count}, Final Path Nodes: {path.Count}");
                return path;
            }

            closedSet.Add(current.Position);

            foreach (Vector2 dir in directions)
            {
                Vector2 neighborPos = current.Position + dir;
                
                // Snap to step size grid to prevent floating point drift
                neighborPos.x = Mathf.Round(neighborPos.x / stepSize) * stepSize;
                neighborPos.y = Mathf.Round(neighborPos.y / stepSize) * stepSize;

                if (closedSet.Contains(neighborPos)) continue;

                // Constraint: Not before A, not after B
                // A node N is valid if Dot(AB, AN) >= 0 AND Dot(AB, BN) <= 0
                Vector2 AN = neighborPos - start;
                Vector2 BN = neighborPos - end;
                if (Vector2.Dot(AB, AN) < 0 || Vector2.Dot(AB, BN) > 0)
                {
                    continue; // Skip this neighbor, it's outside the infinite orthogonal slab
                }

                Node neighbor = new Node(neighborPos);

                float dist = Vector2.Distance(current.Position, neighborPos);
                float heightDiff = neighbor.Height - current.Height;
                
                float moveCost = dist;
                if (heightDiff > 0)
                {
                    moveCost += heightDiff * uphillPenalty;
                }

                float tentativeG = current.G + moveCost;

                Node existingOpen = openSet.Find(n => n.Position == neighborPos);
                if (existingOpen == null)
                {
                    neighbor.G = tentativeG;
                    // Heuristic: distance to end
                    neighbor.F = tentativeG + Vector2.Distance(neighborPos, end);
                    neighbor.Parent = current;
                    openSet.Add(neighbor);
                }
                else if (tentativeG < existingOpen.G)
                {
                    existingOpen.G = tentativeG;
                    existingOpen.F = tentativeG + Vector2.Distance(neighborPos, end);
                    existingOpen.Parent = current;
                }
            }
        }

        sw.Stop();
        Debug.LogWarning($"[RiverPathfinder] FAILED. Reached max iterations or exhausted nodes. Time: {sw.ElapsedMilliseconds}ms. Iterations: {iterations}, Explored Nodes: {closedSet.Count}");
        return null;
    }

    private static List<Vector2> RetracePath(Node startNode, Node endNode, Vector2 endTarget)
    {
        List<Vector2> path = new List<Vector2>();
        Node current = endNode;
        while (current != startNode && current != null)
        {
            path.Add(current.Position);
            current = current.Parent;
        }
        path.Add(startNode.Position);
        path.Reverse();
        
        // Ensure exact target is at the end
        if (Vector2.Distance(path[path.Count-1], endTarget) > 0.01f)
            path.Add(endTarget);
            
        return path;
    }
}
