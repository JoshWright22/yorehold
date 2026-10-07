using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Doors, levers, chests, what the dead left, the traps the party has found and lamp flames. Each
/// shows the picture the content or an art pack gives it (objects/door.png, objects/door-open.png,
/// objects/chest.png...); without one it is a plain palette block, like a tile without a picture.
/// The game draws no object pictures of its own.
/// </summary>
public partial class ObjectsView : Node2D
{
    private World? _world;

    public void Bind(World world)
    {
        _world = world;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // doors open, chests empty and fog lifts all the time
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null)
        {
            return;
        }
        const float cell = GameMap.CellSize;

        // the lamps' flames; their light is in the light map
        foreach (MapLight lamp in _world.Chapter.Map.Lights)
        {
            if (lamp.Flame)
            {
                var at = new Vector2((float)lamp.X, (float)lamp.Y) * cell;
                Draw(new Rect2(at - new Vector2(cell * 0.2f, cell * 0.2f), new Vector2(cell * 0.4f, cell * 0.4f)), "flame", Palette.Amber, false);
            }
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
            if (o.Has("container"))
            {
                Pile? inside = _world.Piles.Find(p => p.Object == o.Id);
                DrawChest(area.GetCenter(), inside == null || inside.Empty, o.Locked);
            }
            else if (o.HasDoor)
            {
                var inner = new Rect2(area.Position + new Vector2(4, 4), area.Size - new Vector2(8, 8));
                if (o.Open)
                {
                    Draw(inner, "door-open", Palette.Rust, false, outline: true);
                }
                else
                {
                    Draw(inner, o.Locked ? "door-locked" : "door", Palette.Leather, o.Locked, fallback: "door");
                }
            }
            else if (o.Has("lever"))
            {
                Draw(Middle(area, 0.5f), "lever", Palette.Iron, false);
            }
            else if (o.Trap != null)
            {
                Draw(Middle(area, 0.6f), o.TrapArmed ? "trap" : "trap-off", o.TrapArmed ? Palette.Red : Palette.Smoke, false, outline: true, fallback: "trap");
            }
            else
            {
                Draw(Middle(area, 0.6f), "block", Palette.Slate, false);
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
            Draw(new Rect2(sack - new Vector2(cell * 0.16f, cell * 0.16f), new Vector2(cell * 0.32f, cell * 0.32f)), "sack", Palette.Rust, false);
        }
    }

    // dull once emptied, edged in brass while locked
    private void DrawChest(Vector2 centre, bool empty, bool locked)
    {
        const float cell = GameMap.CellSize;
        var box = new Rect2(centre - new Vector2(cell * 0.3f, cell * 0.3f), new Vector2(cell * 0.6f, cell * 0.6f));
        Draw(box, empty ? "chest-open" : "chest", empty ? Palette.Iron : Palette.Leather, locked, fallback: "chest");
    }

    private static Rect2 Middle(Rect2 area, float share)
    {
        float side = Mathf.Min(area.Size.X, area.Size.Y) * share;
        return new Rect2(area.GetCenter() - new Vector2(side, side) / 2, new Vector2(side, side));
    }

    // objects/<name>.png, else objects/<fallback>.png, else a flat block (or a frame) in the palette
    private void Draw(Rect2 where, string name, Color fill, bool locked, bool outline = false, string fallback = "")
    {
        where = new Rect2(where.Position.Round(), where.Size.Round());
        Texture2D? picture = PlayerArt.Texture(_world!.Files, $"objects/{name}.png");
        if (picture == null && fallback.Length > 0)
        {
            picture = PlayerArt.Texture(_world.Files, $"objects/{fallback}.png");
        }
        if (picture != null)
        {
            DrawTextureRect(picture, where, false);
        }
        else if (outline)
        {
            DrawRect(where, fill, false, 2);
        }
        else
        {
            DrawRect(where, fill);
            DrawRect(where, Palette.Ink, false, 1);
        }
        if (locked)
        {
            DrawRect(where, Palette.Amber, false, 2);
        }
    }
}
