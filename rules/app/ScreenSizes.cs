namespace Yorehold.Rules;

/// <summary>
/// ui/screens.json: the sizes the game-facing screens (title, pause, lobby, creation) share: the
/// ink band down the left, its big buttons, the heading, and how long each banner picture stays.
/// </summary>
public sealed class ScreenSizes
{
    public const string File = "ui/screens.json";

    public float BandWidth { get; init; } = 440;
    public float ButtonHeight { get; init; } = 50;
    public int ButtonFont { get; init; } = 22;
    public int HeadingFont { get; init; } = 60;
    public float Gap { get; init; } = 6;
    public float Margin { get; init; } = 48;
    public double BannerSeconds { get; init; } = 8;

    public static ScreenSizes Load(ContentFiles files) => files.Exists(File) ? Read(ContentNode.Read(files, File)) : new ScreenSizes();

    public static ScreenSizes Read(ContentNode node)
    {
        node.RequireObject("screen sizes are a JSON object");
        node.Only("bandWidth", "buttonHeight", "buttonFont", "headingFont", "gap", "margin", "bannerSeconds");
        return new ScreenSizes
        {
            BandWidth = (float)node.Number("bandWidth", 440, 200, 1200),
            ButtonHeight = (float)node.Number("buttonHeight", 50, 20, 200),
            ButtonFont = node.Int("buttonFont", 22, 8, 96),
            HeadingFont = node.Int("headingFont", 60, 12, 200),
            Gap = (float)node.Number("gap", 6, 0, 100),
            Margin = (float)node.Number("margin", 48, 0, 400),
            BannerSeconds = node.Number("bannerSeconds", 8, 1, 600),
        };
    }
}
