namespace Yorehold.Rules;

/// <summary>A square on a map, counted from the top-left.</summary>
public readonly record struct Cell(int X, int Y);

/// <summary>A colour as content files write it: [r, g, b] or [r, g, b, a], each 0 to 255.</summary>
public readonly record struct ContentColor(byte R, byte G, byte B, byte A = 255);

public enum ModifierOp
{
    Add,
    Multiply,
    Override,
}

/// <summary>A change to a stat while something lasts: "ac" add 2, "speed" multiply 0.5.</summary>
/// <summary>
/// A change to a stat. Adds with the same Type don't stack: only the biggest bonus and the
/// biggest penalty of each type count ("item", "status", "circumstance"); untyped ones all add.
/// </summary>
/// <remarks>
/// With If, it is situational: it counts only on a roll whose context makes the formula true (a
/// ranged attack, a Dexterity save), never in the sheet's standing numbers.
/// </remarks>
public record Modifier(string Stat, ModifierOp Op, double Value, string Type = "", Formula? If = null);

public static class ContentIds
{
    /// <summary>Ids are file names too, so they stay lowercase letters, digits, - and _.</summary>
    public static bool IsId(string id)
    {
        if (id.Length == 0 || id.Length > 64)
        {
            return false;
        }
        foreach (char c in id)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>Small readers shared by several content kinds.</summary>
public static class ContentParts
{
    public static Cell CellFrom(ContentNode node)
    {
        if (!node.IsArray || node.Count != 2)
        {
            throw node.Fail("cells are [x, y]");
        }
        ContentNode[] parts = node.Items().ToArray();
        return new Cell(parts[0].AsInt(), parts[1].AsInt());
    }

    public static ContentColor ColorFrom(ContentNode node)
    {
        if (!node.IsArray || (node.Count != 3 && node.Count != 4))
        {
            throw node.Fail("colours are [r,g,b] or [r,g,b,a] in 0..255");
        }
        var parts = new List<byte>();
        foreach (ContentNode part in node.Items())
        {
            if (!part.IsWhole || part.AsInt() < 0 || part.AsInt() > 255)
            {
                throw node.Fail("colours are [r,g,b] or [r,g,b,a] in 0..255");
            }
            parts.Add((byte)part.AsInt());
        }
        return new ContentColor(parts[0], parts[1], parts[2], parts.Count == 4 ? parts[3] : (byte)255);
    }

    public static ModifierOp OpFrom(ContentNode node, string key)
    {
        string op = node.Text(key, "add");
        return op switch
        {
            "add" => ModifierOp.Add,
            "multiply" => ModifierOp.Multiply,
            "override" => ModifierOp.Override,
            _ => throw node.Fail(key, "is \"add\", \"multiply\" or \"override\""),
        };
    }

    /// <summary>An optional "modifiers" list. Strict readers also refuse unknown fields on each entry.</summary>
    public static List<Modifier> ModifiersFrom(ContentNode node, bool strict)
    {
        var list = new List<Modifier>();
        ContentNode? found = node.Get("modifiers");
        if (found == null)
        {
            return list;
        }
        if (!found.Value.IsArray || found.Value.Count > 100)
        {
            throw found.Value.Fail("is a list");
        }
        foreach (ContentNode entry in found.Value.Items())
        {
            entry.RequireObject("is an object with a stat and a value");
            if (strict)
            {
                entry.Only("stat", "op", "value", "type", "if");
            }
            // Stats are camelCase ("maxHp"), so they are names and not ids.
            string stat = entry.At("stat").AsName();
            Formula? when = null;
            if (entry.Get("if") is ContentNode condition)
            {
                when = Formula.Parse(condition.AsText(2000), out string error) ?? throw condition.Fail(error);
            }
            list.Add(new Modifier(stat, OpFrom(entry, "op"), entry.At("value").AsNumber(), entry.Text("type", "", 64), when));
        }
        return list;
    }

    /// <summary>An optional object of name to whole number, like "abilities" or "resources".</summary>
    public static Dictionary<string, int> NumbersFrom(ContentNode node, string key, int low, int high, bool idKeys)
    {
        var map = new Dictionary<string, int>();
        ContentNode? found = node.Get(key);
        if (found == null)
        {
            return map;
        }
        if (!found.Value.IsObject)
        {
            throw found.Value.Fail("maps a name to a whole number");
        }
        foreach (KeyValuePair<string, ContentNode> member in found.Value.Members())
        {
            bool named = idKeys ? ContentIds.IsId(member.Key) : member.Key.Length > 0 && member.Key.Length <= 64;
            if (!named || !member.Value.IsWhole)
            {
                throw member.Value.Fail($"is a whole number from {low} to {high}");
            }
            map[member.Key] = member.Value.AsInt(low, high);
        }
        return map;
    }

    /// <summary>An optional object of name to name, like "proficiencyRanks".</summary>
    public static Dictionary<string, string> NamesFrom(ContentNode node, string key, bool ids)
    {
        var map = new Dictionary<string, string>();
        ContentNode? found = node.Get(key);
        if (found == null)
        {
            return map;
        }
        if (!found.Value.IsObject || found.Value.Members().Count() > 1000)
        {
            throw found.Value.Fail("maps a skill, ability, \"weapons\", \"armor\" or \"dc\" to a rank");
        }
        foreach (KeyValuePair<string, ContentNode> member in found.Value.Members())
        {
            if (member.Key.Length == 0 || member.Key.Length > 64 || (ids && !ContentIds.IsId(member.Key)))
            {
                throw member.Value.Fail("is a rank id");
            }
            map[member.Key] = ids ? member.Value.AsId() : member.Value.AsName();
        }
        return map;
    }
}
