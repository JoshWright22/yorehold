namespace Yorehold.Rules;

/// <summary>A hero's reaction waiting for an answer (World.React), or for its time to run out.</summary>
public sealed class ReactionPrompt
{
    public int Creature { get; init; }
    /// <summary>Whoever set it off by moving.</summary>
    public int Mover { get; init; }
    public string Name { get; init; } = "";
    public double SecondsLeft { get; set; }
    /// <summary>A new number for every offer, so an old answer can't take a later one.</summary>
    public ulong Id { get; init; }
}

// Fights: starting and ending them, whose turn it is, ending turns, and the turns the AI plays.
public sealed partial class World
{
    /// <summary>The actions the game itself uses, by the names the ruleset's roles give them.</summary>
    public string StrikeAction => Rules.Roles.Strike;
    public string StrideAction => Rules.Roles.Stride;
    public string EndTurnAction => Rules.Roles.EndTurn;
    public string InteractAction => Rules.Roles.Interact;

    private enum EnemyStep
    {
        Think,
        Walk,
        Strike,
        Wait,
    }

    private readonly int[] _sideAtStart = new int[2];
    private readonly bool[] _hadLeader = new bool[2];
    private Dictionary<Cell, float> _reach = new();
    private Cell _standing;
    private int _fights;
    private int _logShown;
    private ulong _turnBlockShown;
    private int? _pendingAttack;
    private string _pendingAction = "";
    private Cell? _pendingStep;
    private EnemyStep _enemyStep;
    private double _enemyTimer;
    private int? _enemyTarget;

    /// <summary>The fight, once one has started. It stays after the fight ends until the next one.</summary>
    public Encounter? Encounter { get; private set; }
    /// <summary>The party lost a fight. Going back to the last save comes with saves (P10).</summary>
    public bool PartyWiped { get; private set; }

    /// <summary>The creature whose turn it is; null outside a fight.</summary>
    public int? CurrentCreature
    {
        get
        {
            if (Encounter == null || !Encounter.Started || Encounter.Finished)
            {
                return null;
            }
            CharacterSheet current = Encounter.Current.Sheet;
            int found = Creatures.FindIndex(c => ReferenceEquals(c.Sheet, current));
            return found < 0 ? null : found;
        }
    }

    /// <summary>Where a creature stands in the fight's order; null if it isn't in the fight.</summary>
    public int? OrderIndex(int creature)
    {
        if (Encounter == null || creature < 0 || creature >= Creatures.Count)
        {
            return null;
        }
        for (int i = 0; i < Encounter.Order.Count; i++)
        {
            if (ReferenceEquals(Encounter.Order[i].Sheet, Creatures[creature].Sheet))
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>What a creature in the fight has left this turn (or kept from its last one).</summary>
    public TurnBudget? BudgetOf(int creature)
    {
        return OrderIndex(creature) is int index ? Encounter!.Order[index].Budget : null;
    }

    /// <summary>Actions the creature whose turn it is still has.</summary>
    public int ActionsLeft => Encounter != null && CurrentCreature != null ? Encounter.Current.Budget.Actions : 0;

    /// <summary>Squares of movement the creature whose turn it is still has.</summary>
    public int MovementLeft => Encounter != null && CurrentCreature != null ? Encounter.Current.Budget.MovementLeft : 0;

    public bool Adjacent(int a, int b) => Grid.Distance(CellOf(a), CellOf(b)) <= 1.01f;

    /// <summary>
    /// Starts a fight with an encounter: everyone stops on a square of their own, the encounter's
    /// creatures wake, initiative is rolled and the first turn begins. only = just that creature
    /// of the group; surprise = the enemies lose their first turn.
    /// </summary>
    public void StartFight(int group, int? only = null, bool surprise = false)
    {
        Raise("fightStart");
        for (int i = 0; i < HeroCount; i++)
        {
            SetSneaking(i, false); // nobody sneaks through a fight
            _sneak[i].Reset();
            Creatures[i].MayPrepare = false; // the day's spells are settled once fighting starts
        }
        // Everyone stops on a square of their own.
        var taken = new List<Cell>();
        for (int i = 0; i < Creatures.Count; i++)
        {
            Token token = Tokens.Tokens[i];
            token.Path.Clear();
            if (token.Floor == DeadFloor)
            {
                continue;
            }
            Cell spot = Grid.CellAt(token.Position);
            for (int radius = 0; radius < 4; radius++)
            {
                bool found = false;
                for (int dy = -radius; dy <= radius && !found; dy++)
                {
                    for (int dx = -radius; dx <= radius && !found; dx++)
                    {
                        var c = new Cell(spot.X + dx, spot.Y + dy);
                        if (Map.Walkable(c) && !taken.Contains(c))
                        {
                            spot = c;
                            found = true;
                        }
                    }
                }
                if (found)
                {
                    break;
                }
            }
            taken.Add(spot);
            token.Position = Grid.Center(spot);
        }

        Encounter = new Encounter(Rules, unchecked(_seed * 7919UL + (ulong)++_fights));
        _logShown = 0;
        _turnBlockShown = 0;
        _sideAtStart[0] = _sideAtStart[1] = 0;
        _hadLeader[0] = _hadLeader[1] = false;
        PartyWiped = false;
        FightGroup = group;
        for (int i = 0; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            c.Fleeing = false;
            c.BreakAs = "";
            c.Sheet.SyncDeath(Rules);
            if (c.Sheet.Down && !(Rules.Death.Enabled && c.Sheet.Death.Saves && !c.Sheet.Death.Dead))
            {
                continue;
            }
            if (c.Team == 0)
            {
                Encounter.Add(c.Sheet, 0);
            }
            else if (c.Group == group && c.Team == 1 && (only == null || only == i))
            {
                c.Awake = true;
                Encounter.Add(c.Sheet, 1);
            }
            else
            {
                continue;
            }
            _sideAtStart[c.Team]++;
            _hadLeader[c.Team] |= AiFor(i).Leader;
        }

        if (group >= 0 && group < Chapter.Encounters.Count && Chapter.Encounters[group].Text.Length > 0)
        {
            Say(Chapter.Encounters[group].Text);
        }
        Tokens.Settings.InCombat = true;
        if (surprise)
        {
            Say("The party strikes from hiding!");
            Encounter.Surprise(1);
        }
        Encounter.Start();
        SyncLog();
        _events.Add(new WorldEvent(WorldEventKind.Banner, "Combat") { Seconds = 1.5 });
        _events.Add(new WorldEvent(WorldEventKind.Fight) { Group = group });
        if (Encounter.Finished)
        {
            EndFight(); // nobody there to fight
            return;
        }
        BeginTurn();
    }

    /// <summary>
    /// The hidden party attacks an enemy it can see that hasn't noticed it: the fight with its
    /// encounter starts with the enemies surprised.
    /// </summary>
    public bool Ambush(int creature)
    {
        Refusal = "";
        if (Fighting || InCutscene || creature < HeroCount || creature >= Creatures.Count || Creatures[creature].Team != 1
            || Creatures[creature].Awake || Creatures[creature].Sheet.Down || Fog.State(0, 0, CellOf(creature)) != FogState.Visible)
        {
            Refusal = "Nobody there to ambush.";
            return false;
        }
        if (!AnySneaking)
        {
            Refusal = "The party isn't hidden.";
            return false;
        }
        StartFight(Creatures[creature].Group, null, true);
        return true;
    }

    /// <summary>Whoever's turn it is in the shared block can hand it to another member of their side who hasn't gone.</summary>
    public bool CanChooseTurn(int creature)
    {
        int? index = OrderIndex(creature);
        int? current = CurrentCreature;
        return index != null && current != null && !InCutscene && _pendingMovement == null && _pendingAttack == null
            && Tokens.Tokens[current.Value].Path.Count == 0 && Encounter!.CanSelectTurn(index.Value);
    }

    /// <summary>Hands the shared turn to another member of the block. Everyone keeps what they had left.</summary>
    public bool ChooseTurn(int creature)
    {
        Refusal = "";
        if (!CanChooseTurn(creature) || !Encounter!.SelectTurn(OrderIndex(creature)!.Value))
        {
            Refusal = "Not their turn.";
            return false;
        }
        SyncLog();
        BeginTurn();
        return true;
    }

    /// <summary>Ends the turn of whoever's turn it is (the End turn action does the same).</summary>
    public bool EndTurn() => Use(EndTurnAction);

    // Clears everything a fight leaves behind, for a new adventure.
    private void ResetFight()
    {
        Encounter = null;
        PartyWiped = false;
        ReactionPrompt = null;
        _pendingMovement = null;
        _pendingReaction = null;
        _pendingAttack = null;
        _pendingStep = null;
        _reach = new Dictionary<Cell, float>();
        _fights = 0;
    }

    // The encounter's own log lines that haven't been said yet.
    private void SyncLog()
    {
        if (Encounter == null)
        {
            return;
        }
        for (; _logShown < Encounter.Log.Count; _logShown++)
        {
            Say(Encounter.Log[_logShown]);
        }
    }

    // Something happened to everyone ("fightStart", "fightEnd"): conditions that end on it come off.
    private void Raise(string name)
    {
        foreach (WorldCreature c in Creatures)
        {
            c.Sheet.ConditionEvent(Rules, name);
        }
    }

    // The ruleset's death conditions (or plain Downed and Dead without death saves), and the
    // fallen lying on the floor where they dropped.
    private void FallenConditions()
    {
        for (int i = 0; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            Token token = Tokens.Tokens[i];
            if (c.Sheet.Down && !c.Fled && c.Sheet.HasCondition(HiddenCondition))
            {
                c.Sheet.RemoveCondition(HiddenCondition);
            }
            if (Rules.Death.Enabled)
            {
                if (!c.Fled)
                {
                    c.Sheet.SyncDeath(Rules);
                }
                if (c.Sheet.Down)
                {
                    Lie(i);
                }
                else if (token.Floor == DeadFloor)
                {
                    token.Floor = 0;
                }
                continue;
            }
            string fallen = PartyMember(i) ? DownedCondition : DeadCondition;
            if (c.Sheet.Down)
            {
                // Someone who got away is out of the adventure, not lying in it.
                if (!c.Fled && !c.Sheet.HasCondition(fallen) && Rules.Condition(fallen) != null)
                {
                    c.Sheet.AddCondition(Rules, fallen);
                }
                if (!PartyMember(i))
                {
                    Lie(i);
                }
            }
            else if (c.Sheet.HasCondition(fallen))
            {
                c.Sheet.ConditionEvent(Rules, "healed");
                c.Sheet.RemoveCondition(fallen); // whatever its file says ends it, nobody on their feet is down
            }
        }
    }

    // Puts a fallen creature's token on the floor.
    private void Lie(int creature)
    {
        Token token = Tokens.Tokens[creature];
        if (token.Floor != DeadFloor)
        {
            token.Floor = DeadFloor;
            token.Selected = false;
            token.Path.Clear();
        }
    }

    private bool PartyMember(int creature) => InParty(creature);

    private void BeginTurn()
    {
        FallenConditions();
        int? current = CurrentCreature;
        if (current == null || Encounter == null)
        {
            return;
        }
        if (_turnBlockShown != Encounter.BlockSerial)
        {
            // A Ready lasts until its owner's next turn comes round.
            _turnBlockShown = Encounter.BlockSerial;
            for (int i = Encounter.BlockFirst; i < Encounter.BlockEnd; i++)
            {
                if (Encounter.Order[i].TurnDone)
                {
                    continue;
                }
                foreach (WorldCreature c in Creatures.Where(c => ReferenceEquals(c.Sheet, Encounter.Order[i].Sheet)))
                {
                    c.ReadiedAction = "";
                }
            }
        }
        Tokens.Settings.ActiveTurn = current;
        _pendingAttack = null;
        _enemyStep = EnemyStep.Think;
        _enemyTimer = 0;
        _enemyTarget = null;
        if (Creatures[current.Value].Team == 0)
        {
            for (int i = 0; i < Tokens.Tokens.Count; i++)
            {
                Tokens.Tokens[i].Selected = i == current;
            }
        }
        ComputeReach(current.Value);
        _events.Add(new WorldEvent(WorldEventKind.Turn, Creatures[current.Value].Sheet.Name) { At = Tokens.Tokens[current.Value].Position });
        if (SurfacesAt(CellOf(current.Value)).Count > 0)
        {
            SurfaceDamage(current.Value);
            if (Encounter.Finished)
            {
                EndFight();
            }
        }
    }

    private void EndCurrentTurn()
    {
        if (CurrentCreature is int current)
        {
            Token token = Tokens.Tokens[current];
            if (token.Path.Count > 0)
            {
                token.Position = token.Path[^1];
            }
            token.Path.Clear();
        }
        int round = Encounter!.Round;
        Encounter.NextTurn();
        if (Encounter.Round != round)
        {
            SurfacesAge();
        }
        SyncLog();
        if (Encounter.Finished)
        {
            EndFight();
        }
        else
        {
            BeginTurn();
        }
    }

    // One side is left: a win heals and pays the party and sets the beaten encounters' flags; a
    // loss is the party wiped.
    private void EndFight()
    {
        if (PartyWiped || Encounter == null)
        {
            return;
        }
        foreach (WorldCreature c in Creatures)
        {
            c.ReadiedAction = "";
        }
        Tokens.Settings.InCombat = false;
        Tokens.Settings.ActiveTurn = null;
        _reach.Clear();
        _pendingAttack = null;
        _pendingStep = null;
        _pendingMovement = null;
        _pendingReaction = null;
        ReactionPrompt = null;
        foreach (Token token in Tokens.Tokens)
        {
            token.Selected = false;
        }
        Raise("fightEnd");
        FallenConditions();

        if (Encounter.WinningTeam != 0)
        {
            PartyWiped = true;
            _events.Add(new WorldEvent(WorldEventKind.Banner, Chapter.DefeatText) { Seconds = 2 });
            _events.Add(new WorldEvent(WorldEventKind.FightOver, "defeat") { Group = FightGroup ?? -1 });
            Say(Chapter.DefeatText);
            if (Chapter.WipeCutscene.Length > 0)
            {
                _events.Add(new WorldEvent(WorldEventKind.Cutscene, Chapter.WipeCutscene));
                InCutscene = true;
            }
            return;
        }

        // The encounter that started the fight says what it is worth, or the chapter does.
        int xp = Chapter.XpPerVictory;
        int group = FightGroup ?? -1;
        if (group >= 0 && group < Chapter.Encounters.Count && Chapter.Encounters[group].Xp is int worth)
        {
            xp = worth;
        }
        FightGroup = null;

        // Healing after a win, as the ruleset says: the downed get up, then any recovery.
        Rng rest = NextRandom(0x5eedUL);
        for (int i = 0; i < Creatures.Count; i++)
        {
            if (!PartyMember(i))
            {
                continue;
            }
            CharacterSheet sheet = Creatures[i].Sheet;
            if (sheet.Down && !sheet.Death.Dead && Rules.ReviveAfterVictory > 0)
            {
                sheet.Hp = Math.Min(Rules.ReviveAfterVictory, sheet.MaxHp);
                Say($"{sheet.Name} gets back up with {sheet.Hp} HP.");
            }
            int healed = sheet.Recover(Rules, Rules.AfterVictory, rest);
            if (healed > 0)
            {
                Say($"{sheet.Name} recovers {healed} HP.");
            }
            if (!sheet.Down)
            {
                Tokens.Tokens[i].Floor = 0;
            }
            if (i < HeroCount)
            {
                sheet.AddXp(Rules, xp);
                GainLevels(i);
            }
        }
        FallenConditions(); // the revived are no longer Downed
        SelectStandingHero();
        Say(Chapter.VictoryText.Replace("{xp}", xp.ToString()));
        _events.Add(new WorldEvent(WorldEventKind.FightOver, "victory") { Group = group });

        // Every encounter with nobody left standing sets its story flags.
        var won = new List<string>();
        for (int g = 0; g < Chapter.Encounters.Count; g++)
        {
            bool beaten = !Creatures.Skip(HeroCount).Any(c => c.Group == g && !c.Sheet.Down && !c.Surrendered);
            if (beaten)
            {
                won.AddRange(Chapter.Encounters[g].Set);
            }
        }
        SetFlags(won);
        DropLoot();

        if (ChapterCleared())
        {
            PlayEnding();
            return;
        }
        _events.Add(new WorldEvent(WorldEventKind.Banner, "Victory") { Seconds = 2 });
        RequestSave();
    }

    // The chapter is done: its ending cutscene, or its text as a banner. Once only, like the win condition.
    private void PlayEnding()
    {
        if (_won)
        {
            return;
        }
        _won = true;
        if (Chapter.ClearedCutscene.Length > 0)
        {
            _events.Add(new WorldEvent(WorldEventKind.Cutscene, Chapter.ClearedCutscene));
            InCutscene = true;
            return;
        }
        _events.Add(new WorldEvent(WorldEventKind.Banner, Chapter.ClearedText) { Seconds = 1e9 });
        Say(Chapter.ClearedText);
    }

    private void SelectStandingHero()
    {
        int? pick = null;
        for (int i = 0; i < HeroCount; i++)
        {
            if (!Creatures[i].Sheet.Down && (pick == null || Tokens.Tokens[i].Selected))
            {
                pick = i;
            }
        }
        for (int i = 0; i < HeroCount; i++)
        {
            Tokens.Tokens[i].Selected = pick == i;
        }
    }

    // One step of a fight's time: a move waiting on a reaction, a hero's walk-up-and-swing, or
    // the turn of a creature the AI plays.
    private void TakeTurns(double deltaSeconds)
    {
        if (PartyWiped)
        {
            return;
        }
        if (_pendingMovement != null)
        {
            ReactionTime(deltaSeconds);
            return;
        }
        if (CurrentCreature is not int now)
        {
            return;
        }
        if (Creatures[now].Team == 0 && !Options.AutoPlay)
        {
            if (SwingReady())
            {
                SwingIfReady();
            }
            return;
        }
        UpdateAiTurn(deltaSeconds);
    }

    /// <summary>
    /// Strikes a creature, walking first to the cheapest square in reach of it when it isn't
    /// already. with names the action (Strike unless another one is asked for).
    /// </summary>
    public bool Attack(int target, string? with = null)
    {
        with ??= StrikeAction;
        Refusal = "";
        if (CurrentCreature is not int me)
        {
            Refusal = "Not in a fight.";
            return false;
        }
        ActionDefinition? action = FindAction(with);
        string why = "";
        if (action == null || !CanUse(me, action, out why))
        {
            Refusal = why.Length > 0 ? $"{Creatures[me].Sheet.Name} can't attack: {why}." : $"{Creatures[me].Sheet.Name} doesn't have the actions left to attack.";
            return false;
        }
        if (InRange(me, action, target))
        {
            return Use(with, target);
        }
        Cell goal = CellOf(target);
        float range = action.Range + 0.01f;
        Cell? best = null;
        float bestCost = 0;
        foreach ((Cell c, float cost) in OrderedReach())
        {
            if (Grid.Distance(c, goal) <= range && (best == null || cost < bestCost))
            {
                best = c;
                bestCost = cost;
            }
        }
        if (best == null)
        {
            Refusal = $"{Creatures[target].Sheet.Name} is out of reach this turn.";
            return false;
        }
        if (!MoveTo(best.Value))
        {
            return false;
        }
        _pendingAttack = target;
        _pendingAction = with;
        _pendingStep = best;
        return true;
    }

    private bool SwingReady()
    {
        return CurrentCreature is int me && _pendingAttack != null && _pendingStep != null && Tokens.Tokens[me].Path.Count == 0
            && CellOf(me) == _pendingStep;
    }

    // Walked up to swing at someone: swing once the walk has landed.
    private void SwingIfReady()
    {
        if (!SwingReady())
        {
            return;
        }
        int me = CurrentCreature!.Value;
        int target = _pendingAttack!.Value;
        _pendingAttack = null;
        _pendingStep = null;
        ActionDefinition? action = FindAction(_pendingAction);
        if (action != null && InRange(me, action, target))
        {
            Use(_pendingAction, target);
        }
    }

    // The AI's turn, in steps with pauses so the screen can show it: think, walk, strike, end.
    private void UpdateAiTurn(double deltaSeconds)
    {
        int me = CurrentCreature!.Value;
        Token token = Tokens.Tokens[me];
        _enemyTimer += deltaSeconds;
        switch (_enemyStep)
        {
            case EnemyStep.Think:
            {
                if (_enemyTimer < 0.45)
                {
                    return;
                }
                var random = new Rng(_seed ^ ((ulong)_fights << 40) ^ ((ulong)Encounter!.Round << 20) ^ (ulong)me);
                AiProfile profile = AiFor(me);
                // The first time its nerve goes, it settles how it reacts for the rest of the fight.
                if (Creatures[me].BreakAs.Length == 0 && Tactics.WantsToFlee(profile, TacticalViewOf(me, new List<int>())))
                {
                    Creatures[me].BreakAs = Tactics.PickBreak(profile, random);
                }
                // A spell worth more than a swing is cast from where it stands, then it thinks again.
                if (!Creatures[me].Fleeing && Creatures[me].BreakAs is "" or "fight" && PickAbility(me) is AbilityChoice spell
                    && spell.Value >= StrikeWorth(me) && Use(spell.Action.Id, spell.Target, spell.At))
                {
                    _enemyTimer = 0;
                    return;
                }
                // Nobody in reach of a spell that beats its swing: it walks only as far as it must to cast.
                if (!Creatures[me].Fleeing && Creatures[me].BreakAs is "" or "fight" && ApproachToCast(me) is Cell spot && spot != _standing && MoveTo(spot))
                {
                    _enemyTimer = 0;
                    _enemyTarget = null;
                    _enemyStep = EnemyStep.Walk;
                    return;
                }
                var who = new List<int>();
                TacticalView view = TacticalViewOf(me, who);
                TacticalChoice choice = Tactics.Decide(profile, view, Grid, random);
                _enemyTimer = 0;
                _enemyTarget = null;
                if (choice.Kind == ChoiceKind.Surrender)
                {
                    Surrender(me);
                    return;
                }
                if (choice.Kind == ChoiceKind.Attack)
                {
                    _enemyTarget = who[choice.Target];
                }
                if (choice.Kind is ChoiceKind.Flee or ChoiceKind.Alarm && !Creatures[me].Fleeing)
                {
                    Flee(me, choice.Kind == ChoiceKind.Alarm ? "alarm" : "flee");
                }
                if (choice.Dash)
                {
                    Use(StrideAction); // works out the reach again
                }
                if (choice.Cell != _standing)
                {
                    MoveTo(choice.Cell);
                }
                if (CurrentCreature != me)
                {
                    return;
                }
                _enemyStep = EnemyStep.Walk;
                return;
            }
            case EnemyStep.Walk:
                if (token.Path.Count > 0)
                {
                    return;
                }
                _enemyTimer = 0;
                // having walked, a spell may beat the swing it came for; then it thinks again
                if (!Creatures[me].Fleeing && Creatures[me].BreakAs is "" or "fight" && PickAbility(me) is AbilityChoice cast
                    && cast.Value >= StrikeWorth(me) && Use(cast.Action.Id, cast.Target, cast.At))
                {
                    _enemyStep = EnemyStep.Think;
                    return;
                }
                _enemyStep = _enemyTarget is int aimed && Adjacent(me, aimed) && CanUse(me, StrikeAction) ? EnemyStep.Strike : EnemyStep.Wait;
                return;
            case EnemyStep.Strike:
            {
                if (_enemyTimer < 0.25)
                {
                    return;
                }
                _enemyTimer = 0;
                _enemyStep = EnemyStep.Wait;
                int target = _enemyTarget!.Value;
                Use(StrikeAction, target);
                // Actions to spare and the target still up: hit it again.
                if (Fighting && CurrentCreature == me && CanUse(me, StrikeAction) && !Creatures[target].Sheet.Down && OrderIndex(target) != null)
                {
                    _enemyStep = EnemyStep.Strike;
                }
                return;
            }
            case EnemyStep.Wait:
            {
                if (_enemyTimer < 0.6)
                {
                    return;
                }
                WorldCreature runner = Creatures[me];
                // Running for help and close enough to shout: the allies it reached join the fight.
                if (runner.Fleeing && runner.BreakAs == "alarm" && SleepingGroupNear(me, (float)AiFor(me).AlarmReach) is int group)
                {
                    RaiseAlarm(me, group);
                    Use(EndTurnAction);
                    return;
                }
                // Running, far enough from everyone and out of their sight (or walled off from
                // them): it's gone. While the party can see it they get a chance to chase it down.
                if (runner.Fleeing && !(runner.BreakAs == "alarm" && SleepingGroupNear(me, 1e6f) != null))
                {
                    Dictionary<Cell, float> away = DistanceToFoes(runner.Team);
                    bool watched = runner.Team != 0 && Fog.State(0, 0, CellOf(me)) == FogState.Visible;
                    if (!away.TryGetValue(CellOf(me), out float distance) || (distance >= (float)AiFor(me).EscapeAt && !watched))
                    {
                        Escape(me);
                        return;
                    }
                }
                Use(EndTurnAction);
                return;
            }
        }
    }

    // Its morale broke: it runs from now on (for help, as "alarm").
    private void Flee(int creature, string how)
    {
        WorldCreature runner = Creatures[creature];
        runner.Fleeing = true;
        runner.BreakAs = how;
        Say(runner.Sheet.Name + (how == "alarm" ? " runs for help!" : " turns and runs!"));
    }

    // Gives up where it stands: out of the fight, on nobody's side, still standing.
    private void Surrender(int creature)
    {
        WorldCreature yielded = Creatures[creature];
        Token token = Tokens.Tokens[creature];
        if (token.Path.Count > 0)
        {
            token.Position = token.Path[^1];
        }
        token.Path.Clear();
        yielded.Surrendered = true;
        yielded.Fleeing = false;
        yielded.Team = 2;
        Say($"{yielded.Sheet.Name} throws down their weapon and surrenders!");
        Encounter!.Withdraw(OrderIndex(creature)!.Value, "surrenders");
        SyncLog();
        if (Encounter.Finished)
        {
            EndFight();
        }
        else
        {
            EndCurrentTurn();
        }
    }

    // Everyone asleep in the group it ran to joins the fight where they stand.
    private void RaiseAlarm(int creature, int group)
    {
        WorldCreature runner = Creatures[creature];
        Say($"{runner.Sheet.Name} raises the alarm!");
        runner.Fleeing = false;
        runner.BreakAs = "fight"; // with friends at its side it fights on
        for (int i = HeroCount; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            if (c.Group != group || c.Team != 1 || c.Awake || c.Sheet.Down)
            {
                continue;
            }
            c.Awake = true;
            Encounter!.Join(c.Sheet, 1);
            _sideAtStart[1]++;
            _hadLeader[1] |= AiFor(i).Leader;
        }
        if (group >= 0 && group < Chapter.Encounters.Count && Chapter.Encounters[group].Text.Length > 0)
        {
            Say(Chapter.Encounters[group].Text);
        }
        SyncLog();
    }

    // Got away: out of the adventure, not lying dead in it.
    private void Escape(int creature)
    {
        WorldCreature gone = Creatures[creature];
        Token token = Tokens.Tokens[creature];
        Say($"{gone.Sheet.Name} gets away.");
        if (token.Path.Count > 0)
        {
            token.Position = token.Path[^1];
        }
        token.Path.Clear();
        token.Floor = DeadFloor;
        gone.Sheet.Hp = 0;
        gone.Fled = true;
        foreach (string id in gone.Sheet.Conditions.Select(c => c.Id).ToList())
        {
            gone.Sheet.RemoveCondition(id);
        }
        if (Encounter!.Finished)
        {
            EndFight();
        }
        else
        {
            EndCurrentTurn();
        }
    }

    /// <summary>The squares the creature whose turn it is can move to with what it has left, and what each costs.</summary>
    public IReadOnlyDictionary<Cell, float> ReachableCells()
    {
        if (CurrentCreature is int current && _pendingMovement == null)
        {
            ComputeReach(current);
        }
        return _reach;
    }

    // Where the mover can get to with its movement (plus extra squares), around walls and creatures.
    private void ComputeReach(int mover, int extra = 0)
    {
        _reach = new Dictionary<Cell, float>();
        _standing = CellOf(mover);
        if (Creatures[mover].Sheet.HasFlag(Rules, "cantMove") || Encounter == null)
        {
            _reach[_standing] = 0;
            return;
        }
        float budget = Encounter.Current.Budget.MovementLeft + extra + 0.01f;
        bool Open(Cell c) => Walkable(c) && !Occupied(c, mover);
        var queue = new HeapQueue<(float Cost, Cell Cell)>((a, b) => a.Cost > b.Cost);
        _reach[_standing] = 0;
        queue.Push((0, _standing));
        while (queue.Count > 0)
        {
            (float cost, Cell c) = queue.Pop();
            if (cost > _reach[c])
            {
                continue;
            }
            foreach (Cell next in Grid.Neighbours(c))
            {
                if (!Open(next))
                {
                    continue;
                }
                // Same rule as paths: no cutting corners past walls or creatures.
                if (next.X != c.X && next.Y != c.Y && (!Open(new Cell(next.X, c.Y)) || !Open(new Cell(c.X, next.Y))))
                {
                    continue;
                }
                float nextCost = cost + Grid.StepCost(c, next, 0);
                if (nextCost > budget)
                {
                    continue;
                }
                if (!_reach.TryGetValue(next, out float known) || nextCost < known)
                {
                    _reach[next] = nextCost;
                    queue.Push((nextCost, next));
                }
            }
        }
    }

    // The reach in a fixed order, so the first of equal costs is the same every run.
    private IEnumerable<(Cell Cell, float Cost)> OrderedReach()
    {
        return _reach.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).Select(p => (p.Key, p.Value));
    }
}
