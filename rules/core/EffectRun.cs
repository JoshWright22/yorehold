namespace Yorehold.Rules;

/// <summary>
/// One run of an effect: its steps in order, against whatever the host says is there. The order
/// of the rolls is the C++ client's, so the same seed plays out the same way.
/// </summary>
internal sealed class EffectRun
{
    // What the rolls made so far in this run say about one creature.
    private sealed class Outcome
    {
        public CheckOutcome? Attack;
        public CheckOutcome? Check;
        public CheckOutcome? Save;

        public bool Critical => Attack is { Critical: true };
        public bool? Saved => Save?.Passes;
    }

    private readonly Effect _effect;
    private readonly EffectHost _host;
    private readonly EffectContext _context;
    private readonly Ruleset _rules;
    private readonly Rng _random;
    private readonly EffectResult _result = new();
    private readonly Dictionary<int, Outcome> _outcomes = new();
    private readonly HashSet<(int Who, string Id)> _modified = new(); // modifiers this run has already started afresh

    public EffectRun(Effect effect, EffectHost host, EffectContext context)
    {
        _effect = effect;
        _host = host;
        _context = context;
        _rules = context.Rules;
        _random = context.Random;
    }

    public EffectResult Run()
    {
        Steps(_effect.Steps, _context.Targets, true);
        return _result;
    }

    private static bool IsEvent(string when) => ConditionDefinition.Events.Contains(when);

    private void Steps(List<EffectStep> list, List<int> subjects, bool top)
    {
        foreach (EffectStep step in list)
        {
            // Something done now leaves the steps that wait for an event alone; an event runs only those.
            bool skip = IsEvent(step.When) ? step.When != _context.Event : top && _context.Event.Length > 0;
            if (!skip)
            {
                One(step, subjects);
            }
        }
    }

    private Outcome OutcomeOf(int who)
    {
        if (!_outcomes.TryGetValue(who, out Outcome? outcome))
        {
            outcome = new Outcome();
            _outcomes[who] = outcome;
        }
        return outcome;
    }

    private List<int> AimedAt(EffectStep step, List<int> subjects)
    {
        return step.Target switch
        {
            "self" => new List<int> { _context.Self },
            "target" => subjects,
            _ => _host.Group(step.Target, _context),
        };
    }

    // The save that goes with the whole effect, made once by each creature a step asks it of.
    private void SaveIfAsked(int who, Outcome outcome)
    {
        CharacterSheet? sheet = _host.Sheet(who);
        if (outcome.Saved != null || _effect.Save.Ability.Length == 0 || sheet == null)
        {
            return;
        }
        int dc = _effect.Save.CasterDc ? _context.Dc : _effect.Save.Dc;
        RollResult roll = sheet.RollSave(_rules, _effect.Save.Ability, Advantage.None, _random);
        outcome.Save = _rules.Checks.Kind(CheckRules.Save).Resolve(roll, dc);
        _result.Events.Add(new EffectEvent
        {
            Kind = EffectEventKind.Save, Who = who, By = _context.Self, Roll = roll, Dc = dc,
            Success = outcome.Save.Passes, Outcome = outcome.Save.Id, Id = _effect.Save.Ability,
        });
    }

    // How an attack or check went, as far as `when` asks. Null if none was made about them.
    private bool? Rolled(string when, int who)
    {
        if (!_outcomes.TryGetValue(who, out Outcome? outcome))
        {
            return null;
        }
        // The words the game has always had mean what they did in any system: a hit is an
        // attack that passes, success a check that passes.
        if (when is "hit" or "miss" or "crit")
        {
            return outcome.Attack == null ? null
                : when == "hit" ? outcome.Attack.Passes
                : when == "miss" ? !outcome.Attack.Passes
                : outcome.Attack.Critical;
        }
        if (when is "success" or "failure" && outcome.Check != null)
        {
            return outcome.Check.Passes == (when == "success");
        }
        // Anything else is one of the system's own outcomes ("criticalFailure"), of whichever roll was made.
        CheckOutcome?[] made = { outcome.Check, outcome.Attack, outcome.Save };
        if (made.All(roll => roll == null))
        {
            return null;
        }
        return made.Any(roll => roll?.Id == when);
    }

    // A step waiting on one of the save's own outcomes asks for the effect's save, as "saveFailed" does.
    private bool AsksForSave(string when, Outcome outcome)
    {
        if (when is "saveFailed" or "saveSucceeded")
        {
            return true;
        }
        return _effect.Save.Ability.Length > 0 && outcome.Attack == null && outcome.Check == null
            && when is not ("hit" or "miss" or "crit") && _rules.Checks.Kind(CheckRules.Save).Outcome(when) != null;
    }

    // Whether the step happens to `who`. A save is the business of whoever the step lands on. An
    // attack or a check is about whoever it was rolled against, so a step for someone else (the
    // doer healing on a hit) follows the roll made about the creatures in `subjects`.
    private bool Holds(EffectStep step, int who, List<int> subjects)
    {
        if (step.IfFlag.Length > 0 && (_host.Sheet(who) == null || !_host.HasFlag(who, step.IfFlag, _context)))
        {
            return false;
        }
        Outcome outcome = OutcomeOf(who);
        string when = step.When;
        if (AsksForSave(when, outcome) || step.OnSave == OnSave.None)
        {
            SaveIfAsked(who, outcome);
        }
        if (step.OnSave == OnSave.None && outcome.Saved == true)
        {
            return false;
        }
        if (when.Length == 0 || IsEvent(when))
        {
            return true;
        }
        if (when == "saveFailed")
        {
            return outcome.Saved == false;
        }
        if (when == "saveSucceeded")
        {
            return outcome.Saved == true;
        }
        return Rolled(when, who) ?? subjects.Exists(subject => Rolled(when, subject) == true);
    }

    private int ScaleSteps(EffectScale scale)
    {
        if (scale.By == ScaleBy.None)
        {
            return 0;
        }
        int level = scale.By == ScaleBy.Slot ? _context.Slot
            : _context.Level > 0 ? _context.Level
            : _host.Sheet(_context.Self)?.Level ?? 1;
        return level > scale.From ? (level - scale.From) / Math.Max(1, scale.Every) : 0;
    }

    // The step's amount, rolled: its dice (twice over for a critical hit) plus whatever scaling adds.
    private RollResult RollAmount(EffectStep step, bool doubled)
    {
        CharacterSheet? self = _host.Sheet(_context.Self);
        string text = step.Amount switch
        {
            "weapon" => self?.DamageDice(_rules) ?? "0",
            "speed" => (self?.SpeedSquares(_rules) ?? 0).ToString(),
            _ => step.Amount,
        };
        DiceExpression dice = DiceExpression.Parse(text) ?? new DiceExpression();
        if (doubled)
        {
            for (int i = 0; i < dice.Terms.Count; i++)
            {
                if (dice.Terms[i].Sides > 0)
                {
                    dice.Terms[i] = dice.Terms[i] with { Count = dice.Terms[i].Count * 2 };
                }
            }
        }
        int times = ScaleSteps(step.Scale);
        if (times > 0)
        {
            DiceExpression? more = step.Scale.Dice.Length > 0 ? DiceExpression.Parse(step.Scale.Dice) : null;
            if (more != null)
            {
                foreach (DiceTerm term in more.Terms)
                {
                    dice.Terms.Add(term with { Count = term.Count * times });
                }
            }
            if (step.Scale.Value != 0)
            {
                dice.Terms.Add(new DiceTerm(Math.Abs(step.Scale.Value) * times, 0, 0, 0, step.Scale.Value < 0 ? -1 : 1));
            }
        }
        return Dice.Roll(dice, _random);
    }

    private void Ended(int who, List<string> ids)
    {
        foreach (string id in ids)
        {
            _result.Events.Add(new EffectEvent { Kind = EffectEventKind.ConditionEnded, Who = who, By = _context.Self, Id = id });
        }
    }

    private void Note(EffectEventKind kind, int who, string id, int amount = 0, RollResult? roll = null, string tracked = "")
    {
        _result.Events.Add(new EffectEvent
        {
            Kind = kind, Who = who, By = _context.Self, Roll = roll ?? new RollResult(), Amount = amount, Id = id, Tracked = tracked,
        });
    }

    private void One(EffectStep step, List<int> subjects)
    {
        var who = new List<int>();
        foreach (int actor in AimedAt(step, subjects))
        {
            if (Holds(step, actor, subjects))
            {
                who.Add(actor);
            }
        }
        // A step waiting on a result needs someone the result is true of.
        bool gated = step.When.Length > 0 && !IsEvent(step.When);
        bool nobody = gated && who.Count == 0;

        switch (step.Kind)
        {
        case EffectKind.Damage:
            DamageStep(step, who);
            break;
        case EffectKind.Heal:
        case EffectKind.TempHp:
            HealStep(step, who);
            break;
        case EffectKind.Condition:
            foreach (int actor in who)
            {
                CharacterSheet? sheet = _host.Sheet(actor);
                if (sheet == null)
                {
                    continue;
                }
                if (step.Remove)
                {
                    if (sheet.HasCondition(step.Id))
                    {
                        sheet.RemoveCondition(step.Id);
                        Note(EffectEventKind.ConditionRemoved, actor, step.Id);
                    }
                    continue;
                }
                int value = Math.Max(1, step.Value + step.Scale.Value * ScaleSteps(step.Scale));
                sheet.AddCondition(_rules, step.Id, step.Duration, value);
                Note(EffectEventKind.ConditionAdded, actor, step.Id, sheet.ConditionValue(step.Id));
            }
            break;
        case EffectKind.Modifier:
            foreach (int actor in who)
            {
                CharacterSheet? sheet = _host.Sheet(actor);
                if (sheet == null || step.Modifier == null)
                {
                    continue;
                }
                // Tracked apart from the ruleset's conditions. Doing the same thing again replaces
                // what it left last time instead of piling up.
                string id = "effect:" + (step.Id.Length > 0 ? step.Id : _context.Source.Length > 0 ? _context.Source : "modifier");
                if (_modified.Add((actor, id)))
                {
                    sheet.RemoveCondition(id);
                }
                sheet.AddModifier(id, step.Modifier, step.Duration);
                Note(EffectEventKind.Modifier, actor, step.Modifier.Stat, (int)(float)step.Modifier.Value, tracked: id);
            }
            break;
        case EffectKind.Move:
        case EffectKind.Resource:
        {
            RollResult? shared = null;
            foreach (int actor in who)
            {
                shared ??= RollAmount(step, false);
                int amount = Math.Max(0, shared.Total);
                if (step.Kind == EffectKind.Move)
                {
                    if (_host.Move(actor, step.How, amount, _context))
                    {
                        Note(EffectEventKind.Move, actor, step.How, amount);
                    }
                    continue;
                }
                int change = step.How == "spend" ? -amount : amount;
                if (_host.Resource(actor, step.Id, change, _context))
                {
                    Note(EffectEventKind.Resource, actor, step.Id, change);
                }
            }
            break;
        }
        case EffectKind.Light:
            foreach (int actor in who)
            {
                if (_host.Light(actor, (float)step.Size, step.Duration, _context))
                {
                    Note(EffectEventKind.Light, actor, "", (int)(float)step.Size);
                }
            }
            break;
        case EffectKind.Summon:
        {
            if (nobody)
            {
                break;
            }
            int count = Math.Max(0, RollAmount(step, false).Total);
            if (count > 0 && _host.Summon(step.Id, count, step.Duration, _context))
            {
                Note(EffectEventKind.Summon, _context.Self, step.Id, count);
            }
            break;
        }
        case EffectKind.Surface:
            if (!nobody && _host.Surface(step.Id, (float)step.Size, step.Duration, _context))
            {
                Note(EffectEventKind.Surface, _context.Self, step.Id, (int)(float)step.Size);
            }
            break;
        case EffectKind.Flag:
            if (!nobody && _host.Flag(step.Id, !step.Remove, _context))
            {
                Note(EffectEventKind.Flag, _context.Self, step.Id, step.Remove ? 0 : 1);
            }
            break;
        case EffectKind.Roll:
            foreach (int actor in who)
            {
                if (RollFor(step, actor))
                {
                    Steps(step.Steps, new List<int> { actor }, false);
                }
            }
            break;
        case EffectKind.Repeat:
        {
            if (nobody)
            {
                break;
            }
            int times = Math.Clamp(RollAmount(step, false).Total, 0, 100);
            for (int i = 0; i < times; i++)
            {
                Steps(step.Steps, gated ? who : subjects, false);
            }
            break;
        }
        case EffectKind.Choose:
        {
            if (nobody || step.Options.Count == 0)
            {
                break;
            }
            // A host answering out of range gets the last option.
            int choice = Math.Clamp(_host.Choose(step.Options.Select(option => option.Name).ToList(), _context), 0, step.Options.Count - 1);
            Note(EffectEventKind.Choice, _context.Self, step.Options[choice].Name, choice);
            Steps(step.Options[choice].Steps, gated ? who : subjects, false);
            break;
        }
        }
    }

    private void DamageStep(EffectStep step, List<int> who)
    {
        RollResult? shared = null; // one roll for everyone it lands on, as an area spell is rolled
        foreach (int actor in who)
        {
            CharacterSheet? sheet = _host.Sheet(actor);
            if (sheet == null)
            {
                continue;
            }
            Outcome outcome = OutcomeOf(actor);
            bool doubled = step.CritDoubles && outcome.Critical;
            // A critical hit rolls its dice twice, or the system's formula says what it comes to.
            Formula? critical = _rules.Checks.CriticalDamage;
            RollResult rolled = doubled ? RollAmount(step, critical == null) : shared ??= RollAmount(step, false);
            int total = rolled.Total;
            if (doubled && critical != null)
            {
                total = critical.Whole(name => name switch
                {
                    "dice" => rolled.Total - rolled.Flat,
                    "flat" => rolled.Flat,
                    "max" => rolled.Dice.Where(die => die.Kept).Sum(die => die.Sides),
                    _ => null,
                });
            }
            int amount = Math.Max(step.Minimum, total);
            if (step.OnSave == OnSave.Half)
            {
                // the share the save's outcome lets through: half when it holds, in the game's own rules
                SaveIfAsked(actor, outcome);
                if (outcome.Save != null && outcome.Save.Damage != 1)
                {
                    amount = (int)Math.Floor(amount * outcome.Save.Damage);
                }
            }
            bool wasUp = !sheet.Down;
            int dealt = _host.Damage(actor, Math.Max(0, amount), step.Type, _context with { CriticalDamage = outcome.Critical });
            _result.Events.Add(new EffectEvent
            {
                Kind = EffectEventKind.Damage, Who = actor, By = _context.Self, Roll = rolled, Amount = dealt,
                Critical = doubled, Dropped = wasUp && sheet.Down, Id = step.Type,
            });
            Ended(actor, sheet.ConditionEvent(_rules, "damage"));
        }
    }

    private void HealStep(EffectStep step, List<int> who)
    {
        RollResult? shared = null;
        foreach (int actor in who)
        {
            CharacterSheet? sheet = _host.Sheet(actor);
            if (sheet == null)
            {
                continue;
            }
            shared ??= RollAmount(step, false);
            int amount = Math.Max(0, shared.Total);
            if (step.Kind == EffectKind.TempHp)
            {
                sheet.TempHp = Math.Max(sheet.TempHp, amount); // temporary HP never adds up
                Note(EffectEventKind.TempHp, actor, "", amount, shared);
                continue;
            }
            bool wasDown = sheet.Down;
            int before = sheet.Hp;
            sheet.Heal(amount);
            Note(EffectEventKind.Heal, actor, "", sheet.Hp - before, shared);
            if (wasDown && !sheet.Down)
            {
                Ended(actor, sheet.ConditionEvent(_rules, "healed"));
            }
        }
    }

    // Makes the step's roll about `actor` and records how it went. False if it could not be made.
    private bool RollFor(EffectStep step, int actor)
    {
        CharacterSheet? self = _host.Sheet(_context.Self);
        CharacterSheet? subject = _host.Sheet(actor);
        if (subject == null)
        {
            return false;
        }
        Outcome outcome = OutcomeOf(actor);
        if (step.How == "save")
        {
            int dc = step.CasterDc ? _context.Dc : step.Dc;
            RollResult roll = subject.RollSave(_rules, step.Ability, Advantage.None, _random);
            outcome.Save = _rules.Checks.Kind(CheckRules.Save).Resolve(roll, dc);
            _result.Events.Add(new EffectEvent
            {
                Kind = EffectEventKind.Save, Who = actor, By = _context.Self, Roll = roll, Dc = dc,
                Success = outcome.Save.Passes, Outcome = outcome.Save.Id, Id = step.Ability,
            });
            return true;
        }
        if (self == null)
        {
            return false;
        }
        if (step.How == "check")
        {
            int dc = step.Against.Length > 0 ? subject.PassiveScore(_rules, step.Against) : step.CasterDc ? _context.Dc : step.Dc;
            RollResult roll = self.RollCheck(_rules, step.Ability, Advantage.None, _random);
            outcome.Check = _rules.Checks.Kind(CheckRules.Check).Resolve(roll, dc);
            _result.Events.Add(new EffectEvent
            {
                Kind = EffectEventKind.Check, Who = actor, By = _context.Self, Roll = roll, Dc = dc,
                Success = outcome.Check.Passes, Outcome = outcome.Check.Id, Id = step.Ability,
            });
            return true;
        }
        // An attack with whatever the doer holds, against armour class, read the way the system
        // reads attacks (the game's own: a natural 1 misses, a natural 20 is a critical hit).
        CheckKind kind = _rules.Checks.Kind(CheckRules.Attack);
        // each attack after the first in a turn takes the ruleset's penalty, this effect's own included
        int attacksSoFar = _context.AttacksMade + _result.Events.Count(e => e.Kind == EffectEventKind.Attack);
        int penalty = _rules.AttackPenalty?.Whole(name => name == "attacks" ? attacksSoFar : null) ?? 0;
        RollResult attack = kind.Roll(self.AttackModifier(_rules) + penalty, self.AttackAdvantage(_rules), _random);
        List<string> afterAttack = self.ConditionEvent(_rules, "attack"); // they still counted for this roll
        int ac = _host.ArmorClass(actor, _context);
        outcome.Attack = kind.Resolve(attack, ac);
        _result.Events.Add(new EffectEvent
        {
            Kind = EffectEventKind.Attack, Who = actor, By = _context.Self, Roll = attack, Dc = ac,
            Success = outcome.Attack.Passes, Critical = outcome.Critical, Outcome = outcome.Attack.Id,
        });
        Ended(_context.Self, afterAttack);
        return true;
    }
}
