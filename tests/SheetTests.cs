namespace Yorehold.Rules.Tests;

/// <summary>Stats and how modifiers stack, proficiency ranks, DCs, and conditions on a sheet.</summary>
public class SheetTests
{
    private const string Abilities = """
        "abilities":[{"id":"str","name":"Str"},{"id":"dex","name":"Dex"},{"id":"con","name":"Con"},
            {"id":"int","name":"Int"},{"id":"wis","name":"Wis"},{"id":"cha","name":"Cha"}],
        "skills":[{"id":"athletics","name":"Athletics","ability":"str"},{"id":"stealth","name":"Stealth","ability":"dex"}]
        """;

    private const string Table = "\"proficiencyByLevel\":[2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,6,6,6,6]";

    private const string Ranks = """
        "proficiencyRanks":[{"id":"none","name":"None","bonus":0,"addsLevel":false},{"id":"basic","name":"Basic","bonus":2,"addsLevel":true},
            {"id":"advanced","name":"Advanced","bonus":4,"addsLevel":true},{"id":"master","name":"Master","bonus":6,"addsLevel":true},
            {"id":"highest","name":"Highest","bonus":8,"addsLevel":true}],
        "proficientRank":"basic","untrainedRank":"none"
        """;

    // One condition of each kind the machinery knows.
    private const string Conditions = """
        {"id":"t","name":"Test","abilities":[{"id":"str","name":"Str"},{"id":"dex","name":"Dex"},{"id":"con","name":"Con"},{"id":"wis","name":"Wis"}],
        "conditions":[
            {"id":"guarded","name":"Guarded","modifiers":[{"stat":"ac","value":2}],"duration":2},
            {"id":"marked","name":"Marked","stacking":"longest"},
            {"id":"shaken","name":"Shaken","stacking":"value","maxValue":3,"perValue":true,"decay":1,
                "modifiers":[{"stat":"attack","value":-1},{"stat":"speed","op":"multiply","value":0.5}]},
            {"id":"held","name":"Held","flags":["cantMove","offGuard"],"save":{"ability":"str","dc":-100}},
            {"id":"gripped","name":"Gripped","flags":["cantMove"],"save":{"ability":"str","dc":1000}},
            {"id":"stunned","name":"Stunned","flags":["cantAct"],"ends":["damage"]},
            {"id":"braced","name":"Braced","ends":["turnStart"]},
            {"id":"unseen","name":"Unseen","ends":["attack"]},
            {"id":"asleep","name":"Asleep","flags":["cantAct","cantMove"],"ends":["damage","rest"]},
            {"id":"gone","name":"Gone","removes":["asleep","stunned"]},
            {"id":"lucky","name":"Lucky","advantageOnAttacks":true},
            {"id":"dizzy","name":"Dizzy","disadvantageOnAttacks":true}
        ]}
        """;

    [Fact]
    public void ModifiersStackOnTheBase()
    {
        var stats = new StatBlock();
        Assert.Equal((0f, 0), (stats.Value("speed"), stats.Integer("speed")));
        stats.SetBase("speed", 30);
        stats.SetBase("ac", 10);
        Assert.Equal((30f, 30f), (stats.Base("speed"), stats.Value("speed")));

        // Adds sum, multiplies multiply, and the adds go on before the multiplies.
        stats.AddModifier(new Modifier("speed", ModifierOp.Add, 10), "boots");
        stats.AddModifier(new Modifier("speed", ModifierOp.Add, -5), "mud");
        Assert.Equal(35, stats.Integer("speed"));
        stats.AddModifier(new Modifier("speed", ModifierOp.Multiply, 0.5), "slowed");
        Assert.Equal((17.5f, 17), (stats.Value("speed"), stats.Integer("speed")));
        stats.AddModifier(new Modifier("speed", ModifierOp.Multiply, 0.5), "webbed");
        Assert.Equal(8, stats.Integer("speed"));

        // The highest override stands in for the base; adds still go on top of it.
        stats.AddModifier(new Modifier("ac", ModifierOp.Override, 14), "leather");
        stats.AddModifier(new Modifier("ac", ModifierOp.Override, 16), "mail");
        stats.AddModifier(new Modifier("ac", ModifierOp.Override, 12), "cloth");
        stats.AddModifier(new Modifier("ac", ModifierOp.Add, 2), "shield");
        Assert.Equal((18, 10f), (stats.Integer("ac"), stats.Base("ac")));

        // Everything from one source comes off together, and only that.
        stats.AddModifier(new Modifier("speed", ModifierOp.Add, 100), "mail");
        stats.RemoveSource("mail");
        Assert.Equal((16, 8, 7), (stats.Integer("ac"), stats.Integer("speed"), stats.Modifiers.Count));
        stats.RemoveSource("nothing");
        Assert.Equal(7, stats.Modifiers.Count);

        // A third of 30 three times over is a hair under 30 in float; it still reads as 30.
        var thirds = new StatBlock();
        thirds.SetBase("x", 30);
        thirds.AddModifier(new Modifier("x", ModifierOp.Multiply, 1.0 / 3), "a");
        thirds.AddModifier(new Modifier("x", ModifierOp.Multiply, 3), "b");
        Assert.Equal(30, thirds.Integer("x"));
        thirds.SetBase("y", -0.5f);
        Assert.Equal(-1, thirds.Integer("y"));
    }

    [Fact]
    public void AbilityScoresGiveModifiers()
    {
        Ruleset d20 = RulesTesting.Rules("{\"id\":\"m\",\"name\":\"M\"," + Abilities + "}");
        Assert.Equal(new[] { -5, -4, -1, -1, 0, 0, 2, 4, 5 }, new[] { 1, 3, 8, 9, 10, 11, 14, 18, 20 }.Select(d20.AbilityModifier).ToArray());
        Ruleset classic = RulesTesting.Rules("{\"id\":\"c\",\"name\":\"C\",\"modifierTable\":\"classic\"," + Abilities + "}");
        Assert.Equal(new[] { -3, -2, -2, -1, -1, 0, 0, 1, 1, 2, 2, 3 }, new[] { 3, 4, 5, 6, 8, 9, 12, 13, 15, 16, 17, 18 }.Select(classic.AbilityModifier).ToArray());

        // No table and no ranks: nothing is added for proficiency.
        Assert.Equal((0, 0), (classic.ProficiencyBonus(5), classic.ProficiencyBonus(5, "basic")));
        Ruleset table = RulesTesting.Rules("{\"id\":\"m\",\"name\":\"M\"," + Abilities + "," + Table + "}");
        Assert.Equal(new[] { 2, 2, 3, 6, 6 }, new[] { 0, 1, 5, 20, 99 }.Select(table.ProficiencyBonus).ToArray());
    }

    [Fact]
    public void RanksAddTheirBonusAndTheLevel()
    {
        Ruleset rules = RulesTesting.Rules("{\"id\":\"r\",\"name\":\"R\"," + Abilities + "," + Table + "," + Ranks + "}");
        var sheet = new CharacterSheet { DcAbility = "wis" };
        sheet.Stats.SetBase("str", 16);
        sheet.Stats.SetBase("dex", 14);
        sheet.Stats.SetBase("wis", 18);
        sheet.Stats.SetBase("ac", 10);
        sheet.Stats.SetBase("attack", 1);
        sheet.Stats.SetBase("dc", 2);
        foreach (ProficiencyRank rank in rules.ProficiencyRanks)
        {
            for (int level = 1; level <= 20; level++)
            {
                sheet.Level = level;
                foreach (string target in new[] { "weapons", "armor", "dc", "str", "athletics" })
                {
                    sheet.ProficiencyRanks[target] = rank.Id;
                }
                int bonus = rank.Bonus + (rank.AddsLevel ? level : 0);
                Assert.Equal(bonus, rules.ProficiencyBonus(level, rank.Id));
                Assert.Equal(4 + bonus, sheet.AttackModifier(rules));
                Assert.Equal(3 + bonus, sheet.SaveModifier(rules, "str"));
                Assert.Equal(3 + bonus, sheet.CheckModifier(rules, "athletics"));
                Assert.Equal(13 + bonus, sheet.PassiveScore(rules, "athletics"));
                Assert.Equal(12 + bonus, sheet.ArmorClass(rules));
                Assert.Equal(16 + bonus, sheet.DifficultyClass(rules));
                Assert.Equal(15 + bonus, sheet.DifficultyClass(rules, "str"));
                Assert.Equal((3, 2), (sheet.CheckModifier(rules, "str"), sheet.InitiativeModifier(rules)));
            }
        }

        // The plain list means the ruleset's "proficient" rank; a rank written out wins over it.
        sheet.Level = 7;
        sheet.ProficiencyRanks.Clear();
        sheet.Proficiencies.UnionWith(new[] { "weapons", "armor", "athletics", "str", "dc" });
        Assert.Equal(("basic", 13), (sheet.ProficiencyRank(rules, "athletics"), sheet.AttackModifier(rules)));
        Assert.Equal(("none", 2), (sheet.ProficiencyRank(rules, "stealth"), sheet.CheckModifier(rules, "stealth")));
        sheet.ProficiencyRanks["weapons"] = "none";
        Assert.Equal(4, sheet.AttackModifier(rules));
        sheet.ProficiencyRanks["armor"] = "advanced";
        Assert.Equal(23, sheet.ArmorClass(rules));
        sheet.Stats.AddModifier(new Modifier("ac", ModifierOp.Add, -2), "test");
        sheet.Stats.AddModifier(new Modifier("dc", ModifierOp.Add, 1), "test");
        Assert.Equal((21, 26), (sheet.ArmorClass(rules), sheet.DifficultyClass(rules)));
        sheet.DcAbility = "";
        Assert.Equal(22, sheet.DifficultyClass(rules));
        Assert.Equal(0, rules.ProficiencyBonus(7, "missing"));

        // A rank that doesn't add the level is a flat bonus.
        Ruleset flat = RulesTesting.Rules("{\"id\":\"r\",\"name\":\"R\"," + Abilities + ","
            + "\"proficiencyRanks\":[{\"id\":\"none\"},{\"id\":\"basic\",\"bonus\":7}],\"proficientRank\":\"basic\",\"untrainedRank\":\"none\"}");
        Assert.Equal(7, sheet.ProficiencyModifier(flat, "athletics"));

        // Rulesets with the per-level table ignore ranks on the sheet and add no armour proficiency to AC.
        Ruleset table = RulesTesting.Rules("{\"id\":\"m\",\"name\":\"M\"," + Abilities + "," + Table + "}");
        Assert.Equal(3 + 3 + 1, sheet.AttackModifier(table));
        Assert.Equal(3 + 3, sheet.SaveModifier(table, "str"));
        Assert.Equal(8 + 2, sheet.ArmorClass(table));
        Ruleset classic = RulesTesting.Rules("{\"id\":\"c\",\"name\":\"C\",\"modifierTable\":\"classic\",\"armorClassAbility\":\"\"," + Abilities + "}");
        Assert.Equal(2 + 0 + 1, sheet.AttackModifier(classic));
        Assert.Equal(2, sheet.SaveModifier(classic, "str"));
        Assert.Equal(8, sheet.ArmorClass(classic));
        Assert.Equal(0, sheet.InitiativeModifier(RulesTesting.Rules("{\"id\":\"c\",\"name\":\"C\",\"initiativeAbility\":\"\"," + Abilities + "}")));
    }

    [Fact]
    public void WeaponsSetTheAttackAndDamage()
    {
        Ruleset rules = RulesTesting.Rules("{\"id\":\"m\",\"name\":\"M\"," + Abilities + "}");
        CharacterSheet sheet = RulesTesting.Plain("Ana");
        Assert.Equal((0, "1"), (sheet.AttackModifier(rules), sheet.DamageDice(rules))); // unarmed
        sheet.Stats.SetBase("str", 14);
        sheet.Stats.SetBase("dex", 18);
        sheet.Weapon = new Weapon("1d8");
        Assert.Equal((2, "1d8+2"), (sheet.AttackModifier(rules), sheet.DamageDice(rules)));
        sheet.Weapon = new Weapon("1d6", "dex");
        sheet.Stats.SetBase("damage", 1);
        Assert.Equal((4, "1d6+5"), (sheet.AttackModifier(rules), sheet.DamageDice(rules)));
        sheet.Stats.SetBase("dex", 6);
        sheet.Stats.SetBase("damage", 0);
        Assert.Equal("1d6-2", sheet.DamageDice(rules));
        Assert.Equal(6, sheet.SpeedSquares(rules));
    }

    [Fact]
    public void DamageBurnsTemporaryHpFirst()
    {
        CharacterSheet hero = RulesTesting.Plain("Ana");
        hero.TempHp = 3;
        Assert.False(hero.TakeDamage(4));
        Assert.Equal((0, 99), (hero.TempHp, hero.Hp));
        Assert.False(hero.TakeDamage(-5));
        Assert.Equal(99, hero.Hp);
        Assert.True(hero.TakeDamage(500));
        Assert.True(hero.Down);
        Assert.False(hero.TakeDamage(1)); // already down
        hero.Heal(30);
        Assert.Equal(30, hero.Hp);
        hero.Heal(500);
        Assert.Equal(100, hero.Hp);
    }

    [Fact]
    public void ConditionsGoOnStackAndEnd()
    {
        Ruleset rules = RulesTesting.Rules(Conditions);
        CharacterSheet c = RulesTesting.Plain("subject");
        int ac = c.ArmorClass(rules);
        int attack = c.AttackModifier(rules);
        int speed = c.SpeedFeet;

        // Applying: modifiers go on with it and come off with it; the duration is the definition's unless one is given.
        c.AddCondition(rules, "guarded");
        Assert.True(c.HasCondition("guarded"));
        Assert.Equal((1, ac + 2, 2), (c.ConditionValue("guarded"), c.ArmorClass(rules), c.Conditions[0].RoundsLeft));
        c.RemoveCondition("guarded");
        Assert.False(c.HasCondition("guarded"));
        Assert.Equal((0, ac), (c.ConditionValue("guarded"), c.ArmorClass(rules)));
        c.AddCondition(rules, "guarded", 5);
        Assert.Equal(5, c.Conditions[0].RoundsLeft);
        c.AddCondition(rules, "homebrew", 1); // not in the ruleset: still tracked, with nothing attached
        Assert.True(c.HasCondition("homebrew") && !c.HasFlag(rules, "cantAct"));
        c.AddCondition(rules, "held");
        Assert.True(c.HasFlag(rules, "cantMove") && c.HasFlag(rules, "offGuard") && !c.HasFlag(rules, "cantAct"));
        c.RemoveCondition("held");
        Assert.False(c.HasFlag(rules, "cantMove"));

        // Stacking. Refresh: the newest duration stands, and the modifiers are not doubled.
        c.AddCondition(rules, "guarded", 1);
        Assert.Equal((2, 1, ac + 2), (c.Conditions.Count, c.Conditions[^1].RoundsLeft, c.ArmorClass(rules)));
        // Longest: a shorter one does not cut a longer one short, and nothing outlasts "until removed".
        c.AddCondition(rules, "marked", 4);
        c.AddCondition(rules, "marked", 2);
        Assert.Equal(4, c.Conditions[^1].RoundsLeft);
        c.AddCondition(rules, "marked", 6);
        Assert.Equal(6, c.Conditions[^1].RoundsLeft);
        c.AddCondition(rules, "marked");
        c.AddCondition(rules, "marked", 3);
        Assert.Equal(-1, c.Conditions[^1].RoundsLeft);
        // Value: they add up to the cap, and additive modifiers scale with the value.
        c.AddCondition(rules, "shaken");
        Assert.Equal((1, attack - 1, speed / 2), (c.ConditionValue("shaken"), c.AttackModifier(rules), c.SpeedFeet));
        c.AddCondition(rules, "shaken");
        Assert.Equal((2, attack - 2, speed / 2), (c.ConditionValue("shaken"), c.AttackModifier(rules), c.SpeedFeet));
        c.AddCondition(rules, "shaken", CharacterSheet.DefinedDuration, 5);
        Assert.Equal((3, attack - 3), (c.ConditionValue("shaken"), c.AttackModifier(rules)));

        // Ending by time and by decay: one round takes a point off Shaken and ends the one-round conditions.
        List<string> ended = c.EndRound(rules);
        Assert.Equal(new[] { "guarded", "homebrew" }, ended.Order().ToArray());
        Assert.Equal((ac, 2, attack - 2), (c.ArmorClass(rules), c.ConditionValue("shaken"), c.AttackModifier(rules)));
        Assert.True(c.HasCondition("marked"));
        ended = c.EndRound(rules);
        Assert.Empty(ended);
        Assert.Equal((1, attack - 1), (c.ConditionValue("shaken"), c.AttackModifier(rules)));
        ended = c.EndRound(rules);
        Assert.Equal(new[] { "shaken" }, ended.ToArray());
        Assert.False(c.HasCondition("shaken"));
        Assert.Equal((attack, speed), (c.AttackModifier(rules), c.SpeedFeet));
        c.RemoveCondition("marked");
        Assert.Empty(c.Conditions);
        Assert.Empty(c.Stats.Modifiers);

        // Ending by a save: rolled at the end of the round, and only when there are dice to roll.
        var random = new Rng(5);
        c.AddCondition(rules, "held");    // DC so low it always ends
        c.AddCondition(rules, "gripped"); // DC so high it never does
        Assert.Empty(c.EndRound(rules));
        Assert.True(c.HasCondition("held"));
        ended = c.EndRound(rules, random);
        Assert.Equal(new[] { "held" }, ended.ToArray());
        Assert.True(c.HasCondition("gripped") && c.HasFlag(rules, "cantMove"));
        c.RemoveCondition("gripped");

        // Ending by what happens: only the conditions that listen for it.
        c.AddCondition(rules, "asleep");
        c.AddCondition(rules, "braced");
        c.AddCondition(rules, "stunned");
        Assert.Empty(c.ConditionEvent(rules, "move"));
        Assert.Equal(3, c.Conditions.Count);
        ended = c.ConditionEvent(rules, "rest");
        Assert.Equal(new[] { "asleep" }, ended.ToArray());
        Assert.True(c.HasCondition("stunned"));
        c.AddCondition(rules, "asleep");
        ended = c.ConditionEvent(rules, "damage");
        Assert.Equal(new[] { "asleep", "stunned" }, ended.Order().ToArray());
        Assert.True(c.HasCondition("braced") && !c.HasFlag(rules, "cantAct"));
        Assert.Equal(new[] { "braced" }, c.ConditionEvent(rules, "turnStart").ToArray());

        // One condition can take others off as it goes on.
        c.AddCondition(rules, "asleep");
        c.AddCondition(rules, "stunned");
        c.AddCondition(rules, "braced");
        c.AddCondition(rules, "gone");
        Assert.True(c.HasCondition("gone") && !c.HasCondition("asleep") && !c.HasCondition("stunned") && c.HasCondition("braced"));

        // The plain countdown still works for sheets used without a ruleset.
        CharacterSheet plain = RulesTesting.Plain("plain");
        plain.AddCondition(rules, "guarded", 1);
        plain.EndRound();
        Assert.False(plain.HasCondition("guarded"));
        Assert.Equal(ac, plain.ArmorClass(rules));
    }

    [Fact]
    public void AModifierOfItsOwnCountsDownLikeACondition()
    {
        Ruleset rules = RulesTesting.Rules(Conditions);
        CharacterSheet c = RulesTesting.Plain("subject");
        int ac = c.ArmorClass(rules);
        c.AddModifier("effect:ward", new Modifier("ac", ModifierOp.Add, 2), 2);
        c.AddModifier("effect:ward", new Modifier("speed", ModifierOp.Multiply, 0.5), 3);
        Assert.Equal((ac + 2, 15, 1, 3), (c.ArmorClass(rules), c.SpeedFeet, c.Conditions.Count, c.Conditions[0].RoundsLeft));
        c.EndRound(rules);
        c.EndRound(rules);
        Assert.Equal(ac + 2, c.ArmorClass(rules));
        Assert.Equal(new[] { "effect:ward" }, c.EndRound(rules).ToArray());
        Assert.Equal((ac, 30), (c.ArmorClass(rules), c.SpeedFeet));
    }

    [Fact]
    public void ConditionsCanForceAdvantage()
    {
        Ruleset rules = RulesTesting.Rules(Conditions);
        CharacterSheet c = RulesTesting.Plain("subject");
        Assert.Equal(Advantage.None, c.AttackAdvantage(rules));
        c.AddCondition(rules, "lucky");
        Assert.Equal(Advantage.Advantage, c.AttackAdvantage(rules));
        c.AddCondition(rules, "dizzy");
        Assert.Equal(Advantage.None, c.AttackAdvantage(rules)); // both cancel out
        c.RemoveCondition("lucky");
        Assert.Equal(Advantage.Disadvantage, c.AttackAdvantage(rules));
    }

    [Fact]
    public void TheShippedRulesetGivesTheGamesNumbers()
    {
        RulesFolder folder = RulesFolder.Load(TestContent.Shipped());
        Ruleset rules = folder.Rules;
        Assert.Equal(new[] { -1, -1, 0, 2, 5 }, new[] { 8, 9, 10, 14, 20 }.Select(rules.AbilityModifier).ToArray());
        Assert.Equal((0, 5, 9, 16, 28), (rules.ProficiencyBonus(3, "untrained"), rules.ProficiencyBonus(3, "trained"),
            rules.ProficiencyBonus(5, "expert"), rules.ProficiencyBonus(10, "master"), rules.ProficiencyBonus(20, "legendary")));
        Assert.Equal(new[] { 1, 1, 2, 2, 3, 20, 20 }, new[] { 0, 249, 250, 799, 800, 355000, 9000000 }.Select(rules.LevelForXp).ToArray());
        Assert.Equal((10, 12, 8), (rules.HitDie("Fighter"), rules.HitDie("Barbarian"), rules.HitDie("Nobody")));

        // A level 3 fighter type: strength 16, dexterity 14, trained in weapons, armour and athletics.
        var sheet = new CharacterSheet { Level = 3, Hp = 30, DcAbility = "str" };
        sheet.Stats.SetBase("str", 16);
        sheet.Stats.SetBase("dex", 14);
        sheet.Stats.SetBase("wis", 10);
        sheet.Stats.SetBase("ac", rules.BaseArmorClass);
        sheet.Stats.SetBase("speed", 30);
        sheet.Stats.SetBase("maxHp", 30);
        sheet.Proficiencies.UnionWith(new[] { "weapons", "armor", "athletics" });
        sheet.ProficiencyRanks["str"] = "expert";
        Assert.Equal(3 + 5, sheet.AttackModifier(rules));
        Assert.Equal(3 + 5, sheet.CheckModifier(rules, "athletics"));
        Assert.Equal(2, sheet.CheckModifier(rules, "stealth"));
        Assert.Equal(10 + 2, sheet.PassiveScore(rules, "stealth"));
        Assert.Equal(3 + 7, sheet.SaveModifier(rules, "str"));
        Assert.Equal(2, sheet.SaveModifier(rules, "dex"));
        Assert.Equal(10 + 2 + 5, sheet.ArmorClass(rules));
        Assert.Equal(10 + 3, sheet.DifficultyClass(rules));
        Assert.Equal(6, sheet.SpeedSquares(rules));

        // Off-guard is -2 AC until its next turn starts.
        int ac = sheet.ArmorClass(rules);
        sheet.AddCondition(rules, "off-guard");
        Assert.Equal(ac - 2, sheet.ArmorClass(rules));
        Assert.True(sheet.HasFlag(rules, "offGuard"));
        Assert.Equal(new[] { "off-guard" }, sheet.ConditionEvent(rules, "turnStart").ToArray());
        Assert.Equal(ac, sheet.ArmorClass(rules));

        // Frightened takes its value off attacks and AC, and drops by one each round.
        int attack = sheet.AttackModifier(rules);
        sheet.AddCondition(rules, "frightened", CharacterSheet.DefinedDuration, 2);
        Assert.Equal((2, attack - 2, ac - 2), (sheet.ConditionValue("frightened"), sheet.AttackModifier(rules), sheet.ArmorClass(rules)));
        sheet.AddCondition(rules, "frightened", CharacterSheet.DefinedDuration, 9);
        Assert.Equal(4, sheet.ConditionValue("frightened"));
        for (int left = 3; left >= 1; left--)
        {
            Assert.Empty(sheet.EndRound(rules));
            Assert.Equal((left, attack - left), (sheet.ConditionValue("frightened"), sheet.AttackModifier(rules)));
        }
        Assert.Equal(new[] { "frightened" }, sheet.EndRound(rules).ToArray());
        Assert.Equal((attack, ac), (sheet.AttackModifier(rules), sheet.ArmorClass(rules)));
    }
}
