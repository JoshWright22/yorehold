using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>A hero on the left edge: portrait, HP, conditions, and a frame when it is their turn or they can take it.</summary>
public partial class PartyCard : TipButton
{
    public int Creature { get; private set; } = -1;

    // the scene has all of these
    private PortraitView _portrait = null!;
    private Label _name = null!;
    private ProgressBar _hp = null!;
    private Label _hpText = null!;
    private HBoxContainer _conditions = null!;
    private Panel _ready = null!;
    private Panel _turn = null!;
    private readonly List<ConditionBadge> _badges = new();

    public override void _Ready()
    {
        base._Ready();
        _portrait = GetNode<PortraitView>("Portrait");
        _name = GetNode<Label>("Name");
        _hp = GetNode<ProgressBar>("Hp");
        _hpText = GetNode<Label>("Hp/Text");
        _conditions = GetNode<HBoxContainer>("Conditions");
        _ready = GetNode<Panel>("Ready");
        _turn = GetNode<Panel>("Turn");
    }

    /// <summary>marked = the gold frame (their turn, or the selected hero between fights); ready = the green one (can take the shared turn).</summary>
    public void Show(World world, int creature, bool marked, bool ready)
    {
        Creature = creature;
        CharacterSheet sheet = world.Creatures[creature].Sheet;
        Token token = world.Tokens.Tokens[creature];
        _portrait.Show(sheet.Name, token.Color.ToGodot(), sheet.Down, Portraits.Of(world, creature), Portraits.FocusOf(world, creature));
        _name.Text = sheet.Name;
        _hp.MaxValue = Mathf.Max(1, sheet.MaxHp);
        _hp.Value = Mathf.Max(0, sheet.Hp);
        // down, the card shows the system's own track (death saves, a dying value) in place of HP
        (string downed, string downedLine) = sheet.DownedText(world.Rules);
        _hpText.Text = downed.Length > 0 ? downed : sheet.Tracks.Count > 0 ? HudText.TrackBoxes(sheet) : $"{Mathf.Max(0, sheet.Hp)} / {sheet.MaxHp}";
        _turn.Visible = marked;
        _ready.Visible = ready && !marked;

        var conditions = HudText.Conditions(world, sheet);
        while (_badges.Count < conditions.Count)
        {
            var badge = new ConditionBadge();
            _conditions.AddChild(badge);
            _badges.Add(badge);
        }
        for (int i = 0; i < _badges.Count; i++)
        {
            _badges[i].Visible = i < conditions.Count;
            if (i < conditions.Count)
            {
                _badges[i].Show(conditions[i].Definition.Name);
            }
        }

        TipTitle = sheet.Name;
        TipMeta = HudText.Health(world, sheet);
        TipBody = HudText.ConditionLines(world, sheet);
        TipWarning = downedLine;
    }
}
