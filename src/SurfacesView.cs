using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The surfaces lying on the map (fire, water, ice, grease) as tinted squares under the tokens, so
/// the light and the fog fall over them like the floor. A surface the palette has no colour for
/// is drawn grey.
/// </summary>
public partial class SurfacesView : Node2D
{
    private static readonly Dictionary<string, Color> Colors = new()
    {
        ["fire"] = Palette.Red, // amber is the aimed area's colour
        ["water"] = Palette.Blue,
        ["ice"] = Palette.Mint,
        ["grease"] = Palette.Olive,
    };

    private World? _world;
    private string _shown = "";
    private double _time;

    public void Bind(World world)
    {
        _world = world;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        // a fire flickers; the rest only redraw when what lies there changes
        string shown = _world == null ? "" : string.Join("|", _world.Surfaces.ConvertAll(s => $"{s.Id}{s.At}{s.Size}"));
        if (shown != _shown || shown.Contains("fire"))
        {
            _shown = shown;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_world == null)
        {
            return;
        }
        const float s = GameMap.CellSize;
        foreach (SurfacePatch patch in _world.Surfaces)
        {
            Color color = Colors.GetValueOrDefault(patch.Id, Palette.Smoke);
            float alpha = patch.Id == "fire" ? 0.45f + 0.1f * Mathf.Sin((float)_time * 7) : 0.45f;
            foreach (Cell c in _world.CellsOf(patch))
            {
                var square = new Rect2(c.X * s + 2, c.Y * s + 2, s - 4, s - 4);
                DrawRect(square, Palette.Faded(color, alpha));
                DrawRect(square, Palette.Faded(color, 0.9f), false, 2);
            }
        }
    }
}
