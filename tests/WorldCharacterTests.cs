namespace Yorehold.Rules.Tests;

/// <summary>Heroes as characters in a World: rolled choices, levelling from XP, and library characters taking seats. The C++ client's WorldCharacterTests.</summary>
public class WorldCharacterTests
{
    // Ana the fighter and Bo the cleric with a goblin beside Ana, and an action that ends any fight.
    public static Dictionary<string, string> YardFiles(int xpPerVictory)
    {
        return new Dictionary<string, string>
        {
            ["chapters/choice-yard/chapter.json"] = """{"id":"choice-yard","title":"Yard","map":"map.json","xpPerVictory":""" + xpPerVictory + """
                ,"party":[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}],
                "encounters":[{"id":"yard","creatures":[{"creature":"goblin","name":"Gik","at":[4,3]}]}]}
                """,
            ["chapters/choice-yard/map.json"] = """
                {"name":"Yard","tiles":{"floor":{"art":"grass"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
                 "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["########","#......#","#......#","#......#",
                 "#......#","#......#","#......#","########"]}]}
                """,
            ["rulesets/yorehold/actions/finish-test.json"] = """{"id":"finish-test","cost":0,"effects":[{"do":"damage","dice":10000,"target":"enemies"}]}""",
        };
    }

    public static WorldFixture Yard(int xp = 50) => WorldFixture.LoadJson("chapters/choice-yard", YardFiles(xp), 5);

    [Fact]
    public void HeroesStartFromRolledChoices()
    {
        using WorldFixture world = Yard();
        World w = world.World;
        CharacterChoices ana = w.Creatures[0].Choices!;
        Assert.True(ana.Name == "Ana" && ana.ScoreMethod == "roll" && ana.Level == 1 && ana.Levels[0].ClassId == "fighter");
        Assert.Equal(w.Creatures[0].Sheet.AbilityScore("con"), ana.Scores["con"]);
        Assert.Null(w.Creatures[2].Choices);
        // the class's gear is on: the fighter holds the longsword and wears the rest
        CharacterSheet sheet = w.Creatures[0].Sheet;
        Assert.True(sheet.WeaponItem?.Id == "longsword" && sheet.ClassName == "Fighter" && sheet.Inventory.Any(i => i.Id == "healing-potion"));
        // the same seed rolls the same heroes
        using WorldFixture again = Yard();
        Assert.Equal(ana.ToJson().ToJsonString(), again.World.Creatures[0].Choices!.ToJson().ToJsonString());
    }

    [Fact]
    public void WinningEnoughXpLevelsAHeroUp()
    {
        using WorldFixture world = Yard(300);
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        int maxHp = ana.MaxHp;
        ana.Hp = maxHp - 2;
        ana.Stats.SetBase("dex", 2000); // acts first
        world.Fight();
        Assert.Equal(0, w.CurrentCreature);
        Assert.True(world.Use("finish-test") && world.Said("Ana reaches level 2."), "Winning with enough XP levels a hero up");
        WorldCreature grown = w.Creatures[0];
        Assert.True(grown.Choices!.Level == 2 && grown.Choices.Levels[1].ClassId == "fighter" && grown.Sheet.Level == 2);
        Assert.True(grown.Sheet.MaxHp > maxHp && grown.Sheet.Hp == maxHp - 2 + (grown.Sheet.MaxHp - maxHp), "The new level is in the hero's class, with its HP added");
        Assert.True(grown.Choices.Xp == 300 && w.LibraryCopy(0)!.Choices.Level == 2);
    }

    [Fact]
    public void LibraryCharactersTakeSeats()
    {
        using WorldFixture world = Yard();
        World w = world.World;
        string bo = Describe(w.Creatures[1].Sheet);
        string gik = Describe(w.Creatures[2].Sheet);
        CharacterChoices ada = CharacterChoices.Roll(w.Rules, "Ada", "rogue", new Rng(9));
        var rope = new Item(new ItemDefinition { Id = "rope", Name = "Rope" });
        var blade = new Item(w.Chapter.Compendium.Item("shortsword")!) { Equipped = true };
        w.SetParty(new PartyPick?[] { new PartyPick(ada, new[] { rope, blade }, "ada.json", 40), null });
        w.NewAdventure(5);
        WorldCreature seated = w.Creatures[0];
        Assert.True(seated.Sheet.Name == "Ada" && seated.Library == "ada.json" && seated.Choices!.Levels[0].ClassId == "rogue"
            && w.Tokens.Tokens[0].Name == "Ada" && seated.Sheet.Inventory.Count == 2 && seated.Sheet.WeaponItem?.Id == "shortsword"
            && seated.Sheet.Coins == 40, "A brought character takes the seat with what it carries, worn as it was");
        Assert.True(Describe(w.Creatures[1].Sheet) == bo && Describe(w.Creatures[2].Sheet) == gik, "The other seats and the dice are unchanged");
        LibraryEntry copy = w.LibraryCopy(0)!;
        Assert.True(copy.Choices.Name == "Ada" && copy.Inventory.Count == 2 && copy.Coins == 40);

        ada.Levels[0].ClassId = "bard";
        w.SetParty(new PartyPick?[] { new PartyPick(ada, Array.Empty<Item>(), "ada.json", 0) });
        w.NewAdventure(5);
        Assert.True(w.Creatures[0].Sheet.Name == "Ana" && w.Creatures[0].Library.Length == 0, "A character the chapter can't build leaves the seat to its own hero");
    }

    [Fact]
    public void LaunchClassesHaveTwentyLevels()
    {
        Chapter chapter = Chapter.Load(TestContent.Shipped(), "chapters/chapter-one");
        Ruleset rules = chapter.Rules.Rules;
        foreach (string id in new[] { "fighter", "rogue", "cleric", "wizard" })
        {
            Assert.Equal(20, chapter.Compendium.Class(id)!.Levels.Count);
            CharacterChoices choices = CharacterChoices.Roll(rules, "Test", id, new Rng(1));
            for (int level = 1; level <= 20; level++)
            {
                CharacterSheet? sheet = CharacterBuild.Build(rules, chapter.Compendium, choices, out string error);
                Assert.True(sheet != null, $"{id} level {level}: {error}");
                Assert.Equal(level, sheet!.Level);
                choices.Levels.Add(new LevelChoice(id));
            }
        }
    }

    private static string Describe(CharacterSheet sheet)
    {
        return $"{sheet.Name} {sheet.Hp}/{sheet.MaxHp} " + string.Join(",", sheet.Stats.Bases.Select(b => $"{b.Key}={b.Value}"));
    }
}
