using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// A chapter in play between fights. Loads it into a World, steps it every frame, draws what it
/// says through the child views and turns clicks and taps into World calls: a tap on a hero selects
/// them, on a door, lever or locked chest walks over and uses it, anywhere else walks there. Every
/// rule is the World's; this only shows and asks.
/// Start another chapter with "-- --chapter chapters/goblin-keep" on the command line.
/// </summary>
public partial class PlayScreen : Node2D
{
    [Export] public string ChapterFolder { get; set; } = "chapters/chapter-one";
    [Export] public int Seed { get; set; } = 1;

    private World? _world;
    // the scene file has all of these and _Ready fetches them before anything else runs
    private MapView _map = null!;
    private ObjectsView _objects = null!;
    private TokensView _tokens = null!;
    private SubViewport _lightMap = null!;
    private LightingView _lighting = null!;
    private TextureRect _shading = null!;
    private FogView _fog = null!;
    private FloatersView _floaters = null!;
    private PlayCamera _camera = null!;
    private Hud _hud = null!;
    private (int Hero, int Object)? _pendingUse;

    public World? World => _world;

    public override void _Ready()
    {
        _map = GetNode<MapView>("Map");
        _objects = GetNode<ObjectsView>("Objects");
        _tokens = GetNode<TokensView>("Tokens");
        _lightMap = GetNode<SubViewport>("LightMap");
        _lighting = GetNode<LightingView>("LightMap/Lighting");
        _shading = GetNode<TextureRect>("Shading/LightMap");
        _fog = GetNode<FogView>("Overlay/Fog");
        _floaters = GetNode<FloatersView>("Overlay/Floaters");
        _camera = GetNode<PlayCamera>("Camera");
        _hud = GetNode<Hud>("Hud");
        _camera.Tapped += Tap;
        _shading.Texture = _lightMap.GetTexture();
        GetViewport().SizeChanged += FitLightMap;
        FitLightMap();

        string folder = ChapterFolder;
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--chapter")
            {
                folder = args[i + 1];
            }
        }

        try
        {
            _world = World.Load(new ContentFiles(ProjectSettings.GlobalizePath("res://assets")), folder, (ulong)Seed);
        }
        catch (ContentException e)
        {
            GD.PushError($"Could not load {folder}: {e.Message}");
            _hud.SetTitle($"Could not load {folder}");
            _hud.AddLog(e.Message);
            return;
        }
        Build(_world);
    }

    /// <summary>Where a cell's centre is on screen now; input scripts click cells with it.</summary>
    public Vector2 ScreenOfCell(Cell cell)
    {
        return _camera.WorldToScreen(new Vector2((cell.X + 0.5f) * GameMap.CellSize, (cell.Y + 0.5f) * GameMap.CellSize));
    }

    private void Build(World world)
    {
        GameMap map = world.Chapter.Map;
        _hud.SetTitle(world.Chapter.Title);
        _map.Build(world);
        _objects.Bind(world);
        _tokens.Build(world);
        _lighting.Build(world);
        _fog.Bind(world);
        _camera.Bounds = new Rect2(0, 0, map.Width * GameMap.CellSize, map.Height * GameMap.CellSize);
        _camera.JumpTo(world.Tokens.Tokens[0].Position.ToGodot(), 1);
        ShowEvents();
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_world == null)
        {
            return;
        }
        _world.Update(delta);
        if (_pendingUse is (int hero, int id) && _world.Tokens.Tokens[hero].Path.Count == 0)
        {
            _pendingUse = null;
            if (!_world.Interact(hero, id))
            {
                Refuse(_world.Tokens.Tokens[hero].Position.ToGodot());
            }
        }
        ShowEvents();
        Refresh();
    }

    private void FitLightMap()
    {
        Vector2 size = GetViewportRect().Size;
        _lightMap.Size = new Vector2I(Mathf.Max(1, (int)size.X), Mathf.Max(1, (int)size.Y));
    }

    private void Tap(Vector2 screen)
    {
        if (_world == null)
        {
            return;
        }
        World w = _world;
        Vector2 at = _camera.ScreenToWorld(screen);
        for (int i = 0; i < w.HeroCount; i++)
        {
            Token token = w.Tokens.Tokens[i];
            if (!w.Creatures[i].Sheet.Down && at.DistanceTo(token.Position.ToGodot()) <= token.Radius)
            {
                for (int j = 0; j < w.HeroCount; j++)
                {
                    w.Tokens.Tokens[j].Selected = j == i;
                }
                _camera.Following = true;
                return;
            }
        }

        int hero = w.LeaderIndex();
        Cell cell = w.Grid.CellAt(at.ToRules());
        WorldObject? thing = w.Map.ObjectAt(cell);
        _pendingUse = null;
        if (thing != null && w.Usable(thing) && w.Fog.State(w.ViewTeam(), 0, cell) != FogState.Unexplored)
        {
            if (w.GoNear(hero, thing.Id))
            {
                _pendingUse = (hero, thing.Id);
                _camera.Following = true;
            }
            else
            {
                Refuse(at);
            }
            return;
        }
        if (w.Go(hero, cell))
        {
            _camera.Following = true;
        }
        else
        {
            Refuse(at);
        }
    }

    private void Refuse(Vector2 at)
    {
        if (_world != null && _world.Refusal.Length > 0)
        {
            _floaters.Add(at, _world.Refusal, Color.Color8(200, 200, 210));
        }
    }

    private void ShowEvents()
    {
        if (_world == null)
        {
            return;
        }
        foreach (WorldEvent e in _world.TakeEvents())
        {
            switch (e.Kind)
            {
                case WorldEventKind.Reset:
                    _hud.ClearLog();
                    _floaters.Clear();
                    _pendingUse = null;
                    break;
                case WorldEventKind.Log:
                    _hud.AddLog(e.Text);
                    break;
                case WorldEventKind.Floater:
                    _floaters.Add(e.At.ToGodot(), e.Text, Color.Color8(150, 200, 255));
                    break;
                case WorldEventKind.Banner:
                    _hud.Banner(e.Text, e.Seconds);
                    break;
                case WorldEventKind.Cutscene:
                    // cutscenes play from P10; until then one is over as soon as it starts, so the world doesn't wait
                    GD.Print($"Cutscene skipped for now: {e.Text}");
                    _world.EndCutscene();
                    break;
                case WorldEventKind.Talk:
                    GD.Print($"Conversation not shown yet: {e.Text}");
                    break;
                case WorldEventKind.Fight:
                    _pendingUse = null;
                    GD.Print($"Fight with group {e.Group}; fights come with P5");
                    break;
            }
        }
    }

    private void Refresh()
    {
        if (_world == null)
        {
            return;
        }
        _shading.Visible = _world.CurrentLighting() != LightingMode.Off;
        _camera.FollowTarget = _world.Tokens.Tokens[_world.LeaderIndex()].Position.ToGodot();
    }
}
