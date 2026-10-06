namespace Yorehold.Rules;

/// <summary>One step of a quest: done once all its flags are set.</summary>
public record QuestObjective(string Id, string Text, List<string> Require);

public enum QuestStatus
{
    Hidden,
    Active,
    Completed,
    Failed,
}

/// <summary>Where a quest stands for some flags: its status and which objectives are done.</summary>
public sealed record QuestProgress(QuestStatus Status, List<bool> ObjectiveDone);

public class Quest
{
    /// <summary>Hidden until its require flags are set; failed once any fail flag is; done when every objective is.</summary>
    public QuestProgress Progress(IReadOnlySet<string> flags)
    {
        List<bool> done = Objectives.Select(o => o.Require.All(flags.Contains)).ToList();
        if (!Require.All(flags.Contains))
        {
            return new QuestProgress(QuestStatus.Hidden, done);
        }
        bool failed = Fail.Any(flags.Contains);
        bool completed = Objectives.Count > 0 && done.All(d => d);
        return new QuestProgress(failed ? QuestStatus.Failed : completed ? QuestStatus.Completed : QuestStatus.Active, done);
    }

    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Flags that make the quest appear; none = from the start.</summary>
    public List<string> Require { get; init; } = new();
    /// <summary>Any of these set fails it.</summary>
    public List<string> Fail { get; init; } = new();
    public List<QuestObjective> Objectives { get; init; } = new();
}

/// <summary>A chapter's journal file: quests and objectives that follow the story flags.</summary>
public class QuestJournal
{
    public List<Quest> Quests { get; init; } = new();

    public static QuestJournal Read(ContentNode node)
    {
        node.RequireObject("a quest file is a JSON object");
        ContentNode list = node.At("quests");
        if (!list.IsArray || list.Count > 512)
        {
            throw list.Fail("is a list of at most 512 quests");
        }
        var quests = new List<Quest>();
        foreach (ContentNode entry in list.Items())
        {
            entry.RequireObject("a quest is an object");
            var quest = new Quest
            {
                Id = entry.At("id").AsText(),
                Title = entry.At("title").AsText(),
                Description = entry.Text("description", ""),
                Require = entry.Texts("require"),
                Fail = entry.Texts("fail"),
            };
            if (quest.Id.Length == 0 || quest.Title.Length == 0 || quests.Any(q => q.Id == quest.Id))
            {
                throw entry.Fail($"a quest needs an id of its own and a title: \"{quest.Id}\"");
            }
            if (!Dialogue.DistinctFlags(quest.Require) || !Dialogue.DistinctFlags(quest.Fail))
            {
                throw entry.Fail("require and fail are lists of different flags");
            }
            ContentNode objectives = entry.At("objectives");
            if (!objectives.IsArray || objectives.Count == 0 || objectives.Count > 128)
            {
                throw objectives.Fail("is a list of 1 to 128 objectives");
            }
            foreach (ContentNode item in objectives.Items())
            {
                item.RequireObject("an objective is an object");
                var objective = new QuestObjective(item.At("id").AsText(), item.At("text").AsText(), item.At("require").Items().Select(f => f.AsText()).ToList());
                if (objective.Id.Length == 0 || objective.Text.Length == 0 || quest.Objectives.Any(o => o.Id == objective.Id))
                {
                    throw item.Fail("objectives need different ids and some text");
                }
                if (objective.Require.Count == 0 || !Dialogue.DistinctFlags(objective.Require))
                {
                    throw item.Fail("require", "needs at least one flag, each a different one");
                }
                quest.Objectives.Add(objective);
            }
            quests.Add(quest);
        }
        return new QuestJournal { Quests = quests };
    }
}
