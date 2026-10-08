namespace Yorehold.Rules;

/// <summary>One creature standing in the fight, as the scoring sees it.</summary>
public sealed record TacticalUnit
{
    public int Team { get; init; }
    public Cell At { get; init; }
    public int Hp { get; init; } = 1;
    public int MaxHp { get; init; } = 1;
    public int ArmorClass { get; init; } = 10;
    public int AttackBonus { get; init; }
    public float AverageDamage { get; init; } = 1;
    /// <summary>Squares per turn.</summary>
    public int Speed { get; init; } = 6;
    public bool Leader { get; init; }
}

/// <summary>Everything the scorer knows on one creature's turn.</summary>
public sealed class TacticalView
{
    /// <summary>Everyone still standing, both sides.</summary>
    public List<TacticalUnit> Units { get; set; } = new();
    public int Self { get; set; }
    /// <summary>Actions it has left this turn.</summary>
    public int Actions { get; set; } = 1;
    public int StrikeCost { get; set; } = 1;
    /// <summary>Squares it can end on with the movement it has (its own costs 0).</summary>
    public Dictionary<Cell, float> Reach { get; set; } = new();
    /// <summary>The same with a dash on top; empty = it can't dash.</summary>
    public Dictionary<Cell, float> DashReach { get; set; } = new();
    /// <summary>Squares of walking from each cell to the nearest foe.</summary>
    public Dictionary<Cell, float> FoeDistance { get; set; } = new();
    /// <summary>How many its side began the fight with.</summary>
    public int SideAtStart { get; set; } = 1;
    public bool HadLeader { get; set; }
    /// <summary>It broke on an earlier turn and keeps running.</summary>
    public bool Fleeing { get; set; }
    /// <summary>How it reacts now its morale broke (one of the onBreak names). Empty: the first one listed.</summary>
    public string BreakAs { get; set; } = "";
    /// <summary>Squares of walking to the nearest ally not yet fighting, for "alarm"; empty = none.</summary>
    public Dictionary<Cell, float> AllyDistance { get; set; } = new();
    /// <summary>The rules system's attack roll, so the odds are its own; null = a d20 against AC.</summary>
    public CheckKind? Attack { get; set; }
}

public enum ChoiceKind
{
    Hold,
    Attack,
    Advance,
    Flee,
    Alarm,
    Surrender,
}

public sealed record TacticalChoice
{
    public ChoiceKind Kind { get; init; }
    /// <summary>Where it ends its move; its own cell = stays put.</summary>
    public Cell Cell { get; init; }
    /// <summary>Index into the view's units, for Attack.</summary>
    public int Target { get; init; }
    /// <summary>The move needs a dash (then the Strike, for Attack).</summary>
    public bool Dash { get; init; }
    public float Score { get; init; }
}

/// <summary>
/// How a creature picks what to do on its turn: every option is scored with its profile's weights
/// and the best wins, as the C++ framework's utility model does. Sums are kept in float like the
/// C++ so the same seed picks the same option.
/// </summary>
public static class Tactics
{
    private const float Unreachable = 1e6f;

    /// <summary>
    /// The system's chance to hit, counted from its dice and outcomes; without one, a d20 plus the
    /// bonus against AC where a 1 always misses and a 20 always hits.
    /// </summary>
    public static float HitChance(TacticalUnit attacker, TacticalUnit target, CheckKind? attack = null)
    {
        if (attack != null)
        {
            return (float)attack.ChanceToPass(attacker.AttackBonus, target.ArmorClass);
        }
        return Math.Clamp((21 + attacker.AttackBonus - target.ArmorClass) / 20.0f, 0.05f, 0.95f);
    }

    /// <summary>Its morale has broken (see the profile's flee numbers).</summary>
    public static bool WantsToFlee(AiProfile profile, TacticalView view)
    {
        if (view.Fleeing)
        {
            return true;
        }
        TacticalUnit me = view.Units[view.Self];
        if ((float)profile.FleeHp > 0 && me.Hp <= (float)profile.FleeHp * me.MaxHp)
        {
            return true;
        }
        int standing = 0;
        bool leader = false;
        foreach (TacticalUnit unit in view.Units.Where(u => u.Team == me.Team))
        {
            standing++;
            leader |= unit.Leader;
        }
        int started = Math.Max(view.SideAtStart, standing);
        if (started - standing >= (float)profile.FleeLosses * started)
        {
            return true;
        }
        return profile.FleeLeaderless && view.HadLeader && !leader;
    }

    /// <summary>One of the profile's onBreak reactions, picked by weight.</summary>
    public static string PickBreak(AiProfile profile, Rng random)
    {
        float total = 0;
        foreach (KeyValuePair<string, double> entry in profile.OnBreak)
        {
            total += (float)entry.Value;
        }
        if (profile.OnBreak.Count == 0 || total <= 0)
        {
            return "flee";
        }
        float roll = total * random.Range(0, 9999) / 10000.0f;
        foreach (KeyValuePair<string, double> entry in profile.OnBreak)
        {
            if (roll < (float)entry.Value)
            {
                return entry.Key;
            }
            roll -= (float)entry.Value;
        }
        return profile.OnBreak[^1].Key;
    }

    /// <summary>The profile's choice. considered gets every option, best first, for debug notes.</summary>
    public static TacticalChoice Decide(AiProfile profile, TacticalView view, Grid grid, Rng random, List<TacticalChoice>? considered = null)
    {
        // "utility" is the only model so far; profiles only load with that one.
        TacticalUnit me = view.Units[view.Self];
        var options = new List<TacticalChoice>();
        float damageWeight = (float)profile.Damage;
        float dangerWeight = (float)profile.Danger;

        // What standing on a cell is likely to cost before its next turn: foes next to it hit now,
        // foes that can walk up to it might.
        float Danger(Cell cell)
        {
            float total = 0;
            foreach (TacticalUnit foe in view.Units)
            {
                if (foe.Team == me.Team)
                {
                    continue;
                }
                float distance = grid.Distance(cell, foe.At);
                float share = distance <= 1.01f ? 1.0f : distance <= foe.Speed + 1.01f ? 0.5f : 0.0f;
                total += share * HitChance(foe, me, view.Attack) * foe.AverageDamage;
            }
            return total;
        }
        float here = CostAt(view.FoeDistance, me.At);

        string breakAs = view.BreakAs.Length > 0 ? view.BreakAs : profile.OnBreak.Count == 0 ? "flee" : profile.OnBreak[0].Key;
        if (breakAs != "fight" && WantsToFlee(profile, view))
        {
            Dictionary<Cell, float> cells = view.Actions >= 1 && view.DashReach.Count > 0 ? view.DashReach : view.Reach;
            TacticalChoice GiveUp()
            {
                var yield = new TacticalChoice { Kind = ChoiceKind.Surrender, Cell = me.At };
                considered?.Add(yield);
                return yield;
            }
            if (breakAs == "surrender")
            {
                return GiveUp();
            }

            // Raising the alarm: head for the nearest allies not yet fighting. None left = just run.
            float allyHere = CostAt(view.AllyDistance, me.At);
            if (breakAs == "alarm" && allyHere < Unreachable)
            {
                var alarm = new TacticalChoice { Kind = ChoiceKind.Alarm, Cell = me.At, Score = -allyHere };
                foreach ((Cell cell, float cost) in Ordered(cells))
                {
                    float score = -CostAt(view.AllyDistance, cell) - cost * 0.01f;
                    if (score > alarm.Score)
                    {
                        alarm = alarm with { Cell = cell, Score = score, Dash = !view.Reach.ContainsKey(cell) };
                    }
                }
                considered?.Add(alarm);
                return alarm;
            }

            // As far from every foe as it can get. Nowhere better = cornered, so it fights (or gives up).
            var flee = new TacticalChoice { Kind = ChoiceKind.Flee, Cell = me.At, Score = here };
            foreach ((Cell cell, float cost) in Ordered(cells))
            {
                float away = CostAt(view.FoeDistance, cell);
                if (away >= Unreachable)
                {
                    continue; // sealed off from every foe: not somewhere to run to
                }
                float score = away - cost * 0.01f;
                if (score > flee.Score)
                {
                    flee = flee with { Cell = cell, Score = score, Dash = !view.Reach.ContainsKey(cell) };
                }
            }
            if (flee.Cell != me.At && flee.Score >= here + 1)
            {
                considered?.Add(flee);
                return flee;
            }
            if (profile.SurrenderCornered)
            {
                return GiveUp();
            }
        }

        // Stay where it is and do nothing: what everything else has to beat.
        options.Add(new TacticalChoice { Kind = ChoiceKind.Hold, Cell = me.At, Score = -dangerWeight * Danger(me.At) });

        if (view.Actions >= view.StrikeCost)
        {
            // With an action to spare it can dash in first.
            Dictionary<Cell, float> cells = view.Actions >= view.StrikeCost + 1 && view.DashReach.Count > 0 ? view.DashReach : view.Reach;
            // "nearby" weighs the walk against the shortest one to anybody, so a long way to the
            // only target still beats standing about.
            float shortest = 1e9f;
            foreach (TacticalUnit target in view.Units.Where(u => u.Team != me.Team))
            {
                foreach ((Cell cell, float cost) in cells)
                {
                    if (Beside(grid, cell, target.At))
                    {
                        shortest = Math.Min(shortest, cost);
                    }
                }
            }
            for (int t = 0; t < view.Units.Count; t++)
            {
                TacticalUnit target = view.Units[t];
                if (target.Team == me.Team)
                {
                    continue;
                }
                float expected = HitChance(me, target, view.Attack) * me.AverageDamage;
                int friends = 0;
                int mine = 0;
                for (int other = 0; other < view.Units.Count; other++)
                {
                    if (other == t || other == view.Self || !Beside(grid, view.Units[other].At, target.At))
                    {
                        continue;
                    }
                    if (view.Units[other].Team == target.Team)
                    {
                        friends++;
                    }
                    else
                    {
                        mine++;
                    }
                }
                float who = damageWeight * expected
                    + (float)profile.Finish * (expected >= target.Hp ? 4.0f : 0.0f)
                    + (float)profile.Weak * 3.0f * (1.0f - (float)target.Hp / Math.Max(1, target.MaxHp))
                    + (float)profile.Isolated * (friends == 0 ? 2.0f : 0.0f)
                    + (float)profile.Pack * 1.5f * mine;
                foreach ((Cell cell, float cost) in cells)
                {
                    if (!Beside(grid, cell, target.At))
                    {
                        continue;
                    }
                    // Hitting someone beats walking about, whoever it is.
                    float score = 5 + who - (float)profile.Nearby * (cost - shortest) * 0.5f - dangerWeight * Danger(cell) - cost * 0.01f;
                    options.Add(new TacticalChoice
                    {
                        Kind = ChoiceKind.Attack, Cell = cell, Target = t, Dash = !view.Reach.ContainsKey(cell), Score = score,
                    });
                }
            }
        }

        // Nobody to hit from here: close in, dashing if that gets nearer.
        if (!options.Exists(o => o.Kind == ChoiceKind.Attack) && here < Unreachable)
        {
            Dictionary<Cell, float> cells = view.Actions >= 1 && view.DashReach.Count > 0 ? view.DashReach : view.Reach;
            foreach ((Cell cell, float cost) in cells)
            {
                float closer = here - CostAt(view.FoeDistance, cell);
                if (closer <= 0)
                {
                    continue;
                }
                options.Add(new TacticalChoice
                {
                    Kind = ChoiceKind.Advance, Cell = cell, Dash = !view.Reach.ContainsKey(cell),
                    Score = closer - dangerWeight * 0.25f * Danger(cell) - cost * 0.01f,
                });
            }
        }

        // A fixed order before the noise goes on, so the same seed picks the same option whatever
        // order the cell maps list their cells in.
        options = options.OrderBy(o => o.Kind).ThenBy(o => o.Cell.Y).ThenBy(o => o.Cell.X).ThenBy(o => o.Target).ToList();
        if ((float)profile.Random > 0)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Kind != ChoiceKind.Hold)
                {
                    options[i] = options[i] with { Score = options[i].Score + (float)profile.Random * random.Range(0, 1000) / 1000.0f };
                }
            }
        }
        options = options.OrderByDescending(o => o.Score).ToList(); // stable, like std::stable_sort
        TacticalChoice best = options[0];
        if (considered != null)
        {
            considered.Clear();
            considered.AddRange(options);
        }
        return best;
    }

    private static bool Beside(Grid grid, Cell a, Cell b) => grid.Distance(a, b) <= 1.01f;

    private static float CostAt(Dictionary<Cell, float> costs, Cell cell)
    {
        return costs.TryGetValue(cell, out float found) ? found : Unreachable;
    }

    // Cells top to bottom, left to right, so the first of equal scores is the same every run.
    private static IEnumerable<(Cell Cell, float Cost)> Ordered(Dictionary<Cell, float> cells)
    {
        return cells.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).Select(p => (p.Key, p.Value));
    }
}
