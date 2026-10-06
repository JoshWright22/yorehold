using System.Numerics;

namespace Yorehold.Rules;

/// <summary>Everyone on the map with a sheet. Heroes come first, then the encounters' creatures, then NPCs.</summary>
public sealed class WorldCreature
{
    public WorldCreature(CharacterSheet sheet, int team, int group = -1)
    {
        Sheet = sheet;
        Team = team;
        Group = group;
    }

    public CharacterSheet Sheet { get; set; }
    /// <summary>0 = the party, 1 = enemies, 2 = neutral NPCs.</summary>
    public int Team { get; set; }
    /// <summary>Index into the chapter's encounters (enemies that wake together); NPCs count on after them.</summary>
    public int Group { get; }
    /// <summary>It has noticed the party and no longer keeps watch.</summary>
    public bool Awake { get; set; }
    /// <summary>Index into the chapter's NPCs, or -1.</summary>
    public int Npc { get; init; } = -1;
    /// <summary>Its definition in the compendium; empty for heroes.</summary>
    public string CreatureId { get; init; } = "";
    /// <summary>Radians: where an enemy looks until it notices the party.</summary>
    public float Facing { get; set; }
    /// <summary>It died and left what it carried as a pile.</summary>
    public bool Dropped { get; set; }

    /// <summary>The "ai" entries of its encounter and placement, laid over its creature file's in that order.</summary>
    public List<ContentNode> AiLayers { get; } = new();
    /// <summary>Its morale broke and it is running (for help, with BreakAs "alarm").</summary>
    public bool Fleeing { get; set; }
    /// <summary>How it took its morale breaking: "flee", "alarm", "surrender" or "fight". Empty until then.</summary>
    public string BreakAs { get; set; } = "";
    /// <summary>Gave up in a fight: out of it, standing, no longer an enemy.</summary>
    public bool Surrendered { get; set; }
    /// <summary>Got away: out of the adventure, not lying dead in it.</summary>
    public bool Fled { get; set; }
    /// <summary>The action a Ready recorded, for its reaction; empty when none.</summary>
    public string ReadiedAction { get; set; } = "";
    /// <summary>A hero's choices, which its sheet is built from; null for everyone else.</summary>
    public CharacterChoices? Choices { get; set; }
    /// <summary>The library file a brought character came from; empty for a ready-made hero.</summary>
    public string Library { get; set; } = "";
    /// <summary>The conversation it offers once it gives up in a fight.</summary>
    public string Surrender { get; init; } = "";
    /// <summary>The companion it is, when its NPC entry makes it one; empty otherwise.</summary>
    public string CompanionId { get; set; } = "";
    /// <summary>A companion's conversation when they came from another chapter, where it lives.</summary>
    public string CompanionTalk { get; set; } = "";
    /// <summary>The spell it holds in place, if any.</summary>
    public Concentration Concentration { get; set; } = new();
    /// <summary>A prepared caster may choose its spells: from the start until a fight, then after the rest the rules name.</summary>
    public bool MayPrepare { get; set; } = true;

    /// <summary>A hero moving quietly: slower, lights covered, only noticed inside a vision cone.</summary>
    public bool Sneaking => Sheet.HasCondition(World.HiddenCondition);
}

public enum WorldEventKind
{
    /// <summary>A new adventure began.</summary>
    Reset,
    /// <summary>Text is a line for the adventure log.</summary>
    Log,
    /// <summary>Text floats up from At.</summary>
    Floater,
    /// <summary>Text across the screen for Seconds.</summary>
    Banner,
    /// <summary>Text is the content path of a conversation to open (World.Talk has it open already).</summary>
    Talk,
    /// <summary>Text is the content path of a cutscene to play.</summary>
    Cutscene,
    /// <summary>A fight with Group started.</summary>
    Fight,
    /// <summary>Text is the name of whoever's turn it is now; At is where they stand.</summary>
    Turn,
    /// <summary>The fight is over. Text is "victory" or "defeat".</summary>
    FightOver,
    /// <summary>Another chapter's map took this one's place (travel, camp, a save); Text is its folder.</summary>
    ChapterChanged,
    /// <summary>A good moment to write the autosave; Text is the save's data.</summary>
    Save,
    /// <summary>A save was loaded; Text is the chapter's resume line.</summary>
    Resumed,
}

/// <summary>A library character taking a seat in place of the chapter's ready-made hero.</summary>
public sealed record PartyPick(CharacterChoices Choices, IReadOnlyList<Item> Inventory, string Library, int Coins);

/// <summary>Something for the screen to show.</summary>
public sealed record WorldEvent(WorldEventKind Kind, string Text = "")
{
    public Vector2 At { get; init; }
    public double Seconds { get; init; }
    public int Group { get; init; } = -1;
}

/// <summary>Player choices that change what the rules see.</summary>
public sealed class WorldOptions
{
    /// <summary>0 = as the map says, else 1 + LightingMode.</summary>
    public int Lighting { get; set; }
    /// <summary>0 = as the map says, else 1 + MapTime (Day, Dusk, Night).</summary>
    public int TimeOfDay { get; set; }
    /// <summary>The fog shows the whole party's view, not only the selected hero's.</summary>
    public bool SharedFog { get; set; } = true;
    /// <summary>The heroes' turns are played by the AI too (the "hero" profile).</summary>
    public bool AutoPlay { get; set; }
    /// <summary>A hero's reaction waits for React instead of being taken at once.</summary>
    public bool ReactionPrompts { get; set; }
}

/// <summary>
/// An adventure in progress: the map with its objects, the party and everyone else on it,
/// walking, what the party sees, sneaking, traps, flags and triggers, and fights (WorldFight and
/// the files beside it). It draws nothing and has no clock: time comes in through Update, what
/// the player does through calls that return false with a Refusal when the rules say no, and
/// what the screen should show goes out as events.
/// </summary>
public sealed partial class World
{
    /// <summary>The ruleset's conditions the world itself puts on creatures. A ruleset without one still plays.</summary>
    public const string HiddenCondition = "hidden";
    public const string DownedCondition = "downed";
    public const string DeadCondition = "dead";

    /// <summary>Token floor for the fallen.</summary>
    public const int DeadFloor = -1;
    /// <summary>Token floor for creatures the party can't see.</summary>
    public const int HiddenFloor = 1;
    public const int EnemyOwner = 1000;
    public const int NpcOwner = 1001;

    private readonly List<WorldEvent> _events = new();
    private readonly List<StealthTracker> _sneak = new();
    private readonly List<Vector2> _lastAt = new();
    private readonly HashSet<(int Hero, int Trap)> _trapsLookedAt = new();
    private Rng _stealthRandom = new(1);
    private ulong _seed;
    private ulong _rolls;
    private bool _won;

    public World(Chapter chapter, ulong seed, ContentFiles? files = null, Adventure? adventure = null)
    {
        Files = files;
        Adventure = adventure;
        HomeFolder = chapter.Folder;
        Chapter = chapter;
        Rules = chapter.Rules.Rules;
        Map = new MapState(chapter.Map);
        Fog = new FogOfWar(Map.Width, Map.Height, GameMap.CellSize);
        LightLevels = new LightLevels(Map.Width, Map.Height, GameMap.CellSize);
        if (files != null)
        {
            // The adventure's own camp, else the shared one when the content has it.
            string camp = adventure?.Camp ?? "";
            CampFolder = camp.Length > 0 ? camp : files.Exists("chapters/camp/chapter.json") ? "chapters/camp" : "";
        }
        NewAdventure(seed);
    }

    /// <summary>
    /// Loads the chapter in folder and starts it. A chapter the content's adventure.json lists plays
    /// as part of that adventure, which then has to load too.
    /// </summary>
    public static World Load(ContentFiles files, string folder, ulong seed)
    {
        Adventure? adventure = Adventure.ListedChapters(files).Contains(folder) ? Adventure.Load(files) : null;
        return new World(Chapter.Load(files, folder), seed, files, adventure);
    }

    public Chapter Chapter { get; private set; }
    public Ruleset Rules { get; private set; }
    /// <summary>Where other chapters are loaded from (travel, camp, saves); null for a World made from a Chapter alone.</summary>
    public ContentFiles? Files { get; }
    /// <summary>The adventure the chapter belongs to, or null when it plays on its own.</summary>
    public Adventure? Adventure { get; }
    /// <summary>The chapter the World started in.</summary>
    public string HomeFolder { get; }
    /// <summary>The chapter folder the party makes camp in; empty when there is none.</summary>
    public string CampFolder { get; } = "";
    public StealthRules StealthRules => Chapter.Rules.Stealth;
    public Grid Grid { get; } = new(GridType.Square, GameMap.CellSize);
    public MapState Map { get; private set; }
    public TokenMover Tokens { get; } = new();
    public FogOfWar Fog { get; private set; }
    public LightLevels LightLevels { get; private set; }
    public List<WorldCreature> Creatures { get; } = new();
    public int HeroCount { get; private set; }
    /// <summary>Where the chapter's NPCs start in Creatures.</summary>
    public int NpcStart { get; private set; }
    public SortedSet<string> Flags { get; } = new(StringComparer.Ordinal);
    /// <summary>Trigger ids that have fired in this adventure.</summary>
    public SortedSet<string> FiredTriggers { get; } = new(StringComparer.Ordinal);
    public WorldOptions Options { get; } = new();
    /// <summary>A trigger's or the ending's cutscene is playing: nothing else happens until EndCutscene.</summary>
    public bool InCutscene { get; private set; }
    /// <summary>The encounter the fight is with; null between fights.</summary>
    public int? FightGroup { get; private set; }
    /// <summary>A fight is on and not yet decided.</summary>
    public bool Fighting => Encounter != null && !Encounter.Finished;
    /// <summary>Why the last call that returned false said no, when there is a reason to give.</summary>
    public string Refusal { get; private set; } = "";

    public List<WorldEvent> TakeEvents()
    {
        List<WorldEvent> taken = new(_events);
        _events.Clear();
        return taken;
    }

    public void Say(string line)
    {
        _events.Add(new WorldEvent(WorldEventKind.Log, line));
    }

    /// <summary>Starts the chapter again from its files. Every roll that follows comes from seed.</summary>
    public void NewAdventure(ulong seed)
    {
        if (Chapter.Folder != HomeFolder && Files != null)
        {
            SwitchChapter(Chapter.Load(Files, HomeFolder));
        }
        _rolls = 0;
        _events.Add(new WorldEvent(WorldEventKind.Reset));
        Stash.Clear();
        RestsUsed.Clear();
        CampReturn = "";
        Companions.Clear();
        Begin(seed, false);
        Checkpoint = StateJson().ToJsonString();
    }

    // The chapter's people and things as its files place them. Quiet leaves out the intro, the
    // title and the triggers, for travel and saves, which say and fire those themselves.
    private void Begin(ulong seed, bool quiet)
    {
        _seed = seed;
        _won = false;
        Talk = null;
        TalkingWith = -1;
        _pendingTalk = null;
        _talkQueue.Clear();
        Fog = new FogOfWar(Map.Width, Map.Height, GameMap.CellSize);
        Tokens.ClearLinks();
        Tokens.Tokens.Clear();
        Tokens.Settings.InCombat = false;
        Tokens.Settings.ActiveTurn = null;
        Creatures.Clear();
        InCutscene = false;
        FightGroup = null;
        ResetFight();
        Flags.Clear();
        FiredTriggers.Clear();
        _trapsLookedAt.Clear();
        Map.ResetObjects();
        Surfaces.Clear();
        _roundClock = 0;
        FillContainers();
        FillMerchants();
        LightLevels.Ambient = Chapter.Map.Lighting.Ambient;
        LightLevels.BrightFraction = (float)Chapter.Map.Lighting.BrightFraction;
        LightLevels.SetFixed(Map.Lights, Map.Walls);

        var partyDice = new Rng(seed);
        foreach (PartyMember member in Chapter.Party)
        {
            WorldCreature hero = SeatHero(member, partyDice);
            Creatures.Add(hero);
            Tokens.Tokens.Add(new Token
            {
                Name = hero.Sheet.Name,
                Color = member.Color,
                Radius = GameMap.CellSize * 0.4f,
                Position = Grid.Center(member.At),
            });
        }
        HeroCount = Creatures.Count;
        Tokens.Tokens[0].Selected = true;

        for (int group = 0; group < Chapter.Encounters.Count; group++)
        {
            foreach (Placement placement in Chapter.Encounters[group].Creatures)
            {
                CreatureDefinition definition = Chapter.Compendium.Creature(placement.CreatureId)!;
                var creature = new WorldCreature(WorldSheets.Creature(Rules, Chapter.Compendium, definition, placement.Name), 1, group)
                {
                    CreatureId = placement.CreatureId,
                    Facing = FacingOf(placement),
                    Surrender = placement.Surrender.Length > 0 ? placement.Surrender
                        : Chapter.Encounters[group].Surrender.Length > 0 ? Chapter.Encounters[group].Surrender : Chapter.Surrender,
                };
                creature.AiLayers.AddRange(new[] { Chapter.Encounters[group].Ai, placement.Ai }.OfType<ContentNode>());
                Creatures.Add(creature);
                Tokens.Tokens.Add(new Token
                {
                    Name = creature.Sheet.Name,
                    Owner = EnemyOwner,
                    Color = definition.Token.Color,
                    Image = definition.Token.Image,
                    Radius = GameMap.CellSize * (float)definition.Token.Size,
                    Position = Grid.Center(placement.At),
                    Floor = HiddenFloor,
                });
            }
        }

        _sneak.Clear();
        _lastAt.Clear();
        for (int i = 0; i < HeroCount; i++)
        {
            _sneak.Add(StealthTracker.OnMap(StealthRules, Rules.FeetPerSquare));
            _lastAt.Add(Tokens.Tokens[i].Position);
        }
        _stealthRandom = new Rng(seed ^ 0x57ea1UL);

        NpcStart = Creatures.Count;
        for (int i = 0; i < Chapter.Npcs.Count; i++)
        {
            ChapterNpc npc = Chapter.Npcs[i];
            CreatureDefinition definition = Chapter.Compendium.Creature(npc.Creature)!;
            var creature = new WorldCreature(WorldSheets.Creature(Rules, Chapter.Compendium, definition, npc.Name), 2, Chapter.Encounters.Count + i)
            {
                Npc = i,
                CreatureId = npc.Creature,
                Surrender = Chapter.Surrender,
                CompanionId = npc.Companion != null ? npc.Id : "",
            };
            Creatures.Add(creature);
            Tokens.Tokens.Add(new Token
            {
                Name = npc.Name,
                Owner = NpcOwner,
                Color = npc.Color,
                Radius = GameMap.CellSize * (float)definition.Token.Size,
                Position = Grid.Center(npc.At),
                Floor = HiddenFloor,
            });
        }
        for (int i = 1; i < HeroCount; i++)
        {
            Tokens.Link(i, i - 1);
        }
        MeetCompanions();
        // Starting on an exit doesn't take anyone through it; stepping off and back on does.
        _heroMarker.Clear();
        for (int i = 0; i < HeroCount; i++)
        {
            _heroMarker.Add(ExitMarkerAt(CellOf(i)));
        }
        if (quiet)
        {
            return;
        }

        foreach (string line in Chapter.Intro)
        {
            Say(line);
        }
        _events.Add(new WorldEvent(WorldEventKind.Banner, Chapter.Title) { Seconds = 3 });
        CheckTriggers(onEnterOnly: true);
    }

    // Another chapter of the adventure (or camp) takes the place of this one. Begin fills it.
    private void SwitchChapter(Chapter next)
    {
        Chapter = next;
        Rules = next.Rules.Rules;
        Map = new MapState(next.Map);
        Fog = new FogOfWar(Map.Width, Map.Height, GameMap.CellSize);
        LightLevels = new LightLevels(Map.Width, Map.Height, GameMap.CellSize);
        _events.Add(new WorldEvent(WorldEventKind.ChapterChanged, next.Folder));
    }

    /// <summary>One step of time: walking, traps under foot or in sight, and what the party sees (which can wake enemies).</summary>
    public void Update(double deltaSeconds)
    {
        Walk(deltaSeconds);
        SurfaceClock(deltaSeconds);
        if (Fighting)
        {
            TakeTurns(deltaSeconds);
        }
        UpdateVisibility();
        WatchPendingTalk();
        WatchExits();
    }

    /// <summary>Everyone on the move takes their next steps.</summary>
    public void Walk(double deltaSeconds)
    {
        // A sneaking hero is slower, and so is one carrying too much. In a fight the squares they
        // may move already say so.
        for (int i = 0; i < HeroCount; i++)
        {
            float pace = !Fighting && Creatures[i].Sneaking ? (float)StealthRules.SneakSpeed : 1.0f;
            int weighed = Fighting ? 0 : Creatures[i].Sheet.Encumbrance(Rules);
            if (weighed > 0)
            {
                pace = weighed == 2 ? 0 : pace * (float)Rules.EncumberedSpeed;
            }
            Tokens.Tokens[i].Pace = pace;
        }
        Tokens.Advance(Grid, Walkable, deltaSeconds);
        WatchTraps();
    }

    // ---------------------------------------------------------------- who stands where

    public Cell CellOf(int creature)
    {
        Token token = Tokens.Tokens[creature];
        return Grid.CellAt(token.Path.Count == 0 ? token.Position : token.Path[^1]);
    }

    public bool Occupied(Cell cell, int except)
    {
        for (int i = 0; i < Creatures.Count; i++)
        {
            if (i != except && Tokens.Tokens[i].Floor != DeadFloor && CellOf(i) == cell)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Someone the party can talk to: an NPC standing and not fighting, one who gave up, or a companion with something to say.</summary>
    public bool Talkable(int creature)
    {
        if (creature < HeroCount || creature >= Creatures.Count)
        {
            return false;
        }
        WorldCreature c = Creatures[creature];
        if (Companion(creature))
        {
            return !c.Sheet.Down && DialogueFor(creature).Length > 0;
        }
        return c.Team == 2 && !c.Sheet.Down && (c.Npc >= 0 || (c.Surrendered && c.Surrender.Length > 0) || c.CompanionTalk.Length > 0);
    }

    public int? TalkerAt(Cell cell)
    {
        // a companion walks with the party, so it doesn't stand in its way
        for (int i = HeroCount; i < Creatures.Count; i++)
        {
            if (Talkable(i) && !Companion(i) && CellOf(i) == cell)
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>The map lets you stand there, nobody to talk to is in the way, and no trap the party knows of is armed there.</summary>
    public bool Walkable(Cell cell)
    {
        if (!Map.Walkable(cell) || TalkerAt(cell) != null)
        {
            return false;
        }
        WorldObject? found = Map.ObjectAt(cell);
        return found == null || !found.ArmedTrap || !found.TrapFound;
    }

    public bool PartyDown => HeroCount > 0 && Creatures.Take(HeroCount).All(c => c.Sheet.Down);

    /// <summary>The selected hero, else the first one standing.</summary>
    public int LeaderIndex()
    {
        for (int i = 0; i < HeroCount; i++)
        {
            if (Tokens.Tokens[i].Selected && !Creatures[i].Sheet.Down)
            {
                return i;
            }
        }
        for (int i = 0; i < HeroCount; i++)
        {
            if (!Creatures[i].Sheet.Down)
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>A hero walking to a square between fights; the others follow.</summary>
    public bool CanGo(int hero, Cell to)
    {
        if (hero < 0 || hero >= HeroCount || Creatures[hero].Sheet.Down || Fighting || InCutscene || Talk != null || !Walkable(to))
        {
            return false;
        }
        return Paths.Find(Grid, CellOf(hero), to, Walkable).Count > 0;
    }

    public bool Go(int hero, Cell to)
    {
        Refusal = "";
        if (!CanGo(hero, to))
        {
            Refusal = "There is no way there.";
            return false;
        }
        Token token = Tokens.Tokens[hero];
        List<Cell> path = Paths.Find(Grid, CellOf(hero), to, Walkable);
        token.Path.Clear();
        for (int i = 1; i < path.Count; i++)
        {
            token.Path.Add(Grid.Center(path[i]));
        }
        return true;
    }

    /// <summary>Puts a creature on a cell at once, for tests and for whatever moves people without walking.</summary>
    public void Place(int creature, Cell cell)
    {
        Tokens.Tokens[creature].Position = Grid.Center(cell);
        Tokens.Tokens[creature].Path.Clear();
    }

    // ---------------------------------------------------------------- flags, triggers and the chapter's end

    public void SetFlags(IEnumerable<string> flags)
    {
        var before = new SortedSet<string>(Flags, StringComparer.Ordinal);
        bool changed = false;
        foreach (string flag in flags)
        {
            changed |= Flags.Add(flag);
        }
        if (changed)
        {
            FlagsChanged(before);
        }
    }

    // Companions think about it, the journal says what moved, and triggers waiting on flags fire.
    private void FlagsChanged(IReadOnlySet<string> before)
    {
        CompanionFlags();
        foreach (Quest quest in Chapter.Quests.Quests)
        {
            QuestProgress was = quest.Progress(before);
            QuestProgress now = quest.Progress(Flags);
            if (now.Status == QuestStatus.Hidden)
            {
                continue;
            }
            if (was.Status == QuestStatus.Hidden)
            {
                Say($"New quest: {quest.Title} (J: journal)");
            }
            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                if (now.ObjectiveDone[i] && !was.ObjectiveDone[i] && now.Status != QuestStatus.Failed)
                {
                    Say("Done: " + quest.Objectives[i].Text);
                }
            }
            if (now.Status != was.Status && now.Status == QuestStatus.Completed)
            {
                Say("Quest complete: " + quest.Title);
                _events.Add(new WorldEvent(WorldEventKind.Banner, quest.Title) { Seconds = 2.5 });
            }
            else if (now.Status != was.Status && now.Status == QuestStatus.Failed)
            {
                Say("Quest failed: " + quest.Title);
            }
        }
        CheckTriggers(onEnterOnly: false);
    }

    public bool ChapterCleared()
    {
        if (Chapter.WinCondition != null)
        {
            return Chapter.WinCondition.When.All(Flags.Contains);
        }
        if (Chapter.CompleteWhen.Count > 0)
        {
            return Chapter.CompleteWhen.All(Flags.Contains);
        }
        // Every authored enemy is down (NPCs the party picked a fight with don't count).
        return Chapter.Encounters.Count > 0 && !Creatures.Skip(HeroCount).Any(c => c.Npc < 0 && c.Team == 1 && !c.Sheet.Down);
    }

    /// <summary>The cutscene a trigger asked for has finished or was skipped.</summary>
    public void EndCutscene()
    {
        InCutscene = false;
    }

    // Triggers without flags fire once when the chapter starts; the others once all their flags are set.
    private void CheckTriggers(bool onEnterOnly)
    {
        foreach (ChapterTrigger trigger in Chapter.Triggers)
        {
            bool onEnter = trigger.When.Count == 0;
            if (FiredTriggers.Contains(trigger.Id) || onEnter != onEnterOnly || !trigger.When.All(Flags.Contains))
            {
                continue;
            }
            FiredTriggers.Add(trigger.Id);
            Play(trigger.Dialogue, trigger.Cutscene);
        }

        // The win condition: once, when its flags are all set outside a fight.
        WinCondition? win = Chapter.WinCondition;
        if (win != null && !_won && !Fighting && !InCutscene && win.When.All(Flags.Contains))
        {
            _won = true;
            Say(Chapter.ClearedText);
            _events.Add(new WorldEvent(WorldEventKind.Banner, Chapter.ClearedText) { Seconds = 3 });
            Play(win.Dialogue, win.Cutscene);
        }
    }

    // Chapter.Load already read and checked both files.
    private void Play(string dialogue, string cutscene)
    {
        if (dialogue.Length > 0)
        {
            StartTalk(-1, dialogue);
        }
        if (cutscene.Length > 0)
        {
            _events.Add(new WorldEvent(WorldEventKind.Cutscene, cutscene));
            InCutscene = true;
        }
    }

    private Rng NextRandom(ulong salt)
    {
        return new Rng(_seed ^ salt ^ (++_rolls * 0x9e3779b97f4a7c15UL));
    }

    private float FacingOf(Placement placement)
    {
        if (placement.Facing is double degrees)
        {
            return (float)degrees * MathF.PI / 180;
        }
        if (Chapter.Party.Count == 0 || Chapter.Party[0].At == placement.At)
        {
            return 0;
        }
        // Nobody told it where to look: it watches the way the party comes from.
        Cell party = Chapter.Party[0].At;
        return MathF.Atan2(party.Y - placement.At.Y, party.X - placement.At.X);
    }

    // ---------------------------------------------------------------- doors, levers, locks, chests and traps

    /// <summary>
    /// A hero beside a door, lever, locked chest or found trap can use it. A chest that isn't
    /// locked is looted, not used.
    /// </summary>
    public bool CanInteract(int hero, int objectId, out string why)
    {
        why = "";
        WorldObject? o = Map.Get(objectId);
        if (o == null || o.Destroyed || hero < 0 || hero >= HeroCount || Creatures[hero].Sheet.Down)
        {
            return false;
        }
        if (!Usable(o))
        {
            return false;
        }
        Cell at = CellOf(hero);
        List<Cell> cells = MapState.CellsOf(o);
        if (!cells.Any(c => Math.Abs(c.X - at.X) <= 1 && Math.Abs(c.Y - at.Y) <= 1))
        {
            why = $"{Creatures[hero].Sheet.Name} is too far from {Called(o, "it")}.";
            return false;
        }
        // A door can't swing shut on someone standing in it.
        if (o.HasDoor && o.Open)
        {
            for (int i = 0; i < Creatures.Count; i++)
            {
                if (!Creatures[i].Sheet.Down && cells.Contains(CellOf(i)))
                {
                    why = "Something is in the way.";
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>A door, lever, locked chest or found trap: something Interact works on, from close enough.</summary>
    public bool Usable(WorldObject o)
    {
        bool hiddenTrap = o.Trap != null && !o.TrapFound;
        return !o.Destroyed
            && ((o.HasDoor && (!o.Has("container") || o.Locked)) || o.Has("lever") || o.Has("interactable") || (o.ArmedTrap && !hiddenTrap));
    }

    /// <summary>
    /// Walks a hero to the nearest square beside an object, so it can be used on arrival. True
    /// with no walking when they already stand beside it.
    /// </summary>
    public bool GoNear(int hero, int objectId)
    {
        Refusal = "";
        WorldObject? o = Map.Get(objectId);
        if (o == null || o.Destroyed || hero < 0 || hero >= HeroCount || Creatures[hero].Sheet.Down || Fighting || InCutscene)
        {
            Refusal = "Not now.";
            return false;
        }
        List<Cell> cells = MapState.CellsOf(o);
        Cell at = CellOf(hero);
        if (cells.Any(c => Math.Abs(c.X - at.X) <= 1 && Math.Abs(c.Y - at.Y) <= 1))
        {
            return true;
        }
        List<Cell>? best = null;
        foreach (Cell c in cells)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var beside = new Cell(c.X + dx, c.Y + dy);
                    if (cells.Contains(beside) || !Walkable(beside))
                    {
                        continue;
                    }
                    List<Cell> path = Paths.Find(Grid, at, beside, Walkable);
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
        return Go(hero, best[^1]);
    }

    /// <summary>Something beside the hero to use, the first by id.</summary>
    public int? ObjectNear(int hero)
    {
        foreach (WorldObject o in Map.Objects)
        {
            if (CanInteract(hero, o.Id, out _))
            {
                return o.Id;
            }
        }
        return null;
    }

    /// <summary>
    /// Uses an object: a door opens or shuts, a lever swings what it is linked to, a lock opens
    /// with the key its key:id tag names or a check, a found trap is disarmed with a check.
    /// </summary>
    public bool Interact(int hero, int objectId)
    {
        Refusal = "";
        if (Fighting || InCutscene || Talk != null)
        {
            Refusal = "Not now.";
            return false;
        }
        if (!CanInteract(hero, objectId, out string why))
        {
            Refusal = why;
            return false;
        }
        UseObject(hero, objectId);
        RequestSave();
        return true;
    }

    private void UseObject(int hero, int id)
    {
        WorldObject o = Map.Get(id)!; // CanInteract found it
        CharacterSheet sheet = Creatures[hero].Sheet;
        string name = Called(o, o.HasDoor ? "the door" : o.Has("lever") ? "the lever" : "it");
        bool used = false;

        int Check(string with, int dc, string what)
        {
            Rng dice = NextRandom(0x0b1ec7UL);
            RollResult roll = sheet.RollCheck(Rules, with, Advantage.None, dice);
            Say($"{sheet.Name} tries to {what} {name} ({with} DC {dc}): {roll.Describe()}");
            return roll.Total;
        }

        if (o.ArmedTrap && o.TrapFound)
        {
            int dc = o.Trap!.DisarmDc;
            int total = Check(CheckWith(o.Trap.DisarmSkill, "dex"), dc, "disarm");
            if (total >= dc)
            {
                Map.Disarm(id);
                Say($"{sheet.Name} disarms {name}.");
                used = true;
            }
            else if (total <= dc - 5)
            {
                SpringTrap(id, hero); // fumbled it
            }
            else
            {
                Say($"{sheet.Name} can't work out how to disarm {name}.");
            }
        }
        else
        {
            List<string> keys = sheet.Inventory.Select(item => "key:" + item.Id).ToList();
            bool wasLocked = o.IsLocked;
            Interaction result = Map.Interact(id, keys);
            if (result == Interaction.Locked && o.Lock != null && o.Lock.Dc > 0)
            {
                if (Check(CheckWith(o.Lock.Skill, "dex"), o.Lock.Dc, "pick the lock of") >= o.Lock.Dc)
                {
                    Map.Unlock(id);
                    result = Map.Interact(id, keys);
                }
                else
                {
                    Say($"{name} stays locked.");
                }
            }
            else if (result == Interaction.Locked)
            {
                Say($"{name} is locked. It needs a key.");
            }
            if (wasLocked && !o.IsLocked)
            {
                Say($"{sheet.Name} unlocks {name}.");
            }
            if (result == Interaction.Opened || result == Interaction.Closed)
            {
                Say($"{sheet.Name}{(result == Interaction.Opened ? " opens " : " closes ")}{name}.");
            }
            else if (result == Interaction.Activated)
            {
                Say($"{sheet.Name}{(o.Has("lever") ? " pulls " : " uses ")}{name}.");
            }
            used = result is Interaction.Opened or Interaction.Closed or Interaction.Activated;
        }
        // Story flags named on the object ("flag:gate-open") are set the first time it is used.
        if (used)
        {
            List<string> flags = o.Tags.Where(tag => tag.StartsWith("flag:", StringComparison.Ordinal) && tag.Length > 5).Select(tag => tag[5..]).ToList();
            if (flags.Count > 0)
            {
                SetFlags(flags);
            }
        }
        ObjectsChanged();
    }

    // A hero standing on an armed trap sets it off; one close by with their passive score above its DC finds it.
    private void WatchTraps()
    {
        if (InCutscene)
        {
            return;
        }
        for (int h = 0; h < HeroCount; h++)
        {
            if (Creatures[h].Sheet.Down || Tokens.Tokens[h].Floor == DeadFloor)
            {
                continue;
            }
            Cell at = CellOf(h);
            var standing = new Rect(at.X * GameMap.CellSize + 1, at.Y * GameMap.CellSize + 1, GameMap.CellSize - 2, GameMap.CellSize - 2);
            List<WorldObject> under = Map.TrapsIn(standing, 0);
            if (under.Count > 0)
            {
                SpringTrap(under[0].Id, h);
                return; // one at a time: the next step sees what is left
            }
            // Each pair is only looked at once, since standing there longer changes nothing about the score.
            foreach (WorldObject o in Map.Objects)
            {
                if (!o.ArmedTrap || o.TrapFound || _trapsLookedAt.Contains((h, o.Id)))
                {
                    continue;
                }
                Cell trapAt = MapState.CellOf(o);
                if (Math.Max(Math.Abs(trapAt.X - at.X), Math.Abs(trapAt.Y - at.Y)) > Chapter.Map.TrapSpotRange
                    || !Sight.LineOfSight(Grid.Center(at), Grid.Center(trapAt), Map.Walls))
                {
                    continue;
                }
                _trapsLookedAt.Add((h, o.Id));
                string skill = CheckWith(o.Trap!.DetectSkill, "perception");
                if (Rules.PassiveBase + Creatures[h].Sheet.CheckModifier(Rules, skill) >= o.Trap.DetectDc)
                {
                    o.TrapFound = true;
                    Say($"{Creatures[h].Sheet.Name} spots {Called(o, "a trap")}.");
                    _events.Add(new WorldEvent(WorldEventKind.Floater, "Trap") { At = Tokens.Tokens[h].Position });
                    return;
                }
            }
        }
    }

    private void SpringTrap(int id, int hero)
    {
        if (!Map.Spring(id) || hero >= HeroCount)
        {
            return;
        }
        WorldObject o = Map.Get(id)!; // Spring found it
        Say($"{Creatures[hero].Sheet.Name} sets off {Called(o, "a trap")}!");
        _events.Add(new WorldEvent(WorldEventKind.Floater, "Trap!") { At = Tokens.Tokens[hero].Position });
        Effect? effect = o.Trap!.Effect;
        if (effect != null && !effect.IsEmpty)
        {
            var context = new EffectContext(Rules, NextRandom(0x7a4b5UL))
            {
                Self = hero,
                Targets = new List<int> { hero },
                Source = o.Name,
                Dc = effect.Save.Dc,
            };
            EffectResult result = effect.Run(new WorldEffectHost(this), context);
            Narrate(result);
            AfterEffect(result);
        }
        ObjectsChanged();
    }

    private void ObjectsChanged()
    {
        Map.RefreshWalls();
        LightLevels.SetFixed(Map.Lights, Map.Walls);
    }

    // The skill or ability a check uses when the object names none, or one this ruleset lacks.
    private string CheckWith(string wanted, string fallback)
    {
        if (wanted.Length > 0 && (Rules.Skill(wanted) != null || Rules.Ability(wanted) != null))
        {
            return wanted;
        }
        return Rules.Skill(fallback) != null || Rules.Ability(fallback) != null ? fallback : Rules.Abilities.FirstOrDefault()?.Id ?? "";
    }

    private static string Called(WorldObject o, string otherwise) => o.Name.Length == 0 ? otherwise : o.Name;

    // ---------------------------------------------------------------- what the party sees, and sneaking

    public LightingMode CurrentLighting()
    {
        return Options.Lighting >= 1 && Options.Lighting <= 3 ? (LightingMode)(Options.Lighting - 1) : Chapter.Map.Lighting.Mode;
    }

    public MapTime CurrentTime()
    {
        // There's no sky to change underground.
        MapTime time = Chapter.Map.Lighting.Time;
        return time != MapTime.Underground && Options.TimeOfDay >= 1 && Options.TimeOfDay <= 3 ? (MapTime)(Options.TimeOfDay - 1) : time;
    }

    /// <summary>The fog view on screen: 0 = the party, 1 + i = hero i alone (the selected one).</summary>
    public int ViewTeam()
    {
        if (Options.SharedFog)
        {
            return 0;
        }
        for (int i = 0; i < HeroCount; i++)
        {
            if (Tokens.Tokens[i].Selected)
            {
                return i + 1;
            }
        }
        return 1;
    }

    /// <summary>Starts or stops the standing heroes sneaking (the Hidden condition).</summary>
    public bool Sneak(bool on)
    {
        Refusal = "";
        if (Fighting || InCutscene || !Creatures.Take(HeroCount).Any(c => !c.Sheet.Down))
        {
            Refusal = "Not now.";
            return false;
        }
        for (int i = 0; i < HeroCount; i++)
        {
            if (Creatures[i].Sheet.Down)
            {
                continue;
            }
            SetSneaking(i, on);
            _sneak[i].Reset();
        }
        Say(on ? "Sneaking: slower, with lights covered. Stay out of the red cones." : "No longer sneaking.");
        return true;
    }

    public bool AnySneaking => Creatures.Take(HeroCount).Any(c => c.Sneaking && !c.Sheet.Down);

    private void SetSneaking(int hero, bool on)
    {
        CharacterSheet sheet = Creatures[hero].Sheet;
        if (!on)
        {
            sheet.RemoveCondition(HiddenCondition);
        }
        else if (!sheet.HasCondition(HiddenCondition))
        {
            sheet.AddCondition(Rules, HiddenCondition);
        }
    }

    // The lights heroes carry, unless they are covered for sneaking.
    private List<WorldLight> CarriedLights()
    {
        var carried = new List<WorldLight>();
        float radius = (float)Chapter.Map.Lighting.Carried * GameMap.CellSize;
        for (int i = 0; i < HeroCount; i++)
        {
            if (Tokens.Tokens[i].Floor != DeadFloor && radius > 0 && !Creatures[i].Sneaking)
            {
                carried.Add(new WorldLight(Tokens.Tokens[i].Position, radius));
            }
        }
        return carried;
    }

    public LightLevel LightAt(Vector2 point)
    {
        if (CurrentLighting() != LightingMode.Rules)
        {
            return LightLevel.Bright;
        }
        Cell c = Grid.CellAt(point);
        Sky sky = Map.SkyAt(CurrentTime());
        if (sky.Differs && !Map.Indoors(c) && sky.Level != LightLevel.Dark)
        {
            return sky.Level;
        }
        return LightLevels.Level(c, CarriedLights(), Map.Walls);
    }

    /// <summary>
    /// One per creature after the heroes. Enemies that haven't noticed the party watch in a cone,
    /// as far as the heroes see; the rest don't watch (Range below 0).
    /// </summary>
    public List<Watcher> Watchers()
    {
        Sky sky = Map.SkyAt(CurrentTime());
        string perception = Rules.Skill("perception") != null ? "perception" : "wis";
        float perSquare = Math.Max(1, Rules.FeetPerSquare);
        var watching = new List<Watcher>();
        for (int i = HeroCount; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            float sight = (sky.Differs && Map.Indoors(CellOf(i)) ? (float)Chapter.Map.Lighting.Sight : sky.Sight) * GameMap.CellSize;
            watching.Add(new Watcher
            {
                Position = Tokens.Tokens[i].Position,
                Facing = c.Facing,
                PassivePerception = Rules.PassiveBase + c.Sheet.CheckModifier(Rules, perception),
                DarkRange = c.Sheet.Stats.Value("darkvision") / perSquare * GameMap.CellSize,
                Range = c.Team == 1 && !c.Awake && !c.Sheet.Down ? sight : -1,
            });
        }
        return watching;
    }

    // Wall cells are never in line of sight (their centre is behind the wall edge), so the ones
    // bordering what a view sees are shown too. Shut doors hide their own square the same way.
    private void RevealWalls(int team)
    {
        var doors = new HashSet<Cell>();
        foreach (WorldObject o in Map.Objects.Where(o => o.BlocksSight))
        {
            doors.UnionWith(MapState.CellsOf(o));
        }
        bool Solid(Cell c) => Map.BlocksSight(c) || doors.Contains(c);
        for (int y = 0; y < Map.Height; y++)
        {
            for (int x = 0; x < Map.Width; x++)
            {
                if (Solid(new Cell(x, y)) || Fog.State(team, 0, new Cell(x, y)) != FogState.Visible)
                {
                    continue;
                }
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var next = new Cell(x + dx, y + dy);
                        if (Map.Inside(next) && Solid(next))
                        {
                            Fog.Reveal(team, 0, next);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// What the party sees now, which creatures show, and, between fights, whether a watching
    /// enemy notices anyone.
    /// </summary>
    public void UpdateVisibility()
    {
        MapLighting lighting = Chapter.Map.Lighting;
        bool rules = CurrentLighting() == LightingMode.Rules;
        Sky sky = Map.SkyAt(CurrentTime());
        float perSquare = Math.Max(1, Rules.FeetPerSquare);
        var eyes = new Vision?[HeroCount];
        for (int i = 0; i < HeroCount; i++)
        {
            if (Tokens.Tokens[i].Floor == DeadFloor)
            {
                continue;
            }
            float darkvision = Creatures[i].Sheet.Stats.Value("darkvision") / perSquare * GameMap.CellSize;
            eyes[i] = new Vision(Tokens.Tokens[i].Position, sky.Sight * GameMap.CellSize, darkvision);
        }
        List<WorldLight> carried = CarriedLights();

        // In rules mode a cell is only seen if some light reaches it (or it's within darkvision).
        // By day the outdoors is lit by the sky and seen from far off; under a roof it's the usual
        // sight distance and the map's own lights.
        Func<Cell, bool>? lit = null;
        if (rules || sky.Differs)
        {
            lit = c =>
            {
                if (sky.Differs && !Map.Indoors(c))
                {
                    return !rules || sky.Level != LightLevel.Dark || LightLevels.Lit(c, carried, Map.Walls);
                }
                if (sky.Differs)
                {
                    Vector2 at = Grid.Center(c);
                    float reach = (float)lighting.Sight * GameMap.CellSize;
                    bool near = false;
                    for (int i = 0; i < HeroCount && !near; i++)
                    {
                        near = Tokens.Tokens[i].Floor != DeadFloor && Stealth.Length(at - Tokens.Tokens[i].Position) <= reach;
                    }
                    if (!near)
                    {
                        return false;
                    }
                }
                return !rules || LightLevels.Lit(c, carried, Map.Walls);
            };
        }

        // Team 0 is everyone's view together; it decides when enemies are spotted. With shared fog
        // off, each hero also keeps a view of their own (team 1 + index) for the screen.
        List<Vision> all = eyes.Where(e => e != null).Select(e => e!.Value).ToList();
        Fog.Update(0, 0, all, Map.Walls, lit);
        RevealWalls(0);
        if (!Options.SharedFog)
        {
            for (int i = 0; i < HeroCount; i++)
            {
                List<Vision> mine = eyes[i] is Vision own ? new List<Vision> { own } : new List<Vision>();
                Fog.Update(i + 1, 0, mine, Map.Walls, lit);
                RevealWalls(i + 1);
            }
        }
        int view = ViewTeam();
        for (int i = HeroCount; i < Creatures.Count; i++)
        {
            Token token = Tokens.Tokens[i];
            if (token.Floor == DeadFloor)
            {
                continue;
            }
            token.Floor = Fog.State(view, 0, CellOf(i)) == FogState.Visible ? 0 : HiddenFloor;
        }
        if (!Fighting && !PartyDown && !InCutscene && Talk == null)
        {
            UpdateStealth();
        }
    }

    // Walking openly, a hero is noticed as soon as they and a watching enemy see each other.
    // Sneaking, they roll Stealth against its passive Perception as they move through its cone.
    private void UpdateStealth()
    {
        List<Watcher> watching = Watchers();
        string stealth = Rules.Skill("stealth") != null ? "stealth" : "dex";
        int? noticed = null;
        string note = "";
        for (int h = 0; h < HeroCount; h++)
        {
            Vector2 at = Tokens.Tokens[h].Position;
            Vector2 from = _lastAt[h];
            _lastAt[h] = at;
            if (noticed != null || Tokens.Tokens[h].Floor == DeadFloor)
            {
                continue;
            }
            if (Stealth.Length(from - at) > 2 * GameMap.CellSize) // put somewhere else, not walked
            {
                from = at;
                _sneak[h].Reset();
            }
            if (!Creatures[h].Sneaking)
            {
                for (int w = 0; w < watching.Count && noticed == null; w++)
                {
                    if (watching[w].Range > 0 && Fog.State(0, 0, CellOf(HeroCount + w)) == FogState.Visible
                        && Stealth.Length(at - watching[w].Position) <= watching[w].Range + GameMap.CellSize / 2.0f
                        && Sight.LineOfSight(at, watching[w].Position, Map.Walls))
                    {
                        noticed = HeroCount + w;
                    }
                }
                continue;
            }
            int bonus = Creatures[h].Sheet.CheckModifier(Rules, stealth);
            foreach (StealthCheck check in _sneak[h].Move(from, at, true, bonus, watching, Map.Walls, _stealthRandom, LightAt))
            {
                if (!check.Spotted)
                {
                    _events.Add(new WorldEvent(WorldEventKind.Floater, "Unseen") { At = Tokens.Tokens[h].Position });
                    continue;
                }
                noticed = HeroCount + check.Watcher;
                note = $"{Creatures[noticed.Value].Sheet.Name} spots {Creatures[h].Sheet.Name}! (Stealth {check.Total} against {check.Dc})";
            }
        }
        if (noticed is int who)
        {
            Notice(Creatures[who].Group, note);
        }
    }

    /// <summary>
    /// An encounter's creatures noticed the party: the fight with them starts (StartFight), which
    /// wakes them, stops everyone walking and ends sneaking.
    /// </summary>
    public void Notice(int group, string note = "")
    {
        if (note.Length > 0)
        {
            Say(note);
        }
        StartFight(group);
    }
}
