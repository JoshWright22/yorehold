using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>One square in the turn order along the top, underlined in its side's colour; whoever's turn it is is framed.</summary>
public partial class InitiativeCard : TipButton
{
    [Export] public Vector2 NormalSize { get; set; } = new(40, 40);
    [Export] public Vector2 CurrentSize { get; set; } = new(40, 40);

    public int Creature { get; private set; } = -1;

    // the scene has all of these
    private PortraitView _portrait = null!;
    private ColorRect _side = null!;
    private ProgressBar _hp = null!;
    private Panel _ready = null!;
    private Panel _turn = null!;

    public override void _Ready()
    {
        base._Ready();
        _portrait = GetNode<PortraitView>("Portrait");
        _side = GetNode<ColorRect>("Side");
        _hp = GetNode<ProgressBar>("Hp");
        _ready = GetNode<Panel>("Ready");
        _turn = GetNode<Panel>("Turn");
    }

    /// <summary>done = it has had its turn this round, so it fades back.</summary>
    public void Show(World world, int creature, bool current, bool ready, bool done)
    {
        Creature = creature;
        WorldCreature who = world.Creatures[creature];
        CharacterSheet sheet = who.Sheet;
        _portrait.Show(sheet.Name, world.Tokens.Tokens[creature].Color.ToGodot(), sheet.Down, Portraits.Of(world, creature), Portraits.FocusOf(world, creature));
        // a card that has had its turn greys its side strip instead of going see-through
        _side.Color = done && !current ? Palette.Slate : who.Team == 0 ? Palette.Bone : Palette.Red;
        _hp.MaxValue = Mathf.Max(1, sheet.MaxHp);
        _hp.Value = Mathf.Max(0, sheet.Hp);
        _turn.Visible = current;
        _ready.Visible = ready && !current;
        CustomMinimumSize = current ? CurrentSize : NormalSize;

        TipTitle = sheet.Name;
        TipMeta = HudText.Health(world, sheet);
        TipBody = HudText.ConditionLines(world, sheet);
        TipWarning = "";
    }
}
