namespace Yorehold.Rules.Tests;

public class OutlineTests
{
    private static ContentException Refused(string json) => TestContent.Refused(() => Outline.Parse("outline.json", json));

    private static string With(string entry) =>
        "{\"format\": \"yorehold.outline\", \"version\": 1, \"entries\": [" +
        "{\"id\": \"hall\", \"kind\": \"place\", \"data\": {\"name\": \"Hall\", \"size\": [4, 4]}}, " + entry + "]}";

    [Fact]
    public void TheSampleOutlineReadsAndKnowsWhereEachWordCameFrom()
    {
        Outline outline = SampleOutline.Read();
        Assert.Equal("The Old Mill", outline.Title);
        Assert.Equal(3, outline.OfKind(OutlineKind.Place).Count());
        OutlineEntry marn = outline.Find("marn")!;
        Assert.True(marn.Kind == OutlineKind.Hero && marn.Picture == "pictures/p1-1.png" && marn.From.Page == 1 && marn.From.Quote == "MARN");
        Assert.True(outline.Find("pell")!.From.IsInvented, "An entry the book doesn't have is marked invented");
        Assert.Equal("mill-key", outline.Find("mill-door")!.Text("key"));
        Assert.Equal(new[] { "The wheel turns though the race is dry." }, outline.Find("mill")!.Texts("readAloud"));
    }

    [Fact]
    public void AnOutlineSavesAndLoadsTheSame()
    {
        using var scratch = new Scratch();
        Outline outline = SampleOutline.Read();
        outline.Save(scratch.Folder);
        Outline again = Outline.Load(scratch.Folder);
        Assert.Equal(outline.ToJson(), again.ToJson());
        Assert.Equal(outline.Entries.Count, again.Entries.Count);
    }

    [Fact]
    public void ContentEntriesAreCheckedByTheGamesOwnReaders()
    {
        // the creature reader's own complaint, with the entry's place in the file
        ContentException error = Refused(With("{\"id\": \"rat\", \"kind\": \"creature\", \"data\": {\"hp\": 0}}"));
        Assert.Contains("entries[1].data.hp", error.Message);
        Assert.Contains("is the entry's id", Refused(With("{\"id\": \"rat\", \"kind\": \"creature\", \"data\": {\"id\": \"mouse\"}}")).Message);
        Assert.Contains("data.nodes", Refused(With("{\"id\": \"talk\", \"kind\": \"dialogue\", \"data\": {\"start\": \"a\"}}")).Message);
        Assert.Contains("objectives", Refused(With("{\"id\": \"q\", \"kind\": \"quest\", \"data\": {\"title\": \"Q\"}}")).Message);
    }

    [Fact]
    public void TheOutlinesOwnKindsAreChecked()
    {
        Assert.Contains("kind: is one of", Refused(With("{\"id\": \"x\", \"kind\": \"spaceship\", \"data\": {}}")).Message);
        Assert.Contains("size: is [width, height]", Refused(With("{\"id\": \"y\", \"kind\": \"place\", \"data\": {\"name\": \"Y\", \"size\": [1, 90]}}")).Message);
        Assert.Contains("needs a key or a check", Refused(With("{\"id\": \"l\", \"kind\": \"link\", \"data\": {\"from\": \"hall\", \"to\": \"hall\", \"way\": \"locked\"}}")).Message);
        Assert.Contains("way: is one of", Refused(With("{\"id\": \"l\", \"kind\": \"link\", \"data\": {\"from\": \"hall\", \"to\": \"hall\", \"way\": \"teleport\"}}")).Message);
        Assert.Contains("\"cellar\" is not a place entry", Refused(With("{\"id\": \"e\", \"kind\": \"encounter\", \"data\": {\"place\": \"cellar\", \"creatures\": [{\"creature\": \"goblin\"}]}}")).Message);
        Assert.Contains("\"chat\" is not a dialogue entry", Refused(With("{\"id\": \"n\", \"kind\": \"npc\", \"data\": {\"name\": \"N\", \"place\": \"hall\", \"dialogue\": \"chat\"}}")).Message);
        Assert.Contains("used by two entries", Refused(With("{\"id\": \"hall\", \"kind\": \"note\", \"data\": {\"text\": \"again\"}}")).Message);
        Assert.Contains("pictures/p7-1.png", Refused(With("{\"id\": \"h\", \"kind\": \"hero\", \"data\": {\"name\": \"H\", \"class\": \"fighter\"}, \"picture\": \"../face.png\"}")).Message);
        Assert.Contains("format", Refused("{\"format\": \"other\", \"version\": 1, \"entries\": []}").Message);
        Assert.Contains("newer than this game reads", Refused("{\"format\": \"yorehold.outline\", \"version\": 9, \"entries\": []}").Message);
    }

    [Fact]
    public void EveryKindHasASchemaTheSampleFits()
    {
        ContentNode kinds = ContentNode.Read(TestContent.Shipped(), "import/schemas.json").At("kinds");
        Assert.Equal(Enum.GetValues<OutlineKind>().Select(Outline.KindName).Order(), kinds.Members().Select(m => m.Key).Order());
        foreach (OutlineEntry entry in SampleOutline.Read().Entries)
        {
            ContentNode schema = kinds.At(Outline.KindName(entry.Kind));
            var properties = schema.At("properties").Members().Select(m => m.Key).ToHashSet();
            Assert.True(entry.Data.All(p => properties.Contains(p.Key)), $"{entry.Id}: a field its schema doesn't list");
            Assert.True(schema.Texts("required").All(entry.Data.ContainsKey), $"{entry.Id}: a field its schema requires is missing");
        }
    }

    [Fact]
    public void NamesTheGameMustKnowAreCheckedAgainstItsContent()
    {
        var game = new Compendium();
        game.Load(TestContent.Shipped(), "");
        Assert.Empty(SampleOutline.Read().Check(game));
        Outline wrong = Outline.Parse("outline.json", With(
            "{\"id\": \"h\", \"kind\": \"hero\", \"data\": {\"name\": \"H\", \"class\": \"necromancer\"}}, " +
            "{\"id\": \"e\", \"kind\": \"encounter\", \"data\": {\"place\": \"hall\", \"creatures\": [{\"creature\": \"dragon\"}]}}, " +
            "{\"id\": \"c\", \"kind\": \"container\", \"data\": {\"place\": \"hall\", \"items\": [\"vorpal-sword\"]}}"));
        Assert.Equal(new[] { "h: no class \"necromancer\"", "e: no creature \"dragon\"", "c: no item \"vorpal-sword\"" }, wrong.Check(game));
    }
}
