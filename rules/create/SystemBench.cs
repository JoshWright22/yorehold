using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// The test bench beside Create's system forms (R12b): a rule is judged by playing it. The
/// chance a roll kind passes for each modifier against each DC, and a duel: one hero of a class
/// at a level against some of the system's creatures, played out many times by the game's own AI
/// from seeds, so the same system always gives the same answer.
/// </summary>
public static class SystemBench
{
    /// <summary>The chance of the roll kind passing (its success outcome or better), for every modifier against every DC.</summary>
    public static double[,] PassTable(CheckKind kind, IReadOnlyList<int> modifiers, IReadOnlyList<int> dcs)
    {
        var table = new double[modifiers.Count, dcs.Count];
        for (int m = 0; m < modifiers.Count; m++)
        {
            for (int d = 0; d < dcs.Count; d++)
            {
                table[m, d] = kind.ChanceToPass(modifiers[m], dcs[d], Advantage.None);
            }
        }
        return table;
    }

    /// <summary>The damage an attack of the roll kind deals on average, for every modifier against every DC, with the system's own critical rule.</summary>
    public static double[,] DamageTable(CheckRules checks, string kindId, DiceExpression damage, IReadOnlyList<int> modifiers, IReadOnlyList<int> dcs)
    {
        CheckKind kind = checks.Kind(kindId);
        var table = new double[modifiers.Count, dcs.Count];
        for (int m = 0; m < modifiers.Count; m++)
        {
            for (int d = 0; d < dcs.Count; d++)
            {
                table[m, d] = kind.ExpectedDamage(kind.Odds(modifiers[m], dcs[d]), damage, checks.CriticalDamage);
            }
        }
        return table;
    }

    /// <summary>
    /// One hero of heroClass at level against the creatures (ids in the system), on an open
    /// floor, played out fights times. The bench chapter is written into scratch, a folder of
    /// its own laid over files, which load gives fresh for each fight.
    /// </summary>
    public static FightForecast Duel(Func<ContentFiles> load, string rulesetFolder, string heroClass, int level,
        IReadOnlyList<string> creatures, int fights, string scratch)
    {
        const string chapter = "chapters/system-bench";
        string folder = Path.Combine(scratch, chapter.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);
        var foes = new JsonArray();
        for (int i = 0; i < creatures.Count; i++)
        {
            foes.Add(new JsonObject { ["creature"] = creatures[i], ["at"] = new JsonArray(8, 2 + i) });
        }
        var file = new JsonObject
        {
            ["id"] = "system-bench",
            ["title"] = "Bench",
            ["map"] = "map.json",
            ["ruleset"] = rulesetFolder,
            ["level"] = Math.Clamp(level, 1, 20),
            ["party"] = new JsonArray(new JsonObject { ["name"] = "Hero", ["class"] = heroClass, ["color"] = new JsonArray(220, 90, 80), ["at"] = new JsonArray(3, 3) }),
            ["encounters"] = new JsonArray(new JsonObject { ["id"] = "duel", ["creatures"] = foes }),
        };
        var rows = new JsonArray();
        for (int y = 0; y < 8; y++)
        {
            rows.Add(y == 0 || y == 7 ? new string('#', 12) : "#" + new string('.', 10) + "#");
        }
        var map = new JsonObject
        {
            ["name"] = "Bench",
            ["tiles"] = new JsonObject
            {
                ["floor"] = new JsonObject { ["art"] = "dirt" },
                ["wall"] = new JsonObject { ["art"] = "wall", ["walkable"] = false, ["blocksSight"] = true },
            },
            ["legend"] = new JsonObject { ["."] = "floor", ["#"] = "wall" },
            ["layers"] = new JsonArray(new JsonObject { ["name"] = "ground", ["rows"] = rows }),
        };
        File.WriteAllText(Path.Combine(folder, "chapter.json"), file.ToJsonString());
        File.WriteAllText(Path.Combine(folder, "map.json"), map.ToJsonString());
        return FightSimulation.Forecast(seed =>
        {
            ContentFiles files = load();
            files.Add(scratch);
            return World.Load(files, chapter, seed);
        }, 0, fights);
    }
}
