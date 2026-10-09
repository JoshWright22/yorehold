using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Encounters mode of Create, a layout over an EncountersEditor like the C++ client's: the tools and
/// the groups on the left, the chapter's map in the middle, the picked group or creature on the
/// right. Left button picks, drags and places, right button removes, middle drags the view and the
/// wheel zooms. Typing in a box is one undo step until the box is left.
/// </summary>
public partial class EncountersModePanel : HBoxContainer
{
    // one per group, round again after the last
    private static readonly Color[] GroupColors = { Palette.Red, Palette.Blue, Palette.Amber, Palette.Orchid, Palette.Leaf, Palette.Sky };

    // the eight ways on the map, with "toward the party" in the middle
    private static readonly (string Name, double Degrees)[] Ways =
    {
        ("NW", 225), ("N", 270), ("NE", 315), ("W", 180), ("Party", -1), ("E", 0), ("SW", 135), ("S", 90), ("SE", 45),
    };

    private EncountersEditor? _editor;
    private ToolColumn _tools = null!;
    private ToolColumn _props = null!;
    private EditorMapView _view = null!;
    private Label _error = null!;

    private bool _placing;
    private int _group;
    private int? _creature;
    private string _kind = "";
    private string _lootItem = "";
    private bool _dragging;
    private string _hint = "";
    private Func<ContentFiles?> _files = () => null;
    // a fight played out many times, in the background so the screen keeps drawing
    private Task<FightForecast>? _forecast;
    private string _forecastOf = "";
    // the same, taking foes out until the fight is no longer too hard
    private Task<(int LeaveOut, FightForecast Forecast)>? _fit;
    private string _fitOf = "";
    // the other way: more of the last foe for a fight that is too easy
    private Task<(int More, FightForecast Forecast, List<Cell> At)>? _grow;
    private string _growOf = "";
    // every fight of the chapter fitted in turn, by fight id, and how far it has got
    private Task<List<(string Id, int LeaveOut, FightForecast Forecast)>>? _fitAll;
    private int _fitAllDone;
    private int _fitAllOf;
    /// <summary>How many times each try of a fit plays the fight; fewer than a forecast, as it tries several.</summary>
    private const int FitFights = 8;

    /// <summary>How many times Forecast plays a fight.</summary>
    private const int ForecastFights = 20;

    public EditorMapView View => _view;
    /// <summary>The chapter folder the groups belong to, for the forecast to load.</summary>
    public string Chapter { get; set; } = "";
    /// <summary>Nothing is waiting to be saved: the forecast plays the saved files.</summary>
    public bool Saved { get; set; } = true;

    public static Color ColorOf(int group) => GroupColors[group % GroupColors.Length];

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 0);
        _tools = Column(172);
        _view = new EditorMapView { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(_view);
        _props = Column(252);
        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        _view.AddChild(_error);
        _error.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide, LayoutPresetMode.Minsize, 16);
        _view.Pressed += Press;
        _view.Released += Release;
        _view.DrawOver += DrawMarks;
    }

    /// <summary>The chapter's groups and its map as drawn now, or null with why. Called every frame.</summary>
    public void Present(EncountersEditor? editor, GameMap? map, string error, Func<ContentFiles?> files)
    {
        _files = files;
        if (!ReferenceEquals(editor, _editor))
        {
            _editor = editor;
            _group = 0;
            _creature = null;
            _dragging = false;
            _view.Fit();
            _tools.Invalidate();
            _props.Invalidate();
        }
        _error.Visible = editor == null || map == null;
        _error.Text = error.Length > 0 ? error : "This package has no chapter to place creatures in.";
        if (editor == null || map == null)
        {
            return;
        }

        // an undo can take away the group or creature that was picked
        if (_group >= editor.Groups.Count)
        {
            _group = Math.Max(0, editor.Groups.Count - 1);
            _creature = null;
        }
        if (_creature is int c && (editor.Groups.Count == 0 || c >= editor.Groups[_group].Creatures.Count))
        {
            _creature = null;
        }
        if (_creature == null)
        {
            _dragging = false;
        }
        if (!editor.Names.Creatures.ContainsKey(_kind))
        {
            _kind = editor.Names.Creatures.Keys.FirstOrDefault() ?? "";
        }
        if (!editor.Names.Items.ContainsKey(_lootItem))
        {
            _lootItem = editor.Names.Items.Keys.FirstOrDefault() ?? "";
        }
        _view.Floor = 0; // creatures stand on floor 0
        _view.ShowMap(map, files);
        _view.QueueRedraw();

        _tools.Build(string.Join("|", editor.Groups.Select(g => g.Id + "=" + g.Creatures.Count)), BuildTools);
        string props = $"{_placing}|{_group}|{_creature}|{editor.Groups.Count}|{(editor.Groups.Count > 0 ? editor.Groups[_group].Creatures.Count : 0)}|"
            + $"{(editor.Groups.Count > 0 ? string.Join(",", editor.Groups[_group].Loot.Items) : "")}|{editor.Names.Creatures.Count}|{editor.Names.Items.Count}";
        _props.Build(props, BuildProps);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_editor != null && IsVisibleInTree() && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete } && _creature is int c && !_dragging)
        {
            _editor.RemoveCreature(_group, c);
            _creature = null;
            GetViewport().SetInputAsHandled();
        }
    }

    private ToolColumn Column(float width)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(panel);
        var box = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(box);
        var column = new ToolColumn { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddChild(column);
        return column;
    }

    private void BuildTools()
    {
        if (_editor == null)
        {
            return;
        }
        EncountersEditor editor = _editor;
        HBoxContainer tools = _tools.Row();
        _tools.Toggle("Select", () => !_placing, () => _placing = false, tools, "TabButton").Alignment = HorizontalAlignment.Center;
        _tools.Toggle("Place", () => _placing, () => _placing = true, tools, "TabButton").Alignment = HorizontalAlignment.Center;
        _tools.Gap();
        _tools.Heading("Fights");
        for (int g = 0; g < editor.Groups.Count; g++)
        {
            int group = g;
            _tools.Toggle($"{editor.Groups[g].Id}  ({editor.Groups[g].Creatures.Count})", () => _group == group, () =>
            {
                // picking a group shows the group, not whoever was picked in it
                _group = group;
                _creature = null;
                _hint = "";
            }).Icon = MapModePanel.Swatch(ColorOf(g));
        }
        if (editor.Groups.Count == 0)
        {
            _tools.Dim("None yet.");
        }
        HBoxContainer row = _tools.Row();
        _tools.Act("Add", () =>
        {
            if (editor.AddGroup() is int added)
            {
                _group = added;
                _creature = null;
            }
        }, null, row);
        _tools.Act("Remove", () =>
        {
            editor.RemoveGroup(_group);
            _creature = null;
        }, () => editor.Groups.Count > 0, row);
        // an imported adventure's fights, all at once: each played with fewer foes until it fits
        _tools.Act("Fit every fight", () => StartFitAll(editor), () => editor.Groups.Count > 0 && (_fitAll == null || _fitAll.IsCompleted));
        _tools.Live(FitAllLine);
        _tools.Act(() => $"Take out {FitAllCuts()?.Sum(c => c.LeaveOut) ?? 0}", () => ApplyFitAll(editor), () => FitAllCuts()?.Any(c => c.LeaveOut > 0) == true);
        _tools.Gap();
        _tools.Toggle("Grid", () => _view.Grid, () => _view.Grid = !_view.Grid, null, "ChipButton");
        _tools.Act("Fit map", _view.Fit);
        _tools.Live(() => _view.Hover is Cell at ? $"at {at.X}, {at.Y}" : "");
        _tools.Gap();
        _tools.Dim("Left: pick, drag, place\nRight: remove\nMiddle: move\nWheel: zoom");
    }

    private void BuildProps()
    {
        if (_editor == null)
        {
            return;
        }
        if (_placing)
        {
            BuildPalette(_editor);
        }
        else if (_creature is int creature)
        {
            BuildCreature(_editor, creature);
        }
        else
        {
            BuildGroup(_editor);
        }
        _props.Gap();
        _props.Live(() => _hint, "WarnLabel");
    }

    private void BuildPalette(EncountersEditor editor)
    {
        _props.Heading("Place");
        _props.Live(() => editor.Groups.Count == 0 ? "into a new fight" : "into " +editor.Groups[Math.Min(_group, editor.Groups.Count - 1)].Id);
        if (editor.Names.Creatures.Count == 0)
        {
            _props.Dim("No creatures in this package.");
            return;
        }
        foreach ((string id, EncountersEditor.Catalog.Creature creature) in editor.Names.Creatures)
        {
            _props.Toggle($"{creature.Name}   L{creature.Level}", () => _kind == id, () => _kind = id).Icon
                = MapModePanel.Swatch(Palette.Nearest(Color.Color8(creature.Color.R, creature.Color.G, creature.Color.B)));
        }
    }

    private void BuildGroup(EncountersEditor editor)
    {
        if (editor.Groups.Count == 0)
        {
            _props.Heading("No fights yet");
            _props.Dim("Pick Place, then click the map. The first creature makes the first fight; more join it.");
            return;
        }
        int group = _group;
        EncountersEditor.Group Now() => editor.Groups[Math.Min(group, editor.Groups.Count - 1)];

        _props.Heading("Fight").AddThemeColorOverride("font_color", ColorOf(group));
        _props.Dim("Its name, for the story and triggers");
        _props.Field(() => Now().Id, typed =>
        {
            if (typed != Now().Id && !editor.SetGroupId(group, typed))
            {
                _hint = typed.Length == 0 ? "A group needs an id." : "Another group has that id.";
            }
            else
            {
                _hint = "";
            }
        }, editor.EndTyping, "id");
        _props.Dim("What the game says as it starts");
        _props.Field(() => Now().Text, typed => editor.SetGroupText(group, typed), editor.EndTyping, "nothing");
        _props.Dim("Story flags it sets when won, for a door or a conversation to wait for (comma between)");
        _props.Field(() => string.Join(", ", Now().Set), typed =>
        {
            List<string> flags = FlagsFrom(typed);
            if (!flags.SequenceEqual(Now().Set) && !editor.SetGroupFlags(group, flags))
            {
                _hint = "Flags are 1 to 64 characters.";
            }
        }, editor.EndTyping, "none");
        _props.Dim("How they fight");
        Stepper(() => EncountersEditor.ProfileOf(Now().Ai) is { Length: > 0 } p ? p : "each one's own",
            step => editor.SetGroupAi(group, StepName(Profiles(editor), EncountersEditor.ProfileOf(Now().Ai), step)));

        HBoxContainer heads = _props.Row();
        _props.Dim("XP for this fight", heads).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _props.Dim("XP for any other", heads).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        HBoxContainer xp = _props.Row();
        _props.Field(() => Now().Xp?.ToString(CultureInfo.InvariantCulture) ?? "", typed =>
        {
            bool fine = typed.Length == 0 ? Now().Xp == null || editor.SetGroupXp(group, null)
                : int.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && (value == Now().Xp || editor.SetGroupXp(group, value));
            _hint = fine ? "" : "XP is a whole number.";
        }, editor.EndTyping, "chapter's", xp);
        _props.Field(() => editor.ChapterXp.ToString(CultureInfo.InvariantCulture), typed =>
        {
            bool fine = int.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && (value == editor.ChapterXp || editor.SetChapterXp(value));
            _hint = fine ? "" : "XP is a whole number.";
        }, editor.EndTyping, "0", xp);
        _props.Act(() => $"Use {editor.ProposedXp(group)}, from the foes' levels",() => editor.SetGroupXp(group, editor.ProposedXp(group)),
            () => Now().Xp != editor.ProposedXp(group) && Now().Creatures.Count > 0);
        _props.Gap();

        _props.Heading("Forecast");
        _props.Dim($"Played out {ForecastFights} times by the AI, with the party the chapter starts with");
        _props.Live(() => ForecastLine(Now().Id));
        _props.Act("Play it out", () => StartForecast(group, Now().Id), () => Now().Creatures.Count > 0 && (_forecast == null || _forecast.IsCompleted));
        _props.Act("Fit to the party", () => StartFit(group, Now().Id), () => Now().Creatures.Count > 1 && (_fit == null || _fit.IsCompleted));
        _props.Live(() => FitLine(Now().Id));
        _props.Act(() => FitDone(Now().Id) is int fewer && fewer > 0 ? $"Take the last {fewer} out" : "Take them out", () =>
        {
            // the last ones first, as the fit left them out
            if (FitDone(Now().Id) is int fewer)
            {
                for (int i = 0; i < fewer && Now().Creatures.Count > 1; i++)
                {
                    editor.RemoveCreature(group, Now().Creatures.Count - 1);
                }
                _fit = null;
            }
        }, () => FitDone(Now().Id) is > 0);
        _props.Act("Too easy: try more foes", () => StartGrow(group, Now().Id), () => Now().Creatures.Count > 0 && (_grow == null || _grow.IsCompleted));
        _props.Live(() => GrowLine(Now().Id));
        _props.Act(() => GrowDone(Now().Id) is { More: > 0 } grown ? $"Add {grown.More} more {Now().Creatures[^1].Creature}" : "Add them", () =>
        {
            if (GrowDone(Now().Id) is { More: > 0 } grown && editor.AddCreatures(group, Now().Creatures[^1].Creature, grown.At))
            {
                _grow = null;
            }
            else
            {
                _hint = "Those squares aren't free any more; try again.";
            }
        }, () => GrowDone(Now().Id) is { More: > 0 });
        _props.Gap();

        _props.Heading("Loot");
        _props.Dim("Coins dropped, as dice like 2d6");
        _props.Field(() => Now().Loot.Coins, typed =>
        {
            var loot = new LootTable { Coins = typed.Trim(), Items = Now().Loot.Items };
            bool fine = typed.Trim() == Now().Loot.Coins || editor.SetGroupLoot(group, loot);
            _hint = fine ? "" : "Coins are dice, like 2d6.";
        }, editor.EndTyping, "none");
        // a click on the list is its own undo step, apart from any typing before or after it
        void Click(LootTable loot)
        {
            editor.EndTyping();
            editor.SetGroupLoot(group, loot);
            editor.EndTyping();
        }
        List<LootEntry> items = Now().Loot.Items;
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            LootEntry entry = items[i];
            HBoxContainer line = _props.Row();
            var name = new Label { Text = editor.Names.Items.GetValueOrDefault(entry.Item, entry.Item), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
            line.AddChild(name);
            // the chance steps down and comes round again; so does how many
            _props.Act($"{Math.Round(entry.Chance * 100)}%", () =>
            {
                double lower = new[] { 0.75, 0.5, 0.25, 0.1 }.FirstOrDefault(c => c < entry.Chance - 0.001, 1);
                Click(With(Now().Loot, index, entry with { Chance = lower }));
            }, null, line).CustomMinimumSize = new Vector2(52, 24);
            _props.Act($"x{entry.Quantity}", () =>
            {
                int more = new[] { 2, 3, 5, 10 }.FirstOrDefault(q => q > entry.Quantity, 1);
                Click(With(Now().Loot, index, entry with { Quantity = more }));
            }, null, line).CustomMinimumSize = new Vector2(40, 24);
            _props.Act("x", () =>
            {
                var left = Now().Loot.Items.ToList();
                left.RemoveAt(index);
                Click(new LootTable { Coins = Now().Loot.Coins, Items = left });
            }, null, line).CustomMinimumSize = new Vector2(26, 24);
            foreach (Button b in line.GetChildren().OfType<Button>())
            {
                b.SizeFlagsHorizontal = SizeFlags.Fill;
            }
        }
        if (editor.Names.Items.Count == 0)
        {
            _props.Dim("No items in this package.");
            return;
        }
        Stepper(() => editor.Names.Items.GetValueOrDefault(_lootItem, _lootItem), step => _lootItem = StepName(editor.Names.Items.Keys.ToList(), _lootItem, step));
        _props.Act("Add to loot", () => Click(new LootTable { Coins = Now().Loot.Coins, Items = Now().Loot.Items.Append(new LootEntry(_lootItem)).ToList() }));
    }

    private void StartForecast(int group, string id)
    {
        if (!Saved)
        {
            _hint = "Save first: the forecast plays the saved chapter.";
            return;
        }
        if (_files() is not ContentFiles files || Chapter.Length == 0)
        {
            return;
        }
        _hint = "";
        string chapter = Chapter;
        _forecastOf = id;
        // the rules have no engine types, so a world can be played on another thread
        _forecast = Task.Run(() => FightSimulation.Forecast(seed => World.Load(files, chapter, seed), group, ForecastFights));
    }

    private void StartFit(int group, string id)
    {
        if (!Saved)
        {
            _hint = "Save first: the fit plays the saved chapter.";
            return;
        }
        if (_files() is not ContentFiles files || Chapter.Length == 0)
        {
            return;
        }
        _hint = "";
        string chapter = Chapter;
        _fitOf = id;
        _fit = Task.Run(() => FightSimulation.Fit(seed => World.Load(files, chapter, seed), group, FitFights));
    }

    private void StartFitAll(EncountersEditor editor)
    {
        if (!Saved)
        {
            _hint = "Save first: the fit plays the saved chapter.";
            return;
        }
        if (_files() is not ContentFiles files || Chapter.Length == 0)
        {
            return;
        }
        _hint = "";
        string chapter = Chapter;
        List<string> ids = editor.Groups.Select(g => g.Id).ToList();
        _fitAllDone = 0;
        _fitAllOf = ids.Count;
        _fitAll = Task.Run(() =>
        {
            var cuts = new List<(string, int, FightForecast)>();
            for (int g = 0; g < ids.Count; g++)
            {
                (int leaveOut, FightForecast forecast) = FightSimulation.Fit(seed => World.Load(files, chapter, seed), g, FitFights);
                cuts.Add((ids[g], leaveOut, forecast));
                System.Threading.Interlocked.Increment(ref _fitAllDone);
            }
            return cuts;
        });
    }

    private List<(string Id, int LeaveOut, FightForecast Forecast)>? FitAllCuts() => _fitAll is { IsCompletedSuccessfully: true } ? _fitAll.Result : null;

    private string FitAllLine()
    {
        if (_fitAll == null)
        {
            return "";
        }
        if (!_fitAll.IsCompleted)
        {
            return $"Fitting {Math.Min(_fitAllDone + 1, _fitAllOf)} of {_fitAllOf}...";
        }
        if (_fitAll.IsFaulted)
        {
            return "Could not fit them: " + (_fitAll.Exception?.InnerException?.Message ?? "unknown");
        }
        List<(string Id, int LeaveOut, FightForecast Forecast)> cuts = _fitAll.Result;
        int over = cuts.Count(c => c.LeaveOut > 0);
        return over == 0 ? "Every fight fits the party." : $"{over} of {cuts.Count} too hard: " + string.Join(", ", cuts.Where(c => c.LeaveOut > 0).Select(c => $"{c.Id} -{c.LeaveOut}"));
    }

    // The last foes of each fight the fit left out, taken out, as one change per fight.
    private void ApplyFitAll(EncountersEditor editor)
    {
        foreach ((string id, int leaveOut, _) in FitAllCuts() ?? new())
        {
            int group = editor.Groups.ToList().FindIndex(g => g.Id == id);
            for (int i = 0; group >= 0 && i < leaveOut && editor.Groups[group].Creatures.Count > 1; i++)
            {
                editor.RemoveCreature(group, editor.Groups[group].Creatures.Count - 1);
            }
        }
        _fitAll = null;
    }

    // How many foes the finished fit for this group leaves out; null while none is ready.
    private int? FitDone(string id) => _fit is { IsCompletedSuccessfully: true } && id == _fitOf ? _fit.Result.LeaveOut : null;

    private void StartGrow(int group, string id)
    {
        if (!Saved)
        {
            _hint = "Save first: the fit plays the saved chapter.";
            return;
        }
        if (_files() is null || Chapter.Length == 0)
        {
            return;
        }
        _hint = "";
        string chapter = Chapter;
        Func<ContentFiles?> files = _files;
        string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "yorehold-grow");
        _growOf = id;
        _grow = Task.Run(() => FightSimulation.Grow(() => files()!, chapter, group, FitFights, scratch));
    }

    private (int More, FightForecast Forecast, List<Cell> At)? GrowDone(string id) =>
        _grow is { IsCompletedSuccessfully: true } && id == _growOf ? _grow.Result : null;

    private string GrowLine(string id)
    {
        if (_grow == null || id != _growOf)
        {
            return "";
        }
        if (!_grow.IsCompleted)
        {
            return "Playing it with more foes...";
        }
        if (_grow.IsFaulted)
        {
            return "Could not try it: " + (_grow.Exception?.InnerException?.Message ?? "unknown");
        }
        (int more, FightForecast result, _) = _grow.Result;
        string with = more == 0 ? "Not too easy as it is" : $"With {more} more";
        return result.Verdict() is { Length: > 0 } verdict ? $"{with}: {result.Summary()}\n{verdict}" : $"{with}: {result.Summary()}";
    }

    private string FitLine(string id)
    {
        if (_fit == null || id != _fitOf)
        {
            return "";
        }
        if (!_fit.IsCompleted)
        {
            return "Fitting: playing it with fewer foes...";
        }
        if (_fit.IsFaulted)
        {
            return "Could not fit it: " + (_fit.Exception?.InnerException?.Message ?? "unknown");
        }
        (int fewer, FightForecast result) = _fit.Result;
        string with = fewer == 0 ? "It fits as it is" : $"With the last {fewer} out";
        return result.Verdict() is { Length: > 0 } verdict ? $"{with}: {result.Summary()}\n{verdict}" : $"{with}: {result.Summary()}";
    }

    private string ForecastLine(string id)
    {
        if (_forecast == null || id != _forecastOf)
        {
            return "Not played yet.";
        }
        if (!_forecast.IsCompleted)
        {
            return "Playing...";
        }
        if (_forecast.IsFaulted)
        {
            return "Could not play it: " + (_forecast.Exception?.InnerException?.Message ?? "unknown");
        }
        FightForecast result = _forecast.Result;
        string line = result.Unfinished > 0 ? $"{result.Summary()}; {result.Unfinished} never ended" : result.Summary();
        return result.Verdict() is { Length: > 0 } verdict ? $"{line}\n{verdict}" : line;
    }

    private void BuildCreature(EncountersEditor editor, int index)
    {
        int group = _group;
        EncountersEditor.Placement Now() => editor.Groups[group].Creatures[Math.Min(index, editor.Groups[group].Creatures.Count - 1)];
        EncountersEditor.Placement p = Now();
        bool known = editor.Names.Creatures.TryGetValue(p.Creature, out EncountersEditor.Catalog.Creature? look);

        _props.Heading(known ? look!.Name : p.Creature);
        if (known)
        {
            _props.Live(() => $"Level {look!.Level}, at {Now().At.X}, {Now().At.Y}");
        }
        else
        {
            _props.Live(() => "Not in this package", "WarnLabel");
        }
        _props.Dim("Name");
        _props.Field(() => Now().Name, typed => editor.SetCreatureName(group, index, typed), editor.EndTyping, look?.Name ?? p.Creature);
        _props.Dim("In the fight");
        Stepper(() => editor.Groups[group].Id, step =>
        {
            int count = editor.Groups.Count;
            if (count < 2)
            {
                return;
            }
            int to = ((group + step) % count + count) % count;
            if (editor.MoveToGroup(group, index, to) is int moved)
            {
                _group = to;
                _creature = moved;
            }
        });
        _props.Live(() => Now().Facing is double f ? $"Looks {Math.Round(f)} degrees" : "Looks toward the party");
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 3);
        _props.AddChild(grid);
        foreach ((string name, double degrees) in Ways)
        {
            double? way = degrees < 0 ? null : degrees;
            _props.Toggle(name, () => Now().Facing == way, () => editor.SetFacing(group, index, way), grid, "TabButton").Alignment = HorizontalAlignment.Center;
        }
        _props.Dim("How it fights");
        Stepper(() => EncountersEditor.ProfileOf(Now().Ai) is { Length: > 0 } profile ? profile : "as the fight says",
            step => editor.SetCreatureAi(group, index, StepName(Profiles(editor), EncountersEditor.ProfileOf(Now().Ai), step)));
        _props.Gap();
        _props.Act("Remove (Del)", () =>
        {
            editor.RemoveCreature(group, index);
            _creature = null;
        });
    }

    // "<  text  >": the arrows step through a list.
    private void Stepper(Func<string> text, Action<int> step)
    {
        HBoxContainer row = _props.Row();
        _props.Act("<", () => step(-1), null, row).CustomMinimumSize = new Vector2(28, 26);
        Label shown = _props.Live(text, "", row);
        shown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        shown.HorizontalAlignment = HorizontalAlignment.Center;
        _props.Act(">", () => step(1), null, row).CustomMinimumSize = new Vector2(28, 26);
        foreach (Button b in row.GetChildren().OfType<Button>())
        {
            b.SizeFlagsHorizontal = SizeFlags.Fill;
        }
    }

    private static LootTable With(LootTable loot, int index, LootEntry entry)
    {
        var items = loot.Items.ToList();
        items[index] = entry;
        return new LootTable { Coins = loot.Coins, Items = items };
    }

    // The AI profiles an entry can be given, with "" (none) first.
    private static List<string> Profiles(EncountersEditor editor) => new[] { "" }.Concat(editor.Names.Ai).ToList();

    // The name step places along from current in names, round at the ends. One that isn't there counts as the first.
    private static string StepName(IReadOnlyList<string> names, string current, int step)
    {
        if (names.Count == 0)
        {
            return current;
        }
        int index = Math.Max(0, names.ToList().IndexOf(current));
        return names[((index + step) % names.Count + names.Count) % names.Count];
    }

    // "a, b" to its names, blanks dropped.
    private static List<string> FlagsFrom(string text) => text.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToList();

    private void Press(Cell at, MouseButton button)
    {
        if (_editor == null)
        {
            return;
        }
        (int Group, int Index)? under = _editor.CreatureAt(at);
        if (button == MouseButton.Right)
        {
            if (under is (int g, int c))
            {
                _editor.RemoveCreature(g, c);
                _creature = null;
                _dragging = false;
            }
            return;
        }
        _hint = "";
        if (under is (int group, int index))
        {
            _group = group;
            _creature = index;
            _dragging = true;
        }
        else if (!_placing)
        {
            _creature = null;
        }
        else if (_kind.Length == 0)
        {
            _hint = "This package has no creatures.";
        }
        else
        {
            // the first creature of a chapter makes its first group
            if (_editor.Groups.Count == 0)
            {
                _group = _editor.AddGroup() ?? 0;
            }
            _creature = _editor.AddCreature(_group, _kind, at);
            if (_creature == null)
            {
                _hint = "Nobody can stand there.";
            }
        }
    }

    private void Release(Cell? at, MouseButton button)
    {
        if (!_dragging || button != MouseButton.Left || _editor == null)
        {
            return;
        }
        // let go: the creature goes where the pointer is, if it can stand there
        _dragging = false;
        if (_creature is int c && at is Cell to && to != _editor.Groups[_group].Creatures[c].At)
        {
            _hint = _editor.MoveCreature(_group, c, to) ? "" : "Nobody can stand there.";
        }
    }

    private void DrawMarks(EditorMapView view)
    {
        if (_editor == null)
        {
            return;
        }
        float cell = GameMap.CellSize * view.Zoom;
        foreach (EncountersEditor.Fixed other in _editor.FixedOnes)
        {
            Rect2 square = view.CellRect(other.At);
            Color color = Palette.Nearest(Color.Color8(other.Color.R, other.Color.G, other.Color.B));
            if (other.Kind == EncountersEditor.FixedKind.Chest)
            {
                var box = new Rect2(square.Position + new Vector2(0.25f, 0.3f) * cell, new Vector2(0.5f, 0.4f) * cell);
                view.DrawRect(box, Palette.Amber);
                view.DrawRect(box, Palette.Ink, false, 2);
                continue;
            }
            // heroes and NPCs are squares, so nobody takes them for something to fight
            Rect2 body = square.Grow(-cell * 0.2f);
            view.DrawRect(body, color);
            view.DrawRect(body, Palette.Ink, false, 2);
        }

        IReadOnlyList<EncountersEditor.Group> groups = _editor.Groups;
        for (int g = 0; g < groups.Count; g++)
        {
            for (int c = 0; c < groups[g].Creatures.Count; c++)
            {
                EncountersEditor.Placement p = groups[g].Creatures[c];
                bool known = _editor.Names.Creatures.TryGetValue(p.Creature, out EncountersEditor.Catalog.Creature? look);
                float radius = (float)(known ? Math.Clamp(look!.Size, 0.2, 0.5) : 0.36) * cell;
                Vector2 middle = view.CellCentre(p.At);
                // the ring says which group it wakes with; the picked group's is thicker, and never thinner than a few pixels
                float ring = g == _group ? Math.Max(cell * 0.12f, 4) : Math.Max(cell * 0.07f, 2.5f);
                view.DrawCircle(middle, radius + ring, ColorOf(g));
                view.DrawCircle(middle, radius, known ? Palette.Nearest(Color.Color8(look!.Color.R, look.Color.G, look.Color.B)) : Palette.Red);
                // where it looks: solid if the writer set it, faint if it only watches for the party
                double angle = _editor.FacingOf(p) * Math.PI / 180;
                Vector2 tip = middle + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * cell * 0.8f;
                view.DrawLine(middle, tip, Palette.Ink, 5);
                view.DrawLine(middle, tip, p.Facing != null ? Palette.Bone : Palette.Faded(Palette.Bone, 0.45f), 3);
                if (g == _group && _creature == c)
                {
                    view.DrawRect(view.CellRect(p.At), Palette.Straw, false, 3);
                }
            }
        }

        if (view.Hover is Cell hover)
        {
            // red where nobody could be put: while dragging one, and with Place
            bool placing = _dragging || _placing;
            bool own = _dragging && _creature is int dragged && groups[_group].Creatures[dragged].At == hover;
            bool blocked = placing && !_editor.Free(hover) && !own;
            view.DrawRect(view.CellRect(hover), blocked ? Palette.Red : Palette.Bone, false, 2);
        }

        // names, at screen size whatever the zoom
        if (cell >= 14)
        {
            foreach (EncountersEditor.Fixed other in _editor.FixedOnes)
            {
                view.Words(view.CellRect(other.At).Position + new Vector2(0, cell * 0.85f + 12), other.Name, Palette.Ash, 12);
            }
            for (int g = 0; g < groups.Count; g++)
            {
                foreach (EncountersEditor.Placement p in groups[g].Creatures)
                {
                    string name = p.Name.Length > 0 ? p.Name : _editor.Names.Creatures.TryGetValue(p.Creature, out EncountersEditor.Catalog.Creature? look) ? look.Name : p.Creature;
                    view.Words(view.CellRect(p.At).Position + new Vector2(0, cell * 0.95f + 12), name, ColorOf(g), 12);
                }
            }
        }
    }
}
