namespace Yorehold.Rules;

/// <summary>What many played-out runs of one encounter came to.</summary>
public sealed record FightForecast(int Fights, int Won, int LostAHero, int HeroesDead, double AverageRounds, int Unfinished)
{
    /// <summary>The share of fights the party won, 0 to 1.</summary>
    public double WinChance => Fights == 0 ? 0 : (double)Won / Fights;
    /// <summary>The share of fights that ended with a hero down or dead.</summary>
    public double HeroLossChance => Fights == 0 ? 0 : (double)LostAHero / Fights;

    /// <summary>One line for Create and the log: "this fight: won 9 in 10, 1 in 5 lose a hero, 3 rounds".</summary>
    public string Summary()
    {
        if (Fights == 0)
        {
            return "this fight: no party to play it with";
        }
        string lost = LostAHero == 0 ? "no hero lost" : $"{InN(HeroLossChance)} lose a hero";
        return $"this fight: won {InN(WinChance)}, {lost}, {AverageRounds:0.#} rounds";
    }

    /// <summary>
    /// What the forecast says about the fight for a party: too hard when it wins less often than
    /// wantWin or loses a hero more often than mostLoss, too easy when it always wins in a round
    /// or two without a scratch. Empty when it fits, or nothing was played.
    /// </summary>
    public string Verdict(double wantWin = 0.75, double mostLoss = 0.35)
    {
        if (Fights == 0 || Fights == Unfinished)
        {
            return "";
        }
        if (WinChance < wantWin || HeroLossChance > mostLoss)
        {
            return "too hard for this party: take a creature out, or give it a weaker one";
        }
        if (WinChance >= 0.995 && LostAHero == 0 && AverageRounds <= 2)
        {
            return "too easy for this party: add a creature, or a stronger one";
        }
        return "";
    }

    // 0.2 as "1 in 5", 0.9 as "9 in 10": the plainest fraction near it
    private static string InN(double share)
    {
        if (share >= 0.995)
        {
            return "always";
        }
        if (share <= 0.005)
        {
            return "never";
        }
        foreach (int n in new[] { 2, 3, 4, 5, 10, 20, 50, 100 })
        {
            int k = (int)Math.Round(share * n);
            if (k >= 1 && Math.Abs((double)k / n - share) <= 0.025)
            {
                return $"{k} in {n}";
            }
        }
        return $"{share * 100:0}%";
    }
}

/// <summary>
/// The rules evaluator's fights: an encounter played out many times with the AI on both sides
/// and every roll from a seed, so the same chapter and seeds always give the same forecast. The
/// adventure builder and Create read it to judge whether a fight suits the party.
/// </summary>
public static class FightSimulation
{
    // Long enough for any fight the game ships; one that runs past it counts as unfinished.
    private const double MostSeconds = 3600;
    // Coarser than a screen's frame: the AI's pauses and walks only need to pass, not be seen.
    private const double Frame = 1.0 / 10;

    /// <summary>
    /// Plays encounter group fights times, each in a fresh world from load(seed); with leaveOut,
    /// the group's last that many creatures stand aside, to see the fight without them.
    /// </summary>
    public static FightForecast Forecast(Func<ulong, World> load, int group, int fights, ulong firstSeed = 1, int leaveOut = 0)
    {
        int won = 0, lostAHero = 0, dead = 0, unfinished = 0, rounds = 0;
        for (int i = 0; i < fights; i++)
        {
            World w = load(firstSeed + (ulong)i);
            if (w.HeroCount == 0 || group < 0 || group >= w.Chapter.Encounters.Count)
            {
                // no party to play it with, or no such fight: nothing to forecast
                return new FightForecast(0, 0, 0, 0, 0, 0);
            }
            foreach (int aside in Members(w, group).TakeLast(leaveOut))
            {
                // a bystander for this run: not on the foes' side, so not in the fight
                w.Creatures[aside].Team = 2;
            }
            PlayOut(w, group, out bool over);
            if (!over)
            {
                unfinished++;
                continue;
            }
            won += w.PartyWiped ? 0 : 1;
            IEnumerable<CharacterSheet> heroes = w.Creatures.Take(w.HeroCount).Select(c => c.Sheet);
            lostAHero += heroes.Any(s => s.Down || s.Death.Dead) ? 1 : 0;
            dead += heroes.Count(s => s.Death.Dead);
            rounds += w.Encounter?.Round ?? 0;
        }
        int finished = fights - unfinished;
        return new FightForecast(fights, won, lostAHero, dead, finished == 0 ? 0 : (double)rounds / finished, unfinished);
    }

    /// <summary>
    /// How many of the group's creatures, the last first, to take out so the fight is no longer
    /// too hard for the party, with the forecast that leaves; it keeps at least one. Too easy is
    /// left to the writer, who knows what to add.
    /// </summary>
    public static (int LeaveOut, FightForecast Forecast) Fit(Func<ulong, World> load, int group, int fights, ulong firstSeed = 1)
    {
        int members = Members(load(firstSeed), group).Count;
        for (int leave = 0; ; leave++)
        {
            FightForecast forecast = Forecast(load, group, fights, firstSeed, leave);
            if (forecast.Fights == 0 || !forecast.Verdict().StartsWith("too hard", StringComparison.Ordinal) || leave >= members - 1)
            {
                return (leave, forecast);
            }
        }
    }

    // The group's foes, in the chapter's order.
    private static List<int> Members(World w, int group) =>
        Enumerable.Range(0, w.Creatures.Count).Where(i => w.Creatures[i].Group == group && w.Creatures[i].Team == 1).ToList();

    /// <summary>One run: the party stood near the group, everyone played by the AI, to the end.</summary>
    public static void PlayOut(World w, int group, out bool over)
    {
        w.Options.AutoPlay = true;
        w.Options.ReactionPrompts = false;
        // an opening cutscene or conversation is not part of the fight
        w.EndCutscene();
        while (w.Talk != null)
        {
            w.EndTalk();
        }
        PlacePartyNear(w, group);
        w.StartFight(group);
        for (double t = 0; t < MostSeconds && w.Fighting; t += Frame)
        {
            w.Update(Frame);
            w.TakeEvents();
        }
        over = !w.Fighting;
    }

    /// <summary>
    /// The heroes on the nearest free squares two or more steps from the group's first creature,
    /// as if they had just walked in. False if there wasn't room for all of them.
    /// </summary>
    public static bool PlacePartyNear(World w, int group)
    {
        int first = w.Creatures.FindIndex(c => c.Group == group && c.Team != 0);
        if (first < 0)
        {
            return false;
        }
        var seen = new HashSet<Cell> { w.CellOf(first) };
        var queue = new Queue<(Cell Cell, int Steps)>();
        queue.Enqueue((w.CellOf(first), 0));
        int hero = 0;
        while (queue.Count > 0 && hero < w.HeroCount)
        {
            (Cell at, int steps) = queue.Dequeue();
            if (steps >= 2 && !w.Occupied(at, -1))
            {
                w.Place(hero++, at);
            }
            foreach (Cell next in w.Grid.Neighbours(at))
            {
                // straight steps only, so no hero is put behind a wall's corner
                if ((next.X == at.X || next.Y == at.Y) && w.Walkable(next) && seen.Add(next))
                {
                    queue.Enqueue((next, steps + 1));
                }
            }
        }
        return hero == w.HeroCount;
    }
}
