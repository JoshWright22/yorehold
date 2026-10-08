namespace Yorehold.Rules;

/// <summary>Rolling. Every die comes from the Rng handed in, in the order the text lists them.</summary>
public static class Dice
{
    public static RollResult Roll(DiceExpression expression, Rng random)
    {
        var result = new RollResult { Expression = expression.ToString() };
        foreach (DiceTerm term in expression.Terms)
        {
            if (term.Sides == 0)
            {
                result.Flat += term.Sign * term.Count;
                result.Total += term.Sign * term.Count;
                continue;
            }

            int first = result.Dice.Count;
            for (int i = 0; i < term.Count; i++)
            {
                int face = random.Range(1, term.Sides);
                int value = term.Fudge ? face - 2 : face;
                // an exploding die adds each roll again on its top face, up to a limit
                for (int again = 0; term.Explode && face == term.Sides && again < DiceTerm.MostExplosions; again++)
                {
                    face = random.Range(1, term.Sides);
                    value += face;
                }
                result.Dice.Add(new DieRoll(term.Sides, value, true, term.Fudge));
            }

            if (term.KeepHighest != 0 || term.KeepLowest != 0)
            {
                // Highest first; OrderBy keeps equal dice in the order rolled, which decides
                // which of two equal dice is shown as dropped.
                List<int> order = Enumerable.Range(first, term.Count).OrderByDescending(i => result.Dice[i].Value).ToList();
                int keep = term.Kept;
                for (int i = 0; i < term.Count; i++)
                {
                    bool kept = term.KeepHighest != 0 ? i < keep : i >= term.Count - keep;
                    result.Dice[order[i]] = result.Dice[order[i]] with { Kept = kept };
                }
            }

            for (int i = first; i < result.Dice.Count; i++)
            {
                if (result.Dice[i].Kept)
                {
                    // a success-counting term adds one for each die that reaches its number
                    int counts = term.SuccessAt == 0 ? result.Dice[i].Value : result.Dice[i].Value >= term.SuccessAt ? 1 : 0;
                    result.Total += term.Sign * counts;
                }
            }
        }
        return result;
    }

    /// <summary>Text that is not dice rolls nothing: total 0 and an expression that says "invalid".</summary>
    public static RollResult Roll(string expression, Rng random)
    {
        DiceExpression? parsed = DiceExpression.Parse(expression);
        return parsed == null ? new RollResult { Expression = "invalid: " + expression } : Roll(parsed, random);
    }

    /// <summary>1d20 plus a modifier, or 2d20 keeping the higher or the lower.</summary>
    public static RollResult RollD20(int modifier, Advantage advantage, Rng random)
    {
        var expression = new DiceExpression();
        expression.Terms.Add(advantage switch
        {
            Advantage.Advantage => new DiceTerm(2, 20, KeepHighest: 1),
            Advantage.Disadvantage => new DiceTerm(2, 20, KeepLowest: 1),
            _ => new DiceTerm(1, 20),
        });
        if (modifier != 0)
        {
            expression.Terms.Add(new DiceTerm(Math.Abs(modifier), 0, 0, 0, modifier < 0 ? -1 : 1));
        }
        return Roll(expression, random);
    }
}
