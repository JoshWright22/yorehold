using System.Numerics;

namespace Yorehold.Rules;

/// <summary>A line that stops sight, in world units.</summary>
public readonly record struct Wall(Vector2 A, Vector2 B);

public static class Sight
{
    /// <summary>True when no wall crosses the line from one point to the other. A wall lying along the line blocks it too.</summary>
    public static bool LineOfSight(Vector2 from, Vector2 to, IReadOnlyList<Wall> walls)
    {
        Vector2 delta = to - from;
        float length = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (length < 1e-4f)
        {
            return true;
        }
        Vector2 ray = delta / length;
        for (int i = 0; i < walls.Count; i++)
        {
            float hit = HitDistance(from, ray, walls[i]);
            if (hit >= 0 && hit < length - 1e-4f)
            {
                return false;
            }
        }
        return true;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    // How far along the ray the wall is hit; -1 if it is not.
    private static float HitDistance(Vector2 origin, Vector2 ray, Wall wall)
    {
        Vector2 edge = wall.B - wall.A;
        Vector2 offset = wall.A - origin;
        float denominator = Cross(ray, edge);
        if (MathF.Abs(denominator) < 1e-7f)
        {
            // Parallel: only a wall on the very same line can be in the way.
            if (MathF.Abs(Cross(offset, ray)) > 1e-5f)
            {
                return -1;
            }
            float a = offset.X * ray.X + offset.Y * ray.Y;
            Vector2 other = wall.B - origin;
            float b = other.X * ray.X + other.Y * ray.Y;
            if (MathF.Max(a, b) < 1e-4f)
            {
                return -1;
            }
            return MathF.Max(1e-4f, MathF.Min(a, b));
        }
        float distance = Cross(offset, edge) / denominator;
        float along = Cross(offset, ray) / denominator;
        return distance > 1e-4f && along >= 0 && along <= 1 ? distance : -1;
    }
}
