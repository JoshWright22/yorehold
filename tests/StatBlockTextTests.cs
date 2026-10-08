namespace Yorehold.Rules.Tests;

public class StatBlockTextTests
{
    // invented creatures in the two layouts books print, so no book's words are in the tests
    private const string Old = """
        Marsh Lurker
        Medium Monstrosity, Unaligned
        Armor Class 13 (natural armor)
        Hit Points 22 (4d8 + 4)
        Speed 30 ft., swim 30 ft.
        STR DEX CON INT WIS CHA
        14 (+2) 12 (+1) 13 (+1) 4 (-3) 11 (+0) 6 (-2)
        Damage Resistances cold
        Damage Immunities poison
        Senses darkvision 60 ft., passive Perception 10
        Challenge 1 (200 XP)
        Actions
        Multiattack. The lurker makes two attacks: one with its bite and one with its claws.
        Bite. Melee Weapon Attack: +4 to hit, reach 5 ft., one target. Hit: 6 (1d8 + 2) piercing damage.
        Claws. Melee Weapon Attack: +4 to hit, reach 5 ft., one target. Hit: 5 (1d6 + 2) slashing damage.
        """;

    private const string New = """
        Cinder Hound
        Small Fiend, Neutral Evil
        AC 12 Initiative +2 (12)
        HP l6 (3d6 + 6)
        Speed 40 ft.
        Str 8 -1 -1 Dex 14 +2 +2 Con 15 +2 +2
        Int 6 -2 -2 Wis 12 +1 +1 Cha 7 -2 -2
        Vulnerabilities Cold
        Immunities Fire, Poison; Poisoned
        Senses Darkvision 60 ft.; Passive Perception 11
        CR 1/2 (XP 100; PB +2)
        Actions
        Scorching Bite. Melee Attack Roll: +4, reach 5 ft. Hit: 7 (2d4 + 2) Fire damage.
        """;

    [Fact]
    public void TheOlderLayoutReads()
    {
        StatBlockText.Creature lurker = StatBlockText.Read(Old)!;
        Assert.Equal("Marsh Lurker", lurker.Name);
        Assert.Equal((13, 22, 30, 60, 1), ((int)lurker.Data["armorClass"]!, (int)lurker.Data["hp"]!, (int)lurker.Data["speed"]!,
            (int)lurker.Data["darkvision"]!, (int)lurker.Data["level"]!));
        Assert.Equal(14, (int)lurker.Data["abilities"]!["str"]!);
        Assert.Equal(6, (int)lurker.Data["abilities"]!["cha"]!);
        Assert.Equal(1, (int)lurker.Data["stats"]!["resist.cold"]!);
        Assert.Equal(1, (int)lurker.Data["stats"]!["immune.poison"]!);
        Assert.Equal(2, (int)lurker.Data["multiattack"]!);
        Assert.Equal(new[] { ("Bite", 4, "1d8", "piercing"), ("Claws", 4, "1d6", "slashing") }, lurker.Attacks.Select(a => (a.Name, a.Bonus, a.Dice, a.Type)));
        Assert.Contains("Multiattack", (string)lurker.Data["text"]!);
    }

    [Fact]
    public void TheNewerLayoutReadsThroughOcrSlips()
    {
        StatBlockText.Creature hound = StatBlockText.Read(New)!;
        Assert.Equal((12, 16, 40, 1), ((int)hound.Data["armorClass"]!, (int)hound.Data["hp"]!, (int)hound.Data["speed"]!, (int)hound.Data["level"]!));
        Assert.Equal(15, (int)hound.Data["abilities"]!["con"]!);
        Assert.Equal(1, (int)hound.Data["stats"]!["weak.cold"]!);
        Assert.True(hound.Data["stats"]!["immune.fire"] != null && hound.Data["stats"]!["immune.poison"] != null);
        Assert.Equal(("Scorching Bite", "2d4", "fire"), (hound.Attacks[0].Name, hound.Attacks[0].Dice, hound.Attacks[0].Type));
        Assert.Equal(0.36, (double)hound.Data["token"]!["size"]!);
    }

    [Fact]
    public void BlocksAreFoundOnAPage()
    {
        string page = "The bog is quiet.\n" + Old + "\nSome other words.\n" + New;
        List<string> blocks = StatBlockText.Find(page);
        Assert.Equal(2, blocks.Count);
        Assert.StartsWith("Marsh Lurker", blocks[0]);
        Assert.StartsWith("Cinder Hound", blocks[1]);
        Assert.Null(StatBlockText.Read("Just a heading\nMedium room\nNothing else"));
    }

    [Fact]
    public void ABooksStatBlockBecomesTheSystemsCreature()
    {
        // one page with the lurker's block as its text lines, read without a model, built for 5e
        var lines = new System.Text.Json.Nodes.JsonArray();
        int y = 40;
        foreach (string line in Old.Split('\n'))
        {
            lines.Add(new System.Text.Json.Nodes.JsonObject { ["kind"] = "text", ["text"] = line.Trim(), ["at"] = new System.Text.Json.Nodes.JsonArray(50, y, 400, 12) });
            y += 14;
        }
        var source = new System.Text.Json.Nodes.JsonObject
        {
            ["format"] = "yorehold.source", ["version"] = 1, ["title"] = "Bog", ["file"] = "bog.pdf", ["bodySize"] = 10,
            ["pages"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["number"] = 1, ["width"] = 600, ["height"] = 800, ["blocks"] = lines }),
        };
        SourceBook book = SourceBook.Parse("source.json", source.ToJsonString());
        var draft = new Outline { Title = "Bog" };
        BookCast.Add(book, draft, new[] { "fighter" });
        OutlineEntry lurker = draft.OfKind(OutlineKind.Creature).Single(c => c.Text("name") == "Marsh Lurker");
        Assert.Equal(2, draft.OfKind(OutlineKind.Item).Count(i => i.Id.StartsWith(lurker.Id + "-", StringComparison.Ordinal)));
        Assert.Equal(22, (int)lurker.Data["hp"]!);

        // built for 5e: it takes the system's Multiattack, its resistances are stats, and it loads
        using var scratch = new Scratch();
        string package = Path.Combine(scratch.Folder, "bog");
        List<string> problems = new OutlineBuilder(draft, Path.Combine(package, "import"), TestContent.Shipped(), "rulesets/dnd5e").Build(package);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        ContentFiles files = TestContent.Shipped();
        files.Add(package);
        var compendium = new Compendium();
        compendium.Load(files, "rulesets/dnd5e", "");
        CreatureDefinition made = compendium.Creatures[lurker.Id];
        Assert.Contains("multiattack", made.Actions);
        Assert.Equal(1, made.Stats["resist.cold"]);
        Assert.Equal(2, made.Items.Count);
    }
}
