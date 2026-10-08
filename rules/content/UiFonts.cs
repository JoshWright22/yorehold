namespace Yorehold.Rules;

/// <summary>
/// ui/fonts.json: the faces of the game's screens by role (sans, book, mono), each a list of
/// system font names tried in order. The game reads it at start, so a skin's own file changes the
/// type everywhere; a role it leaves out keeps the game's own. An entry may be a font file the
/// skin brings ("ui/fonts/body.ttf"), used before the system names listed with it.
/// </summary>
public sealed class UiFonts
{
    public const string File = "ui/fonts.json";
    public static readonly string[] Roles = { "sans", "book", "mono" };

    public Dictionary<string, List<string>> Faces { get; } = new(StringComparer.Ordinal);

    private static readonly string[] FileEndings = { ".ttf", ".otf", ".woff", ".woff2" };

    /// <summary>An entry that names a font file in the content, not a system font.</summary>
    public static bool IsFile(string name) => FileEndings.Any(end => name.EndsWith(end, StringComparison.OrdinalIgnoreCase));

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
            if (names.Count == 0 || names.Any(n => n.Trim().Length == 0 || n.Length > 200))
            {
                throw list.Fail("is a list of font names, tried in order");
            }
            if (names.FirstOrDefault(n => IsFile(n) && !ContentFiles.IsContentPath(n)) is string bad)
            {
                throw list.Fail($"names \"{bad}\": a font file is a path inside the package, like ui/fonts/body.ttf");
            }
            if (names.Count(IsFile) > 1)
            {
                throw list.Fail("names one font file at most; the rest are system fonts to fall back on");
            }
            fonts.Faces[role] = names;
        }
        return fonts;
    }
}
