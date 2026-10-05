using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>Doors, levers, chests, the traps the party has found and lamp flames, drawn the way the C++ client drew them.</summary>
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
            float flicker = 1 + 0.15f * Mathf.Sin((float)_time * 11 + i * 1.7f);
            DrawCircle(at, 10 * flicker, Color.Color8(255, 140, 50));
            DrawCircle(at, 5 * flicker, Color.Color8(255, 235, 170));
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
                DrawChest(o, centre);
            }
            else if (o.HasDoor)
            {
                if (o.Open)
                {
                    DrawRect(inner, Color.Color8(110, 75, 40), false, 3);
                }
                else
                {
                    DrawRect(inner, Color.Color8(120, 80, 42));
                    DrawRect(inner, Color.Color8(45, 28, 14), false, 3);
                }
                if (o.Locked)
                {
                    DrawCircle(centre, 6, Color.Color8(225, 195, 80));
                }
            }
            else if (o.Has("lever"))
            {
                DrawRect(new Rect2(centre.X - 10, centre.Y + 6, 20, 8), Color.Color8(70, 70, 76));
                DrawLine(new Vector2(centre.X, centre.Y + 8), new Vector2(centre.X + 10, centre.Y - 14), Color.Color8(150, 120, 80), 4);
            }
            else if (o.Trap != null)
            {
                Color mark = o.TrapArmed ? Color.Color8(220, 60, 50) : Color.Color8(110, 100, 95);
                float r = Mathf.Min(area.Size.X, area.Size.Y) * 0.3f;
                DrawLine(centre - new Vector2(r, r), centre + new Vector2(r, r), mark, 4);
                DrawLine(centre + new Vector2(-r, r), centre + new Vector2(r, -r), mark, 4);
            }
            else
            {
                DrawRect(new Rect2(area.Position + new Vector2(8, 8), area.Size - new Vector2(16, 16)), Color.Color8(100, 90, 80));
            }
        }
    }

    // dull once emptied, a gold dot under it while locked
    private void DrawChest(WorldObject o, Vector2 centre)
    {
        const float cell = GameMap.CellSize;
        bool empty = o.Contents.Count == 0;
        if (o.Locked)
        {
            DrawCircle(centre + new Vector2(0, cell * 0.3f), 5, Color.Color8(225, 195, 80));
        }
        var box = new Rect2(centre.X - cell * 0.3f, centre.Y - cell * 0.2f, cell * 0.6f, cell * 0.42f);
        DrawRect(box, empty ? Color.Color8(70, 60, 50) : Color.Color8(140, 95, 45));
        DrawRect(box, Color.Color8(30, 20, 10), false, 2);
        DrawRect(new Rect2(centre.X - 4, centre.Y - 5, 8, 8), empty ? Color.Color8(50, 45, 40) : Color.Color8(235, 200, 90));
    }
}
