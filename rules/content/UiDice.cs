using System.Globalization;

namespace Yorehold.Rules;

/// <summary>
/// ui/dice.json: how the thrown dice look, read at start: the body and number colours of a die
/// that counts and one that doesn't (the low die of advantage), each a colour role of
/// ui/colors.json ("greys.paper-2") or "#rrggbb"; how many are shown at once; and their size.
/// A skin's file changes only what it names.
/// </summary>
public sealed class UiDice
{
    public const string File = "ui/dice.json";

    public string Body { get; init; } = "greys.paper-2";
    public string Numbers { get; init; } = "greys.bg-deep";
    public string Unkept { get; init; } = "greys.soft";
    public string UnkeptNumbers { get; init; } = "greys.muted";
    public int Most { get; init; } = 6;
    public double Size { get; init; } = 1;

    public static UiDice Read(ContentNode node)
    {
        node.RequireObject("a dice file is a JSON object");
        node.Only("format", "version", "about", "body", "numbers", "unkept", "unkeptNumbers", "most", "size");
        if (node.At("format").AsText() != "yorehold.dice")
        {
            throw node.Fail("format", "is \"yorehold.dice\"");
        }
        var defaults = new UiDice();
        string Colour(string key, string fallback)
        {
            if (node.Get(key) is not ContentNode value)
            {
                return fallback;
            }
            string text = value.AsText(32);
            bool hex = text.Length == 7 && text[0] == '#' && int.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
            bool role = text.Contains('.') && text.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-');
            if (!hex && !role)
            {
                throw value.Fail("is a colour role like \"greys.paper\" or a colour like \"#f7f7f7\"");
            }
            return text;
        }
        return new UiDice
        {
            Body = Colour("body", defaults.Body),
            Numbers = Colour("numbers", defaults.Numbers),
            Unkept = Colour("unkept", defaults.Unkept),
            UnkeptNumbers = Colour("unkeptNumbers", defaults.UnkeptNumbers),
            Most = node.Int("most", defaults.Most, 1, 12),
            Size = node.Number("size", defaults.Size, 0.5, 2),
        };
    }
}
