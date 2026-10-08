namespace Yorehold.Rules;

public enum ReactionTrigger
{
    LeavesReach,
    EntersReach,
    /// <summary>An attack on the reactor hit it.</summary>
    Hit,
    /// <summary>An attack on the reactor missed it.</summary>
    Missed,
    /// <summary>An attack hit one of the reactor's allies.</summary>
    AllyHit,
    /// <summary>An attack on the reactor would hit: taken before it lands, the roll is read again (Shield).</summary>
    BeforeHit,
    /// <summary>A foe in reach starts casting a spell: taken before it does anything (a disrupting strike, a counterspell).</summary>
    SpellCast,
}

/// <summary>A movement trigger and the action it offers: rulesets/yorehold/reactions/opportunity.json.</summary>
public class ReactionDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public ReactionTrigger Trigger { get; init; }
    /// <summary>The action it runs. Empty when Readied: then it runs what the creature recorded.</summary>
    public string Action { get; init; } = "";
    public bool Readied { get; init; }
    /// <summary>True: anyone may take it. False: only those a class, feat or creature file grants it.</summary>
    public bool General { get; init; } = true;
    public int Order { get; init; }
    public double PromptSeconds { get; init; } = 2;

    /// <summary>A step from before to after squares away crosses the edge of reach the way the trigger needs.</summary>
    public bool Matches(float before, float after, int reach)
    {
        float limit = reach + 0.01f;
        return Trigger == ReactionTrigger.LeavesReach ? before <= limit && after > limit : before > limit && after <= limit;
    }

    public static ReactionDefinition Read(ContentNode node)
    {
        node.RequireObject("a reaction is a JSON object");
        node.Only("id", "name", "trigger", "action", "readied", "order", "promptSeconds", "general");
        string id = node.At("id").AsName();
        ReactionTrigger trigger = node.Name("trigger", "") switch
        {
            "leavesReach" => ReactionTrigger.LeavesReach,
            "entersReach" => ReactionTrigger.EntersReach,
            "hit" => ReactionTrigger.Hit,
            "missed" => ReactionTrigger.Missed,
            "allyHit" => ReactionTrigger.AllyHit,
            "beforeHit" => ReactionTrigger.BeforeHit,
            "spellCast" => ReactionTrigger.SpellCast,
            _ => throw node.Fail("trigger", "is leavesReach, entersReach, hit, missed, allyHit, beforeHit or spellCast"),
        };
        bool readied = node.Bool("readied", false);
        string action = node.Name("action", "");
        if (readied ? action.Length > 0 : action.Length == 0)
        {
            throw node.Fail("action", "name an action, or set readied, but not both");
        }
        return new ReactionDefinition
        {
            Id = id,
            Name = node.Name("name", id),
            Trigger = trigger,
            Action = action,
            Readied = readied,
            General = node.Bool("general", true),
            Order = node.Int("order", 0, -100000, 100000),
            PromptSeconds = node.Number("promptSeconds", 2, 0.1, 30),
        };
    }

    /// <summary>Reads a folder of reactions, checked against the actions they name, sorted by order then id.</summary>
    public static List<ReactionDefinition> LoadFolder(ContentFiles files, string folder, List<ActionDefinition> actions)
    {
        var reactions = new List<ReactionDefinition>();
        foreach (string path in files.List(folder))
        {
            ContentNode node = ContentNode.Read(files, path);
            ReactionDefinition reaction = Read(node);
            if (reaction.Id != ContentFiles.Stem(path))
            {
                throw node.Fail("id", "must match the file name");
            }
            if (!reaction.Readied)
            {
                ActionDefinition? action = actions.Find(a => a.Id == reaction.Action);
                if (action == null || action.EndsTurn || action.Readies.Length > 0)
                {
                    throw node.Fail("action", "must name an action that neither readies another nor ends the turn");
                }
            }
            reactions.Add(reaction);
        }
        return reactions.OrderBy(r => r.Order).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
    }
}
