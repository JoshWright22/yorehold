using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// A chapter in play. Loads it into a World, steps it every frame, draws what it says through the
/// child views and turns clicks and taps into World calls. Between fights a tap on a hero selects
/// them, on a door, lever or locked chest walks over and uses it, anywhere else walks there. In a
/// fight the taps go to FightControl. Every rule is the World's; this only shows and asks.
/// Start another chapter with "-- --chapter chapters/goblin-keep" on the command line, and
/// another run of the dice with "--seed 7".
/// </summary>
public partial class PlayScreen : Node2D
{
    [Export] public string ChapterFolder { get; set; } = "chapters/chapter-one";
    [Export] public int Seed { get; set; } = 1;
    /// <summary>A hero's reaction asks first (use it or pass) and is not just taken.</summary>
    [Export] public bool ReactionPrompts { get; set; } = true;

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
    private FightControl _fight = null!;
    private FightGroundView _fightGround = null!;
    private TokenBarsView _tokenBars = null!;
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
        _fight = GetNode<FightControl>("Fight");
        _fightGround = GetNode<FightGroundView>("Overlay/FightGround");
        _tokenBars = GetNode<TokenBarsView>("Overlay/TokenBars");
        _camera.Tapped += Tap;
        _shading.Texture = _lightMap.GetTexture();
        GetViewport().SizeChanged += FitLightMap;
        FitLightMap();

        string folder = ChapterFolder;
        int seed = Seed;
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--chapter")
            {
                folder = args[i + 1];
            }
            else if (args[i] == "--seed" && int.TryParse(args[i + 1], out int asked))
            {
                seed = asked;
            }
        }

        try
        {
            _world = World.Load(new ContentFiles(ProjectSettings.GlobalizePath("res://assets")), folder, (ulong)seed);
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

    /// <summary>Where the token with this name is on screen now; null if nobody has it. For input scripts, since creatures move.</summary>
    public Vector2? ScreenOfCreature(string name)
    {
        if (_world == null)
        {
            return null;
        }
        foreach (Token token in _world.Tokens.Tokens)
        {
            if (token.Name == name)
            {
                return _camera.WorldToScreen(token.Position.ToGodot());
            }
        }
        return null;
    }

    private void Build(World world)
    {
        GameMap map = world.Chapter.Map;
        world.Options.ReactionPrompts = ReactionPrompts;
        _hud.SetTitle(world.Chapter.Title);
        _map.Build(world);
        _objects.Bind(world);
        _tokens.Build(world);
        _lighting.Build(world);
        _fog.Bind(world);
        _fightGround.Bind(world, _fight.Aim);
        _tokenBars.Bind(world, _fight.Aim);
        if (_hud.Panels != null)
        {
            _fight.Bind(world, _camera, _hud.Panels, _floaters);
        }
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
        if (w.PartyWiped)
        {
            return; // nothing to do until saves can take the party back (P10)
        }
        if (w.Fighting)
        {
            _fight.Tap(screen);
            return;
        }
        Vector2 at = _camera.ScreenToWorld(screen);
        for (int i = 0; i < w.HeroCount; i++)
        {
            Token token = w.Tokens.Tokens[i];
            if (!w.Creatures[i].Sheet.Down && at.DistanceTo(token.Position.ToGodot()) <= token.Radius)
            {
                _fight.PickCreature(i);
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
                    _fight.FightEnded();
                    break;
                case WorldEventKind.Log:
                    _hud.AddLog(e.Text);
                    if (ShotRunner.Running)
                    {
                        // a screenshot script is timed by frames, so its log says when each thing happened
                        GD.Print($"[frame {ShotRunner.Frame}] {e.Text}");
                    }
                    break;
                case WorldEventKind.Floater:
                {
                    (Color color, float scale) = FloaterLook(e.Text);
                    _floaters.Add(e.At.ToGodot(), e.Text, color, scale);
                    break;
                }
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
                    break;
                case WorldEventKind.Turn:
                    _fight.TurnBegan();
                    break;
                case WorldEventKind.FightOver:
                    _fight.FightEnded();
                    _camera.Following = true;
                    break;
            }
        }
    }

    // Damage is big and red, healing green, a miss or a save pale; anything else is a note in blue.
    private static (Color Color, float Scale) FloaterLook(string text)
    {
        if (text.StartsWith("Critical"))
        {
            return (Color.Color8(255, 210, 90), 1.6f);
        }
        if (text.Length > 0 && char.IsDigit(text[0]))
        {
            return (Color.Color8(255, 110, 85), 1.5f);
        }
        if (text.Length > 1 && text[0] == '+' && char.IsDigit(text[1]))
        {
            return (Color.Color8(120, 220, 110), 1.4f);
        }
        if (text is "Miss" or "Saved" or "Failed")
        {
            return (Color.Color8(225, 225, 235), 1.15f);
        }
        return (Color.Color8(150, 200, 255), 1);
    }

    private void Refresh()
    {
        if (_world == null)
        {
            return;
        }
        _shading.Visible = _world.CurrentLighting() != LightingMode.Off;
        // in a fight the camera stays with whoever is acting, enemies too while the party sees them
        int watched = _world.Fighting && _fight.Watch is int who && who < _world.Tokens.Tokens.Count ? who : _world.LeaderIndex();
        _camera.FollowTarget = _world.Tokens.Tokens[watched].Position.ToGodot();
        _hud.Panels?.Refresh(_world, _fight.Aim);
    }
}
