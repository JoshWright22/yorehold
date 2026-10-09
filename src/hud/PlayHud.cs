using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>A hotbar slot changed by a drag: the hero's slot now holds Action, or nothing when it is "".</summary>
public sealed record HotbarEdit(int Hero, int Slot, string Action);

/// <summary>
/// The panels over the map, laid out like Baldur's Gate 3: party portraits down the left, the
/// hotbar with the selected hero's portrait and pips along the bottom, the menu down the right
/// edge and the log in the bottom right always, and in a fight the turn order along the top, End
/// Turn beside the hotbar, the reaction prompt and the tooltip. It shows what the World says and
/// raises an event when something is pressed; it never acts on the World itself.
/// </summary>
public partial class PlayHud : Control
{
    [Export] public PackedScene? SlotScene { get; set; }
    [Export] public PackedScene? PartyCardScene { get; set; }
    [Export] public PackedScene? InitiativeCardScene { get; set; }
    /// <summary>Where the log's bottom edge rests, in pixels above the screen's: clear of the hotbar, in the corner when the hotbar is away.</summary>
    [Export] public float LogAboveHotbar { get; set; } = 166;
    [Export] public float LogAboveEdge { get; set; } = 10;

    public event Action<string>? ActionPressed;
    public event Action? EndTurnPressed;
    /// <summary>A die to roll in the open: its sides, and the hero shown on the bar.</summary>
    public event Action<int, int>? DieRolled;
    /// <summary>A party card or a card in the turn order.</summary>
    public event Action<int>? CreaturePressed;
    /// <summary>True for use it, false for pass.</summary>
    public event Action<bool>? ReactionAnswered;
    public event Action<int, string, bool>? ReactionHeld;
    /// <summary>A button on the menu beside the hotbar, by its node name ("Characters").</summary>
    public event Action<string>? MenuPressed;
    /// <summary>Something pressed on the gear panel.</summary>
    public event Action<ItemOrder>? ItemOrdered;
    /// <summary>Something pressed on the spell panel.</summary>
    public event Action<SpellOrder>? SpellOrdered;
    /// <summary>Something pressed on the camp panel.</summary>
    public event Action<CampOrder>? CampOrdered;
    /// <summary>A reply in the conversation, by its place among those on offer.</summary>
    public event Action<int>? ReplyPressed;
    /// <summary>The wiped party goes back to the autosave.</summary>
    public event Action? BackPressed;
    /// <summary>Trade, under a merchant's replies.</summary>
    public event Action? TradePressed;
    /// <summary>An action dragged onto a hotbar slot, or off one (Action "").</summary>
    public event Action<HotbarEdit>? HotbarChanged;

    // the scene has all of these
    private VBoxContainer _party = null!;
    private Label _partyCount = null!;
    private bool? _barLow;
    private Control _top = null!;
    private Label _round = null!;
    private HBoxContainer _cards = null!;
    private Label _turn = null!;
    private Control _bottom = null!;
    private PortraitView _portrait = null!;
    private ProgressBar _hp = null!;
    private Label _hpText = null!;
    private PipsView _actions = null!;
    private Label _bonusLabel = null!;
    private PipsView _bonus = null!;
    private PipsView _reactionPip = null!;
    private MoveBarView _move = null!;
    private Label _moveText = null!;
    private GridContainer _slots = null!;
    private Button _endTurn = null!;
    private LogPanel _log = null!;
    private Control _reaction = null!;
    private Label _reactionTitle = null!;
    private Label _reactionText = null!;
    private ProgressBar _reactionTime = null!;
    private Control _defeat = null!;
    private Label _defeatText = null!;
    private Control _tip = null!;
    private Label _tipTitle = null!;
    private Label _tipMeta = null!;
    private Label _tipBody = null!;
    private Label _tipWarning = null!;
    private Label _cursor = null!;
    private SheetPanel _sheet = null!;
    private Button _sheetButton = null!;
    private DataPanel _gearView = null!;
    private Button _gearButton = null!;
    private GearPanel _gear = null!;
    private DataPanel _spellsView = null!;
    private Button _spellsButton = null!;
    private SpellPanel _spells = null!;
    private DataPanel _journalView = null!;
    private Button _journalButton = null!;
    private JournalPanel _journal = null!;
    private DataPanel _campView = null!;
    private Button _campButton = null!;
    private CampPanel _camp = null!;
    private Control _talk = null!;
    private Control _talkColumn = null!;
    private Control _talkPlate = null!;
    private PortraitView _speakerView = null!;
    private PortraitView _listenerView = null!;
    private Label _talkSpeaker = null!;
    private Label _talkText = null!;
    private VBoxContainer _replies = null!;
    private string _talkShown = "";

    private readonly List<PartyCard> _partyCards = new();
    private readonly List<InitiativeCard> _orderCards = new();
    private readonly List<ActionSlot> _slotViews = new();
    private string _orderShown = "";
    private TipButton? _tipOwner;
    private bool _tipHeld;
    private bool _touch;
    private bool _fighting;
    // whose bars are shown, for a drag onto them
    private int _barHero;
    private ulong _promptId;
    private double _promptSeconds = 1;

    public LogPanel Log => _log;

    /// <summary>Centres the turn order on the screen less this much on the right (the chat column).</summary>
    private float _rightRoom;

    public void MakeRoom(float right)
    {
        _rightRoom = right;
        _top.OffsetLeft = -200 - right / 2;
        _top.OffsetRight = 200 - right / 2;
        _openDice.OffsetRight = -right - 80;
        _openDice.Visible = _bottom.Visible;
    }

    private HBoxContainer _openDice = null!;
    private HBoxContainer _weapons = null!;
    private string _weaponsShown = "";

    // the weapons a hero carries, as chips beside the hotbar's heading: the one in hand in amber,
    // a press takes another in hand (at the rules' cost in a fight)
    private void MakeWeaponChips()
    {
        var head = GetNode<Label>("Bottom/Row/Hotbar/Head");
        Node hotbar = head.GetParent();
        var row = new HBoxContainer { Name = "HeadRow" };
        hotbar.AddChild(row);
        hotbar.MoveChild(row, 0);
        head.Reparent(row);
        head.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.VerticalAlignment = VerticalAlignment.Center;
        _weapons = new HBoxContainer();
        _weapons.AddThemeConstantOverride("separation", 4);
        row.AddChild(_weapons);
    }

    private void ShowWeapons(World world, int hero)
    {
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        var weapons = sheet.Inventory.Select((item, index) => (Item: item, Index: index)).Where(w => w.Item.Slot == "mainHand").Take(4).ToList();
        string shown = hero + "|" + string.Join(",", weapons.Select(w => w.Item.Name + (w.Item.Equipped ? "*" : "")));
        if (shown == _weaponsShown)
        {
            return;
        }
        _weaponsShown = shown;
        foreach (Node old in _weapons.GetChildren())
        {
            _weapons.RemoveChild(old);
            old.QueueFree();
        }
        // one weapon has nothing to switch to
        if (weapons.Count < 2)
        {
            return;
        }
        foreach ((Item item, int index) in weapons)
        {
            var chip = new Button { Text = item.Name, ToggleMode = true, FocusMode = FocusModeEnum.None, ThemeTypeVariation = "MainButton",
                TooltipText = item.Equipped ? $"{item.Name} is in hand" : $"Take the {item.Name.ToLowerInvariant()} in hand" };
            StyleBoxFlat off = GameScreen.Box(Palette.Ink, Palette.Slate);
            StyleBoxFlat on = GameScreen.Box(Palette.Dusk, Palette.Straw);
            foreach (StyleBoxFlat box in new[] { off, on })
            {
                box.ContentMarginLeft = box.ContentMarginRight = 8;
                box.ContentMarginTop = box.ContentMarginBottom = 1;
            }
            chip.AddThemeStyleboxOverride("normal", off);
            chip.AddThemeStyleboxOverride("hover", on);
            chip.AddThemeStyleboxOverride("pressed", on);
            chip.AddThemeStyleboxOverride("hover_pressed", on);
            chip.AddThemeColorOverride("font_color", Palette.Bone);
            chip.AddThemeColorOverride("font_hover_color", Palette.Bone);
            chip.AddThemeColorOverride("font_pressed_color", Palette.Straw);
            chip.AddThemeColorOverride("font_hover_pressed_color", Palette.Straw);
            chip.AddThemeFontSizeOverride("font_size", 12);
            chip.SetPressedNoSignal(item.Equipped);
            int at = index;
            chip.Pressed += () =>
            {
                _weaponsShown = ""; // shown again from what the rules made of it
                ItemOrdered?.Invoke(new ItemOrder(ItemOrderKind.Equip, hero, at));
            };
            _weapons.AddChild(chip);
        }
    }

    // d4 to d20 over the map's lower right, for a roll the rules didn't ask for
    private void MakeOpenDice()
    {
        _openDice = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, GrowHorizontal = GrowDirection.Begin, GrowVertical = GrowDirection.Begin };
        _openDice.AddThemeConstantOverride("separation", 4);
        AddChild(_openDice);
        _openDice.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.KeepSize);
        _openDice.OffsetBottom = -212;
        _openDice.OffsetTop = -240;
        foreach (int sides in new[] { 4, 6, 8, 10, 12, 20 })
        {
            var die = new Button { Name = $"D{sides}", Text = $"d{sides}", FocusMode = FocusModeEnum.None, ThemeTypeVariation = "MainButton", CustomMinimumSize = new Vector2(36, 28),
                TooltipText = $"Roll a d{sides} for everyone to see" };
            die.AddThemeStyleboxOverride("normal", GameScreen.Box(Palette.Ink, Palette.Slate));
            die.AddThemeStyleboxOverride("hover", GameScreen.Box(Palette.Dusk, Palette.Ash));
            die.AddThemeStyleboxOverride("pressed", GameScreen.Box(Palette.Dusk, Palette.Straw));
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
            {
                die.AddThemeColorOverride(state, Palette.Bone);
            }
            die.AddThemeFontOverride("font", GetThemeFont("font", "NumberLabel"));
            die.AddThemeFontSizeOverride("font_size", 13);
            die.Pressed += () => DieRolled?.Invoke(sides, _barHero);
            _openDice.AddChild(die);
        }
        var say = new Label { Text = "roll for everyone to see", ThemeTypeVariation = "DimLabel", VerticalAlignment = VerticalAlignment.Center };
        say.AddThemeFontSizeOverride("font_size", 12);
        _openDice.AddChild(say);
    }

    public override void _ExitTree()
    {
        ChatPanel.Current?.ShowLog(null);
    }

    public override void _Ready()
    {
        _party = GetNode<VBoxContainer>("Party");
        _partyCount = GetNode<Label>("PartyHead/Count");
        _top = GetNode<Control>("Top");
        MakeOpenDice();
        MakeWeaponChips();
        _round = GetNode<Label>("Top/Initiative/Row/Head/Round");
        _cards = GetNode<HBoxContainer>("Top/Initiative/Row/Cards");
        _turn = GetNode<Label>("Top/Initiative/Row/Head/Turn");
        _bottom = GetNode<Control>("Bottom");
        _portrait = GetNode<PortraitView>("Bottom/Row/Selected/Who/Portrait");
        _hp = GetNode<ProgressBar>("Bottom/Row/Selected/Hp");
        _hpText = GetNode<Label>("Bottom/Row/Selected/Who/Facts/HpText");
        _actions = GetNode<PipsView>("Bottom/Row/Selected/Status/ActionRow/Actions");
        _bonusLabel = GetNode<Label>("Bottom/Row/Selected/Status/BonusRow/BonusLabel");
        _bonus = GetNode<PipsView>("Bottom/Row/Selected/Status/BonusRow/Bonus");
        _reactionPip = GetNode<PipsView>("Bottom/Row/Selected/Status/ReactionRow/Reaction");
        _move = GetNode<MoveBarView>("Bottom/Row/Selected/Status/MoveRow/Move");
        _moveText = GetNode<Label>("Bottom/Row/Selected/Status/MoveRow/MoveText");
        _slots = GetNode<GridContainer>("Bottom/Row/Hotbar/Slots");
        _endTurn = GetNode<Button>("Bottom/Row/EndTurn");
        _log = GetNode<LogPanel>("Log");
        // the log reads in the chat column's Combat log tab, as the design has it
        ChatPanel.Current?.ShowLog(_log);
        _reaction = GetNode<Control>("Reaction");
        _reactionTitle = GetNode<Label>("Reaction/Rows/Title");
        _reactionText = GetNode<Label>("Reaction/Rows/Text");
        _reactionTime = GetNode<ProgressBar>("Reaction/Rows/Time");
        _defeat = GetNode<Control>("Defeat");
        _defeatText = GetNode<Label>("Defeat/Rows/Text");
        GetNode<Button>("Defeat/Rows/Back").Pressed += () => BackPressed?.Invoke();
        _talk = GetNode<Control>("Talk");
        _talkColumn = GetNode<Control>("Talk/Column");
        _talkPlate = GetNode<Control>("Talk/Column/Plate");
        _talkSpeaker = GetNode<Label>("Talk/Column/Plate/Name");
        _talkText = GetNode<Label>("Talk/Column/Box/Rows/Text");
        _replies = GetNode<VBoxContainer>("Talk/Column/Box/Rows/Replies");
        // the scene behind a conversation stays in view, darkened toward ink
        GetNode<ColorRect>("Talk/Dim").Color = Palette.Faded(Palette.Ink, 0.6f);
        _speakerView = GetNode<PortraitView>("Talk/Speaker");
        _listenerView = GetNode<PortraitView>("Talk/Listener");
        _tip = GetNode<Control>("Tip");
        _tipTitle = GetNode<Label>("Tip/Rows/Title");
        _tipMeta = GetNode<Label>("Tip/Rows/Meta");
        _tipBody = GetNode<Label>("Tip/Rows/Body");
        _tipWarning = GetNode<Label>("Tip/Rows/Warning");
        _cursor = GetNode<Label>("Cursor");
        _sheet = GetNode<SheetPanel>("Sheet");
        _sheet.HeroPicked += hero => CreaturePressed?.Invoke(hero);
        _sheet.ClosePressed += () => OpenPanel = "";
        _sheet.ReactionHeld += (hero, id, held) => ReactionHeld?.Invoke(hero, id, held);
        _sheetButton = GetNode<Button>("Bottom/Row/Menu/Sheet");
        _gearView = GetNode<DataPanel>("Gear");
        _gearButton = GetNode<Button>("Bottom/Row/Menu/Gear");
        _gear = new GearPanel(_gearView);
        _gear.Ordered += order => ItemOrdered?.Invoke(order);
        _gearView.ClosePressed += () => OpenPanel = "";
        _spellsView = GetNode<DataPanel>("Spells");
        _spellsButton = GetNode<Button>("Bottom/Row/Menu/Spells");
        _spells = new SpellPanel(_spellsView);
        _spells.Ordered += order => SpellOrdered?.Invoke(order);
        _spells.HeroPicked += hero => CreaturePressed?.Invoke(hero);
        _spellsView.ClosePressed += () => OpenPanel = "";
        _journalView = GetNode<DataPanel>("Journal");
        _journalButton = GetNode<Button>("Bottom/Row/Menu/Journal");
        _journal = new JournalPanel(_journalView);
        _journalView.ClosePressed += () => OpenPanel = "";
        _campView = GetNode<DataPanel>("Camp");
        _campButton = GetNode<Button>("Bottom/Row/Menu/Camp");
        _camp = new CampPanel(_campView);
        _camp.Ordered += order => CampOrdered?.Invoke(order);
        _camp.HeroPicked += hero => CreaturePressed?.Invoke(hero);
        _campView.ClosePressed += () => OpenPanel = "";
        foreach (Node child in GetNode("Bottom/Row/Menu").GetChildren())
        {
            if (child is Button button)
            {
                string name = button.Name;
                button.Pressed += () => MenuPressed?.Invoke(name);
            }
        }

        _endTurn.Pressed += () => EndTurnPressed?.Invoke();
        GetNode<Button>("Reaction/Rows/Buttons/Use").Pressed += () => ReactionAnswered?.Invoke(true);
        GetNode<Button>("Reaction/Rows/Buttons/Pass").Pressed += () => ReactionAnswered?.Invoke(false);
        _top.Visible = false;
        _bottom.Visible = false;
    }

    private PanelContainer? _help;
    private RichTextLabel? _helpText;

    public bool HelpShown => _help?.Visible == true;

    /// <summary>The controls card in the middle of the screen, or away again. Its keys are the ones bound now.</summary>
    public void ShowHelp(bool show)
    {
        if (_help == null)
        {
            _help = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Stop };
            _help.SetAnchorsPreset(LayoutPreset.Center);
            _help.GrowHorizontal = GrowDirection.Both;
            _help.GrowVertical = GrowDirection.Both;
            var rows = new VBoxContainer();
            rows.AddThemeConstantOverride("separation", 12);
            _helpText = new RichTextLabel { BbcodeEnabled = true, FitContent = true, CustomMinimumSize = new Vector2(520, 0), ScrollActive = false };
            var done = new Button { Text = "Got it", ThemeTypeVariation = "MainButton", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(140, 40) };
            done.Pressed += () => ShowHelp(false);
            rows.AddChild(_helpText);
            rows.AddChild(done);
            _help.AddChild(rows);
            AddChild(_help);
        }
        if (show)
        {
            string Key(string action) => App.KeyHint(action) is { Length: > 0 } key ? key : "no key";
            _helpText!.Text = new BookPage().Title("How to play").Sub("This card comes back with " + Key("help") + ".").Rule()
                .Entry("Walk", "Click the ground. The party follows the hero you picked.")
                .Entry("Pick a hero", "Click their picture on the left, or their token.")
                .Entry("Doors, chests, people", "Click them to open, look inside or talk.")
                .Entry("Actions", "The bar at the bottom, or keys 1 to 0. Pick one, then click where it goes. Right click puts it back.")
                .Entry("Fights", $"Each hero has two actions and a move a turn. {Key("end_turn")} ends the turn.")
                .Entry("Look around", $"{Key("pan_up")}, {Key("pan_left")}, {Key("pan_down")}, {Key("pan_right")} or the screen's edge; the wheel zooms; {Key("recenter")} goes back to the hero.")
                .Entry("Panels", $"{Key("sheet")} sheet, {Key("gear")} gear, {Key("spells")} spells, {Key("journal")} journal, {Key("camp")} rest and camp.")
                .Entry("Saving", $"{Key("save")} saves, {Key("load")} loads, Esc for the menu.")
                .ToString();
            // the card is laid out from its text; centre it on what it came to
            _help.ResetSize();
            _help.Position = (Size - _help.GetCombinedMinimumSize()) / 2;
        }
        _help.Visible = show;
    }

    /// <summary>The menu buttons name the key each one has now, after the settings screen changed them.</summary>
    public void ShowKeys()
    {
        foreach ((string button, string label, string action) in new[]
        {
            ("Sheet", "Sheet", "sheet"), ("Gear", "Gear", "gear"), ("Spells", "Spells", "spells"), ("Journal", "Journal", "journal"),
            ("Camp", "Camp", "camp"), ("Save", "Save", "save"),
        })
        {
            GetNode<Button>("Bottom/Row/Menu/" + button).Text = App.WithKey(label, action);
        }
    }

    /// <summary>The panel opened from the menu (the sheet, the gear, the spells), or "" for none.</summary>
    public string OpenPanel { get; private set; } = "";

    /// <summary>Opens a panel by its menu name, or closes it when it is the one open.</summary>
    public void TogglePanel(string name)
    {
        OpenPanel = OpenPanel == name ? "" : name;
        if (OpenPanel == "Gear")
        {
            _gear.Source = GearPanel.Pack;
        }
    }

    /// <summary>Opens the gear panel on a pile ("pile:2") or a shop ("shop:0") beside the hero.</summary>
    public void OpenGear(string source)
    {
        OpenPanel = "Gear";
        _gear.Source = source;
        _gearView.Reset();
    }

    /// <summary>Says under the gear panel's entry why the world said no.</summary>
    public void GearRefused(string why)
    {
        _gear.Refused(why);
    }

    /// <summary>Says under the spell panel's entry why the world said no.</summary>
    public void SpellRefused(string why)
    {
        _spells.Refused(why);
    }

    /// <summary>Says under the camp panel's entry why the world said no.</summary>
    public void CampRefused(string why)
    {
        _camp.Refused(why);
    }

    /// <summary>Opens the spell panel with a spell picked, for input scripts and the first look.</summary>
    public void OpenSpells(string spell = "")
    {
        OpenPanel = "Spells";
        _spellsView.Reset();
        if (spell.Length > 0)
        {
            _spellsView.Pick(spell);
        }
    }

    /// <summary>The action on the hotbar's slot with this place, 0 first; null when there is none.</summary>
    public string? SlotAction(int index)
    {
        // between fights the hotbar is there to read, not to press
        return _bottom.Visible && _fighting && index >= 0 && index < _slotViews.Count && _slotViews[index].ActionId.Length > 0 ? _slotViews[index].ActionId : null;
    }

    public override void _Input(InputEvent @event)
    {
        // a finger has no hover: tips then only come from holding
        if (@event is InputEventScreenTouch or InputEventScreenDrag)
        {
            _touch = true;
        }
        else if (@event is InputEventMouseMotion motion && motion.Device != InputEvent.DeviceIdEmulation)
        {
            _touch = false;
        }
        // a tip that was held open goes away at the next press anywhere
        if (@event is InputEventMouseButton { Pressed: true } && _tipHeld)
        {
            _tipOwner = null;
            _tipHeld = false;
        }
    }

    /// <summary>Called every frame with the world as it is and what the player points at.</summary>
    public void Refresh(World world, FightAim aim)
    {
        bool fighting = world.Fighting && !world.PartyWiped;
        int? current = world.CurrentCreature;
        int shown = aim.HeroTurn && current is int acting ? acting : world.LeaderIndex();

        _fighting = fighting;
        ShowParty(world, fighting, current);
        _top.Visible = fighting;
        // the hotbar stays between fights; a conversation takes its place along the bottom
        _bottom.Visible = !world.PartyWiped && !world.InCutscene && world.Talk?.Current == null && shown < world.HeroCount;
        _endTurn.Visible = fighting;
        if (fighting)
        {
            ShowOrder(world);
        }
        if (_bottom.Visible)
        {
            ShowBar(world, aim, shown, fighting);
        }
        _log.Rest(_bottom.Visible ? LogAboveHotbar : LogAboveEdge);
        ShowReaction(world);
        _defeat.Visible = world.PartyWiped && !world.InCutscene;
        _defeatText.Text = world.Chapter.DefeatText;
        ShowTalk(world);
        ShowPanels(world, shown, aim.HeroTurn);

        bool card = ShowFoeCard(world, aim);
        // the card says the chance to hit itself; a refusal still shows at the pointer
        _cursor.Visible = aim.Label.Length > 0 && !(card && !aim.LabelBad && aim.Action.Length == 0);
        if (_cursor.Visible)
        {
            _cursor.Text = aim.Label;
            _cursor.Modulate = aim.LabelBad ? Palette.Rose : Palette.Bone;
            _cursor.Size = Vector2.Zero;
            // kept on screen: a long refusal near the right edge would run off it
            Vector2 room = GetViewportRect().Size - _cursor.GetCombinedMinimumSize() - new Vector2(6 + _rightRoom, 6);
            Vector2 at = aim.LabelAt + new Vector2(20, 14);
            _cursor.Position = new Vector2(Mathf.Clamp(at.X, 6, Mathf.Max(6, room.X)), Mathf.Clamp(at.Y, 6, Mathf.Max(6, room.Y)));
        }
        ShowTip();
    }

    private PanelContainer? _foeCard;
    private Label _foeName = null!;
    private Label _foeSide = null!;
    private ColorRect _foeLine = null!;
    private GridContainer _foeRows = null!;

    // The card beside a creature under the pointer in a fight: its name and side, its health and
    // defence, and the chance to hit it for the hero whose turn it is.
    private bool ShowFoeCard(World world, FightAim aim)
    {
        _foeCard ??= MakeFoeCard();
        if (!_fighting || aim.Hovered is not int who || who < 0 || who >= world.Creatures.Count || who < world.HeroCount)
        {
            _foeCard.Visible = false;
            return false;
        }
        WorldCreature creature = world.Creatures[who];
        CharacterSheet sheet = creature.Sheet;
        bool foe = creature.Team != 0;
        _foeName.Text = sheet.Name;
        _foeSide.Text = foe ? "ENEMY" : "ALLY";
        Color side = foe ? Palette.Red : Palette.Bone;
        _foeSide.AddThemeColorOverride("font_color", side);
        _foeLine.Color = side;
        var rows = new List<(string Name, string Value, Color Tint)>();
        Ruleset rules = world.Rules;
        rows.Add((sheet.Tracks.Count > 0 ? "Stress" : rules.Sheet.NameOf("hp"),
            sheet.Tracks.Count > 0 ? HudText.TrackBoxes(sheet) : $"{Math.Max(0, sheet.Hp)} / {sheet.MaxHp}", Palette.Bone));
        string defence = rules.Checks.Kind(CheckRules.Attack).DefenceId;
        rows.Add((rules.DefenceName(defence), sheet.Defence(rules, defence).ToString(), Palette.Bone));
        if (foe && aim.HeroTurn && world.CurrentCreature is int me)
        {
            float chance = world.HitChance(me, who, aim.Action.Length > 0 ? aim.Action : null);
            rows.Add(("Chance to hit", $"{Mathf.RoundToInt(chance * 100)}%", Palette.Straw));
        }
        while (_foeRows.GetChildCount() < rows.Count * 2)
        {
            var name = new Label { ThemeTypeVariation = "DimLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            name.AddThemeFontSizeOverride("font_size", 12);
            var value = new Label { ThemeTypeVariation = "NumberLabel", HorizontalAlignment = HorizontalAlignment.Right };
            value.AddThemeFontSizeOverride("font_size", 13);
            _foeRows.AddChild(name);
            _foeRows.AddChild(value);
        }
        for (int i = 0; i < _foeRows.GetChildCount() / 2; i++)
        {
            var name = _foeRows.GetChild<Label>(i * 2);
            var value = _foeRows.GetChild<Label>(i * 2 + 1);
            name.Visible = value.Visible = i < rows.Count;
            if (i < rows.Count)
            {
                name.Text = rows[i].Name;
                value.Text = rows[i].Value;
                value.AddThemeColorOverride("font_color", rows[i].Tint);
            }
        }
        _foeCard.Visible = true;
        _foeCard.Size = Vector2.Zero;
        // up and to the right of the pointer, kept on screen
        Vector2 size = _foeCard.GetCombinedMinimumSize();
        Vector2 at = aim.LabelAt + new Vector2(28, -size.Y - 12);
        // the chat column covers the right; a card that would go under it flips to the pointer's left
        Vector2 view = GetViewportRect().Size - new Vector2(_rightRoom, 0);
        if (at.X + size.X > view.X - 6)
        {
            at.X = aim.LabelAt.X - 28 - size.X;
        }
        _foeCard.Position = new Vector2(Mathf.Clamp(at.X, 6, Mathf.Max(6, view.X - size.X - 6)), Mathf.Clamp(at.Y, 6, Mathf.Max(6, view.Y - size.Y - 6)));
        return true;
    }

    private PanelContainer MakeFoeCard()
    {
        var box = new StyleBoxFlat { BgColor = Palette.Ink, BorderColor = Palette.Slate, ShadowColor = Palette.Night, ShadowSize = 0, ShadowOffset = new Vector2(3, 3) };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(12);
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 10, Visible = false };
        card.AddThemeStyleboxOverride("panel", box);
        var rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(160, 0) };
        rows.AddThemeConstantOverride("separation", 4);
        card.AddChild(rows);
        var head = new HBoxContainer();
        _foeName = new Label { ThemeTypeVariation = "TitleLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _foeName.AddThemeFontSizeOverride("font_size", 14);
        _foeName.AddThemeColorOverride("font_color", Palette.Bone);
        _foeSide = new Label { ThemeTypeVariation = "CapsLabel", VerticalAlignment = VerticalAlignment.Center };
        _foeSide.AddThemeFontSizeOverride("font_size", 10);
        head.AddChild(_foeName);
        head.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });
        head.AddChild(_foeSide);
        rows.AddChild(head);
        _foeLine = new ColorRect { CustomMinimumSize = new Vector2(0, 2), MouseFilter = MouseFilterEnum.Ignore };
        rows.AddChild(_foeLine);
        _foeRows = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        _foeRows.AddThemeConstantOverride("h_separation", 24);
        _foeRows.AddThemeConstantOverride("v_separation", 0);
        rows.AddChild(_foeRows);
        AddChild(card);
        return card;
    }

    // The hero a panel shows is the one whose turn it is, or the selected one between fights.
    private void ShowPanels(World world, int hero, bool heroTurn)
    {
        _spellsButton.SetPressedNoSignal(OpenPanel == "Spells");
        _spellsView.Visible = OpenPanel == "Spells" && hero < world.HeroCount;
        if (_spellsView.Visible)
        {
            _spells.Refresh(world, hero, heroTurn);
        }
        _sheetButton.SetPressedNoSignal(OpenPanel == "Sheet");
        _sheet.Visible = OpenPanel == "Sheet" && hero < world.HeroCount;
        if (_sheet.Visible)
        {
            _sheet.Refresh(world, hero);
        }
        _gearButton.SetPressedNoSignal(OpenPanel == "Gear");
        _gearView.Visible = OpenPanel == "Gear" && hero < world.HeroCount;
        if (_gearView.Visible)
        {
            _gear.Refresh(world, hero);
        }
        _journalButton.SetPressedNoSignal(OpenPanel == "Journal");
        _journalView.Visible = OpenPanel == "Journal";
        if (_journalView.Visible)
        {
            _journal.Refresh(world);
        }
        _campButton.SetPressedNoSignal(OpenPanel == "Camp");
        _campView.Visible = OpenPanel == "Camp" && hero < world.HeroCount && !world.Fighting;
        if (_campView.Visible)
        {
            _camp.Refresh(world, hero);
        }
    }

    // The conversation along the bottom, between the party cards and the log: who speaks, the line,
    // and a numbered button per reply (1 to 9 pick them too). The buttons are only made again when
    // the line changes, so a press isn't lost.
    private void ShowTalk(World world)
    {
        DialogueSession? talk = world.Talk;
        DialogueNode? node = talk?.Current;
        _talk.Visible = node != null;
        // like a visual novel, the conversation has the screen: the party and the log step back
        _party.Visible = node == null;
        GetNode<Control>("PartyHead").Visible = node == null;
        _log.Visible = node == null && ChatPanel.Current == null;
        if (talk == null || node == null)
        {
            _talkShown = "";
            return;
        }

        // who speaks stands large on the left, the hero they talk to dimmed on the right
        int with = world.TalkingWith;
        if (with >= 0 && with < world.Creatures.Count)
        {
            CharacterSheet them = world.Creatures[with].Sheet;
            _speakerView.Show(them.Name, world.Tokens.Tokens[with].Color.ToGodot(), false, Portraits.Of(world, with));
        }
        int hero = world.LeaderIndex();
        CharacterSheet me = world.Creatures[hero].Sheet;
        _listenerView.Show(me.Name, world.Tokens.Tokens[hero].Color.ToGodot(), false, Portraits.Of(world, hero));
        _speakerView.Visible = with >= 0;
        // narration (a room's passage, nobody speaking) is read over the map, not between two faces
        _listenerView.Visible = with >= 0 || node.Speaker.Length > 0;

        List<DialogueChoice> choices = talk.Choices();
        string shown = $"{talk.Dialogue.Id}/{node.Id}/{string.Join(",", choices.Select(c => c.Id))}";
        if (shown == _talkShown)
        {
            FitTalk(); // a wrapped line only knows its height a frame after it is set
            return;
        }
        _talkShown = shown;
        _talkSpeaker.Text = (node.Speaker.Length > 0 ? node.Speaker : with >= 0 ? world.Creatures[with].Sheet.Name : "").ToUpperInvariant();
        _talkPlate.Visible = _talkSpeaker.Text.Length > 0;
        _talkText.Text = node.Text;
        _listenerView.Modulate = Palette.Smoke;
        foreach (Node old in _replies.GetChildren())
        {
            _replies.RemoveChild(old);
            old.QueueFree();
        }
        var lines = choices.Count == 0
            ? new List<string> { "1. (Leave)" }
            : choices.Select((c, i) => $"{i + 1}. {c.Text}" + (c.Check != null ? $"  [{c.Check.Skill} {c.Check.Difficulty}]" : "")).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Text = lines[i],
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 36),
                ThemeTypeVariation = "ChoiceButton",
            };
            button.Pressed += () => ReplyPressed?.Invoke(index);
            // the hero steps forward while the player weighs what they would say
            button.MouseEntered += () => _listenerView.Modulate = Colors.White;
            button.MouseExited += () => _listenerView.Modulate = Palette.Smoke;
            _replies.AddChild(button);
        }
        // a merchant's shop opens from the conversation, as E did beside one in the C++ client
        int npc = world.TalkingWith >= world.NpcStart ? world.Creatures[world.TalkingWith].Npc : -1;
        if (npc >= 0 && npc < world.Merchants.Count && world.Merchants[npc] != null)
        {
            var trade = new Button
            {
                Text = "Trade (T)",
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 36),
                ThemeTypeVariation = "ChoiceButton",
            };
            trade.Pressed += () => TradePressed?.Invoke();
            _replies.AddChild(trade);
        }
        FitTalk();
    }

    // The name plate and the box grow up from the bottom edge to fit the line and the replies.
    private void FitTalk()
    {
        float top = _talkColumn.OffsetBottom - _talkColumn.GetCombinedMinimumSize().Y;
        if (!Mathf.IsEqualApprox(_talkColumn.OffsetTop, top))
        {
            _talkColumn.OffsetTop = top;
        }
    }

    private void ShowParty(World world, bool fighting, int? current)
    {
        if (PartyCardScene == null)
        {
            return;
        }
        while (_partyCards.Count < world.HeroCount)
        {
            var card = PartyCardScene.Instantiate<PartyCard>();
            _party.AddChild(card);
            Listen(card);
            card.Clicked += pressed => CreaturePressed?.Invoke(((PartyCard)pressed).Creature);
            _partyCards.Add(card);
        }
        int leader = world.LeaderIndex();
        int standing = Enumerable.Range(0, world.HeroCount).Count(i => !world.Creatures[i].Sheet.Down);
        _partyCount.Text = $"{standing} / {world.HeroCount}";
        for (int i = 0; i < _partyCards.Count; i++)
        {
            _partyCards[i].Visible = i < world.HeroCount;
            if (i >= world.HeroCount)
            {
                continue;
            }
            bool marked = fighting ? current == i : i == leader && !world.Creatures[i].Sheet.Down;
            _partyCards[i].Show(world, i, marked, fighting && world.CanChooseTurn(i));
        }
    }

    // The turn order from the block whose turn it is, round the list once. The dead and the gone
    // are left out; a hero who is down and still rolling death saves stays.
    private void ShowOrder(World world)
    {
        Encounter fight = world.Encounter!;
        if (InitiativeCardScene == null || fight.Order.Count == 0)
        {
            return;
        }
        var creatureAt = new int[fight.Order.Count];
        Array.Fill(creatureAt, -1);
        for (int i = 0; i < world.Creatures.Count; i++)
        {
            if (world.OrderIndex(i) is int place)
            {
                creatureAt[place] = i;
            }
        }

        var entries = new List<(int Creature, int Place, bool Gap)>();
        var signature = new StringBuilder();
        int lastTeam = -1;
        for (int step = 0; step < fight.Order.Count; step++)
        {
            int place = (fight.BlockFirst + step) % fight.Order.Count;
            Combatant combatant = fight.Order[place];
            bool dying = world.Rules.Death.Enabled && combatant.Sheet.Death.Saves && !combatant.Sheet.Death.Dead;
            if (creatureAt[place] < 0 || combatant.Out || (combatant.Sheet.Down && !dying))
            {
                continue;
            }
            // allies next to each other share a turn and sit together; a gap marks the next block
            bool gap = entries.Count > 0 && (combatant.Team != lastTeam || place == 0);
            lastTeam = combatant.Team;
            entries.Add((creatureAt[place], place, gap));
            signature.Append(creatureAt[place]).Append(gap ? '|' : ',');
        }

        if (signature.ToString() != _orderShown)
        {
            _orderShown = signature.ToString();
            if (_tipOwner is InitiativeCard)
            {
                _tipOwner = null;
            }
            foreach (Node child in _cards.GetChildren())
            {
                _cards.RemoveChild(child);
                child.QueueFree();
            }
            _orderCards.Clear();
            foreach ((int _, int _, bool gap) in entries)
            {
                if (gap)
                {
                    _cards.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0), MouseFilter = MouseFilterEnum.Ignore });
                }
                var card = InitiativeCardScene.Instantiate<InitiativeCard>();
                _cards.AddChild(card);
                Listen(card);
                card.Clicked += pressed => CreaturePressed?.Invoke(((InitiativeCard)pressed).Creature);
                _orderCards.Add(card);
            }
        }
        for (int i = 0; i < entries.Count && i < _orderCards.Count; i++)
        {
            (int creature, int place, bool _) = entries[i];
            bool inBlock = place >= fight.BlockFirst && place < fight.BlockEnd;
            _orderCards[i].Show(world, creature, place == fight.CurrentIndex, world.CanChooseTurn(creature), inBlock && fight.Order[place].TurnDone);
        }

        _round.Text = $"ROUND {Math.Max(1, fight.Round)}";
        _turn.Text = world.CurrentCreature is int now && world.ReactionPrompt == null ? $"{world.Creatures[now].Sheet.Name}'s turn" : "";
    }

    private void ShowBar(World world, FightAim aim, int shown, bool fighting)
    {
        CharacterSheet sheet = world.Creatures[shown].Sheet;
        _portrait.Show(sheet.Name, world.Tokens.Tokens[shown].Color.ToGodot(), sheet.Down, Portraits.Of(world, shown), Portraits.FocusOf(world, shown));
        _hp.MaxValue = Mathf.Max(1, sheet.MaxHp);
        _hp.Value = Mathf.Max(0, sheet.Hp);
        _barLow = PartyCard.ShowLow(_hp, _hpText, sheet, _barLow);
        (string downed, _) = sheet.DownedText(world.Rules);
        _hpText.Text = downed.Length > 0 ? downed : sheet.Tracks.Count > 0 ? HudText.TrackBoxes(sheet) : $"{Mathf.Max(0, sheet.Hp)}/{sheet.MaxHp}";

        // off their turn a hero has nothing to spend, whatever was left over from the last one
        TurnBudget? budget = world.BudgetOf(shown);
        bool mine = aim.HeroTurn && world.CurrentCreature == shown && budget != null;
        // between fights nothing is spent, so the pips and the move bar stand full
        int actions = mine ? budget!.Actions : fighting ? 0 : world.Rules.ActionsPerTurn;
        _actions.Show(Math.Max(world.Rules.ActionsPerTurn, actions), actions);
        _bonusLabel.Visible = world.Rules.BonusActions;
        _bonus.Visible = world.Rules.BonusActions;
        _bonus.Show(1, !fighting || (mine && budget!.BonusAction) ? 1 : 0);
        _reactionPip.Show(1, !fighting || budget is { Reaction: true } ? 1 : 0);
        int left = mine ? budget!.MovementLeft : fighting ? 0 : sheet.SpeedSquares(world.Rules);
        _move.Show(Math.Max(sheet.SpeedSquares(world.Rules), left), left, mine ? aim.PathCost : 0);
        _moveText.Text = $"{left * world.Rules.FeetPerSquare}/{sheet.SpeedSquares(world.Rules) * world.Rules.FeetPerSquare} ft";
        GetNode<Label>("Bottom/Row/Selected/Who/Facts/Name").Text = sheet.Name;
        string defence = world.Rules.Checks.Kind(CheckRules.Attack).DefenceId;
        GetNode<Label>("Bottom/Row/Selected/Who/Facts/Defence").Text = $"{world.Rules.DefenceName(defence)} {sheet.Defence(world.Rules, defence)}";

        // the bars as the player arranged them; End turn has the big button, so it gets no slot
        _barHero = shown;
        ShowWeapons(world, shown);
        string[] layout = world.HotbarOf(shown);
        while (SlotScene != null && _slotViews.Count < layout.Length)
        {
            var slot = SlotScene.Instantiate<ActionSlot>();
            slot.Index = _slotViews.Count;
            _slots.AddChild(slot);
            Listen(slot);
            slot.Clicked += pressed =>
            {
                var which = (ActionSlot)pressed;
                if (which.ActionId.Length == 0)
                {
                    return;
                }
                if (which.Usable)
                {
                    ActionPressed?.Invoke(which.ActionId);
                }
                else
                {
                    // pressing a greyed slot says why, which is all a finger can do to find out
                    Want(which, true);
                }
            };
            slot.Dropped += (onto, data) => SlotDropped(onto.Index, data);
            slot.DraggedOff += off => HotbarChanged?.Invoke(new HotbarEdit(_barHero, off.Index, ""));
            _slotViews.Add(slot);
        }
        List<ActionDefinition> owned = world.ActionsOf(shown);
        for (int i = 0; i < _slotViews.Count; i++)
        {
            ActionSlot slot = _slotViews[i];
            string key = i < 9 ? (i + 1).ToString() : i == 9 ? "0" : "";
            if (i >= layout.Length || owned.Find(a => a.Id == layout[i]) is not ActionDefinition action)
            {
                slot.ShowEmpty(key);
                slot.TipTitle = "";
                continue;
            }
            int cost = world.ActionCost(shown, action);
            string why = "";
            bool usable = mine && world.CanUse(shown, action, out why);
            slot.Show(action.Id, action.Name, key, cost, usable, aim.Action == action.Id,
                ActionIcon.PictureOf(world, action.Id));
            slot.TipTitle = action.Name;
            slot.TipMeta = HudText.ActionMeta(world, action, cost, shown);
            slot.TipBody = action.Description;
            slot.TipWarning = usable ? ""
                : !fighting ? "Used in a fight."
                : !mine ? "Not their turn."
                : why.Length > 0 ? char.ToUpperInvariant(why[0]) + why[1..] + "."
                : "Can't be used right now.";
        }
        _endTurn.Disabled = !(mine && world.CanUse(shown, world.EndTurnAction));
    }

    private void ShowReaction(World world)
    {
        ReactionPrompt? prompt = world.ReactionPrompt;
        _reaction.Visible = prompt != null;
        if (prompt == null)
        {
            return;
        }
        if (prompt.Id != _promptId)
        {
            _promptId = prompt.Id;
            _promptSeconds = Math.Max(0.1, prompt.SecondsLeft);
        }
        _reactionTitle.Text = prompt.Name;
        _reactionText.Text = $"{world.Creatures[prompt.Creature].Sheet.Name} can react to {world.Creatures[prompt.Mover].Sheet.Name}.";
        _reactionTime.Value = Math.Clamp(prompt.SecondsLeft / _promptSeconds, 0, 1);
    }

    private void Listen(TipButton button)
    {
        button.TipWanted += Want;
        button.TipDone += done =>
        {
            if (_tipOwner == done && !_tipHeld)
            {
                _tipOwner = null;
            }
        };
    }

    // A slot or a spell book icon let go on a slot: from another slot it moves (and swaps), from the book it is put there.
    private void SlotDropped(int slot, string data)
    {
        string action = "";
        if (data.StartsWith(ActionSlot.SlotDrag) && int.TryParse(data[ActionSlot.SlotDrag.Length..], out int from) && from >= 0 && from < _slotViews.Count)
        {
            action = _slotViews[from].ActionId;
        }
        else if (data.StartsWith(ActionSlot.ActionDrag))
        {
            action = data[ActionSlot.ActionDrag.Length..];
        }
        if (action.Length > 0)
        {
            HotbarChanged?.Invoke(new HotbarEdit(_barHero, slot, action));
        }
    }

    private void Want(TipButton button, bool held)
    {
        if ((!held && _touch) || button.TipTitle.Length == 0)
        {
            return;
        }
        _tipOwner = button;
        _tipHeld = held;
    }

    private void ShowTip()
    {
        if (_tipOwner == null || !IsInstanceValid(_tipOwner) || !_tipOwner.IsVisibleInTree())
        {
            _tipOwner = null;
            _tipHeld = false;
            _tip.Visible = false;
            return;
        }
        _tipTitle.Text = _tipOwner.TipTitle;
        _tipMeta.Text = _tipOwner.TipMeta;
        _tipMeta.Visible = _tipOwner.TipMeta.Length > 0;
        _tipBody.Text = _tipOwner.TipBody;
        _tipBody.Visible = _tipOwner.TipBody.Length > 0;
        _tipWarning.Text = _tipOwner.TipWarning;
        _tipWarning.Visible = _tipOwner.TipWarning.Length > 0;
        _tip.Visible = true;
        _tip.Size = Vector2.Zero; // shrinks to what the words need

        // above a button on the bottom bar, right of a party card, under a card in the turn order
        Rect2 anchor = _tipOwner.GetGlobalRect();
        Vector2 view = GetViewportRect().Size;
        Vector2 size = _tip.Size;
        Vector2 at;
        if (anchor.Position.Y > view.Y / 2)
        {
            at = new Vector2(anchor.GetCenter().X - size.X / 2, anchor.Position.Y - size.Y - 10);
        }
        else if (anchor.Position.X < 160)
        {
            at = new Vector2(anchor.End.X + 10, anchor.Position.Y);
        }
        else
        {
            at = new Vector2(anchor.GetCenter().X - size.X / 2, anchor.End.Y + 10);
        }
        _tip.Position = new Vector2(Mathf.Clamp(at.X, 6, Mathf.Max(6, view.X - size.X - 6)), Mathf.Clamp(at.Y, 6, Mathf.Max(6, view.Y - size.Y - 6)));
    }
}
