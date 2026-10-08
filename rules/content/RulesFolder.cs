namespace Yorehold.Rules;

/// <summary>
/// A ruleset folder, loaded: ruleset.json and the files beside it (conditions, surfaces, actions,
/// reactions, positioning, stealth, spellcasting). The game's own is rulesets/yorehold.
/// </summary>
public class RulesFolder
{
    public const string Default = "rulesets/yorehold";

    public Ruleset Rules { get; init; } = new();
    /// <summary>Empty when the ruleset is a single file with nothing beside it.</summary>
    public string Folder { get; init; } = "";
    public List<ActionDefinition> Actions { get; init; } = new();
    public List<ReactionDefinition> Reactions { get; init; } = new();
    public PositioningRules Positioning { get; init; } = new();
    public StealthRules Stealth { get; init; } = new();
    public SpellRules Spellcasting { get; init; } = new();

    public ActionDefinition? Action(string id) => Actions.Find(a => a.Id == id);

    /// <summary>
    /// Loads the ruleset a chapter plays by: a folder with ruleset.json in it, or one JSON file.
    /// Paths are looked up in the chapter folder first, then at the root.
    /// </summary>
    public static RulesFolder Load(ContentFiles files, string ruleset = Default, string chapterFolder = "")
    {
        if (ruleset is "modern" or "classic")
        {
            throw new ContentException(ruleset, "", "the framework's built-in test rulesets aren't part of this game; name a ruleset folder or file");
        }
        string file = Resolve(files, chapterFolder, ruleset + "/ruleset.json");
        string folder = "";
        if (files.Exists(file))
        {
            folder = file[..^"/ruleset.json".Length];
        }
        else
        {
            file = Resolve(files, chapterFolder, ruleset);
        }
        Ruleset rules = Ruleset.Read(ContentNode.Read(files, file));

        List<ActionDefinition> actions = ActionDefinition.Basic(rules);
        var reactions = new List<ReactionDefinition>();
        var positioning = new PositioningRules();
        var stealth = new StealthRules();
        var spellcasting = new SpellRules();
        if (folder.Length > 0)
        {
            rules.LoadConditions(files, folder + "/conditions");
            rules.LoadSurfaces(files, folder + "/surfaces");
            ActionDefinition.LoadFolder(files, folder + "/actions", rules, actions);
            reactions = ReactionDefinition.LoadFolder(files, folder + "/reactions", actions);
            if (files.Exists(folder + "/positioning.json"))
            {
                positioning = PositioningRules.Read(ContentNode.Read(files, folder + "/positioning.json"));
                positioning.Check(rules, folder + "/positioning.json");
            }
            if (files.Exists(folder + "/stealth.json"))
            {
                stealth = StealthRules.Read(ContentNode.Read(files, folder + "/stealth.json"));
            }
            if (files.Exists(folder + "/spellcasting.json"))
            {
                spellcasting = SpellRules.Read(ContentNode.Read(files, folder + "/spellcasting.json"));
                spellcasting.Check(rules, folder + "/spellcasting.json");
            }
        }
        // Content from before rulesets were folders keeps its stealth file at the root, and it still wins.
        if (files.Exists("rules/stealth.json"))
        {
            stealth = StealthRules.Read(ContentNode.Read(files, "rules/stealth.json"));
        }
        rules.CheckDeathRules(file);
        return new RulesFolder
        {
            Rules = rules,
            Folder = folder,
            Actions = actions,
            Reactions = reactions,
            Positioning = positioning,
            Stealth = stealth,
            Spellcasting = spellcasting,
        };
    }

    /// <summary>
    /// Checks the definitions against the rules: rank choices, what items and spells do, and that
    /// the ruleset can cast the spells it has.
    /// </summary>
    public void Check(Compendium compendium, string chapterFolder, ContentFiles files)
    {
        string FileOf(string kind, string id) => compendium.PathOf(kind, id);
        foreach (ClassDefinition definition in compendium.Classes.Values)
        {
            ClassDefinition.CheckRanks(Rules, definition.ProficiencyRanks, definition.DcAbility, FileOf("classes", definition.Id));
        }
        foreach (CreatureDefinition creature in compendium.Creatures.Values)
        {
            ClassDefinition.CheckRanks(Rules, creature.ProficiencyRanks, creature.DcAbility, FileOf("creatures", creature.Id));
        }
        foreach (ItemDefinition item in compendium.Items.Values)
        {
            item.Use?.Effect.Check(Rules, FileOf("items", item.Id), "use");
        }
        foreach (SpellDefinition spell in compendium.Spells.Values)
        {
            string file = $"{Folder}/spells/{spell.Id}.json";
            spell.Action.Effect.Check(Rules, file);
            if (Action(spell.Id) != null)
            {
                throw new ContentException(file, "id", "an action already has this id");
            }
        }
        if (compendium.Spells.Count > 0)
        {
            Spellcasting.Check(Rules, Folder + "/spellcasting.json");
        }
    }

    /// <summary>
    /// The id and name of the rules system a chapter plays, read without loading the rest, for
    /// lists. Empty strings when the chapter or its ruleset can't be read.
    /// </summary>
    public static (string Id, string Name) SystemOf(ContentFiles files, string chapterFolder)
    {
        try
        {
            string chapterFile = chapterFolder.Length == 0 ? "chapter.json" : chapterFolder + "/chapter.json";
            return SystemAt(files, chapterFolder, ContentNode.Read(files, chapterFile).Text("ruleset", Default));
        }
        catch (ContentException)
        {
            return ("", "");
        }
    }

    /// <summary>The same for a ruleset path (a folder or a file) as a chapter in chapterFolder would name it.</summary>
    public static (string Id, string Name) SystemAt(ContentFiles files, string chapterFolder, string ruleset)
    {
        try
        {
            string file = Resolve(files, chapterFolder, ruleset + "/ruleset.json");
            if (!files.Exists(file))
            {
                file = Resolve(files, chapterFolder, ruleset);
            }
            ContentNode rules = ContentNode.Read(files, file);
            string id = rules.Text("id", "", 64);
            return (id, rules.Text("name", id, 80));
        }
        catch (ContentException)
        {
            return ("", "");
        }
    }

    /// <summary>A path written in a chapter: in its folder if the file is there, else from the root.</summary>
    public static string Resolve(ContentFiles files, string folder, string path)
    {
        if (!ContentFiles.IsContentPath(path))
        {
            throw new ContentException(path, "", "expected a relative content path");
        }
        string local = folder.Length == 0 ? path : folder + "/" + path;
        return files.Exists(local) ? local : path;
    }
}
