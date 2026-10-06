namespace Yorehold.Rules;

/// <summary>Money is counted in copper: 10 cp to the silver, 10 sp to the gold.</summary>
public static class Coins
{
    /// <summary>"1 gp 2 sp 5 cp", leaving out what is none; "0 cp" for nothing.</summary>
    public static string Text(int copper)
    {
        var parts = new List<string>();
        void Add(int amount, string coin)
        {
            if (amount > 0)
            {
                parts.Add($"{amount} {coin}");
            }
        }
        Add(copper / 100, "gp");
        Add(copper / 10 % 10, "sp");
        Add(copper % 10, "cp");
        return parts.Count == 0 ? "0 cp" : string.Join(" ", parts);
    }
}
