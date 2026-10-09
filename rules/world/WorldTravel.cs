namespace Yorehold.Rules;

// Moving between the chapters of an adventure (exit markers), and camp: a small map the party goes
// to between fights and comes back from to the same place. The stash, revival for coins and the
// rests the ruleset keeps for camp are only there.
public sealed partial class World
{
    // the exit marker each hero stands on, so arriving on one doesn't send them straight back
    private readonly List<string> _heroMarker = new();

    /// <summary>Uses of each rest so far in the adventure, by rest id.</summary>
    public SortedDictionary<string, int> RestsUsed { get; } = new(StringComparer.Ordinal);
    /// <summary>The party's chest at camp.</summary>
    public Stash Stash { get; } = new();
    /// <summary>At camp: the chapter as the party left it, to go back to. Empty anywhere else.</summary>
    public string CampReturn { get; private set; } = "";

    public bool AtCamp => CampFolder.Length > 0 && Chapter.Folder == CampFolder;
    public bool CanLeaveCamp => AtCamp && CampReturn.Length > 0 && Quiet;

    // Nothing going on: no fight, cutscene or conversation, and someone standing.
    private bool Quiet => !Fighting && !InCutscene && Talk == null && !PartyDown && !PartyWiped;

    // ---------------------------------------------------------------- travel

    /// <summary>The exit marker on a cell, when a transition of the adventure leaves from it.</summary>
    public string ExitMarkerAt(Cell cell)
    {
        if (Adventure == null)
        {
            return "";
        }
        foreach (Transition t in Adventure.Transitions)
        {
            if (t.FromChapter == Chapter.Id && Chapter.Map.Marker(t.ExitMarker) == cell)
            {
                return t.ExitMarker;
            }
        }
        return "";
    }

    // A hero stepping onto an open exit takes the party on. A closed way is just a square; stepping
    // off and on again tries it again.
    private void WatchExits()
    {
        if (Adventure == null)
        {
            return;
        }
        while (_heroMarker.Count < HeroCount)
        {
            _heroMarker.Add("");
        }
        for (int h = 0; h < HeroCount; h++)
        {
            string marker = Creatures[h].Sheet.Down || Tokens.Tokens[h].Path.Count > 0 ? "" : ExitMarkerAt(CellOf(h));
            if (marker == _heroMarker[h])
            {
                continue;
            }
            _heroMarker[h] = marker;
            if (marker.Length > 0 && Quiet && Adventure.TransitionFrom(Chapter.Id, marker, Flags) is Transition way)
            {
                Travel(way.ToChapter, way.EntryMarker);
                return;
            }
        }
    }

    /// <summary>A milestone: every living hero goes up a level, as far as the system's table goes.</summary>
    public void Milestone()
    {
        for (int h = 0; h < HeroCount; h++)
        {
            CharacterSheet sheet = Creatures[h].Sheet;
            if (sheet.Death.Dead || sheet.Level >= Rules.MaxLevel)
            {
                continue;
            }
            // the experience the next level asks for, so the level follows it as it always has
            sheet.AddXp(Rules, Math.Max(0, Rules.XpForLevel[sheet.Level - 1] - sheet.Xp));
            Say($"{sheet.Name} reaches level {sheet.Level}.");
            GainLevels(h);
        }
    }

    private void Travel(string toChapter, string entryMarker)
    {
        Chapter next;
        try
        {
            next = Chapter.Load(Files!, Adventure!.FolderOf(toChapter)); // an adventure always has its files
        }
        catch (ContentException error)
        {
            Say("The way on can't be opened: " + error.Message);
            return;
        }
        if (Rules.Advancement == "milestone")
        {
            Milestone();
        }
        Carried carried = Carry();
        foreach (string local in Chapter.LocalFlags)
        {
            carried.Flags.Remove(local);
        }
        SwitchChapter(next);
        Begin(_seed, true);
        PutBack(carried);
        CompanionFlags(); // companions met here think about what the party did before
        GatherAt(Chapter.Map.Marker(entryMarker) ?? Chapter.Party[0].At); // Adventure.Load checked the marker
        Arrived();
        Say($"The party goes on to {Chapter.Title}.");
        foreach (string line in Chapter.Intro)
        {
            Say(line);
        }
        _events.Add(new WorldEvent(WorldEventKind.Banner, Chapter.Title) { Seconds = 3 });
        CheckTriggers(onEnterOnly: true);
        CheckTriggers(onEnterOnly: false);
        Checkpoint = StateJson().ToJsonString();
        RequestSave();
    }

    // ---------------------------------------------------------------- camp

    public bool CanMakeCamp(out string why)
    {
        why = "";
        if (CampFolder.Length == 0 || Files == null)
        {
            why = "There is nowhere to make camp.";
        }
        else if (AtCamp)
        {
            why = "The party is already at camp.";
        }
        else if (!Chapter.CampAllowed)
        {
            why = "The party can't make camp here.";
        }
        else if (!Quiet)
        {
            why = "Not now.";
        }
        return why.Length == 0;
    }

    /// <summary>The party goes to camp, keeping the chapter as it left it to come back to.</summary>
    public bool MakeCamp()
    {
        Refusal = "";
        if (!CanMakeCamp(out string why))
        {
            Refusal = why;
            return false;
        }
        Chapter camp;
        try
        {
            camp = LoadChapterFor(CampFolder, HeroCount);
        }
        catch (ContentException error)
        {
            Refusal = "Camp can't be made: " + error.Message;
            Say(Refusal);
            return false;
        }
        EndAllConcentration();
        string away = StateJson().ToJsonString();
        Carried carried = Carry();
        SwitchChapter(camp);
        Begin(_seed, true);
        PutBack(carried);
        CampReturn = away;
        GatherAt(Chapter.Map.Marker("entry") ?? Chapter.Party[0].At);
        Arrived();
        Say($"The party makes camp. Supplies: {SuppliesHeld()}.");
        _events.Add(new WorldEvent(WorldEventKind.Banner, Chapter.Title) { Seconds = 2 });
        Checkpoint = StateJson().ToJsonString();
        RequestSave();
        return true;
    }

    /// <summary>The party breaks camp and goes back to where it was.</summary>
    public bool LeaveCamp()
    {
        Refusal = "";
        if (!CanLeaveCamp)
        {
            Refusal = AtCamp ? "Not now." : "The party isn't at camp.";
            return false;
        }
        Carried carried = Carry();
        string back = CampReturn;
        if (!Restore(back))
        {
            Say("The way back can't be found: " + Refusal);
            return false;
        }
        PutBack(carried);
        CampReturn = "";
        Say($"The party breaks camp and heads back to {Chapter.Title}.");
        Checkpoint = StateJson().ToJsonString();
        RequestSave();
        return true;
    }

    /// <summary>Supply points in the stash and the living heroes' packs.</summary>
    public int SuppliesHeld() => Stash.SupplyPoints(Stash, LivingHeroSheets());

    /// <summary>Uses left of a rest in this adventure; -1 when unlimited.</summary>
    public int RestsLeft(RestDefinition rest)
    {
        if (rest.PerAdventure == 0)
        {
            return -1;
        }
        return Math.Max(0, rest.PerAdventure - RestsUsed.GetValueOrDefault(rest.Id));
    }

    public bool CanRest(RestDefinition rest, out string why)
    {
        why = "";
        string name = rest.Name.Length == 0 ? rest.Id : rest.Name;
        if (!Quiet)
        {
            why = "Not now.";
        }
        else if (RestsLeft(rest) == 0)
        {
            why = $"No {name} left.";
        }
        else if (rest.CampOnly && !AtCamp)
        {
            why = $"Make camp first to take a {name}.";
        }
        else if (SuppliesHeld() < rest.SupplyCost)
        {
            why = $"Not enough supplies for a {name}: {SuppliesHeld()} of {rest.SupplyCost}.";
        }
        return why.Length == 0;
    }

    /// <summary>The party takes a rest from the ruleset: HP, the resources it names, spells to prepare again.</summary>
    public bool Rest(string id)
    {
        Refusal = "";
        RestDefinition? rest = Rules.Rest(id);
        if (rest == null)
        {
            Refusal = "There is no such rest.";
            return false;
        }
        if (!CanRest(rest, out string why))
        {
            Refusal = why;
            return false;
        }
        RestsUsed[rest.Id] = RestsUsed.GetValueOrDefault(rest.Id) + 1;
        foreach (string other in rest.Resets)
        {
            RestsUsed.Remove(other);
        }
        Rng dice = NextRandom(0x5eedUL);
        Say($"The party takes a {(rest.Name.Length == 0 ? rest.Id : rest.Name)}.");
        if (rest.SupplyCost > 0)
        {
            Stash.SpendSupplies(Stash, LivingHeroSheets(), rest.SupplyCost);
            Say($"It uses {rest.SupplyCost} supplies; {SuppliesHeld()} left.");
        }
        EndAllConcentration(); // nobody holds a spell through a rest
        for (int i = 0; i < Creatures.Count; i++)
        {
            if (!InParty(i))
            {
                continue;
            }
            CharacterSheet c = Creatures[i].Sheet;
            // a class may get more back from this rest than everyone does (a warlock's slots on a short rest)
            List<string> restores = rest.Restores.Concat((Creatures[i].Choices?.Levels ?? new List<LevelChoice>()).Select(l => l.ClassId).Distinct()
                .SelectMany(id => Chapter.Compendium.Class(id)?.Restores.GetValueOrDefault(rest.Id) ?? new List<string>())).Distinct().ToList();
            if (!c.Death.Dead && c.RestoreResources(restores) > 0)
            {
                Say($"{c.Name} is ready to cast again.");
            }
            if (!c.Death.Dead && c.Preparable.Count > 0 && SpellRules.PrepareAfter.Contains(rest.Id))
            {
                if (!Creatures[i].MayPrepare)
                {
                    Say($"{c.Name} may prepare spells again (K).");
                }
                Creatures[i].MayPrepare = true;
            }
            c.ClearTracks(rest.Id);
            if (c.HealWounds(Rules, rest.Id))
            {
                Say($"{c.Name} is no longer wounded.");
            }
            if (c.Hp >= c.MaxHp)
            {
                continue;
            }
            int healed = c.Recover(Rules, rest.Recovery, dice);
            if (healed <= 0)
            {
                continue;
            }
            Tokens.Tokens[i].Floor = 0;
            Say($"{c.Name} recovers {healed} HP.");
            _events.Add(new WorldEvent(WorldEventKind.Floater, $"+{healed}") { At = Tokens.Tokens[i].Position });
        }
        Raise("rest");
        FallenConditions();
        if (RestsLeft(rest) is int left && left >= 0)
        {
            Say($"{left} left.");
        }
        RequestSave();
        return true;
    }

    /// <summary>A hero puts an entry of their pack in the camp stash.</summary>
    public bool ToStash(int hero, int item)
    {
        Refusal = "";
        if (!AtCamp || !Quiet || hero < 0 || hero >= HeroCount || Creatures[hero].Sheet.Death.Dead
            || item < 0 || item >= Creatures[hero].Sheet.Inventory.Count)
        {
            Refusal = AtCamp ? "Not now." : "The stash is at camp.";
            return false;
        }
        CharacterSheet sheet = Creatures[hero].Sheet;
        Item stored = sheet.TakeOut(item);
        Say($"{sheet.Name} puts {stored.Name}{(stored.Quantity > 1 ? $" x{stored.Quantity}" : "")} in the stash.");
        Stash.Add(stored);
        RequestSave();
        return true;
    }

    /// <summary>A hero takes an entry out of the camp stash.</summary>
    public bool FromStash(int hero, int item)
    {
        Refusal = "";
        if (!AtCamp || !Quiet || hero < 0 || hero >= HeroCount || Creatures[hero].Sheet.Down || item < 0 || item >= Stash.Items.Count)
        {
            Refusal = AtCamp ? "Not now." : "The stash is at camp.";
            return false;
        }
        CharacterSheet sheet = Creatures[hero].Sheet;
        if (Stash.Items[item].Magic && !sheet.RoomForMagic(Rules, Math.Max(1, Stash.Items[item].Quantity)))
        {
            Refusal = MagicLimitText(hero);
            return false;
        }
        Item taken = Stash.Take(item, 0)!; // the index was checked
        Say($"{sheet.Name} takes {taken.Name}{(taken.Quantity > 1 ? $" x{taken.Quantity}" : "")} from the stash.");
        Loot.AddTo(sheet, taken);
        RequestSave();
        return true;
    }

    public bool CanRevive(int payer, int target, out string why)
    {
        why = "";
        if (!AtCamp || !Quiet)
        {
            why = "The dead can only be brought back at camp.";
        }
        else if (Rules.RevivePrice <= 0)
        {
            why = "Nobody here can bring back the dead.";
        }
        else if (payer < 0 || payer >= HeroCount || target < 0 || target >= HeroCount || Creatures[payer].Sheet.Down)
        {
            why = "Not now.";
        }
        else if (!Creatures[target].Sheet.Death.Dead)
        {
            why = $"{Creatures[target].Sheet.Name} isn't dead.";
        }
        else if (Creatures[payer].Sheet.Coins < Rules.RevivePrice)
        {
            why = $"{Creatures[payer].Sheet.Name} needs {Coins.Text(Rules.RevivePrice)} to pay for it.";
        }
        return why.Length == 0;
    }

    /// <summary>A hero pays the ruleset's price to bring a dead one back, at camp.</summary>
    public bool Revive(int payer, int target)
    {
        Refusal = "";
        if (!CanRevive(payer, target, out string why))
        {
            Refusal = why;
            return false;
        }
        CharacterSheet pays = Creatures[payer].Sheet;
        CharacterSheet back = Creatures[target].Sheet;
        pays.Coins -= Rules.RevivePrice;
        back.Revive(Rules, Rules.ReviveHp);
        back.RemoveCondition(DeadCondition);
        Tokens.Tokens[target].Floor = 0;
        FallenConditions();
        Say($"{pays.Name} pays {Coins.Text(Rules.RevivePrice)} and {back.Name} is brought back with {back.Hp} HP.");
        _events.Add(new WorldEvent(WorldEventKind.Floater, $"+{back.Hp}") { At = Tokens.Tokens[target].Position });
        RequestSave();
        return true;
    }

    // ---------------------------------------------------------------- what goes along

    private sealed class Carried
    {
        public List<WorldCreature> Heroes { get; } = new();
        public List<ContentColor> Colors { get; } = new();
        public SortedSet<string> Flags { get; } = new(StringComparer.Ordinal);
        public List<string> Fired { get; } = new();
        public List<(WorldCreature Creature, Token Token)> Along { get; } = new();
    }

    // The heroes and companions as they are, and the story so far. Rests used, the stash, the
    // roster and the dice counters stay on the World itself.
    private Carried Carry()
    {
        var c = new Carried();
        for (int i = 0; i < HeroCount; i++)
        {
            c.Heroes.Add(Creatures[i]);
            c.Colors.Add(Tokens.Tokens[i].Color);
        }
        c.Flags.UnionWith(Flags);
        c.Fired.AddRange(FiredTriggers);
        c.Along.AddRange(CompanionsAlong());
        return c;
    }

    private void PutBack(Carried carried)
    {
        for (int i = 0; i < HeroCount && i < carried.Heroes.Count; i++)
        {
            WorldCreature hero = carried.Heroes[i];
            hero.Concentration = new Concentration(); // nobody holds a spell across the way
            hero.Awake = false;
            Creatures[i] = hero;
            Tokens.Tokens[i].Name = hero.Sheet.Name;
            Tokens.Tokens[i].Color = carried.Colors[i];
            Tokens.Tokens[i].Floor = hero.Sheet.Down ? DeadFloor : 0;
        }
        Flags.Clear();
        Flags.UnionWith(carried.Flags);
        FiredTriggers.UnionWith(carried.Fired); // trigger ids are per adventure
        MeetCompanions();
        PlaceCompanions(carried.Along);
        FallenConditions();
    }

    // The companions in the party, ready to be put on another map.
    private List<(WorldCreature Creature, Token Token)> CompanionsAlong()
    {
        var along = new List<(WorldCreature, Token)>();
        for (int i = HeroCount; i < Creatures.Count; i++)
        {
            if (!Companion(i))
            {
                continue;
            }
            WorldCreature c = Creatures[i];
            var moved = new WorldCreature(c.Sheet, 0)
            {
                CreatureId = c.CreatureId,
                CompanionId = c.CompanionId,
                CompanionTalk = DialogueFor(i),
                MayPrepare = c.MayPrepare,
                Awake = true,
            };
            Token t = Tokens.Tokens[i];
            along.Add((moved, new Token { Name = t.Name, Color = t.Color, Image = t.Image, Radius = t.Radius, Position = t.Position }));
        }
        return along;
    }

    // Companions from elsewhere come after the chapter's own creatures; one already on this map
    // (their own chapter) takes the sheet as it is now.
    private void PlaceCompanions(List<(WorldCreature Creature, Token Token)> along)
    {
        foreach ((WorldCreature creature, Token token) in along)
        {
            if (CompanionToken(creature.CompanionId) is int here)
            {
                WorldCreature c = Creatures[here];
                c.Sheet = creature.Sheet;
                c.MayPrepare = creature.MayPrepare;
                c.Team = 0;
                c.Awake = true;
                c.Fled = false;
                Tokens.Tokens[here].Name = c.Sheet.Name;
                Tokens.Tokens[here].Floor = c.Sheet.Down ? DeadFloor : 0;
                continue;
            }
            creature.Team = 0;
            creature.Awake = true;
            token.Floor = creature.Sheet.Down ? DeadFloor : 0;
            token.Owner = 0;
            Creatures.Add(creature);
            Tokens.Tokens.Add(token);
        }
        FollowParty();
    }

    // The first hero on the cell, the rest and then the companions on the nearest free squares.
    private void GatherAt(Cell entry)
    {
        List<int> party = Enumerable.Range(0, Creatures.Count).Where(InParty).ToList();
        var open = new Queue<Cell>();
        open.Enqueue(entry);
        var seen = new HashSet<Cell> { entry };
        for (int next = 0; next < party.Count && open.Count > 0;)
        {
            Cell c = open.Dequeue();
            int i = party[next];
            if (Walkable(c) && !Occupied(c, i))
            {
                Tokens.Tokens[i].Position = Grid.Center(c);
                Tokens.Tokens[i].Path.Clear();
                next++;
            }
            foreach (Cell n in new[] { new Cell(c.X + 1, c.Y), new Cell(c.X - 1, c.Y), new Cell(c.X, c.Y + 1), new Cell(c.X, c.Y - 1) })
            {
                if (Map.Inside(n) && Map.Walkable(n) && seen.Add(n))
                {
                    open.Enqueue(n);
                }
            }
        }
        for (int i = 0; i < HeroCount; i++)
        {
            _lastAt[i] = Tokens.Tokens[i].Position;
        }
    }

    // After the party is put down somewhere new: exits under them don't count until stepped off.
    private void Arrived()
    {
        _heroMarker.Clear();
        for (int i = 0; i < HeroCount; i++)
        {
            _heroMarker.Add(ExitMarkerAt(CellOf(i)));
        }
        // Sight waits for the next Update: noticing someone here is the game going on, not the arrival.
        SelectStandingHero();
    }

    // A chapter folder loaded for the party: camp seats as many as come, the extra seats beside the first.
    private Chapter LoadChapterFor(string folder, int heroes)
    {
        // camp names no system of its own: it plays the adventure's
        bool camp = folder == CampFolder;
        Chapter chapter = Chapter.Load(Files!, folder, camp && Chapter.Rules.Folder.Length > 0 ? Chapter.Rules.Folder : null);
        if (camp && heroes > 0 && chapter.Party.Count > 0)
        {
            while (chapter.Party.Count > heroes)
            {
                chapter.Party.RemoveAt(chapter.Party.Count - 1);
            }
            while (chapter.Party.Count < heroes)
            {
                chapter.Party.Add(chapter.Party[0]);
            }
        }
        return chapter;
    }

    private bool KnownFolder(string folder)
    {
        return folder == HomeFolder || (CampFolder.Length > 0 && folder == CampFolder) || (Adventure?.ChapterFolders.Contains(folder) ?? false);
    }

    private List<CharacterSheet> LivingHeroSheets()
    {
        return Creatures.Take(HeroCount).Where(c => !c.Sheet.Death.Dead).Select(c => c.Sheet).ToList();
    }

    private void EndAllConcentration()
    {
        for (int i = 0; i < Creatures.Count; i++)
        {
            EndConcentration(i, "");
        }
    }
}
