using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// What a fight draws over the tokens: an HP bar above everyone in it, a ring on whoever's turn it
/// is, and rings on who a picked action can be aimed at. It sits above the lighting so the bars
/// read the same in a dark room.
/// </summary>
public partial class TokenBarsView : Node2D
{
    [Export] public Color PartyBar { get; set; } = new(0.4f, 0.76f, 0.36f);
    [Export] public Color EnemyBar { get; set; } = new(0.82f, 0.22f, 0.18f);
    [Export] public Color TurnRing { get; set; } = new(1, 0.82f, 0.42f);
    [Export] public Color EnemyTarget { get; set; } = new(1, 0.4f, 0.3f);
    [Export] public Color AllyTarget { get; set; } = new(0.5f, 0.92f, 0.5f);

    private World? _world;
    private FightAim? _aim;
    private double _time;

    public void Bind(World world, FightAim aim)
    {
        _world = world;
        _aim = aim;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null || _aim == null || !_world.Fighting || _world.Encounter == null)
        {
            return;
        }
        World w = _world;
        int? current = w.CurrentCreature;
        ActionDefinition? action = _aim.HeroTurn && _aim.Action.Length > 0 ? w.FindAction(_aim.Action) : null;
        List<int> targets = action != null ? w.ValidTargets(action.Id) : new List<int>();
        float pulse = 2 + 2 * Mathf.Sin((float)_time * 5);

        for (int i = 0; i < w.Creatures.Count; i++)
        {
            if (w.OrderIndex(i) is not int place || w.Encounter.Order[place].Out)
            {
                continue;
            }
            WorldCreature who = w.Creatures[i];
            Token token = w.Tokens.Tokens[i];
            bool party = who.Team == 0;
            // enemies only where the party sees them; a hero who is down keeps a bar, a dead goblin doesn't
            if (party ? who.Sheet.Death.Dead : token.Floor != 0)
            {
                continue;
            }
            Vector2 at = token.Position.ToGodot();
            float r = token.Radius;

            if (i == current && !token.Selected)
            {
                DrawArc(at, r * 1.2f, 0, Mathf.Tau, 48, TurnRing, 3, true);
            }
            Color ring = party ? AllyTarget : EnemyTarget;
            if (targets.Contains(i))
            {
                DrawArc(at, r * 1.25f + pulse, 0, Mathf.Tau, 48, new Color(ring.R, ring.G, ring.B, 0.8f), 2.5f, true);
            }
            if (_aim.Target == i)
            {
                DrawArc(at, r * 1.25f, 0, Mathf.Tau, 48, ring, 5, true);
            }

            float width = Mathf.Max(r * 2, 44);
            var bar = new Rect2(at.X - width / 2, at.Y - r - 18, width, 7);
            DrawRect(bar.Grow(1.5f), new Color(0, 0, 0, 0.85f));
            DrawRect(bar, new Color(0.14f, 0.12f, 0.12f));
            float share = Mathf.Clamp(who.Sheet.Hp / (float)Mathf.Max(1, who.Sheet.MaxHp), 0, 1);
            DrawRect(new Rect2(bar.Position, new Vector2(width * share, bar.Size.Y)), party ? PartyBar : EnemyBar);
        }
    }
}
