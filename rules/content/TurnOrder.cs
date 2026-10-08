namespace Yorehold.Rules;

/// <summary>
/// ruleset.json's "turnOrder": who acts when. "initiative" (the default) puts everyone in one
/// order by initiative; "sides" lets one whole side act, then the other, each side in initiative
/// order (with sharedTurns its members pick who goes). Roll false orders by the initiative
/// modifier alone, as Fate orders by an approach; First says which side starts in "sides".
/// Picked (Fate): whoever just acted picks who goes next among those yet to act this round; by
/// default an ally, so a side's turns run together, and a hero's player may pick anyone.
/// </summary>
public sealed class TurnOrder
{
    public static readonly string[] Modes = { "initiative", "sides" };
    public static readonly string[] Firsts = { "initiative", "party", "foes" };

    public string Mode { get; init; } = "initiative";
    public bool Roll { get; init; } = true;
    public string First { get; init; } = "initiative";
    public bool Picked { get; init; }

    public bool BySides => Mode == "sides";

    public static TurnOrder Read(ContentNode? found)
    {
        if (found is not ContentNode node)
        {
            return new TurnOrder();
        }
        node.RequireObject("is an object: mode, roll, first, picked");
        node.Only("mode", "roll", "first", "picked");
        string mode = node.Text("mode", "initiative", 32);
        if (!Modes.Contains(mode))
        {
            throw node.Fail("mode", $"is {string.Join(" or ", Modes)}");
        }
        string first = node.Text("first", "initiative", 32);
        if (!Firsts.Contains(first))
        {
            throw node.Fail("first", $"is {string.Join(", ", Firsts)}");
        }
        return new TurnOrder { Mode = mode, Roll = node.Bool("roll", true), First = first, Picked = node.Bool("picked", false) };
    }
}
