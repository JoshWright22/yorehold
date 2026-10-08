using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

public class OutlineBuilderTests
{
    // the sample outline built into a package in scratch, with the book's one picture in its import folder
    private static (string Package, OutlineBuilder Builder, List<string> Problems) Build(Scratch scratch, string json = SampleOutline.Json)
    {
        string package = Path.Combine(scratch.Folder, "old-mill");
        string import = Path.Combine(package, "import");
        Directory.CreateDirectory(Path.Combine(import, "pictures"));
        File.WriteAllBytes(Path.Combine(import, "pictures", "p1-1.png"), PaperGroundTests.Figure(235));
        var builder = new OutlineBuilder(Outline.Parse("outline.json", json), import, TestContent.Shipped());
        return (package, builder, builder.Build(package));
    }

    private static ContentFiles Play(string package)
    {
        ContentFiles files = TestContent.Shipped();
        files.Add(package);
        return files;
    }

    [Theory]
    [InlineData("rulesets/dnd5e", "dnd5e")]
    [InlineData("rulesets/pf2e", "pf2e")]
    public void TheSampleBuildsForAnotherSystem(string system, string id)
    {
        using var scratch = new Scratch();
        string package = Path.Combine(scratch.Folder, "old-mill");
        string import = Path.Combine(package, "import");
        Directory.CreateDirectory(Path.Combine(import, "pictures"));
        File.WriteAllBytes(Path.Combine(import, "pictures", "p1-1.png"), PaperGroundTests.Figure(235));
        var builder = new OutlineBuilder(Outline.Parse("outline.json", SampleOutline.Json), import, TestContent.Shipped(), system);
        List<string> problems = builder.Build(package);
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // the chapter names the chosen system, loads under it, and its fight plays out
        using WorldFixture world = WorldFixture.LoadFrom(Play(package), "chapters/mill-chapter", 3);
        World w = world.World;
        Assert.Equal(id, w.Rules.Id);
        Assert.All(w.Creatures.Take(w.HeroCount), c => Assert.NotNull(w.Chapter.Compendium.Class(c.Choices!.Levels[0].ClassId)));
        w.Options.AutoPlay = true;
        while (w.Talk != null)
        {
            w.EndTalk();
        }
        Assert.True(FightSimulation.PlacePartyNear(w, 0));
        world.Fight(0);
        Assert.True(world.StepUntil(() => !w.Fighting, 3600), string.Join("\n", world.Log.TakeLast(20)));
    }

    [Fact]
    public void TheSampleBuildsIntoAPackageThatLoads()
    {
        using var scratch = new Scratch();
        (string package, OutlineBuilder builder, List<string> problems) = Build(scratch);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        foreach (string file in new[] { "content.json", "adventure.json", "story.json", "creatures/mill-rat.json", "items/mill-key.json",
            "chapters/mill-chapter/chapter.json", "chapters/mill-chapter/map.json", "chapters/mill-chapter/quests.json",
            "chapters/mill-chapter/dialogue/ferryman-talk.json", "pictures/p1-1.png", "import/report.json" })
        {
            Assert.True(File.Exists(Path.Combine(package, file)), file + " was written");
        }

        Chapter chapter = Chapter.Load(Play(package), "chapters/mill-chapter");
        Assert.Equal("The Old Mill", chapter.Title);
        Assert.Equal(new[] { "Marn", "Pell" }, chapter.Party.Select(p => p.Name));
        Assert.True(chapter.Party[0].Image == "pictures/p1-1.png", "The book's hero keeps their own picture");
        byte[] face = File.ReadAllBytes(Path.Combine(package, "pictures", "p1-1.png"));
        Assert.Equal(0, StbImageSharp.ImageResult.FromMemory(face, StbImageSharp.ColorComponents.RedGreenBlueAlpha).Data[3]);
        EncounterGroup rats = Assert.Single(chapter.Encounters);
        Assert.Equal(new[] { "mill-rat", "mill-rat", "goblin" }, rats.Creatures.Select(c => c.CreatureId));
        Assert.Equal("Snag", rats.Creatures[2].Name);
        Assert.Single(chapter.Npcs);
        Assert.Contains(chapter.Containers, c => c.Name == "Flour chest");
        Assert.Contains("The road to the mill runs along the river and the party follows it until dusk.", chapter.Intro);
        Assert.Single(chapter.Intro);

        // the locked door between the road and the mill opens with the mill key or a strength check
        Assert.Contains(chapter.Map.Objects, o => o.Door is { Locked: true } && o.Tags.Contains("key:mill-key") && o.Lock is { Dc: 12, Skill: "athletics" });
        // the book's notes, the passages read in rooms and what the game can't play are in the report
        string report = File.ReadAllText(Path.Combine(package, "import", "report.json"));
        Assert.Contains("lantern", report);
        Assert.DoesNotContain(builder.Report, line => line.Text.Contains("no walk from the start"));
        // the mill's and the bank's passages are read out when a hero first steps in
        Assert.Equal(new[] { "room-mill", "room-bank" }, chapter.Map.Areas.Select(a => a.Id));
        Assert.Contains(chapter.Triggers, t => t.Id == "read-mill" && t.When.SequenceEqual(new[] { "entered_mill" }));
    }

    [Fact]
    public void ARoomsPassageIsReadBeforeTheFightWhenItsFoesSeeThePartyFromTheDoor()
    {
        using var scratch = new Scratch();
        string package = Build(scratch).Package;
        using WorldFixture world = WorldFixture.LoadFrom(Play(package), "chapters/mill-chapter", 3);
        World w = world.World;
        while (w.Talk != null)
        {
            w.EndTalk();
        }
        // nobody has stepped into the mill, and its rats notice the party
        Assert.DoesNotContain("entered_mill", w.Flags);
        w.Notice(0);
        Assert.True(w.Flags.Contains("entered_mill") && w.Talk != null, "The room is told first");
        Assert.False(w.Fighting, "... and the fight waits for it");
        w.EndTalk();
        w.Notice(0);
        Assert.True(w.Fighting && w.Talk == null, "Seen again with the passage read, the fight starts");
    }

    [Fact]
    public void ARoomsPassageIsReadWhenAHeroStepsIn()
    {
        using var scratch = new Scratch();
        string package = Build(scratch).Package;
        using WorldFixture world = WorldFixture.LoadFrom(Play(package), "chapters/mill-chapter", 3);
        World w = world.World;
        while (w.Talk != null)
        {
            w.EndTalk();
        }
        MapArea bank = w.Chapter.Map.Areas.First(a => a.Id == "room-bank");
        world.Step(1.0 / 60);
        Assert.Null(w.Talk);
        w.Place(0, new Cell(bank.X + 1, bank.Y + 1));
        world.Step(1.0 / 60);
        Assert.True(w.Flags.Contains("entered_bank"), "Stepping into the room sets its flag");
        Assert.True(w.Talk != null, "... and its passage opens");
        w.EndTalk();
        w.Place(0, new Cell(bank.X + 2, bank.Y + 1));
        world.Step(1.0 / 60);
        Assert.True(w.Talk == null, "It is read only once");
    }

    [Fact]
    public void AMapAreaIsChecked()
    {
        string Map(string area) => "{\"tiles\": {\"floor\": {\"art\": \"stone\"}}, \"legend\": {\".\": \"floor\"}, \"layers\": [{\"name\": \"ground\", \"rows\": [\"....\", \"....\"]}], \"areas\": [" + area + "]}";
        GameMap map = GameMap.Read(ContentNode.Parse("map.json", Map("{\"id\": \"a\", \"area\": [1, 0, 2, 2], \"set\": [\"in_a\"]}")), new Dictionary<string, Kit>());
        Assert.True(map.Areas[0].Holds(new Cell(2, 1)) && !map.Areas[0].Holds(new Cell(0, 0)));
        Assert.Contains("inside the map", TestContent.Refused(() => GameMap.Read(ContentNode.Parse("map.json", Map("{\"id\": \"a\", \"area\": [3, 0, 2, 2], \"set\": [\"x\"]}")), new Dictionary<string, Kit>())).Message);
        Assert.Contains("at least one flag", TestContent.Refused(() => GameMap.Read(ContentNode.Parse("map.json", Map("{\"id\": \"a\", \"area\": [0, 0, 1, 1]}")), new Dictionary<string, Kit>())).Message);
    }

    [Fact]
    public void TheLibraryListsTheGamesAdventureAndEveryPackage()
    {
        using var scratch = new Scratch();
        string package = Build(scratch).Package;
        Outline.Parse("outline.json", SampleOutline.Json).Save(Path.Combine(package, "import"));
        Directory.CreateDirectory(Path.Combine(scratch.Folder, "broken"));
        File.WriteAllText(Path.Combine(scratch.Folder, "broken", "adventure.json"), "{\"chapters\": []}");

        List<AdventureListing> listed = AdventureLibrary.List(TestContent.AssetsFolder(), scratch.Folder);
        Assert.Equal(AdventureLibrary.Game, listed[0].Source);
        AdventureListing mill = listed.Single(l => l.Package == package);
        Assert.Equal(("The Old Mill", AdventureLibrary.Imported, "pictures/p1-1.png"), (mill.Name, mill.Source, mill.Adventure!.Cover));
        AdventureListing broken = listed.Single(l => l.Name == "broken");
        Assert.True(broken.Adventure == null && broken.Problem.Contains("at least one chapter"), "One that can't be read is listed with why");
        Assert.True(AdventureLibrary.FilesOf(TestContent.AssetsFolder(), package).Exists("chapters/mill-chapter/chapter.json"));
    }

    [Fact]
    public void ASaveRemembersTheAdventuresPackage()
    {
        using var scratch = new Scratch();
        string package = Build(scratch).Package;
        using WorldFixture world = WorldFixture.LoadFrom(Play(package), "chapters/mill-chapter");
        world.World.Package = package;
        string path = Path.Combine(scratch.Folder, "save.json");
        World.SaveFile.WriteFile(path, world.World.StateJson());

        SaveSummary summary = SaveSummary.Read(path, TestContent.Shipped());
        Assert.Equal(("", package, "The Old Mill"), (summary.Problem, summary.Package, summary.ChapterTitle));
        Directory.Delete(package, true);
        Assert.Contains("is gone", SaveSummary.Read(path, TestContent.Shipped()).Problem);
    }

    [Fact]
    public void TheSameOutlineGivesTheSameFiles()
    {
        using var first = new Scratch();
        using var second = new Scratch();
        string a = Build(first).Package, b = Build(second).Package;
        foreach (string file in Directory.GetFiles(a, "*.json", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(a, file);
            Assert.True(File.ReadAllText(file) == File.ReadAllText(Path.Combine(b, relative)), relative + " differs between two builds");
        }
    }

    [Fact]
    public void TheBuiltFightPlaysOut()
    {
        using var scratch = new Scratch();
        string package = Build(scratch).Package;
        using WorldFixture world = WorldFixture.LoadFrom(Play(package), "chapters/mill-chapter", 3);
        World w = world.World;
        w.Options.AutoPlay = true;
        while (w.Talk != null)
        {
            w.EndTalk();
        }
        // the party stood in the mill with the rats, as if the door were already open
        int rat = w.Creatures.FindIndex(c => c.Team == 1);
        var seen = new HashSet<Cell> { w.CellOf(rat) };
        var queue = new Queue<Cell>(new[] { w.CellOf(rat) });
        int hero = 0;
        while (queue.Count > 0 && hero < w.HeroCount)
        {
            Cell at = queue.Dequeue();
            if (!w.Occupied(at, -1))
            {
                w.Place(hero++, at);
            }
            foreach (Cell next in w.Grid.Neighbours(at).Where(n => (n.X == at.X || n.Y == at.Y) && w.Walkable(n) && seen.Add(n)))
            {
                queue.Enqueue(next);
            }
        }
        world.Fight(0);
        Assert.True(world.StepUntil(() => !w.Fighting, 3600), string.Join("\n", world.Log.TakeLast(20)));
        Assert.Single(world.EventsOf(WorldEventKind.FightOver));
    }

    [Fact]
    public void ACreatureTheGameHasByNameIsUsedNotCopied()
    {
        using var scratch = new Scratch();
        string json = SampleOutline.Json.Replace("\"name\": \"Mill rat\"", "\"name\": \"Goblin\"");
        (string package, OutlineBuilder builder, List<string> problems) = Build(scratch, json);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.False(File.Exists(Path.Combine(package, "creatures", "mill-rat.json")));
        Chapter chapter = Chapter.Load(Play(package), "chapters/mill-chapter");
        Assert.All(chapter.Encounters[0].Creatures, c => Assert.Equal("goblin", c.CreatureId));
        Assert.Contains(builder.Report, line => line.Entry == "mill-rat" && line.Text.Contains("game's own Goblin"));
    }

    [Fact]
    public void RoomsAreLaidSideBySideWithAWallBetween()
    {
        using var scratch = new Scratch();
        string package = Build(scratch).Package;
        var map = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(package, "chapters", "mill-chapter", "map.json")))!;
        List<string> rows = ((JsonArray)map["layers"]![0]!["rows"]!).Select(r => r!.GetValue<string>()).ToList();
        // the road is 10 by 6 of grass with trees round it, the mill 8 by 8 of floor, the bank 6 by 6;
        // the two ways off the road are cut through the wall in the road's own ground
        Assert.Equal(10 * 6 + 6 * 6 + 2, rows.Sum(r => r.Count(c => c == ',')));
        Assert.Equal(8 * 8, rows.Sum(r => r.Count(c => c == '.')));
        Assert.True(rows.All(r => r.Length == rows[0].Length), "every row is as long as the first");
        Assert.Equal(new[] { "start" }, ((JsonObject)map["markers"]!).Select(m => m.Key));
    }

    [Fact]
    public void NamesTheGameDoesntKnowStopTheBuild()
    {
        using var scratch = new Scratch();
        (_, _, List<string> problems) = Build(scratch, SampleOutline.Json.Replace("\"class\": \"cleric\"", "\"class\": \"bard\""));
        Assert.Equal(new[] { "pell: no class \"bard\"" }, problems);
    }

    [Theory]
    [InlineData("The Old Mill", "the-old-mill")]
    [InlineData("  Caves of Shadow (3.0)!  ", "caves-of-shadow-3-0")]
    [InlineData("???", "imported")]
    public void TitlesBecomeIds(string title, string id) => Assert.Equal(id, OutlineBuilder.Slug(title));
}
