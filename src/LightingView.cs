using System.Collections.Generic;
using System.Text;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The light map, drawn inside the LightMap viewport like the C++ client's lighting pass: white
/// ground under the map's ambient colour (indoors keep a darker one by day), the lamps and the
/// lights heroes carry added on top with walls and shut doors casting shadows. The viewport is 8
/// bit, so the sum stops at full light, and the Shading layer multiplies it over the world.
/// </summary>
public partial class LightingView : Node2D
{
    private const int Block = 8;

    [Export] public PackedScene? LightScene { get; set; }
    [Export] public Color CarriedColor { get; set; } = Palette.Sand;

    private readonly List<Rid> _ground = new();
    private readonly List<LightView> _carried = new();
    private World? _world;
    private string _wallsWere = "";
    // the scene file has these and _Ready fetches them
    private CanvasModulate _ambient = null!;
    private Node2D _occluders = null!;
    private PointLight2D _sky = null!;
    private Node2D _lamps = null!;
    private Node2D _carriedNode = null!;

    public override void _Ready()
    {
        _ambient = GetNode<CanvasModulate>("Ambient");
        _occluders = GetNode<Node2D>("Occluders");
        _sky = GetNode<PointLight2D>("Sky");
        _lamps = GetNode<Node2D>("Lamps");
        _carriedNode = GetNode<Node2D>("Carried");
    }

    public void Build(World world)
    {
        _world = world;
        GameMap map = world.Chapter.Map;
        int cell = GameMap.CellSize;

        // white ground in blocks, so each block only has the few lights near it on it
        Clear();
        for (int by = 0; by < map.Height; by += Block)
        {
            for (int bx = 0; bx < map.Width; bx += Block)
            {
                Rid item = RenderingServer.CanvasItemCreate();
                RenderingServer.CanvasItemSetParent(item, GetNode<Node2D>("Ground").GetCanvasItem());
                int w = Mathf.Min(Block, map.Width - bx), h = Mathf.Min(Block, map.Height - by);
                RenderingServer.CanvasItemAddRect(item, new Rect2(bx * cell, by * cell, w * cell, h * cell), Colors.White);
                _ground.Add(item);
            }
        }

        foreach (Node old in _lamps.GetChildren())
        {
            old.QueueFree();
        }
        foreach (LightView old in _carried)
        {
            old.QueueFree();
        }
        _carried.Clear();
        if (LightScene != null)
        {
            for (int i = 0; i < map.Lights.Count; i++)
            {
                MapLight lamp = map.Lights[i];
                LightView light = NewLight(_lamps, $"Lamp{i}");
                light.Setup(new Vector2((float)lamp.X, (float)lamp.Y) * cell, (float)lamp.Radius * cell, lamp.Color.ToGodot(), lamp.Flame, i);
            }
            for (int i = 0; i < world.HeroCount; i++)
            {
                LightView light = NewLight(_carriedNode, $"Hero{i}");
                light.Setup(Vector2.Zero, (float)map.Lighting.Carried * cell, CarriedColor, false, i);
                _carried.Add(light);
            }
        }

        // the sky light covers the outdoor cells, so by day the indoors keep the darker ambient
        Image outdoors = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgba8);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                outdoors.SetPixel(x, y, world.Map.Indoors(new Cell(x, y)) ? Colors.Black : Colors.White);
            }
        }
        _sky.Texture = ImageTexture.CreateFromImage(outdoors);
        _sky.TextureScale = cell;
        _sky.Position = new Vector2(map.Width, map.Height) * cell / 2;
        _wallsWere = "";
        Refresh();
    }

    public override void _Process(double delta)
    {
        Refresh();
    }

    public override void _ExitTree()
    {
        Clear();
    }

    private void Refresh()
    {
        if (_world == null)
        {
            return;
        }
        World w = _world;

        // walls change when a door opens or shuts
        var state = new StringBuilder();
        foreach (WorldObject o in w.Map.Objects)
        {
            state.Append(o.Open ? 'o' : 's').Append(o.Destroyed ? 'x' : '-');
        }
        if (state.ToString() != _wallsWere)
        {
            _wallsWere = state.ToString();
            BuildOccluders(w);
        }

        Rules.Sky sky = w.Map.SkyAt(w.CurrentTime());
        _ambient.Color = (sky.Differs ? sky.Indoors : sky.Outdoors).ToGodot();
        _sky.Visible = sky.Differs;
        Color open = sky.Outdoors.ToGodot(), roofed = sky.Indoors.ToGodot();
        _sky.Color = new Color(open.R - roofed.R, open.G - roofed.G, open.B - roofed.B);

        bool carries = w.Chapter.Map.Lighting.Carried > 0;
        for (int i = 0; i < _carried.Count && i < w.HeroCount; i++)
        {
            Token token = w.Tokens.Tokens[i];
            _carried[i].Position = token.Position.ToGodot();
            _carried[i].Visible = carries && token.Floor != World.DeadFloor && !w.Sneaking(i);
        }
    }

    private LightView NewLight(Node parent, string name)
    {
        // LightScene is checked by the caller
        var light = LightScene!.Instantiate<LightView>();
        light.Name = name;
        parent.AddChild(light);
        return light;
    }

    private void BuildOccluders(World w)
    {
        foreach (Node old in _occluders.GetChildren())
        {
            old.QueueFree();
        }
        foreach (Wall wall in w.Map.Walls)
        {
            _occluders.AddChild(new LightOccluder2D
            {
                Occluder = new OccluderPolygon2D
                {
                    Closed = false,
                    Polygon = new[] { wall.A.ToGodot(), wall.B.ToGodot() },
                },
            });
        }
    }

    private void Clear()
    {
        foreach (Rid item in _ground)
        {
            RenderingServer.FreeRid(item);
        }
        _ground.Clear();
    }
}
