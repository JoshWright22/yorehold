using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The panels over the map: party cards down the left and the log in the bottom right always, and
/// in a fight the turn order along the top, the hotbar with the acting hero's portrait, pips and
/// End Turn along the bottom, the reaction prompt and the tooltip. It shows what the World says and
/// raises an event when something is pressed; it never acts on the World itself.
/// </summary>
public partial class PlayHud : Control
{
    [Export] public PackedScene? SlotScene { get; set; }
    [Export] public PackedScene? PartyCardScene { get; set; }
    [Export] public PackedScene? InitiativeCardScene { get; set; }
    /// <summary>Which shape each action's icon is drawn with, by action id.</summary>
    [Export] public string IconsFile { get; set; } = "res://assets/ui/action-icons.json";
    /// <summary>Where the log's bottom edge rests, in pixels above the screen's: clear of the hotbar in a fight, in the corner otherwise.</summary>
    [Export] public float LogAboveHotbar { get; set; } = 126;
    [Export] public float LogAboveEdge { get; set; } = 10;

    public event Action<string>? ActionPressed;
    public event Action? EndTurnPressed;
    /// <summary>A party card or a card in the turn order.</summary>
    public event Action<int>? CreaturePressed;
    /// <summary>True for use it, false for pass.</summary>
    public event Action<bool>? ReactionAnswered;
    /// <summary>A button on the menu along the top right, by its node name ("Characters").</summary>
    public event Action<string>? MenuPressed;
    /// <summary>Something pressed on the gear panel.</summary>
    public event Action<ItemOrder>? ItemOrdered;

    // the scene has all of these
    private VBoxContainer _party = null!;
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
    private HBoxContainer _slots = null!;
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
    private DataPanel _sheetView = null!;
    private SheetPanel _sheet = null!;
    private Button _sheetButton = null!;
    private DataPanel _gearView = null!;
    private Button _gearButton = null!;
    private GearPanel _gear = null!;

    private readonly Dictionary<string, string> _icons = new();
    private readonly List<PartyCard> _partyCards = new();
    private readonly List<InitiativeCard> _orderCards = new();
    private readonly List<ActionSlot> _slotViews = new();
    private string _orderShown = "";
    private TipButton? _tipOwner;
    private bool _tipHeld;
    private bool _touch;
    private ulong _promptId;
    private double _promptSeconds = 1;

    public LogPanel Log => _log;

    public override void _Ready()
    {
        _party = GetNode<VBoxContainer>("Party");
        _top = GetNode<Control>("Top");
        _round = GetNode<Label>("Top/Initiative/Row/Round");
        _cards = GetNode<HBoxContainer>("Top/Initiative/Row/Cards");
        _turn = GetNode<Label>("Top/Turn");
        _bottom = GetNode<Control>("Bottom");
        _portrait = GetNode<PortraitView>("Bottom/Selected/Rows/Portrait");
        _hp = GetNode<ProgressBar>("Bottom/Selected/Rows/Hp");
        _hpText = GetNode<Label>("Bottom/Selected/Rows/Hp/Text");
        _actions = GetNode<PipsView>("Bottom/Hotbar/Rows/Status/Actions");
        _bonusLabel = GetNode<Label>("Bottom/Hotbar/Rows/Status/BonusLabel");
        _bonus = GetNode<PipsView>("Bottom/Hotbar/Rows/Status/Bonus");
        _reactionPip = GetNode<PipsView>("Bottom/Hotbar/Rows/Status/Reaction");
        _move = GetNode<MoveBarView>("Bottom/Hotbar/Rows/Status/Move");
        _moveText = GetNode<Label>("Bottom/Hotbar/Rows/Status/MoveText");
        _slots = GetNode<HBoxContainer>("Bottom/Hotbar/Rows/Slots");
        _endTurn = GetNode<Button>("Bottom/EndTurn");
        _log = GetNode<LogPanel>("Log");
        _reaction = GetNode<Control>("Reaction");
        _reactionTitle = GetNode<Label>("Reaction/Rows/Title");
        _reactionText = GetNode<Label>("Reaction/Rows/Text");
        _reactionTime = GetNode<ProgressBar>("Reaction/Rows/Time");
        _defeat = GetNode<Control>("Defeat");
        _defeatText = GetNode<Label>("Defeat/Text");
        _tip = GetNode<Control>("Tip");
        _tipTitle = GetNode<Label>("Tip/Rows/Title");
        _tipMeta = GetNode<Label>("Tip/Rows/Meta");
        _tipBody = GetNode<Label>("Tip/Rows/Body");
        _tipWarning = GetNode<Label>("Tip/Rows/Warning");
        _cursor = GetNode<Label>("Cursor");
        _sheetView = GetNode<DataPanel>("Sheet");
        _sheet = new SheetPanel(_sheetView);
        _sheet.HeroPicked += hero => CreaturePressed?.Invoke(hero);
        _sheetView.ClosePressed += () => OpenPanel = "";
        _sheetButton = GetNode<Button>("Menu/Sheet");
        _gearView = GetNode<DataPanel>("Gear");
        _gearButton = GetNode<Button>("Menu/Gear");
        _gear = new GearPanel(_gearView);
        _gear.Ordered += order => ItemOrdered?.Invoke(order);
        _gearView.ClosePressed += () => OpenPanel = "";
        foreach (Node child in GetNode("Menu").GetChildren())
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
        ReadIcons();
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

    /// <summary>The action on the hotbar's slot with this place, 0 first; null when there is none.</summary>
    public string? SlotAction(int index)
    {
        return _bottom.Visible && index >= 0 && index < _slotViews.Count && _slotViews[index].Visible ? _slotViews[index].ActionId : null;
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

        ShowParty(world, fighting, current);
        _top.Visible = fighting;
        _bottom.Visible = fighting;
        if (fighting)
        {
            ShowOrder(world);
            ShowBar(world, aim, shown);
        }
        _log.Rest(fighting ? LogAboveHotbar : LogAboveEdge);
        ShowReaction(world);
        _defeat.Visible = world.PartyWiped;
        _defeatText.Text = world.Chapter.DefeatText;
        ShowPanels(world, shown);

        _cursor.Visible = aim.Label.Length > 0;
        if (_cursor.Visible)
        {
            _cursor.Text = aim.Label;
            _cursor.Modulate = aim.LabelBad ? Palette.Rose : Palette.Bone;
            _cursor.Size = Vector2.Zero;
            _cursor.Position = aim.LabelAt + new Vector2(20, 14);
        }
        ShowTip();
    }

    // The hero a panel shows is the one whose turn it is, or the selected one between fights.
    private void ShowPanels(World world, int hero)
    {
        _sheetButton.SetPressedNoSignal(OpenPanel == "Sheet");
        _sheetView.Visible = OpenPanel == "Sheet" && hero < world.HeroCount;
        if (_sheetView.Visible)
        {
            _sheet.Refresh(world, hero);
        }
        _gearButton.SetPressedNoSignal(OpenPanel == "Gear");
        _gearView.Visible = OpenPanel == "Gear" && hero < world.HeroCount;
        if (_gearView.Visible)
        {
            _gear.Refresh(world, hero);
        }
    }

    private void ReadIcons()
    {
        string file = ProjectSettings.GlobalizePath(IconsFile);
        if (!System.IO.File.Exists(file))
        {
            return; // every action then gets its first letter
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllText(file));
            foreach (JsonProperty entry in document.RootElement.EnumerateObject())
            {
                _icons[entry.Name] = entry.Value.GetString() ?? "";
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            GD.PushWarning($"{IconsFile}: {error.Message}");
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

        _round.Text = $"Round {Math.Max(1, fight.Round)}";
        _turn.Text = world.CurrentCreature is int now && world.ReactionPrompt == null ? $"{world.Creatures[now].Sheet.Name}'s turn" : "";
    }

    private void ShowBar(World world, FightAim aim, int shown)
    {
        CharacterSheet sheet = world.Creatures[shown].Sheet;
        _portrait.Show(sheet.Name, world.Tokens.Tokens[shown].Color.ToGodot(), sheet.Down);
        _hp.MaxValue = Mathf.Max(1, sheet.MaxHp);
        _hp.Value = Mathf.Max(0, sheet.Hp);
        _hpText.Text = $"{Mathf.Max(0, sheet.Hp)} / {sheet.MaxHp}";

        // off their turn a hero has nothing to spend, whatever was left over from the last one
        TurnBudget? budget = world.BudgetOf(shown);
        bool mine = aim.HeroTurn && world.CurrentCreature == shown && budget != null;
        int actions = mine ? budget!.Actions : 0;
        _actions.Show(Math.Max(world.Rules.ActionsPerTurn, actions), actions);
        _bonusLabel.Visible = world.Rules.BonusActions;
        _bonus.Visible = world.Rules.BonusActions;
        _bonus.Show(1, mine && budget!.BonusAction ? 1 : 0);
        _reactionPip.Show(1, budget is { Reaction: true } ? 1 : 0);
        int left = mine ? budget!.MovementLeft : 0;
        _move.Show(Math.Max(sheet.SpeedSquares(world.Rules), left), left, mine ? aim.PathCost : 0);
        _moveText.Text = $"{left * world.Rules.FeetPerSquare} ft";

        // End turn has the big button, so it gets no slot
        List<ActionDefinition> actionsShown = world.ActionsOf(shown).Where(a => a.Id != World.EndTurnAction).ToList();
        while (SlotScene != null && _slotViews.Count < actionsShown.Count)
        {
            var slot = SlotScene.Instantiate<ActionSlot>();
            _slots.AddChild(slot);
            Listen(slot);
            slot.Clicked += pressed =>
            {
                var which = (ActionSlot)pressed;
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
            _slotViews.Add(slot);
        }
        for (int i = 0; i < _slotViews.Count; i++)
        {
            ActionSlot slot = _slotViews[i];
            slot.Visible = i < actionsShown.Count;
            if (i >= actionsShown.Count)
            {
                continue;
            }
            ActionDefinition action = actionsShown[i];
            int cost = world.ActionCost(shown, action);
            string why = "";
            bool usable = mine && world.CanUse(shown, action, out why);
            string key = i < 9 ? (i + 1).ToString() : i == 9 ? "0" : "";
            slot.Show(action.Id, _icons.GetValueOrDefault(action.Id, ""), action.Name, key, cost, usable, aim.Action == action.Id);
            slot.TipTitle = action.Name;
            slot.TipMeta = HudText.ActionMeta(world, action, cost);
            slot.TipBody = action.Description;
            slot.TipWarning = usable ? ""
                : !mine ? "Not their turn."
                : why.Length > 0 ? char.ToUpperInvariant(why[0]) + why[1..] + "."
                : "Can't be used right now.";
        }
        _endTurn.Disabled = !(mine && world.CanUse(shown, World.EndTurnAction));
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

    private void Want(TipButton button, bool held)
    {
        if (!held && _touch)
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
