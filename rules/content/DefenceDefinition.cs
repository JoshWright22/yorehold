namespace Yorehold.Rules;

/// <summary>
/// One of ruleset.json's "defences": a number attacks are rolled against, worked out from the
/// sheet (Fate's Defend from its approaches, a Reflex defence). "ac" is always there: the game's
/// armour class from the "ac" stat, armour and the armorClass formula.
/// </summary>
public sealed record DefenceDefinition(string Id, string Name, Formula Value)
{
    public const string ArmorClass = "ac";

    public static DefenceDefinition Read(ContentNode node)
    {
        node.RequireObject("a defence is an object with an id, a name and a value");
        node.Only("id", "name", "value");
        string id = node.At("id").AsId();
        if (id == ArmorClass)
        {
            throw node.Fail("id", "\"ac\" is the armour class every system has; name another");
        }
        ContentNode value = node.At("value");
        Formula formula = Formula.Parse(value.AsText(2000), out string error) ?? throw value.Fail(error);
        return new DefenceDefinition(id, node.Text("name", id, 64), formula);
    }
}
