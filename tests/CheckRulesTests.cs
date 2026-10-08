namespace Yorehold.Rules.Tests;

public class CheckRulesTests
{
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
