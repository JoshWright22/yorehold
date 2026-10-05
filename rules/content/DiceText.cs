namespace Yorehold.Rules;

/// <summary>
/// Says whether text is dice the game can roll ("2d6+1", "4d6kh3", "d%", "3"). Content is checked
/// with this when it loads; rolling itself comes with the core rules.
/// </summary>
public static class DiceText
{
    public static bool IsValid(string input)
    {
        if (input.Length > 4096)
        {
            return false;
        }
        string text = new string(input.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray());
        if (text.Length == 0)
        {
            return false;
        }

        int pos = 0;
        long magnitude = 0;
        int totalDice = 0;
        int terms = 0;
        while (pos < text.Length)
        {
            if (text[pos] == '+' || text[pos] == '-')
            {
                pos++;
            }

            int number = ReadNumber(text, ref pos);
            int count;
            int sides = 0;
            int kept;
            if (pos < text.Length && text[pos] == 'd')
            {
                pos++;
                count = number < 0 ? 1 : number;
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
                    return false;
                }
                kept = count;
                if (pos + 1 < text.Length && text[pos] == 'k' && (text[pos + 1] == 'h' || text[pos + 1] == 'l'))
                {
                    pos += 2;
                    kept = ReadNumber(text, ref pos);
                    if (kept < 1 || kept > count)
                    {
                        return false;
                    }
                }
            }
            else
            {
                if (number < 0)
                {
                    return false;
                }
                count = number;
                kept = number;
            }

            magnitude += sides == 0 ? count : (long)kept * sides;
            totalDice += sides == 0 ? 0 : count;
            // Room for critical doubling and modifiers, and a stop on absurd expressions.
            if (magnitude > int.MaxValue / 4 || totalDice > 10000 || terms >= 128)
            {
                return false;
            }
            terms++;

            if (pos < text.Length && text[pos] != '+' && text[pos] != '-')
            {
                return false;
            }
        }
        return true;
    }

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
