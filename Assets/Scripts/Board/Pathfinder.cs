using System.Collections.Generic;
using UnityEngine;

public class Pathfinder
{
    private Board _board;

    public Pathfinder(Board board)
    {
        _board = board;
    }

    // Returns list of tiles to traverse, excluding start, including end
    // Returns null if no path found
    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int end)
    {
        if (!_board.IsInBounds(start) || !_board.IsInBounds(end)) return null;
        if (start == end) return new List<Vector2Int>();

        var openSet = new List<AStarNode>();
        var closedSet = new HashSet<Vector2Int>();
        var nodeMap = new Dictionary<Vector2Int, AStarNode>();

        var startNode = new AStarNode(start, null, 0, ManhattanDistance(start, end));
        openSet.Add(startNode);
        nodeMap[start] = startNode;

        while (openSet.Count > 0)
        {
            // Get node with lowest F cost
            var current = GetLowestFCost(openSet);
            openSet.Remove(current);
            closedSet.Add(current.Position);

            if (current.Position == end)
                return ReconstructPath(current);

            foreach (var neighbor in _board.GetNeighbors(current.Position))
            {
                var neighborPos = neighbor.GridPosition;

                if (closedSet.Contains(neighborPos)) continue;
                if (!neighbor.IsPassable) continue;
                if (neighbor.IsOccupied && neighborPos != end) continue;

                float newG = current.G + neighbor.MovementCost;

                if (nodeMap.TryGetValue(neighborPos, out var existingNode))
                {
                    if (newG < existingNode.G)
                    {
                        existingNode.G = newG;
                        existingNode.Parent = current;
                    }
                }
                else
                {
                    var newNode = new AStarNode(
                        neighborPos,
                        current,
                        newG,
                        ManhattanDistance(neighborPos, end));
                    openSet.Add(newNode);
                    nodeMap[neighborPos] = newNode;
                }
            }
        }

        return null; // No path found
    }

    // Returns all tiles reachable within a movement budget.
    // Dictionary value is the actual move cost to reach that tile (useful for subtracting from remaining points).
    public Dictionary<Vector2Int, float> GetReachableTiles(Vector2Int start, float movementPoints)
    {
        var costs    = new Dictionary<Vector2Int, float>(); // position -> cost to reach
        var frontier = new Dictionary<Vector2Int, float>(); // position -> best known cost
        frontier[start] = 0f;

        while (frontier.Count > 0)
        {
            Vector2Int current = GetLowestCost(frontier);
            float currentCost = frontier[current];
            frontier.Remove(current);

            if (costs.ContainsKey(current)) continue;
            costs[current] = currentCost;

            foreach (var neighbor in _board.GetNeighbors(current))
            {
                var neighborPos = neighbor.GridPosition;
                if (costs.ContainsKey(neighborPos)) continue;
                if (!neighbor.IsPassable) continue;
                if (neighbor.IsOccupied) continue;

                float newCost = currentCost + neighbor.MovementCost;
                if (newCost <= movementPoints)
                {
                    if (!frontier.ContainsKey(neighborPos) || frontier[neighborPos] > newCost)
                        frontier[neighborPos] = newCost;
                }
            }
        }

        costs.Remove(start); // Don't include starting tile
        return costs;
    }

    private AStarNode GetLowestFCost(List<AStarNode> nodes)
    {
        var lowest = nodes[0];
        foreach (var node in nodes)
            if (node.F < lowest.F)
                lowest = node;
        return lowest;
    }

    private Vector2Int GetLowestCost(Dictionary<Vector2Int, float> frontier)
    {
        Vector2Int lowest = default;
        float lowestCost = float.MaxValue;
        foreach (var kvp in frontier)
            if (kvp.Value < lowestCost)
            {
                lowestCost = kvp.Value;
                lowest = kvp.Key;
            }
        return lowest;
    }

    private List<Vector2Int> ReconstructPath(AStarNode endNode)
    {
        var path = new List<Vector2Int>();
        var current = endNode;
        while (current.Parent != null)
        {
            path.Add(current.Position);
            current = current.Parent;
        }
        path.Reverse();
        return path;
    }

    private float ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private class AStarNode
    {
        public Vector2Int Position;
        public AStarNode Parent;
        public float G; // Cost from start
        public float H; // Heuristic to end
        public float F => G + H;

        public AStarNode(Vector2Int pos, AStarNode parent, float g, float h)
        {
            Position = pos;
            Parent = parent;
            G = g;
            H = h;
        }
    }
}