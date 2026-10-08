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
}
