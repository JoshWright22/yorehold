using System.Numerics;

namespace Yorehold.Rules;

/// <summary>A light for the rules, in world units. One without Shadows shines through walls.</summary>
public readonly record struct WorldLight(Vector2 Position, float Radius, bool Shadows = true);

/// <summary>
/// How well lit each cell is, for rules that care (seeing in the dark, hiding). A light is bright
/// out to BrightFraction of its radius and dim to its edge; walls block it. Lights that never move
/// go in once with SetFixed; carried ones are passed to Level each time.
/// </summary>
public sealed class LightLevels
{
    private readonly int _width;
    private readonly int _height;
    private readonly float _cellSize;
    private readonly LightLevel[] _fixed;

    public LightLevels(int width, int height, float cellSize)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _cellSize = cellSize > 0 ? cellSize : 1;
        _fixed = new LightLevel[_width * _height];
    }

    /// <summary>The level where no light reaches.</summary>
    public LightLevel Ambient { get; set; } = LightLevel.Dark;
    public float BrightFraction { get; set; } = 0.5f;

    public void SetFixed(IReadOnlyList<WorldLight> lights, IReadOnlyList<Wall> walls)
    {
        Array.Fill(_fixed, LightLevel.Dark);
        foreach (WorldLight light in lights)
        {
            if (!(light.Radius > 0) || !float.IsFinite(light.Radius))
            {
                continue;
            }
            int x0 = Math.Clamp((int)MathF.Floor((light.Position.X - light.Radius) / _cellSize), 0, _width - 1);
            int x1 = Math.Clamp((int)MathF.Floor((light.Position.X + light.Radius) / _cellSize), 0, _width - 1);
            int y0 = Math.Clamp((int)MathF.Floor((light.Position.Y - light.Radius) / _cellSize), 0, _height - 1);
            int y1 = Math.Clamp((int)MathF.Floor((light.Position.Y + light.Radius) / _cellSize), 0, _height - 1);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * _width + x;
                    if (_fixed[i] == LightLevel.Bright)
                    {
                        continue;
                    }
                    LightLevel here = From(light, new Vector2((x + 0.5f) * _cellSize, (y + 0.5f) * _cellSize), walls);
                    _fixed[i] = here > _fixed[i] ? here : _fixed[i];
                }
            }
        }
    }

    /// <summary>The fixed lights plus moving ones (torches in hand), which are tested on the spot.</summary>
    public LightLevel Level(Cell cell, IReadOnlyList<WorldLight>? moving = null, IReadOnlyList<Wall>? walls = null)
    {
        if (cell.X < 0 || cell.Y < 0 || cell.X >= _width || cell.Y >= _height)
        {
            return Ambient;
        }
        LightLevel fixedHere = _fixed[cell.Y * _width + cell.X];
        LightLevel best = fixedHere > Ambient ? fixedHere : Ambient;
        var center = new Vector2((cell.X + 0.5f) * _cellSize, (cell.Y + 0.5f) * _cellSize);
        foreach (WorldLight light in moving ?? Array.Empty<WorldLight>())
        {
            if (best == LightLevel.Bright)
            {
                break;
            }
            LightLevel here = From(light, center, walls ?? Array.Empty<Wall>());
            best = here > best ? here : best;
        }
        return best;
    }

    public bool Lit(Cell cell, IReadOnlyList<WorldLight>? moving = null, IReadOnlyList<Wall>? walls = null)
    {
        return Level(cell, moving, walls) != LightLevel.Dark;
    }

    private LightLevel From(WorldLight light, Vector2 center, IReadOnlyList<Wall> walls)
    {
        if (!(light.Radius > 0) || !float.IsFinite(light.Radius))
        {
            return LightLevel.Dark;
        }
        Vector2 delta = center - light.Position;
        float distance2 = delta.X * delta.X + delta.Y * delta.Y;
        if (distance2 > light.Radius * light.Radius)
        {
            return LightLevel.Dark;
        }
        if (light.Shadows && !Sight.LineOfSight(light.Position, center, walls))
        {
            return LightLevel.Dark;
        }
        float bright = light.Radius * Math.Clamp(BrightFraction, 0.0f, 1.0f);
        return distance2 <= bright * bright ? LightLevel.Bright : LightLevel.Dim;
    }
}
