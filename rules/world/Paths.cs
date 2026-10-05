namespace Yorehold.Rules;

/// <summary>A* over any grid, step for step the C++ client's findPath.</summary>
public static class Paths
{
    private readonly record struct Node(float Cost, int Diagonals);

    private readonly record struct Open(float Priority, float Cost, Cell Cell);

    /// <summary>
    /// The cells from start to goal, both included, or an empty list if there is no way there
    /// within maxVisited cells (so a click on an unreachable spot doesn't stall a frame).
    /// </summary>
    public static List<Cell> Find(Grid grid, Cell start, Cell goal, Func<Cell, bool> passable, int maxVisited = 50000)
    {
        if (start == goal)
        {
            return new List<Cell> { start };
        }
        if (!passable(goal))
        {
            return new List<Cell>();
        }

        // Many routes on a grid cost the same. Nudging the priority by how far a cell strays from
        // the straight line picks the route a person would draw instead of a zig-zag.
        float lineX = goal.X - start.X;
        float lineY = goal.Y - start.Y;
        float lineLength = MathF.Max(1.0f, MathF.Sqrt(lineX * lineX + lineY * lineY));
        float Straying(Cell c)
        {
            float cx = c.X - start.X;
            float cy = c.Y - start.Y;
            return MathF.Abs(cx * lineY - cy * lineX) / lineLength;
        }

        var open = new HeapQueue<Open>((a, b) => a.Priority > b.Priority);
        var best = new Dictionary<Cell, Node> { [start] = new Node(0, 0) };
        var cameFrom = new Dictionary<Cell, Cell>();
        open.Push(new Open(grid.Distance(start, goal), 0, start));

        int visited = 0;
        bool found = false;
        while (open.Count > 0 && visited < maxVisited)
        {
            Open entry = open.Pop();
            Node node = best[entry.Cell];
            if (entry.Cost > node.Cost + 1e-4f)
            {
                continue; // a cheaper way here turned up after this entry was queued
            }
            if (entry.Cell == goal)
            {
                found = true;
                break;
            }
            visited++;

            Cell current = entry.Cell;
            foreach (Cell next in grid.Neighbours(current))
            {
                if (!passable(next))
                {
                    continue;
                }
                bool diagonal = grid.Type != GridType.Hex && next.X != current.X && next.Y != current.Y;
                // No cutting corners past walls or creatures.
                if (diagonal && (!passable(new Cell(next.X, current.Y)) || !passable(new Cell(current.X, next.Y))))
                {
                    continue;
                }
                float cost = node.Cost + grid.StepCost(current, next, node.Diagonals);
                if (best.TryGetValue(next, out Node known) && known.Cost <= cost)
                {
                    continue;
                }
                best[next] = new Node(cost, node.Diagonals + (diagonal ? 1 : 0));
                cameFrom[next] = current;
                open.Push(new Open(cost + grid.Distance(next, goal) + Straying(next) * 0.001f, cost, next));
            }
        }

        if (!found)
        {
            return new List<Cell>();
        }
        var path = new List<Cell> { goal };
        for (Cell at = goal; at != start;)
        {
            at = cameFrom[at];
            path.Add(at);
        }
        path.Reverse();
        return path;
    }
}
