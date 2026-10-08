using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The dice of a roll thrown in a strip over the screen (R17), each turned as it settles so the
/// face the game's seeded roll gave lands toward the player. The result never comes from the
/// throw. Flat faces in the screens' colours, numbers in the light grey; a die that wasn't kept
/// (the low one of advantage) is dimmed. Fast or full is how long they tumble.
/// </summary>
public partial class DiceTray : SubViewportContainer
{
    private const float Spacing = 0.95f;
    private const float Radius = 0.42f;

    /// <summary>The dice's look from ui/dice.json (a skin's when one is laid on top), read at start.</summary>
    public static UiDice Look { get; private set; } = new();

    public static void Load(ContentFiles files)
    {
        if (!files.Exists(UiDice.File))
        {
            return;
        }
        try
        {
            Look = UiDice.Read(ContentNode.Read(files, UiDice.File));
        }
        catch (ContentException error)
        {
            GD.PushWarning($"The dice's look can't be read, so it is the game's own: {error.Message}");
        }
    }

    private sealed class Thrown
    {
        public Node3D Die = null!;
        public Quaternion To;
        public Vector3 Axis;
        public float Turns;
        public float X;
    }

    private SubViewport _view = null!;
    private Node3D _table = null!;
    private readonly List<Thrown> _thrown = new();
    private readonly Dictionary<string, ArrayMesh> _meshes = new();
    private readonly RandomNumberGenerator _spin = new();
    private double _age;
    private double _tumble = 1.1;
    private const double Hold = 0.9;
    private const double Fade = 0.3;

    public override void _Ready()
    {
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        _view = new SubViewport { TransparentBg = true, OwnWorld3D = true, Size = new Vector2I(760, 160), RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible };
        AddChild(_view);
        _view.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 1.25f, Position = new Vector3(0, 0, 6) });
        _view.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -30, 0), LightEnergy = 1.1f });
        var environment = new Godot.Environment
        {
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Palette.Ash,
            AmbientLightEnergy = 0.55f,
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
        };
        _view.AddChild(new WorldEnvironment { Environment = environment });
        _table = new Node3D();
        _view.AddChild(_table);
    }

    /// <summary>The last throw has come to rest on its faces (or there is none showing).</summary>
    public bool Landed => !Visible || _age >= _tumble;

    /// <summary>Throws a roll's dice; fast tumbles for half as long. A new throw replaces the last.</summary>
    public void Throw(List<DiceFaces.Shown> dice, bool fast)
    {
        foreach (Node child in _table.GetChildren())
        {
            child.QueueFree();
        }
        _thrown.Clear();
        _tumble = fast ? 0.55 : 1.1;
        _age = 0;
        List<DiceFaces.Shown> shown = dice.Take(Look.Most).ToList();
        float size = (float)Look.Size;
        // more than the strip shows: the rest as a count after the last die
        int more = dice.Count - shown.Count;
        float start = -(shown.Count - (more > 0 ? 0 : 1)) * Spacing * size / 2;
        for (int i = 0; i < shown.Count; i++)
        {
            DiceFaces.Shown die = shown[i];
            DiceSolids.Solid solid = DiceSolids.Of(die.Shape);
            List<string> labels = DiceFaces.Labels(die.Shape);
            // the faces that show the rolled label; a Fate die has two of each, either will do
            List<int> faces = Enumerable.Range(0, labels.Count).Where(f => labels[f] == die.Face).ToList();
            int face = faces.Count == 0 ? 0 : faces[_spin.RandiRange(0, faces.Count - 1)];
            Node3D node = Build(die.Shape, solid, labels, die.Kept);
            _table.AddChild(node);
            // turned so that face looks at the camera, spun a random amount about the view
            Vector3 normal = ToGodot(solid.Normals[face]);
            // straight away from the camera has no single arc to turn by: half a turn about up
            Quaternion facing = normal.Dot(Vector3.Back) < -0.999f ? new Quaternion(Vector3.Up, Mathf.Pi) : new Quaternion(normal, Vector3.Back).Normalized();
            // the number stands upright, give or take a little, however its face was turned up
            Vector3 top = facing * LabelBasis(normal).Y;
            float lean = Mathf.Atan2(top.X, top.Y);
            Quaternion roll = new Quaternion(Vector3.Back, lean + _spin.RandfRange(-0.3f, 0.3f));
            _thrown.Add(new Thrown
            {
                Die = node,
                To = (roll * facing).Normalized(),
                Axis = new Vector3(_spin.RandfRange(-1, 1), _spin.RandfRange(-1, 1), _spin.RandfRange(-0.3f, 0.3f)).Normalized(),
                Turns = _spin.RandfRange(2.5f, 4f),
                X = start + i * Spacing * size,
            });
        }
        if (more > 0)
        {
            _table.AddChild(new Label3D
            {
                Text = $"+{more}", FontSize = 64, PixelSize = 0.006f * size, Modulate = Palette.Named(Look.Body), OutlineSize = 0,
                Position = new Vector3(start + shown.Count * Spacing * size, 0, 0),
            });
        }
        Visible = shown.Count > 0;
        Modulate = Colors.White;
        Place(0);
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }
        _age += delta;
        Place(_age);
        if (_age > _tumble + Hold)
        {
            float left = 1 - (float)((_age - _tumble - Hold) / Fade);
            Modulate = new Color(1, 1, 1, Mathf.Clamp(left, 0, 1));
            if (left <= 0)
            {
                Visible = false;
            }
        }
    }

    // Where each die is at a time: rolling in from the left and slowing to its face.
    private void Place(double age)
    {
        float t = Mathf.Clamp((float)(age / _tumble), 0, 1);
        float settle = 1 - (1 - t) * (1 - t) * (1 - t);
        foreach (Thrown die in _thrown)
        {
            var spin = new Quaternion(die.Axis, die.Turns * Mathf.Tau * (1 - settle));
            die.Die.Quaternion = (spin * die.To).Normalized();
            float bounce = Mathf.Abs(Mathf.Sin(settle * Mathf.Pi * 2.5f)) * 0.18f * (1 - settle);
            die.Die.Position = new Vector3(die.X - (1 - settle) * 1.4f, bounce, 0);
        }
    }

    private Node3D Build(string shape, DiceSolids.Solid solid, List<string> labels, bool kept)
    {
        var root = new Node3D { Scale = Vector3.One * Radius * (float)Look.Size };
        root.AddChild(new MeshInstance3D { Mesh = Mesh(shape, solid), MaterialOverride = Body(kept) });
        for (int f = 0; f < solid.Faces.Count; f++)
        {
            if (labels[f].Length == 0)
            {
                continue;
            }
            Vector3 normal = ToGodot(solid.Normals[f]);
            Vector3 centre = solid.Faces[f].Select(i => ToGodot(solid.Vertices[i])).Aggregate(Vector3.Zero, (a, b) => a + b) / solid.Faces[f].Length;
            var label = new Label3D
            {
                Text = labels[f],
                FontSize = 64,
                PixelSize = solid.Faces.Count >= 12 ? 0.0042f : 0.006f,
                Modulate = Palette.Named(kept ? Look.Numbers : Look.UnkeptNumbers),
                OutlineSize = 0,
                DoubleSided = false,
                Transform = new Transform3D(LabelBasis(normal), centre + normal * 0.012f),
            };
            root.AddChild(label);
        }
        return root;
    }

    // How a face's number sits on it: looking out along the face's normal.
    private static Basis LabelBasis(Vector3 normal)
    {
        Vector3 up = Mathf.Abs(normal.Dot(Vector3.Up)) > 0.9f ? Vector3.Forward : Vector3.Up;
        return Basis.LookingAt(-normal, up);
    }

    private Material Body(bool kept) => new StandardMaterial3D
    {
        AlbedoColor = Palette.Named(kept ? Look.Body : Look.Unkept),
        Roughness = 0.9f,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
    };

    // One mesh per shape, its faces flat (each corner of a face shares the face's normal).
    private ArrayMesh Mesh(string shape, DiceSolids.Solid solid)
    {
        string key = shape == "dF" ? "d6" : shape == "d10t" ? "d10" : shape;
        if (_meshes.TryGetValue(key, out ArrayMesh? found))
        {
            return found;
        }
        var tool = new SurfaceTool();
        tool.Begin(Godot.Mesh.PrimitiveType.Triangles);
        for (int f = 0; f < solid.Faces.Count; f++)
        {
            int[] face = solid.Faces[f];
            Vector3 normal = ToGodot(solid.Normals[f]);
            for (int k = 1; k + 1 < face.Length; k++)
            {
                // wound so the outside is the front
                foreach (int corner in new[] { face[0], face[k + 1], face[k] })
                {
                    tool.SetNormal(normal);
                    tool.AddVertex(ToGodot(solid.Vertices[corner]));
                }
            }
        }
        ArrayMesh mesh = tool.Commit();
        _meshes[key] = mesh;
        return mesh;
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
