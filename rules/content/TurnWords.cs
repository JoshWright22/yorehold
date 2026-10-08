namespace Yorehold.Rules;

/// <summary>
/// ruleset.json's "turnWords": what the system calls the parts of a turn, for costs on the
/// hotbar, the spell book and refusals ("2 actions", "bonus action", "reaction"). Fate's one
/// action is an "action" too, but a system can call it anything.
/// </summary>
public sealed class TurnWords
{
    public string Action { get; init; } = "action";
    public string Actions { get; init; } = "actions";
    public string Bonus { get; init; } = "bonus action";
    public string Reaction { get; init; } = "reaction";
    public string Free { get; init; } = "free";

    /// <summary>"1 action", "2 actions", "free".</summary>
    public string Cost(int actions) => actions <= 0 ? Free : actions == 1 ? $"1 {Action}" : $"{actions} {Actions}";

    /// <summary>The same with a capital, to start a line.</summary>
    public static string Capital(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];

    public static TurnWords Read(ContentNode? found)
    {
        var defaults = new TurnWords();
        if (found is not ContentNode node)
        {
            return defaults;
        }
        node.RequireObject("is an object of the turn's words: action, actions, bonus, reaction, free");
        node.Only("action", "actions", "bonus", "reaction", "free");
        return new TurnWords
        {
            Action = node.Text("action", defaults.Action, 32),
            Actions = node.Text("actions", defaults.Actions, 32),
            Bonus = node.Text("bonus", defaults.Bonus, 32),
            Reaction = node.Text("reaction", defaults.Reaction, 32),
            Free = node.Text("free", defaults.Free, 32),
        };
    }
}
