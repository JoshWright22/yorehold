using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

/// <summary>Saves: what they keep, that a loaded game rolls like one that never stopped, the checkpoint and bad files. The C++ client's save checks.</summary>
public class WorldSaveTests
{
    private static JsonObject Data(World w) => w.StateJson();

    private static string Text(World w) => w.StateJson().ToJsonString();

    [Fact]
    public void ASaveRoundTripsUnchanged()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/choice-yard", WorldItemTestsFiles(), 5);
        World w = world.World;
        world.Put(0, new Cell(2, 3));
        Assert.True(w.Take(0, 0, all: true));
        w.Creatures[1].Sheet.Hp -= 2;
        w.Creatures[1].Sheet.AddCondition(w.Rules, "frightened", 3, 2);
        Assert.True(w.AddSurface(w.Rules.Surfaces.First().Id, new Cell(5, 5), 1, 4));
        world.SetFlags("met-wren");
        world.Step(0.2);
        string saved = Text(w);

        using WorldFixture other = WorldFixture.LoadJson("chapters/choice-yard", WorldItemTestsFiles(), 9);
        Assert.True(other.World.Restore(saved), other.World.Refusal);
        Assert.Equal(saved, Text(other.World));
        World o = other.World;
        Assert.True(o.Piles[0].Empty && o.Creatures[0].Sheet.Inventory.Any(i => i.Id == "mace") && o.Creatures[1].Sheet.Hp == w.Creatures[1].Sheet.Hp
            && o.Creatures[1].Sheet.ConditionValue("frightened") == 2 && o.Surfaces.Count == 1 && o.Flags.Contains("met-wren"),
            "Piles, gear, wounds, conditions, surfaces and flags come back");
        Assert.True(o.Creatures[1].Sheet.ArmorClass(o.Rules) == w.Creatures[1].Sheet.ArmorClass(w.Rules)
            && o.Creatures[0].Sheet.AttackModifier(o.Rules) == w.Creatures[0].Sheet.AttackModifier(w.Rules), "Worn gear and conditions count the same");
        other.Said("");
        Assert.Contains(other.Events, e => e.Kind == WorldEventKind.Resumed);
    }

    [Fact]
    public void ALoadedGameRollsLikeOneThatNeverStopped()
    {
        using WorldFixture world = WorldCharacterTests.Yard();
        World w = world.World;
        // some dice before the save, so the counters aren't at their start
        Assert.True(w.Rest("short"));
        string saved = Text(w);

        static List<string> PlayOut(WorldFixture f)
        {
            f.Said(""); // takes what came before
            int start = f.Log.Count;
            f.World.Options.AutoPlay = true;
            f.Fight();
            Assert.True(f.StepUntil(() => !f.World.Fighting, 120), "The fight ends");
            f.World.Rest("short");
            f.Said("");
            return f.Log.Skip(start).ToList();
        }

        List<string> straight = PlayOut(world);
        using WorldFixture loaded = WorldCharacterTests.Yard();
        loaded.World.NewAdventure(77); // a different run, put back by the save
        Assert.True(loaded.World.Restore(saved), loaded.World.Refusal);
        Assert.Equal(saved, Text(loaded.World));
        List<string> again = PlayOut(loaded);
        Assert.Equal(straight, again);
        Assert.Equal(Text(w), Text(loaded.World));
        JsonObject data = Data(w);
        Assert.True(data["rolls"]!.GetValue<ulong>() > 0 && data["stealthDice"]!.AsArray().Count == 2, "The dice counters and the stealth dice are saved");
    }

    [Fact]
    public void AWipedPartyGoesBackToTheCheckpoint()
    {
        Dictionary<string, string> files = WorldCharacterTests.YardFiles(50);
        files["rulesets/yorehold/actions/wipe-test.json"] = """{"id":"wipe-test","cost":0,"effects":[{"do":"damage","dice":10000,"target":"allies"}]}""";
        using WorldFixture world = WorldFixture.LoadJson("chapters/choice-yard", files, 5);
        World w = world.World;
        w.Creatures[0].Sheet.Stats.SetBase("dex", 2000); // Ana acts first
        world.Put(0, new Cell(2, 2));
        Assert.True(w.Rest("short")); // asks for a save, which moves the checkpoint
        world.Said("");
        Assert.Contains(world.Events, e => e.Kind == WorldEventKind.Save);
        world.Fight();
        Assert.True(world.Use("wipe-test") && w.PartyWiped && !w.CanSave, "A wiped party can't save");
        w.ReturnFromWipe();
        Assert.True(!w.PartyWiped && !w.Fighting && w.CellOf(0) == new Cell(2, 2) && w.Creatures[0].Sheet.Hp > 0 && world.Said("returns to the autosave"),
            "The party stands where the last save had it");
        Assert.Equal(1, w.RestsUsed["short"]);
    }

    [Fact]
    public void BadSavesChangeNothing()
    {
        using WorldFixture world = WorldCharacterTests.Yard();
        World w = world.World;
        string unchanged = Text(w);

        JsonObject wrongChapter = Data(w);
        wrongChapter["chapterId"] = "elsewhere";
        Assert.True(!w.Restore(wrongChapter.ToJsonString()) && w.Refusal.Contains("another chapter"));

        JsonObject missing = Data(w);
        missing["creatures"]!.AsArray().RemoveAt(2);
        Assert.True(!w.Restore(missing.ToJsonString()) && w.Refusal.Contains("creatures have changed"));

        JsonObject badClass = Data(w);
        badClass["heroes"]![1]!["choices"]!["levels"]![0]!["class"] = "bard";
        Assert.True(!w.Restore(badClass.ToJsonString()) && w.Refusal.Contains("Bo") && w.Refusal.Contains("bard"),
            "A saved character with an unknown class fails, naming it");

        JsonObject badHold = Data(w);
        badHold["creatures"]![0]!["concentration"] = new JsonObject { ["spell"] = "bless", ["holds"] = new JsonArray(new JsonArray(99, "blessed")) };
        Assert.False(w.Restore(badHold.ToJsonString()), "A save holding a spell on a creature that isn't there is refused");

        Assert.False(w.Restore("{not json"));
        Assert.Equal(unchanged, Text(w));
    }

    [Fact]
    public void TheFileKeepsItsFormat()
    {
        using WorldFixture world = WorldCharacterTests.Yard();
        string file = World.SaveFile.Write(world.World.StateJson());
        Assert.Equal(Text(world.World), JsonNode.Parse(World.SaveFile.Read(file))!.ToJsonString());

        string older = """{"format":"yorehold.adventure","version":3,"framework":1,"data":{}}""";
        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => World.SaveFile.Read(older));
        Assert.Contains("older version", refused.Message);

        using var scratch = new Scratch();
        string path = Path.Combine(scratch.Folder, "adventure.json");
        World.SaveFile.WriteFile(path, world.World.StateJson());
        World.SaveFile.WriteFile(path, world.World.StateJson()); // the first one is kept as the .bak
        Assert.True(world.World.Load(path), world.World.Refusal);
        File.WriteAllText(path, "broken");
        Assert.True(world.World.Load(path), "A broken file falls back to the one before it");
        Assert.False(world.World.Load(Path.Combine(scratch.Folder, "none.json")));
    }

    [Fact]
    public void TriggersDontFireTwiceAfterALoad()
    {
        using WorldFixture world = WorldFixture.Load("chapters/trigger-test");
        Assert.True(world.Said("Welcome to the trigger test chapter!"));
        world.Reply(0);
        string saved = Text(world.World);
        using WorldFixture other = WorldFixture.Load("chapters/trigger-test");
        other.Reply(0);
        int talks = other.EventsOf(WorldEventKind.Talk).Count;
        Assert.True(other.World.Restore(saved));
        other.Step(0.1);
        Assert.True(other.EventsOf(WorldEventKind.Talk).Count == talks && other.World.Talk == null, "An onEnter trigger does not fire twice");
    }

    [Fact]
    public void ConcentrationIsSaved()
    {
        using WorldFixture world = WorldCharacterTests.Yard();
        World w = world.World;
        w.Creatures[2].Sheet.AddCondition(w.Rules, "slowed");
        w.Creatures[1].Concentration = Concentration.Restore("mire", new[] { new Concentration.Hold(2, "slowed") });
        w.Creatures[1].MayPrepare = false;
        string saved = Text(w);
        using WorldFixture other = WorldCharacterTests.Yard();
        Assert.True(other.World.Restore(saved), other.World.Refusal);
        WorldCreature bo = other.World.Creatures[1];
        Assert.True(bo.Concentration.Spell == "mire" && bo.Concentration.Holds.SequenceEqual(w.Creatures[1].Concentration.Holds) && !bo.MayPrepare
            && other.World.Creatures[2].Sheet.HasCondition("slowed"), "What a caster holds is saved, and whether they may prepare");
    }

    // the yard with a box of loot beside Ana's start
    private static Dictionary<string, string> WorldItemTestsFiles()
    {
        Dictionary<string, string> files = WorldCharacterTests.YardFiles(50);
        string chapter = files["chapters/choice-yard/chapter.json"].TrimEnd();
        files["chapters/choice-yard/chapter.json"] = chapter[..^1] + """, "containers":[{"id":"box","name":"Box","at":[2,2],"items":["mace"],"coins":30}]}""";
        return files;
    }
}
