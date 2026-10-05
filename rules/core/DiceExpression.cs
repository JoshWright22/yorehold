using System.Text;

namespace Yorehold.Rules;

/// <summary>One part of dice text: "3d6", "4d6kh3", "2d20kl1" or a flat number (Sides 0, Count is the number).</summary>
public readonly record struct DiceTerm(int Count = 1, int Sides = 0, int KeepHighest = 0, int KeepLowest = 0, int Sign = 1)
{
    /// <summary>How many of its dice count toward the total.</summary>
    public int Kept => KeepHighest != 0 ? KeepHighest : KeepLowest != 0 ? KeepLowest : Count;
}

/// <summary>Parsed dice text like "2d6+3", "1d20-1", "4d6kh3" or "d%". Case and spaces don't matter.</summary>
public sealed class DiceExpression
{
    public List<DiceTerm> Terms { get; } = new();

    /// <summary>Null if the text is not dice the game can roll.</summary>
    public static DiceExpression? Parse(string input)
    {
        if (input.Length > 4096)
        {
            return null;
        }
        string text = new string(input.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray());
        if (text.Length == 0)
        {
            return null;
        }

        var expression = new DiceExpression();
        int pos = 0;
        long magnitude = 0;
        int totalDice = 0;
        while (pos < text.Length)
        {
            int sign = 1;
            if (text[pos] == '+' || text[pos] == '-')
            {
                sign = text[pos] == '-' ? -1 : 1;
                pos++;
            }

            int number = ReadNumber(text, ref pos);
            DiceTerm term;
            if (pos < text.Length && text[pos] == 'd')
            {
                pos++;
                int count = number < 0 ? 1 : number;
                int sides;
                if (pos < text.Length && text[pos] == '%')
                {
                    sides = 100;
                    pos++;
                }
                else
                {
                    sides = ReadNumber(text, ref pos);
                }
                if (sides < 1 || count < 1 || count > 1000)
                {
                    return null;
                }
                int keepHighest = 0;
                int keepLowest = 0;
                if (pos + 1 < text.Length && text[pos] == 'k' && (text[pos + 1] == 'h' || text[pos + 1] == 'l'))
                {
                    bool highest = text[pos + 1] == 'h';
                    pos += 2;
                    int keep = ReadNumber(text, ref pos);
                    if (keep < 1 || keep > count)
                    {
                        return null;
                    }
                    keepHighest = highest ? keep : 0;
                    keepLowest = highest ? 0 : keep;
                }
                term = new DiceTerm(count, sides, keepHighest, keepLowest, sign);
            }
            else
            {
                if (number < 0)
                {
                    return null;
                }
                term = new DiceTerm(number, 0, 0, 0, sign);
            }

            magnitude += term.Sides == 0 ? term.Count : (long)term.Kept * term.Sides;
            totalDice += term.Sides == 0 ? 0 : term.Count;
            // Room for critical doubling and modifiers, and a stop on absurd expressions.
            if (magnitude > int.MaxValue / 4 || totalDice > 10000 || expression.Terms.Count >= 128)
            {
                return null;
            }
            expression.Terms.Add(term);

            if (pos < text.Length && text[pos] != '+' && text[pos] != '-')
            {
                return null;
            }
        }
        return expression;
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (DiceTerm term in Terms)
        {
            if (text.Length > 0 || term.Sign < 0)
            {
                text.Append(term.Sign < 0 ? '-' : '+');
            }
            text.Append(term.Count);
            if (term.Sides == 0)
            {
                continue;
            }
            text.Append('d').Append(term.Sides);
            if (term.KeepHighest != 0)
            {
                text.Append("kh").Append(term.KeepHighest);
            }
            if (term.KeepLowest != 0)
            {
                text.Append("kl").Append(term.KeepLowest);
            }
        }
        return text.ToString();
    }

    public int Minimum()
    {
        int total = 0;
        foreach (DiceTerm term in Terms)
        {
            total += term.Sign > 0 ? Low(term) : -High(term);
        }
        return total;
    }

    public int Maximum()
    {
        int total = 0;
        foreach (DiceTerm term in Terms)
        {
            total += term.Sign > 0 ? High(term) : -Low(term);
        }
        return total;
    }

    private static int Low(DiceTerm term) => term.Sides == 0 ? term.Count : term.Kept;
    private static int High(DiceTerm term) => term.Sides == 0 ? term.Count : term.Kept * term.Sides;

    // Reads digits at pos; -1 if there are none or the number is too big to be dice.
    private static int ReadNumber(string text, ref int pos)
    {
        if (pos >= text.Length || !char.IsAsciiDigit(text[pos]))
        {
            return -1;
        }
        int value = 0;
        while (pos < text.Length && char.IsAsciiDigit(text[pos]))
        {
            value = value * 10 + (text[pos] - '0');
            if (value > 100000)
            {
                return -1;
            }
            pos++;
        }
        return value;
    }
}
