using System.Text;

namespace Yorehold.Rules.Tests;

/// <summary>
/// A World for tests, with no window: load a chapter from the shipped content or from text written
/// in the test, call into it, step time and read what it said. Small() builds a whole chapter from
/// a few map rows.
/// </summary>
public sealed class WorldFixture : IDisposable
{
    // The party a Small map's letters stand for, in seat order.
    private static readonly (char Letter, string Name, string Class)[] Seats =
    {
        ('A', "Ana", "fighter"), ('B', "Bo", "rogue"), ('C', "Cy", "cleric"), ('D', "Di", "wizard"),
    };

    private readonly Scratch? _scratch;

    private WorldFixture(World world, Scratch? scratch)
    {
        World = world;
        _scratch = scratch;
        TakeEvents();
    }

    public World World { get; }
    /// <summary>Every log line so far.</summary>
    public List<string> Log { get; } = new();
    /// <summary>Every event so far, log lines included.</summary>
    public List<WorldEvent> Events { get; } = new();

    /// <summary>A shipped chapter folder ("chapters/goblin-keep").</summary>
    public static WorldFixture Load(string folder, ulong seed = 1)
    {
        return new WorldFixture(World.Load(TestContent.Shipped(), folder, seed), null);
    }

    /// <summary>A chapter written in the test: paths to their text, laid over the shipped content so it can use its classes and creatures.</summary>
    public static WorldFixture LoadJson(string folder, IReadOnlyDictionary<string, string> files, ulong seed = 1)
    {
        var scratch = new Scratch();
        foreach (KeyValuePair<string, string> file in files)
        {
            scratch.Write(file.Key, file.Value);
        }
        try
        {
            return new WorldFixture(World.Load(TestContent.ShippedWith(scratch), folder, seed), scratch);
        }
        catch
        {
            scratch.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A chapter from map rows: '.' floor, '#' wall, ',' floor under a roof, 'A' to 'D' the party
    /// (a fighter, a rogue, a cleric and a wizard) and 'g' a goblin, all goblins in one encounter.
    /// chapter and map add fields to the two files ("\"triggers\": [...]"); files adds more files
    /// under the chapter's folder (chapters/small/).
    /// </summary>
    public static WorldFixture Small(string[] rows, string chapter = "", string map = "", ulong seed = 1,
        IReadOnlyDictionary<string, string>? files = null)
    {
        var party = new SortedDictionary<int, string>();
        var goblins = new List<string>();
        var cleaned = new List<string>();
        for (int y = 0; y < rows.Length; y++)
        {
            var row = new StringBuilder();
            for (int x = 0; x < rows[y].Length; x++)
            {
                char c = rows[y][x];
                int seat = Array.FindIndex(Seats, s => s.Letter == c);
                if (seat >= 0)
                {
                    party[seat] = $"{{\"name\": \"{Seats[seat].Name}\", \"class\": \"{Seats[seat].Class}\", \"at\": [{x}, {y}]}}";
                }
                else if (c == 'g')
                {
                    goblins.Add($"{{\"creature\": \"goblin\", \"name\": \"Gik {goblins.Count + 1}\", \"at\": [{x}, {y}]}}");
                }
                row.Append(seat >= 0 || c == 'g' ? '.' : c);
            }
            cleaned.Add(row.ToString());
        }
        // Seats go in letter order, whatever order they appear on the map.
        string encounters = goblins.Count == 0 ? "[]"
            : $"[{{\"id\": \"goblins\", \"text\": \"Goblins!\", \"set\": [\"goblins-done\"], \"creatures\": [{string.Join(", ", goblins)}]}}]";
        string chapterJson = $"{{\"id\": \"small\", \"title\": \"Small\", \"party\": [{string.Join(", ", party.Values)}], \"encounters\": {encounters}"
            + (chapter.Length > 0 ? ", " + chapter : "") + "}";
        string mapJson = "{\"name\": \"Small\", \"tiles\": {\"floor\": {\"art\": \"stone\"}, \"wall\": {\"art\": \"wall\", \"walkable\": false, \"blocksSight\": true}, "
            + "\"roofed\": {\"art\": \"wood\", \"indoors\": true}}, \"legend\": {\".\": \"floor\", \"#\": \"wall\", \",\": \"roofed\"}, "
            + $"\"layers\": [{{\"name\": \"ground\", \"rows\": [{string.Join(", ", cleaned.Select(r => "\"" + r + "\""))}]}}]"
            + (map.Length > 0 ? ", " + map : "") + "}";
        var all = new Dictionary<string, string>
        {
            ["chapters/small/chapter.json"] = chapterJson,
            ["chapters/small/map.json"] = mapJson,
        };
        foreach (KeyValuePair<string, string> file in files ?? new Dictionary<string, string>())
        {
            all["chapters/small/" + file.Key] = file.Value;
        }
        return LoadJson("chapters/small", all, seed);
    }

    /// <summary>Moves time on in frames of 1/60 s, as the game would.</summary>
    public void Step(double seconds)
    {
        const double frame = 1.0 / 60;
        for (double t = 0; t < seconds - 1e-9; t += frame)
        {
            World.Update(frame);
            TakeEvents();
        }
    }

    /// <summary>Steps until done holds or limitSeconds have passed; true if it held.</summary>
    public bool StepUntil(Func<bool> done, double limitSeconds)
    {
        const double frame = 1.0 / 60;
        for (double t = 0; t < limitSeconds; t += frame)
        {
            if (done())
            {
                return true;
            }
            Step(frame);
        }
        return done();
    }

    public bool Said(string text) => Log.Any(line => line.Contains(text, StringComparison.Ordinal));

    /// <summary>The events of one kind so far, by their text.</summary>
    public List<string> EventsOf(WorldEventKind kind) => Events.Where(e => e.Kind == kind).Select(e => e.Text).ToList();

    public bool Walking => World.Tokens.Tokens.Any(t => t.Path.Count > 0);

    /// <summary>Puts a creature straight onto a cell.</summary>
    public void Put(int creature, Cell cell)
    {
        World.Place(creature, cell);
    }

    public int ObjectOn(Cell cell) => World.Map.ObjectAt(cell)?.Id ?? 0;

    public bool Go(int hero, Cell to) => After(World.Go(hero, to));

    public bool Interact(int hero, int objectId) => After(World.Interact(hero, objectId));

    public bool Sneak(bool on) => After(World.Sneak(on));

    /// <summary>Starts the fight with an encounter where everyone stands.</summary>
    public void Fight(int group = 0)
    {
        World.StartFight(group);
        TakeEvents();
    }

    public bool Use(string action, int? target = null) => After(World.Use(action, target));

    public bool MoveTo(Cell to) => After(World.MoveTo(to));

    public bool ChooseTurn(int creature) => After(World.ChooseTurn(creature));

    public bool React(bool take) => After(World.React(take));

    /// <summary>Ends turns until it is creature's; false if it never comes.</summary>
    public bool TurnTo(int creature)
    {
        for (int i = 0; i < 8 && World.CurrentCreature != creature; i++)
        {
            if (!Use(World.EndTurnAction))
            {
                return false;
            }
        }
        return World.CurrentCreature == creature;
    }

    /// <summary>Lands a creature's walk at once.</summary>
    public void FinishWalk(int creature)
    {
        Token token = World.Tokens.Tokens[creature];
        if (token.Path.Count > 0)
        {
            token.Position = token.Path[^1];
        }
        token.Path.Clear();
    }

    public bool HasReaction(int creature) => World.BudgetOf(creature)?.Reaction ?? false;

    public void SetFlags(params string[] flags)
    {
        World.SetFlags(flags);
        TakeEvents();
    }

    public void Dispose()
    {
        _scratch?.Dispose();
    }

    private bool After(bool result)
    {
        TakeEvents();
        return result;
    }

    private void TakeEvents()
    {
        foreach (WorldEvent e in World.TakeEvents())
        {
            Events.Add(e);
            if (e.Kind == WorldEventKind.Log)
            {
                Log.Add(e.Text);
            }
        }
    }
}
