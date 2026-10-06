namespace Yorehold.Rules;

/// <summary>What a node or a reply changes: story flags set and cleared, and "do" actions like "recruit".</summary>
public class DialogueChanges
{
    public List<string> Set { get; init; } = new();
    public List<string> Clear { get; init; } = new();
    public List<string> Actions { get; init; } = new();

    public static DialogueChanges Read(ContentNode node)
    {
        var changes = new DialogueChanges { Set = node.Texts("set"), Clear = node.Texts("clear"), Actions = node.Texts("do") };
        if (!Dialogue.DistinctFlags(changes.Set))
        {
            throw node.Fail("set", "flags are different and not empty");
        }
        if (!Dialogue.DistinctFlags(changes.Clear))
        {
            throw node.Fail("clear", "flags are different and not empty");
        }
        if (changes.Set.Intersect(changes.Clear).Any())
        {
            throw node.Fail("clear", "can't clear a flag it also sets");
        }
        if (changes.Actions.Any(action => action.Length == 0))
        {
            throw node.Fail("do", "actions can't be empty");
        }
        return changes;
    }
}

public record DialogueCheck(string Skill, int Difficulty, string Success, string Failure);

public class DialogueChoice
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>The node it leads to; empty ends the conversation.</summary>
    public string Next { get; init; } = "";
    public List<string> Require { get; init; } = new();
    public List<string> Forbid { get; init; } = new();
    public DialogueChanges Changes { get; init; } = new();
    public DialogueCheck? Check { get; init; }
}

public class DialogueNode
{
    public string Id { get; init; } = "";
    public string Speaker { get; init; } = "";
    public string Text { get; init; } = "";
    public DialogueChanges Changes { get; init; } = new();
    public List<DialogueChoice> Choices { get; init; } = new();
}

/// <summary>A conversation file: nodes with replies, flags and skill checks.</summary>
public class Dialogue
{
    public string Id { get; init; } = "";
    public string Start { get; init; } = "";
    public List<DialogueNode> Nodes { get; init; } = new();

    public DialogueNode? Node(string id) => Nodes.Find(node => node.Id == id);

    public static bool DistinctFlags(List<string> flags)
    {
        return flags.All(flag => flag.Length > 0) && flags.Distinct().Count() == flags.Count;
    }

    public static Dialogue Read(ContentNode node)
    {
        node.RequireObject("a dialogue is a JSON object");
        string id = node.At("id").AsText();
        if (id.Length == 0)
        {
            throw node.Fail("id", "is needed");
        }
        ContentNode list = node.At("nodes");
        if (!list.IsArray || list.Count == 0 || list.Count > 4096)
        {
            throw list.Fail("is a list of 1 to 4096 nodes");
        }

        var nodes = new List<DialogueNode>();
        var sources = new List<ContentNode>();
        foreach (ContentNode entry in list.Items())
        {
            entry.RequireObject("a node is an object");
            string nodeId = entry.At("id").AsText();
            if (nodeId.Length == 0 || nodes.Any(n => n.Id == nodeId))
            {
                throw entry.Fail("id", $"empty or used twice: \"{nodeId}\"");
            }
            var choices = new List<DialogueChoice>();
            if (entry.Get("choices") is ContentNode choiceList)
            {
                if (!choiceList.IsArray || choiceList.Count > 128)
                {
                    throw choiceList.Fail("is a list of at most 128 replies");
                }
                foreach (ContentNode item in choiceList.Items())
                {
                    item.RequireObject("a reply is an object");
                    var choice = new DialogueChoice
                    {
                        Id = item.At("id").AsText(),
                        Text = item.At("text").AsText(),
                        Next = item.Text("next", ""),
                        Require = item.Texts("require"),
                        Forbid = item.Texts("forbid"),
                        Changes = DialogueChanges.Read(item),
                        Check = item.Get("check") is ContentNode check
                            ? new DialogueCheck(check.At("skill").AsText(), check.At("difficulty").AsInt(0, 100000),
                                check.At("success").AsText(), check.At("failure").AsText())
                            : null,
                    };
                    if (choice.Id.Length == 0 || choice.Text.Length == 0 || choices.Any(c => c.Id == choice.Id))
                    {
                        throw item.Fail("replies need different ids and some text");
                    }
                    if (!DistinctFlags(choice.Require) || !DistinctFlags(choice.Forbid) || choice.Require.Intersect(choice.Forbid).Any())
                    {
                        throw item.Fail("require and forbid are lists of different flags that don't overlap");
                    }
                    if (choice.Check != null && choice.Next.Length > 0)
                    {
                        throw item.Fail("next", "a reply with a check goes to its success or failure, not next");
                    }
                    if (choice.Check != null && choice.Check.Skill.Length == 0)
                    {
                        throw item.Fail("check.skill", "is needed");
                    }
                    choices.Add(choice);
                }
            }
            nodes.Add(new DialogueNode
            {
                Id = nodeId,
                Speaker = entry.Text("speaker", ""),
                Text = entry.Text("text", ""),
                Changes = DialogueChanges.Read(entry),
                Choices = choices,
            });
            sources.Add(entry);
        }

        var dialogue = new Dialogue { Id = id, Start = node.At("start").AsText(), Nodes = nodes };
        if (dialogue.Node(dialogue.Start) == null)
        {
            throw node.Fail("start", $"no node \"{dialogue.Start}\"");
        }
        bool Exists(string target) => target.Length == 0 || dialogue.Node(target) != null;
        for (int n = 0; n < nodes.Count; n++)
        {
            for (int c = 0; c < nodes[n].Choices.Count; c++)
            {
                DialogueChoice choice = nodes[n].Choices[c];
                string path = $"{sources[n].Path}.choices[{c}]";
                if (choice.Check == null && !Exists(choice.Next))
                {
                    throw new ContentException(node.File, path + ".next", $"no node \"{choice.Next}\"");
                }
                if (choice.Check != null && (!Exists(choice.Check.Success) || !Exists(choice.Check.Failure)))
                {
                    throw new ContentException(node.File, path + ".check", "success or failure names a node that isn't there");
                }
            }
        }
        return dialogue;
    }
}
