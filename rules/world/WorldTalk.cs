namespace Yorehold.Rules;

// Conversations and companions: talking to NPCs, the replies and what they set off, companions
// joining and leaving, approval, and picking a fight with someone who wasn't an enemy.
public sealed partial class World
{
    private int? _pendingTalk;
    // conversations triggers opened while another was going on
    private readonly Queue<(int Creature, string Path)> _talkQueue = new();

    /// <summary>The conversation going on, if any. Nothing else happens until it ends.</summary>
    public DialogueSession? Talk { get; private set; }
    /// <summary>Who the party is talking to; -1 for a conversation a trigger or the chapter's end opened.</summary>
    public int TalkingWith { get; private set; } = -1;
    /// <summary>Approval and membership of every companion met so far.</summary>
    public Companions Companions { get; } = new();

    /// <summary>The conversation a creature offers: its surrender one once it gave up, its NPC one, or a companion's from home.</summary>
    public string DialogueFor(int creature)
    {
        WorldCreature c = Creatures[creature];
        if (c.Surrendered)
        {
            return c.Surrender;
        }
        return c.Npc >= 0 ? Chapter.Npcs[c.Npc].Dialogue : c.CompanionTalk;
    }

    /// <summary>
    /// The leader walks up to someone and talks to them on arrival (at once when already beside
    /// them). Between fights only.
    /// </summary>
    public bool TalkTo(int creature)
    {
        Refusal = "";
        if (!Calm || Talk != null || !Talkable(creature))
        {
            Refusal = "Not now.";
            return false;
        }
        int leader = LeaderIndex();
        if (Creatures[leader].Sheet.Down)
        {
            Refusal = "Not now.";
            return false;
        }
        Cell from = CellOf(leader);
        Cell goal = CellOf(creature);
        Token token = Tokens.Tokens[leader];
        token.Path.Clear();
        if (Grid.Distance(from, goal) > 1.5f)
        {
            List<Cell>? best = null;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var next = new Cell(goal.X + dx, goal.Y + dy);
                    if ((dx != 0 || dy != 0) && Walkable(next))
                    {
                        List<Cell> path = Paths.Find(Grid, from, next, Walkable);
                        if (path.Count > 0 && (best == null || path.Count < best.Count))
                        {
                            best = path;
                        }
                    }
                }
            }
            if (best == null)
            {
                Refusal = "There is no way there.";
                return false;
            }
            for (int i = 1; i < best.Count; i++)
            {
                token.Path.Add(Grid.Center(best[i]));
            }
        }
        _pendingTalk = creature;
        return true;
    }

    /// <summary>
    /// Picks a reply by its place among the replies on offer, spoken by hero. On the last line
    /// (no replies) any pick ends the conversation.
    /// </summary>
    public bool Reply(int index, int hero = -1)
    {
        Refusal = "";
        if (Talk == null)
        {
            return false;
        }
        List<DialogueChoice> choices = Talk.Choices();
        if (Talk.Finished || choices.Count == 0)
        {
            EndTalk();
            return true;
        }
        if (index < 0 || index >= choices.Count)
        {
            return false;
        }
        int speaker = hero >= 0 && hero < HeroCount ? hero : LeaderIndex();
        CharacterSheet sheet = Creatures[speaker].Sheet;
        DialogueChoice choice = choices[index];
        DialogueResult? result = Talk.Choose(choice.Id, skill =>
        {
            Rng dice = NextRandom(0x7a1cUL);
            return sheet.RollCheck(Rules, skill, Advantage.None, dice);
        });
        if (result == null)
        {
            return false;
        }
        Say($"{sheet.Name}: {choice.Text}");
        if (result.Roll != null && choice.Check != null)
        {
            Say($"{sheet.Name} rolls {choice.Check.Skill}: {result.Roll.Describe()} vs {choice.Check.Difficulty}{(result.Passed ? ", success" : ", failure")}");
        }
        SayLine();
        TakeTalkFlags();
        DialogueActions();
        if (Talk != null && Talk.Finished && Talk.Current == null)
        {
            EndTalk();
        }
        return true;
    }

    /// <summary>Walks away from the conversation where it stands.</summary>
    public void EndTalk()
    {
        if (Talk == null)
        {
            return;
        }
        Talk = null;
        TalkingWith = -1;
        if (_talkQueue.Count > 0)
        {
            (int creature, string path) = _talkQueue.Dequeue();
            StartTalk(creature, path);
            return;
        }
        if (ChapterCleared() && !_won && Chapter.WinCondition == null)
        {
            PlayEnding();
        }
        else
        {
            CheckTriggers(onEnterOnly: false); // a win condition waits for the talk to end
            RequestSave();
        }
    }

    /// <summary>
    /// The party attacks someone who wasn't an enemy: an NPC, or one who surrendered. They fight
    /// alone; the rest of their group is down, gone, or wakes when it sees the party.
    /// </summary>
    public bool PickFight(int creature)
    {
        Refusal = "";
        if (Fighting || InCutscene || creature < HeroCount || creature >= Creatures.Count || Creatures[creature].Sheet.Down
            || Creatures[creature].Team == 0 && !Companion(creature) || Creatures[creature].Team == 1)
        {
            Refusal = "Not now.";
            return false;
        }
        TurnHostile(creature);
        return true;
    }

    // Starts a conversation: with a creature on the map, or with nobody (-1) for one a trigger opened.
    private void StartTalk(int creature, string path)
    {
        if (Talk != null)
        {
            _talkQueue.Enqueue((creature, path)); // one at a time; it opens when this one ends
            return;
        }
        string name = creature >= 0 ? Creatures[creature].Sheet.Name : "";
        string problem = "";
        Dialogue? dialogue = path.Length == 0 ? null : FindDialogue(path, out problem);
        if (dialogue == null)
        {
            Say(path.Length == 0 ? $"{name} has nothing to say." : $"{(name.Length > 0 ? name + "'s d" : "D")}ialogue {path}: {problem}");
            return;
        }
        foreach (Token token in Tokens.Tokens)
        {
            token.Path.Clear();
        }
        _pendingTalk = null;
        TalkingWith = creature;
        Talk = new DialogueSession(dialogue, Flags);
        _events.Add(new WorldEvent(WorldEventKind.Talk, path));
        SayLine();
        TakeTalkFlags(); // the first line's own changes count too
        DialogueActions();
    }

    // The line just reached goes in the log too, so it can be read back later.
    private void SayLine()
    {
        if (Talk?.Current is DialogueNode node && node.Text.Length > 0)
        {
            string speaker = node.Speaker.Length > 0 ? node.Speaker : TalkingWith >= 0 ? Creatures[TalkingWith].Sheet.Name : "";
            Say(speaker.Length > 0 ? $"{speaker}: {node.Text}" : node.Text);
        }
    }

    // The chapter loaded its own conversations; a companion from another chapter brings theirs.
    private Dialogue? FindDialogue(string path, out string problem)
    {
        problem = "";
        if (Chapter.Dialogues.TryGetValue(path, out Dialogue? known))
        {
            return known;
        }
        if (Files == null)
        {
            problem = "not found";
            return null;
        }
        try
        {
            return Dialogue.Read(ContentNode.Read(Files, path));
        }
        catch (ContentException error)
        {
            problem = error.Message;
            return null;
        }
    }

    // The conversation's flags become the story's.
    private void TakeTalkFlags()
    {
        if (Talk == null || Talk.Flags.SetEquals(Flags))
        {
            return;
        }
        var before = new SortedSet<string>(Flags, StringComparer.Ordinal);
        Flags.Clear();
        Flags.UnionWith(Talk.Flags);
        FlagsChanged(before);
    }

    // What a conversation can make happen beyond story flags ("do" in the dialogue file):
    //   release  they leave for good (no body)
    //   kill     they die where they stand
    //   fight    they attack the party (again)
    //   recruit, dismiss, approve [id] <change>   companions
    private void DialogueActions()
    {
        if (Talk == null)
        {
            return;
        }
        int who = TalkingWith;
        foreach (string action in Talk.TakeActions())
        {
            if (who < 0)
            {
                CompanionAction(-1, action); // only "approve id n" means anything without someone to talk to
                continue;
            }
            WorldCreature c = Creatures[who];
            if (c.Sheet.Down)
            {
                break;
            }
            if (Companion(who) && action is "release" or "kill" or "fight")
            {
                CompanionLeaves(c.CompanionId);
            }
            if (action == "release")
            {
                Say($"{c.Sheet.Name} leaves.");
                c.Sheet.Hp = 0;
                c.Fled = true;
                Tokens.Tokens[who].Floor = DeadFloor;
            }
            else if (action == "kill")
            {
                Say($"{c.Sheet.Name} is killed.");
                c.Sheet.Hp = 0;
                Tokens.Tokens[who].Floor = DeadFloor;
                if (c.Npc >= 0)
                {
                    SetFlags(Chapter.Npcs[c.Npc].Killed);
                }
            }
            else if (action == "fight")
            {
                TurnHostile(who);
                return;
            }
            else
            {
                CompanionAction(who, action);
            }
        }
        FallenConditions();
    }

    private void TurnHostile(int creature)
    {
        WorldCreature them = Creatures[creature];
        Say(them.Sheet.Name + (them.Surrendered ? " takes up arms again!" : " fights back!"));
        if (Companion(creature))
        {
            CompanionLeaves(them.CompanionId);
        }
        them.Team = 1;
        them.Surrendered = false;
        Tokens.Unlink(creature);
        Tokens.Tokens[creature].Owner = EnemyOwner;
        if (them.Npc >= 0)
        {
            SetFlags(Chapter.Npcs[them.Npc].Attacked);
        }
        Talk = null;
        TalkingWith = -1;
        _pendingTalk = null;
        them.Awake = false;
        StartFight(them.Group, creature);
    }

    // The leader got there: the conversation starts.
    private void WatchPendingTalk()
    {
        if (_pendingTalk is not int creature)
        {
            return;
        }
        int leader = LeaderIndex();
        if (!Calm || Talk != null || !Talkable(creature))
        {
            _pendingTalk = null;
            return;
        }
        if (Tokens.Tokens[leader].Path.Count > 0)
        {
            return;
        }
        _pendingTalk = null;
        if (Grid.Distance(CellOf(leader), CellOf(creature)) <= 1.5f)
        {
            StartTalk(creature, DialogueFor(creature));
        }
    }

    // ---------------------------------------------------------------- companions

    /// <summary>An NPC on the party's side because they joined it.</summary>
    public bool Companion(int creature)
    {
        if (creature < HeroCount || creature >= Creatures.Count)
        {
            return false;
        }
        WorldCreature c = Creatures[creature];
        return c.Team == 0 && c.CompanionId.Length > 0 && Companions.Member(c.CompanionId);
    }

    /// <summary>A hero or a companion.</summary>
    public bool InParty(int creature) => creature >= 0 && creature < HeroCount || Companion(creature);

    public int? CompanionToken(string id)
    {
        for (int i = HeroCount; i < Creatures.Count; i++)
        {
            if (Creatures[i].CompanionId == id)
            {
                return i;
            }
        }
        return null;
    }

    // The chapter's companions become known to the roster.
    private void MeetCompanions()
    {
        foreach (ChapterNpc npc in Chapter.Npcs)
        {
            if (npc.Companion is CompanionDefinition definition)
            {
                Companions.Define(new CompanionDefinition
                {
                    Id = npc.Id, Approval = definition.Approval, JoinAt = definition.JoinAt,
                    LeaveAt = definition.LeaveAt, Flags = definition.Flags,
                });
            }
        }
    }

    // Companions walk at the back of the line behind the heroes.
    private void FollowParty()
    {
        if (HeroCount == 0)
        {
            return;
        }
        int leader = HeroCount - 1;
        for (int i = HeroCount; i < Creatures.Count; i++)
        {
            if (!Companion(i))
            {
                continue;
            }
            Tokens.Unlink(i);
            Tokens.Tokens[i].Owner = 0;
            if (!Creatures[i].Sheet.Down && Tokens.Link(i, leader))
            {
                leader = i;
            }
        }
    }

    private void JoinParty(int creature)
    {
        WorldCreature c = Creatures[creature];
        string name = c.Sheet.Name;
        CompanionJoin join = c.CompanionId.Length == 0 ? CompanionJoin.Unknown : Companions.Join(Rules.Companions, c.CompanionId, HeroCount);
        switch (join)
        {
            case CompanionJoin.Joined:
                c.Team = 0;
                c.Awake = true;
                c.Sheet.Death.Saves = true; // a companion goes down like a hero, not straight to dead
                Say($"{name} joins the party.");
                FollowParty();
                break;
            case CompanionJoin.Already:
                if (c.Team != 0)
                {
                    c.Team = 0;
                    FollowParty();
                }
                break;
            case CompanionJoin.LowApproval:
                Say($"{name} isn't ready to join yet.");
                break;
            case CompanionJoin.Full:
                Say($"The party is full. {name} can't join.");
                break;
            default:
                Say($"{name} won't join the party.");
                break;
        }
    }

    private void CompanionLeaves(string id)
    {
        Companions.Leave(id);
        if (CompanionToken(id) is not int found || Creatures[found].Team != 0)
        {
            return;
        }
        WorldCreature c = Creatures[found];
        c.Team = 2; // a neutral NPC again, where they stand
        Tokens.Unlink(found);
        Token token = Tokens.Tokens[found];
        token.Owner = NpcOwner;
        token.Path.Clear();
        token.Selected = false;
        Say($"{c.Sheet.Name} leaves the party.");
        FollowParty();
    }

    private void ChangeApproval(string id, int delta)
    {
        if (Companions.Definition(id) == null || delta == 0)
        {
            return;
        }
        int before = Companions.Approval(id);
        bool left = Companions.Adjust(Rules.Companions, id, delta);
        SayApproval(id, Companions.Approval(id) - before);
        if (left)
        {
            CompanionLeaves(id);
        }
    }

    private void SayApproval(string id, int change)
    {
        if (change == 0)
        {
            return;
        }
        string name = CompanionToken(id) is int found ? Creatures[found].Sheet.Name : id;
        Say(name + (change > 0 ? $" approves (+{change})." : $" disapproves ({change})."));
    }

    // Story flags the companions care about move their approval once each.
    private void CompanionFlags()
    {
        var before = Companions.Definitions.ToDictionary(d => d.Id, d => Companions.Approval(d.Id));
        List<string> left = Companions.FlagsSet(Rules.Companions, Flags);
        foreach ((string id, int was) in before)
        {
            SayApproval(id, Companions.Approval(id) - was);
        }
        foreach (string id in left)
        {
            CompanionLeaves(id);
        }
    }

    // recruit, dismiss, "approve 5" (the one talked to) and "approve wren -3" (anyone met).
    private void CompanionAction(int creature, string action)
    {
        string[] words = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return;
        }
        string self = creature >= 0 ? Creatures[creature].CompanionId : "";
        if (words[0] == "recruit" && words.Length == 1 && creature >= 0)
        {
            JoinParty(creature);
        }
        else if (words[0] == "dismiss" && words.Length == 1 && creature >= 0)
        {
            if (Companion(creature))
            {
                CompanionLeaves(self);
            }
        }
        else if (words[0] == "approve" && words.Length is 2 or 3)
        {
            string id = words.Length == 3 ? words[1] : self;
            string number = words[^1].StartsWith('+') ? words[^1][1..] : words[^1];
            if (id.Length > 0 && int.TryParse(number, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int delta))
            {
                ChangeApproval(id, delta);
            }
        }
    }
}
