using System.Numerics;

namespace Yorehold.Rules;

/// <summary>
/// A die thrown across a table: a small rigid-body simulation of one solid (DiceSolids) under
/// gravity, bouncing off the felt and the table's rails until it comes to rest flat on a face.
/// Nothing here decides the roll; the screen plays the path back and puts the rolled number on
/// whichever face the path leaves on top. The same seed gives the same throw.
/// </summary>
public static class DiceTumble
{
    /// <summary>One frame of the throw: where the die's middle is and how it is turned.</summary>
    public readonly record struct Frame(Vector3 Position, Quaternion Rotation);

    /// <summary>The throw, a frame for each sixtieth of a second, and the face it rests on top with (for a d4, the face down).</summary>
    public sealed record Path(List<Frame> Frames, int Face);

    /// <summary>The table: floor at y = 0, rails at x = ±HalfWidth and z = ±HalfDepth.</summary>
    public readonly record struct Table(float HalfWidth, float HalfDepth);

    private const float Step = 1f / 240;
    private const int StepsPerFrame = 4;
    private const float Gravity = 30f;
    private const float Bounce = 0.35f;
    private const float Friction = 0.45f;
    private const float MostSeconds = 3f;

    /// <summary>
    /// Throws a die of size (its farthest corner from the middle) from the table's left end, in its
    /// lane (z), with the seed's own spin and speed, to rest.
    /// </summary>
    public static Path Throw(string shape, float size, Table table, float lane, int seed) =>
        ThrowAll(new[] { shape }, size, table, new[] { lane }, seed)[0];

    /// <summary>
    /// Throws several dice together, one per lane, from the table's left end. They knock off each
    /// other (as balls of their size) as well as the felt and the rails, so none ends inside another.
    /// </summary>
    public static List<Path> ThrowAll(IReadOnlyList<string> shapes, float size, Table table, IReadOnlyList<float> lanes, int seed)
    {
        var random = new Random(seed);
        float Between(float low, float high) => low + (float)random.NextDouble() * (high - low);
        var dice = new List<Body>();
        for (int i = 0; i < shapes.Count; i++)
        {
            DiceSolids.Solid solid = DiceSolids.Of(shapes[i]);
            dice.Add(new Body
            {
                Solid = solid,
                Corners = solid.Vertices.Select(v => v * size).ToList(),
                Position = new Vector3(-table.HalfWidth + size * (1.5f + Between(0, 1.5f)), size * 3f + Between(0, size * 2), lanes[i]),
                // thrown hard enough to cross most of the table, whatever its size
                Velocity = new Vector3(table.HalfWidth * Between(1.2f, 1.6f), Between(0.5f, 2f), table.HalfDepth * Between(-0.6f, 0.6f)),
                Rotation = Quaternion.Normalize(new Quaternion(Between(-1, 1), Between(-1, 1), Between(-1, 1), Between(-1, 1))),
                Spin = new Vector3(Between(-25, 25), Between(-25, 25), Between(-25, 25)),
            });
        }
        foreach (Body die in dice)
        {
            die.Frames.Add(new Frame(die.Position, die.Rotation));
        }
        float inertia = 0.4f * size * size; // a solid ball's, near enough for a die
        float reach = size * 0.9f; // how near two dice's middles come before they knock
        float still = 0;
        for (int step = 1; step <= MostSeconds / Step; step++)
        {
            foreach (Body die in dice)
            {
                die.Velocity.Y -= Gravity * Step;
                die.Position += die.Velocity * Step;
                var turn = new Quaternion(die.Spin * (Step * 0.5f), 0) * die.Rotation;
                die.Rotation = Quaternion.Normalize(new Quaternion(die.Rotation.X + turn.X, die.Rotation.Y + turn.Y, die.Rotation.Z + turn.Z, die.Rotation.W + turn.W));
                // the felt and the four rails, each a plane the corners can't pass
                Collide(die, Vector3.UnitY, 0);
                Collide(die, -Vector3.UnitX, -table.HalfWidth);
                Collide(die, Vector3.UnitX, -table.HalfWidth);
                Collide(die, -Vector3.UnitZ, -table.HalfDepth);
                Collide(die, Vector3.UnitZ, -table.HalfDepth);
                // the air and the cloth take a little of the spin
                die.Spin *= 1 - 0.6f * Step;
            }
            // two dice that meet push apart and trade some of their speed
            for (int a = 0; a < dice.Count; a++)
            {
                for (int b = a + 1; b < dice.Count; b++)
                {
                    Vector3 apart = dice[b].Position - dice[a].Position;
                    float distance = apart.Length();
                    if (distance >= reach * 2 || distance < 1e-5f)
                    {
                        continue;
                    }
                    Vector3 normal = apart / distance;
                    float overlap = reach * 2 - distance;
                    dice[a].Position -= normal * (overlap / 2);
                    dice[b].Position += normal * (overlap / 2);
                    float closing = Vector3.Dot(dice[a].Velocity - dice[b].Velocity, normal);
                    if (closing > 0)
                    {
                        Vector3 push = normal * (closing * (1 + Bounce) / 2);
                        dice[a].Velocity -= push;
                        dice[b].Velocity += push;
                    }
                }
            }
            if (step % StepsPerFrame == 0)
            {
                foreach (Body die in dice)
                {
                    die.Frames.Add(new Frame(die.Position, die.Rotation));
                }
            }
            still = dice.All(d => d.Velocity.Length() < 0.2f && d.Spin.Length() < 0.8f) ? still + Step : 0;
            if (still > 0.15f)
            {
                break;
            }
        }

        var paths = new List<Path>();
        for (int i = 0; i < dice.Count; i++)
        {
            Body die = dice[i];
            // settle: turn the last little way onto whichever face is lowest, flat on the felt
            int down = Face(die.Solid, die.Rotation, lowest: true);
            Vector3 downNormal = Vector3.Transform(die.Solid.Normals[down], die.Rotation);
            Quaternion flat = Between2(downNormal, -Vector3.UnitY) * die.Rotation;
            float rest = -die.Corners.Select(c => Vector3.Transform(c, flat).Y).Min();
            Frame last = die.Frames[^1];
            for (int k = 1; k <= 8; k++)
            {
                float t = k / 8f;
                var at = Vector3.Lerp(last.Position, new Vector3(last.Position.X, rest, last.Position.Z), t);
                die.Frames.Add(new Frame(at, Quaternion.Slerp(last.Rotation, flat, t)));
            }
            paths.Add(new Path(die.Frames, shapes[i] == "d4" ? down : Face(die.Solid, flat, lowest: false)));
        }
        return paths;

        // pushes a corner that has gone through the plane (normal n, offset d: n·x >= d) back out,
        // with a bounce along n and friction across it
        void Collide(Body die, Vector3 normal, float offset)
        {
            foreach (Vector3 corner in die.Corners)
            {
                Vector3 arm = Vector3.Transform(corner, die.Rotation);
                Vector3 point = die.Position + arm;
                float depth = offset - Vector3.Dot(normal, point);
                if (depth <= 0)
                {
                    continue;
                }
                die.Position += normal * depth;
                Vector3 pointVelocity = die.Velocity + Vector3.Cross(die.Spin, arm);
                float into = Vector3.Dot(pointVelocity, normal);
                if (into >= 0)
                {
                    continue;
                }
                Vector3 armCrossN = Vector3.Cross(arm, normal);
                float push = -(1 + Bounce) * into / (1 + armCrossN.LengthSquared() / inertia);
                die.Velocity += normal * push;
                die.Spin += armCrossN * (push / inertia);
                Vector3 slide = pointVelocity - normal * into;
                if (slide.LengthSquared() > 1e-6f)
                {
                    Vector3 along = Vector3.Normalize(slide);
                    Vector3 armCrossT = Vector3.Cross(arm, along);
                    float rub = Math.Min(Friction * push, slide.Length() / (1 + armCrossT.LengthSquared() / inertia));
                    die.Velocity -= along * rub;
                    die.Spin -= armCrossT * (rub / inertia);
                }
            }
        }
    }

    // One die in the air or on the felt.
    private sealed class Body
    {
        public DiceSolids.Solid Solid = null!;
        public List<Vector3> Corners = new();
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Rotation;
        public Vector3 Spin;
        public List<Frame> Frames = new();
    }

    /// <summary>The face whose outward normal points most up (or, lowest, most down) with the die turned so.</summary>
    public static int Face(DiceSolids.Solid solid, Quaternion rotation, bool lowest)
    {
        int best = 0;
        float bestY = lowest ? float.MaxValue : float.MinValue;
        for (int f = 0; f < solid.Normals.Count; f++)
        {
            float y = Vector3.Transform(solid.Normals[f], rotation).Y;
            if (lowest ? y < bestY : y > bestY)
            {
                bestY = y;
                best = f;
            }
        }
        return best;
    }

    // the shortest turn that carries direction a onto b
    private static Quaternion Between2(Vector3 a, Vector3 b)
    {
        a = Vector3.Normalize(a);
        b = Vector3.Normalize(b);
        float dot = Vector3.Dot(a, b);
        if (dot > 0.99999f)
        {
            return Quaternion.Identity;
        }
        if (dot < -0.99999f)
        {
            Vector3 axis = Math.Abs(a.X) < 0.9f ? Vector3.Cross(a, Vector3.UnitX) : Vector3.Cross(a, Vector3.UnitY);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        Vector3 cross = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(cross, 1 + dot));
    }
}
