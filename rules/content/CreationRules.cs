namespace Yorehold.Rules;

/// <summary>One step of making a character: its name on the tab and the parts picked in it.</summary>
public sealed record CreationStep(string Name, IReadOnlyList<string> Parts);

/// <summary>
/// ruleset.json's "creation": the steps of making a character, what each part is called, and the
/// ways scores can be set. Parts are the game's: name, race, background, class, scores, skills and
/// feats; a system leaves out what it hasn't (Fate has no race or background) and names the rest
/// as it likes (PF2e's race is an Ancestry).
/// </summary>
public sealed class CreationRules
{
    public static readonly string[] Parts = { "name", "fields", "race", "background", "class", "scores", "skills", "feats" };
    public static readonly string[] Methods = { "array", "pointBuy", "roll" };

    public List<CreationStep> Steps { get; init; } = new()
    {
        new("Origin", new[] { "name", "race", "background" }),
        new("Class and scores", new[] { "class", "scores" }),
        new("Skills and feats", new[] { "skills", "feats" }),
    };
    public Dictionary<string, string> Names { get; init; } = new(StringComparer.Ordinal)
    {
        ["name"] = "Name", ["race"] = "Race", ["background"] = "Background", ["class"] = "Class",
        ["scores"] = "Ability scores", ["skills"] = "Skills", ["feats"] = "Feat", ["fields"] = "Who they are",
    };
    public List<string> ScoreMethods { get; init; } = new(Methods);

    public string NameOf(string part) => Names.GetValueOrDefault(part, part);

    /// <summary>Which step a part is picked in; -1 when the system leaves it out.</summary>
    public int StepOf(string part) => Steps.FindIndex(step => step.Parts.Contains(part));

    public static CreationRules Read(ContentNode? found)
    {
        var defaults = new CreationRules();
        if (found is not ContentNode node)
        {
            return defaults;
        }
        node.RequireObject("is an object with steps, names and scoreMethods");
        node.Only("steps", "names", "scoreMethods");
        var steps = defaults.Steps;
        if (node.Get("steps") is ContentNode list)
        {
            if (!list.IsArray || list.Count < 1 || list.Count > 8)
            {
                throw list.Fail("is a list of 1 to 8 steps");
            }
            steps = new List<CreationStep>();
            var seen = new HashSet<string>();
            foreach (ContentNode entry in list.Items())
            {
                entry.RequireObject("is a step with a name and parts");
                entry.Only("name", "parts");
                List<string> parts = entry.Names("parts");
                foreach (string part in parts)
                {
                    if (!Parts.Contains(part))
                    {
                        throw entry.Fail("parts", $"unknown part \"{part}\"; parts are {string.Join(", ", Parts)}");
                    }
                    if (!seen.Add(part))
                    {
                        throw entry.Fail("parts", $"\"{part}\" is in two steps");
                    }
                }
                steps.Add(new CreationStep(entry.At("name").AsText(64), parts));
            }
            // every character has a name and a class, so the build always has something to build
            foreach (string needed in new[] { "name", "class" })
            {
                if (!seen.Contains(needed))
                {
                    throw list.Fail($"a step has to pick the \"{needed}\"");
                }
            }
        }
        var names = new Dictionary<string, string>(defaults.Names, StringComparer.Ordinal);
        foreach (KeyValuePair<string, ContentNode> member in node.Get("names")?.Members() ?? Enumerable.Empty<KeyValuePair<string, ContentNode>>())
        {
            if (!Parts.Contains(member.Key))
            {
                throw member.Value.Fail($"unknown part; parts are {string.Join(", ", Parts)}");
            }
            names[member.Key] = member.Value.AsText(64);
        }
        List<string> methods = node.Has("scoreMethods") ? node.Names("scoreMethods") : defaults.ScoreMethods;
        if (methods.Count == 0 || methods.Any(m => !Methods.Contains(m)))
        {
            throw node.Fail("scoreMethods", $"lists some of {string.Join(", ", Methods)}");
        }
        return new CreationRules { Steps = steps, Names = names, ScoreMethods = methods };
    }
}
