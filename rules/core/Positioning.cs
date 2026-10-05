using System.Numerics;

namespace Yorehold.Rules;

public enum Cover
{
    None,
    Half,
    ThreeQuarters,
    Full,
}

/// <summary>Flanking and cover on a grid. What blocks a ray is the caller's to say: walls, bodies or both.</summary>
public static class Positioning
{
    /// <summary>
    /// Two foes within reach on exactly opposite sides of the target's centre. A foe whose ray to
    /// the target is blocked does not count.
    /// </summary>
    public static bool IsFlanked(Grid grid, Cell target, IReadOnlyList<Cell> foes, float reach = 1, Func<Vector2, Vector2, bool>? blocked = null)
    {
        Vector2 centre = grid.Center(target);
        for (int i = 0; i < foes.Count; i++)
        {
            if (foes[i] == target || !grid.InReach(foes[i], target, reach))
            {
                continue;
            }
            Vector2 a = grid.Center(foes[i]);
            if (blocked != null && blocked(a, centre))
            {
                continue;
            }
            for (int j = i + 1; j < foes.Count; j++)
            {
                if (foes[j] == target || !grid.InReach(foes[j], target, reach))
                {
                    continue;
                }
                Vector2 b = grid.Center(foes[j]);
                if (blocked != null && blocked(b, centre))
                {
                    continue;
                }
                Vector2 first = a - centre;
                Vector2 second = b - centre;
                float dot = first.X * second.X + first.Y * second.Y;
                float cross = first.X * second.Y - first.Y * second.X;
                if (dot < 0 && MathF.Abs(cross) <= 0.001f * MathF.Abs(dot))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Rays from the attacker's centre to corners set a little inside the target's cell: any
    /// blocked ray is half cover, three in four or more is three-quarters, all of them is full.
    /// </summary>
    public static Cover CoverBetween(Grid grid, Cell from, Cell target, Func<Vector2, Vector2, bool>? blocked)
    {
        if (blocked == null || from == target)
        {
            return Cover.None;
        }
        Vector2 start = grid.Center(from);
        Vector2 centre = grid.Center(target);
        float inset = grid.Size * 0.4f;
        Vector2[] corners;
        if (grid.Type == GridType.Hex)
        {
            float radius = grid.Size * 0.8f / MathF.Sqrt(3.0f);
            corners = new Vector2[]
            {
                new(0, -radius), new(inset, -radius / 2), new(inset, radius / 2),
                new(0, radius), new(-inset, radius / 2), new(-inset, -radius / 2),
            };
        }
        else
        {
            corners = new Vector2[] { new(-inset, -inset), new(inset, -inset), new(inset, inset), new(-inset, inset) };
        }
        int hits = corners.Count(offset => blocked(start, centre + offset));
        if (hits == 0)
        {
            return Cover.None;
        }
        if (hits == corners.Length)
        {
            return Cover.Full;
        }
        return hits * 4 >= corners.Length * 3 ? Cover.ThreeQuarters : Cover.Half;
    }

    /// <summary>
    /// Cover from walls, and failing that from the creatures standing in between. Bodies screen a
    /// shot but never make it impossible, so they give half cover at most.
    /// </summary>
    public static Cover CoverFrom(Grid grid, Cell from, Cell target, IReadOnlyList<Wall> walls, IEnumerable<Cell> bodies, PositioningRules rules)
    {
        if (!rules.Enabled)
        {
            return Cover.None;
        }
        Cover terrain = CoverBetween(grid, from, target, (a, b) => !Sight.LineOfSight(a, b, walls));
        if (terrain != Cover.None || !rules.CreaturesProvideCover)
        {
            return terrain;
        }
        var outlines = new List<Wall>();
        foreach (Cell body in bodies)
        {
            if (body == from || body == target)
            {
                continue;
            }
            // A body is a box half a cell wide in the middle of its cell.
            Vector2 centre = grid.Center(body);
            float radius = grid.Size * 0.25f;
            Vector2 nw = centre - new Vector2(radius, radius);
            Vector2 se = centre + new Vector2(radius, radius);
            Vector2 ne = new(se.X, nw.Y);
            Vector2 sw = new(nw.X, se.Y);
            outlines.Add(new Wall(nw, ne));
            outlines.Add(new Wall(ne, se));
            outlines.Add(new Wall(se, sw));
            outlines.Add(new Wall(sw, nw));
        }
        Cover screened = CoverBetween(grid, from, target, (a, b) => !Sight.LineOfSight(a, b, outlines));
        return screened == Cover.None ? Cover.None : Cover.Half;
    }
}
