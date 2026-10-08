using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// What a hero's turn paints on the floor: the squares they can still walk to, the path a click
/// would take, and for a picked action its range and the area it would cover. It is drawn above
/// the lighting, or none of it would show in a dark room.
/// </summary>
public partial class FightGroundView : Node2D
{
    [Export] public Color ReachFill { get; set; } = Palette.Faded(Palette.Sky, 0.1f);
    [Export] public Color ReachEdge { get; set; } = Palette.Faded(Palette.Sky, 0.7f);
    [Export] public Color PathColor { get; set; } = Palette.Faded(Palette.Bone, 0.95f);
    [Export] public Color EnemyRange { get; set; } = Palette.Faded(Palette.Red, 0.85f);
    [Export] public Color AllyRange { get; set; } = Palette.Faded(Palette.Leaf, 0.85f);
    [Export] public Color AreaFill { get; set; } = Palette.Faded(Palette.Amber, 0.35f);
    [Export] public Color AreaBad { get; set; } = Palette.Faded(Palette.Red, 0.35f);

    private World? _world;
    private FightAim? _aim;

    public void Bind(World world, FightAim aim)
    {
        _world = world;
        _aim = aim;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null || _aim == null || !_aim.HeroTurn || _world.CurrentCreature is not int me)
        {
            return;
        }
        World w = _world;
        Cell standing = w.CellOf(me);
        ActionDefinition? action = _aim.Action.Length > 0 ? w.FindAction(_aim.Action) : null;
        if (action == null)
        {
            // while they walk, the reach is from where they are headed and would jump about
            if (w.Tokens.Tokens[me].Path.Count == 0)
            {
                Region(w.ReachableCells().Keys, ReachFill, ReachEdge);
            }
        }
        else if (action.Target != ActionTarget.Self)
        {
            Color edge = action.Side == ActionSide.Ally ? AllyRange : EnemyRange;
            var inRange = new List<Cell>();
            int range = Mathf.Max(1, w.RangeOf(me, action));
            for (int dy = -range; dy <= range; dy++)
            {
                for (int dx = -range; dx <= range; dx++)
                {
                    var c = new Cell(standing.X + dx, standing.Y + dy);
                    if (w.Map.Walkable(c) && w.Grid.Distance(standing, c) <= range + 0.01f)
                    {
                        inRange.Add(c);
                    }
                }
            }
            if (!(action.Area?.Directed ?? false))
            {
                Region(inRange, new Color(edge.R, edge.G, edge.B, 0.1f), edge);
            }
            if (action.Area != null && _aim.Hover is Cell hover)
            {
                // the squares the rules would cover, orange where it can go and red where it can't
                Color fill = _aim.AimBad ? AreaBad : AreaFill;
                Region(w.AreaCells(me, action, hover).Where(w.Map.Walkable), fill, new Color(fill.R, fill.G, fill.B, 0.9f));
                if (action.Target == ActionTarget.Point && !action.Area.Directed)
                {
                    DrawLine(w.Grid.Center(standing).ToGodot(), w.Grid.Center(hover).ToGodot(), new Color(fill.R, fill.G, fill.B, 0.9f), 2, true);
                }
            }
        }

        if (_aim.Path.Count > 1)
        {
            var points = new Vector2[_aim.Path.Count];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = w.Grid.Center(_aim.Path[i]).ToGodot();
            }
            DrawPolyline(points, Palette.Faded(Palette.Ink, 0.6f), 7, true);
            DrawPolyline(points, PathColor, 3, true);
            DrawCircle(points[^1], 9, Palette.Faded(Palette.Ink, 0.6f));
            DrawCircle(points[^1], 6, PathColor);
        }
    }

    // Tints the cells and draws a line along every side that has no neighbour in the set.
    private void Region(IEnumerable<Cell> cells, Color fill, Color edge)
    {
        const float s = GameMap.CellSize;
        var set = new HashSet<Cell>(cells);
        foreach (Cell c in set)
        {
            DrawRect(new Rect2(c.X * s, c.Y * s, s, s), fill);
        }
        foreach (Cell c in set)
        {
            float x = c.X * s;
            float y = c.Y * s;
            if (!set.Contains(new Cell(c.X, c.Y - 1)))
            {
                DrawLine(new Vector2(x, y), new Vector2(x + s, y), edge, 2);
            }
            if (!set.Contains(new Cell(c.X, c.Y + 1)))
            {
                DrawLine(new Vector2(x, y + s), new Vector2(x + s, y + s), edge, 2);
            }
            if (!set.Contains(new Cell(c.X - 1, c.Y)))
            {
                DrawLine(new Vector2(x, y), new Vector2(x, y + s), edge, 2);
            }
            if (!set.Contains(new Cell(c.X + 1, c.Y)))
            {
                DrawLine(new Vector2(x + s, y), new Vector2(x + s, y + s), edge, 2);
            }
        }
    }
}
