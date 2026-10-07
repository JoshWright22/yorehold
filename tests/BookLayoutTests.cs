namespace Yorehold.Rules.Tests;

public class BookLayoutTests
{
    // Two pages laid out like an adventure module: a map with the place numbers printed on it
    // (two of them run together into one block), a hero's picture with the name under it, and
    // numbered places with passages to read out and a boxed note.
    private const string Source = """
    {
      "format": "yorehold.source", "version": 1, "title": "Rat_Cellar_(v2)", "file": "rat-cellar.pdf", "bodySize": 10,
      "pages": [
        {"number": 1, "width": 600, "height": 800, "blocks": [
          {"kind": "heading", "box": "framed", "text": "1", "at": [100, 300, 8, 8]},
          {"kind": "heading", "box": "framed", "text": "3 2", "at": [100, 150, 40, 8]},
          {"kind": "heading", "box": "framed", "text": "9", "at": [500, 700, 8, 8]},
          {"kind": "heading", "text": "BRAM", "at": [340, 330, 60, 12]},
          {"kind": "heading", "text": "1: THE CELLAR STAIRS", "at": [50, 420, 200, 13]},
          {"kind": "text", "box": "shaded", "text": "Steps go down into the dark.", "at": [50, 440, 200, 20]},
          {"kind": "text", "text": "The rats hear the party coming.", "at": [50, 470, 200, 20]},
          {"kind": "text", "box": "framed", "text": "Let the players roll first.", "at": [50, 500, 200, 20]}
        ]},
        {"number": 2, "width": 600, "height": 800, "blocks": [
          {"kind": "heading", "text": "2. RAT NEST", "at": [50, 60, 200, 13]},
          {"kind": "text", "box": "shaded", "text": "The smell hits you first.", "at": [50, 80, 200, 20]},
          {"kind": "heading", "text": "3: WINE RACKS", "at": [50, 200, 200, 13]},
          {"kind": "heading", "text": "1: Something else entirely", "at": [50, 300, 200, 13]},
          {"kind": "heading", "text": "2", "at": [290, 760, 10, 10]}
        ]}
      ],
      "pictures": [
        {"file": "pictures/p1-1.jpg", "page": 1, "at": [80, 100, 200, 250], "width": 400, "height": 500},
        {"file": "pictures/p1-2.png", "page": 1, "at": [320, 100, 120, 200], "width": 240, "height": 400}
      ]
    }
    """;

    private static SourceBook Book() => SourceBook.Parse("source.json", Source);

    [Fact]
    public void APictureIsNamedByTheHeadingUnderIt()
    {
        Dictionary<string, string> names = BookLayout.PictureNames(Book());
        Assert.Equal("BRAM", names["pictures/p1-2.png"]);
        Assert.False(names.ContainsKey("pictures/p1-1.jpg"), "The map's own numbers are not a name");
    }

    [Fact]
    public void NumberedHeadingsArePlacesWithTheirPassages()
    {
        List<BookLayout.NumberedPlace> places = BookLayout.Places(Book());
        Assert.Equal(new[] { "1", "2", "3" }, places.Select(p => p.Label));
        Assert.Equal(new[] { "The Cellar Stairs", "Rat Nest", "Wine Racks" }, places.Select(p => p.Name));
        Assert.Equal(new[] { "Steps go down into the dark." }, places[0].ReadAloud);
        Assert.Equal(new[] { "Let the players roll first." }, places[0].Notes);
        Assert.Equal(2, places[1].Page);
    }

    [Fact]
    public void TheMapIsThePictureWithThePlacesNumbersOnIt()
    {
        BookLayout.MapPicture? map = BookLayout.FindMap(Book(), new[] { "1", "2", "3" });
        Assert.NotNull(map);
        Assert.Equal("pictures/p1-1.jpg", map!.File);
        Assert.Equal(new[] { "1", "2", "3" }, map.Labels.Keys.Order());
        // "3 2" is two numbers side by side: 3 on the block's left half, 2 on its right
        Assert.True(map.Labels["3"].X < map.Labels["2"].X);
        Assert.Equal(0.12f, map.Labels["1"].X, 2);
        Assert.Equal(0.816f, map.Labels["1"].Y, 2);
        Assert.Null(BookLayout.FindMap(Book(), new[] { "7", "8" }));
    }

    [Fact]
    public void TheDraftIsAnOutlineTheBuilderTakes()
    {
        Outline draft = BookLayout.Draft(Book());
        Assert.Equal("Rat Cellar", draft.Title);
        Assert.Equal(3, draft.OfKind(OutlineKind.Place).Count());
        OutlineEntry stairs = draft.OfKind(OutlineKind.Place).First();
        Assert.Equal(new OutlineSource(1, "1: THE CELLAR STAIRS"), stairs.From);
        Assert.Contains(draft.OfKind(OutlineKind.Note), n => n.Picture == "pictures/p1-1.jpg");
        // it goes through the outline's own reader, and builds
        Outline again = Outline.Parse("outline.json", draft.ToJson());
        using var scratch = new Scratch();
        string package = Path.Combine(scratch.Folder, "cellar");
        Directory.CreateDirectory(Path.Combine(package, "import", "pictures"));
        File.WriteAllBytes(Path.Combine(package, "import", "pictures", "p1-1.jpg"), new byte[] { 255, 216 });
        List<string> problems = new OutlineBuilder(again, Path.Combine(package, "import"), TestContent.Shipped()).Build(package);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [InlineData("Caves_of_Shadow_(3.0)", "Caves of Shadow")]
    [InlineData("The Old Mill", "The Old Mill")]
    public void FileNamesBecomeTitles(string name, string title) => Assert.Equal(title, BookLayout.CleanTitle(name));
}
