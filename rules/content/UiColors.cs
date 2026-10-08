using System.Globalization;

namespace Yorehold.Rules;

/// <summary>
/// ui/colors.json: the colours of the game's screens by role, "greys.panel" or "red.main", each
/// "#rrggbb". The game reads it at start, so a skin that brings its own file recolours every
/// screen; a role it leaves out keeps the game's own colour.
/// </summary>
public sealed class UiColors
{
    public const string File = "ui/colors.json";

    public Dictionary<string, (byte R, byte G, byte B)> Roles { get; } = new(StringComparer.Ordinal);

    public static UiColors Read(ContentNode node)
    {
        node.RequireObject("a colour scheme is a JSON object");
        if (node.At("format").AsText() != "yorehold.colors")
        {
            throw node.Fail("format", "is \"yorehold.colors\"");
        }
        var colors = new UiColors();
        if (node.Get("greys") is ContentNode greys)
        {
            foreach (KeyValuePair<string, ContentNode> grey in greys.Members())
            {
                colors.Roles["greys." + grey.Key] = Hex(grey.Value);
            }
        }
        if (node.Get("highlights") is ContentNode highlights)
        {
            foreach (KeyValuePair<string, ContentNode> highlight in highlights.Members())
            {
                foreach (KeyValuePair<string, ContentNode> shade in highlight.Value.Members().Where(m => m.Key != "use"))
                {
                    colors.Roles[$"{highlight.Key}.{shade.Key}"] = Hex(shade.Value);
                }
            }
        }
        return colors;
    }

    private static (byte, byte, byte) Hex(ContentNode node)
    {
        string text = node.AsText(16);
        if (text.Length != 7 || text[0] != '#' || !int.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            throw node.Fail("is a colour like \"#1e1e1e\"");
        }
        return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
