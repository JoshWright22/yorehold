namespace Yorehold.Rules;

/// <summary>
/// One way a roll can come out. Passes is what "hit", "success" and a save that holds mean;
/// Critical marks the outcome critical damage goes with; Damage is the share of an effect's
/// damage a save with this outcome lets through (half on a held save, twice on a critical failure).
/// </summary>
public sealed record CheckOutcome(string Id, string Name, bool Passes, bool Critical, double Damage);

/// <summary>
/// How one kind of roll is made and read: its dice, the dice with advantage and disadvantage, the
/// outcomes it can have from worst to best, and the formula that picks one (the outcome's place in
/// the list, from "total", "die" (the dice without the modifier), "modifier" and "dc").
/// </summary>
public sealed class CheckKind
{
    private const int MostCombinations = 200000;

    public string Id { get; init; } = "";
    public DiceExpression Dice { get; init; } = DiceExpression.Parse("1d20")!;
    public DiceExpression WithAdvantage { get; init; } = DiceExpression.Parse("2d20kh1")!;
    public DiceExpression WithDisadvantage { get; init; } = DiceExpression.Parse("2d20kl1")!;
    public List<CheckOutcome> Outcomes { get; init; } = new();
    public Formula Degree { get; init; } = Formula.Parse("total >= dc", out _)!;
    /// <summary>
    /// The one it is rolled against rolls too: the DC is their roll of Dice plus their defence
    /// (AC for an attack, the passive score less its base for a check). Fate's active defence.
    /// </summary>
    public bool Opposed { get; init; }

    /// <summary>The DC a roll of this kind meets: the defence itself, or the defender's roll on it.</summary>
    public int Defence(int defence, Rng random, out RollResult? rolled)
    {
        rolled = Opposed ? Roll(defence, Advantage.None, random) : null;
        return rolled?.Total ?? defence;
    }

    public CheckOutcome? Outcome(string id) => Outcomes.Find(o => o.Id == id);

    private DiceExpression DiceFor(Advantage advantage) => advantage switch
    {
        Advantage.Advantage => WithAdvantage,
        Advantage.Disadvantage => WithDisadvantage,
        _ => Dice,
    };

    public RollResult Roll(int modifier, Advantage advantage, Rng random)
    {
        var expression = new DiceExpression();
        expression.Terms.AddRange(DiceFor(advantage).Terms);
        if (modifier != 0)
        {
            expression.Terms.Add(new DiceTerm(Math.Abs(modifier), 0, 0, 0, modifier < 0 ? -1 : 1));
        }
        return Yorehold.Rules.Dice.Roll(expression, random);
    }

    /// <summary>How a roll made with Roll came out against a DC.</summary>
    public CheckOutcome Resolve(RollResult roll, int dc)
    {
        // the modifier is the only flat part of a roll made here
        return Resolve(roll.Total - roll.Flat, roll.Flat, dc);
    }

    public CheckOutcome Resolve(int die, int modifier, int dc)
    {
        int place = Degree.Whole(name => name switch
        {
            "total" => die + modifier,
            "die" => die,
            "modifier" => modifier,
            "dc" => dc,
            _ => null,
        });
        return Outcomes[Math.Clamp(place, 0, Outcomes.Count - 1)];
    }

    /// <summary>
    /// The chance of each outcome, by its id, for a modifier against a DC: every way the dice can
    /// fall, counted. This is what the hit chance on screen, the fight AI and an adventure's
    /// balance are worked out from, whatever the system's dice are.
    /// </summary>
    public Dictionary<string, double> Odds(int modifier, int dc, Advantage advantage = Advantage.None)
    {
        var odds = Outcomes.ToDictionary(o => o.Id, _ => 0.0, StringComparer.Ordinal);
        // an opposed roll: every way the defender's dice can fall, each with its own DC
        Dictionary<int, double> defender = Opposed ? Spread(Dice) : new Dictionary<int, double> { [0] = 1 };
        foreach (KeyValuePair<int, double> die in Spread(DiceFor(advantage)))
        {
            foreach (KeyValuePair<int, double> against in defender)
            {
                odds[Resolve(die.Key, modifier, dc + against.Key).Id] += die.Value * against.Value;
            }
        }
        return odds;
    }

    /// <summary>The chance of an outcome that passes.</summary>
    public double ChanceToPass(int modifier, int dc, Advantage advantage = Advantage.None)
    {
        Dictionary<string, double> odds = Odds(modifier, dc, advantage);
        return Outcomes.Where(o => o.Passes).Sum(o => odds[o.Id]);
    }

    /// <summary>
    /// The odds as the aim shows them: the chance to pass, and the critical share when the
    /// system has a critical that passes ("55%, 5% critical"; a plain "55%" where it has none).
    /// </summary>
    public string OddsLine(Dictionary<string, double> odds)
    {
        static string Percent(double chance) => $"{(int)Math.Round(chance * 100)}%";
        double pass = Outcomes.Where(o => o.Passes).Sum(o => odds.GetValueOrDefault(o.Id));
        List<CheckOutcome> critical = Outcomes.Where(o => o.Passes && o.Critical).ToList();
        double crit = critical.Sum(o => odds.GetValueOrDefault(o.Id));
        return critical.Count == 0 || crit < 0.005 ? Percent(pass) : $"{Percent(pass)}, {Percent(crit)} critical";
    }

    /// <summary>
    /// The damage a roll of this kind deals on average, given each outcome's chance: nothing on
    /// one that doesn't pass, the damage's average on one that does, and on a critical the dice
    /// twice or what the system's critical formula makes of the average dice.
    /// </summary>
    public double ExpectedDamage(Dictionary<string, double> odds, DiceExpression damage, Formula? critical)
    {
        double dice = damage.DiceAverage();
        double flat = damage.Flat();
        double max = damage.Terms.Where(t => t.Sides != 0).Sum(t => t.Sign * t.Kept * t.HighFace);
        double hit = Math.Max(0, dice + flat);
        double crit = critical == null ? 2 * dice + flat : critical.Evaluate(name => name switch
        {
            "dice" => dice,
            "flat" => flat,
            "max" => max,
            _ => null,
        });
        return Outcomes.Where(o => o.Passes).Sum(o => odds.GetValueOrDefault(o.Id) * (o.Critical ? Math.Max(0, crit) : hit));
    }

    /// <summary>What dice can come to, and how likely each total is.</summary>
    public static Dictionary<int, double> Spread(DiceExpression dice)
    {
        var spread = new Dictionary<int, double> { [0] = 1 };
        foreach (DiceTerm term in dice.Terms)
        {
            Dictionary<int, double> part = SpreadOf(term);
            var next = new Dictionary<int, double>();
            foreach (KeyValuePair<int, double> a in spread)
            {
                foreach (KeyValuePair<int, double> b in part)
                {
                    next[a.Key + b.Key] = next.GetValueOrDefault(a.Key + b.Key) + a.Value * b.Value;
                }
            }
            spread = next;
        }
        return spread;
    }

    private static Dictionary<int, double> SpreadOf(DiceTerm term)
    {
        if (term.Sides == 0)
        {
            return new Dictionary<int, double> { [term.Sign * term.Count] = 1 };
        }
        var spread = new Dictionary<int, double>();
        double combinations = Math.Pow(term.Sides, term.Count);
        if (combinations > MostCombinations || term.Explode)
        {
            // too many to count: rolled instead, always the same rolls so the answer never changes
            var random = new Rng();
            var one = new DiceExpression();
            one.Terms.Add(term);
            const int Rolls = 20000;
            for (int i = 0; i < Rolls; i++)
            {
                int total = Yorehold.Rules.Dice.Roll(one, random).Total;
                spread[total] = spread.GetValueOrDefault(total) + 1.0 / Rolls;
            }
            return spread;
        }
        var faces = new int[term.Count];
        Array.Fill(faces, 1);
        while (true)
        {
            IEnumerable<int> shown = faces.Select(f => term.Fudge ? f - 2 : f);
            IEnumerable<int> kept = term.KeepHighest != 0 ? shown.OrderByDescending(f => f).Take(term.KeepHighest)
                : term.KeepLowest != 0 ? shown.OrderBy(f => f).Take(term.KeepLowest)
                : shown;
            int total = term.Sign * (term.SuccessAt == 0 ? kept.Sum() : kept.Count(f => f >= term.SuccessAt));
            spread[total] = spread.GetValueOrDefault(total) + 1 / combinations;
            int at = 0;
            while (at < faces.Length && faces[at] == term.Sides)
            {
                faces[at++] = 1;
            }
            if (at == faces.Length)
            {
                return spread;
            }
            faces[at]++;
        }
    }
}

/// <summary>
/// ruleset.json's "checks": how the system's rolls resolve. Without it the game plays as it always
/// has: a d20, an attack that misses on a 1 and is a critical hit on a 20, checks and saves that
/// pass on the DC or more, and critical damage that rolls the dice twice.
/// </summary>
public sealed class CheckRules
{
    public const string Attack = "attack";
    public const string Check = "check";
    public const string Save = "save";
    /// <summary>Critical damage that rolls its dice twice; anything else is a formula.</summary>
    public const string DoubleDice = "doubleDice";

    public Dictionary<string, CheckKind> Kinds { get; } = new(StringComparer.Ordinal);
    /// <summary>Null = the dice are rolled twice. Else the damage from "dice" (what the dice came to), "flat" (the rest) and "max" (the most the dice could show).</summary>
    public Formula? CriticalDamage { get; init; }

    /// <summary>The kind by id; one the system doesn't describe resolves like a plain check.</summary>
    public CheckKind Kind(string id) => Kinds.GetValueOrDefault(id) ?? Kinds[Check];

    public CheckRules()
    {
        Kinds[Attack] = new CheckKind
        {
            Id = Attack,
            Outcomes = { new("miss", "Miss", false, false, 1), new("hit", "Hit", true, false, 1), new("crit", "Critical hit", true, true, 1) },
            Degree = Formula.Parse("die == 1 ? 0 : die == 20 ? 2 : total >= dc ? 1 : 0", out _)!,
        };
        Kinds[Check] = new CheckKind
        {
            Id = Check,
            Outcomes = { new("failure", "Failure", false, false, 1), new("success", "Success", true, false, 1) },
        };
        Kinds[Save] = new CheckKind
        {
            Id = Save,
            Outcomes = { new("saveFailed", "Failed", false, false, 1), new("saveSucceeded", "Saved", true, false, 0.5) },
        };
    }

    /// <summary>Whether a roll of a kind, made with that kind's Roll, passes against a DC.</summary>
    public bool Passes(string kind, RollResult roll, int dc) => Kind(kind).Resolve(roll, dc).Passes;

    /// <summary>Every outcome id any kind has: what a step's "when" may name in this system.</summary>
    public bool HasOutcome(string id) => Kinds.Values.Any(kind => kind.Outcome(id) != null);

    public static CheckRules Read(ContentNode? found)
    {
        if (found is not ContentNode node)
        {
            return new CheckRules();
        }
        node.RequireObject("is an object of roll kinds");
        Formula? critical = null;
        if (node.Get("criticalDamage") is ContentNode damage && damage.AsText(2000) != DoubleDice)
        {
            critical = FormulaAt(damage, "dice", "flat", "max");
        }
        var rules = new CheckRules { CriticalDamage = critical };
        foreach (KeyValuePair<string, ContentNode> member in node.Members())
        {
            if (member.Key == "criticalDamage")
            {
                continue;
            }
            if (!ContentIds.IsId(member.Key))
            {
                throw member.Value.Fail("roll kinds are named with a-z, 0-9, - and _");
            }
            ContentNode kind = member.Value;
            kind.RequireObject("is an object with dice, outcomes and a degree");
            kind.Only("dice", "advantage", "disadvantage", "outcomes", "degree", "opposed");
            CheckKind standard = rules.Kind(member.Key);
            var outcomes = new List<CheckOutcome>();
            if (kind.Get("outcomes") is ContentNode list)
            {
                if (!list.IsArray || list.Count < 2 || list.Count > 12)
                {
                    throw list.Fail("is a list of 2 to 12 outcomes, worst first");
                }
                foreach (ContentNode entry in list.Items())
                {
                    entry.RequireObject("is an outcome with an id");
                    entry.Only("id", "name", "passes", "critical", "damage");
                    string id = entry.At("id").AsName();
                    if (outcomes.Any(o => o.Id == id))
                    {
                        throw entry.Fail("id", $"two outcomes have the id \"{id}\"");
                    }
                    bool passes = entry.Bool("passes", false);
                    outcomes.Add(new CheckOutcome(id, entry.Text("name", id, 64), passes, entry.Bool("critical", false),
                        entry.Number("damage", member.Key == Save && passes ? 0.5 : 1, 0, 100)));
                }
            }
            else
            {
                outcomes.AddRange(standard.Outcomes);
            }
            rules.Kinds[member.Key] = new CheckKind
            {
                Id = member.Key,
                Dice = DiceAt(kind, "dice", standard.Dice),
                WithAdvantage = DiceAt(kind, "advantage", kind.Has("dice") ? DiceAt(kind, "dice", standard.Dice) : standard.WithAdvantage),
                WithDisadvantage = DiceAt(kind, "disadvantage", kind.Has("dice") ? DiceAt(kind, "dice", standard.Dice) : standard.WithDisadvantage),
                Outcomes = outcomes,
                Opposed = kind.Bool("opposed", standard.Opposed),
                Degree = kind.Get("degree") is ContentNode degree ? FormulaAt(degree, "total", "die", "modifier", "dc")
                    : kind.Has("outcomes") ? throw kind.Fail("degree", "is needed with outcomes: the formula that picks one")
                    : standard.Degree,
            };
        }
        return rules;
    }

    private static DiceExpression DiceAt(ContentNode node, string key, DiceExpression fallback)
    {
        if (node.Get(key) is not ContentNode found)
        {
            return fallback;
        }
        return DiceExpression.Parse(found.AsText(200)) ?? throw found.Fail("is dice like \"1d20\" or \"3d6\"");
    }

    private static Formula FormulaAt(ContentNode node, params string[] names)
    {
        Formula formula = Formula.Parse(node.AsText(2000), out string error) ?? throw node.Fail(error);
        foreach (string name in formula.Names.Where(name => !names.Contains(name)))
        {
            throw node.Fail($"unknown name \"{name}\"; it can use {string.Join(", ", names)}");
        }
        return formula;
    }
}
