using System.Text.RegularExpressions;

namespace Yorehold.Rules;

/// <summary>
/// Says whether text is dice the game can roll ("2d6+1", "4d6kh3", "d%", "3"). Content is checked
/// with this when it loads, by the same parser that rolls it. Dice may hold formulas in braces,
/// worked out from the doer's sheet when rolled: "2d8+{mod.wis}", "1d10+{level}".
/// </summary>
public static class DiceText
{
    private static readonly Regex Braces = new(@"\{([^{}]*)\}", RegexOptions.CultureInvariant);

    public static bool IsValid(string input)
    {
        if (!input.Contains('{'))
        {
            return DiceExpression.Parse(input) != null;
        }
        bool formulasRead = true;
        string plain = Braces.Replace(input, match =>
        {
            formulasRead &= Formula.Parse(match.Groups[1].Value, out _) != null;
            return "1";
        });
        return formulasRead && !plain.Contains('{') && !plain.Contains('}') && DiceExpression.Parse(plain) != null;
    }

    /// <summary>The dice with each formula in braces replaced by its whole value, from names.</summary>
    public static string Fill(string input, Func<string, double?> names)
    {
        if (!input.Contains('{'))
        {
            return input;
        }
        string filled = Braces.Replace(input, match =>
            (Formula.Parse(match.Groups[1].Value, out _)?.Whole(names) ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        // "+-2" from a negative modifier reads as "-2"
        return filled.Replace("+-", "-").Replace("--", "+");
    }
}
