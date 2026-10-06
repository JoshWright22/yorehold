using System;
using System.Collections.Generic;
using System.Linq;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The journal (J): the chapter's quests and the companions met so far as a data panel. Tabs are
/// quests and companions, chips narrow it to what is open or in the party, and the entry is the
/// quest's page with its objectives ticked, or what a companion thinks of the party. It only reads.
/// </summary>
public sealed class JournalPanel
{
    private static readonly string[] Chips = { "Open", "Done", "In party" };
    private static readonly DataColumn[] Columns =
    {
        new("Name", 170), new("Kind", 80), new("Status", 80), new("Progress", 64, true),
    };

    private readonly DataPanel _view;

    public JournalPanel(DataPanel view)
    {
        _view = view;
    }

    public void Refresh(World world)
    {
        List<(Quest Quest, QuestProgress Progress)> quests = world.Chapter.Quests.Quests
            .Select(q => (q, q.Progress(world.Flags)))
            .Where(q => q.Item2.Status != QuestStatus.Hidden)
            .ToList();
        int open = quests.Count(q => q.Progress.Status == QuestStatus.Active);
        _view.SetHead("Journal", $"{world.Chapter.Title}, {open} open {(open == 1 ? "quest" : "quests")}");
        _view.SetSources(Array.Empty<(string, string)>(), "");
        _view.SetTabs(new[] { "All", "Quests", "Companions" });
        _view.SetChips(Chips);
        _view.SetColumns(Columns);

        var rows = new List<DataRow>();
        foreach ((Quest quest, QuestProgress progress) in quests)
        {
            int done = progress.ObjectiveDone.Count(d => d);
            string status = StatusText(progress.Status);
            var tags = new HashSet<string> { "Quests", progress.Status == QuestStatus.Active ? "Open" : "Done" };
            rows.Add(new DataRow
            {
                Key = "quest:" + quest.Id,
                Cells = new[] { quest.Title, "quest", status, $"{done}/{quest.Objectives.Count}" },
                Sort = new IComparable?[] { quest.Title, "quest", (int)progress.Status, done },
                Tags = tags,
                Search = quest.Description + " " + string.Join(" ", quest.Objectives.Select(o => o.Text)),
                Dim = progress.Status != QuestStatus.Active,
            });
        }
        foreach (CompanionDefinition companion in world.Companions.Definitions)
        {
            bool member = world.Companions.Member(companion.Id);
            int approval = world.Companions.Approval(companion.Id);
            var tags = new HashSet<string> { "Companions" };
            if (member)
            {
                tags.Add("In party");
            }
            rows.Add(new DataRow
            {
                Key = "companion:" + companion.Id,
                Cells = new[] { CompanionName(world, companion.Id), "companion", member ? "in party" : "met", approval.ToString("+0;-0;0") },
                Sort = new IComparable?[] { CompanionName(world, companion.Id), "companion", member ? 0 : 1, approval },
                Tags = tags,
            });
        }
        _view.SetRows(rows);

        string picked = _view.Picked;
        string page;
        if (picked.StartsWith("quest:", StringComparison.Ordinal) && quests.Find(q => "quest:" + q.Quest.Id == picked) is var found && found.Quest != null)
        {
            page = QuestPage(found.Quest, found.Progress);
        }
        else if (picked.StartsWith("companion:", StringComparison.Ordinal))
        {
            page = CompanionPage(world, picked["companion:".Length..]);
        }
        else
        {
            page = new BookPage().Note("Nothing yet. Quests show up here as the story moves.").ToString();
        }
        _view.SetEntry(page, Array.Empty<DataAction>(), "");
        _view.SetFoot(world.Companions.Members.Count > 0
            ? "With the party: " + string.Join(", ", world.Companions.Members.Select(id => CompanionName(world, id)))
            : "No companions with the party.");
    }

    private static string QuestPage(Quest quest, QuestProgress progress)
    {
        var page = new BookPage().Title(quest.Title).Sub(StatusText(progress.Status)).Rule();
        if (quest.Description.Length > 0)
        {
            page.Text(quest.Description).Gap();
        }
        page.Heading("Objectives");
        for (int i = 0; i < quest.Objectives.Count; i++)
        {
            page.Text((progress.ObjectiveDone[i] ? "[x] " : "[  ] ") + quest.Objectives[i].Text);
        }
        if (progress.Status == QuestStatus.Failed)
        {
            page.Gap().Warn("This quest can no longer be finished.");
        }
        return page.ToString();
    }

    private static string CompanionPage(World world, string id)
    {
        CompanionDefinition? d = world.Companions.Definition(id);
        if (d == null)
        {
            return new BookPage().Note("Gone.").ToString();
        }
        bool member = world.Companions.Member(id);
        int approval = world.Companions.Approval(id);
        var page = new BookPage().Title(CompanionName(world, id)).Sub(member ? "with the party" : "met on the way").Rule();
        page.Stats(("Approval", approval.ToString("+0;-0;0")), ("Joins at", d.JoinAt.ToString("+0;-0;0")));
        page.Stat("Leaves at", d.LeaveAt is int leave ? leave.ToString("+0;-0;0") : "only if sent away");
        page.Stat("Range", $"{world.Rules.Companions.ApprovalMin} to {world.Rules.Companions.ApprovalMax}");
        if (!member && approval < d.JoinAt)
        {
            page.Gap().Note("Not ready to join yet.");
        }
        return page.ToString();
    }

    private static string CompanionName(World world, string id)
    {
        return world.CompanionToken(id) is int token ? world.Creatures[token].Sheet.Name : id;
    }

    private static string StatusText(QuestStatus status) => status switch
    {
        QuestStatus.Completed => "complete",
        QuestStatus.Failed => "failed",
        _ => "open",
    };
}
