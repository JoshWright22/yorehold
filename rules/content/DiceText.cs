using System.Text.RegularExpressions;

namespace Yorehold.Rules;

/// <summary>
/// Says whether text is dice the game can roll ("2d6+1", "4d6kh3", "d%", "3"). Content is checked
/// with this when it loads, by the same parser that rolls it. Dice may hold formulas in braces,
/// worked out from the doer's sheet when rolled: "2d8+{mod.wis}", "1d10+{level}". Foundry's
/// spelling works too: "1d10 + @abilities.con.mod" is "1d10+{mod.con}".
/// </summary>
public static class DiceText
{
    private static readonly Regex Braces = new(@"\{([^{}]*)\}", RegexOptions.CultureInvariant);
    private static readonly Regex BarePath = new(@"@[A-Za-z][\w.\-]*", RegexOptions.CultureInvariant);

    // A Foundry "@" path outside braces is a formula of its own; spaces don't matter in dice.
    private static string Braced(string input)
    {
        if (!input.Contains('@'))
        {
            return input;
        }
        var text = new System.Text.StringBuilder();
        int at = 0;
        foreach (Match braces in Braces.Matches(input))
        {
            text.Append(BarePath.Replace(input[at..braces.Index], m => "{" + m.Value + "}")).Append(braces.Value);
            at = braces.Index + braces.Length;
        }
        text.Append(BarePath.Replace(input[at..], m => "{" + m.Value + "}"));
        return text.ToString().Replace(" ", "");
    }

    public static bool IsValid(string input)
    {
        input = Braced(input);
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
        input = Braced(input);
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
