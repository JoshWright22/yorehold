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
    }
}
