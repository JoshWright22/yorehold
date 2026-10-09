using System.Text;

namespace Yorehold.Rules;

/// <summary>One die as it fell. Fudge marks Fate's dice (-1, 0, +1) apart from a d3; Faces, a system's own die (what each face counts).</summary>
public readonly record struct DieRoll(int Sides, int Value, bool Kept = true, bool Fudge = false, int[]? Faces = null);

public enum Advantage
{
    None,
    /// <summary>Roll 2d20, keep the higher.</summary>
    Advantage,
    /// <summary>Roll 2d20, keep the lower.</summary>
    Disadvantage,
}

/// <summary>What some dice came to, with every die shown.</summary>
public sealed class RollResult
{
    public string Expression { get; init; } = "";
    public int Total { get; set; }
    public List<DieRoll> Dice { get; } = new();
    /// <summary>The flat numbers added together.</summary>
    public int Flat { get; set; }

    /// <summary>For a d20 roll: the die that counted showed 20.</summary>
    public bool Natural20 => KeptD20() == 20;
    public bool Natural1 => KeptD20() == 1;

    /// <summary>"2d6+3: [4, 2] + 3 = 9", dropped dice in brackets as (1).</summary>
    public string Describe()
    {
        var text = new StringBuilder(Expression).Append(": ");
        if (Dice.Count > 0)
        {
            text.Append('[');
            for (int i = 0; i < Dice.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }
                text.Append(Dice[i].Kept ? Dice[i].Value.ToString() : $"({Dice[i].Value})");
            }
            text.Append(']');
        }
        if (Flat != 0)
        {
            text.Append(Flat > 0 ? " + " : " - ").Append(Math.Abs((long)Flat));
        }
        return text.Append(" = ").Append(Total).ToString();
    }

    private int KeptD20()
    {
        foreach (DieRoll die in Dice)
        {
            if (die.Kept && die.Sides == 20 && die.Faces == null)
            {
                return die.Value;
            }
        }
        return 0;
    }
}
