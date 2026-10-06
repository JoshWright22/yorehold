namespace Yorehold.Rules;

/// <summary>
/// Says whether text is dice the game can roll ("2d6+1", "4d6kh3", "d%", "3"). Content is checked
/// with this when it loads, by the same parser that rolls it.
/// </summary>
public static class DiceText
{
    public static bool IsValid(string input)
    {
        return DiceExpression.Parse(input) != null;
    }
}
