using System.Globalization;

namespace Yorehold.Rules;

/// <summary>
/// ui/animations/&lt;id&gt;.json: how one kind of action looks when it plays out, after its dice land
/// (R19): a short timeline of steps drawn as shapes in the screens' colours (the doer lunging,
/// a projectile, a flash on the target, a ring), one for a hit, one for a miss and optionally one
/// for a critical, and the moment of impact, when the numbers come out. A skin, a system or an
/// art pack may bring its own file of the same name.
/// </summary>
public sealed class UiAnimation
{
    public const string Folder = "ui/animations";
    public static readonly string[] Kinds = { "lunge", "recoil", "dodge", "projectile", "arc", "ring", "flash", "shake", "glow", "line" };

    public sealed record Step(string Do, double At, double Seconds, string On, double Distance, string Color, double Size, double Height);

    public string Id { get; init; } = "";
    /// <summary>How long it plays, seconds.</summary>
    public double Seconds { get; init; }
    /// <summary>When the blow lands: the held numbers come out then.</summary>
    public double Impact { get; init; }
    public List<Step> Hit { get; init; } = new();
    public List<Step> Miss { get; init; } = new();
    public List<Step> Critical { get; init; } = new();

    public static UiAnimation Read(ContentNode node)
    {
        node.RequireObject("an animation is a JSON object");
        node.Only("format", "version", "about", "id", "seconds", "impact", "hit", "miss", "critical");
        if (node.At("format").AsText() != "yorehold.animation")
        {
            throw node.Fail("format", "is \"yorehold.animation\"");
        }
        double seconds = node.Number("seconds", 0.6, 0.05, 5);
        List<Step> Steps(string key)
        {
            var steps = new List<Step>();
            foreach (ContentNode entry in node.Get(key)?.Items() ?? Array.Empty<ContentNode>())
            {
                entry.RequireObject("is a step: do, at, seconds");
                entry.Only("do", "at", "seconds", "on", "distance", "color", "size", "height");
                string kind = entry.At("do").AsName();
                if (!Kinds.Contains(kind))
                {
                    throw entry.Fail("do", "is one of " + string.Join(", ", Kinds));
                }
                string on = entry.Text("on", kind is "lunge" or "glow" ? "doer" : "target", 16);
                if (on is not "doer" and not "target")
                {
                    throw entry.Fail("on", "is \"doer\" or \"target\"");
                }
                steps.Add(new Step(kind, entry.Number("at", 0, 0, seconds), entry.Number("seconds", 0.2, 0.01, 5), on,
                    entry.Number("distance", 0.3, -5, 5), entry.Text("color", "greys.paper", 32), entry.Number("size", 1, 0, 20), entry.Number("height", 0, -10, 10)));
            }
            return steps;
        }
        List<Step> hit = Steps("hit");
        return new UiAnimation
        {
            Id = node.At("id").AsId(),
            Seconds = seconds,
            Impact = node.Number("impact", seconds / 2, 0, seconds),
            Hit = hit,
            Miss = node.Has("miss") ? Steps("miss") : hit,
            Critical = node.Has("critical") ? Steps("critical") : hit,
        };
    }

    /// <summary>
    /// The set an action plays: its own "animation", else its weapon's, else one picked from what
    /// it does: a spell by its area (burst, cone, a line as a bolt), a spell attack or damage as a
    /// bolt, healing as heal, a condition on an ally as buff and on a foe as debuff; a weapon attack
    /// by its weapon (a ranged one as arrow, a creature's own bite or claws as claw, else by its
    /// damage: slashing slash, piercing thrust, bludgeoning bash).
    /// </summary>
    public static string For(ActionDefinition action, bool spell, ItemDefinition? weapon)
    {
        if (action.Animation.Length > 0)
        {
            return action.Animation;
        }
        var steps = new List<EffectStep>();
        Gather(action.Effect.Steps, steps);
        bool attack = steps.Any(s => s.Kind == EffectKind.Roll && s.How == "attack");
        bool damage = steps.Any(s => s.Kind == EffectKind.Damage);
        bool heal = steps.Any(s => s.Kind is EffectKind.Heal or EffectKind.TempHp);
        bool condition = steps.Any(s => s.Kind == EffectKind.Condition && !s.Remove);
        bool usesWeapon = steps.Any(s => s.Kind == EffectKind.Damage && s.Amount == "weapon");
        if (spell)
        {
            if (action.Area is ActionArea area)
            {
                return area.Shape switch { AreaShape.Cone => "cone", AreaShape.Line => "bolt", _ => heal && !damage ? "heal" : "burst" };
            }
            return attack || damage ? "bolt" : heal ? "heal" : action.Side == ActionSide.Enemy ? "debuff" : "buff";
        }
        if (attack && (usesWeapon || weapon != null))
        {
            if (weapon == null)
            {
                return "slash";
            }
            if (weapon.Animation.Length > 0)
            {
                return weapon.Animation;
            }
            if (weapon.Range > 1)
            {
                return weapon.Traits.Contains("thrown") ? "thrown" : "arrow";
            }
            // a creature's own bite or claws: nothing to buy, nothing to carry
            if (weapon.Value == 0 && weapon.Weight == 0)
            {
                return "claw";
            }
            return weapon.DamageType switch { "piercing" => "thrust", "bludgeoning" => "bash", _ => "slash" };
        }
        if (attack || damage)
        {
            return action.Area != null ? "burst" : "bolt";
        }
        return heal ? "heal" : condition && action.Target == ActionTarget.Creature && action.Side == ActionSide.Enemy ? "debuff" : condition ? "buff" : "touch";
    }

    private static void Gather(List<EffectStep> from, List<EffectStep> into)
    {
        foreach (EffectStep step in from)
        {
            into.Add(step);
            Gather(step.Steps, into);
            foreach (EffectOption option in step.Options)
            {
                Gather(option.Steps, into);
            }
        }
    }
}
