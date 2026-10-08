using System.Numerics;

namespace Yorehold.Rules;

// Actions: what the ruleset's actions/ files let a creature do on its turn, and carrying one out.
public sealed partial class World
{
    /// <summary>An action of the ruleset's, or a spell's (a spell may not take an action's id).</summary>
    public ActionDefinition? FindAction(string id) => Chapter.Rules.Action(id) ?? FindSpell(id)?.Action;

    /// <summary>
    /// The actions a creature has, by their order: the ruleset's general ones whose resources it
    /// carries, and the spells on its sheet (after the general ones unless a file says otherwise).
    /// </summary>
    public List<ActionDefinition> ActionsOf(int creature)
    {
        if (creature < 0 || creature >= Creatures.Count)
        {
            return new List<ActionDefinition>();
        }
        CharacterSheet sheet = Creatures[creature].Sheet;
        IEnumerable<ActionDefinition> spells = sheet.Spells.Select(FindSpell).OfType<SpellDefinition>().Select(s => s.Action);
        return Chapter.Rules.Actions
            .Where(a => a.General && a.NeedsResources.Keys.All(sheet.Resources.ContainsKey))
            .Concat(spells)
            .OrderBy(a => a.Order)
            .ToList();
    }

    /// <summary>What each of the creature's hotbar slots shows, an action id or "". End turn has a button of its own and no slot.</summary>
    public string[] HotbarOf(int creature)
    {
        List<ActionDefinition> all = ActionsOf(creature).Where(a => a.Id != EndTurnAction).ToList();
        var always = all.Where(a => a.General && a.NeedsResources.Count == 0).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        return Creatures[creature].Sheet.Hotbar.Layout(all.Select(a => a.Id).ToList(), always.Contains);
    }

    /// <summary>Puts one of the creature's actions in a hotbar slot (dragged from the spell book or another slot).</summary>
    public bool PutOnHotbar(int creature, int slot, string action)
    {
        if (creature < 0 || creature >= Creatures.Count || action == EndTurnAction || ActionsOf(creature).All(a => a.Id != action))
        {
            return false;
        }
        Creatures[creature].Sheet.Hotbar.Put(slot, action);
        return true;
    }

    /// <summary>Takes a slot's action off the hotbar (dragged off it).</summary>
    public void TakeOffHotbar(int creature, int slot)
    {
        if (creature >= 0 && creature < Creatures.Count)
        {
            Creatures[creature].Sheet.Hotbar.Clear(slot);
        }
    }

    public int ActionCost(int creature, ActionDefinition action) => action.CostFor(Creatures[creature].Sheet, Rules);

    /// <summary>It is the creature's turn, it has the action and can pay for it. why says what is missing when there is a reason to give.</summary>
    public bool CanUse(int creature, ActionDefinition action, out string why)
    {
        why = "";
        if (_pendingMovement != null || CurrentCreature != creature || Encounter == null)
        {
            return false;
        }
        if (!ActionsOf(creature).Contains(action))
        {
            return false;
        }
        if (!Encounter.CanAct(ActionCost(creature, action)))
        {
            why = "not enough actions left";
            return false;
        }
        if (SpellOf(action) is SpellDefinition spell && !Spellcasting.CanCast(Creatures[creature].Sheet, spell, SpellRules, out why))
        {
            return false;
        }
        return action.Meets(Creatures[creature].Sheet, Rules, out why);
    }

    public bool CanUse(int creature, string id)
    {
        ActionDefinition? found = FindAction(id);
        return found != null && CanUse(creature, found, out _);
    }

    /// <summary>The actions the creature whose turn it is could use now.</summary>
    public List<ActionDefinition> UsableActions()
    {
        return CurrentCreature is int me ? ActionsOf(me).Where(a => CanUse(me, a, out _)).ToList() : new List<ActionDefinition>();
    }

    /// <summary>Someone the action may be aimed at from where the creature stands: in the fight, on the right side, in range and not behind full cover.</summary>
    public bool ValidTarget(int creature, ActionDefinition action, int target)
    {
        if (action.Target != ActionTarget.Creature || creature < 0 || creature >= Creatures.Count || target < 0 || target >= Creatures.Count)
        {
            return false;
        }
        CharacterSheet sheet = Creatures[target].Sheet;
        if (OrderIndex(target) is not int index || Encounter!.Order[index].Out || sheet.Death.Dead || sheet.HasFlag(Rules, "dead")
            || (sheet.Down && !action.AllowsDowned))
        {
            return false;
        }
        bool sameSide = Creatures[target].Team == Creatures[creature].Team;
        if ((action.Side == ActionSide.Enemy && sameSide) || (action.Side == ActionSide.Ally && !sameSide))
        {
            return false;
        }
        if (!InRange(creature, action, target))
        {
            return false;
        }
        PositioningRules positioning = Chapter.Rules.Positioning;
        if (positioning.Enabled)
        {
            return (action.Range <= 1 && !positioning.CoverAgainstMelee) || CoverFrom(creature, target) != Cover.Full;
        }
        return action.Range <= 1 || Sight.LineOfSight(Grid.Center(CellOf(creature)), Grid.Center(CellOf(target)), Map.Walls);
    }

    /// <summary>Everyone the creature whose turn it is could aim the action at right now.</summary>
    public List<int> ValidTargets(string actionId)
    {
        ActionDefinition? action = FindAction(actionId);
        if (CurrentCreature is not int me || action == null)
        {
            return new List<int>();
        }
        return Enumerable.Range(0, Creatures.Count).Where(t => ValidTarget(me, action, t)).ToList();
    }

    public bool InRange(int creature, ActionDefinition action, int target)
    {
        return action.Range <= 1 ? Adjacent(creature, target) : Grid.Distance(CellOf(creature), CellOf(target)) <= action.Range + 0.01f;
    }

    /// <summary>
    /// The chance from 0 to 1 that an attack by attacker hits target from where they stand: its
    /// bonus and advantage against the armour class flanking and cover give the target.
    /// </summary>
    public float HitChance(int attacker, int target, string? actionId = null)
    {
        actionId ??= StrikeAction;
        if (attacker < 0 || attacker >= Creatures.Count || target < 0 || target >= Creatures.Count)
        {
            return 0;
        }
        ActionDefinition? action = FindAction(actionId);
        CharacterSheet sheet = Creatures[attacker].Sheet;
        int ac = Fighting ? AttackArmorClass(attacker, target, action != null && action.Range > 1) : Creatures[target].Sheet.ArmorClass(Rules);
        // counted from the system's own dice and outcomes, so it is right for any of them
        return (float)Rules.Checks.Kind(CheckRules.Attack).ChanceToPass(sheet.AttackModifier(Rules), ac, sheet.AttackAdvantage(Rules));
    }

    /// <summary>The same attack as HitChance, as the chance of each of the system's outcomes by id.</summary>
    public Dictionary<string, double> AttackOdds(int attacker, int target, string? actionId = null)
    {
        ActionDefinition? action = FindAction(actionId ?? StrikeAction);
        CharacterSheet sheet = Creatures[attacker].Sheet;
        int ac = Fighting ? AttackArmorClass(attacker, target, action != null && action.Range > 1) : Creatures[target].Sheet.ArmorClass(Rules);
        return Rules.Checks.Kind(CheckRules.Attack).Odds(sheet.AttackModifier(Rules), ac, sheet.AttackAdvantage(Rules));
    }

    /// <summary>
    /// The creature whose turn it is uses one of its actions, aimed at a creature or a square when
    /// the action needs one. A spell spends the slot asked for, else the lowest that will do.
    /// False with a Refusal when it can't.
    /// </summary>
    public bool Use(string id, int? target = null, Cell? at = null, int slot = 0)
    {
        Refusal = "";
        if (!Fighting || InCutscene || CurrentCreature is not int me)
        {
            Refusal = "Not in a fight.";
            return false;
        }
        ActionDefinition? action = FindAction(id);
        string why = "";
        if (action == null || !CanUse(me, action, out why))
        {
            Refusal = _pendingMovement != null ? "Wait for the move to finish."
                : why.Length > 0 ? $"{action!.Name}: {why}."
                : $"{Creatures[me].Sheet.Name} can't do that now.";
            return false;
        }
        int spent = 0;
        if (SpellOf(action) is SpellDefinition spell)
        {
            if (Spellcasting.SlotFor(Creatures[me].Sheet, spell, SpellRules, slot) is not int found)
            {
                Refusal = $"{action.Name}: no slot of level {slot} left.";
                return false;
            }
            spent = found;
        }
        if (action.Target == ActionTarget.Point)
        {
            string aimWhy = "";
            if (at is not Cell spot || !ValidAim(me, action, spot, out aimWhy))
            {
                Refusal = at == null ? $"{action.Name} needs a square to aim at." : aimWhy.Length > 0 ? aimWhy : $"{action.Name} can't go there.";
                return false;
            }
            Perform(action, null, spot, spent);
            return true;
        }
        if (action.Target == ActionTarget.Creature && (target is not int aimed || !ValidTarget(me, action, aimed)))
        {
            Refusal = target == null ? $"{action.Name} needs a target." : $"{action.Name} can't reach {Creatures[target.Value].Sheet.Name}.";
            return false;
        }
        Perform(action, action.Target == ActionTarget.Creature ? target : null, null, spent);
        return true;
    }

    private void Perform(ActionDefinition action, int? target, Cell? at = null, int slot = 0)
    {
        int me = CurrentCreature!.Value;
        Creatures[me].ReadiedAction = action.Readies;
        Encounter!.SpendActions(ActionCost(me, action));
        SyncLog();
        if (action.Log.Length > 0)
        {
            Say(action.Log.Replace("{name}", Creatures[me].Sheet.Name));
        }
        if (SpellOf(action) is SpellDefinition spell)
        {
            CastSpell(me, spell, target, at, slot);
        }
        else
        {
            RunActionEffect(me, action, target, at, slot);
        }
        if (action.EndsTurn && !Encounter.Finished)
        {
            EndCurrentTurn();
        }
        else if (Encounter.Finished)
        {
            EndFight();
        }
        else if (CurrentCreature == me)
        {
            ComputeReach(me);
        }
    }

    internal EffectResult RunActionEffect(int me, ActionDefinition action, int? target, Cell? at = null, int slot = 0)
    {
        var result = new EffectResult();
        if (!action.Effect.IsEmpty)
        {
            Rng random = Fighting ? Encounter!.Random : NextRandom(0xc05eUL);
            Cell aim = at ?? CellOf(target ?? me);
            var context = new EffectContext(Rules, random)
            {
                Self = me,
                // an area lands on everyone it covers, from where it was aimed
                Targets = action.Area != null ? CreaturesIn(me, action, aim) : new List<int> { target ?? me },
                Source = action.Id,
                Slot = slot,
                Dc = Creatures[me].Sheet.DifficultyClass(Rules),
            };
            _aim = aim;
            result = action.Effect.Run(new WorldEffectHost(this), context);
            _aim = null;
            Narrate(result);
            ConcentrationChecks(result, random);
        }
        AfterEffect(result);
        TidyConcentration();
        return result;
    }

    // Whoever an effect dropped lies where they fell, whoever it got up stands again.
    private void AfterEffect(EffectResult result)
    {
        FallenConditions();
        for (int i = 0; i < Creatures.Count; i++)
        {
            if (!Creatures[i].Sheet.Down && Tokens.Tokens[i].Floor == DeadFloor && !Creatures[i].Fled)
            {
                Tokens.Tokens[i].Floor = 0;
            }
        }
        foreach (EffectEvent e in result.Events)
        {
            if (e.Kind != EffectEventKind.Damage || !e.Dropped || e.Who < 0 || e.Who >= Creatures.Count || !Creatures[e.Who].Sheet.Down)
            {
                continue;
            }
            Lie(e.Who);
            if (Creatures[e.Who].Npc >= 0)
            {
                SetFlags(Chapter.Npcs[Creatures[e.Who].Npc].Killed);
            }
        }
    }

    // What an effect did, as log lines and floaters. An attack and its damage read as one line;
    // what they ended follows it.
    private void Narrate(EffectResult result)
    {
        string Name(int who) => who >= 0 && who < Creatures.Count ? Creatures[who].Sheet.Name : "Someone";
        string ConditionName(string id) => Rules.Condition(id)?.Name ?? id;
        Vector2 At(int who) => Tokens.Tokens[who].Position;
        void Float(string text, int who) => _events.Add(new WorldEvent(WorldEventKind.Floater, text) { At = At(who) });

        string attackLine = "";
        int attacked = -1;
        bool hitPending = false;
        var after = new List<string>();
        void Flush()
        {
            if (attackLine.Length > 0)
            {
                Say(attackLine + (hitPending ? " - hit" : ""));
            }
            attackLine = "";
            hitPending = false;
            foreach (string line in after)
            {
                Say(line);
            }
            after.Clear();
        }

        foreach (EffectEvent e in result.Events)
        {
            if (e.Who < 0 || e.Who >= Creatures.Count)
            {
                continue;
            }
            switch (e.Kind)
            {
                case EffectEventKind.Attack:
                    Flush();
                    attackLine = $"{Name(e.By)} attacks {Name(e.Who)} (AC {e.Dc}): {e.Roll.Describe()}";
                    attacked = e.Who;
                    hitPending = e.Success;
                    if (!e.Success)
                    {
                        attackLine += " - miss";
                        Float("Miss", e.Who);
                    }
                    break;
                case EffectEventKind.Damage:
                {
                    string fell = e.Dropped ? $". {Name(e.Who)} goes down!" : "";
                    if (hitPending && attacked == e.Who)
                    {
                        attackLine += (e.Critical ? " - CRITICAL HIT, " : " - hit, ") + e.Roll.Describe() + " damage" + fell;
                        hitPending = false;
                    }
                    else
                    {
                        string type = e.Id.Length == 0 || e.Id == "untyped" ? "" : " " + e.Id;
                        after.Add($"{Name(e.Who)} takes {e.Amount}{type} damage ({e.Roll.Describe()}){fell}");
                    }
                    Float((e.Critical ? "Critical! " : "") + e.Amount, e.Who);
                    break;
                }
                case EffectEventKind.Heal:
                    after.Add($"{Name(e.Who)} recovers {e.Amount} HP.");
                    Float("+" + e.Amount, e.Who);
                    break;
                case EffectEventKind.TempHp:
                    after.Add($"{Name(e.Who)} gains {e.Amount} temporary HP.");
                    break;
                case EffectEventKind.Save:
                    after.Add($"{Name(e.Who)} saves ({e.Id}, DC {e.Dc}): {e.Roll.Describe()}{(e.Success ? " - saved" : " - failed")}");
                    Float(e.Success ? "Saved" : "Failed", e.Who);
                    break;
                case EffectEventKind.Check:
                    after.Add($"{Name(e.By)} tries ({e.Id}, DC {e.Dc}): {e.Roll.Describe()}{(e.Success ? " - success" : " - failure")}");
                    break;
                case EffectEventKind.ConditionAdded:
                    after.Add($"{Name(e.Who)} is {ConditionName(e.Id)}{(e.Amount > 1 ? " " + e.Amount : "")}");
                    break;
                case EffectEventKind.ConditionRemoved:
                case EffectEventKind.ConditionEnded:
                    after.Add($"{Name(e.Who)} is no longer {ConditionName(e.Id)}");
                    break;
                default:
                    break; // nothing to read: the action's own log line says what was done
            }
        }
        Flush();
    }

    /// <summary>What effects ask of the world: sheets, armour class with flanking and cover, who counts as allies and enemies, movement and pushes.</summary>
    private sealed class WorldEffectHost : EffectHost
    {
        private readonly World _world;

        public WorldEffectHost(World world)
        {
            _world = world;
        }

        public override CharacterSheet? Sheet(int who)
        {
            return who >= 0 && who < _world.Creatures.Count ? _world.Creatures[who].Sheet : null;
        }

        // Flanking applies for this roll only and never stays on the sheet.
        public override int ArmorClass(int who, EffectContext context)
        {
            if (Sheet(who) == null || Sheet(context.Self) == null || !_world.Fighting)
            {
                return base.ArmorClass(who, context);
            }
            ActionDefinition? action = _world.FindAction(context.Source);
            return _world.AttackArmorClass(context.Self, who, action != null && action.Range > 1);
        }

        public override bool HasFlag(int who, string flag, EffectContext context)
        {
            if (base.HasFlag(who, flag, context))
            {
                return true;
            }
            ConditionDefinition? flanked = _world.Rules.Condition(_world.Chapter.Rules.Positioning.FlankingCondition);
            return Sheet(who) != null && flanked != null && flanked.HasFlag(flag) && _world.IsFlanked(who);
        }

        // In a fight, allies and enemies are whoever still stands in it, on the doer's side or
        // another. Outside one, everyone standing on the map.
        public override List<int> Group(string which, EffectContext context)
        {
            if (which == "area")
            {
                return context.Targets;
            }
            if (Sheet(context.Self) == null)
            {
                return new List<int>();
            }
            int team = _world.Creatures[context.Self].Team;
            bool allies = which == "allies";
            if (_world.Encounter == null || !_world.Fighting)
            {
                return Enumerable.Range(0, _world.Creatures.Count)
                    .Where(i => !_world.Creatures[i].Sheet.Down && (_world.Creatures[i].Team == team) == allies)
                    .ToList();
            }
            return Enumerable.Range(0, _world.Creatures.Count)
                .Where(i => _world.OrderIndex(i) is int index && _world.Encounter.Order[index].Standing && (_world.Creatures[i].Team == team) == allies)
                .ToList();
        }

        // "movement" is the squares the creature whose turn it is may still move.
        public override bool Resource(int who, string id, int change, EffectContext context)
        {
            if (id != "movement")
            {
                return base.Resource(who, id, change, context);
            }
            if (_world.CurrentCreature != who)
            {
                return false;
            }
            TurnBudget budget = _world.Encounter!.Current.Budget;
            budget.MovementLeft = Math.Max(0, budget.MovementLeft + change);
            return true;
        }

        // Where the action was aimed, else where the doer stands.
        public override bool Surface(string id, float size, int rounds, EffectContext context)
        {
            if (Sheet(context.Self) == null)
            {
                return false;
            }
            return _world.AddSurface(id, _world._aim ?? _world.CellOf(context.Self), size, rounds);
        }

        public override bool Flag(string name, bool set, EffectContext context)
        {
            if (set)
            {
                _world.SetFlags(new[] { name });
            }
            else
            {
                _world.Flags.Remove(name);
            }
            return true;
        }

        // A push or pull in a straight line, stopping at walls and anyone in the way. Being moved
        // like this sets off no reactions.
        public override bool Move(int who, string how, int squares, EffectContext context)
        {
            if (Sheet(who) == null || Sheet(context.Self) == null || (how != "push" && how != "pull") || who == context.Self)
            {
                return false;
            }
            Cell from = _world.CellOf(context.Self);
            Cell at = _world.CellOf(who);
            int direction = how == "push" ? 1 : -1;
            var delta = new Cell(Math.Sign(at.X - from.X) * direction, Math.Sign(at.Y - from.Y) * direction);
            bool moved = false;
            for (int i = 0; i < squares; i++)
            {
                var to = new Cell(at.X + delta.X, at.Y + delta.Y);
                if (to == from || !_world.Walkable(to) || _world.Occupied(to, who)
                    || !Sight.LineOfSight(_world.Grid.Center(at), _world.Grid.Center(to), _world.Map.Walls))
                {
                    break;
                }
                at = to;
                moved = true;
            }
            if (moved)
            {
                Token token = _world.Tokens.Tokens[who];
                token.Position = _world.Grid.Center(at);
                token.Path.Clear();
                Sheet(who)!.ConditionEvent(_world.Rules, "move");
            }
            return moved;
        }
    }
}
