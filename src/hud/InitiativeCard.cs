using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>One portrait in the turn order along the top. Whoever's turn it is gets a bigger card.</summary>
public partial class InitiativeCard : TipButton
{
    [Export] public Vector2 NormalSize { get; set; } = new(50, 58);
    [Export] public Vector2 CurrentSize { get; set; } = new(66, 76);
    [Export] public Color PartyColor { get; set; } = new(0.36f, 0.6f, 0.86f);
    [Export] public Color EnemyColor { get; set; } = new(0.8f, 0.26f, 0.2f);

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
        _portrait.Show(sheet.Name, world.Tokens.Tokens[creature].Color.ToGodot(), sheet.Down);
        // a card that has had its turn greys its side strip instead of going see-through
        _side.Color = done && !current ? Palette.Slate : who.Team == 0 ? PartyColor : EnemyColor;
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
