namespace Yorehold.Rules;

// How the creatures the game plays think: their AI profile, and what they see of the fight.
public sealed partial class World
{
    /// <summary>
    /// A creature's AI, worked out fresh each turn from layers, each on top of the last: its
    /// creature file, its encounter, where it was placed, then the chapter's aiChanges whose flags
    /// are set. Heroes in auto-play use the "hero" profile, or fight like the cunning that never run.
    /// </summary>
    public AiProfile AiFor(int index)
    {
        WorldCreature c = Creatures[index];
        Func<string, AiProfile?> lookup = name => _serverProfiles.GetValueOrDefault(name) ?? Chapter.Compendium.AiNamed(name);
        AiProfile profile = AiProfile.Preset("cunning")!;
        if (index < HeroCount)
        {
            return lookup("hero") ?? profile with { FleeHp = 0, FleeLosses = 2, FleeLeaderless = false };
        }
        // Chapter.Load checked every layer; one that no longer fits is skipped, not fatal.
        void Layer(ContentNode? node)
        {
            if (node is not ContentNode written)
            {
                return;
            }
            try
            {
                profile = AiProfile.Read(written, lookup, profile);
            }
            catch (ContentException)
            {
            }
        }
        Layer(Chapter.Compendium.Creature(c.CreatureId)?.Ai);
        foreach (ContentNode layer in c.AiLayers)
        {
            Layer(layer);
        }
        string encounter = c.Group >= 0 && c.Group < Chapter.Encounters.Count ? Chapter.Encounters[c.Group].Id : "";
        foreach (AiChange change in Chapter.AiChanges)
        {
            bool matches = (change.Creature.Length == 0 || change.Creature == c.CreatureId)
                && (change.Encounter.Length == 0 || change.Encounter == encounter)
                && (change.Name.Length == 0 || change.Name == c.Sheet.Name);
            if (matches && change.When.All(Flags.Contains))
            {
                Layer(change.Ai);
            }
        }
        if (_serverCreatureAi.TryGetValue(c.CreatureId, out ContentNode server))
        {
            Layer(server);
        }
        return profile;
    }

    private readonly Dictionary<string, AiProfile> _serverProfiles = new();
    private readonly Dictionary<string, ContentNode> _serverCreatureAi = new();

    /// <summary>
    /// The account server's "ai" config: {"profiles": {"coward": {...}}, "creatures": {"goblin": "coward"}}.
    /// Profiles add to (or replace) the ones in the files; creatures put a last layer on a kind of
    /// creature. Anything that doesn't read is skipped, so a bad config never stops a fight.
    /// </summary>
    public void ApplyServerAi(System.Text.Json.Nodes.JsonNode? config)
    {
        _serverProfiles.Clear();
        _serverCreatureAi.Clear();
        if (config is not System.Text.Json.Nodes.JsonObject j)
        {
            return;
        }
        if (j["profiles"] is System.Text.Json.Nodes.JsonObject profiles)
        {
            AiProfile? Lookup(string name) => _serverProfiles.GetValueOrDefault(name) ?? Chapter.Compendium.AiNamed(name);
            var blank = new AiProfile { Base = "custom" };
            // server profiles can build on each other; a few passes settle any order
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (KeyValuePair<string, System.Text.Json.Nodes.JsonNode?> entry in profiles)
                {
                    if (_serverProfiles.ContainsKey(entry.Key) || entry.Value is not System.Text.Json.Nodes.JsonObject written)
                    {
                        continue;
                    }
                    try
                    {
                        AiProfile profile = AiProfile.Read(ContentNode.Parse("server ai", written.ToJsonString()), Lookup, blank);
                        _serverProfiles[entry.Key] = profile with { Base = entry.Key };
                    }
                    catch (ContentException)
                    {
                    }
                }
            }
        }
        if (j["creatures"] is System.Text.Json.Nodes.JsonObject creatures)
        {
            foreach (KeyValuePair<string, System.Text.Json.Nodes.JsonNode?> entry in creatures)
            {
                // a profile name or an object, as the C++ client takes them
                if (entry.Value is System.Text.Json.Nodes.JsonObject
                    || (entry.Value is System.Text.Json.Nodes.JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String))
                {
                    _serverCreatureAi[entry.Key] = ContentNode.Parse("server ai", entry.Value.ToJsonString());
                }
            }
        }
    }

    // Walking distance from every cell to the nearest standing foe of team.
    private Dictionary<Cell, float> DistanceToFoes(int team)
    {
        var foes = new List<Cell>();
        for (int i = 0; i < Creatures.Count; i++)
        {
            if (Creatures[i].Team != team && Creatures[i].Team != 2 && !Creatures[i].Sheet.Down && OrderIndex(i) != null)
            {
                foes.Add(CellOf(i));
            }
        }
        return DistanceFrom(foes);
    }

    // The group of enemies not yet in the fight within so many squares' walk of creature, if any.
    private int? SleepingGroupNear(int creature, float squares)
    {
        var cells = new List<Cell>();
        var groups = new List<int>();
        for (int i = HeroCount; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            if (c.Team == Creatures[creature].Team && !c.Awake && !c.Sheet.Down && OrderIndex(i) == null && c.Npc < 0)
            {
                cells.Add(CellOf(i));
                groups.Add(c.Group);
            }
        }
        if (cells.Count == 0)
        {
            return null;
        }
        Dictionary<Cell, float> distance = DistanceFrom(cells);
        if (!distance.TryGetValue(CellOf(creature), out float here) || here > squares)
        {
            return null;
        }
        // The group of whoever is nearest in a straight line.
        int nearest = 0;
        for (int i = 1; i < cells.Count; i++)
        {
            if (Grid.Distance(cells[i], CellOf(creature)) < Grid.Distance(cells[nearest], CellOf(creature)))
            {
                nearest = i;
            }
        }
        return groups[nearest];
    }

    // Walking distance from every cell to the nearest of cells, around walls (not creatures).
    private Dictionary<Cell, float> DistanceFrom(List<Cell> cells)
    {
        var distance = new Dictionary<Cell, float>();
        var queue = new HeapQueue<(float Cost, Cell Cell)>((a, b) => a.Cost > b.Cost);
        foreach (Cell c in cells)
        {
            distance[c] = 0;
            queue.Push((0, c));
        }
        while (queue.Count > 0)
        {
            (float cost, Cell c) = queue.Pop();
            if (cost > distance[c])
            {
                continue;
            }
            foreach (Cell next in Grid.Neighbours(c))
            {
                if (!Walkable(next) || (next.X != c.X && next.Y != c.Y && (!Walkable(new Cell(next.X, c.Y)) || !Walkable(new Cell(c.X, next.Y)))))
                {
                    continue;
                }
                float nextCost = cost + Grid.StepCost(c, next, 0);
                if (!distance.TryGetValue(next, out float known) || nextCost < known)
                {
                    distance[next] = nextCost;
                    queue.Push((nextCost, next));
                }
            }
        }
        return distance;
    }

    /// <summary>The fight as creature me's AI sees it. who gets the creature index of each unit.</summary>
    public TacticalView TacticalViewOf(int me, List<int> who)
    {
        var view = new TacticalView();
        who.Clear();
        for (int i = 0; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            if (c.Sheet.Down || OrderIndex(i) == null || c.Surrendered)
            {
                continue;
            }
            if (i == me)
            {
                view.Self = who.Count;
            }
            DiceExpression? damage = DiceExpression.Parse(c.Sheet.DamageDice(Rules));
            view.Units.Add(new TacticalUnit
            {
                Team = c.Team,
                At = CellOf(i),
                Hp = c.Sheet.Hp,
                MaxHp = c.Sheet.MaxHp,
                ArmorClass = c.Sheet.ArmorClass(Rules),
                AttackBonus = c.Sheet.AttackModifier(Rules),
                AverageDamage = damage != null ? Math.Max(1.0f, (float)damage.Average()) : 1.0f,
                Damage = damage,
                Speed = c.Sheet.SpeedSquares(Rules),
                Leader = AiFor(i).Leader,
            });
            who.Add(i);
        }
        int team = Creatures[me].Team;
        view.Actions = Encounter!.Current.Budget.Actions;
        view.Attack = Rules.Checks.Kind(CheckRules.Attack);
        view.CriticalDamage = Rules.Checks.CriticalDamage;
        ActionDefinition? strike = FindAction(StrikeAction);
        view.StrikeCost = strike != null ? ActionCost(me, strike) : Encounter.StrikeCost;
        if (view.Actions >= 1)
        {
            ComputeReach(me, Creatures[me].Sheet.SpeedSquares(Rules));
            view.DashReach = _reach;
        }
        ComputeReach(me);
        view.Reach = _reach;
        view.FoeDistance = DistanceToFoes(team);
        view.SideAtStart = _sideAtStart[team == 0 ? 0 : 1];
        view.HadLeader = _hadLeader[team == 0 ? 0 : 1];
        view.Fleeing = Creatures[me].Fleeing;
        view.BreakAs = Creatures[me].BreakAs;
        if (view.BreakAs == "alarm")
        {
            var help = new List<Cell>();
            for (int i = HeroCount; i < Creatures.Count; i++)
            {
                WorldCreature c = Creatures[i];
                if (c.Team == team && !c.Awake && !c.Sheet.Down && OrderIndex(i) == null && c.Npc < 0)
                {
                    help.Add(CellOf(i));
                }
            }
            if (help.Count > 0)
            {
                view.AllyDistance = DistanceFrom(help);
            }
        }
        return view;
    }
}
