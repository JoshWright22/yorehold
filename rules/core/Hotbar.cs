using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Where a character's actions and spells sit on their bars, as the player arranged them. Slots
/// hold action ids. An action the character gains goes into the first empty slot the first time
/// it is seen, so a new spell turns up on its own; one the player took off stays off. An id the
/// character can't use now (a spell not prepared today) keeps its slot and shows empty until it
/// is back.
/// </summary>
public sealed class Hotbar
{
    /// <summary>Two bars of twelve.</summary>
    public const int Size = 24;

    private readonly string[] _slots = Enumerable.Repeat("", Size).ToArray();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Slots => _slots;

    /// <summary>
    /// Puts actions not seen before into the first empty slots, then says what each slot shows:
    /// the id when it is among actions, "" otherwise. actions is the character's in their usual order.
    /// </summary>
    public string[] Layout(IReadOnlyList<string> actions)
    {
        foreach (string id in actions)
        {
            if (_seen.Add(id) && Array.IndexOf(_slots, id) < 0)
            {
                int empty = Array.IndexOf(_slots, "");
                if (empty >= 0)
                {
                    _slots[empty] = id;
                }
            }
        }
        return _slots.Select(s => actions.Contains(s) ? s : "").ToArray();
    }

    /// <summary>
    /// Puts an action in a slot. If it was already on the bar, the two slots swap, so whatever was
    /// in the way lands where it came from; otherwise what was there comes off.
    /// </summary>
    public void Put(int slot, string id)
    {
        if (slot < 0 || slot >= Size || id.Length == 0)
        {
            return;
        }
        _seen.Add(id);
        int from = Array.IndexOf(_slots, id);
        if (from >= 0)
        {
            _slots[from] = _slots[slot];
        }
        _slots[slot] = id;
    }

    /// <summary>Takes whatever is in a slot off the bar. It stays off until put back.</summary>
    public void Clear(int slot)
    {
        if (slot >= 0 && slot < Size)
        {
            _slots[slot] = "";
        }
    }

    public JsonObject ToJson()
    {
        // trailing empty slots are left out
        int used = Array.FindLastIndex(_slots, s => s.Length > 0) + 1;
        return new JsonObject
        {
            ["slots"] = new JsonArray(_slots.Take(used).Select(s => (JsonNode)JsonValue.Create(s)).ToArray()),
            ["seen"] = new JsonArray(_seen.Order(StringComparer.Ordinal).Select(s => (JsonNode)JsonValue.Create(s)).ToArray()),
        };
    }

    public static Hotbar Read(ContentNode node)
    {
        var bar = new Hotbar();
        string[] slots = node.Texts("slots").ToArray();
        if (slots.Length > Size)
        {
            throw node.Fail("slots", $"has at most {Size}");
        }
        slots.CopyTo(bar._slots, 0);
        bar._seen.UnionWith(node.Texts("seen"));
        bar._seen.UnionWith(slots.Where(s => s.Length > 0));
        return bar;
    }
}
