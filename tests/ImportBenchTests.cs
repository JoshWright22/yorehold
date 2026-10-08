using Xunit;
using Yorehold.Rules;

namespace Yorehold.Rules.Tests;

/// <summary>A comparison run of the import: books through versions, judged and held against the run before.</summary>
public class ImportBenchTests
{
    private const string Mill = "THE OLD MILL\n\n1: THE RIVER ROAD\n\nThe road to the mill runs along the river.\n\n2: THE MILL\n\nThe wheel turns though the race is dry.\n";

    [Fact]
    public async Task EveryBookGoesThroughEveryVersionAndIsHeldAgainstTheRunBefore()
    {
        using var scratch = new Scratch();
        scratch.Write("books/Old_Mill.txt", Mill);
        scratch.Write("books/old-mill.key.json", "{\"format\": \"yorehold.import-key\", \"expect\": [{\"place\": \"1\"}, {\"place\": \"2\"}, {\"fight\": \"2\"}, {\"place\": \"3\"}]}");
        var bench = new ImportBench(scratch.Folder, TestContent.Shipped());
        Assert.Equal(new[] { "layout", "full" }, bench.Versions().Select(v => v.Name));

        List<ImportBench.Result> first = await bench.Run("First");
        Assert.Equal(new[] { ("old-mill", "layout"), ("old-mill", "full") }, first.Select(r => (r.Book, r.Version)));
        // two of the key's four lines met
        Assert.All(first, r => Assert.Equal((2, 4), r.Parts["key"]));
        Assert.All(first, r => Assert.InRange(r.Judged, 30, 90));
        Assert.Contains("a fight in 2", first[0].Missed);
        Assert.True(File.Exists(Path.Combine(scratch.Folder, "runs", "first", "old-mill", "full", "adventure.json")));
        string report = File.ReadAllText(Path.Combine(scratch.Folder, "runs", "first", ImportBench.ReportFile));
        Assert.Contains("the first run here", report);
        Assert.Contains("missed place 3", report);

        // the book gains its third place: the next run wins that line, and says so
        scratch.Write("books/Old_Mill.txt", Mill + "\n3: THE LOFT\n\nSacks of old flour lie split on the boards.\n");
        List<ImportBench.Result> second = await bench.Run("second");
        Assert.All(second, r => Assert.True(r.Judged > first[0].Judged));
        report = File.ReadAllText(Path.Combine(scratch.Folder, "runs", "second", ImportBench.ReportFile));
        Assert.Contains("held against first", report);
        Assert.Contains("won   place 3", report);
        Assert.Contains($"+{second[0].Judged - first[0].Judged}", report);
        Assert.Equal(4, bench.History().Count);
        // a label run again takes the place of its earlier run
        await bench.Run("second");
        Assert.Equal(4, bench.History().Count);
    }

    [Fact]
    public void WhatAnImportMakesUpCostsIt()
    {
        ImportKey key = ImportKey.Parse("k.json", "{\"format\": \"yorehold.import-key\", \"expect\": [{\"place\": \"1\"}, {\"fight\": \"1\", \"creatures\": {\"rat\": 1}}, {\"person\": \"Tobb\"}]}");
        Outline outline = Outline.Parse("outline.json", """
            {"format": "yorehold.outline", "version": 1, "title": "Cellar", "entries": [
              {"id": "cellar", "kind": "place", "data": {"name": "Cellar", "label": "1", "size": [6, 6]}},
              {"id": "yard", "kind": "place", "data": {"name": "Yard", "label": "2", "size": [6, 6]}},
              {"id": "f1", "kind": "encounter", "data": {"place": "cellar", "creatures": [{"creature": "rat", "count": 1}]}},
              {"id": "f2", "kind": "encounter", "data": {"place": "yard", "creatures": [{"creature": "rat", "count": 3}]}},
              {"id": "tobb", "kind": "npc", "data": {"name": "Tobb", "place": "yard"}},
              {"id": "mara", "kind": "npc", "data": {"name": "Mara", "place": "yard"}}]}
            """);
        Assert.All(key.Check(outline), line => Assert.True(line.Met));
        Assert.Equal(new[] { "a fight in 2 the book doesn't have", "Mara, someone to talk to" }, key.Extras(outline));
        // every line met, but two things made up: three of five right, so the key's share is 75 of 100
        ImportScore score = ImportScore.Of(new SourceBook(), outline, null, "", key, null);
        Assert.Equal(2, score.Extras.Count);
        Assert.True(score.Judged < 100);
    }

    [Fact]
    public void ABenchFileListsTheVersions()
    {
        using var scratch = new Scratch();
        scratch.Write("bench.json", "{\"format\": \"yorehold.import-bench\", \"versions\": [{\"name\": \"no-walls\", \"walls\": false}, {\"name\": \"local\", \"model\": \"http://127.0.0.1:11434\", \"modelName\": \"small\"}]}");
        List<ImportBench.Version> versions = new ImportBench(scratch.Folder, TestContent.Shipped()).Versions();
        Assert.Equal(new ImportBench.Version("no-walls", true, false, "", ""), versions[0]);
        Assert.Equal(("local", "http://127.0.0.1:11434", "small"), (versions[1].Name, versions[1].Model, versions[1].ModelName));
        scratch.Write("bench.json", "{\"format\": \"yorehold.import-bench\", \"versions\": [{\"name\": \"No Walls\"}]}");
        Assert.Contains("names a folder", Assert.Throws<ContentException>(() => new ImportBench(scratch.Folder, TestContent.Shipped()).Versions()).Message);
    }

    // The comparison run itself, as check.ps1 -Bench starts it: YOREHOLD_BENCH is the folder,
    // YOREHOLD_BENCH_LABEL what to call the run. Without the variable nothing runs.
    [Fact]
    public async Task ABenchNamedInTheEnvironmentIsRun()
    {
        string? folder = Environment.GetEnvironmentVariable("YOREHOLD_BENCH");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        string label = Environment.GetEnvironmentVariable("YOREHOLD_BENCH_LABEL") is { Length: > 0 } given ? given : "run";
        await new ImportBench(folder, TestContent.Shipped()).Run(label);
    }
}
