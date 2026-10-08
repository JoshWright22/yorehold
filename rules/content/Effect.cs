namespace Yorehold.Rules;

public enum EffectKind
{
    Damage,
    Heal,
    TempHp,
    Condition,
    Modifier,
    Move,
    Resource,
    Summon,
    Light,
    Surface,
    Flag,
    Roll,
    Repeat,
    Choose,
}

public enum OnSave
{
    Full,
    Half,
    None,
}

public enum ScaleBy
{
    None,
    Level,
    Slot,
}

/// <summary>Grows a step once for each Every levels above From.</summary>
public record EffectScale(ScaleBy By = ScaleBy.None, int From = 1, int Every = 1, string Dice = "", int Value = 0);

/// <summary>One option of a "choose" step.</summary>
public record EffectOption(string Name, List<EffectStep> Steps);

/// <summary>
/// One step of an effect. Which fields mean something depends on Kind; the names follow the
/// file format ("dice", "id", "how"...) so a step reads like the JSON it came from.
/// </summary>
public class EffectStep
{
    /// <summary>A condition step with this duration lasts as long as the condition's own file says.</summary>
    public const int DefinedDuration = -2;

    public EffectKind Kind { get; init; }
    /// <summary>"self", "target", "area", "allies" or "enemies".</summary>
    public string Target { get; init; } = "target";
    /// <summary>Empty for always; else a roll result or an event.</summary>
    public string When { get; init; } = "";
    public string IfFlag { get; init; } = "";
    public OnSave OnSave { get; init; }
    public EffectScale Scale { get; init; } = new();

    /// <summary>Dice, a whole number as text, or a word like "weapon" or "speed".</summary>
    public string Amount { get; init; } = "";
    public string Type { get; init; } = "";
    public int Minimum { get; init; }
    public bool CritDoubles { get; init; } = true;
    public string Id { get; init; } = "";
    public bool Remove { get; init; }
    public int Duration { get; init; } = -1;
    public int Value { get; init; } = 1;
    public Modifier? Modifier { get; init; }
    /// <summary>A move's "push"/"pull"/"teleport", a resource's "spend"/"restore", a roll's "attack"/"check"/"save".</summary>
    public string How { get; init; } = "";
    public double Size { get; init; }
    public string Ability { get; init; } = "";
    public int Dc { get; init; } = 10;
    public bool CasterDc { get; init; }
    public string Against { get; init; } = "";
    public List<EffectStep> Steps { get; init; } = new();
    public List<EffectOption> Options { get; init; } = new();
}

/// <summary>The save beside an effect's steps: each creature a step asks it of rolls it once.</summary>
public record EffectSave(string Ability = "", int Dc = 10, bool CasterDc = false);

/// <summary>
/// What a spell, an action, an item or a trap does: a list of steps, written as the list itself
/// or as an object with "effects" and an optional "save".
/// </summary>
public class Effect
{
    /// <summary>What a step's "when" can name besides an event.</summary>
    public static readonly string[] Results = { "hit", "miss", "crit", "saveFailed", "saveSucceeded", "success", "failure" };

    private const int Deepest = 6; // steps under steps under steps...
    private static readonly string[] Common = { "do", "target", "when", "ifFlag", "scale", "onSave" };

    public EffectSave Save { get; init; } = new();
    public List<EffectStep> Steps { get; init; } = new();

    public bool IsEmpty => Steps.Count == 0;

    /// <summary>Runs the steps in order and says what each did.</summary>
    public EffectResult Run(EffectHost host, EffectContext context)
    {
        return new EffectRun(this, host, context).Run();
    }

    /// <summary>Reads a list of steps, or an object holding "effects" and "save".</summary>
    public static Effect Read(ContentNode node)
    {
        if (node.IsArray)
        {
            return new Effect { Steps = ReadSteps(node, 1) };
        }
        if (!node.IsObject)
        {
            throw node.Fail("is a list of steps");
        }
        node.Only("effects", "save");
        return Read(node.Get("effects"), node.Get("save"));
    }

    /// <summary>Reads the "effects" and "save" fields of something bigger, like an action.</summary>
    public static Effect Read(ContentNode? effects, ContentNode? save)
    {
        var read = new EffectSave();
        if (save is ContentNode saveNode)
        {
            saveNode.RequireObject("is an object with an ability and a dc");
            saveNode.Only("ability", "dc");
            string ability = saveNode.At("ability").AsName();
            bool caster = IsCaster(saveNode.Get("dc"));
            read = new EffectSave(ability, caster ? 10 : saveNode.Int("dc", 10, -1000, 1000), caster);
        }
        return new Effect
        {
            Save = read,
            Steps = effects is ContentNode list ? ReadSteps(list, 1) : new List<EffectStep>(),
        };
    }

    /// <summary>What only a ruleset can tell: the conditions, abilities and skills named exist.</summary>
    public void Check(Ruleset rules, string file, string path = "")
    {
        string prefix = path.Length == 0 ? "" : path + ".";
        if (Save.Ability.Length > 0 && !rules.IsSave(Save.Ability))
        {
            throw new ContentException(file, prefix + "save.ability", $"unknown ability \"{Save.Ability}\"");
        }
        CheckSteps(Steps, prefix + "effects", rules, file, Save.Ability.Length > 0);
    }

    private static bool IsCaster(ContentNode? dc)
    {
        return dc is ContentNode node && node.IsString && node.AsText() == "caster";
    }

    private static List<EffectStep> ReadSteps(ContentNode list, int depth)
    {
        if (!list.IsArray || list.Count > 64)
        {
            throw list.Fail("is a list of up to 64 steps");
        }
        if (depth > Deepest)
        {
            throw list.Fail("steps are nested too deep");
        }
        return list.Items().Select(item => ReadStep(item, depth)).ToList();
    }

    private static string Amount(ContentNode node, string key, string[] words, string? fallback = null)
    {
        ContentNode? found = node.Get(key);
        if (found == null)
        {
            return fallback ?? throw node.Fail(key, "is needed");
        }
        ContentNode value = found.Value;
        if (value.IsWhole && value.AsInt() >= 0 && value.AsInt() <= 100000)
        {
            return value.AsInt().ToString();
        }
        if (value.IsString && (words.Contains(value.AsText()) || DiceText.IsValid(value.AsText())))
        {
            return value.AsText();
        }
        string allowed = "is a whole number or dice like \"2d6+1\"";
        foreach (string word in words)
        {
            allowed += $", or \"{word}\"";
        }
        throw value.Fail(allowed);
    }

    // Rounds: left out is the fallback, -1 is "until something ends it".
    private static int Rounds(ContentNode node, int fallback)
    {
        int value = node.Int("duration", fallback, -1, 100000);
        if (node.Has("duration") && value == 0)
        {
            throw node.Fail("duration", "is a number of rounds, or -1 for until something ends it");
        }
        return value;
    }

    private static string OneOf(ContentNode node, string key, string? fallback, params string[] allowed)
    {
        string value = node.Get(key)?.AsName() ?? fallback ?? throw node.Fail(key, "is needed");
        if (!allowed.Contains(value))
        {
            throw node.Fail(key, "is " + string.Join(", ", allowed.Select(a => $"\"{a}\"")));
        }
        return value;
    }

    private static EffectStep ReadStep(ContentNode node, int depth)
    {
        node.RequireObject("a step is an object with a \"do\"");
        string kindName = node.At("do").AsName();
        if (!Enum.TryParse(kindName, ignoreCase: true, out EffectKind kind) || !string.Equals(KindName(kind), kindName, StringComparison.Ordinal))
        {
            throw node.Fail("do", $"unknown step \"{kindName}\"");
        }

        string amount = "";
        string type = "";
        int minimum = 0;
        bool critDoubles = true;
        string id = "";
        bool remove = false;
        int duration = -1;
        int value = 1;
        Modifier? modifier = null;
        string how = "";
        double size = 0;
        string ability = "";
        int dc = 10;
        bool casterDc = false;
        string against = "";
        var options = new List<EffectOption>();
        string[] own;

        switch (kind)
        {
        case EffectKind.Damage:
            own = new[] { "dice", "type", "crit", "minimum" };
            amount = Amount(node, "dice", new[] { "weapon" });
            type = node.Name("type", "untyped");
            minimum = node.Int("minimum", 0, 0, 100000);
            critDoubles = OneOf(node, "crit", "double", "double", "normal") == "double";
            break;
        case EffectKind.Heal:
        case EffectKind.TempHp:
            own = new[] { "dice" };
            amount = Amount(node, "dice", Array.Empty<string>());
            break;
        case EffectKind.Condition:
            own = new[] { "id", "remove", "duration", "value" };
            id = node.At("id").AsName();
            remove = node.Bool("remove", false);
            duration = Rounds(node, EffectStep.DefinedDuration);
            value = node.Int("value", 1, 1, 1000);
            break;
        case EffectKind.Modifier:
            own = new[] { "id", "stat", "op", "value", "duration", "type" };
            id = node.Name("id", "");
            modifier = new Modifier(node.At("stat").AsName(), ContentParts.OpFrom(node, "op"), node.At("value").AsNumber(-100000, 100000), node.Text("type", "", 64));
            duration = Rounds(node, -1);
            break;
        case EffectKind.Move:
            own = new[] { "how", "distance" };
            how = OneOf(node, "how", null, "push", "pull", "teleport");
            amount = Amount(node, "distance", new[] { "speed" }, "1");
            break;
        case EffectKind.Resource:
            own = new[] { "id", "op", "amount" };
            id = node.At("id").AsName();
            how = OneOf(node, "op", "spend", "spend", "restore");
            amount = Amount(node, "amount", new[] { "speed" }, "1");
            break;
        case EffectKind.Summon:
            own = new[] { "id", "count", "duration" };
            id = node.At("id").AsName();
            amount = Amount(node, "count", Array.Empty<string>(), "1");
            duration = Rounds(node, -1);
            break;
        case EffectKind.Light:
            own = new[] { "radius", "duration" };
            size = node.At("radius").AsNumber(0, 1000);
            duration = Rounds(node, -1);
            break;
        case EffectKind.Surface:
            own = new[] { "id", "size", "duration" };
            id = node.At("id").AsName();
            size = node.Number("size", 1, 0, 1000);
            duration = Rounds(node, -1);
            break;
        case EffectKind.Flag:
            own = new[] { "id", "remove" };
            id = node.At("id").AsName();
            remove = node.Bool("remove", false);
            break;
        case EffectKind.Roll:
            own = new[] { "kind", "ability", "dc", "against", "steps" };
            how = OneOf(node, "kind", null, "attack", "check", "save");
            if (how == "attack")
            {
                // with the weapon, with "ability": "caster" as a spell attack, or with a named ability
                if (node.Has("dc") || node.Has("against"))
                {
                    throw node.Fail("an attack is rolled against armour class; it takes no dc or against");
                }
                ability = node.Name("ability", "");
            }
            else
            {
                ability = node.At("ability").AsName();
                casterDc = IsCaster(node.Get("dc"));
                dc = casterDc ? 10 : node.Int("dc", 10, -1000, 1000);
                against = node.Name("against", "");
                if (against.Length > 0 && (how == "save" || node.Has("dc")))
                {
                    throw node.Fail("against", "is for a check, in place of dc");
                }
            }
            if (!node.Has("steps"))
            {
                throw node.Fail("steps", "is needed");
            }
            break;
        case EffectKind.Repeat:
            own = new[] { "times", "steps" };
            amount = Amount(node, "times", Array.Empty<string>());
            if (!node.Has("steps"))
            {
                throw node.Fail("steps", "is needed");
            }
            break;
        default:
            own = new[] { "options" };
            ContentNode list = node.At("options");
            if (!list.IsArray || list.Count == 0 || list.Count > 32)
            {
                throw list.Fail("is a list of 1 to 32 options");
            }
            foreach (ContentNode option in list.Items())
            {
                option.RequireObject("an option is an object with a name and steps");
                option.Only("name", "steps");
                options.Add(new EffectOption(option.At("name").AsName(), ReadSteps(option.At("steps"), depth + 1)));
            }
            break;
        }

        foreach (KeyValuePair<string, ContentNode> member in node.Members())
        {
            if (!Common.Contains(member.Key) && !own.Contains(member.Key))
            {
                throw member.Value.Fail($"unknown field for a \"{kindName}\" step");
            }
        }

        // A check against nobody in particular is the doer's own; everything else is aimed.
        bool ownCheck = kind == EffectKind.Roll && how == "check" && against.Length == 0;
        string target = OneOf(node, "target", ownCheck ? "self" : "target", "self", "target", "area", "allies", "enemies");
        // Read as a name here; whether it is an event or one of the system's outcomes is checked
        // against the ruleset (Check), since a system names its own outcomes.
        string when = node.Name("when", "");
        OnSave onSave = OneOf(node, "onSave", "full", "full", "half", "none") switch
        {
            "half" => OnSave.Half,
            "none" => OnSave.None,
            _ => OnSave.Full,
        };
        if (onSave == OnSave.Half && kind != EffectKind.Damage)
        {
            throw node.Fail("onSave", "only damage can be halved");
        }

        var scale = new EffectScale();
        if (node.Get("scale") is ContentNode scaleNode)
        {
            scaleNode.RequireObject("is an object with \"by\"");
            scaleNode.Only("by", "from", "every", "dice", "value");
            ScaleBy by = OneOf(scaleNode, "by", null, "level", "slot") == "level" ? ScaleBy.Level : ScaleBy.Slot;
            scale = new EffectScale(by, scaleNode.Int("from", 1, 0, 1000), scaleNode.Int("every", 1, 1, 1000),
                Amount(scaleNode, "dice", Array.Empty<string>(), ""), scaleNode.Int("value", 0, -100000, 100000));
            bool rolled = kind is EffectKind.Damage or EffectKind.Heal or EffectKind.TempHp or EffectKind.Repeat
                or EffectKind.Summon or EffectKind.Resource or EffectKind.Move;
            if (scale.Dice.Length > 0 && !rolled)
            {
                throw scaleNode.Fail("dice", "this step has no amount to add dice to");
            }
            if (!rolled && kind != EffectKind.Condition)
            {
                throw scaleNode.Fail("this step has nothing to scale");
            }
            if (scale.Dice.Length == 0 && scale.Value == 0)
            {
                throw scaleNode.Fail("needs \"dice\" or \"value\"");
            }
        }

        return new EffectStep
        {
            Kind = kind,
            Target = target,
            When = when,
            IfFlag = node.Name("ifFlag", ""),
            OnSave = onSave,
            Scale = scale,
            Amount = amount,
            Type = type,
            Minimum = minimum,
            CritDoubles = critDoubles,
            Id = id,
            Remove = remove,
            Duration = duration,
            Value = value,
            Modifier = modifier,
            How = how,
            Size = size,
            Ability = ability,
            Dc = dc,
            CasterDc = casterDc,
            Against = against,
            Steps = node.Get("steps") is ContentNode steps ? ReadSteps(steps, depth + 1) : new List<EffectStep>(),
            Options = options,
        };
    }

    // The file writes kinds in camelCase ("tempHp"); the enum is the same word with a capital.
    private static string KindName(EffectKind kind)
    {
        string name = kind.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static void CheckSteps(List<EffectStep> steps, string path, Ruleset rules, string file, bool hasSave)
    {
        bool Measurable(string name) => rules.Ability(name) != null || rules.Skill(name) != null || rules.SaveOf(name) != null;
        for (int i = 0; i < steps.Count; i++)
        {
            EffectStep step = steps[i];
            string at = $"{path}[{i}]";
            if (step.Kind == EffectKind.Condition && rules.Condition(step.Id) == null)
            {
                throw new ContentException(file, at + ".id", $"unknown condition \"{step.Id}\"");
            }
            if (step.Kind == EffectKind.Roll && step.How == "save" && !rules.IsSave(step.Ability))
            {
                throw new ContentException(file, at + ".ability", $"unknown ability \"{step.Ability}\"");
            }
            if (step.Kind == EffectKind.Roll && step.How == "attack" && step.Ability.Length > 0 && step.Ability != "caster" && rules.Ability(step.Ability) == null)
            {
                throw new ContentException(file, at + ".ability", $"an attack names \"caster\" or an ability; unknown \"{step.Ability}\"");
            }
            if (step.Kind == EffectKind.Roll && step.How == "check" && !Measurable(step.Ability))
            {
                throw new ContentException(file, at + ".ability", $"unknown ability or skill \"{step.Ability}\"");
            }
            if (step.Against.Length > 0 && !Measurable(step.Against))
            {
                throw new ContentException(file, at + ".against", $"unknown ability or skill \"{step.Against}\"");
            }
            if (step.When.Length > 0 && !Results.Contains(step.When) && !ConditionDefinition.Events.Contains(step.When)
                && !rules.Checks.HasOutcome(step.When))
            {
                throw new ContentException(file, at + ".when", $"unknown \"{step.When}\": not an event or one of this system's outcomes");
            }
            if (step.OnSave != OnSave.Full && !hasSave)
            {
                throw new ContentException(file, at + ".onSave",
                    "there is no save to make: give the effect a \"save\" or put the step under a save roll");
            }
            bool underSave = hasSave || (step.Kind == EffectKind.Roll && step.How == "save");
            CheckSteps(step.Steps, at + ".steps", rules, file, underSave);
            for (int option = 0; option < step.Options.Count; option++)
            {
                CheckSteps(step.Options[option].Steps, $"{at}.options[{option}].steps", rules, file, hasSave);
            }
        }
    }
}
