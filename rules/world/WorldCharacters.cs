namespace Yorehold.Rules;

/// <summary>The heroes as characters: who takes each seat, levelling up from XP, and the copy that goes back to the library.</summary>
public sealed partial class World
{
    private List<PartyPick?> _partyPicks = new();

    /// <summary>
    /// Library characters for the seats, in the chapter's party order; null leaves a seat to its
    /// ready-made hero. Takes effect at the next NewAdventure.
    /// </summary>
    public void SetParty(IEnumerable<PartyPick?> picks)
    {
        _partyPicks = picks.ToList();
    }

    public IReadOnlyList<PartyPick?> PartyPicks => _partyPicks;

    /// <summary>
    /// A hero's copy as the library keeps it: its choices with the XP it has now, what it carries
    /// and its coins. Null for anyone who isn't a hero with choices.
    /// </summary>
    public LibraryEntry? LibraryCopy(int hero)
    {
        if (hero < 0 || hero >= HeroCount || Creatures[hero].Choices is not CharacterChoices choices)
        {
            return null;
        }
        CharacterSheet sheet = Creatures[hero].Sheet;
        var entry = new LibraryEntry { Choices = choices.Copy(), Coins = sheet.Coins };
        entry.Choices.Xp = Math.Max(entry.Choices.Xp, sheet.Xp);
        entry.Inventory.AddRange(sheet.Inventory.Select(i => i.Copy()));
        return entry;
    }

    // The ready-made hero is rolled even when someone else takes the seat, so the dice that follow
    // are the same either way.
    private WorldCreature SeatHero(PartyMember member, Rng dice)
    {
        CharacterChoices ready = CharacterChoices.Roll(Rules, member.Name, member.ClassId, dice);
        while (ready.Levels.Count < Chapter.Level)
        {
            ready.Levels.Add(ready.Levels[0].Copy());
        }
        if (Chapter.Level > 1 && Rules.XpForLevel.Count > 0)
        {
            ready.Xp = Rules.XpForLevel[Math.Min(Chapter.Level - 2, Rules.XpForLevel.Count - 1)];
        }
        int seat = Creatures.Count;
        PartyPick? pick = seat < _partyPicks.Count ? _partyPicks[seat] : null;
        if (pick != null)
        {
            CharacterSheet? brought = CharacterBuild.Build(Rules, Chapter.Compendium, pick.Choices, out string error);
            if (brought != null)
            {
                // what they carry comes with them, worn as it was
                for (int i = brought.Inventory.Count - 1; i >= 0; i--)
                {
                    brought.Unequip(i);
                }
                brought.Inventory.Clear();
                foreach (Item carried in pick.Inventory)
                {
                    Item item = carried.Copy();
                    bool worn = item.Equipped;
                    item.Equipped = false;
                    brought.Inventory.Add(item);
                    if (worn)
                    {
                        brought.Equip(brought.Inventory.Count - 1);
                    }
                }
                brought.Coins = pick.Coins;
                return new WorldCreature(brought, 0) { Choices = pick.Choices.Copy(), Library = pick.Library };
            }
            Say($"{pick.Choices.Name} can't play this adventure ({error}); {member.Name} takes the seat.");
        }
        // Chapter.Load checked the class, and rolled scores always build.
        CharacterSheet sheet = CharacterBuild.Build(Rules, Chapter.Compendium, ready, out string problem)
            ?? throw new ContentException("chapter.json", "party", $"{member.Name} can't be built: {problem}");
        return new WorldCreature(sheet, 0) { Choices = ready };
    }

    // XP took a hero past its level: the new levels go into its latest class, and the HP the
    // maximum gained comes with them.
    private void GainLevels(int hero)
    {
        WorldCreature c = Creatures[hero];
        if (c.Choices is not CharacterChoices old || c.Sheet.Level <= old.Level)
        {
            return;
        }
        CharacterChoices choices = old.Copy();
        choices.Xp = c.Sheet.Xp;
        while (choices.Levels.Count < c.Sheet.Level)
        {
            choices.Levels.Add(new LevelChoice(choices.Levels[^1].ClassId));
        }
        CharacterSheet? built = CharacterBuild.Build(Rules, Chapter.Compendium, choices, out string error);
        if (built == null)
        {
            Say($"{c.Sheet.Name} can't level up: {error}");
            return;
        }
        int before = c.Sheet.MaxHp;
        c.Sheet.AdoptBuild(built);
        if (!c.Sheet.Down)
        {
            c.Sheet.Hp += Math.Max(0, c.Sheet.MaxHp - before);
        }
        c.Choices = choices;
        Say($"{c.Sheet.Name} reaches level {c.Sheet.Level}.");
    }
}
