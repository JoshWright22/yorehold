namespace Yorehold.Rules.Tests;

public class SystemTableTests
{
    [Fact]
    public void ShippedTablesLoad()
    {
        foreach (string system in new[] { "dnd-3.0", "dnd-5e" })
        {
            SystemTable table = SystemTable.Load(TestContent.Shipped(), system);
            Assert.NotEmpty(table.Skills);
        }
        Assert.Equal("athletics", SystemTable.Load(TestContent.Shipped(), "dnd-3.0").Skills["climb"]);
    }

    [Theory]
    [InlineData("Move Silently", "move-silently")]
    [InlineData("Knowledge (arcana)", "knowledge-arcana")]
    [InlineData("open_lock", "open-lock")]
    public void SkillNamesAreWrittenOneWay(string book, string key) => Assert.Equal(key, SystemTable.Key(book));

    [Fact]
    public void TheBuilderMakesTheBooksNumbersTheGames()
    {
        // a book that says "Open Lock DC 20" and gives a rat 4 hit points, with a table that halves
        // hit points and takes 5 off difficulties
        string json = SampleOutline.Json
            .Replace("\"system\": \"\"", "\"system\": \"halving\"")
            .Replace("\"skill\": \"athletics\", \"difficulty\": 12", "\"skill\": \"Open Lock\", \"difficulty\": 20")
            .Replace("\"hp\": 4,", "\"hp\": 8,")
            .Replace("\"next\": \"paid\", \"set\": [\"fare_paid\"]}", "\"set\": [\"fare_paid\"], \"check\": {\"skill\": \"Gibberish\", \"difficulty\": 15, \"success\": \"paid\", \"failure\": \"paid\"}}");
        using var scratch = new Scratch();
        scratch.Write("import/systems/halving.json", """
            {"format": "yorehold.system", "version": 1, "name": "Halving", "skills": {"open lock": "dex"},
             "difficulty": {"scale": 1, "add": -5}, "hitPoints": {"scale": 0.5, "add": 0}}
            """);
        ContentFiles game = TestContent.ShippedWith(scratch);
        string package = Path.Combine(scratch.Folder, "mill");
        var builder = new OutlineBuilder(Outline.Parse("outline.json", json), Path.Combine(package, "import"), game);
        List<string> problems = builder.Build(package);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        var play = TestContent.ShippedWith(scratch);
        play.Add(package);
        Chapter chapter = Chapter.Load(play, "chapters/mill-chapter");
        Assert.Contains(chapter.Map.Objects, o => o.Lock is { Dc: 15, Skill: "dex" });
        Assert.Equal(4, chapter.Compendium.Creature("mill-rat")!.Hp);
        Assert.Contains(builder.Report, l => l.Entry == "ferryman-talk" && l.Text.Contains("\"gibberish\""));
        Assert.Contains("\"skill\": \"perception\"", File.ReadAllText(Path.Combine(package, "chapters", "mill-chapter", "dialogue", "ferryman-talk.json")));
    }

    [Fact]
    public void AnUnknownSystemStopsTheBuild()
    {
        using var scratch = new Scratch();
        string json = SampleOutline.Json.Replace("\"system\": \"\"", "\"system\": \"gurps\"");
        List<string> problems = new OutlineBuilder(Outline.Parse("outline.json", json), scratch.Folder, TestContent.Shipped()).Build(Path.Combine(scratch.Folder, "p"));
        Assert.Contains("gurps", Assert.Single(problems));
    }
}
