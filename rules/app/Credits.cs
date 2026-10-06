namespace Yorehold.Rules;

/// <summary>One line of the credits: who or what, and under which licence it is used.</summary>
public sealed record CreditEntry(string Name, string Kind, string By, string Licence, string Text);

/// <summary>
/// The game's own credits, ui/credits.json: {"entries": [{"name", "kind", "by", "licence",
/// "text"}]}. The engine and the libraries inside it are not in this file; the credits screen
/// asks the engine for those and their licence texts, so they are always the ones shipped.
/// </summary>
public static class Credits
{
    public static List<CreditEntry> Read(ContentNode node)
    {
        node.RequireObject("credits");
        node.Only("entries");
        var entries = new List<CreditEntry>();
        foreach (ContentNode entry in node.At("entries").Items())
        {
            entry.RequireObject("a credit");
            entry.Only("name", "kind", "by", "licence", "text");
            string name = entry.At("name").AsName();
            if (entries.Exists(e => e.Name == name))
            {
                throw entry.Fail("name", $"\"{name}\" is listed twice");
            }
            entries.Add(new CreditEntry(name, entry.Text("kind", "Game"), entry.Text("by", ""), entry.Text("licence", ""), entry.Text("text", "")));
        }
        return entries;
    }
}
