using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

public class StoryImportTests
{
    // a pitch as plain text: two numbered places, the first with its passage
    private const string Pitch = "THE OLD MILL\n\n1: THE RIVER ROAD\n\nThe road to the mill runs along the river.\n\n2: THE MILL\n\nThe wheel turns though the race is dry.\n";

    private sealed class Answers : IStoryModel
    {
        private readonly string _answer;

        public Answers(string answer) => _answer = answer;

        public Task<StoryAnswer> Ask(StoryRequest request, CancellationToken cancel = default) => Task.FromResult(new StoryAnswer(_answer, 10, 5));
    }

    private static string WritePitch(Scratch scratch)
    {
        string book = Path.Combine(scratch.Folder, "Old_Mill.txt");
        File.WriteAllText(book, Pitch);
        return book;
    }

    [Fact]
    public async Task WithoutAModelTheLayoutsDraftIsReviewedAndBuilt()
    {
        using var scratch = new Scratch();
        string package = StoryImport.FolderFor(Path.Combine(scratch.Folder, "create"), WritePitch(scratch));
        Assert.EndsWith("old-mill", package);
        var import = new StoryImport(package, TestContent.Shipped());
        await import.Read(WritePitch(scratch), null);
        Assert.Equal(2, import.Outline!.OfKind(OutlineKind.Place).Count());
        Assert.True(StoryImport.IsImport(package));
        Assert.Empty(import.Build());
        Assert.True(File.Exists(Path.Combine(package, "content.json")));
        Assert.Equal("Built", import.Stage);
    }

    [Fact]
    public async Task WhatTheWriterDropsIsLeftOutWithWhatHangsOnIt()
    {
        using var scratch = new Scratch();
        string package = Path.Combine(scratch.Folder, "mill");
        // a model that adds a fight in the mill
        string fight = "{\"entries\": [{\"id\": \"rats\", \"kind\": \"encounter\", \"data\": {\"place\": \"place-2-the-mill\", \"creatures\": [{\"creature\": \"goblin\"}]}, \"from\": \"invented\"}]}";
        var import = new StoryImport(package, TestContent.Shipped());
        await import.Read(WritePitch(scratch), new Answers(fight));
        Assert.NotNull(import.Outline!.Find("rats"));
        Assert.Contains("calls", File.ReadAllText(Path.Combine(package, "import", StoryImport.ModelLogFile)));

        import.SetDropped("place-2-the-mill", true);
        // the choice is kept with the import, so the review can be left and opened again
        StoryImport again = StoryImport.Open(package, TestContent.Shipped());
        Assert.Contains("place-2-the-mill", again.Dropped);
        Assert.Empty(again.Build());
        Chapter chapter = Chapter.Load(Play(package), "chapters/chapter-one");
        Assert.Empty(chapter.Encounters);
        Assert.DoesNotContain("The wheel turns", File.ReadAllText(Path.Combine(package, "chapters", "chapter-one", "map.json")) + string.Join(" ", chapter.Intro));
    }

    private static ContentFiles Play(string package)
    {
        ContentFiles files = TestContent.Shipped();
        files.Add(package);
        return files;
    }
}
