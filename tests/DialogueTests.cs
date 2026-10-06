namespace Yorehold.Rules.Tests;

/// <summary>Conversations, the journal and cutscene timing. The framework's DialogueTests, QuestTests and CutsceneTests, and the C++ client's talk checks.</summary>
public class DialogueTests
{
    private const string Story = """
        {"id":"gate", "start":"hello", "nodes":[
            {"id":"hello", "speaker":"Mara", "text":"The keep is closed.", "set":["met_guard"], "choices":[
                {"id":"ask", "text":"What happened?", "next":"hello", "set":["heard_rumor"], "forbid":["heard_rumor"]},
                {"id":"known", "text":"About those goblins...", "next":"open", "require":["heard_rumor"]},
                {"id":"convince", "text":"Let us help.", "set":["tried"],
                    "check":{"skill":"persuasion", "difficulty":12, "success":"open", "failure":"refused"}},
                {"id":"leave", "text":"Farewell.", "next":""}
            ]},
            {"id":"open", "text":"The gate opens.", "set":["gate_open"], "clear":["tried"]},
            {"id":"refused", "text":"Come back with proof.", "choices":[{"id":"back", "text":"Try again", "next":"hello"}]}
        ]}
        """;

    private static Dialogue Gate() => Dialogue.Read(TestContent.Json(Story));

    private static RollResult Rolled(int total, int natural = 10) => new RollResult { Expression = "1d20", Total = total, Dice = { new DieRoll(20, natural) } };

    [Fact]
    public void BranchesAndFlags()
    {
        var session = new DialogueSession(Gate(), Array.Empty<string>());
        Assert.True(session.Current!.Speaker == "Mara" && session.Flags.Contains("met_guard"));
        Assert.True(session.Choices().Count == 3 && !session.Finished);
        Assert.True(session.Choose("known") == null && session.Choose("missing") == null && session.Choose("convince") == null,
            "Hidden, unknown and unrolled replies change nothing");
        Assert.False(session.Flags.Contains("tried"));
        DialogueResult? ask = session.Choose("ask");
        Assert.True(ask != null && ask.From == "hello" && ask.To == "hello" && ask.Roll == null);
        Assert.True(session.Flags.Contains("heard_rumor") && session.Choices().Count == 3 && session.Choose("ask") == null);
        Assert.True(session.Choose("known") != null && session.Current!.Id == "open" && session.Finished);
        Assert.True(session.Flags.Contains("gate_open") && session.Choices().Count == 0 && session.Choose("leave") == null);
        session.Close();
        Assert.True(session.Current == null && session.Finished);

        var leave = new DialogueSession(Gate(), Array.Empty<string>());
        Assert.True(leave.Choose("leave") != null && leave.Current == null && leave.Finished);
        var returning = new DialogueSession(Gate(), new[] { "heard_rumor" });
        Assert.Equal("known", returning.Choices()[0].Id);
    }

    [Fact]
    public void ChecksUseTheTotal()
    {
        foreach (int total in new[] { 11, 12 })
        {
            var session = new DialogueSession(Gate(), Array.Empty<string>());
            DialogueResult? result = session.Choose("convince", skill =>
            {
                Assert.Equal("persuasion", skill);
                return Rolled(total);
            });
            Assert.True(result?.Roll?.Total == total && result.Passed == (total == 12));
            Assert.Equal(total == 12 ? "open" : "refused", session.Current!.Id);
            Assert.Equal(total == 11, session.Flags.Contains("tried"));
        }
        // natural 1 and 20 don't decide a skill check
        foreach (bool twenty in new[] { false, true })
        {
            var session = new DialogueSession(Gate(), Array.Empty<string>());
            DialogueResult? result = session.Choose("convince", _ => Rolled(twenty ? 11 : 12, twenty ? 20 : 1));
            Assert.Equal(!twenty, result!.Passed);
        }
    }

    [Fact]
    public void QuestsFollowTheFlags()
    {
        QuestJournal journal = QuestJournal.Read(TestContent.Json("""
            {"quests":[{"id":"gate","title":"Open the gate","require":["met_guard"],"fail":["gate_burned"],
              "objectives":[{"id":"talk","text":"Talk your way in","require":["gate_open"]},{"id":"key","text":"Find the key","require":["has_key"]}]}]}
            """));
        Quest quest = journal.Quests[0];
        var flags = new SortedSet<string>();
        Assert.Equal(QuestStatus.Hidden, quest.Progress(flags).Status);
        flags.Add("met_guard");
        flags.Add("gate_open");
        QuestProgress now = quest.Progress(flags);
        Assert.True(now.Status == QuestStatus.Active && now.ObjectiveDone.SequenceEqual(new[] { true, false }));
        flags.Add("has_key");
        Assert.Equal(QuestStatus.Completed, quest.Progress(flags).Status);
        flags.Add("gate_burned");
        Assert.Equal(QuestStatus.Failed, quest.Progress(flags).Status);
    }

    [Fact]
    public void TheJournalSaysWhatMoved()
    {
        using WorldFixture world = WorldFixture.Load("chapters/goblin-keep", 2);
        World w = world.World;
        Quest quest = w.Chapter.Quests.Quests[0];
        world.SetFlags(quest.Require.ToArray());
        Assert.True(world.Said($"New quest: {quest.Title}"), "A quest showing up is told");
        world.SetFlags(quest.Objectives[0].Require.ToArray());
        Assert.True(world.Said("Done: " + quest.Objectives[0].Text));
        world.SetFlags(quest.Objectives.SelectMany(o => o.Require).ToArray());
        Assert.True(world.Said("Quest complete: " + quest.Title) || quest.Fail.Any(w.Flags.Contains));
    }

    // Ana the fighter beside two NPCs: Mara at the gate and Pell who can be sent off or fought.
    private static WorldFixture TalkYard()
    {
        const string pell = """
            {"id":"pell","start":"hi","nodes":[{"id":"hi","text":"Well?","choices":[
                {"id":"go","text":"Leave.","do":["release"]},
                {"id":"die","text":"Die.","do":["kill"]},
                {"id":"fight","text":"Fight me.","do":["fight"]}]}]}
            """;
        return WorldFixture.Small(new[] { "########", "#A.....#", "#......#", "#......#", "########" },
            "\"npcs\":[{\"id\":\"mara\",\"name\":\"Mara\",\"at\":[3,1],\"dialogue\":\"gate.json\"},{\"id\":\"pell\",\"name\":\"Pell\",\"at\":[5,3],\"dialogue\":\"pell.json\",\"attacked\":[\"pell-attacked\"],\"killed\":[\"pell-dead\"]}]",
            files: new Dictionary<string, string> { ["gate.json"] = Story, ["pell.json"] = pell });
    }

    [Fact]
    public void TalkingToSomeone()
    {
        using WorldFixture world = TalkYard();
        World w = world.World;
        int mara = w.NpcStart, pell = w.NpcStart + 1;
        Assert.True(w.TalkTo(mara) && w.Talk == null && w.Tokens.Tokens[0].Path.Count > 0, "The leader walks over first");
        Assert.True(world.StepUntil(() => w.Talk != null, 5) && w.TalkingWith == mara && world.Said("Mara: The keep is closed."),
            "and the conversation opens beside them, its first line in the log");
        Assert.True(w.Flags.Contains("met_guard"), "The first line's flags are the story's");
        Assert.False(world.Go(0, new Cell(6, 3)), "Nobody walks off mid-conversation");
        Assert.True(world.Reply(0) && world.Said("Ana: What happened?") && w.Flags.Contains("heard_rumor"));
        Assert.True(world.Reply(0) && w.Talk!.Current!.Id == "open" && w.Flags.Contains("gate_open"));
        Assert.True(world.Reply(0) && w.Talk == null, "On the last line any reply ends it");
        world.Said("");
        Assert.Contains(world.Events, e => e.Kind == WorldEventKind.Save);

        Assert.True(world.TalkTo(pell) && world.Reply(0) && w.Creatures[pell].Fled && w.Creatures[pell].Sheet.Down && world.Said("Pell leaves."),
            "release: they go for good");
    }

    [Fact]
    public void PickingAFightThroughTalk()
    {
        using WorldFixture world = TalkYard();
        World w = world.World;
        int pell = w.NpcStart + 1;
        Assert.True(world.TalkTo(pell) && world.Reply(2) && w.Fighting && w.Creatures[pell].Team == 1 && w.Flags.Contains("pell-attacked")
            && w.Talk == null && world.Said("Pell fights back!"), "fight: they turn on the party, alone");

        using WorldFixture other = TalkYard();
        int doomed = other.World.NpcStart + 1;
        Assert.True(other.TalkTo(doomed) && other.Reply(1) && other.World.Creatures[doomed].Sheet.Down && other.World.Flags.Contains("pell-dead"),
            "kill: they die where they stand");

        using WorldFixture third = TalkYard();
        Assert.True(third.World.PickFight(third.World.NpcStart) && third.World.Fighting && third.World.Creatures[third.World.NpcStart].Team == 1,
            "The party can attack an NPC outright");
    }

    [Fact]
    public void CutscenesPlayInOrder()
    {
        Cutscene cutscene = Cutscene.Read(TestContent.Json("""
            {"steps": [
                {"bars": true},
                {"event": "music"},
                {"camera": [2000, 1000], "zoom": 2, "seconds": 2, "wait": false},
                {"caption": "The goblins are gone.", "seconds": 1},
                {"event": "halfway"},
                {"pause": 1},
                {"title": "The End", "seconds": 2},
                {"fade": [0, 0, 0, 255], "seconds": 1},
                {"event": "done"}
            ]}
            """));
        var events = new List<string>();
        var run = new CutsceneRun(cutscene, new System.Numerics.Vector2(1000, 1000), 1);
        Assert.True(run.Running);
        run.Update(0);
        events.AddRange(run.TakeEvents());
        Assert.Equal(new[] { "music" }, events); // bars and the first event are instant; the camera doesn't hold up the caption
        Assert.Single(run.Texts());
        run.Update(0.5);
        Assert.True(run.CameraPosition.X > 1000 && run.CameraPosition.X < 2000 && run.CameraSteered);
        run.Update(0.6); // the caption ends and "halfway" comes while the camera still moves
        events.AddRange(run.TakeEvents());
        Assert.True(events.Count == 2 && events[1] == "halfway" && run.CameraPosition.X < 2000);
        for (int i = 0; i < 100 && run.Running; i++)
        {
            run.Update(0.1);
        }
        events.AddRange(run.TakeEvents());
        Assert.True(!run.Running && Math.Abs(run.CameraPosition.X - 2000) < 0.01f && Math.Abs(run.CameraZoom - 2) < 0.001f);
        Assert.Equal(new[] { "music", "halfway", "done" }, events);
        Assert.True(run.Fade.A == 255 && run.Bars == 1, "It ends faded out with the bars in");

        var skipped = new CutsceneRun(cutscene, new System.Numerics.Vector2(1000, 1000), 1);
        skipped.Update(0.1);
        skipped.Skip();
        Assert.True(!skipped.Running && skipped.TakeEvents().SequenceEqual(new[] { "music", "halfway", "done" })
            && Math.Abs(skipped.CameraPosition.X - 2000) < 0.01f, "Skipping lands on the end and every event still comes out");

        Assert.False(new CutsceneRun(Cutscene.Read(TestContent.Json("""{"steps": []}""")), default, 1).Running);
        Assert.Equal(1, Ease.Apply("inOutCubic", 1));
        Assert.Equal(0.5f, Ease.Apply("linear", 0.5f));
    }
}
