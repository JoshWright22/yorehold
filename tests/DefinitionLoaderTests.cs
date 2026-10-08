namespace Yorehold.Rules.Tests;

/// <summary>Items, classes, creatures, player options and AI profiles, and the folders they load from.</summary>
public class DefinitionLoaderTests
{
    [Fact]
    public void ItemsReadTheirFieldsAndDefaults()
    {
        ItemDefinition shield = ItemDefinition.Read(TestContent.Json("""
            {"id": "shield", "name": "Shield", "slot": "offHand", "weight": 6, "value": 1000, "magic": true,
             "modifiers": [{"stat": "ac", "value": 2}]}
            """));
        Assert.Equal(("offHand", 1, 6.0, 1000, true), (shield.Slot, shield.Hands, shield.Weight, shield.Value, shield.Magic));
        Assert.Equal(new Modifier("ac", ModifierOp.Add, 2), shield.Modifiers[0]);

        ItemDefinition bare = ItemDefinition.Read(TestContent.Json("{\"id\": \"rock\"}"));
        Assert.Equal(("rock", "", "", "", 1, 0.0, 0, 1, false, 0), (bare.Name, bare.Slot, bare.Damage, bare.AttackAbility, bare.Hands, bare.Weight, bare.Value, bare.Quantity, bare.Magic, bare.Supplies));
        Assert.Null(bare.Use);

        // A consumable's use is an action: its id, name and cost come from the item unless it says.
        ItemDefinition potion = ItemDefinition.Read(TestContent.Json("""
            {"id": "healing-potion", "name": "Healing potion",
             "use": {"target": {"kind": "creature", "side": "ally", "range": 1, "downed": true}, "effects": [{"do": "heal", "dice": "2d4+2"}]}}
            """));
        Assert.NotNull(potion.Use);
        Assert.Equal(("healing-potion", "Healing potion", 1, false, true), (potion.Use.Id, potion.Use.Name, potion.Use.Cost, potion.Use.General, potion.Use.AllowsDowned));
    }

    [Theory]
    [InlineData("{\"id\": \"Big Sword\"}", "id")]
    [InlineData("{\"id\": \"x\", \"hands\": 5}", "hands")]
    [InlineData("{\"id\": \"x\", \"damage\": \"sharp\"}", "damage")]
    [InlineData("{\"id\": \"x\", \"weight\": -1}", "weight")]
    [InlineData("{\"id\": \"x\", \"supplies\": 20000}", "supplies")]
    [InlineData("{\"id\": \"x\", \"slot\": \"mainHand\", \"use\": {\"effects\": [{\"do\": \"heal\", \"dice\": 1}]}}", "use")]
    [InlineData("{\"id\": \"x\", \"use\": {\"effects\": []}}", "use")]
    [InlineData("{\"id\": \"x\", \"use\": {\"effects\": [{\"do\": \"heal\"}]}}", "use.effects[0].dice")]
    [InlineData("{\"id\": \"x\", \"modifiers\": [{\"stat\": \"ac\", \"value\": \"two\"}]}", "modifiers[0].value")]
    public void ABrokenItemNamesTheField(string text, string field)
    {
        ContentException error = TestContent.Refused(() => ItemDefinition.Read(TestContent.Json(text, "items/x.json")));
        Assert.Equal(("items/x.json", field), (error.File, error.Field));
    }

    [Fact]
    public void ClassesReadTheirTablesAndDefaults()
    {
        ClassDefinition wizard = ClassDefinition.Read(TestContent.Json("""
            {"id": "wizard", "name": "Wizard", "hitDie": 6, "casting": "prepared", "dcAbility": "int",
             "proficiencyRanks": {"dc": "trained"}, "resources": {"potions": {"max": 2}, "wands": {"max": 3, "current": 1}},
             "spells": {"0": ["spark"], "1": ["mire"]},
             "levels": [
               {"slots": {"1": 2}, "spells": 2, "skills": 1,
                "features": [{"id": "focus-pool", "name": "Focus Pool", "resources": {"focus": 1}, "modifiers": [{"stat": "dc", "value": 1}]}]},
               {"feats": ["class"], "ranks": {"dc": "expert"}, "slots": {"1": 3}}
             ]}
            """));
        Assert.Equal((6, 30, "prepared", "int"), (wizard.HitDie, wizard.Speed, wizard.Casting, wizard.DcAbility));
        Assert.Equal(new Resource(2, 2), wizard.Resources["potions"]);
        Assert.Equal(new Resource(1, 3), wizard.Resources["wands"]);
        Assert.Equal(new[] { "mire" }, wizard.Spells[1]);
        Assert.Equal(2, wizard.Levels.Count);
        Assert.Equal((2, 2, 1), (wizard.Levels[0].Slots[1], wizard.Levels[0].Spells, wizard.Levels[0].Skills));
        Assert.Equal(1, wizard.Levels[0].Features[0].Gives.Resources["focus"]);
        Assert.Equal(new[] { "class" }, wizard.Levels[1].Feats);
        Assert.Equal("expert", wizard.Levels[1].Ranks["dc"]);

        ClassDefinition bare = ClassDefinition.Read(TestContent.Json("{\"id\": \"peasant\"}"));
        Assert.Equal(("peasant", 8, 30, 0, 0, "known", ""), (bare.Name, bare.HitDie, bare.Speed, bare.Darkvision, bare.BonusHp, bare.Casting, bare.DcAbility));
        Assert.Empty(bare.Levels);
        Assert.Empty(bare.Spells);
    }

    [Theory]
    [InlineData("{\"id\": \"x\", \"hitDie\": 0}", "hitDie")]
    [InlineData("{\"id\": \"x\", \"casting\": \"wild\"}", "casting")]
    [InlineData("{\"id\": \"x\", \"spells\": {\"one\": []}}", "spells.one")]
    [InlineData("{\"id\": \"x\", \"spells\": {\"1\": [\"Big Bang\"]}}", "spells.1[0]")]
    [InlineData("{\"id\": \"x\", \"resources\": {\"potions\": {\"max\": 1, \"current\": 2}}}", "resources.potions.current")]
    [InlineData("{\"id\": \"x\", \"levels\": [{}, {\"slots\": {\"0\": 2}}]}", "levels[1].slots.0")]
    [InlineData("{\"id\": \"x\", \"levels\": [{\"feats\": [\"lucky\"]}]}", "levels[0].feats")]
    [InlineData("{\"id\": \"x\", \"levels\": [{\"bonus\": 1}]}", "levels[0].bonus")]
    [InlineData("{\"id\": \"x\", \"levels\": [{\"features\": [{\"name\": \"No id\"}]}]}", "levels[0].features[0].id")]
    [InlineData("{\"id\": \"x\", \"levels\": [{\"features\": [{\"id\": \"f\", \"modifiers\": [{\"stat\": \"ac\", \"value\": 1, \"when\": \"raging\"}]}]}]}", "levels[0].features[0].modifiers[0].when")]
    public void ABrokenClassNamesTheField(string text, string field)
    {
        Assert.Equal(field, TestContent.Refused(() => ClassDefinition.Read(TestContent.Json(text))).Field);
    }

    [Fact]
    public void CreaturesReadTheirFieldsAndDefaults()
    {
        CreatureDefinition boss = CreatureDefinition.Read(TestContent.Json("""
            {"id": "goblin-boss", "name": "Goblin boss", "hp": 20, "armorClass": 15, "level": 3, "deathSaves": true,
             "abilities": {"str": 14}, "items": ["battleaxe"], "loot": {"coins": "4d10"},
             "token": {"color": [180, 60, 50], "size": 0.46}, "ai": {"base": "tactical", "leader": true}}
            """));
        Assert.Equal((20, 15, 3, true, 14), (boss.Hp, boss.ArmorClass, boss.Level, boss.DeathSaves, boss.Abilities["str"]));
        Assert.Equal(new CreatureToken(new ContentColor(180, 60, 50), 0.46), boss.Token);
        Assert.Equal("4d10", boss.Loot.Coins);
        Assert.True(AiProfile.Read(boss.Ai!.Value).Leader);

        CreatureDefinition bare = CreatureDefinition.Read(TestContent.Json("{\"id\": \"rat\"}"));
        Assert.Equal(("rat", 7, 1, 12, 30, 0, false), (bare.Name, bare.Hp, bare.Level, bare.ArmorClass, bare.Speed, bare.Darkvision, bare.DeathSaves));
        Assert.Null(bare.Ai);
        Assert.True(bare.Loot.IsEmpty);
        Assert.Equal(0.4, bare.Token.Size);
    }

    [Theory]
    [InlineData("{\"id\": \"x\", \"hp\": 0}", "hp")]
    [InlineData("{\"id\": \"x\", \"armorClass\": 101}", "armorClass")]
    [InlineData("{\"id\": \"x\", \"token\": {\"color\": [300, 0, 0]}}", "token.color")]
    [InlineData("{\"id\": \"x\", \"token\": {\"size\": 0}}", "token.size")]
    [InlineData("{\"id\": \"x\", \"ai\": 7}", "ai")]
    [InlineData("{\"id\": \"x\", \"loot\": {\"coins\": -5}}", "loot.coins")]
    [InlineData("{\"id\": \"x\", \"proficiencyRanks\": {\"weapons\": 2}}", "proficiencyRanks.weapons")]
    public void ABrokenCreatureNamesTheField(string text, string field)
    {
        Assert.Equal(field, TestContent.Refused(() => CreatureDefinition.Read(TestContent.Json(text))).Field);
    }

    [Fact]
    public void PlayerOptionsReadAndRefuseUnknownFields()
    {
        RaceDefinition dwarf = RaceDefinition.Read(TestContent.Json(
            "{\"id\": \"dwarf\", \"name\": \"Dwarf\", \"speed\": 25, \"darkvision\": 60, \"bonusHp\": 2, \"abilities\": {\"con\": 2, \"cha\": -2}, \"proficiencies\": [\"survival\"], \"feats\": [\"stone-sense\"]}"));
        Assert.Equal((25, 60, 2, 2, -2), (dwarf.Speed, dwarf.Darkvision, dwarf.BonusHp, dwarf.Abilities["con"], dwarf.Abilities["cha"]));
        Assert.Equal("elf", RaceDefinition.Read(TestContent.Json("{\"id\": \"elf\"}")).Name);
        Assert.Equal("size", TestContent.Refused(() => RaceDefinition.Read(TestContent.Json("{\"id\": \"elf\", \"size\": \"medium\"}"))).Field);
        Assert.Equal("abilities.con", TestContent.Refused(() => RaceDefinition.Read(TestContent.Json("{\"id\": \"elf\", \"abilities\": {\"con\": 11}}"))).Field);

        BackgroundDefinition soldier = BackgroundDefinition.Read(TestContent.Json(
            "{\"id\": \"soldier\", \"abilities\": {\"str\": 1}, \"proficiencies\": [\"athletics\"], \"feats\": [\"drilled\"], \"items\": [\"spear\"]}"));
        Assert.Equal(new[] { "spear" }, soldier.Items);
        Assert.Equal("speed", TestContent.Refused(() => BackgroundDefinition.Read(TestContent.Json("{\"id\": \"soldier\", \"speed\": 30}"))).Field);

        FeatDefinition tough = FeatDefinition.Read(TestContent.Json("""
            {"id": "tough", "name": "Tough", "kind": "general", "repeatable": true,
             "requires": {"level": 2, "classes": ["fighter"], "abilities": {"con": 12}},
             "modifiers": [{"stat": "maxHp", "op": "add", "value": 3}], "ranks": {"con": "expert"}, "resources": {"grit": 1}}
            """));
        Assert.Equal((2, 12, true), (tough.Needs.Level, tough.Needs.Abilities["con"], tough.Repeatable));
        Assert.Equal(new Modifier("maxHp", ModifierOp.Add, 3), tough.Gives.Modifiers[0]);
        Assert.Equal(("expert", 1), (tough.Gives.Ranks["con"], tough.Gives.Resources["grit"]));
        FeatDefinition bare = FeatDefinition.Read(TestContent.Json("{\"id\": \"plain\"}"));
        Assert.Equal(("general", 1, false), (bare.Kind, bare.Needs.Level, bare.Repeatable));
        Assert.Equal("kind", TestContent.Refused(() => FeatDefinition.Read(TestContent.Json("{\"id\": \"x\", \"kind\": \"epic\"}"))).Field);
        Assert.Equal("requires.level", TestContent.Refused(() => FeatDefinition.Read(TestContent.Json("{\"id\": \"x\", \"requires\": {\"level\": 0}}"))).Field);
        Assert.Equal("requires.alignment", TestContent.Refused(() => FeatDefinition.Read(TestContent.Json("{\"id\": \"x\", \"requires\": {\"alignment\": \"good\"}}"))).Field);
    }

    [Fact]
    public void AiProfilesBuildOnAnother()
    {
        AiProfile cunning = AiProfile.Read(TestContent.Json("\"cunning\""));
        Assert.Equal((1.5, 0.3, 0.75, true), (cunning.Finish, cunning.FleeHp, cunning.FleeLosses, cunning.FleeLeaderless));

        // No base: the changes go on top of the AI being adjusted, "cunning" when there is none.
        AiProfile braver = AiProfile.Read(TestContent.Json("{\"fleeHp\": 0.1}"));
        Assert.Equal((0.1, 1.5), (braver.FleeHp, braver.Finish));
        AiProfile onBrute = AiProfile.Read(TestContent.Json("{\"fleeHp\": 0.1}"), null, new AiProfile { Base = "brute", Damage = 3 });
        Assert.Equal((0.1, 3.0, "brute"), (onBrute.FleeHp, onBrute.Damage, onBrute.Base));

        AiProfile custom = AiProfile.Read(TestContent.Json("{\"base\": \"none\", \"onBreak\": {\"flee\": 2, \"surrender\": 1}, \"leader\": true}"));
        Assert.Equal(("custom", 0.0, true), (custom.Base, custom.Finish, custom.Leader));
        Assert.Equal(new[] { new KeyValuePair<string, double>("flee", 2), new KeyValuePair<string, double>("surrender", 1) }, custom.OnBreak);
        Assert.Equal("alarm", AiProfile.Read(TestContent.Json("{\"onBreak\": \"alarm\"}")).OnBreak[0].Key);

        Assert.Contains("unknown AI \"genius\"", TestContent.Refused(() => AiProfile.Read(TestContent.Json("\"genius\""))).Message);
        Assert.Equal("base", TestContent.Refused(() => AiProfile.Read(TestContent.Json("{\"base\": \"genius\"}"))).Field);
        Assert.Equal("onBreak", TestContent.Refused(() => AiProfile.Read(TestContent.Json("{\"onBreak\": \"sulk\"}"))).Field);
        Assert.Equal("onBreak", TestContent.Refused(() => AiProfile.Read(TestContent.Json("{\"onBreak\": {\"flee\": 0}}"))).Field);
        Assert.Equal("fleeHp", TestContent.Refused(() => AiProfile.Read(TestContent.Json("{\"fleeHp\": -1}"))).Field);
        Assert.Equal("escapeAt", TestContent.Refused(() => AiProfile.Read(TestContent.Json("{\"escapeAt\": 0}"))).Field);
        Assert.Equal("model", TestContent.Refused(() => AiProfile.Read(TestContent.Json("{\"model\": \"oracle\"}"))).Field);
    }

    [Fact]
    public void ACompendiumLoadsAFolderAndChecksWhatItNames()
    {
        using var scratch = new Scratch();
        scratch.Write("items/club.json", "{\"id\": \"club\", \"damage\": \"1d4\"}");
        scratch.Write("classes/thug.json", "{\"id\": \"thug\", \"items\": [\"club\"]}");
        // Profiles may build on each other in any order: "meek" is read before "mouse" exists.
        scratch.Write("ai/meek.json", "{\"id\": \"meek\", \"base\": \"mouse\", \"fleeHp\": 0.9}");
        scratch.Write("ai/mouse.json", "{\"id\": \"mouse\", \"base\": \"animal\", \"random\": 1}");
        scratch.Write("creatures/rat.json", "{\"id\": \"rat\", \"items\": [\"club\"], \"loot\": {\"items\": [\"club\"]}, \"ai\": \"meek\"}");
        var files = new ContentFiles(scratch.Folder);
        var compendium = new Compendium();
        compendium.Load(files, "");
        Assert.Equal("1d4", compendium.Item("club")?.Damage);
        Assert.NotNull(compendium.Class("thug"));
        Assert.Equal((0.9, 1.0, 1.5, "meek"), (compendium.Ai["meek"].FleeHp, compendium.Ai["meek"].Random, compendium.Ai["meek"].Weak, compendium.Ai["meek"].Base));
        Assert.Equal(0.9, compendium.AiFor(compendium.Creature("rat")!).FleeHp);
        Assert.Null(compendium.Item("sword"));

        void Broken(string path, string text, string field, string says)
        {
            using var broken = new Scratch();
            broken.Write(path, text);
            var both = new ContentFiles(scratch.Folder);
            both.Add(broken.Folder);
            ContentException error = TestContent.Refused(() => new Compendium().Load(both, ""));
            Assert.Equal((path, field), (error.File, error.Field));
            Assert.Contains(says, error.Message);
        }
        Broken("items/axe.json", "{\"id\": \"hatchet\"}", "id", "doesn't match the file name");
        Broken("classes/thug.json", "{\"id\": \"thug\", \"items\": [\"sword\"]}", "items", "unknown item \"sword\"");
        Broken("creatures/rat.json", "{\"id\": \"rat\", \"items\": [\"sword\"]}", "items", "unknown item \"sword\"");
        Broken("creatures/rat.json", "{\"id\": \"rat\", \"loot\": {\"items\": [\"gem\"]}}", "loot", "unknown item \"gem\"");
        Broken("creatures/rat.json", "{\"id\": \"rat\", \"ai\": \"genius\"}", "ai", "unknown AI \"genius\"");
        Broken("ai/mouse.json", "{\"id\": \"mouse\", \"base\": \"meek\"}", "base", "unknown AI");
        Broken("items/club.json", "{\"id\": \"club\", \"damage\": 4}", "damage", "is text");
    }

    [Fact]
    public void ChapterFilesReplaceSharedOnes()
    {
        using var scratch = new Scratch();
        scratch.Write("items/club.json", "{\"id\": \"club\", \"damage\": \"1d4\"}");
        scratch.Write("chapters/pit/items/club.json", "{\"id\": \"club\", \"damage\": \"1d6\"}");
        var files = new ContentFiles(scratch.Folder);
        var compendium = new Compendium();
        compendium.Load(files, "");
        compendium.Load(files, "chapters/pit");
        Assert.Equal("1d6", compendium.Item("club")?.Damage);
    }

    [Fact]
    public void PlayerOptionsAreCheckedAgainstEachOther()
    {
        void Broken(string path, string text, string file, string field, string says)
        {
            using var scratch = new Scratch();
            scratch.Write(path, text);
            ContentFiles files = TestContent.ShippedWith(scratch);
            ContentException error = TestContent.Refused(() => Chapter.Load(files, "chapters/goblin-keep"));
            Assert.Equal((file, field), (error.File, error.Field));
            Assert.Contains(says, error.Message);
        }
        const string options = "rulesets/yorehold/";
        Broken(options + "races/gnome.json", "{\"id\": \"gnome\", \"feats\": [\"tinker\"]}", options + "races/gnome.json", "feats", "no feat \"tinker\"");
        Broken(options + "feats/tinker.json", "{\"id\": \"tinker\", \"requires\": {\"races\": [\"gnome\"]}}", options + "feats/tinker.json", "requires.races", "no race \"gnome\"");
        Broken(options + "feats/tinker.json", "{\"id\": \"tinker\", \"requires\": {\"classes\": [\"artificer\"]}}", options + "feats/tinker.json", "requires.classes", "no class \"artificer\"");
        Broken(options + "backgrounds/smith.json", "{\"id\": \"smith\", \"items\": [\"anvil\"]}", options + "backgrounds/smith.json", "items", "no item \"anvil\"");
        Broken(options + "feats/tinker.json", "{\"id\": \"tinker\", \"cost\": 2}", options + "feats/tinker.json", "cost", "unknown field");
        Broken("classes/bard.json", "{\"id\": \"bard\", \"spells\": {\"1\": [\"lullaby\"]}}", "classes/bard.json", "spells.1", "no spell \"lullaby\"");
        Broken("classes/bard.json", "{\"id\": \"bard\", \"spells\": {\"2\": [\"mire\"]}}", "classes/bard.json", "spells.2", "\"mire\" is a level 1 spell");
        Broken("classes/bard.json", "{\"id\": \"bard\", \"proficiencyRanks\": {\"lutes\": \"trained\"}}", "classes/bard.json", "proficiencyRanks.lutes", "unknown proficiency target");
        Broken("creatures/imp.json", "{\"id\": \"imp\", \"proficiencyRanks\": {\"weapons\": \"godlike\"}}", "creatures/imp.json", "proficiencyRanks.weapons", "unknown rank \"godlike\"");
        Broken("creatures/imp.json", "{\"id\": \"imp\", \"dcAbility\": \"luck\"}", "creatures/imp.json", "dcAbility", "unknown ability \"luck\"");
        Broken("items/tonic.json", "{\"id\": \"tonic\", \"use\": {\"effects\": [{\"do\": \"condition\", \"id\": \"tipsy\"}]}}", "items/tonic.json", "use.effects[0].id", "unknown condition \"tipsy\"");
        Broken(options + "spells/hex.json", "{\"id\": \"hex\", \"effects\": [{\"do\": \"condition\", \"id\": \"hexed\"}]}", options + "spells/hex.json", "effects[0].id", "unknown condition \"hexed\"");
        Broken(options + "spells/strike.json", "{\"id\": \"strike\", \"effects\": [{\"do\": \"heal\", \"dice\": 1}]}", options + "spells/strike.json", "id", "an action already has this id");
        Broken(options + "positioning.json", "{\"flankingCondition\": \"pinned\"}", options + "positioning.json", "flankingCondition", "unknown condition \"pinned\"");
        Broken(options + "stealth.json", "{\"checkEvery\": 0}", options + "stealth.json", "checkEvery", "above 0");
    }
}
