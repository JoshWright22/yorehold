namespace Yorehold.Rules;

/// <summary>
/// One of ruleset.json's "fields": words a character is made of rather than numbers, like Fate's
/// high concept, trouble and aspects. Count is how many lines it holds (1 for a single phrase).
/// Formulas read "field.&lt;id&gt;" as how many are filled in.
/// </summary>
public sealed record FieldDefinition(string Id, string Name, int Count, bool Required, string Hint)
{
    /// <summary>The longest line a field keeps.</summary>
    public const int MostLetters = 200;

    public static FieldDefinition Read(ContentNode node)
    {
        node.RequireObject("a field is an object with an id and a name");
        node.Only("id", "name", "count", "required", "hint");
        string id = node.At("id").AsId();
        return new FieldDefinition(id, node.Text("name", id, 64), node.Int("count", 1, 1, 20), node.Bool("required", false), node.Text("hint", "", 200));
    }
}
