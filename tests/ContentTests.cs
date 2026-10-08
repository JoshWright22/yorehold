using System.Text.Json;

namespace Yorehold.Rules.Tests;

/// <summary>The content check: every shipped file loads into its type, and says what the game expects.</summary>
public class ContentTests
{
    [Fact]
    public void EveryShippedFileLoads()
    {
        string assets = TestContent.AssetsFolder();
        ContentFiles files = TestContent.Shipped();
        var problems = new List<string>();
        void Try(string what, Action load)
        {
            try
            {
                load();
            }
            catch (ContentException error)
            {
                problems.Add($"assets/{error.Message} (while loading {what})");
            }
        }

        Try("content.json", () => ContentPackage.Load(files).Validate(files));
        Try("adventure.json", () => Adventure.Load(files));
        foreach (string folder in Directory.GetDirectories(Path.Combine(assets, "chapters")))
        {
            string chapter = "chapters/" + Path.GetFileName(folder);
            Try(chapter, () => Chapter.Load(files, chapter));
        }
        // Conversations at the root that no chapter has to name.
        foreach (string folder in new[] { "dialogue", "dialogues" })
        {
            foreach (string path in files.List(folder))
            {
                Try(path, () => Dialogue.Read(ContentNode.Read(files, path)));
            }
        }

        Try("ui/keys.json", () => KeyBindings.Read(ContentNode.Read(files, "ui/keys.json")));
        Try("ui/credits.json", () => Credits.Read(ContentNode.Read(files, "ui/credits.json")));
        // the screens' colours, shared with the site; src/hud/Palette.cs holds them and PaletteTests checks they agree
        Try("ui/colors.json", () => ContentNode.Read(files, "ui/colors.json").RequireObject("is an object of colours"));
        Try("create/compendium.json", () =>
        {
            if (!new CompendiumEditor(new History()).SetKinds(files.ReadText("create/compendium.json"), out string error))
            {
                throw new ContentException("create/compendium.json", "", error);
            }
        });
        Try(OutlineSchemas.File, () => OutlineSchemas.Load(files));
        Try(ScreenSizes.File, () => ScreenSizes.Load(files));
        foreach (string table in files.List(SystemTable.Folder))
        {
            Try(table, () => SystemTable.Read(ContentNode.Read(files, table)));
        }
        Try("create/voice.json", () =>
        {
            if (VoiceImporter.Settings.Read(files.ReadText("create/voice.json"), out string error) == null)
            {
                throw new ContentException("create/voice.json", "", error);
            }
        });

        string[] all = Directory.GetFiles(assets, "*.json", SearchOption.AllDirectories);
        Assert.True(all.Length > 0, $"no JSON files found under {assets}");
        foreach (string file in all)
        {
            string name = Path.GetRelativePath(assets, file).Replace('\\', '/');
            if (!files.PathsRead.Contains(name))
            {
                problems.Add($"assets/{name}: nothing loads this file");
            }
        }
        Assert.True(problems.Count == 0, "Content problems:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void TheKeepIsWhatItsFilesSay()
    {
        ContentFiles files = TestContent.Shipped();
        ContentPackage package = ContentPackage.Load(files);
        Assert.Equal("chapters/goblin-keep", package.DefaultChapter);
        Chapter chapter = Chapter.Load(files, package.DefaultChapter);

        Assert.Equal(4, chapter.Party.Count);
        Assert.Equal(3, chapter.Encounters.Count);
        Assert.Equal(48, chapter.Map.Width);
        Assert.Equal(30, chapter.Map.Height);
        Assert.True(files.Exists(chapter.ClearedCutscene), "the chapter resolves its own ending file");
        Assert.Equal("The goblins are gone. The keep is yours!", chapter.ClearedText);
        Assert.Equal(5, chapter.Rules.Stealth.CheckEvery);
        Assert.Equal(180, chapter.Encounters[0].Creatures[0].Facing);
        Assert.Null(chapter.Encounters[0].Creatures[1].Facing);
        Assert.Equal("lookout", chapter.Encounters[0].Creatures[1].Ai?.AsText());
        Assert.Single(chapter.AiChanges);
        Assert.True(chapter.Compendium.Ai.ContainsKey("lookout"));

        // Everyone starts on a cell of their own that can be stood on.
        var taken = new HashSet<Cell>();
        foreach (Cell at in chapter.Party.Select(p => p.At).Concat(chapter.Encounters.SelectMany(e => e.Creatures).Select(c => c.At)))
        {
            Assert.True(chapter.Map.Walkable(at) && taken.Add(at), $"placement {at} is open and not shared");
        }
        Assert.All(chapter.Map.Lights, light => Assert.True(light.X >= 0 && light.Y >= 0 && light.X < 48 && light.Y < 30 && light.Radius > 0));

        ClassDefinition fighter = chapter.Compendium.Class("fighter")!;
        Assert.Contains("longsword", fighter.Items);
        Assert.Equal(20, fighter.Levels.Count);
        CreatureDefinition goblin = chapter.Compendium.Creature("goblin")!;
        Assert.Equal(7, goblin.Hp);
        Assert.Equal(13, goblin.ArmorClass);
        Assert.Equal("tactical", chapter.Compendium.AiFor(chapter.Compendium.Creature("goblin-boss")!).Base);
        Assert.True(chapter.Compendium.AiFor(chapter.Compendium.Creature("goblin-boss")!).Leader);
    }

    [Fact]
    public void TheRulesetHasTheGamesNumbers()
    {
        RulesFolder folder = RulesFolder.Load(TestContent.Shipped());
        Ruleset rules = folder.Rules;
        Assert.Equal("yorehold", rules.Id);
        Assert.Equal(RulesFolder.Default, folder.Folder);
        Assert.Equal(2, rules.ActionsPerTurn);
        Assert.False(rules.BonusActions);
        Assert.True(rules.StrikeCostsHands);
        Assert.Equal(3, rules.MagicItemLimit);
        Assert.Equal(10, rules.PassiveBase);
        Assert.True(rules.SharedTurns);
        Assert.Equal(5, rules.ProficiencyRanks.Count);
        Assert.Equal(new ProficiencyRank("trained", "Trained", 2, true), rules.Rank("trained"));
        Assert.True(rules.Death.Enabled);
        Assert.Equal(3, rules.Death.Successes);
        Assert.Equal(3, rules.Death.Failures);
        Assert.Equal(19, rules.XpForLevel.Count);
        Assert.Equal(2, rules.Rests.Count);
        Assert.Equal(new[] { "slots-*", "focus" }, rules.Rests[1].Restores);
        Assert.True(rules.Rests[1].CampOnly);
        Assert.Equal(40, rules.Rests[1].SupplyCost);
        Assert.Equal(0, rules.Rests[1].PerAdventure);
        Assert.Equal(2, rules.Companions.Limit);
        Assert.Equal(6, rules.Companions.PartyLimit);

        Assert.Equal(12, rules.Conditions.Count);
        Assert.NotNull(rules.Condition("off-guard"));
        Assert.Equal(4, rules.Surfaces.Count);
        Assert.Equal(12, folder.Actions.Count);
        Assert.Equal("strike", folder.Actions[0].Id);
        Assert.True(folder.Actions[0].CostsHands);
        // Lowest order first: the readied action (5) is offered before the opportunity strike (10).
        Assert.Equal(new[] { "readied", "opportunity" }, folder.Reactions.Select(r => r.Id));
        Assert.True(folder.Positioning.Enabled);
        Assert.Equal("off-guard", folder.Positioning.FlankingCondition);
        Assert.Equal(new[] { "long" }, folder.Spellcasting.PrepareAfter);
    }

    [Fact]
    public void TheRulesetShipsItsPlayerOptions()
    {
        ContentFiles files = TestContent.Shipped();
        Chapter chapter = Chapter.Load(files, "chapters/goblin-keep");
        Compendium options = chapter.Compendium;
        Assert.Equal(new[] { "dwarf", "elf", "halfling", "human" }, options.Races.Keys);
        Assert.Equal(6, options.Backgrounds.Count);
        Assert.Equal(new[] { "class", "general", "race", "skill" }, options.Feats.Values.Select(f => f.Kind).Distinct().Order());
        Assert.Equal(20, options.Spells.Count);
        Assert.Equal(0, options.Spells["spark"].Level);
        Assert.Equal(2, options.Spells["flame-fan"].Hands);
        Assert.Equal(2, options.Spells["flame-fan"].Action.Cost);
        Assert.Equal(AreaShape.Cone, options.Spells["flame-fan"].Action.Area?.Shape);
        Assert.Equal(1, options.Spells["arcane-dart"].Spends["focus"]);
        Assert.True(options.Spells["mire"].Concentration);

        // Every class with a table has all twenty rows, and casters carry slots to level 9.
        foreach (string id in new[] { "fighter", "rogue", "cleric", "wizard" })
        {
            Assert.Equal(20, options.Class(id)!.Levels.Count);
        }
        Assert.Empty(options.Class("barbarian")!.Levels);
        Assert.Equal("prepared", options.Class("wizard")!.Casting);
        Assert.Equal(9, options.Class("wizard")!.Levels[19].Slots.Keys.Max());
    }

    [Fact]
    public void TheTestAdventureTiesItsChaptersTogether()
    {
        Adventure adventure = Adventure.Load(TestContent.Shipped());
        Assert.Equal("test-adventure", adventure.Id);
        Assert.Equal(1, adventure.MinLevel);
        Assert.Equal(5, adventure.MaxLevel);
        Assert.Equal(3, adventure.RecommendedPartySize);
        Assert.Equal(new[] { "chapters/chapter-one", "chapters/chapter-two" }, adventure.ChapterFolders);
        Assert.Equal(new[] { "chapter-one", "chapter-two" }, adventure.ChapterIds);
        Assert.Equal("chapters/chapter-two", adventure.FolderOf("chapter-two"));
        Assert.Equal(3, adventure.Transitions.Count);
        // A transition with flags only opens once they are set.
        Assert.Null(adventure.NextChapter("chapter-two", "end", Array.Empty<string>()));
        Assert.Equal("chapter-one", adventure.NextChapter("chapter-two", "end", new[] { "chapter_two_complete" }));
        Assert.Equal("chapter-one", adventure.NextChapter("chapter-two", "back", Array.Empty<string>()));
        Assert.Equal(new[] { "chapters/chapter-one", "chapters/chapter-two" }, Adventure.ListedChapters(TestContent.Shipped()));
    }

    [Fact]
    public void EveryJsonFileIsStrictJson()
    {
        // Comments and trailing commas would load in some tools and not others, so none are allowed.
        string assets = TestContent.AssetsFolder();
        var problems = new List<string>();
        foreach (string file in Directory.GetFiles(assets, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
            }
            catch (JsonException error)
            {
                problems.Add($"assets/{Path.GetRelativePath(assets, file).Replace('\\', '/')} line {error.LineNumber + 1}: {error.Message}");
            }
        }
        Assert.True(problems.Count == 0, "Content files that do not parse:\n" + string.Join("\n", problems));
    }
}
