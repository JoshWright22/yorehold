using System.Text;

namespace Yorehold.Rules;

/// <summary>
/// One part of dice text: "3d6", "4d6kh3", "2d20kl1" or a flat number (Sides 0, Count is the
/// number). Explode: a die showing its highest face rolls again and adds ("1d6!"). SuccessAt: the
/// term counts the dice showing that much or more instead of adding them ("6d6s5"). Fudge: the
/// dice show -1, 0 or +1 ("4dF"). Faces: a system's own die, what each face counts written out
/// ("2d{0,0,1,1,2,1}"); Sides is how many faces it has.
/// </summary>
public readonly record struct DiceTerm(int Count = 1, int Sides = 0, int KeepHighest = 0, int KeepLowest = 0, int Sign = 1,
    bool Explode = false, int SuccessAt = 0, bool Fudge = false, int[]? Faces = null)
{
    /// <summary>Rolls again on an exploding die stop here, so a roll always ends.</summary>
    public const int MostExplosions = 20;
    /// <summary>The most faces a die of its own may have.</summary>
    public const int MostFaces = 100;

    /// <summary>How many of its dice count toward the total.</summary>
    public int Kept => KeepHighest != 0 ? KeepHighest : KeepLowest != 0 ? KeepLowest : Count;

    /// <summary>The lowest and highest face of one die.</summary>
    public int LowFace => Faces != null ? Faces.Min() : Fudge ? -1 : 1;
    public int HighFace => Faces != null ? Faces.Max() : Fudge ? 1 : Sides;

    /// <summary>What face number n (1 to Sides) of one die counts.</summary>
    public int ValueOf(int face) => Faces != null ? Faces[face - 1] : Fudge ? face - 2 : face;
}

/// <summary>Parsed dice text like "2d6+3", "1d20-1", "4d6kh3", "d%", "1d6!", "6d6s5", "4dF" or "3d{0,1,1,2}". Case and spaces don't matter.</summary>
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
                bool fudge = false;
                int[]? faces = null;
                if (pos < text.Length && text[pos] == '{')
                {
                    faces = ReadFaces(text, ref pos);
                    if (faces == null)
                    {
                        return null;
                    }
                    sides = faces.Length;
                }
                else if (pos < text.Length && text[pos] == '%')
                {
                    sides = 100;
                    pos++;
                }
                else if (pos < text.Length && text[pos] == 'f')
                {
                    sides = 3;
                    fudge = true;
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
                bool explode = false;
                if (pos < text.Length && text[pos] == '!')
                {
                    if (sides < 2 || fudge || faces != null)
                    {
                        return null;
                    }
                    explode = true;
                    pos++;
                }
                int successAt = 0;
                if (pos < text.Length && text[pos] == 's')
                {
                    pos++;
                    successAt = ReadNumber(text, ref pos);
                    if (successAt < 1 || fudge)
                    {
                        return null;
                    }
                }
                term = new DiceTerm(count, sides, keepHighest, keepLowest, sign, explode, successAt, fudge, faces);
            }
            else
            {
                if (number < 0)
                {
                    return null;
                }
                term = new DiceTerm(number, 0, 0, 0, sign);
            }

            int biggest = term.Faces == null ? term.Sides : Math.Max(Math.Abs(term.LowFace), Math.Abs(term.HighFace));
            magnitude += term.Sides == 0 ? term.Count : (long)term.Kept * biggest * (term.Explode ? DiceTerm.MostExplosions + 1 : 1);
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
            text.Append('d');
            if (term.Faces != null)
            {
                text.Append('{').Append(string.Join(",", term.Faces)).Append('}');
            }
            else if (term.Fudge)
            {
                text.Append('F');
            }
            else
            {
                text.Append(term.Sides);
            }
            if (term.KeepHighest != 0)
            {
                text.Append("kh").Append(term.KeepHighest);
            }
            if (term.KeepLowest != 0)
            {
                text.Append("kl").Append(term.KeepLowest);
            }
            if (term.Explode)
            {
                text.Append('!');
            }
            if (term.SuccessAt != 0)
            {
                text.Append('s').Append(term.SuccessAt);
            }
        }
        return text.ToString();
    }

    /// <summary>What it comes to on average.</summary>
    public double Average() => DiceAverage() + Flat();

    /// <summary>The flat numbers alone.</summary>
    public int Flat() => Terms.Where(t => t.Sides == 0).Sum(t => t.Sign * t.Count);

    /// <summary>What the dice alone come to on average, the flat numbers left out.</summary>
    public double DiceAverage()
    {
        double total = 0;
        foreach (DiceTerm term in Terms.Where(t => t.Sides != 0))
        {
            if (term.KeepHighest != 0 || term.KeepLowest != 0 || term.SuccessAt != 0)
            {
                // which dice count depends on the others: every way they fall, counted
                var one = new DiceExpression();
                one.Terms.Add(term);
                total += CheckKind.Spread(one).Sum(p => p.Key * p.Value);
                continue;
            }
            double face = term.Faces != null ? term.Faces.Average() : term.Fudge ? 0 : (term.Sides + 1) / 2.0;
            if (term.Explode)
            {
                // each top face rolls again: s/(s-1) times the plain average, less the rolls past the limit
                face *= (1 - Math.Pow(1.0 / term.Sides, DiceTerm.MostExplosions + 1)) / (1 - 1.0 / term.Sides);
            }
            total += term.Sign * term.Count * face;
        }
        return total;
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

    private static int Low(DiceTerm term) => term.Sides == 0 ? term.Count : term.SuccessAt != 0 ? 0 : term.Kept * term.LowFace;
    private static int High(DiceTerm term) => term.Sides == 0 ? term.Count : term.SuccessAt != 0 ? term.Kept
        : term.Kept * term.HighFace * (term.Explode ? DiceTerm.MostExplosions + 1 : 1);

    // Reads "{0,0,1,-1}" at pos: whole numbers, each -1000 to 1000, 1 to MostFaces of them. Null if it isn't.
    private static int[]? ReadFaces(string text, ref int pos)
    {
        int close = text.IndexOf('}', pos);
        if (close < 0)
        {
            return null;
        }
        string[] parts = text[(pos + 1)..close].Split(',');
        pos = close + 1;
        var faces = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out faces[i]) || Math.Abs(faces[i]) > 1000)
            {
                return null;
            }
        }
        return faces.Length is >= 1 and <= DiceTerm.MostFaces ? faces : null;
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
