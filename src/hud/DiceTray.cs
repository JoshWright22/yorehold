using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The dice of a roll thrown across the screen as if it were the table (R17, R19): each die flies
/// in from the left, bounces off the screen's edges and the others and rolls to a stop, as
/// DiceTumble worked out, seen from straight above with only its shadow on the map. The number
/// the game's seeded roll gave is put on the face that ends up on top; the result never comes
/// from the throw. A die that wasn't kept (the low one of advantage) is dimmed; fast plays the
/// throw at twice the speed.
/// </summary>
public partial class DiceTray : SubViewportContainer
{
    // the screen as a table, in the tray's own units: 16 by 9 like the window, floor at y = 0
    private static readonly DiceTumble.Table Felt = new(8f, 4.5f);
    private const float DieSize = 0.42f;
    // high above with a narrow lens, so the floor fills the screen and the dice barely lean
    private const float CameraHeight = 30f;
    private const double Hold = 0.9;
    private const double Fade = 0.35;

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

    private sealed record Thrown(Node3D Die, DiceTumble.Path Path);

    private SubViewport _view = null!;
    private Node3D _dice = null!;
    private readonly List<Thrown> _thrown = new();
    private readonly Dictionary<string, ArrayMesh> _meshes = new();
    private readonly RandomNumberGenerator _spin = new();
    private double _age;
    private double _speed = 1;
    private double _length;

    /// <summary>The last throw has come to rest on its faces (or there is none showing).</summary>
    public bool Landed => !Visible || _age * _speed * 60 >= _length;

    public override void _Ready()
    {
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        _view = new SubViewport { TransparentBg = true, OwnWorld3D = true, Size = new Vector2I(1280, 720), RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(_view);
        // straight down at the screen-table; its height and lens make the floor exactly the screen
        var camera = new Camera3D { Fov = 2 * Mathf.RadToDeg(Mathf.Atan(Felt.HalfDepth / CameraHeight)), KeepAspect = Camera3D.KeepAspectEnum.Height };
        _view.AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, CameraHeight, 0), Vector3.Zero, Vector3.Forward);
        _view.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-60, -25, 0), LightEnergy = 1.1f, ShadowEnabled = true });
        var environment = new Godot.Environment
        {
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Palette.Ash,
            AmbientLightEnergy = 0.5f,
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
        };
        _view.AddChild(new WorldEnvironment { Environment = environment });
        _view.AddChild(ShadowCatcher());
        _dice = new Node3D();
        _view.AddChild(_dice);
    }

    // The floor the dice land on: unseen itself, it only takes their shadows, so the dice sit on the map.
    private static Node3D ShadowCatcher() => new MeshInstance3D
    {
        Mesh = new PlaneMesh { Size = new Vector2(Felt.HalfWidth * 2, Felt.HalfDepth * 2) },
        MaterialOverride = new StandardMaterial3D { ShadowToOpacity = true, AlbedoColor = new Color(Palette.Night, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
    };

    /// <summary>Throws a roll's dice across the table; fast plays the throw twice as quick. A new throw replaces the last.</summary>
    public void Throw(List<DiceFaces.Shown> dice, bool fast)
    {
        foreach (Node child in _dice.GetChildren())
        {
            child.QueueFree();
        }
        _thrown.Clear();
        _speed = fast ? 2 : 1;
        _age = 0;
        List<DiceFaces.Shown> shown = dice.Take(Look.Most).ToList();
        int more = dice.Count - shown.Count;
        float size = DieSize * (float)Look.Size;
        // thrown together, each from its own lane across the table; they knock off each other
        List<float> lanes = Enumerable.Range(0, shown.Count)
            .Select(i => shown.Count == 1 ? 0 : (i - (shown.Count - 1) / 2f) * Math.Min(size * 2.6f, Felt.HalfDepth * 1.2f / (shown.Count - 1))).ToList();
        List<string> shapes = shown.Select(d => d.Shape == "dF" ? "d6" : d.Shape == "d10t" ? "d10" : d.Shape).ToList();
        List<DiceTumble.Path> paths = DiceTumble.ThrowAll(shapes, size, Felt, lanes, (int)_spin.Randi());
        for (int i = 0; i < shown.Count; i++)
        {
            Node3D node = Build(shown[i], DiceSolids.Of(shown[i].Shape), paths[i].Face, size);
            _dice.AddChild(node);
            _thrown.Add(new Thrown(node, paths[i]));
        }
        _length = _thrown.Select(t => t.Path.Frames.Count).DefaultIfEmpty(0).Max();
        if (more > 0)
        {
            _dice.AddChild(new Label3D
            {
                Text = $"+{more}", FontSize = 96, PixelSize = 0.006f, Modulate = Palette.Named(Look.Body), OutlineSize = 0,
                Position = new Vector3(Felt.HalfWidth - 1.2f, 0.05f, 0), RotationDegrees = new Vector3(-90, 0, 0),
            });
        }
        Visible = shown.Count > 0;
        Modulate = Colors.White;
        Place();
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }
        _age += delta;
        Place();
        double after = _age - _length / (60 * _speed);
        if (after > Hold)
        {
            float left = 1 - (float)((after - Hold) / Fade);
            Modulate = new Color(1, 1, 1, Mathf.Clamp(left, 0, 1));
            if (left <= 0)
            {
                Visible = false;
            }
        }
    }

    // Each die where its throw has it now.
    private void Place()
    {
        int frame = (int)(_age * _speed * 60);
        foreach (Thrown die in _thrown)
        {
            DiceTumble.Frame at = die.Path.Frames[Math.Min(frame, die.Path.Frames.Count - 1)];
            die.Die.Position = new Vector3(at.Position.X, at.Position.Y, at.Position.Z);
            die.Die.Quaternion = new Quaternion(at.Rotation.X, at.Rotation.Y, at.Rotation.Z, at.Rotation.W).Normalized();
        }
    }

    // A die whose face `up` (the one its throw leaves on top) carries the number rolled: that
    // number's own face and the up face swap labels, so the die still has each label once.
    private Node3D Build(DiceFaces.Shown die, DiceSolids.Solid solid, int up, float size)
    {
        List<string> labels = DiceFaces.Labels(die.Shape).ToList();
        int own = labels.IndexOf(die.Face);
        if (own >= 0 && own != up)
        {
            (labels[own], labels[up]) = (labels[up], labels[own]);
        }
        var root = new Node3D { Scale = Vector3.One * size };
        root.AddChild(new MeshInstance3D { Mesh = Mesh(die.Shape, solid), MaterialOverride = Body(die.Kept) });
        for (int f = 0; f < solid.Faces.Count; f++)
        {
            if (labels[f].Length == 0)
            {
                continue;
            }
            Vector3 normal = ToGodot(solid.Normals[f]);
            Vector3 centre = solid.Faces[f].Select(i => ToGodot(solid.Vertices[i])).Aggregate(Vector3.Zero, (a, b) => a + b) / solid.Faces[f].Length;
            root.AddChild(new Label3D
            {
                Text = labels[f],
                FontSize = 64,
                PixelSize = solid.Faces.Count >= 20 ? 0.0052f : solid.Faces.Count >= 12 ? 0.0066f : 0.009f,
                Modulate = Palette.Named(die.Kept ? Look.Numbers : Look.UnkeptNumbers),
                OutlineSize = 0,
                DoubleSided = false,
                Transform = new Transform3D(LabelBasis(normal), centre + normal * 0.012f),
            });
        }
        return root;
    }

    // How a face's number sits on it: looking out along the face's normal.
    private static Basis LabelBasis(Vector3 normal)
    {
        Vector3 up = Mathf.Abs(normal.Dot(Vector3.Up)) > 0.9f ? Vector3.Forward : Vector3.Up;
        return Basis.LookingAt(-normal, up);
    }

    private static Material Body(bool kept) => new StandardMaterial3D
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
