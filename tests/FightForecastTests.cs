namespace Yorehold.Rules.Tests;

public class FightForecastTests
{
    [Fact]
    public void AnEncounterPlayedManyTimesGivesTheSameForecast()
    {
        ContentFiles files = TestContent.Shipped();
        World Load(ulong seed) => World.Load(files, "chapters/goblin-keep", seed);
        FightForecast forecast = FightSimulation.Forecast(Load, 0, 6);
        Assert.Equal(6, forecast.Fights);
        Assert.Equal(0, forecast.Unfinished);
        Assert.True(forecast.AverageRounds >= 1, forecast.Summary());
        Assert.InRange(forecast.WinChance, 0, 1);
        // every roll comes from the seed, so the same seeds forecast the same
        Assert.Equal(forecast, FightSimulation.Forecast(Load, 0, 6));

        Assert.Equal("this fight: won 9 in 10, 1 in 5 lose a hero, 3 rounds", new FightForecast(10, 9, 2, 0, 3, 0).Summary());
        Assert.Equal("this fight: won always, no hero lost, 2.5 rounds", new FightForecast(4, 4, 0, 0, 2.5, 0).Summary());

        // what it says about the fight for the party
        Assert.StartsWith("too hard", new FightForecast(10, 5, 6, 1, 4, 0).Verdict());
        Assert.StartsWith("too easy", new FightForecast(10, 10, 0, 0, 1.5, 0).Verdict());
        Assert.Equal("", new FightForecast(10, 9, 2, 0, 3, 0).Verdict());
    }

    [Fact]
    public void AFightTooHardForThePartyIsFittedByTakingFoesOut()
    {
        // one hero against three goblin bosses: the fit takes some out, never all
        using var scratch = new Scratch();
        scratch.Write("chapters/crowd/chapter.json", """
            {"id":"crowd","title":"Crowd","map":"map.json","party":[{"name":"Ana","class":"fighter","at":[1,1]}],
             "encounters":[{"id":"crowd","creatures":[{"creature":"goblin-boss","at":[4,4]},{"creature":"goblin-boss","at":[5,4]},{"creature":"goblin-boss","at":[4,5]}]}]}
            """);
        scratch.Write("chapters/crowd/map.json", """
            {"name":"Room","tiles":{"floor":{"art":"grass"}},"legend":{".":"floor"},"layers":[{"name":"ground","rows":["........","........","........","........","........","........","........","........"]}]}
            """);
        ContentFiles files = TestContent.ShippedWith(scratch);
        World Load(ulong seed) => World.Load(files, "chapters/crowd", seed);
        (int leaveOut, FightForecast fitted) = FightSimulation.Fit(Load, 0, 2);
        Assert.InRange(leaveOut, 1, 2);
        Assert.True(leaveOut == 2 || !fitted.Verdict().StartsWith("too hard"), fitted.Summary());
    }
}
