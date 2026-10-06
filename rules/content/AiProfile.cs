namespace Yorehold.Rules;

/// <summary>
/// One way of thinking a creature can be given by name: ai/coward.json. An "ai" entry anywhere is
/// a profile's name or an object of the numbers to change (see assets/ai/README.md).
/// </summary>
public record AiProfile
{
    private static readonly string[] Breaks = { "flee", "alarm", "surrender", "fight" };

    /// <summary>The profile the numbers started from, for debug notes.</summary>
    public string Base { get; init; } = "cunning";

    // Choosing who to hit and where to stand.
    public double Damage { get; init; } = 1;
    public double Finish { get; init; }
    public double Weak { get; init; }
    public double Isolated { get; init; }
    public double Pack { get; init; }
    public double Nearby { get; init; }
    public double Danger { get; init; }
    public double Random { get; init; }

    // Morale: when it stops fighting and runs.
    public double FleeHp { get; init; }
    public double FleeLosses { get; init; } = 2;
    public bool FleeLeaderless { get; init; }
    public bool Leader { get; init; }
    public double EscapeAt { get; init; } = 8;
    public double AlarmReach { get; init; } = 3;

    /// <summary>What it does once its morale breaks, picked by weight: flee, alarm, surrender or fight.</summary>
    public IReadOnlyList<KeyValuePair<string, double>> OnBreak { get; init; } = new[] { new KeyValuePair<string, double>("flee", 1) };
    public bool SurrenderCornered { get; init; }

    /// <summary>Which decision model runs it. Only "utility" exists so far.</summary>
    public string Model { get; init; } = "utility";
    /// <summary>The model's own JSON object, untouched.</summary>
    public string Settings { get; init; } = "{}";

    /// <summary>The four the framework builds in. Files in ai/ with the same names replace them.</summary>
    public static AiProfile? Preset(string name)
    {
        return name switch
        {
            // Walks at the nearest thing and hits it. Never afraid.
            "mindless" => new AiProfile { Base = "mindless", Nearby = 2, Random = 0.5 },
            // Hunts the hurt and the alone, with the pack. Runs when badly hurt.
            "animal" => new AiProfile
            {
                Base = "animal", Finish = 1, Weak = 1.5, Isolated = 1, Pack = 1, Nearby = 0.5, Danger = 0.2, Random = 0.6, FleeHp = 0.35,
            },
            // Gangs up, avoids standing where it will be surrounded, and breaks when things go badly.
            "cunning" => new AiProfile
            {
                Base = "cunning", Finish = 1.5, Weak = 0.8, Isolated = 0.5, Pack = 1, Nearby = 0.3, Danger = 0.5, Random = 0.3,
                FleeHp = 0.3, FleeLosses = 0.75, FleeLeaderless = true,
            },
            // Focuses fire, finishes the wounded and rarely slips up. Holds until nearly dead.
            "tactical" => new AiProfile
            {
                Base = "tactical", Finish = 2.5, Weak = 1.2, Isolated = 0.5, Pack = 1.5, Nearby = 0.2, Danger = 0.8, Random = 0.05, FleeHp = 0.15,
            },
            _ => null,
        };
    }

    /// <summary>
    /// Reads a profile name or an object. `lookup` finds profiles by name (the presets when null);
    /// `current` is the AI being adjusted when the object names no base ("cunning" when null).
    /// </summary>
    public static AiProfile Read(ContentNode node, Func<string, AiProfile?>? lookup = null, AiProfile? current = null)
    {
        AiProfile Named(string name, ContentNode at)
        {
            return (lookup != null ? lookup(name) : Preset(name)) ?? throw at.Fail($"unknown AI \"{name}\"");
        }

        if (node.IsString)
        {
            return Named(node.AsText(), node);
        }
        if (!node.IsObject)
        {
            throw node.Fail("is a profile name or an object");
        }
        AiProfile p;
        if (node.Get("base") is not ContentNode baseNode)
        {
            p = current ?? Named("cunning", node);
        }
        else if (baseNode.AsText() == "none")
        {
            p = new AiProfile { Base = "custom" };
        }
        else
        {
            p = Named(baseNode.AsText(), baseNode) with { Base = baseNode.AsText() };
        }

        IReadOnlyList<KeyValuePair<string, double>> onBreak = p.OnBreak;
        if (node.Get("onBreak") is ContentNode on)
        {
            var list = new List<KeyValuePair<string, double>>();
            if (on.IsString)
            {
                list.Add(new KeyValuePair<string, double>(on.AsText(), 1));
            }
            else if (on.IsObject)
            {
                foreach (KeyValuePair<string, ContentNode> member in on.Members())
                {
                    list.Add(new KeyValuePair<string, double>(member.Key, member.Value.AsNumber(0, 1000)));
                }
            }
            else
            {
                throw on.Fail("is a name or an object of weights");
            }
            foreach (KeyValuePair<string, double> entry in list)
            {
                if (!Breaks.Contains(entry.Key))
                {
                    throw on.Fail($"\"{entry.Key}\" isn't flee, alarm, surrender or fight");
                }
            }
            if (list.Sum(entry => entry.Value) <= 0)
            {
                throw on.Fail("needs a weight above 0");
            }
            onBreak = list;
        }

        string model = node.Text("model", p.Model);
        if (model != "utility")
        {
            throw node.Fail("model", $"unknown AI model \"{model}\"");
        }
        string settings = p.Settings;
        if (node.Get("settings") is ContentNode settingsNode)
        {
            settings = settingsNode.RequireObject("is an object").Raw();
        }
        double Share(string key, double fallback) => node.Number(key, fallback, 0, 1000);
        string label = node.Text("label", p.Base);
        if (label.Length == 0 || label.Length > 64)
        {
            throw node.Fail("label", "is 1 to 64 characters");
        }
        return p with
        {
            Base = label,
            Damage = Share("damage", p.Damage),
            Finish = Share("finish", p.Finish),
            Weak = Share("weak", p.Weak),
            Isolated = Share("isolated", p.Isolated),
            Pack = Share("pack", p.Pack),
            Nearby = Share("nearby", p.Nearby),
            Danger = Share("danger", p.Danger),
            Random = Share("random", p.Random),
            FleeHp = Share("fleeHp", p.FleeHp),
            FleeLosses = Share("fleeLosses", p.FleeLosses),
            FleeLeaderless = node.Bool("fleeLeaderless", p.FleeLeaderless),
            Leader = node.Bool("leader", p.Leader),
            EscapeAt = node.Number("escapeAt", p.EscapeAt, 1, 1000),
            AlarmReach = Share("alarmReach", p.AlarmReach),
            OnBreak = onBreak,
            SurrenderCornered = node.Bool("surrenderCornered", p.SurrenderCornered),
            Model = model,
            Settings = settings,
        };
    }
}
