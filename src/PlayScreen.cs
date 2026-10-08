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
    /// <summary>How the screen opens, as the title asked.</summary>
    public enum StartKind
    {
        /// <summary>The chapter with its ready-made party.</summary>
        Quick,
        /// <summary>The save in StartSave.</summary>
        Continue,
        /// <summary>The seats first; leaving them without starting goes back to the title.</summary>
        Party,
        /// <summary>The character library; closing it goes back to the title.</summary>
        Library,
    }

    [Export] public string ChapterFolder { get; set; } = "chapters/chapter-one";
    [Export] public int Seed { get; set; } = 1;

    public StartKind Start { get; set; } = StartKind.Quick;
    public string StartSave { get; set; } = "";
    /// <summary>The content the chapter is read from; null reads the game's own.</summary>
    public ContentFiles? Content { get; set; }
    /// <summary>The package Content puts over the game's (an import or one made in Create), for the saves to remember; "" for the game's.</summary>
    public string Package { get; set; } = "";
    /// <summary>A playtest from Create: nothing is saved and no character goes home to the library.</summary>
    public bool Playtest { get; set; }

    /// <summary>Escape with nothing else to close: Main opens the pause list.</summary>
    public event System.Action? PauseAsked;
    /// <summary>The title's character screens were closed without starting.</summary>
    public event System.Action? TitleAsked;
    /// <summary>Why the chapter didn't load; empty when it did.</summary>
    public string LoadProblem { get; private set; } = "";

    private World? _world;
    private int _serverAiSeen; // the server's config version last put on the world (0 = none yet)
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
    private CharacterScreen _characters = null!;
    private CutsceneView _cutscene = null!;
    private (int Hero, int Object)? _pendingUse;
    // the gear panel opens on this pile or shop ("pile:2", "shop:0") once the hero stops beside it
    private (int Hero, string Source)? _pendingOpen;
    private int _seed;
    private bool _clearedWritten;
    private bool _fromTitle;
    private (int Lighting, int Time) _lightingShown;

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
        _characters = GetNode<CharacterScreen>("Characters");
        _characters.StartPressed += Restart;
        _characters.Closed += () =>
        {
            // opened from the title and left without starting: there is no game to go back to
            if (_fromTitle)
            {
                _fromTitle = false;
                TitleAsked?.Invoke();
            }
        };
        _cutscene = GetNode<CutsceneView>("Hud/Cutscene");
        _cutscene.Finished += () => _world?.EndCutscene();
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

        _seed = seed;
        try
        {
            _world = World.Load(Content ?? App.Content(), folder, (ulong)seed);
            _world.Package = Package;
        }
        catch (ContentException e)
        {
            // a playtest of a package being edited may well not load; that is said, not an engine error
            if (!Playtest)
            {
                GD.PushError($"Could not load {folder}: {e.Message}");
            }
            LoadProblem = e.Message;
            _hud.SetTitle($"Could not load {folder}");
            _hud.AddLog(e.Message);
            return;
        }
        Build(_world);
        App.Changed += ApplySettings;
        // the first adventure starts with the controls card; screenshot and playtest runs keep their screens clear
        if (!App.Settings.ShownHelp && !ShotRunner.Running && _hud.Panels is PlayHud firstTime)
        {
            Callable.From(() => firstTime.ShowHelp(true)).CallDeferred();
            App.Settings.ShownHelp = true;
            App.Save();
        }
        ApplySettings();
        switch (Start)
        {
            case StartKind.Continue:
                // C++ client: no save to continue starts a new adventure and says so
                if (!System.IO.File.Exists(StartSave))
                {
                    _hud.AddLog("No save to continue. Starting a new adventure.");
                }
                else if (!_world.Load(StartSave))
                {
                    _hud.AddLog(_world.Refusal);
                    LoadProblem = _world.Refusal;
                }
                break;
            case StartKind.Party:
            case StartKind.Library:
                _fromTitle = true;
                _characters.Open(_world, Places.CharactersFolder(), Start == StartKind.Party ? CharacterScreen.View.Party : CharacterScreen.View.Characters, Start == StartKind.Library);
                break;
        }
    }

    // What the settings screen changed, onto the World and the camera.
    private void ApplySettings()
    {
        GameSettings settings = App.Settings;
        if (_world != null)
        {
            _world.Options.Lighting = settings.Lighting;
            _world.Options.TimeOfDay = settings.TimeOfDay;
            _world.Options.SharedFog = settings.SharedFog;
            _world.Options.ReactionPrompts = settings.ReactionPrompts;
            if ((settings.Lighting, settings.TimeOfDay) != _lightingShown)
            {
                _lightingShown = (settings.Lighting, settings.TimeOfDay);
                _lighting.Build(_world); // the ambient colour comes from the time of day
            }
        }
        _camera.KeyPanSpeed = settings.PanSpeed;
        _camera.ZoomToCursor = settings.ZoomToCursor;
        _camera.EdgeScroll = settings.EdgeScroll;
        _camera.FollowAllowed = settings.CameraFollows;
        _hud.Panels?.ShowKeys();
    }

    /// <summary>Loads a save from the load screen; false with the reason in the log.</summary>
    public bool LoadFrom(string path)
    {
        if (_world == null || _world.Fighting)
        {
            return false;
        }
        _cutscene.Skip();
        if (_world.Load(path))
        {
            return true;
        }
        _hud.AddLog(_world.Refusal);
        return false;
    }

    /// <summary>Why a save can't be loaded right now; empty when it can.</summary>
    public string LoadRefusal => _world == null ? "The chapter didn't load." : _world.Fighting ? "Not in a fight." : "";

    /// <summary>Saves if the rules allow it now, for Save and quit. In a fight the last save stands.</summary>
    public void SaveNow()
    {
        if (_world != null && _world.CanSave && !Playtest)
        {
            WriteSave(_world.StateJson().ToJsonString());
        }
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
        _fightGround.Bind(world, _fight.Aim);
        _tokenBars.Bind(world, _fight.Aim);
        if (_hud.Panels != null)
        {
            _fight.Bind(world, _camera, _hud.Panels, _floaters);
            _hud.Panels.MenuPressed += Menu;
            _hud.Panels.ItemOrdered += Order;
            _hud.Panels.SpellOrdered += Order;
            _hud.Panels.CampOrdered += Order;
            _hud.Panels.ReplyPressed += index => _world?.Reply(index, _world.LeaderIndex());
            _hud.Panels.BackPressed += () => _world?.ReturnFromWipe();
            _hud.Panels.TradePressed += Trade;
            _hud.Panels.HotbarChanged += edit =>
            {
                if (edit.Action.Length > 0)
                {
                    _world?.PutOnHotbar(edit.Hero, edit.Slot, edit.Action);
                }
                else
                {
                    _world?.TakeOffHotbar(edit.Hero, edit.Slot);
                }
            };
        }
        ShowChapter(world);
        ShowEvents();
        Refresh();
    }

    // The map views for the chapter the World is in now; again whenever it goes to another.
    private void ShowChapter(World world)
    {
        GameMap map = world.Chapter.Map;
        _hud.SetTitle(world.Chapter.Title);
        _map.Build(world);
        _objects.Bind(world);
        GetNode<SurfacesView>("Surfaces").Bind(world);
        _tokens.Build(world);
        _lighting.Build(world);
        _fog.Bind(world);
        _pendingUse = null;
        _pendingOpen = null;
        _camera.Bounds = new Rect2(0, 0, map.Width * GameMap.CellSize, map.Height * GameMap.CellSize);
        _camera.JumpTo(world.Tokens.Tokens[world.LeaderIndex()].Position.ToGodot(), 1);
        _camera.Following = true;
    }

    public override void _Process(double delta)
    {
        if (_world == null)
        {
            return;
        }
        // the world waits while the character screens are up
        _fight.Paused = _characters.IsOpen;
        if (_characters.IsOpen)
        {
            Refresh();
            return;
        }
        if (App.Online.ConfigVersion != _serverAiSeen)
        {
            _serverAiSeen = App.Online.ConfigVersion;
            _world.ApplyServerAi(App.Online.Config["ai"]);
        }
        _world.Update(delta);
        if (!_clearedWritten && _world.ChapterCleared())
        {
            _clearedWritten = true;
            WriteBack();
        }
        if (_pendingUse is (int hero, int id) && _world.Tokens.Tokens[hero].Path.Count == 0)
        {
            _pendingUse = null;
            if (!_world.Interact(hero, id))
            {
                Refuse(_world.Tokens.Tokens[hero].Position.ToGodot());
                _pendingOpen = null;
            }
        }
        if (_pendingUse == null && _pendingOpen is (int opener, string source) && _world.Tokens.Tokens[opener].Path.Count == 0)
        {
            _pendingOpen = null;
            if (!_world.Fighting && SourceOpen(_world, opener, source))
            {
                _hud.Panels?.OpenGear(source);
            }
        }
        ShowEvents();
        Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_world == null || _characters.IsOpen || _cutscene.Playing || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        // In a conversation 1 to 9 pick a reply and Escape walks away; the mouse still reaches the buttons.
        if (_world.Talk != null)
        {
            if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key9)
            {
                _world.Reply((int)(key.Keycode - Key.Key1), _world.LeaderIndex());
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.Escape)
            {
                _world.EndTalk();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.T)
            {
                Trade();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        string? panel = null;
        foreach ((string action, string name) in PanelKeys)
        {
            if (App.Pressed(@event, action))
            {
                panel = name;
                break;
            }
        }
        if (App.Pressed(@event, "help") && _hud.Panels is PlayHud help)
        {
            help.ShowHelp(!help.HelpShown);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.Keycode == Key.Escape)
        {
            // Escape closes the help card or the open panel first; with nothing to close it is the pause list
            if (_hud.Panels is PlayHud card && card.HelpShown)
            {
                card.ShowHelp(false);
            }
            else if (_hud.Panels is PlayHud open && open.OpenPanel.Length > 0)
            {
                open.TogglePanel(open.OpenPanel);
            }
            else
            {
                PauseAsked?.Invoke();
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (panel != null)
        {
            Menu(panel);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        App.Changed -= ApplySettings;
        // Leaving the game takes the brought characters home, or marks them away in the save.
        WriteBack(System.IO.File.Exists(Places.SaveFile()) && _world != null && !_world.ChapterCleared() ? SaveName : "");
    }

    // the bound actions that open a panel or save and load, with the menu button each one presses
    private static readonly (string Action, string Menu)[] PanelKeys =
    {
        ("sheet", "Sheet"), ("gear", "Gear"), ("spells", "Spells"), ("journal", "Journal"), ("camp", "Camp"), ("save", "Save"), ("load", "Load"),
    };

    private void Menu(string name)
    {
        if (_world == null)
        {
            return;
        }
        switch (name)
        {
            case "Characters":
                WriteBack(); // so the library shows what the brought characters have now
                _characters.Open(_world, Places.CharactersFolder(), CharacterScreen.View.Characters);
                return;
            case "Save":
                if (_world.CanSave)
                {
                    WriteSave(_world.StateJson().ToJsonString());
                    _hud.Banner("Saved", 1.2);
                }
                else
                {
                    _hud.AddLog("Saves are made between fights, with nothing else going on.");
                }
                return;
            case "Load":
                LoadSave();
                return;
        }
        _hud.Panels?.TogglePanel(name);
    }

    private const string SaveName = "adventure.json";

    // The autosave, through the save format's envelope (the file before it is kept as .bak).
    private void WriteSave(string state)
    {
        if (Playtest)
        {
            return;
        }
        try
        {
            string path = Places.SaveFile();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            World.SaveFile.WriteFile(path, System.Text.Json.Nodes.JsonNode.Parse(state)!);
            WriteBack(SaveName); // which asks for a sync pass too
        }
        catch (System.Exception error) when (error is System.IO.IOException or System.UnauthorizedAccessException)
        {
            _hud.AddLog("Couldn't save: " + error.Message);
        }
    }

    private void LoadSave()
    {
        if (_world == null || _world.Fighting)
        {
            _hud.AddLog("Not in a fight.");
            return;
        }
        if (!System.IO.File.Exists(Places.SaveFile()))
        {
            _hud.AddLog("There is no save yet.");
            return;
        }
        _cutscene.Skip();
        if (!_world.Load(Places.SaveFile()))
        {
            _hud.AddLog(_world.Refusal);
        }
    }

    // New adventure from the character screen: the brought characters so far go home first, then
    // the chapter starts again with the seats as picked.
    private void Restart(System.Collections.Generic.List<PartyPick?> picks)
    {
        if (_world == null)
        {
            return;
        }
        _fromTitle = false;
        WriteBack();
        _world.SetParty(picks);
        // a screenshot run keeps its seed so the same script plays the same way
        ulong seed = ShotRunner.Running ? (ulong)_seed : Time.GetTicksUsec();
        _world.NewAdventure(seed);
        _clearedWritten = false;
        _camera.JumpTo(_world.Tokens.Tokens[0].Position.ToGodot(), 1);
    }

    // Each brought character's copy goes back to its library file: choices with the XP earned,
    // what it carries and its coins. The dead go to the graveyard. Ready-made heroes stay behind.
    // away names the save that still holds them, as the C++ client marks them; "" brings them home.
    private void WriteBack(string away = "")
    {
        if (_world == null || Playtest)
        {
            return;
        }
        string folder = Places.CharactersFolder();
        for (int i = 0; i < _world.HeroCount; i++)
        {
            WorldCreature hero = _world.Creatures[i];
            if (hero.Library.Length == 0 || _world.LibraryCopy(i) is not LibraryEntry copy)
            {
                continue;
            }
            try
            {
                if (CharacterLibrary.Find(folder, hero.Library) is not LibraryEntry entry || entry.Retired)
                {
                    continue;
                }
                entry.Choices = copy.Choices;
                entry.Inventory.Clear();
                entry.Inventory.AddRange(copy.Inventory);
                entry.Coins = copy.Coins;
                entry.Away = hero.Sheet.Death.Dead ? "" : away;
                CharacterLibrary.Write(folder, entry);
                if (hero.Sheet.Death.Dead)
                {
                    CharacterLibrary.Retire(folder, entry);
                }
            }
            catch (System.Exception error) when (error is ContentException or System.IO.IOException or System.InvalidOperationException)
            {
                GD.PushWarning($"Couldn't write {hero.Library} back: {error.Message}");
            }
        }
        App.FilesWritten();
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
        if (w.PartyWiped || w.Talk != null || _cutscene.Playing)
        {
            return; // the defeat panel, the conversation or the cutscene has the screen
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
        _pendingOpen = null;
        bool seen = w.Fog.State(w.ViewTeam(), 0, cell) != FogState.Unexplored;

        // anyone with something to say: walk up and talk (a merchant too, whose shop opens from
        // the talk, and a companion in the party)
        for (int creature = w.HeroCount; creature < w.Creatures.Count && seen; creature++)
        {
            Token token = w.Tokens.Tokens[creature];
            if (w.Talkable(creature) && token.Floor == 0 && at.DistanceTo(token.Position.ToGodot()) <= token.Radius)
            {
                if (!w.TalkTo(creature))
                {
                    Refuse(at);
                }
                return;
            }
        }

        // a chest or what the dead left: walk over, open a locked one first, then look inside
        int pile = w.Piles.FindIndex(p => !p.Empty && (thing != null && p.Object == thing.Id || p.Object == 0 && p.At == cell));
        if (pile >= 0 && seen)
        {
            bool walking = thing != null && w.Piles[pile].Object == thing.Id ? w.GoNear(hero, thing.Id) : GoBeside(w, hero, cell);
            if (!walking)
            {
                Refuse(at);
                return;
            }
            if (w.PileLocked(pile) && thing != null)
            {
                _pendingUse = (hero, thing.Id);
            }
            _pendingOpen = (hero, $"pile:{pile}");
            return;
        }

        if (thing != null && w.Usable(thing) && seen)
        {
            if (w.GoNear(hero, thing.Id))
            {
                _pendingUse = (hero, thing.Id);
            }
            else
            {
                Refuse(at);
            }
            return;
        }
        // the camera stays where the player put it; Home brings it back to the hero
        if (!w.Go(hero, cell))
        {
            Refuse(at);
        }
    }

    // Walks the hero onto a square next to cell (or keeps them there), the nearest one there is a way to.
    private static bool GoBeside(World w, int hero, Cell cell)
    {
        Cell at = w.CellOf(hero);
        if (System.Math.Abs(at.X - cell.X) <= 1 && System.Math.Abs(at.Y - cell.Y) <= 1)
        {
            return true;
        }
        Cell? best = null;
        float bestDistance = float.MaxValue;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                var beside = new Cell(cell.X + dx, cell.Y + dy);
                float distance = w.Grid.Distance(at, beside);
                if ((dx != 0 || dy != 0) && distance < bestDistance && w.CanGo(hero, beside))
                {
                    best = beside;
                    bestDistance = distance;
                }
            }
        }
        // what the dead left lies on a square anyone can stand on
        return best is Cell to ? w.Go(hero, to) : w.Go(hero, cell);
    }

    private static bool SourceOpen(World w, int hero, string source)
    {
        int index = int.Parse(source[5..]);
        return source.StartsWith("shop:") ? w.CanTrade(hero, index) : index < w.Piles.Count && !w.Piles[index].Empty && !w.PileLocked(index);
    }

    // What the gear panel asked for, done on the World; a refusal is said under the panel's entry.
    private void Order(ItemOrder order)
    {
        if (_world == null)
        {
            return;
        }
        World w = _world;
        bool done = order.Kind switch
        {
            ItemOrderKind.Equip => w.Equip(order.Hero, order.Item, true),
            ItemOrderKind.Unequip => w.Equip(order.Hero, order.Item, false),
            ItemOrderKind.Use => w.Consume(order.Hero, order.Item, order.Target),
            ItemOrderKind.Give => w.Give(order.Hero, order.Target, item: order.Item),
            ItemOrderKind.Take => w.Take(order.Hero, order.Pile, item: order.Item),
            ItemOrderKind.TakeCoins => w.Take(order.Hero, order.Pile, coins: true),
            ItemOrderKind.TakeAll => w.Take(order.Hero, order.Pile, all: true),
            ItemOrderKind.Buy => w.Buy(order.Hero, order.Npc, order.Item),
            ItemOrderKind.Sell => w.Sell(order.Hero, order.Npc, order.Item),
            _ => false,
        };
        if (!done)
        {
            _hud.Panels?.GearRefused(w.Refusal.Length > 0 ? w.Refusal : "Not now.");
        }
    }

    // What the spell panel asked for. Use now puts the panel away and picks the spell on the hotbar,
    // so it is aimed on the map like any other action.
    private void Order(SpellOrder order)
    {
        if (_world == null || _hud.Panels is not PlayHud panels)
        {
            return;
        }
        World w = _world;
        bool done = true;
        switch (order.Kind)
        {
            case SpellOrderKind.Prepare:
                done = w.Prepare(order.Hero, order.Prepared ?? System.Array.Empty<string>());
                break;
            case SpellOrderKind.Cast:
                done = w.Cast(order.Hero, order.Spell, order.Target);
                break;
            case SpellOrderKind.Aim:
                panels.TogglePanel("Spells");
                _fight.PickAction(order.Spell);
                break;
        }
        if (!done)
        {
            panels.SpellRefused(w.Refusal.Length > 0 ? w.Refusal : "Not now.");
        }
    }

    // Trade with the merchant being talked to: the talk ends and their shop opens.
    private void Trade()
    {
        if (_world?.Talk == null || _world.TalkingWith < 0 || _world.Creatures[_world.TalkingWith].Npc < 0)
        {
            return;
        }
        int npc = _world.Creatures[_world.TalkingWith].Npc;
        int hero = _world.LeaderIndex();
        _world.EndTalk();
        if (_world.CanTrade(hero, npc))
        {
            _hud.Panels?.OpenGear($"shop:{npc}");
        }
    }

    // What the camp panel asked for.
    private void Order(CampOrder order)
    {
        if (_world == null || _hud.Panels is not PlayHud panels)
        {
            return;
        }
        World w = _world;
        bool done = order.Kind switch
        {
            CampOrderKind.MakeCamp => w.MakeCamp(),
            CampOrderKind.LeaveCamp => w.LeaveCamp(),
            CampOrderKind.Rest => w.Rest(order.Rest),
            CampOrderKind.ToStash => w.ToStash(order.Hero, order.Item),
            CampOrderKind.FromStash => w.FromStash(order.Hero, order.Item),
            CampOrderKind.Revive => w.Revive(order.Hero, order.Target),
            _ => false,
        };
        if (!done)
        {
            panels.CampRefused(w.Refusal.Length > 0 ? w.Refusal : "Not now.");
        }
    }

    private void Refuse(Vector2 at)
    {
        if (_world != null && _world.Refusal.Length > 0)
        {
            _floaters.Add(at, _world.Refusal, Palette.Ash);
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
                    if (_world.Chapter.Cutscenes.TryGetValue(e.Text, out Cutscene? cutscene))
                    {
                        _hud.Panels?.TogglePanel(""); // closes whatever panel was open
                        _cutscene.Play(cutscene, _camera);
                    }
                    else
                    {
                        _world.EndCutscene(); // Chapter.Load checks them, so only a broken setup lands here
                    }
                    break;
                case WorldEventKind.Talk:
                    if (_hud.Panels is PlayHud talking && talking.OpenPanel.Length > 0)
                    {
                        talking.TogglePanel(talking.OpenPanel);
                    }
                    break;
                case WorldEventKind.ChapterChanged:
                    ShowChapter(_world);
                    break;
                case WorldEventKind.Resumed:
                    ShowChapter(_world);
                    _hud.Banner(e.Text, 2);
                    _hud.AddLog(e.Text);
                    _fight.FightEnded();
                    break;
                case WorldEventKind.Save:
                    WriteSave(e.Text); // a screenshot run writes its own under ../.dev
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
            return (Palette.Straw, 1.6f);
        }
        if (text.Length > 0 && char.IsDigit(text[0]))
        {
            return (Palette.Red, 1.5f);
        }
        if (text.Length > 1 && text[0] == '+' && char.IsDigit(text[1]))
        {
            return (Palette.Leaf, 1.4f);
        }
        if (text is "Miss" or "Saved" or "Failed")
        {
            return (Palette.Bone, 1.15f);
        }
        return (Palette.Sky, 1);
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
        if (_hud.Panels != null)
        {
            _hud.Panels.Visible = !_cutscene.Playing; // a cutscene has the whole screen
            _hud.Panels.Refresh(_world, _fight.Aim);
        }
    }
}
