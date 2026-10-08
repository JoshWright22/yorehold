namespace Yorehold.Rules;

// Moving in a fight, and the reactions a move sets off (opportunity strikes, readied actions).
public sealed partial class World
{
    // A move in progress, edge by edge: each edge is checked for leaving reach, then entering it.
    private sealed class PendingMovement
    {
        public int Creature { get; init; }
        public List<Cell> Path { get; init; } = new();
        public int Edge { get; set; } = 1;
        public int Phase { get; set; }
        public int NextCreature { get; set; }
        public int AnimateFrom { get; set; }
        public bool Prompts { get; init; }
    }

    private sealed record PendingReaction(int Creature, int Target, string Action, string Name, bool Readied);

    private PendingMovement? _pendingMovement;
    private PendingReaction? _pendingReaction;
    private ulong _reactionSequence;

    /// <summary>A hero's reaction waiting for React; null when there is none.</summary>
    public ReactionPrompt? ReactionPrompt { get; private set; }

    /// <summary>
    /// The creature whose turn it is walks to a square it can reach with the movement it has left.
    /// Leaving or entering a foe's reach on the way can set off its reaction.
    /// </summary>
    public bool MoveTo(Cell to)
    {
        Refusal = "";
        if (!Fighting || InCutscene || _pendingMovement != null || CurrentCreature is not int me)
        {
            Refusal = "Not now.";
            return false;
        }
        Token token = Tokens.Tokens[me];
        ComputeReach(me);
        if (to == _standing || !_reach.ContainsKey(to))
        {
            Refusal = "Can't move there.";
            return false;
        }
        // Finish any walk still playing, then take the new one and pay for it.
        if (token.Path.Count > 0)
        {
            token.Position = token.Path[^1];
        }
        token.Path.Clear();
        ComputeReach(me);
        float cost = _reach[to];
        int squares = (int)MathF.Ceiling(cost - 0.01f);
        Cell standing = _standing;
        List<Cell> path = Paths.Find(Grid, standing, to, c => c == standing || _reach.ContainsKey(c));
        Encounter!.SpendMovement(Math.Min(squares, Encounter.Current.Budget.MovementLeft));
        Creatures[me].Sheet.ConditionEvent(Rules, "move");
        _pendingMovement = new PendingMovement { Creature = me, Path = path, Prompts = Options.ReactionPrompts };
        ContinueMovement();
        return true;
    }

    /// <summary>Answers the reaction prompt: take the reaction or pass on it. The move then carries on.</summary>
    public bool React(bool take)
    {
        Refusal = "";
        if (_pendingReaction == null || ReactionPrompt == null)
        {
            Refusal = "No reaction to answer.";
            return false;
        }
        ResolveReaction(take);
        ContinueMovement();
        return true;
    }

    private void ResolveReaction(bool take)
    {
        if (_pendingReaction == null)
        {
            return;
        }
        PendingReaction offer = _pendingReaction;
        _pendingReaction = null;
        ReactionPrompt = null;
        if (!take)
        {
            Say($"{Creatures[offer.Creature].Sheet.Name} passes on {offer.Name}.");
            return;
        }
        ActionDefinition? action = FindAction(offer.Action);
        if (OrderIndex(offer.Creature) is not int index || action == null || !Encounter!.UseReaction(index))
        {
            return;
        }
        if (offer.Readied)
        {
            Creatures[offer.Creature].ReadiedAction = "";
        }
        Say($"{Creatures[offer.Creature].Sheet.Name} takes {offer.Name}.");
        int? aimed = action.Target == ActionTarget.Self ? offer.Creature : offer.Target;
        if (SpellOf(action) is SpellDefinition spell)
        {
            // a spell cast as a reaction spends its slot like any casting
            CastSpell(offer.Creature, spell, aimed, null, Spellcasting.SlotFor(Creatures[offer.Creature].Sheet, spell, SpellRules, spell.Level) ?? 0);
            return;
        }
        RunActionEffect(offer.Creature, action, aimed);
    }

    private void ContinueMovement()
    {
        if (_pendingMovement == null || _pendingReaction != null)
        {
            return;
        }
        PendingMovement move = _pendingMovement;
        int mover = move.Creature;
        Token token = Tokens.Tokens[mover];
        bool Stopped() => Creatures[mover].Sheet.Down || Encounter!.Finished;
        while (move.Edge < move.Path.Count && !Stopped())
        {
            Cell from = move.Path[move.Edge - 1];
            Cell to = move.Path[move.Edge];
            ReactionTrigger phase = move.Phase == 0 ? ReactionTrigger.LeavesReach : ReactionTrigger.EntersReach;
            while (move.NextCreature < Creatures.Count)
            {
                int reactor = move.NextCreature++;
                if (reactor == mover || Creatures[reactor].Team == Creatures[mover].Team || OrderIndex(reactor) is not int index
                    || !Encounter!.Order[index].Standing || !Encounter.Order[index].Budget.Reaction
                    || Creatures[reactor].Sheet.HasFlag(Rules, "cantAct"))
                {
                    continue;
                }
                foreach (ReactionDefinition definition in Chapter.Rules.Reactions)
                {
                    if (definition.Trigger != phase || Unless(definition, mover) || (!definition.General && !Creatures[reactor].Sheet.Granted.Contains(definition.Id)))
                    {
                        continue;
                    }
                    string id = definition.Readied ? Creatures[reactor].ReadiedAction : definition.Action;
                    ActionDefinition? action = FindAction(id);
                    Cell at = CellOf(reactor);
                    if (action == null || !action.Meets(Creatures[reactor].Sheet, Rules, out _)
                        || !definition.Matches(Grid.Distance(at, from), Grid.Distance(at, to), action.Range))
                    {
                        continue;
                    }
                    int triggerAt = move.Phase == 0 ? move.Edge - 1 : move.Edge;
                    token.Position = Grid.Center(move.Path[triggerAt]);
                    if (action.Target == ActionTarget.Creature && !ValidTarget(reactor, action, mover))
                    {
                        continue;
                    }
                    _pendingReaction = new PendingReaction(reactor, mover, action.Id, definition.Name, definition.Readied);
                    if (move.Prompts && reactor < HeroCount && !Options.AutoPlay)
                    {
                        move.AnimateFrom = triggerAt;
                        ReactionPrompt = new ReactionPrompt
                        {
                            Creature = reactor, Mover = mover, Name = definition.Name, SecondsLeft = definition.PromptSeconds, Id = ++_reactionSequence,
                        };
                        return;
                    }
                    ResolveReaction(true);
                    break;
                }
                if (Stopped())
                {
                    break;
                }
            }
            if (Stopped())
            {
                break;
            }
            move.NextCreature = 0;
            if (move.Phase == 0)
            {
                move.Phase = 1;
            }
            else
            {
                move.Phase = 0;
                move.Edge++;
            }
        }
        if (!Creatures[mover].Sheet.Down)
        {
            token.Position = Grid.Center(move.Path.Count == 0 ? CellOf(mover) : move.Path[move.AnimateFrom]);
            for (int i = move.AnimateFrom + 1; i < move.Path.Count; i++)
            {
                token.Path.Add(Grid.Center(move.Path[i]));
            }
        }
        _pendingMovement = null;
        if (Encounter!.Finished)
        {
            EndFight();
        }
        else if (Creatures[mover].Sheet.Down)
        {
            EndCurrentTurn();
        }
        else
        {
            ComputeReach(mover);
        }
    }

    // Reactions an attack sets off once it is done: the one attacked, on a hit or a miss, or its
    // allies when it was hit. Taken at once, with no prompt: the attacker's turn can't wait for an
    // answer the way a move does yet.
    private void AttackReactions(EffectResult result)
    {
        foreach (EffectEvent e in result.Events.Where(e => e.Kind == EffectEventKind.Attack).ToList())
        {
            int attacker = e.By;
            int target = e.Who;
            if (!Fighting || attacker < 0 || target < 0 || Creatures[attacker].Sheet.Down)
            {
                continue;
            }
            for (int reactor = 0; reactor < Creatures.Count && Fighting; reactor++)
            {
                if (reactor == attacker || Creatures[reactor].Team == Creatures[attacker].Team || OrderIndex(reactor) is not int index
                    || !Encounter!.Order[index].Standing || !Encounter.Order[index].Budget.Reaction
                    || Creatures[reactor].Sheet.HasFlag(Rules, "cantAct"))
                {
                    continue;
                }
                foreach (ReactionDefinition definition in Chapter.Rules.Reactions)
                {
                    bool fits = definition.Trigger switch
                    {
                        ReactionTrigger.Hit => reactor == target && e.Success,
                        ReactionTrigger.Missed => reactor == target && !e.Success,
                        ReactionTrigger.AllyHit => reactor != target && Creatures[target].Team == Creatures[reactor].Team && e.Success,
                        _ => false,
                    };
                    if (!fits || Unless(definition, attacker) || ReactionAction(definition, reactor, attacker) is not ActionDefinition action)
                    {
                        continue;
                    }
                    _pendingReaction = new PendingReaction(reactor, attacker, action.Id, definition.Name, false);
                    if (Options.ReactionPrompts && reactor < HeroCount && !Options.AutoPlay)
                    {
                        // a hero's player is asked, as for a move; the fight waits on the answer
                        ReactionPrompt = new ReactionPrompt
                        {
                            Creature = reactor, Mover = attacker, Name = definition.Name, SecondsLeft = definition.PromptSeconds, Id = ++_reactionSequence,
                        };
                        return;
                    }
                    ResolveReaction(true);
                    break;
                }
            }
        }
    }

    // What a reaction would run for reactor at source, or null when it can't: an action it is
    // granted (or anyone has) and meets, or a spell it knows, can cast and has a slot for.
    private ActionDefinition? ReactionAction(ReactionDefinition definition, int reactor, int source)
    {
        CharacterSheet sheet = Creatures[reactor].Sheet;
        ActionDefinition? action;
        if (definition.Spell.Length > 0)
        {
            if (FindSpell(definition.Spell) is not SpellDefinition spell || !sheet.Spells.Contains(spell.Id)
                || !Spellcasting.CanCast(sheet, spell, SpellRules, out _) || (spell.Level > 0 && Spellcasting.SlotFor(sheet, spell, SpellRules, spell.Level) == null))
            {
                return null;
            }
            action = spell.Action;
        }
        else
        {
            action = (definition.General || sheet.Granted.Contains(definition.Id)) ? FindAction(definition.Action) : null;
            if (action == null || !action.Meets(sheet, Rules, out _))
            {
                return null;
            }
        }
        return action.Target == ActionTarget.Creature && !ValidTarget(reactor, action, source) ? null : action;
    }

    // Whether a guard taken now would make a hit that beat the defence by margin miss: the
    // defence the action's conditions add on itself, counted on a copy of its sheet. An action
    // that adds none to the defence is taken anyway (it does something else).
    private bool TurnsTheHit(int target, ActionDefinition action, int margin)
    {
        CharacterSheet sheet = Creatures[target].Sheet;
        List<string> gained = action.Effect.Steps
            .Where(s => s.Kind == EffectKind.Condition && !s.Remove && Rules.Condition(s.Id) != null && !sheet.HasCondition(s.Id))
            .Select(s => s.Id).ToList();
        if (gained.Count == 0)
        {
            return true;
        }
        CharacterSheet guarded = sheet.Copy();
        foreach (string id in gained)
        {
            guarded.AddCondition(Rules, id);
        }
        int raised = guarded.AttackDefence(Rules) - sheet.AttackDefence(Rules);
        return raised <= 0 || raised > margin;
    }

    // The one who set the reaction off has the flag that stops it (disengaged).
    private bool Unless(ReactionDefinition definition, int source) =>
        definition.Unless.Length > 0 && source >= 0 && source < Creatures.Count && Creatures[source].Sheet.HasFlag(Rules, definition.Unless);

    /// <summary>A flag a reaction can give a caster (through a condition) so the spell it is casting is lost: a counterspell.</summary>
    public const string SpellLostFlag = "spellLost";

    // A foe is casting: everyone on another side with a "spellCast" reaction in reach of the
    // caster takes it now, before the spell does anything.
    private void SpellCastReactions(int caster)
    {
        if (!Fighting || _pendingReaction != null)
        {
            return;
        }
        for (int reactor = 0; reactor < Creatures.Count && Fighting && !Creatures[caster].Sheet.Down; reactor++)
        {
            if (reactor == caster || Creatures[reactor].Team == Creatures[caster].Team || OrderIndex(reactor) is not int index
                || !Encounter!.Order[index].Standing || !Encounter.Order[index].Budget.Reaction
                || Creatures[reactor].Sheet.HasFlag(Rules, "cantAct"))
            {
                continue;
            }
            foreach (ReactionDefinition definition in Chapter.Rules.Reactions.Where(r => r.Trigger == ReactionTrigger.SpellCast))
            {
                if (Unless(definition, caster) || ReactionAction(definition, reactor, caster) is not ActionDefinition action)
                {
                    continue;
                }
                Cell? aim = _aim;
                _pendingReaction = new PendingReaction(reactor, caster, action.Id, definition.Name, false);
                ResolveReaction(true);
                _aim = aim;
                break;
            }
        }
    }

    // An attack on target would land: a reaction it has for that (Shield) is taken at once, in the
    // middle of the attack, which is then read again against what the reaction changed.
    internal bool BeforeHitReaction(int target, int attacker, int margin = 0)
    {
        if (!Fighting || _pendingReaction != null || target < 0 || attacker < 0 || target >= Creatures.Count
            || OrderIndex(target) is not int index || !Encounter!.Order[index].Standing || !Encounter.Order[index].Budget.Reaction
            || Creatures[target].Sheet.HasFlag(Rules, "cantAct"))
        {
            return false;
        }
        foreach (ReactionDefinition definition in Chapter.Rules.Reactions.Where(r => r.Trigger == ReactionTrigger.BeforeHit))
        {
            if (Unless(definition, attacker) || ReactionAction(definition, target, attacker) is not ActionDefinition action)
            {
                continue;
            }
            if (!TurnsTheHit(target, action, margin))
            {
                // what it would raise wouldn't make this roll miss: keep the reaction (and the slot)
                continue;
            }
            // the attack's own aim stays as it was for the rest of its steps
            Cell? aim = _aim;
            _pendingReaction = new PendingReaction(target, attacker, action.Id, definition.Name, false);
            ResolveReaction(true);
            _aim = aim;
            return true;
        }
        return false;
    }

    // A prompt nobody answers in time takes the reaction.
    private void ReactionTime(double seconds)
    {
        if (ReactionPrompt == null)
        {
            return;
        }
        ReactionPrompt.SecondsLeft -= Math.Max(0, seconds);
        if (ReactionPrompt.SecondsLeft <= 0)
        {
            React(true);
        }
    }
}
