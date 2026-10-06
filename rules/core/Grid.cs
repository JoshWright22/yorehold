using System.Numerics;

namespace Yorehold.Rules;

public enum GridType
{
    Square,
    /// <summary>Pointy-top hexes; a cell is axial (q, r).</summary>
    Hex,
    /// <summary>Free movement; cells are still squares, for paths.</summary>
    Gridless,
}

/// <summary>How diagonal steps count on a square grid.</summary>
public enum DiagonalRule
{
    /// <summary>Every diagonal costs 1.</summary>
    Equal,
    /// <summary>1, 2, 1, 2... (the 5-10-5 rule).</summary>
    Alternating,
    /// <summary>About 1.41, the true distance.</summary>
    Euclidean,
    /// <summary>Every diagonal costs 2.</summary>
    Double,
}

/// <summary>
/// Cells and the distances between them, whatever the grid's shape. Positions are world units;
/// Size is the distance between the centres of two neighbouring cells. Float sums like the C++
/// client's, so a distance that is just in reach there is just in reach here.
/// </summary>
public sealed class Grid
{
    private const float Sqrt3 = 1.7320508f;
    private const float Diagonal = 1.41421356f;
    private static readonly Cell[] HexDirections = { new(1, 0), new(1, -1), new(0, -1), new(-1, 0), new(-1, 1), new(0, 1) };

    public Grid(GridType type, float size)
    {
        Type = type;
        Size = size;
    }

    public GridType Type { get; }
    public float Size { get; }
    public DiagonalRule Diagonals { get; set; } = DiagonalRule.Equal;

    public Cell CellAt(Vector2 world)
    {
        if (Type == GridType.Hex)
        {
            float r = world.Y / (Size * Sqrt3 / 2.0f);
            float q = world.X / Size - r / 2.0f;
            return RoundHex(q, r);
        }
        return new Cell((int)MathF.Floor(world.X / Size), (int)MathF.Floor(world.Y / Size));
    }

    public Vector2 Center(Cell cell)
    {
        if (Type == GridType.Hex)
        {
            return new Vector2(Size * (cell.X + cell.Y / 2.0f), Size * Sqrt3 / 2.0f * cell.Y);
        }
        return new Vector2((cell.X + 0.5f) * Size, (cell.Y + 0.5f) * Size);
    }

    /// <summary>Where a dropped thing lands. Gridless maps don't snap.</summary>
    public Vector2 Snap(Vector2 world)
    {
        return Type == GridType.Gridless ? world : Center(CellAt(world));
    }

    public List<Cell> Neighbours(Cell cell)
    {
        var cells = new List<Cell>(8);
        if (Type == GridType.Hex)
        {
            foreach (Cell direction in HexDirections)
            {
                cells.Add(new Cell(cell.X + direction.X, cell.Y + direction.Y));
            }
            return cells;
        }
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx != 0 || dy != 0)
                {
                    cells.Add(new Cell(cell.X + dx, cell.Y + dy));
                }
            }
        }
        return cells;
    }

    /// <summary>Movement distance in cells, by the grid's rules.</summary>
    public float Distance(Cell a, Cell b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        if (Type == GridType.Hex)
        {
            return (dx + dy + Math.Abs(a.X + a.Y - b.X - b.Y)) / 2.0f;
        }
        if (Type == GridType.Gridless)
        {
            return MathF.Sqrt(dx * dx + dy * dy);
        }
        int diagonal = Math.Min(dx, dy);
        int straight = Math.Max(dx, dy) - diagonal;
        return Diagonals switch
        {
            DiagonalRule.Alternating => straight + diagonal + diagonal / 2,
            DiagonalRule.Euclidean => straight + diagonal * Diagonal,
            DiagonalRule.Double => straight + diagonal * 2,
            _ => straight + diagonal,
        };
    }

    /// <summary>Cost of one step to a neighbour; a diagonal can cost more.</summary>
    public float StepCost(Cell from, Cell to, int diagonalsSoFar)
    {
        bool diagonal = Type != GridType.Hex && from.X != to.X && from.Y != to.Y;
        if (!diagonal)
        {
            return 1.0f;
        }
        if (Type == GridType.Gridless)
        {
            return Diagonal;
        }
        return Diagonals switch
        {
            DiagonalRule.Alternating => diagonalsSoFar % 2 == 1 ? 2.0f : 1.0f, // every second diagonal costs double
            DiagonalRule.Euclidean => Diagonal,
            DiagonalRule.Double => 2.0f,
            _ => 1.0f,
        };
    }

    /// <summary>
    /// Whether b is within so many cells of a. The slack is the one the C++ client uses for reach
    /// and range, so a Euclidean diagonal that adds up a hair over still counts.
    /// </summary>
    public bool InReach(Cell a, Cell b, float reach)
    {
        return Distance(a, b) <= reach + 0.01f;
    }

    // Rounds fractional axial hex coordinates to the nearest hex, by way of cube coordinates.
    private static Cell RoundHex(float q, float r)
    {
        float s = -q - r;
        float rq = MathF.Round(q, MidpointRounding.AwayFromZero);
        float rr = MathF.Round(r, MidpointRounding.AwayFromZero);
        float rs = MathF.Round(s, MidpointRounding.AwayFromZero);
        float dq = MathF.Abs(rq - q);
        float dr = MathF.Abs(rr - r);
        float ds = MathF.Abs(rs - s);
        if (dq > dr && dq > ds)
        {
            rq = -rr - rs;
        }
        else if (dr > ds)
        {
            rr = -rq - rs;
        }
        return new Cell((int)rq, (int)rr);
    }
}
