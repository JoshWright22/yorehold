namespace Yorehold.Rules;

/// <summary>
/// ui/fonts.json: the faces of the game's screens by role (sans, book, mono), each a list of
/// system font names tried in order. The game reads it at start, so a skin's own file changes the
/// type everywhere; a role it leaves out keeps the game's own.
/// </summary>
public sealed class UiFonts
{
    public const string File = "ui/fonts.json";
    public static readonly string[] Roles = { "sans", "book", "mono" };

    public Dictionary<string, List<string>> Faces { get; } = new(StringComparer.Ordinal);

    public static UiFonts Read(ContentNode node)
    {
        node.RequireObject("a font scheme is a JSON object");
        node.Only("format", "version", "about", "sans", "book", "mono");
        if (node.At("format").AsText() != "yorehold.fonts")
        {
            throw node.Fail("format", "is \"yorehold.fonts\"");
        }
        var fonts = new UiFonts();
        foreach (string role in Roles)
        {
            if (node.Get(role) is not ContentNode list)
            {
                continue;
            }
            List<string> names = node.Texts(role);
            if (names.Count == 0 || names.Any(n => n.Trim().Length == 0 || n.Length > 80))
            {
                throw list.Fail("is a list of font names, tried in order");
            }
            fonts.Faces[role] = names;
        }
        return fonts;
    }
}
