using System.Numerics;

namespace Yorehold.Rules;

/// <summary>
/// Someone keeping watch. They see in a cone in front of them out to Range; in the dark only out
/// to DarkRange (darkvision, 0 = not at all). World units and radians, 0 = facing +x.
/// </summary>
public sealed record Watcher
{
    public Vector2 Position { get; init; }
    public float Facing { get; init; }
    /// <summary>Full width of the cone, about 120 degrees.</summary>
    public float ConeAngle { get; init; } = 2.1f;
    /// <summary>Below 0 = not watching at all.</summary>
    public float Range { get; init; } = 400;
    public float DarkRange { get; init; }
    public int PassivePerception { get; init; } = 10;
    /// <summary>Searching: the cone becomes a full circle.</summary>
    public bool Alert { get; init; }
}

public sealed record StealthCheck(int Watcher, Vector2 At)
{
    /// <summary>The d20.</summary>
    public int Roll { get; init; }
    /// <summary>The roll plus the Stealth modifier and the light bonus.</summary>
    public int Total { get; init; }
    /// <summary>The watcher's passive Perception.</summary>
    public int Dc { get; init; }
    public bool Spotted { get; init; }
}

public static class Stealth
{
    /// <summary>Whether a watcher sees a point. Light says how lit it is; null means everything is bright.</summary>
    public static bool Sees(Watcher watcher, Vector2 point, IReadOnlyList<Wall> walls, Func<Vector2, LightLevel>? light = null)
    {
        Vector2 delta = point - watcher.Position;
        float distance = Length(delta);
        if (distance > watcher.Range)
        {
            return false;
        }
        if (!watcher.Alert && distance > 1e-4f && AngleBetween(MathF.Atan2(delta.Y, delta.X), watcher.Facing) > watcher.ConeAngle / 2)
        {
            return false;
        }
        if (light != null && distance > watcher.DarkRange && light(point) == LightLevel.Dark)
        {
            return false;
        }
        return Sight.LineOfSight(watcher.Position, point, walls);
    }

    /// <summary>The outline of a watcher's cone cut short by walls, starting at the watcher, for drawing while sneaking.</summary>
    public static List<Vector2> VisionCone(Watcher watcher, IReadOnlyList<Wall> walls, int segments = 24)
    {
        segments = Math.Max(segments, 2);
        float width = watcher.Alert ? 2 * MathF.PI : Math.Clamp(watcher.ConeAngle, 0.0f, 2 * MathF.PI);
        var outline = new List<Vector2> { watcher.Position };
        for (int i = 0; i <= segments; i++)
        {
            float angle = watcher.Facing - width / 2 + width * i / segments;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            outline.Add(watcher.Position + direction * RayTo(watcher.Position, direction, watcher.Range, walls));
        }
        return outline;
    }

    /// <summary>The ruleset's checkEvery is in metres; on the map it is world units, 64 to a square.</summary>
    public static float CheckEveryOnMap(StealthRules rules, int feetPerSquare)
    {
        float metresPerSquare = Math.Max(1, feetPerSquare) * 0.3048f;
        return (float)rules.CheckEvery / metresPerSquare * GameMap.CellSize;
    }

    /// <summary>Summed by hand rather than Vector2.Length, so it rounds like the C++ client.</summary>
    public static float Length(Vector2 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    // The angle between two directions, 0 to pi.
    private static float AngleBetween(float a, float b)
    {
        float twoPi = 2 * MathF.PI;
        float d = MathF.Abs(a - b) % twoPi;
        return d > MathF.PI ? twoPi - d : d;
    }

    // How far along the ray it hits a wall, or limit.
    private static float RayTo(Vector2 origin, Vector2 direction, float limit, IReadOnlyList<Wall> walls)
    {
        float best = limit;
        foreach (Wall wall in walls)
        {
            Vector2 edge = wall.B - wall.A;
            float denominator = direction.X * edge.Y - direction.Y * edge.X;
            if (MathF.Abs(denominator) < 1e-6f)
            {
                continue;
            }
            Vector2 toWall = wall.A - origin;
            float along = (toWall.X * edge.Y - toWall.Y * edge.X) / denominator;
            float onWall = (toWall.X * direction.Y - toWall.Y * direction.X) / denominator;
            if (along >= 0 && onWall >= 0 && onWall <= 1)
            {
                best = MathF.Min(best, along);
            }
        }
        return best;
    }
}

/// <summary>
/// Follows one sneaking creature past a group of watchers. It makes a Stealth check when the
/// sneaker first comes into a watcher's view, then again every CheckEvery of movement in view.
/// Someone not sneaking is spotted as soon as they are seen.
/// </summary>
public sealed class StealthTracker
{
    private readonly StealthRules _rules;
    private readonly float _checkEvery;
    // Per watcher: movement in view since the last check; below 0 = not in view.
    private readonly List<float> _travelled = new();

    /// <summary>CheckEvery taken as it is written, in whatever units the positions use.</summary>
    public StealthTracker(StealthRules rules) : this(rules, (float)rules.CheckEvery)
    {
    }

    public StealthTracker(StealthRules rules, float checkEvery)
    {
        _rules = rules;
        _checkEvery = checkEvery;
    }

    public StealthRules Rules => _rules;
    public float CheckEvery => _checkEvery;

    /// <summary>A tracker for the map, with the ruleset's metres turned into world units.</summary>
    public static StealthTracker OnMap(StealthRules rules, int feetPerSquare)
    {
        return new StealthTracker(rules, Stealth.CheckEveryOnMap(rules, feetPerSquare));
    }

    /// <summary>Forgets progress toward the next check (a new area, or put somewhere else).</summary>
    public void Reset()
    {
        _travelled.Clear();
    }

    /// <summary>
    /// Moves the sneaker in a straight line. Returns every check made, in order, stopping at the
    /// first one that spots them.
    /// </summary>
    public List<StealthCheck> Move(Vector2 from, Vector2 to, bool sneaking, int stealthBonus, IReadOnlyList<Watcher> watchers,
        IReadOnlyList<Wall> walls, Rng random, Func<Vector2, LightLevel>? light = null)
    {
        while (_travelled.Count < watchers.Count)
        {
            _travelled.Add(-1);
        }
        if (_travelled.Count > watchers.Count)
        {
            _travelled.RemoveRange(watchers.Count, _travelled.Count - watchers.Count);
        }
        var checks = new List<StealthCheck>();
        float total = Stealth.Length(to - from);
        // Small steps so a check lands close to where the distance runs out.
        float step = MathF.Max(_checkEvery / 8, 1e-3f);
        int steps = (int)MathF.Ceiling(total / step);
        for (int s = 0; s <= steps; s++)
        {
            Vector2 at = steps != 0 ? from + (to - from) * ((float)s / steps) : from;
            float moved = s != 0 ? total / steps : 0;
            for (int i = 0; i < watchers.Count; i++)
            {
                if (!Stealth.Sees(watchers[i], at, walls, light))
                {
                    _travelled[i] = -1;
                    continue;
                }
                bool check = _travelled[i] < 0;
                _travelled[i] = check ? 0 : _travelled[i] + moved;
                if (_travelled[i] >= _checkEvery)
                {
                    _travelled[i] -= _checkEvery;
                    check = true;
                }
                if (!check && sneaking)
                {
                    continue;
                }
                StealthCheck made;
                if (sneaking)
                {
                    int roll = random.Range(1, 20);
                    int sum = roll + stealthBonus + _rules.LightBonus(light != null ? light(at) : LightLevel.Bright);
                    bool spotted = sum < watchers[i].PassivePerception;
                    if (_rules.Critical && roll == 1)
                    {
                        spotted = true;
                    }
                    if (_rules.Critical && roll == 20)
                    {
                        spotted = false;
                    }
                    made = new StealthCheck(i, at) { Roll = roll, Total = sum, Dc = watchers[i].PassivePerception, Spotted = spotted };
                }
                else
                {
                    made = new StealthCheck(i, at) { Dc = watchers[i].PassivePerception, Spotted = true };
                }
                checks.Add(made);
                if (made.Spotted)
                {
                    return checks;
                }
            }
        }
        return checks;
    }
}
