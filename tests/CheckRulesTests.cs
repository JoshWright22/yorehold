namespace Yorehold.Rules.Tests;

public class CheckRulesTests
{
    [Fact]
    public void ASystemCountsACreaturesNumbersItsOwnWay()
    {
        // Proficiency is the rank's bonus plus the level, AC starts at 10 with it, checks add half the level.
        Ruleset rules = RulesTesting.Rules("""
            {"id": "own", "name": "Own", "abilities": [{"id": "str", "name": "Strength"}, {"id": "dex", "name": "Dexterity"}],
             "skills": [{"id": "athletics", "name": "Athletics", "ability": "str"}],
             "proficiencyRanks": [{"id": "untrained", "bonus": 0}, {"id": "trained", "bonus": 2}],
             "proficientRank": "trained", "untrainedRank": "untrained",
             "formulas": {
               "abilityModifier": "score - 10",
               "proficiency": "proficient ? rankBonus + level : 0",
               "armorClass": "10 + stat.ac + min(mod.dex, 2) + proficiency",
               "check": "ability + proficiency + floor(level / 2)",
               "attack": "ability + proficiency + bonus"
             }}
            """);
        var sheet = new CharacterSheet { Name = "Ash", Level = 4 };
        sheet.Stats.SetBase("str", 13);
        sheet.Stats.SetBase("dex", 15);
        sheet.Stats.SetBase("ac", 3);
        sheet.ProficiencyRanks["armor"] = "trained";
        sheet.ProficiencyRanks["weapons"] = "trained";
        Assert.Equal((3, 6, 21, 5, 9), (sheet.AbilityModifier(rules, "str"), sheet.ProficiencyModifier(rules, "armor"),
            sheet.ArmorClass(rules), sheet.CheckModifier(rules, "athletics"), sheet.AttackModifier(rules)));

        ContentException error = TestContent.Refused(() => RulesTesting.Rules(
            """{"id": "x", "name": "X", "abilities": [{"id": "str", "name": "S"}], "formulas": {"attack": "ability + luck"}}"""));
        Assert.Contains("unknown name \"luck\"", error.Message);
    }

    [Fact]
    public void ASystemSaysHowItsRollsResolve()
    {
        // Four degrees: ten over or under moves a step, and so do a natural 20 and a natural 1.
        Ruleset rules = RulesTesting.Rules("""
            {"id": "four", "name": "Four degrees", "abilities": [{"id": "str", "name": "Strength"}],
             "checks": {
               "criticalDamage": "(dice + flat) * 2",
               "attack": {
                 "outcomes": [{"id": "criticalFailure"}, {"id": "failure"}, {"id": "success", "passes": true},
                              {"id": "criticalSuccess", "passes": true, "critical": true}],
                 "degree": "clamp((total >= dc + 10 ? 3 : total >= dc ? 2 : total > dc - 10 ? 1 : 0) + (die == 20 ? 1 : 0) - (die == 1 ? 1 : 0), 0, 3)"
               },
               "pool": {"dice": "3d6", "outcomes": [{"id": "no"}, {"id": "yes", "passes": true}], "degree": "total >= dc"}
             }}
            """);
        CheckKind attack = rules.Checks.Kind(CheckRules.Attack);
        Assert.Equal(new[] { "criticalSuccess", "success", "failure", "criticalFailure", "success", "failure" }, new[]
        {
            attack.Resolve(15, 5, 10).Id, attack.Resolve(10, 5, 15).Id, attack.Resolve(5, 5, 15).Id,
            attack.Resolve(2, 0, 15).Id, attack.Resolve(20, 0, 25).Id, attack.Resolve(1, 20, 15).Id,
        });
        Dictionary<string, double> odds = attack.Odds(5, 15);
        Assert.Equal((0.05, 0.40, 0.50, 0.05), (Math.Round(odds["criticalFailure"], 3), Math.Round(odds["failure"], 3),
            Math.Round(odds["success"], 3), Math.Round(odds["criticalSuccess"], 3)));
        Assert.Equal(0.5, Math.Round(rules.Checks.Kind("pool").ChanceToPass(0, 11), 3));

        // Dice pools: successes counted, Fate dice, and an exploding die that always ends.
        Assert.Equal(("6d6s5", "4dF", "1d6!"), (DiceExpression.Parse("6d6s5")!.ToString(), DiceExpression.Parse("4dF")!.ToString(), DiceExpression.Parse("1d6!")!.ToString()));
        var pool = new CheckKind { Dice = DiceExpression.Parse("2d6s5")!, Outcomes = { new("no", "No", false, false, 1), new("yes", "Yes", true, false, 1) } };
        Assert.Equal(Math.Round(1 - 4.0 / 9, 3), Math.Round(pool.ChanceToPass(0, 1), 3));
        var random = new Rng(3);
        Assert.All(Enumerable.Range(0, 200).Select(_ => Dice.Roll("4dF", random).Total), total => Assert.InRange(total, -4, 4));
        Assert.Equal("(dice + flat) * 2", rules.Checks.CriticalDamage!.Text);

        // The game's own rules, when a system says nothing.
        CheckKind own = new CheckRules().Kind(CheckRules.Attack);
        Assert.Equal(("miss", "crit", "hit"), (own.Resolve(1, 30, 10).Id, own.Resolve(20, 0, 40).Id, own.Resolve(10, 2, 12).Id));

        ContentException error = TestContent.Refused(() => RulesTesting.Rules(
            """{"id": "x", "name": "X", "abilities": [{"id": "str", "name": "S"}], "checks": {"check": {"degree": "total >= armour"}}}"""));
        Assert.Contains("unknown name \"armour\"", error.Message);

        // A step may wait on one of the system's own outcomes, and on nothing the system lacks.
        RulesTesting.Effect("""[{"do": "heal", "dice": "1d6", "when": "criticalFailure"}]""").Check(rules, "x.json");
        error = TestContent.Refused(() => RulesTesting.Effect("""[{"do": "heal", "dice": "1d6", "when": "later"}]""").Check(rules, "x.json"));
        Assert.Equal("effects[0].when", error.Field);
    }
}
