namespace Yorehold.Rules.Tests;

public class SystemBenchTests
{
    [Fact]
    public void PassTableRisesWithTheModifierAndFallsWithTheDc()
    {
        CheckKind attack = new CheckRules().Kind(CheckRules.Attack);
        double[,] table = SystemBench.PassTable(attack, new[] { 0, 5 }, new[] { 10, 15 });
        Assert.Equal(0.55, table[0, 0], 3);
        Assert.True(table[1, 0] > table[0, 0]);
        Assert.True(table[0, 1] < table[0, 0]);
    }

    [Fact]
    public void DuelPlaysAHeroOfAClassAgainstACreature()
    {
        using var scratch = new Scratch();
        FightForecast easy = SystemBench.Duel(TestContent.Shipped, "rulesets/dnd5e", "fighter", 5, new[] { "kobold" }, 6, scratch.Folder);
        FightForecast hard = SystemBench.Duel(TestContent.Shipped, "rulesets/dnd5e", "fighter", 1, new[] { "brown-bear", "brown-bear" }, 6, scratch.Folder);
        Assert.Equal(6, easy.Fights);
        Assert.True(easy.WinChance > hard.WinChance, $"{easy.Summary()} against {hard.Summary()}");
    }
}
