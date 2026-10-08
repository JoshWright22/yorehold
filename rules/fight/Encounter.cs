namespace Yorehold.Rules;

/// <summary>What a combatant still has this turn. How many actions a turn brings is the ruleset's.</summary>
public sealed class TurnBudget
{
    public int Actions { get; set; } = 1;
    /// <summary>Rulesets without bonus actions start the turn with this false.</summary>
    public bool BonusAction { get; set; } = true;
    /// <summary>Comes back at the start of its own turn.</summary>
    public bool Reaction { get; set; } = true;
    /// <summary>Squares.</summary>
    public int MovementLeft { get; set; }
    /// <summary>Attacks made this turn, for the ruleset's attack penalty.</summary>
    public int Attacks { get; set; }

    public TurnBudget Copy() => (TurnBudget)MemberwiseClone();
}

public sealed class Combatant
{
    public Combatant(CharacterSheet sheet, int team)
    {
        Sheet = sheet;
        Team = team;
    }

    public CharacterSheet Sheet { get; }
    /// <summary>Same team = allies.</summary>
    public int Team { get; }
    public int Initiative { get; set; }
    public RollResult InitiativeRoll { get; set; } = new();
    public TurnBudget Budget { get; set; } = new();
    /// <summary>Left the fight without going down (gave up, got away): no more turns, can't be attacked.</summary>
    public bool Out { get; set; }
    /// <summary>Caught unaware: loses its first turn.</summary>
    public bool Surprised { get; set; }
    /// <summary>Shared turns: ended or skipped in this round.</summary>
    public bool TurnDone { get; set; }
    public bool Standing => !Out && !Sheet.Down;
}

public sealed class AttackResult
{
    public RollResult AttackRoll { get; set; } = new();
    public RollResult DamageRoll { get; set; } = new();
    public bool Hit { get; set; }
    public bool Critical { get; set; }
    public bool TargetDropped { get; set; }
}

/// <summary>
/// Turn-based combat: initiative order, rounds and each combatant's action economy, the C++
/// framework's Encounter. Every roll goes through its own Rng, so a seeded fight replays the same.
/// </summary>
public sealed class Encounter
{
    private readonly Ruleset _rules;
    private readonly List<Combatant> _order = new();
    private readonly List<string> _log = new();
    private int _current;
    private int _blockFirst;
    private int _blockEnd;
    private int _blockTeam;

    public Encounter(Ruleset rules, ulong seed)
    {
        _rules = rules;
        Random = new Rng(seed);
    }

    public Rng Random { get; }
    public bool Started { get; private set; }
    public int Round { get; private set; }
    public int CurrentIndex => _current;
    public Combatant Current => _order[_current];
    public IReadOnlyList<Combatant> Order => _order;
    public IReadOnlyList<string> Log => _log;
    /// <summary>The active block is [BlockFirst, BlockEnd). Without shared turns it holds only the current one.</summary>
    public int BlockFirst => _rules.SharedTurns ? _blockFirst : _current;
    public int BlockEnd => _rules.SharedTurns ? _blockEnd : _current + 1;
    /// <summary>Goes up each time a new block (or turn) begins.</summary>
    public ulong BlockSerial { get; private set; }

    public void Add(CharacterSheet sheet, int team)
    {
        if (Started || _order.Exists(c => ReferenceEquals(c.Sheet, sheet)))
        {
            return;
        }
        _order.Add(new Combatant(sheet, team));
    }

    /// <summary>Joins a fight already going (reinforcements): rolls initiative and takes its place in the order.</summary>
    public void Join(CharacterSheet sheet, int team)
    {
        if (!Started)
        {
            Add(sheet, team);
            return;
        }
        if (_order.Exists(c => ReferenceEquals(c.Sheet, sheet)))
        {
            return;
        }
        var joining = new Combatant(sheet, team);
        joining.InitiativeRoll = _rules.Checks.Kind("initiative").Roll(sheet.InitiativeModifier(_rules), Advantage.None, Random);
        joining.Initiative = joining.InitiativeRoll.Total;
        AddLog($"{sheet.Name} joins the fight, initiative {joining.InitiativeRoll.Describe()}");
        // After everyone who rolled at least as high; whoever's turn it is keeps it.
        int at = 0;
        while (at < _order.Count && _order[at].Initiative >= joining.Initiative)
        {
            at++;
        }
        if (_rules.SharedTurns)
        {
            // Joining must not split a block that has begun or give anyone a second turn.
            joining.TurnDone = true;
            if (at <= _blockFirst)
            {
                _blockFirst++;
                _blockEnd++;
            }
            else if (at < _blockEnd)
            {
                _blockEnd++;
            }
        }
        _order.Insert(at, joining);
        if (at <= _current)
        {
            _current++;
        }
    }

    /// <summary>Before Start: everyone on team loses their first turn.</summary>
    public void Surprise(int team)
    {
        if (Started)
        {
            return;
        }
        foreach (Combatant c in _order.Where(c => c.Team == team))
        {
            c.Surprised = true;
        }
    }

    /// <summary>Takes a combatant out for good without dropping it (surrendered, fled). On its own turn, call NextTurn after.</summary>
    public void Withdraw(int index, string why = "")
    {
        if (index < 0 || index >= _order.Count || _order[index].Out)
        {
            return;
        }
        _order[index].Out = true;
        if (why.Length > 0)
        {
            AddLog($"{_order[index].Sheet.Name} {why}");
        }
    }

    /// <summary>Rolls initiative (d20 plus the initiative modifier, ties to the higher modifier) and starts round 1.</summary>
    public void Start()
    {
        if (Started || _order.Count == 0)
        {
            return;
        }
        foreach (Combatant c in _order)
        {
            c.InitiativeRoll = _rules.Checks.Kind("initiative").Roll(c.Sheet.InitiativeModifier(_rules), Advantage.None, Random);
            c.Initiative = c.InitiativeRoll.Total;
            AddLog($"{c.Sheet.Name} initiative {c.InitiativeRoll.Describe()}");
        }
        // A stable sort, like std::stable_sort, so equal rolls keep the order they were added in.
        List<Combatant> sorted = _order
            .Select((c, i) => (c, i))
            .OrderByDescending(p => p.c.Initiative)
            .ThenByDescending(p => p.c.Sheet.InitiativeModifier(_rules))
            .ThenBy(p => p.i)
            .Select(p => p.c)
            .ToList();
        _order.Clear();
        _order.AddRange(sorted);
        Started = true;
        AddLog("Round 1");
        foreach (Combatant c in _order.Where(c => c.Surprised && c.Standing))
        {
            AddLog($"{c.Sheet.Name} is caught by surprise");
        }
        // Whoever goes first, unless they're down or surprised (then the next one that can).
        _current = _order.Count - 1;
        Round = 0;
        NextTurn();
        if (Round == 0)
        {
            // Nothing to fight (one side only): over before it begins.
            Round = 1;
            _current = 0;
            if (_order[_current].Standing)
            {
                BeginTurn();
            }
        }
    }

    public bool CanSelectTurn(int index)
    {
        return _rules.SharedTurns && Started && !Finished && index >= _blockFirst && index < _blockEnd
            && _order[index].Team == _blockTeam && !_order[index].TurnDone && _order[index].Standing;
    }

    /// <summary>Hands the shared turn to another member of the block; everyone keeps their budget and conditions.</summary>
    public bool SelectTurn(int index)
    {
        if (!CanSelectTurn(index))
        {
            return false;
        }
        if (_current == index)
        {
            return true;
        }
        _current = index;
        AddLog($"{_order[_current].Sheet.Name} resumes the shared turn");
        return true;
    }

    /// <summary>
    /// Ends the current turn and skips anyone down, out or surprised. Conditions follow the fight:
    /// they hear turnStart and turnEnd, count down when a round ends, and "cantAct" or "cantMove"
    /// take a turn's actions or movement.
    /// </summary>
    public void NextTurn()
    {
        if (!Started || Finished)
        {
            return;
        }
        if (_rules.SharedTurns)
        {
            NextSharedTurn();
            return;
        }
        if (Round > 0 && _order[_current].Standing)
        {
            ConditionsEnded(_order[_current].Sheet, _order[_current].Sheet.ConditionEvent(_rules, "turnEnd"));
        }
        // Twice round: a surprised combatant passes once and can then take the next turn that comes.
        for (int tries = 0; tries < _order.Count * 2 + 1; tries++)
        {
            _current++;
            if (_current >= _order.Count)
            {
                _current = 0;
                if (Round > 0)
                {
                    EndRound();
                }
                else
                {
                    Round = 1;
                }
            }
            Combatant c = _order[_current];
            DeathTurn(c);
            if (!c.Standing)
            {
                continue;
            }
            if (c.Surprised)
            {
                c.Surprised = false;
                AddLog($"{c.Sheet.Name} is surprised and loses the turn");
                continue;
            }
            break;
        }
        BeginTurn();
    }

    /// <summary>The current combatant has cost actions left.</summary>
    public bool CanAct(int cost = 1)
    {
        return Started && _order.Count > 0 && _order[_current].Standing && !Finished && _order[_current].Budget.Actions >= cost;
    }

    public int StrikeCost => _order.Count == 0 ? 1 : _order[_current].Sheet.StrikeCost(_rules);

    public bool CanStrike => CanAct(StrikeCost);

    /// <summary>Spends StrikeCost actions. Attack roll against AC; a natural 20 always hits and doubles the dice.</summary>
    public AttackResult Attack(int targetIndex)
    {
        var result = new AttackResult();
        if (!CanStrike || targetIndex < 0 || targetIndex >= _order.Count || !_order[targetIndex].Standing)
        {
            return result;
        }
        CharacterSheet self = _order[_current].Sheet;
        CharacterSheet target = _order[targetIndex].Sheet;
        _order[_current].Budget.Actions -= StrikeCost;

        CheckKind attack = _rules.Checks.Kind(CheckRules.Attack);
        result.AttackRoll = attack.Roll(self.AttackModifier(_rules), self.AttackAdvantage(_rules, target), Random);
        List<string> afterAttack = self.ConditionEvent(_rules, "attack"); // they still count for this roll
        int ac = target.ArmorClass(_rules);
        CheckOutcome outcome = attack.Resolve(result.AttackRoll, ac);
        result.Critical = outcome.Critical;
        result.Hit = outcome.Passes;
        string line = $"{self.Name} attacks {target.Name} (AC {ac}): {result.AttackRoll.Describe()}";
        if (!result.Hit)
        {
            AddLog(line + " - miss");
            ConditionsEnded(self, afterAttack);
            return result;
        }
        DiceExpression? damage = DiceExpression.Parse(self.DamageDice(_rules));
        if (damage != null && result.Critical)
        {
            for (int i = 0; i < damage.Terms.Count; i++)
            {
                if (damage.Terms[i].Sides > 0)
                {
                    damage.Terms[i] = damage.Terms[i] with { Count = damage.Terms[i].Count * 2 };
                }
            }
        }
        result.DamageRoll = damage != null ? Dice.Roll(damage, Random) : new RollResult();
        int dealt = Math.Max(1, result.DamageRoll.Total);
        result.TargetDropped = target.TakeDamage(dealt, _rules, result.Critical);
        line += (result.Critical ? " - CRITICAL HIT, " : " - hit, ") + result.DamageRoll.Describe() + " damage";
        if (result.TargetDropped)
        {
            line += $". {target.Name} goes down!";
        }
        AddLog(line);
        ConditionsEnded(self, afterAttack);
        ConditionsEnded(target, target.ConditionEvent(_rules, "damage"));
        return result;
    }

    /// <summary>Stride: spends an action to move its speed again.</summary>
    public bool Dash()
    {
        if (!CanAct())
        {
            return false;
        }
        Combatant c = _order[_current];
        c.Budget.Actions--;
        c.Budget.MovementLeft += c.Sheet.SpeedSquares(_rules);
        AddLog($"{c.Sheet.Name} dashes");
        return true;
    }

    /// <summary>Spends the bonus action; false if it's gone or the rules have none.</summary>
    public bool SpendBonusAction()
    {
        if (!Started || Finished || _order.Count == 0 || !_order[_current].Budget.BonusAction)
        {
            return false;
        }
        _order[_current].Budget.BonusAction = false;
        return true;
    }

    /// <summary>Spends actions on anything else (Defend, Help, Interact...); false if there aren't enough.</summary>
    public bool SpendActions(int count)
    {
        if (count < 0 || !CanAct(count))
        {
            return false;
        }
        _order[_current].Budget.Actions -= count;
        return true;
    }

    /// <summary>Spends squares of movement; false if there isn't enough.</summary>
    public bool SpendMovement(int squares)
    {
        if (!Started || _order.Count == 0 || Finished || squares < 0 || !_order[_current].Standing)
        {
            return false;
        }
        TurnBudget budget = _order[_current].Budget;
        if (squares > budget.MovementLeft)
        {
            return false;
        }
        budget.MovementLeft -= squares;
        return true;
    }

    /// <summary>Spends a combatant's reaction. False if it's gone already or they can't act.</summary>
    public bool UseReaction(int index)
    {
        if (!Started || Finished || index < 0 || index >= _order.Count || !_order[index].Standing || !_order[index].Budget.Reaction
            || _order[index].Sheet.HasFlag(_rules, "cantAct"))
        {
            return false;
        }
        _order[index].Budget.Reaction = false;
        return true;
    }

    /// <summary>Only one team is left standing.</summary>
    public bool Finished
    {
        get
        {
            int? team = null;
            foreach (Combatant c in _order)
            {
                if (!c.Standing)
                {
                    continue;
                }
                if (team != null && c.Team != team)
                {
                    return false;
                }
                team = c.Team;
            }
            return true;
        }
    }

    /// <summary>The one team still standing; -1 while two are, or if nobody is.</summary>
    public int WinningTeam
    {
        get
        {
            int team = -1;
            foreach (Combatant c in _order)
            {
                if (!c.Standing)
                {
                    continue;
                }
                if (team >= 0 && c.Team != team)
                {
                    return -1;
                }
                team = c.Team;
            }
            return team;
        }
    }

    private void BeginTurn()
    {
        RefreshTurn(_order[_current]);
        BlockSerial++;
        AddLog($"{_order[_current].Sheet.Name}'s turn");
    }

    private void RefreshTurn(Combatant c)
    {
        ConditionsEnded(c.Sheet, c.Sheet.ConditionEvent(_rules, "turnStart"));
        // conditions add or take actions through the "actions" stat (quickened +1, slowed -1)
        c.Budget = new TurnBudget
        {
            Actions = Math.Clamp(_rules.ActionsPerTurn + c.Sheet.Stats.Integer("actions"), 0, 10),
            BonusAction = _rules.BonusActions,
            Reaction = true,
            MovementLeft = _rules.FreeMove ? c.Sheet.SpeedSquares(_rules) : 0,
        };
        if (c.Sheet.HasFlag(_rules, "cantAct"))
        {
            c.Budget.Actions = 0;
            c.Budget.BonusAction = false;
            c.Budget.Reaction = false;
        }
        if (c.Sheet.HasFlag(_rules, "cantMove"))
        {
            c.Budget.MovementLeft = 0;
        }
    }

    private void DeathTurn(Combatant c)
    {
        if (c.Out)
        {
            return;
        }
        RollResult? result = c.Sheet.RollDeathSave(_rules, Random);
        if (result == null)
        {
            return;
        }
        CharacterSheet sheet = c.Sheet;
        string state = sheet.Death.Dead ? "dead"
            : sheet.Death.Stable ? "stable"
            : !sheet.Down ? "gets up"
            : _rules.Death.Track != null ? $"dying {sheet.Death.Dying}"
            : $"{sheet.Death.Successes} successes, {sheet.Death.Failures} failures";
        string roll = _rules.Death.Track != null ? "recovery check" : $"death save (DC {_rules.Death.SaveDc})";
        AddLog($"{sheet.Name} {roll}: {result.Describe()} - {state}");
    }

    private void EndRound()
    {
        foreach (Combatant c in _order)
        {
            ConditionsEnded(c.Sheet, c.Sheet.EndRound(_rules, Random));
        }
        Round++;
        AddLog($"Round {Round}");
    }

    private void NextSharedTurn()
    {
        if (Round > 0)
        {
            Combatant ended = _order[_current];
            ended.TurnDone = true;
            if (ended.Standing)
            {
                ConditionsEnded(ended.Sheet, ended.Sheet.ConditionEvent(_rules, "turnEnd"));
            }
            for (int i = _blockFirst; i < _blockEnd; i++)
            {
                if (CanSelectTurn(i))
                {
                    _current = i;
                    AddLog($"{_order[i].Sheet.Name}'s turn");
                    return;
                }
            }
        }
        int at = Round > 0 ? _blockEnd : _order.Count;
        // A whole surprised side may pass; then the next round supplies a turn.
        for (int tries = 0; tries < _order.Count * 2 + 1; tries++)
        {
            if (at >= _order.Count)
            {
                at = 0;
                if (Round > 0)
                {
                    EndRound();
                }
                else
                {
                    Round = 1;
                }
                foreach (Combatant c in _order)
                {
                    c.TurnDone = false;
                }
            }
            _blockFirst = at;
            _blockTeam = _order[at].Team;
            while (at < _order.Count && _order[at].Team == _blockTeam)
            {
                at++;
            }
            _blockEnd = at;
            int? first = null;
            for (int i = _blockFirst; i < _blockEnd; i++)
            {
                Combatant c = _order[i];
                if (!c.TurnDone)
                {
                    DeathTurn(c);
                }
                if (!c.Standing)
                {
                    c.TurnDone = true;
                }
                if (c.TurnDone)
                {
                    continue;
                }
                if (c.Surprised)
                {
                    c.Surprised = false;
                    c.TurnDone = true;
                    AddLog($"{c.Sheet.Name} is surprised and loses the turn");
                    continue;
                }
                RefreshTurn(c);
                first ??= i;
            }
            if (first is int found)
            {
                _current = found;
                BlockSerial++;
                AddLog($"{_order[_current].Sheet.Name}'s turn");
                return;
            }
        }
    }

    private void ConditionsEnded(CharacterSheet sheet, List<string> ids)
    {
        foreach (string id in ids)
        {
            AddLog($"{sheet.Name} is no longer {_rules.Condition(id)?.Name ?? id}");
        }
    }

    private void AddLog(string line)
    {
        _log.Add(line);
    }
}
