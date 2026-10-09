namespace Yorehold.Rules;

/// <summary>
/// ui/motion.json: how the screens move, read at start: how long an opening panel takes to slide
/// up into place and how far it comes from, how long the chat column takes to slide, how long an
/// HP bar takes to run down to its new value, and how long the turn banner slides. Every move
/// is a slide, never a fade: a half-faded panel is a colour off the palette. "Less motion" in
/// Options turns them all into a cut. A skin's file changes only what it names.
/// </summary>
public sealed class UiMotion
{
    public const string File = "ui/motion.json";

    public double PanelSeconds { get; init; } = 0.14;
    public double PanelDistance { get; init; } = 12;
    public double ChatSeconds { get; init; } = 0.15;
    public double BarSeconds { get; init; } = 0.4;
    public double BannerSeconds { get; init; } = 0.2;

    public static UiMotion Read(ContentNode node)
    {
        node.RequireObject("a motion file is a JSON object");
        node.Only("format", "version", "about", "panelSeconds", "panelDistance", "chatSeconds", "barSeconds", "bannerSeconds");
        if (node.At("format").AsText() != "yorehold.motion")
        {
            throw node.Fail("format", "is \"yorehold.motion\"");
        }
        var defaults = new UiMotion();
        return new UiMotion
        {
            PanelSeconds = node.Number("panelSeconds", defaults.PanelSeconds, 0, 2),
            PanelDistance = node.Number("panelDistance", defaults.PanelDistance, 0, 200),
            ChatSeconds = node.Number("chatSeconds", defaults.ChatSeconds, 0, 2),
            BarSeconds = node.Number("barSeconds", defaults.BarSeconds, 0, 5),
            BannerSeconds = node.Number("bannerSeconds", defaults.BannerSeconds, 0, 2),
        };
    }
}
