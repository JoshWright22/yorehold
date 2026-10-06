namespace Yorehold.Rules;

public enum ActionTarget
{
    Self,
    Creature,
    Point,
}

public enum ActionSide
{
    Enemy,
    Ally,
    Any,
}

public enum AreaShape
{
    Burst,
    Cone,
    Line,
    Square,
}

/// <summary>Everyone of the target's side inside it is who the effect lands on. Sizes are in squares.</summary>
public record ActionArea(AreaShape Shape, double Size, double Width = 0, double Angle = 53.13)
{
    /// <summary>A cone or line starts at the doer and points at the aim.</summary>
    public bool Directed => Shape is AreaShape.Cone or AreaShape.Line;
}

/// <summary>Something a creature can do on its turn: rulesets/yorehold/actions/strike.json.</summary>
public class ActionDefinition
{
    private static readonly string[] Fields =
    {
        "id", "name", "description", "order", "cost", "endsTurn", "general", "readies", "requires", "target", "area", "log", "save", "effects",
    };

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int Order { get; init; }
    public int Cost { get; init; } = 1;
    /// <summary>One action per hand the weapon in use needs, in place of Cost.</summary>
    public bool CostsHands { get; init; }
    public bool EndsTurn { get; init; }
    /// <summary>True: every creature has it. False: something has to grant it.</summary>
    public bool General { get; init; } = true;
    public string Readies { get; init; } = "";
    public List<string> NeedsFlags { get; init; } = new();
    public List<string> BarredBy { get; init; } = new();
    public Dictionary<string, int> NeedsResources { get; init; } = new();
    public ActionTarget Target { get; init; }
    public ActionSide Side { get; init; }
    public int Range { get; init; } = 1;
    public bool AllowsDowned { get; init; }
    public ActionArea? Area { get; init; }
    public string Log { get; init; } = "";
    public Effect Effect { get; init; } = new();

    /// <summary>Actions it takes this creature (see CostsHands), never more than a turn has.</summary>
    public int CostFor(CharacterSheet sheet, Ruleset rules)
    {
        if (!CostsHands)
        {
            return Cost;
        }
        return sheet.Weapon != null ? Math.Clamp(sheet.Weapon.Hands, 1, Math.Max(1, rules.ActionsPerTurn)) : 1;
    }

    /// <summary>The creature has the flags, lacks the barring ones and holds the resources it needs. why says what is missing.</summary>
    public bool Meets(CharacterSheet sheet, Ruleset rules, out string why)
    {
        why = "";
        foreach (string flag in NeedsFlags)
        {
            if (!sheet.HasFlag(rules, flag))
            {
                why = "needs " + flag;
                return false;
            }
        }
        foreach (string flag in BarredBy)
        {
            if (sheet.HasFlag(rules, flag))
            {
                why = "not while " + flag;
                return false;
            }
        }
        foreach (KeyValuePair<string, int> need in NeedsResources)
        {
            if (!sheet.Resources.TryGetValue(need.Key, out Resource? held) || held.Current < need.Value)
            {
                why = $"needs {need.Value} {need.Key}";
                return false;
            }
        }
        return true;
    }

    /// <summary>What an item's "use" or a spell fills in where a plain action file would say it.</summary>
    public record Defaults(string Id = "", string Name = "", int? Cost = null, int? Order = null, bool General = true, string[]? AlsoAllowed = null);

    public static ActionDefinition Read(ContentNode node, Defaults? defaults = null)
    {
        defaults ??= new Defaults();
        node.RequireObject("an action is a JSON object");
        node.Only(defaults.AlsoAllowed == null ? Fields : Fields.Concat(defaults.AlsoAllowed).ToArray());

        string id = node.Text("id", defaults.Id, 64);
        if (id.Length == 0)
        {
            throw node.Fail("id", "is needed, 1 to 64 characters");
        }
        string name = node.Text("name", defaults.Name.Length > 0 ? defaults.Name : id, 64);
        if (name.Length == 0)
        {
            throw node.Fail("name", "can't be empty");
        }

        int cost = defaults.Cost ?? 1;
        bool costsHands = false;
        if (node.Get("cost") is ContentNode costNode)
        {
            if (costNode.IsString && costNode.AsText() == "hands")
            {
                costsHands = true;
            }
            else if (costNode.IsWhole && costNode.AsInt() >= 0 && costNode.AsInt() <= 10)
            {
                cost = costNode.AsInt();
            }
            else
            {
                throw costNode.Fail("is a number of actions from 0 to 10, or \"hands\"");
            }
        }

        var needsFlags = new List<string>();
        var barredBy = new List<string>();
        var needsResources = new Dictionary<string, int>();
        if (node.Get("requires") is ContentNode requires)
        {
            requires.RequireObject("is an object");
            requires.Only("flags", "without", "resources");
            needsFlags = requires.Names("flags");
            barredBy = requires.Names("without");
            needsResources = ContentParts.NumbersFrom(requires, "resources", 1, 100000, idKeys: false);
        }

        ActionTarget target = ActionTarget.Self;
        ActionSide side = ActionSide.Enemy;
        int range = 1;
        bool allowsDowned = false;
        if (node.Get("target") is ContentNode targetNode)
        {
            targetNode.RequireObject("is an object with a kind");
            targetNode.Only("kind", "side", "range", "downed");
            allowsDowned = targetNode.Bool("downed", false);
            target = targetNode.Text("kind", "self") switch
            {
                "self" => ActionTarget.Self,
                "creature" => ActionTarget.Creature,
                "point" => ActionTarget.Point,
                _ => throw targetNode.Fail("kind", "is \"self\", \"creature\" or \"point\""),
            };
            side = targetNode.Text("side", "enemy") switch
            {
                "enemy" => ActionSide.Enemy,
                "ally" => ActionSide.Ally,
                "any" => ActionSide.Any,
                _ => throw targetNode.Fail("side", "is \"enemy\", \"ally\" or \"any\""),
            };
            range = targetNode.Int("range", 1, 1, 1000);
            // An area around the doer still says whose side it lands on.
            bool sided = targetNode.Has("side") || targetNode.Has("downed");
            if (target == ActionTarget.Self && (targetNode.Has("range") || (sided && !node.Has("area"))))
            {
                throw targetNode.Fail("side, range and downed are for a creature, a point or an area");
            }
        }

        ActionArea? area = null;
        if (node.Get("area") is ContentNode areaNode)
        {
            areaNode.RequireObject("is an object with a shape and a size");
            areaNode.Only("shape", "size", "width", "angle");
            AreaShape shape = areaNode.Text("shape", "") switch
            {
                "burst" => AreaShape.Burst,
                "cone" => AreaShape.Cone,
                "line" => AreaShape.Line,
                "square" => AreaShape.Square,
                _ => throw areaNode.Fail("shape", "is \"burst\", \"cone\", \"line\" or \"square\""),
            };
            if (areaNode.Has("width") && shape != AreaShape.Line)
            {
                throw areaNode.Fail("width", "is for a line");
            }
            if (areaNode.Has("angle") && shape != AreaShape.Cone)
            {
                throw areaNode.Fail("angle", "is for a cone");
            }
            area = new ActionArea(shape, areaNode.At("size").AsNumber(0.5, 100), areaNode.Number("width", 0, 0.5, 100),
                areaNode.Number("angle", 53.13, 1, 360));
            if (area.Directed && target == ActionTarget.Self)
            {
                throw areaNode.Fail("shape", "a cone or line needs a creature or point target to aim at");
            }
        }
        else if (target == ActionTarget.Point)
        {
            throw node.Fail("area", "a point target needs an area");
        }

        return new ActionDefinition
        {
            Id = id,
            Name = name,
            Description = node.Text("description", "", 2000),
            Order = node.Int("order", defaults.Order ?? 0, -100000, 100000),
            Cost = cost,
            CostsHands = costsHands,
            EndsTurn = node.Bool("endsTurn", false),
            General = defaults.General && node.Bool("general", true),
            Readies = node.Text("readies", "", 64),
            NeedsFlags = needsFlags,
            BarredBy = barredBy,
            NeedsResources = needsResources,
            Target = target,
            Side = side,
            Range = range,
            AllowsDowned = allowsDowned,
            Area = area,
            Log = node.Text("log", "", 200),
            Effect = Effect.Read(node.Get("effects"), node.Get("save")),
        };
    }

    /// <summary>The three any turn-based fight has, for a ruleset with no action files.</summary>
    public static List<ActionDefinition> Basic(Ruleset rules)
    {
        string strikeCost = rules.StrikeCostsHands ? "\"hands\"" : "1";
        string[] sources =
        {
            "{\"id\":\"strike\",\"name\":\"Strike\",\"order\":10,\"cost\":" + strikeCost
                + ",\"target\":{\"kind\":\"creature\",\"side\":\"enemy\",\"range\":1},"
                + "\"effects\":[{\"do\":\"roll\",\"kind\":\"attack\",\"steps\":[{\"do\":\"damage\",\"dice\":\"weapon\",\"when\":\"hit\",\"minimum\":1}]}]}",
            "{\"id\":\"stride\",\"name\":\"Dash\",\"order\":20,\"cost\":1,\"log\":\"{name} dashes\","
                + "\"effects\":[{\"do\":\"resource\",\"id\":\"movement\",\"op\":\"restore\",\"amount\":\"speed\",\"target\":\"self\"}]}",
            "{\"id\":\"end-turn\",\"name\":\"End turn\",\"order\":1000,\"cost\":0,\"endsTurn\":true}",
        };
        return sources.Select(source => Read(ContentNode.Parse("built-in action", source))).ToList();
    }

    /// <summary>
    /// Adds every action file in a folder to the list (same id replaces), checks each against the
    /// ruleset and sorts by order, then id.
    /// </summary>
    public static void LoadFolder(ContentFiles files, string folder, Ruleset rules, List<ActionDefinition> actions)
    {
        foreach (string path in files.List(folder))
        {
            ContentNode node = ContentNode.Read(files, path);
            ActionDefinition action = Read(node);
            if (action.Id != ContentFiles.Stem(path))
            {
                throw node.Fail("id", $"\"{action.Id}\" doesn't match the file name");
            }
            action.Effect.Check(rules, path);
            actions.RemoveAll(a => a.Id == action.Id);
            actions.Add(action);
        }
        foreach (ActionDefinition action in actions)
        {
            if (action.Readies.Length == 0)
            {
                continue;
            }
            ActionDefinition? ready = actions.Find(a => a.Id == action.Readies);
            if (ready == null || ready.Readies.Length > 0 || ready.EndsTurn)
            {
                throw new ContentException($"{folder}/{action.Id}.json", "readies",
                    "must name an action that does not ready another or end the turn");
            }
        }
        // A stable order: List.Sort isn't, and two actions can share an order.
        List<ActionDefinition> sorted = actions.OrderBy(a => a.Order).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
        actions.Clear();
        actions.AddRange(sorted);
    }
}
