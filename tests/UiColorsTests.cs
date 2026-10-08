namespace Yorehold.Rules.Tests;

public class UiColorsTests
{
    [Fact]
    public void TheScreensColoursAreData()
    {
        UiColors shipped = UiColors.Read(ContentNode.Read(TestContent.Shipped(), UiColors.File));
        Assert.Equal(((byte)0x1e, (byte)0x1e, (byte)0x1e), shipped.Roles["greys.bg"]);
        Assert.Equal(((byte)0xe0, (byte)0x67, (byte)0x5e), shipped.Roles["red.main"]);
        Assert.False(shipped.Roles.ContainsKey("gold.use"), "a highlight's \"use\" is words, not a colour");

        // a skin's file names only what it changes
        UiColors skin = UiColors.Read(TestContent.Json("""{"format": "yorehold.colors", "version": 1, "highlights": {"gold": {"main": "#ffcc00"}}}"""));
        Assert.Single(skin.Roles);
        Assert.Equal("highlights.gold.main", TestContent.Refused(() => UiColors.Read(TestContent.Json(
            """{"format": "yorehold.colors", "version": 1, "highlights": {"gold": {"main": "yellow"}}}"""))).Field);
    }

    [Fact]
    public void TheDiceLookIsData()
    {
        UiDice shipped = UiDice.Read(ContentNode.Read(TestContent.Shipped(), UiDice.File));
        Assert.Equal(("greys.paper-2", 6), (shipped.Body, shipped.Most));
        UiDice skin = UiDice.Read(TestContent.Json("""{"format": "yorehold.dice", "version": 1, "body": "#aa3322", "most": 10}"""));
        Assert.Equal(("#aa3322", "greys.bg-deep", 10), (skin.Body, skin.Numbers, skin.Most));
        Assert.Equal("body", TestContent.Refused(() => UiDice.Read(TestContent.Json("""{"format": "yorehold.dice", "version": 1, "body": "red"}"""))).Field);
    }

    [Fact]
    public void TheScreensShapesAreData()
    {
        UiShapes shipped = UiShapes.Read(ContentNode.Read(TestContent.Shipped(), UiShapes.File));
        Assert.Equal((2, 1, 0), (shipped.Corners, shipped.Edges, shipped.Shadow));
        UiShapes skin = UiShapes.Read(TestContent.Json("""{"format": "yorehold.shapes", "version": 1, "corners": 0}"""));
        Assert.Equal((0, (int?)null), (skin.Corners, skin.Edges));
        Assert.Equal("edges", TestContent.Refused(() => UiShapes.Read(TestContent.Json("""{"format": "yorehold.shapes", "version": 1, "edges": 40}"""))).Field);
    }

    [Fact]
    public void TheScreensFacesAreData()
    {
        UiFonts shipped = UiFonts.Read(ContentNode.Read(TestContent.Shipped(), UiFonts.File));
        Assert.Equal("Inter Tight", shipped.Faces["sans"][0]);
        Assert.Equal(3, shipped.Faces.Count);
        UiFonts skin = UiFonts.Read(TestContent.Json("""{"format": "yorehold.fonts", "version": 1, "book": ["Georgia", "serif"]}"""));
        Assert.Equal(new[] { "book" }, skin.Faces.Keys);
        Assert.Equal("book", TestContent.Refused(() => UiFonts.Read(TestContent.Json("""{"format": "yorehold.fonts", "version": 1, "book": []}"""))).Field);
        // a skin's own font file, the system names after it standing in
        UiFonts brought = UiFonts.Read(TestContent.Json("""{"format": "yorehold.fonts", "version": 1, "book": ["ui/fonts/Body.TTF", "Georgia"]}"""));
        Assert.True(UiFonts.IsFile(brought.Faces["book"][0]) && !UiFonts.IsFile("Georgia"));
        Assert.Equal("book", TestContent.Refused(() => UiFonts.Read(TestContent.Json("""{"format": "yorehold.fonts", "version": 1, "book": ["../body.ttf"]}"""))).Field);
    }
}
