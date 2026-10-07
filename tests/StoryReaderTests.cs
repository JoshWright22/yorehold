using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

public class StoryReaderTests
{
    // the tests' book as the reader saw it: the title, two paragraphs, the boxed passage and Marn under his picture
    private const string Source = """
    {"format": "yorehold.source", "version": 1, "title": "The Old Mill", "file": "mill.pdf", "bodySize": 10,
     "pages": [{"number": 1, "width": 600, "height": 800, "blocks": [
       {"kind": "heading", "text": "THE OLD MILL", "at": [50, 40, 200, 20]},
       {"kind": "text", "text": "The road to the mill runs along the river and the party follows it until dusk.", "at": [50, 80, 240, 40]},
       {"kind": "text", "box": "shaded", "text": "The wheel turns though the race is dry.", "at": [50, 200, 240, 20]},
       {"kind": "text", "text": "On the far bank a lantern swings from a pole where the ferryman waits for his fare.", "at": [330, 80, 240, 40]},
       {"kind": "heading", "text": "MARN", "at": [330, 330, 60, 16]}]}],
     "pictures": [{"file": "pictures/p1-1.png", "page": 1, "at": [330, 200, 80, 100], "width": 80, "height": 100}]}
    """;

    /// <summary>A model that hands back what the test says, one answer per request, and keeps what it was asked.</summary>
    private sealed class StandIn : IStoryModel
    {
        private readonly Queue<string> _answers;

        public StandIn(params string[] answers) => _answers = new Queue<string>(answers);

        public List<StoryRequest> Asked { get; } = new();

        public Task<StoryAnswer> Ask(StoryRequest request, CancellationToken cancel = default)
        {
            Asked.Add(request);
            return Task.FromResult(new StoryAnswer(_answers.Count > 0 ? _answers.Dequeue() : "{\"entries\": []}", 1000, 200));
        }
    }

    private static string Entries(IEnumerable<string> entries) => "{\"entries\": [" + string.Join(",", entries) + "]}";

    // the sample outline's entries, as a model would send them
    private static List<string> SampleEntries() =>
        ((JsonArray)JsonNode.Parse(SampleOutline.Json)!["entries"]!).Select(e => e!.ToJsonString()).ToList();

    [Fact]
    public async Task WhatTheModelSendsIsCheckedAndBrokenEntriesGoBackOnce()
    {
        List<string> entries = SampleEntries();
        // the link to the mill names the key before the key's entry, which comes last; the rat has no hit points;
        // the ferryman's quote is made up
        string key = entries.First(e => e.Contains("\"id\":\"mill-key\""));
        entries.Remove(key);
        entries.Add(key);
        string rat = entries.First(e => e.Contains("\"id\":\"mill-rat\""));
        entries[entries.IndexOf(rat)] = rat.Replace("\"hp\":4", "\"hp\":0");
        int ferryman = entries.FindIndex(e => e.Contains("\"id\":\"ferryman\""));
        entries[ferryman] = entries[ferryman].Replace("the ferryman waits for his fare", "the ferryman sings");
        var model = new StandIn(Entries(entries), Entries(new[] { rat }));

        StoryReader.Result result = await new StoryReader(model, TestContent.Shipped())
            .Read(SourceBook.Parse("source.json", Source), new Outline { Title = "The Old Mill" });

        Assert.Equal(2, model.Asked.Count);
        Assert.Contains("hp", model.Asked[1].Prompt);
        Assert.Contains("\"id\":\"mill-rat\"", model.Asked[1].Prompt);
        Assert.Empty(result.Dropped);
        Assert.Equal(SampleOutline.Read().Entries.Count, result.Outline.Entries.Count);
        Assert.True(result.Outline.Find("ferryman")!.From.IsInvented, "A quote the book doesn't have makes the entry invented");
        Assert.Contains(result.Notes, n => n.StartsWith("ferryman:", StringComparison.Ordinal));
        Assert.Equal((2, 2000, 400), (result.Calls, result.InputTokens, result.OutputTokens));
        // what it was told: the kinds' schemas, the game's own creatures, and the book with its marks
        Assert.Contains("\"readAloud\"", model.Asked[0].System);
        Assert.Contains("goblin", model.Asked[0].System);
        Assert.Contains("> The wheel turns though the race is dry.", model.Asked[0].Prompt);
        Assert.Contains("pictures/p1-1.png (page 1), set over or under the name MARN", model.Asked[0].Prompt);
    }

    [Fact]
    public async Task AHeroTheModelGaveNoPictureGetsTheOneWithTheirName()
    {
        string marn = "{\"id\": \"marn\", \"kind\": \"hero\", \"data\": {\"name\": \"Marn\", \"class\": \"fighter\"}, \"from\": \"invented\"}";
        StoryReader.Result result = await new StoryReader(new StandIn(Entries(new[] { marn })), TestContent.Shipped())
            .Read(SourceBook.Parse("source.json", Source), new Outline());
        Assert.Equal("pictures/p1-1.png", result.Outline.Find("marn")!.Picture);
    }

    [Fact]
    public async Task AnEntryStillWrongAfterItsSecondChanceIsDropped()
    {
        string bad = "{\"id\": \"cellar\", \"kind\": \"place\", \"data\": {\"name\": \"Cellar\", \"size\": [1, 1]}, \"from\": \"invented\"}";
        var model = new StandIn(Entries(new[] { bad }), Entries(new[] { bad }));
        StoryReader.Result result = await new StoryReader(model, TestContent.Shipped()).Read(SourceBook.Parse("source.json", Source), new Outline());
        StoryReader.Dropped dropped = Assert.Single(result.Dropped);
        Assert.Equal("cellar", dropped.Entry);
        Assert.Contains("size", dropped.Why);
        Assert.Null(result.Outline.Find("cellar"));
    }

    [Fact]
    public async Task TheDraftIsKeptAndAnAnswerThatIsNotJsonIsLeftOut()
    {
        Outline draft = BookLayout.Draft(SourceBook.Parse("source.json", Source));
        var model = new StandIn("Sorry, I can't help with that.");
        StoryReader.Result result = await new StoryReader(model, TestContent.Shipped()).Read(SourceBook.Parse("source.json", Source), draft);
        Assert.Equal(draft.Entries.Count, result.Outline.Entries.Count);
        Assert.Contains(result.Notes, n => n.Contains("not JSON"));
    }

    [Fact]
    public void ABookIsSentInChunksOfWholePages()
    {
        var pages = Enumerable.Range(1, 5).Select(n =>
            $"{{\"number\": {n}, \"width\": 600, \"height\": 800, \"blocks\": [{{\"kind\": \"text\", \"text\": \"{new string('x', 4000)}\", \"at\": [0, 0, 10, 10]}}]}}");
        SourceBook book = SourceBook.Parse("source.json", "{\"format\": \"yorehold.source\", \"version\": 1, \"title\": \"T\", \"file\": \"t.pdf\", \"bodySize\": 10, \"pages\": [" + string.Join(",", pages) + "], \"pictures\": []}");
        var chunks = StoryReader.Chunks(book, 9000);
        Assert.Equal(new[] { "1 to 2", "3 to 4", "5" }, chunks.Select(c => c.Pages));
        Assert.StartsWith("[page 1]", chunks[0].Text);
    }

    [Fact]
    public void AChatRequestCarriesTheSchemaAndAReplyItsTokens()
    {
        var model = new ChatModel("http://127.0.0.1:8765/", "sonnet");
        JsonObject body = model.Body(new StoryRequest("be brief", "hello", "outline_entries", StoryReader.AnswerSchema()));
        Assert.Equal("sonnet", body["model"]!.GetValue<string>());
        Assert.Equal("outline_entries", body["response_format"]!["json_schema"]!["name"]!.GetValue<string>());
        Assert.Equal("user", body["messages"]![1]!["role"]!.GetValue<string>());
        StoryAnswer answer = ChatModel.Read("{\"choices\": [{\"message\": {\"content\": \"{}\"}}], \"usage\": {\"prompt_tokens\": 12, \"completion_tokens\": 3}}");
        Assert.Equal(("{}", 12, 3), (answer.Text, answer.InputTokens, answer.OutputTokens));
        Assert.Throws<IOException>(() => ChatModel.Read("<html>busy</html>"));
    }
}
