namespace Yorehold.Rules.Tests;

public class CheckRulesTests
{
    [Fact]
    public void ASystemSaysWhatATurnIs()
    {
        // No free move, a bonus-action cost, and -5 then -10 on later attacks.
        Ruleset rules = RulesTesting.Rules("""
            {"id": "t", "name": "T", "abilities": [{"id": "str", "name": "Strength"}],
             "actionsPerTurn": 3, "freeMove": false, "attackPenalty": "attacks >= 2 ? -10 : attacks * -5",
             "turnWords": {"action": "move", "actions": "moves", "free": "free action"}}
            """);
        Assert.Equal(("1 move", "3 moves", "free action", "bonus action"), (rules.Words.Cost(1), rules.Words.Cost(3), rules.Words.Cost(0), rules.Words.Bonus));
        Assert.Equal((false, -5, -10), (rules.FreeMove, rules.AttackPenalty!.Whole(n => n == "attacks" ? 1 : null), rules.AttackPenalty.Whole(n => n == "attacks" ? 3 : null)));
        ActionDefinition bonus = ActionDefinition.Read(TestContent.Json("""{"id": "dash", "name": "Dash", "cost": "bonus"}"""), new ActionDefinition.Defaults());
        Assert.True(bonus.CostsBonus && bonus.Cost == 0);
    }

    [Fact]
    public void SavesAndDefencesAreTheSystems()
    {
        Ruleset rules = RulesTesting.Rules("""
            {"id": "t", "name": "T", "abilities": [{"id": "con", "name": "Constitution"}],
             "saves": [{"id": "fortitude", "name": "Fortitude", "ability": "con"}],
             "formulas": {"damageTaken": "immune ? 0 : floor(amount * (resist > 0 ? 0.5 : 1)) * (weak > 0 ? 2 : 1)"}}
            """);
        var sheet = new CharacterSheet { Name = "Ash" };
        sheet.Stats.SetBase("con", 14);
        sheet.Stats.SetBase("resist.fire", 1);
        sheet.Stats.SetBase("weak.cold", 1);
        Assert.Equal((2, true, 5, 20, 7), (sheet.SaveModifier(rules, "fortitude"), rules.IsSave("fortitude"),
            sheet.DamageAfterDefences(rules, 10, "fire"), sheet.DamageAfterDefences(rules, 10, "cold"), sheet.DamageAfterDefences(rules, 7, "acid")));
    }

    [Fact]
    public void TypedBonusesDontStackAndTraitsReachTheFormulas()
    {
        var stats = new StatBlock();
        stats.SetBase("ac", 10);
        stats.AddModifier(new Modifier("ac", ModifierOp.Add, 2, "item"), "a");
        stats.AddModifier(new Modifier("ac", ModifierOp.Add, 1, "item"), "b");
        stats.AddModifier(new Modifier("ac", ModifierOp.Add, -1, "status"), "c");
        stats.AddModifier(new Modifier("ac", ModifierOp.Add, 1), "d");
        Assert.Equal(12, stats.Integer("ac"));

        // finesse: the better of strength and dexterity
        Ruleset rules = RulesTesting.Rules("""
            {"id": "t", "name": "T", "abilities": [{"id": "str", "name": "S"}, {"id": "dex", "name": "D"}],
             "formulas": {"attack": "(trait.finesse ? max(mod.str, mod.dex) : ability) + proficiency + bonus"}}
            """);
        var sheet = new CharacterSheet { Name = "Ash", Weapon = new Weapon("1d6", "", 1, new[] { "finesse" }) };
        sheet.Stats.SetBase("str", 10);
        sheet.Stats.SetBase("dex", 16);
        Assert.Equal(3, sheet.AttackModifier(rules));
    }

    [Fact]
    public void GrantedActionsBelongToWhoeverIsGrantedThem()
    {
        using WorldFixture world = WorldCharacterTests.Yard();
        World w = world.World;
        Assert.DoesNotContain(w.ActionsOf(0), a => a.Id == "surge");
        w.Chapter.Rules.Actions.Add(ActionDefinition.Read(TestContent.Json("""{"id": "surge", "name": "Surge", "general": false}""")));
        w.Creatures[0].Sheet.Granted.Add("surge");
        Assert.Contains(w.ActionsOf(0), a => a.Id == "surge");
        Assert.DoesNotContain(w.ActionsOf(1), a => a.Id == "surge");
    }

    [Fact]
    public void DyingCanBeATrack()
    {
        // Dying 1 (2 on a critical) plus wounded; a hit while down adds 1; dead at 4.
        Ruleset rules = RulesTesting.Rules("""
            {"id": "t", "name": "T", "abilities": [{"id": "con", "name": "Constitution"}],
             "death": {"enabled": true, "track": {"start": "1 + wounded + (critical ? 1 : 0)", "damage": "critical ? 2 : 1",
                       "dc": "10 + dying", "dead": "dying >= 4", "change": {"failure": 1, "success": -1}}}}
            """);
        var sheet = new CharacterSheet { Name = "Ash", Hp = 5 };
        sheet.Stats.SetBase("maxHp", 5);
        sheet.TakeDamage(9, rules, critical: true);
        Assert.Equal(2, sheet.Death.Dying);
        sheet.TakeDamage(1, rules);
        Assert.Equal(3, sheet.Death.Dying);
        sheet.TakeDamage(1, rules);
        Assert.True(sheet.Death.Dead, "Dying 4 is dead");
    }

    [Fact]
    public void ASystemSaysWhatKillsOutright()
    {
        // 5e's massive damage: what is left after dropping to 0 is the hit point maximum or more.
        Ruleset rules = RulesTesting.Rules("""
            {"id": "t", "name": "T", "abilities": [{"id": "con", "name": "Constitution"}],
             "death": {"enabled": true, "dead": "over >= maxHp"}}
            """);
        var sheet = new CharacterSheet { Name = "Ash", Hp = 5 };
        sheet.Stats.SetBase("maxHp", 8);
        sheet.TakeDamage(12, rules);
        Assert.True(sheet.Down && !sheet.Death.Dead, "7 over of 8: dying, not dead");
        sheet.Hp = 5;
        sheet.Death.Clear();
        sheet.TakeDamage(13, rules);
        Assert.True(sheet.Death.Dead, "8 over of 8 kills outright");
    }

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

    [Fact]
    public void AnAttackIsWorthItsAverageDamageByOutcome()
    {
        Assert.Equal((7.5, 12.24, 4.2, 0.667, 0.0), (DiceExpression.Parse("1d8+3")!.Average(), Math.Round(DiceExpression.Parse("4d6kh3")!.Average(), 2),
            Math.Round(DiceExpression.Parse("1d6!")!.Average(), 2), Math.Round(DiceExpression.Parse("2d6s5")!.Average(), 3), DiceExpression.Parse("4dF")!.Average()));

        // +5 against AC 15: half the rolls hit for 7.5, one in twenty is a critical
        var checks = new CheckRules();
        CheckKind attack = checks.Kind(CheckRules.Attack);
        DiceExpression sword = DiceExpression.Parse("1d8+3")!;
        Dictionary<string, double> odds = attack.Odds(5, 15);
        Assert.Equal(4.35, Math.Round(attack.ExpectedDamage(odds, sword, null), 3));
        // a system whose critical doubles the whole damage
        Assert.Equal(4.5, Math.Round(attack.ExpectedDamage(odds, sword, Formula.Parse("(dice + flat) * 2", out _)), 3));

        // the aim's line: the chance to pass, and the critical share where the system has one
        Assert.Equal("55%, 5% critical", attack.OddsLine(odds));
        Assert.Equal("50%", checks.Kind(CheckRules.Check).OddsLine(checks.Kind(CheckRules.Check).Odds(0, 11)));
    }
}
