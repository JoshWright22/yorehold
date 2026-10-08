namespace Yorehold.Rules;

/// <summary>
/// How a rolled die is shown thrown: its shape and the face that lands up. The result is the
/// game's seeded roll; the throw only turns the die so that face shows. Shapes are the solids
/// (d4, d6, d8, d10, d12, d20); a Fate die is a d6 marked +, - and blank; a d100 is a pair of d10s;
/// any other size shows as a d20-like ball with its number.
/// </summary>
public static class DiceFaces
{
    public sealed record Shown(string Shape, string Face, bool Kept);

    public static readonly int[] Solids = { 4, 6, 8, 10, 12, 20 };

    /// <summary>The dice of a roll as they are thrown, in the order rolled (a d100 is two of them).</summary>
    public static List<Shown> Of(RollResult roll)
    {
        var shown = new List<Shown>();
        foreach (DieRoll die in roll.Dice)
        {
            if (die.Fudge)
            {
                shown.Add(new Shown("dF", die.Value > 0 ? "+" : die.Value < 0 ? "-" : "", die.Kept));
            }
            else if (die.Sides == 100)
            {
                // tens and ones: 100 shows as 00 and 0
                int value = die.Value % 100;
                shown.Add(new Shown("d10t", (value / 10 * 10).ToString("00", System.Globalization.CultureInfo.InvariantCulture), die.Kept));
                shown.Add(new Shown("d10", (value % 10).ToString(System.Globalization.CultureInfo.InvariantCulture), die.Kept));
            }
            else
            {
                // an exploded die shows its top face; the log has the whole. A d10's 10 is its 0.
                int face = Math.Clamp(die.Value, 1, die.Sides);
                string shape = Solids.Contains(die.Sides) ? "d" + die.Sides : "d20";
                string label = die.Sides == 10 ? (face % 10).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : face.ToString(System.Globalization.CultureInfo.InvariantCulture);
                shown.Add(new Shown(shape, label, die.Kept));
            }
        }
        return shown;
    }

    /// <summary>The labels on a shape's faces, face 0 first; the face showing a label is its index.</summary>
    public static List<string> Labels(string shape)
    {
        if (shape == "dF")
        {
            return new List<string> { "+", "+", "", "", "-", "-" };
        }
        if (shape == "d10t")
        {
            return Enumerable.Range(0, 10).Select(n => (n * 10).ToString("00", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        }
        int sides = int.Parse(shape[1..], System.Globalization.CultureInfo.InvariantCulture);
        return Enumerable.Range(sides == 10 ? 0 : 1, sides).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }
}
