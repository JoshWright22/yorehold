using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>Doors, levers, chests, what the dead left, the traps the party has found and lamp flames, drawn the way the C++ client drew them.</summary>
public partial class ObjectsView : Node2D
{
    private World? _world;
    private double _time;

    public void Bind(World world)
    {
        _world = world;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        // a few objects, and fog, doors and flames change all the time
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null)
        {
            return;
        }

        // the lamps' flames; their light is in the light map
        for (int i = 0; i < _world.Chapter.Map.Lights.Count; i++)
        {
            MapLight lamp = _world.Chapter.Map.Lights[i];
            if (!lamp.Flame)
            {
                continue;
            }
            var at = new Vector2((float)lamp.X, (float)lamp.Y) * GameMap.CellSize;
            // a square flame that jumps a pixel or two, flat like the tiles
            float flicker = Mathf.Round(2 * Mathf.Sin((float)_time * 11 + i * 1.7f));
            DrawRect(new Rect2(at.X - 6, at.Y - 8 - flicker, 12, 14 + flicker), Palette.Amber);
            DrawRect(new Rect2(at.X - 3, at.Y - 3 - flicker, 6, 8 + flicker), Palette.Straw);
            DrawRect(new Rect2(at.X - 6, at.Y + 6, 12, 3), Palette.Iron);
        }

        int team = _world.ViewTeam();
        foreach (WorldObject o in _world.Map.Objects)
        {
            if (o.Floor != 0 || o.Destroyed || (o.Trap != null && !o.TrapFound)
                || _world.Fog.State(team, 0, MapState.CellOf(o)) == FogState.Unexplored)
            {
                continue;
            }
            var area = new Rect2(o.Area.X, o.Area.Y, o.Area.W, o.Area.H);
            Vector2 centre = area.GetCenter();
            var inner = new Rect2(area.Position + new Vector2(4, 4), area.Size - new Vector2(8, 8));
            if (o.Has("container"))
            {
                Pile? inside = _world.Piles.Find(p => p.Object == o.Id);
                DrawChest(centre, inside == null || inside.Empty, o.Locked);
            }
            else if (o.HasDoor)
            {
                DrawDoor(inner, o.Open, o.Locked);
            }
            else if (o.Has("lever"))
            {
                DrawRect(new Rect2(centre.X - 10, centre.Y + 6, 20, 8), Palette.Iron);
                DrawRect(new Rect2(centre.X - 10, centre.Y + 6, 20, 2), Palette.Slate);
                DrawLine(new Vector2(centre.X, centre.Y + 8), new Vector2(centre.X + 10, centre.Y - 14), Palette.Leather, 4);
                DrawRect(new Rect2(centre.X + 7, centre.Y - 17, 6, 6), Palette.Red);
            }
            else if (o.Trap != null)
            {
                Color mark = o.TrapArmed ? Palette.Red : Palette.Smoke;
                float r = Mathf.Round(Mathf.Min(area.Size.X, area.Size.Y) * 0.3f);
                DrawLine(centre - new Vector2(r, r), centre + new Vector2(r, r), mark, 4);
                DrawLine(centre + new Vector2(-r, r), centre + new Vector2(r, -r), mark, 4);
            }
            else
            {
                var block = new Rect2(area.Position + new Vector2(8, 8), area.Size - new Vector2(16, 16));
                DrawRect(block, Palette.Slate);
                DrawRect(block, Palette.Ink, false, 2);
            }
        }
        DrawPiles(team);
    }

    // The chapter's containers are chests too, and what the dead left is a sack where they fell.
    private void DrawPiles(int team)
    {
        const float cell = GameMap.CellSize;
        foreach (Pile pile in _world!.Piles)
        {
            if (pile.Object != 0 || _world.Fog.State(team, 0, pile.At) == FogState.Unexplored || (pile.Container < 0 && pile.Empty))
            {
                continue;
            }
            var centre = new Vector2((pile.At.X + 0.5f) * cell, (pile.At.Y + 0.5f) * cell);
            if (pile.Container >= 0)
            {
                DrawChest(centre, pile.Empty, false);
                continue;
            }
            // in the square's corner, since the fallen token lies over the middle
            Vector2 sack = centre + new Vector2(cell * 0.3f, cell * 0.3f);
            DrawCircle(sack, cell * 0.15f, Palette.Rust);
            DrawArc(sack, cell * 0.15f, 0, Mathf.Tau, 20, Palette.Ink, 2);
            DrawLine(sack + new Vector2(-4, -cell * 0.15f), sack + new Vector2(4, -cell * 0.15f), Palette.Amber, 3);
        }
    }

    // planks across a shut door, only its frame once open, a brass lock while locked
    private void DrawDoor(Rect2 inner, bool open, bool locked)
    {
        if (open)
        {
            DrawRect(inner, Palette.Rust, false, 3);
            return;
        }
        DrawRect(inner, Palette.Leather);
        bool across = inner.Size.X >= inner.Size.Y;
        for (float at = 8; at < (across ? inner.Size.X : inner.Size.Y); at += 8)
        {
            if (across)
            {
                DrawRect(new Rect2(inner.Position.X + at, inner.Position.Y, 1, inner.Size.Y), Palette.Rust);
            }
            else
            {
                DrawRect(new Rect2(inner.Position.X, inner.Position.Y + at, inner.Size.X, 1), Palette.Rust);
            }
        }
        DrawRect(inner, Palette.Ink, false, 2);
        if (locked)
        {
            Vector2 c = inner.GetCenter();
            DrawRect(new Rect2(c.X - 4, c.Y - 4, 8, 8), Palette.Amber);
            DrawRect(new Rect2(c.X - 1, c.Y - 2, 2, 4), Palette.Ink);
        }
    }

    // dull once emptied, a brass lock under it while locked
    private void DrawChest(Vector2 centre, bool empty, bool locked)
    {
        const float cell = GameMap.CellSize;
        var box = new Rect2(Mathf.Round(centre.X - cell * 0.3f), Mathf.Round(centre.Y - cell * 0.2f), Mathf.Round(cell * 0.6f), Mathf.Round(cell * 0.42f));
        DrawRect(box, empty ? Palette.Iron : Palette.Leather);
        // the lid's edge, a third of the way down
        DrawRect(new Rect2(box.Position.X, box.Position.Y + Mathf.Round(box.Size.Y / 3), box.Size.X, 2), empty ? Palette.Ink : Palette.Rust);
        DrawRect(box, Palette.Ink, false, 2);
        DrawRect(new Rect2(centre.X - 4, centre.Y - 5, 8, 8), empty ? Palette.Slate : Palette.Straw);
        if (locked)
        {
            DrawRect(new Rect2(centre.X - 4, box.End.Y + 2, 8, 6), Palette.Amber);
        }
    }
}
