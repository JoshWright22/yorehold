using System.Numerics;

namespace Yorehold.Rules;

/// <summary>
/// An action's area placed on the map, in world units: a burst or square sits on the aim, a cone
/// or line starts at the doer and points at it. A cell is covered when its centre is inside, so
/// what is drawn is what is hit. The C++ framework's AreaTemplate, for square grids.
/// </summary>
public sealed class AreaTemplate
{
    public AreaShape Shape { get; init; }
    public Vector2 Origin { get; init; }
    /// <summary>Any length; only the angle matters.</summary>
    public Vector2 Direction { get; init; } = new(1, 0);
    public float Size { get; init; }
    /// <summary>Lines only; 0 is one cell wide.</summary>
    public float Width { get; init; }
    public float AngleDegrees { get; init; } = 53.13f;

    /// <summary>The area of an action done from from and aimed at aim.</summary>
    public static AreaTemplate Place(ActionArea area, Grid grid, Vector2 from, Vector2 aim)
    {
        Vector2 toward = aim - from;
        return new AreaTemplate
        {
            Shape = area.Shape,
            Size = (float)area.Size * grid.Size,
            Width = (float)area.Width * grid.Size,
            AngleDegrees = (float)area.Angle,
            Origin = area.Directed ? from : aim,
            Direction = area.Directed && toward != Vector2.Zero ? toward : new Vector2(1, 0),
        };
    }

    public bool Contains(Vector2 point, float lineWidth)
    {
        Vector2 d = point - Origin;
        float slack = MathF.Max(Size, 1) * 1e-4f; // centres exactly on the edge count as inside
        switch (Shape)
        {
            case AreaShape.Burst:
                return d.Length() <= Size + slack;
            case AreaShape.Square:
                return MathF.Abs(d.X) <= Size / 2 + slack && MathF.Abs(d.Y) <= Size / 2 + slack;
            case AreaShape.Cone:
            {
                float distance = d.Length();
                if (distance <= slack)
                {
                    return true;
                }
                if (distance > Size + slack)
                {
                    return false;
                }
                Vector2 u = Unit(Direction);
                float cosine = Vector2.Dot(d, u) / distance;
                return cosine >= MathF.Cos(Math.Clamp(AngleDegrees, 0, 360) * MathF.PI / 360) - 1e-5f;
            }
            case AreaShape.Line:
            {
                Vector2 u = Unit(Direction);
                float along = Vector2.Dot(d, u);
                float across = MathF.Abs(d.X * u.Y - d.Y * u.X);
                return along >= -slack && along <= Size + slack && across <= lineWidth / 2 + slack;
            }
        }
        return false;
    }

    public List<Cell> Cells(Grid grid)
    {
        float lineWidth = Width > 0 ? Width : grid.Size;
        float reach = Shape == AreaShape.Square ? Size / 2 : Shape == AreaShape.Line ? Size + lineWidth : Size;
        Cell from = grid.CellAt(Origin - new Vector2(reach, reach));
        Cell to = grid.CellAt(Origin + new Vector2(reach, reach));
        var cells = new List<Cell>();
        for (int y = from.Y; y <= to.Y; y++)
        {
            for (int x = from.X; x <= to.X; x++)
            {
                var cell = new Cell(x, y);
                if (Contains(grid.Center(cell), lineWidth))
                {
                    cells.Add(cell);
                }
            }
        }
        return cells;
    }

    private static Vector2 Unit(Vector2 v)
    {
        float length = v.Length();
        return length > 0 ? v / length : new Vector2(1, 0);
    }
}
