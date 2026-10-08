namespace Yorehold.Rules.Tests;

public class ImportScoreTests
{
    // The Old Mill as the reader would leave it: three numbered places with their shaded passages,
    // the ferryman's words, a paragraph nothing is made of, and two pictures.
    private static SourceBook Mill()
    {
        var book = new SourceBook { Title = "The Old Mill", File = "Old_Mill.pdf", BodySize = 10 };
        var page = new SourceBook.Page { Number = 1, Width = 600, Height = 800 };
        void Add(string kind, string text, string box = "") => page.Blocks.Add(new SourceBook.Block { Kind = kind, Text = text, Box = box, Y = page.Blocks.Count * 20, Width = 300, Height = 12 });
        Add("heading", "1: THE RIVER ROAD");
        Add("text", "The road to the mill runs along the river and the party follows it until dusk.", "shaded");
        Add("heading", "2: THE MILL");
        Add("text", "The wheel turns though the race is dry.", "shaded");
        Add("text", "The miller left years ago and the villagers say the place has been empty since, though smoke is seen some nights.");
        Add("heading", "3: THE FAR BANK");
        Add("text", "On the far bank a lantern swings from a pole where the ferryman waits for his fare.", "shaded");
        Add("text", "“Fare first. Then we talk about the mill.”");
        Add("text", "“Nobody crosses after dark, not for any coin.”");
        book.Pages.Add(page);
        book.Pictures.Add(new SourceBook.Picture { File = "pictures/p1-1.png", Page = 1, Width = 100, Height = 100 });
        book.Pictures.Add(new SourceBook.Picture { File = "pictures/p1-2.png", Page = 1, Width = 100, Height = 100 });
        return book;
    }

    private static Outline Sample() => Outline.Parse("outline.json", SampleOutline.Json);

    [Fact]
    public void EachPartCountsWhatTheBookShowsAgainstWhatGotIn()
    {
        ImportScore score = ImportScore.Of(Mill(), Sample(), null, "calls 1", null, null);
        Assert.True(score.ModelRan);
        // Marn has the first picture; nothing uses the second
        ImportScore.Part pictures = score.Find("pictures")!;
        Assert.Equal((1, 2), (pictures.Found, pictures.Of));
        Assert.Contains("p1-2.png", Assert.Single(pictures.Missing));
        Assert.Equal((3, 3), (score.Find("places")!.Found, score.Find("places")!.Of));
        Assert.Equal((3, 3), (score.Find("readout")!.Found, score.Find("readout")!.Of));
        // the ferryman's first line is in his conversation; the second isn't anywhere
        ImportScore.Part spoken = score.Find("spoken")!;
        Assert.Equal((1, 2), (spoken.Found, spoken.Of));
        Assert.Contains("Nobody crosses", Assert.Single(spoken.Missing));
        // all three places have a way given
        Assert.Equal((3, 3), (score.Find("ways")!.Found, score.Find("ways")!.Of));
        // the miller's paragraph is the largest thing left out
        ImportScore.Part words = score.Find("words")!;
        Assert.InRange(words.Found, 1, words.Of - 1);
        Assert.Contains("The miller left", words.Missing[0]);
        Assert.InRange(score.Overall, 1, 99);
    }

    [Fact]
    public void ARunWithNoModelSaysSoFirst()
    {
        Outline draft = BookLayout.Draft(Mill());
        ImportScore score = ImportScore.Of(Mill(), draft, null, "", null, null);
        Assert.False(score.ModelRan);
        Assert.Contains("No story model ran", score.Headline());
        Assert.Equal(0, score.Find("ways")!.Found);
        Assert.Equal(0, score.Find("spoken")!.Found);
        Assert.Contains(score.Find("cast")!.Facts, f => f.Contains("no fights at all"));
    }

    [Fact]
    public void TheMapsShapeIsHeldAgainstWhereTheBookDrawsEachPlace()
    {
        Outline outline = Outline.Parse("outline.json", """
            {"format": "yorehold.outline", "version": 1, "title": "Two rooms", "entries": [
              {"id": "ch", "kind": "chapter", "data": {"title": "Two rooms", "mapPicture": "pictures/p1-1.png"}},
              {"id": "west", "kind": "place", "data": {"name": "West", "label": "1", "size": [6, 6], "mapAt": [0.2, 0.5]}},
              {"id": "east", "kind": "place", "data": {"name": "East", "label": "2", "size": [6, 6], "mapAt": [0.8, 0.5]}}]}
            """);
        var rooms = new Dictionary<string, (string, int, int, int, int)> { ["west"] = ("ch", 20, 1, 6, 6), ["east"] = ("ch", 1, 1, 6, 6) };
        var report = new ImportScore.BuildReport(new(), rooms);
        ImportScore.Part shape = ImportScore.Of(Mill(), outline, report, "", null, new HashSet<string>()).Find("shape")!;
        // level on the map, so only left and right is asked, and the rooms are the wrong way round
        Assert.Equal((0, 1), (shape.Found, shape.Of));
        Assert.Contains("left and right", Assert.Single(shape.Missing));
    }

    [Fact]
    public void AnAnswerKeyCountsTheLinesTheOutlineMeets()
    {
        ImportKey key = ImportKey.Parse("old-mill.key.json", """
            {"format": "yorehold.import-key", "version": 1, "book": "The Old Mill", "notPictures": ["pictures/p1-2.png"],
             "expect": [
               {"place": "2"},
               {"way": ["1", "2"], "how": "locked"},
               {"way": ["2", "3"]},
               {"fight": "2", "creatures": {"rat": 2, "goblin": 1}},
               {"fight": "3"},
               {"hero": "Marn", "class": "fighter", "picture": true},
               {"hero": "Pell", "picture": true},
               {"person": "ferryman", "place": "3", "talks": true},
               {"says": "Fare first", "who": "ferryman"},
               {"says": "Nobody crosses after dark"},
               {"item": "mill key|iron key"},
               {"creature": "goblin"},
               {"chest": "2", "holds": ["healing"], "coins": 30},
               {"chest": "2", "coins": 31},
               {"quest": "fare"}]}
            """);
        List<(string Line, bool Met)> lines = key.Check(Sample());
        Assert.Equal(new[] { true, true, false, true, false, true, false, true, true, false, true, true, true, false, true }, lines.Select(l => l.Met));
        Assert.Equal("a fight in 2: 2 rat, 1 goblin", lines[3].Line);
        Assert.Equal("a way between 2 and 3", lines[2].Line);

        ImportScore score = ImportScore.Of(Mill(), Sample(), null, "calls 1", key, null);
        Assert.Equal((10, 15), (score.Find("key")!.Found, score.Find("key")!.Of));
        // the picture the key says isn't for the game is not counted against the import
        Assert.Equal((1, 1), (score.Find("pictures")!.Found, score.Find("pictures")!.Of));

        ContentException unknown = Assert.Throws<ContentException>(() => ImportKey.Parse("k.json", "{\"format\": \"yorehold.import-key\", \"expect\": [{\"monster\": \"rat\"}]}"));
        Assert.Contains("starts with one of", unknown.Message);
    }

    // Scores an import made before, for a look by hand: YOREHOLD_SCORE_IMPORT is its import folder,
    // YOREHOLD_SCORE_PACKAGE the built package (optional), YOREHOLD_SCORE_KEY a key file (optional).
    // The score goes to score.txt in the import folder. Without the variable nothing runs.
    [Fact]
    public void AnImportNamedInTheEnvironmentIsScoredForALook()
    {
        string? folder = Environment.GetEnvironmentVariable("YOREHOLD_SCORE_IMPORT");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        string? package = Environment.GetEnvironmentVariable("YOREHOLD_SCORE_PACKAGE");
        string? keyFile = Environment.GetEnvironmentVariable("YOREHOLD_SCORE_KEY");
        ImportKey? key = string.IsNullOrEmpty(keyFile) ? null : ImportKey.Parse(Path.GetFileName(keyFile), File.ReadAllText(keyFile));
        ImportScore score = ImportScore.Of(folder, Outline.Load(folder), string.IsNullOrEmpty(package) ? null : package, key);
        File.WriteAllLines(Path.Combine(folder, "score.txt"), score.Lines());
    }

    [Fact]
    public async Task AnImportKeepsItsScoreAndAddsItToTheHistoryWhenBuilt()
    {
        using var scratch = new Scratch();
        string book = Path.Combine(scratch.Folder, "Old_Mill.txt");
        File.WriteAllText(book, "THE OLD MILL\n\n1: THE RIVER ROAD\n\nThe road to the mill runs along the river.\n\n2: THE MILL\n\nThe wheel turns though the race is dry.\n");
        string scores = Path.Combine(scratch.Folder, "import-scores");
        scratch.Write("import-scores/old-mill.key.json", "{\"format\": \"yorehold.import-key\", \"expect\": [{\"place\": \"1\"}, {\"fight\": \"2\"}]}");
        string package = Path.Combine(scratch.Folder, "create", "old-mill");
        var import = new StoryImport(package, TestContent.Shipped()) { ScoresFolder = scores };
        await import.Read(book, null);
        Assert.Equal((1, 2), (import.Score!.Find("key")!.Found, import.Score.Find("key")!.Of));
        Assert.False(import.Score.Built);
        Assert.False(File.Exists(Path.Combine(scores, ImportScore.HistoryFile)));

        Assert.Empty(import.Build());
        Assert.True(import.Score!.Built);
        Assert.Contains("\"overall\"", File.ReadAllText(Path.Combine(package, "import", ImportScore.FileName)));
        Assert.Contains("\"package\":\"old-mill\"", File.ReadAllText(Path.Combine(scores, ImportScore.HistoryFile)).Replace(" ", ""));
        // and an import opened again has its score without a build
        Assert.NotNull(StoryImport.Open(package, TestContent.Shipped(), scores).Score);
    }
}
