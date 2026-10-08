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

        /// <summary>How far the last roll about this creature beat its DC (below 0: missed by).</summary>
        public int? Margin;
        /// <summary>The attack about it was rolled with advantage.</summary>
        public bool Advantage;

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
        outcome.Margin = roll.Total - dc;
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
    // "margin" in a step's dice: how far the roll about whoever it lands on beat its DC (Fate's shifts).
    private RollResult RollAmount(EffectStep step, bool doubled, int who = -1)
    {
        int? margin = who >= 0 && _outcomes.TryGetValue(who, out Outcome? rolledFor) ? rolledFor.Margin : null;
        CharacterSheet? self = _host.Sheet(_context.Self);
        string text = step.Amount switch
        {
            "weapon" => self == null ? "0" : WithBonus(self.DamageDice(_rules), self.Situational(_rules, "damage", AttackContext(step, who >= 0 ? _host.Sheet(who) : null))),
            "speed" => (self?.SpeedSquares(_rules) ?? 0).ToString(),
            // formulas in braces read the doer's sheet: "2d8+{mod.wis}"
            _ => DiceText.Fill(step.Amount, name => name == "margin" ? margin ?? 0 : self?.Named(_rules, name)),
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
                    if (step.How == "attack")
                    {
                        Triggered(actor);
                    }
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

    // Granted triggers that answer how an attack on target went: the doer's on its hit, miss or
    // crit (Sneak Attack), and the target's on being hit, landing on the attacker.
    private void Triggered(int target)
    {
        if (_outcomes.GetValueOrDefault(target) is not { Attack: CheckOutcome attack } rolled)
        {
            return;
        }
        // as the attack was rolled: whatever gave advantage may have ended with it
        bool advantage = rolled.Advantage;
        double? Attack(string name) => name switch
        {
            "advantage" => advantage ? 1 : 0,
            "critical" => attack.Critical ? 1 : 0,
            _ => null,
        };
        _result.Events.AddRange(Fire(_host, _context, _context.Self, trigger => trigger.When switch
        {
            "hit" => attack.Passes,
            "crit" => attack.Critical,
            "miss" => !attack.Passes,
            _ => false,
        }, target, Attack));
        if (attack.Passes)
        {
            _result.Events.AddRange(Fire(_host, _context, target, trigger => trigger.When == "hitBy", _context.Self, Attack));
        }
    }

    // Triggers set off by other triggers stop this deep, so two that answer each other end.
    private const int MostTriggerDepth = 4;
    [ThreadStatic] private static int _triggerDepth;

    /// <summary>
    /// Runs owner's granted triggers that fit, each landing on target. Their "if" reads owner's
    /// sheet names, flag.&lt;flag&gt;, targetFlag.&lt;flag&gt; and whatever extra knows. Returns
    /// what they did, a Triggered event before each.
    /// </summary>
    public static List<EffectEvent> Fire(EffectHost host, EffectContext context, int owner, Func<TriggerDefinition, bool> fits, int target,
        Func<string, double?>? extra = null)
    {
        var events = new List<EffectEvent>();
        CharacterSheet? self = host.Sheet(owner);
        CharacterSheet? subject = host.Sheet(target);
        // one that is down does nothing by itself, Sneak Attack or thorns
        if (self == null || subject == null || self.Down || _triggerDepth >= MostTriggerDepth)
        {
            return events;
        }
        Ruleset rules = context.Rules;
        // its granted triggers, and those everyone has (a weapon trait's rider)
        foreach (string id in self.Granted.Concat(rules.Triggers.Where(t => t.General).Select(t => t.Id)).Distinct().ToList())
        {
            if (rules.Trigger(id) is not TriggerDefinition trigger || !fits(trigger) || (trigger.OncePerTurn && self.TriggersUsed.Contains(id)))
            {
                continue;
            }
            double? Name(string name) => extra?.Invoke(name) ?? name switch
            {
                _ when name.StartsWith("targetFlag.", StringComparison.Ordinal) => subject.HasFlag(rules, name[11..]) ? 1 : 0,
                _ when name.StartsWith("flag.", StringComparison.Ordinal) => self.HasFlag(rules, name[5..]) ? 1 : 0,
                _ => self.Named(rules, name),
            };
            if (trigger.If != null && trigger.If.Evaluate(Name) == 0)
            {
                continue;
            }
            self.TriggersUsed.Add(id);
            events.Add(new EffectEvent { Kind = EffectEventKind.Triggered, Who = target, By = owner, Id = trigger.Name });
            var run = context with
            {
                Self = owner,
                Targets = new List<int> { target },
                Source = trigger.Id,
                Event = "",
                Dc = owner == context.Self ? context.Dc : self.DifficultyClass(rules),
                AttacksMade = owner == context.Self ? context.AttacksMade : 0,
            };
            _triggerDepth++;
            try
            {
                events.AddRange(new EffectRun(trigger.Effect, host, run).Run().Events);
            }
            finally
            {
                _triggerDepth--;
            }
        }
        return events;
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
            // dice that read the margin are rolled for each creature, since each roll's margin differs
            bool own = step.Amount.Contains("margin", StringComparison.Ordinal);
            RollResult rolled = doubled ? RollAmount(step, critical == null, actor)
                : own ? RollAmount(step, false, actor)
                : shared ??= RollAmount(step, false, actor);
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
            // a weapon's damage is of the weapon's type unless the step names one
            string type = step.Type.Length == 0 && step.Amount == "weapon" ? _host.Sheet(_context.Self)?.Weapon?.DamageType ?? "" : step.Type;
            int dealt = _host.Damage(actor, Math.Max(0, amount), type, _context with { CriticalDamage = outcome.Critical, Track = step.Id });
            _result.Events.Add(new EffectEvent
            {
                Kind = EffectEventKind.Damage, Who = actor, By = _context.Self, Roll = rolled, Amount = dealt,
                Critical = doubled, Dropped = wasUp && sheet.Down, Id = step.Type,
            });
            Ended(actor, sheet.ConditionEvent(_rules, "damage"));
            if (wasUp && sheet.Down && actor != _context.Self)
            {
                // the doer dropped someone: its "kill" triggers, on itself
                _result.Events.AddRange(Fire(_host, _context, _context.Self, trigger => trigger.When == "kill", _context.Self));
            }
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

    // What an attack's situational modifiers can ask: the weapon's traits, whether it is ranged or
    // a spell, the ability it is made with, and the target's flags.
    private Func<string, double?> AttackContext(EffectStep step, CharacterSheet? target)
    {
        CharacterSheet? self = _host.Sheet(_context.Self);
        string ability = step.Ability == "caster" ? "caster" : step.Ability.Length > 0 ? step.Ability : self?.AttackAbility(_rules) ?? "";
        bool spell = step.Ability == "caster";
        bool ranged = spell || (self?.WeaponItem?.Definition.Range ?? 1) > 1;
        return name => name switch
        {
            "ranged" => ranged ? 1 : 0,
            "melee" => ranged ? 0 : 1,
            "spell" => spell ? 1 : 0,
            _ when name == "ability." + ability => 1,
            _ when name.StartsWith("ability.", StringComparison.Ordinal) => 0,
            _ when name.StartsWith("trait.", StringComparison.Ordinal) => !spell && self?.Weapon?.Has(name[6..]) == true ? 1 : 0,
            _ when name.StartsWith("targetFlag.", StringComparison.Ordinal) => target?.HasFlag(_rules, name[11..]) == true ? 1 : 0,
            _ => null,
        };
    }

    // "1d8+3" with a situational bonus on the end: "1d8+5"; nothing added when it is 0.
    private static string WithBonus(string dice, int bonus) => bonus == 0 ? dice : dice + (bonus > 0 ? "+" : "") + bonus;

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
            outcome.Margin = roll.Total - dc;
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
            CheckKind checks = _rules.Checks.Kind(CheckRules.Check);
            if (step.Against.Length > 0 && checks.Opposed)
            {
                // a contest: the other side rolls their own modifier rather than standing on a passive score
                dc = checks.Defence(dc - _rules.PassiveBase, _random, out RollResult? resisted);
                Note(EffectEventKind.Defence, actor, step.Against, dc, resisted);
            }
            RollResult roll = self.RollCheck(_rules, step.Ability, Advantage.None, _random);
            outcome.Check = _rules.Checks.Kind(CheckRules.Check).Resolve(roll, dc);
            outcome.Margin = roll.Total - dc;
            _result.Events.Add(new EffectEvent
            {
                Kind = EffectEventKind.Check, Who = actor, By = _context.Self, Roll = roll, Dc = dc,
                Success = outcome.Check.Passes, Outcome = outcome.Check.Id, Id = step.Ability,
            });
            return true;
        }
        // An attack with whatever the doer holds, against armour class, read the way the system
        // reads attacks (the game's own: a natural 1 misses, a natural 20 is a critical hit).
        if (step.Reach > 0 && !_host.InReach(_context.Self, actor, step.Reach))
        {
            // a charge that didn't get there: no attack
            return false;
        }
        CheckKind kind = _rules.Checks.Kind(CheckRules.Attack);
        // each attack after the first in a turn takes the ruleset's penalty, this effect's own included
        int attacksSoFar = _context.AttacksMade + _result.Events.Count(e => e.Kind == EffectEventKind.Attack);
        int penalty = _rules.AttackPenalty?.Whole(name => name == "attacks" ? attacksSoFar
            : name.StartsWith("trait.", StringComparison.Ordinal) ? (self.Weapon?.Has(name[6..]) == true ? 1 : 0) : null) ?? 0;
        int bonus = step.Ability == "caster" ? self.SpellAttackModifier(_rules)
            : step.Ability.Length > 0 ? self.AttackModifier(_rules, step.Ability)
            : self.AttackModifier(_rules);
        bonus += self.Situational(_rules, "attack", AttackContext(step, subject));
        Advantage advantage = self.AttackAdvantage(_rules, subject, _host.PlaceConditions(_context.Self, actor), _host.Distance(_context.Self, actor));
        outcome.Advantage = advantage == Advantage.Advantage;
        RollResult attack = kind.Roll(bonus + penalty, advantage, _random);
        List<string> afterAttack = self.ConditionEvent(_rules, "attack"); // they still counted for this roll
        int armor = _host.ArmorClass(actor, _context, step.Against);
        int ac = kind.Defence(armor, _random, out RollResult? defended);
        if (defended != null)
        {
            // the defender's own roll, shown before the attack it meets
            Note(EffectEventKind.Defence, actor, "defence", ac, defended);
        }
        outcome.Attack = kind.Resolve(attack, ac);
        if (outcome.Attack.Passes && _host.BeforeHit(actor, _context.Self, _context, attack.Total - ac))
        {
            // its reaction may have raised its defence: the same roll, read against the new one
            ac += _host.ArmorClass(actor, _context, step.Against) - armor;
            outcome.Attack = kind.Resolve(attack, ac);
        }
        int flat = subject.Conditions.Select(c => _rules.Condition(c.Id)).OfType<ConditionDefinition>().Select(d => d.AttackersFlatCheck).DefaultIfEmpty(0).Max();
        if (outcome.Attack.Passes && flat > 0)
        {
            // a flat check first (the hidden): a plain d20 that has to reach it, or the attack misses
            RollResult check = Dice.Roll("1d20", _random);
            _result.Events.Add(new EffectEvent
            {
                Kind = EffectEventKind.Check, Who = actor, By = _context.Self, Roll = check, Dc = flat, Success = check.Total >= flat, Id = "flat check",
            });
            if (check.Total < flat)
            {
                outcome.Attack = kind.Outcomes.Last(o => !o.Passes);
            }
        }
        if (outcome.Attack.Passes && !outcome.Attack.Critical && kind.Outcomes.FirstOrDefault(o => o.Passes && o.Critical) is CheckOutcome critical)
        {
            // a hit from close by on one that can't defend itself (paralysed) is a critical hit
            double distance = _host.Distance(_context.Self, actor);
            bool helpless = subject.Conditions.Select(c => _rules.Condition(c.Id)).OfType<ConditionDefinition>()
                .Any(d => d.HitsAreCritical && distance <= Math.Max(1, d.AttackersWithin) + 0.01);
            if (helpless)
            {
                outcome.Attack = critical;
            }
        }
        outcome.Margin = attack.Total - ac;
        _result.Events.Add(new EffectEvent
        {
            Kind = EffectEventKind.Attack, Who = actor, By = _context.Self, Roll = attack, Dc = ac,
            Success = outcome.Attack.Passes, Critical = outcome.Critical, Outcome = outcome.Attack.Id,
        });
        Ended(_context.Self, afterAttack);
        return true;
    }
}
