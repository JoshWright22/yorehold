using System.Numerics;

namespace Yorehold.Rules;

/// <summary>
/// The solids dice are thrown as: vertices, faces (vertex indices round each face, outward) and
/// each face's outward normal, unit-sized. Face i carries DiceFaces.Labels(shape)[i]. Built from
/// each solid's face directions: a face is the vertices that reach furthest along its normal.
/// </summary>
public static class DiceSolids
{
    public sealed record Solid(List<Vector3> Vertices, List<int[]> Faces, List<Vector3> Normals);

    private static readonly float Phi = (1 + MathF.Sqrt(5)) / 2;

    /// <summary>The solid for a die shape ("d4" to "d20", "d10t", "dF").</summary>
    public static Solid Of(string shape) => shape switch
    {
        "d4" => Build(Tetrahedron(), Tetrahedron().Select(v => -v).ToList()),
        "d6" or "dF" => Build(Cube(), Axes()),
        "d8" => Build(Axes(), Cube()),
        "d10" or "d10t" => Trapezohedron(),
        "d12" => Build(Dodecahedron(), Icosahedron()),
        _ => Build(Icosahedron(), Dodecahedron()),
    };

    private static List<Vector3> Tetrahedron() => new() { new(1, 1, 1), new(1, -1, -1), new(-1, 1, -1), new(-1, -1, 1) };

    private static List<Vector3> Cube()
    {
        var corners = new List<Vector3>();
        foreach (int x in new[] { -1, 1 })
        {
            foreach (int y in new[] { -1, 1 })
            {
                foreach (int z in new[] { -1, 1 })
                {
                    corners.Add(new Vector3(x, y, z));
                }
            }
        }
        return corners;
    }

    private static List<Vector3> Axes() => new() { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };

    private static List<Vector3> Icosahedron()
    {
        var points = new List<Vector3>();
        foreach (int a in new[] { -1, 1 })
        {
            foreach (int b in new[] { -1, 1 })
            {
                points.Add(new Vector3(0, a, b * Phi));
                points.Add(new Vector3(a, b * Phi, 0));
                points.Add(new Vector3(b * Phi, 0, a));
            }
        }
        return points;
    }

    private static List<Vector3> Dodecahedron()
    {
        var points = new List<Vector3>(Cube());
        float inv = 1 / Phi;
        foreach (int a in new[] { -1, 1 })
        {
            foreach (int b in new[] { -1, 1 })
            {
                // the order that makes it the icosahedron's dual, each face about an icosahedron vertex
                points.Add(new Vector3(0, a * Phi, b * inv));
                points.Add(new Vector3(a * inv, 0, b * Phi));
                points.Add(new Vector3(a * Phi, b * inv, 0));
            }
        }
        return points;
    }

    // A pentagonal trapezohedron: two apexes and a zigzag ring of ten, each kite flat by placing the
    // apexes where the ring's planes meet the axis.
    private static Solid Trapezohedron()
    {
        const float ring = 0.12f;
        var vertices = new List<Vector3>();
        for (int k = 0; k < 10; k++)
        {
            float angle = k * MathF.PI / 5;
            vertices.Add(new Vector3(MathF.Cos(angle), MathF.Sin(angle), k % 2 == 0 ? ring : -ring));
        }
        // the plane through a raised ring vertex and its two lowered neighbours meets the axis at the top apex
        Vector3 v0 = vertices[0], before = vertices[9], after = vertices[1];
        Vector3 n = Vector3.Cross(after - v0, before - v0);
        float top = v0.Z + (n.X * v0.X + n.Y * v0.Y) / n.Z;
        vertices.Add(new Vector3(0, 0, top));
        vertices.Add(new Vector3(0, 0, -top));
        var normals = new List<Vector3>();
        for (int k = 0; k < 10; k++)
        {
            // upper kites about the raised ring vertices, lower kites about the lowered ones
            Vector3 middle = vertices[k];
            Vector3 apex = k % 2 == 0 ? vertices[10] : vertices[11];
            Vector3 a = vertices[(k + 9) % 10], b = vertices[(k + 1) % 10];
            Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, apex - middle));
            normals.Add(Vector3.Dot(normal, middle) < 0 ? -normal : normal);
        }
        return Build(vertices, normals);
    }

    private static Solid Build(List<Vector3> vertices, List<Vector3> directions)
    {
        float size = vertices.Max(v => v.Length());
        List<Vector3> scaled = vertices.Select(v => v / size).ToList();
        var faces = new List<int[]>();
        var normals = new List<Vector3>();
        foreach (Vector3 direction in directions)
        {
            Vector3 normal = Vector3.Normalize(direction);
            float reach = scaled.Max(v => Vector3.Dot(v, normal));
            int[] face = Enumerable.Range(0, scaled.Count).Where(i => Vector3.Dot(scaled[i], normal) >= reach - 1e-3f).ToArray();
            // round the face, counter-clockwise seen from outside
            Vector3 centre = face.Aggregate(Vector3.Zero, (sum, i) => sum + scaled[i]) / face.Length;
            Vector3 across = Vector3.Normalize(scaled[face[0]] - centre);
            Vector3 up = Vector3.Cross(normal, across);
            faces.Add(face.OrderBy(i =>
            {
                Vector3 d = scaled[i] - centre;
                return MathF.Atan2(Vector3.Dot(d, up), Vector3.Dot(d, across));
            }).ToArray());
            normals.Add(normal);
        }
        return new Solid(scaled, faces, normals);
    }
}
