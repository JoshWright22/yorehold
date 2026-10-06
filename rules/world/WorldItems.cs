namespace Yorehold.Rules;

/// <summary>Something lying on the map to be taken: a container's contents, or what a dead enemy left.</summary>
public sealed class Pile
{
    public string Name { get; init; } = "";
    public Cell At { get; init; }
    /// <summary>Copper.</summary>
    public int Coins { get; set; }
    public List<Item> Items { get; } = new();
    /// <summary>Index into the chapter's containers, or -1.</summary>
    public int Container { get; init; } = -1;
    /// <summary>The chest on the map it is inside, or 0; its lock keeps hands out until it opens.</summary>
    public int Object { get; init; }

    public bool Empty => Coins <= 0 && Items.Count == 0;
}

/// <summary>Gear and what is carried: piles and loot, giving, putting things on and away, using things up, and shops.</summary>
public sealed partial class World
{
    public List<Pile> Piles { get; } = new();
    /// <summary>One per chapter NPC, null for those who don't trade.</summary>
    public List<Merchant?> Merchants { get; } = new();

    // Calm: between fights, with nothing else going on.
    private bool Calm => !Fighting && !InCutscene && !PartyWiped;

    /// <summary>"Ana already carries 3 magic items of the 3 allowed. Give one up first."</summary>
    public string MagicLimitText(int hero)
    {
        CharacterSheet sheet = Creatures[hero].Sheet;
        return $"{sheet.Name} already carries {sheet.MagicItems()} magic items of the {Rules.MagicItemLimit} allowed. Give one up first.";
    }

    // ---------------------------------------------------------------- piles

    // The chapter's containers, then the chests placed on the map as objects, full as the files have them.
    private void FillContainers()
    {
        Piles.Clear();
        // its own dice, so a chapter gaining a container doesn't change how its fights roll
        var dice = new Rng(_seed ^ 0xc0ffeeUL);
        for (int i = 0; i < Chapter.Containers.Count; i++)
        {
            ChapterContainer container = Chapter.Containers[i];
            var pile = new Pile { Name = container.Name, At = container.At, Coins = container.Coins, Container = i };
            foreach (string id in container.Items)
            {
                pile.Items.Add(new Item(Chapter.Compendium.Item(id)!)); // Chapter.Load checked the ids
            }
            LootRoll found = Loot.Roll(container.Loot, dice);
            pile.Coins += found.Coins;
            pile.Items.AddRange(Loot.Items(Chapter.Compendium, found.Items));
            Piles.Add(pile);
        }
        // what is in a chest object ("coins" in copper) becomes a pile kept in that object
        foreach (WorldObject o in Map.Objects)
        {
            if (!o.Has("container") || o.Destroyed)
            {
                continue;
            }
            var pile = new Pile { Name = o.Name.Length == 0 ? "Chest" : o.Name, At = MapState.CellOf(o), Object = o.Id };
            foreach (KeyValuePair<string, int> content in o.Contents)
            {
                if (content.Key == "coins")
                {
                    pile.Coins += content.Value;
                    continue;
                }
                if (Chapter.Compendium.Item(content.Key) is not ItemDefinition item || content.Value <= 0)
                {
                    continue;
                }
                if (item.Slot.Length == 0)
                {
                    pile.Items.Add(new Item(item, content.Value));
                }
                else
                {
                    for (int n = 0; n < content.Value; n++)
                    {
                        pile.Items.Add(new Item(item, 1));
                    }
                }
            }
            Piles.Add(pile);
        }
    }

    // The dead leave what they carried and what their loot table gives, where they fell. A group's
    // own loot lies with the last of it to fall, once nobody in it is left to fight.
    private void DropLoot()
    {
        Rng dice = NextRandom(0x100700UL);
        var fallen = new List<(int Group, Pile Pile)>();
        for (int i = HeroCount; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            // only the dead leave their things: not those who got away, gave up, or never woke
            if (c.Dropped || !c.Sheet.Down || c.Fled || c.Surrendered || c.Team != 1)
            {
                continue;
            }
            c.Dropped = true;
            var pile = new Pile { Name = c.Sheet.Name, At = CellOf(i) };
            foreach (Item carried in c.Sheet.Inventory)
            {
                Item item = carried.Copy();
                item.Equipped = false;
                pile.Items.Add(item);
            }
            if (Chapter.Compendium.Creature(c.CreatureId) is CreatureDefinition definition)
            {
                LootRoll found = Loot.Roll(definition.Loot, dice);
                pile.Coins = found.Coins;
                pile.Items.AddRange(Loot.Items(Chapter.Compendium, found.Items));
            }
            fallen.Add((c.Group, pile));
        }
        for (int group = 0; group < Chapter.Encounters.Count; group++)
        {
            LootTable loot = Chapter.Encounters[group].Loot;
            int last = fallen.FindLastIndex(f => f.Group == group);
            if (loot.IsEmpty || last < 0 || Creatures.Skip(HeroCount).Any(c => c.Group == group && !c.Sheet.Down && !c.Surrendered))
            {
                continue;
            }
            LootRoll found = Loot.Roll(loot, dice);
            fallen[last].Pile.Coins += found.Coins;
            fallen[last].Pile.Items.AddRange(Loot.Items(Chapter.Compendium, found.Items));
        }
        Piles.AddRange(fallen.Select(f => f.Pile).Where(p => !p.Empty));
    }

    public bool PileLocked(int pile)
    {
        return pile >= 0 && pile < Piles.Count && Piles[pile].Object != 0 && Map.Get(Piles[pile].Object) is WorldObject o && o.IsLocked;
    }

    /// <summary>A pile with something in it that the hero stands on or beside, and that isn't locked.</summary>
    public int? PileNear(int hero)
    {
        if (hero < 0 || hero >= HeroCount)
        {
            return null;
        }
        Cell at = CellOf(hero);
        for (int i = 0; i < Piles.Count; i++)
        {
            if (!Piles[i].Empty && !PileLocked(i) && Math.Abs(Piles[i].At.X - at.X) <= 1 && Math.Abs(Piles[i].At.Y - at.Y) <= 1)
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>The pile with something in it on a cell, if any.</summary>
    public int? PileAt(Cell cell)
    {
        int found = Piles.FindIndex(p => p.At == cell && !p.Empty);
        return found < 0 ? null : found;
    }

    /// <summary>
    /// A hero on or beside a pile takes from it between fights: one item (item), the coins, or all
    /// of it. Taking everything leaves the magic items there is no room for.
    /// </summary>
    public bool Take(int hero, int pile, int? item = null, bool coins = false, bool all = false)
    {
        Refusal = "";
        if (!Calm || hero < 0 || hero >= HeroCount || Creatures[hero].Sheet.Down || pile < 0 || pile >= Piles.Count || Piles[pile].Empty)
        {
            Refusal = "Not now.";
            return false;
        }
        Pile p = Piles[pile];
        CharacterSheet sheet = Creatures[hero].Sheet;
        if (PileLocked(pile))
        {
            Refusal = $"{p.Name} is locked.";
            return false;
        }
        Cell at = CellOf(hero);
        if (Math.Abs(p.At.X - at.X) > 1 || Math.Abs(p.At.Y - at.Y) > 1)
        {
            Refusal = $"{sheet.Name} is too far from {p.Name}.";
            return false;
        }
        if (!all && coins && p.Coins <= 0)
        {
            return false;
        }
        if (!all && !coins)
        {
            if (item is not int index || index < 0 || index >= p.Items.Count)
            {
                return false;
            }
            if (p.Items[index].Magic && !sheet.RoomForMagic(Rules, p.Items[index].Quantity))
            {
                Refusal = MagicLimitText(hero);
                return false;
            }
        }

        var taken = new List<string>();
        if ((all || coins) && p.Coins > 0)
        {
            taken.Add(Coins.Text(p.Coins));
            sheet.Coins += p.Coins;
            p.Coins = 0;
        }
        var left = new List<string>();
        int first = all ? 0 : item ?? p.Items.Count;
        for (int i = first; i < p.Items.Count && (all || i == first);)
        {
            Item thing = p.Items[i];
            if (thing.Magic && !sheet.RoomForMagic(Rules, thing.Quantity))
            {
                left.Add(thing.Name);
                i++;
                continue;
            }
            taken.Add(thing.Name + (thing.Quantity > 1 ? " x" + thing.Quantity : ""));
            Loot.AddTo(sheet, thing);
            p.Items.RemoveAt(i);
            if (!all)
            {
                break;
            }
        }
        if (taken.Count > 0)
        {
            Say($"{sheet.Name} takes {string.Join(", ", taken)} ({p.Name}).");
        }
        if (left.Count > 0)
        {
            Say($"{sheet.Name} leaves {string.Join(", ", left)}: {Rules.MagicItemLimit} magic items is all anyone can carry.");
        }
        return true;
    }

    /// <summary>Hands an item (a whole entry) or coins to another hero, between fights and at any distance.</summary>
    public bool Give(int from, int to, int? item = null, int coins = 0)
    {
        Refusal = "";
        if (!Calm || from < 0 || from >= HeroCount || to < 0 || to >= HeroCount || from == to
            || Creatures[from].Sheet.Down || Creatures[to].Sheet.Down)
        {
            Refusal = "Not now.";
            return false;
        }
        CharacterSheet giver = Creatures[from].Sheet;
        CharacterSheet taker = Creatures[to].Sheet;
        if (item == null)
        {
            if (coins <= 0 || coins > giver.Coins)
            {
                return false;
            }
            giver.Coins -= coins;
            taker.Coins += coins;
            Say($"{giver.Name} gives {Coins.Text(coins)} to {taker.Name}.");
            return true;
        }
        if (item < 0 || item >= giver.Inventory.Count)
        {
            return false;
        }
        Item given = giver.Inventory[item.Value];
        if (given.Magic && !taker.RoomForMagic(Rules, given.Quantity))
        {
            Refusal = MagicLimitText(to);
            return false;
        }
        Item moved = giver.TakeOut(item.Value);
        Loot.AddTo(taker, moved);
        Say($"{giver.Name} gives {moved.Name} to {taker.Name}.");
        return true;
    }

    /// <summary>What changing gear costs in a fight: what Interact costs, else one action.</summary>
    public int EquipCost(int creature)
    {
        ActionDefinition? interact = FindAction(InteractAction);
        return interact != null ? ActionCost(creature, interact) : 1;
    }

    public bool CanEquip(int hero, int item, bool on, out string why)
    {
        why = "";
        if (hero < 0 || hero >= HeroCount || Creatures[hero].Sheet.Down || InCutscene || PartyWiped)
        {
            return false;
        }
        CharacterSheet sheet = Creatures[hero].Sheet;
        if (item < 0 || item >= sheet.Inventory.Count || sheet.Inventory[item].Slot.Length == 0 || sheet.Inventory[item].Equipped == on)
        {
            return false;
        }
        if (Fighting)
        {
            if (CurrentCreature != hero || _pendingMovement != null)
            {
                why = $"Gear can only be changed on {sheet.Name}'s own turn.";
                return false;
            }
            if (!Encounter!.CanAct(EquipCost(hero)))
            {
                why = "Not enough actions left to change gear.";
                return false;
            }
        }
        return true;
    }

    /// <summary>Puts an item on or away: free between fights, an Interact on the hero's own turn in one.</summary>
    public bool Equip(int hero, int item, bool on)
    {
        Refusal = "";
        if (!CanEquip(hero, item, on, out string why))
        {
            Refusal = why.Length > 0 ? why : "Not now.";
            return false;
        }
        CharacterSheet sheet = Creatures[hero].Sheet;
        if (Fighting)
        {
            Encounter!.SpendActions(EquipCost(hero));
        }
        Item thing = sheet.Inventory[item];
        if (on)
        {
            // whatever it pushed out of a slot or a hand is named too
            List<bool> before = sheet.Inventory.Select(i => i.Equipped).ToList();
            sheet.Equip(item);
            List<string> away = sheet.Inventory.Where((i, n) => before[n] && !i.Equipped).Select(i => i.Name).ToList();
            Say($"{sheet.Name}{(thing.Held ? " takes up " : " puts on ")}{thing.Name}{(away.Count == 0 ? "." : ", putting away " + string.Join(", ", away) + ".")}");
        }
        else
        {
            sheet.Unequip(item);
            Say($"{sheet.Name}{(thing.Held ? " puts away " : " takes off ")}{thing.Name}.");
        }
        sheet.Hp = Math.Min(sheet.Hp, sheet.MaxHp);
        SyncLog();
        if (Fighting && CurrentCreature == hero)
        {
            ComputeReach(hero);
        }
        return true;
    }

    // ---------------------------------------------------------------- using things up

    /// <summary>
    /// A hero can use up an item on a target: free between fights for helpful ones, its actions on
    /// the hero's own turn in a fight. Hostile items wait for a fight. why says what is wrong.
    /// </summary>
    public bool CanConsume(int hero, int item, int target, out string why)
    {
        why = "";
        if (hero < 0 || hero >= HeroCount || target < 0 || target >= Creatures.Count || InCutscene || PartyWiped || _pendingMovement != null)
        {
            why = "An item cannot be used now.";
            return false;
        }
        CharacterSheet sheet = Creatures[hero].Sheet;
        if (sheet.Down || sheet.HasFlag(Rules, "cantAct") || Tokens.Tokens[hero].Path.Count > 0)
        {
            why = "Wait until that hero can act.";
            return false;
        }
        if (item < 0 || item >= sheet.Inventory.Count || sheet.Inventory[item].Use == null || sheet.Inventory[item].Quantity < 1 || sheet.Inventory[item].Equipped)
        {
            why = "That item cannot be used up.";
            return false;
        }
        ActionDefinition action = sheet.Inventory[item].Use!;
        if (!action.Meets(sheet, Rules, out why))
        {
            return false;
        }
        if (Fighting && (CurrentCreature != hero || !Encounter!.CanAct(action.Cost)))
        {
            why = "Using that item needs actions on this hero's turn.";
            return false;
        }
        if (action.Target == ActionTarget.Self)
        {
            why = target == hero ? "" : "Use that item on yourself.";
            return target == hero;
        }
        WorldCreature subject = Creatures[target];
        if (!InRange(hero, action, target) || !Sight.LineOfSight(Grid.Center(CellOf(hero)), Grid.Center(CellOf(target)), Map.Walls))
        {
            why = "Stand within clear reach of the target.";
            return false;
        }
        if (subject.Fled || subject.Sheet.Death.Dead || subject.Sheet.HasFlag(Rules, "dead") || (subject.Sheet.Down && !action.AllowsDowned))
        {
            why = "That creature cannot receive this item.";
            return false;
        }
        if (Fighting)
        {
            if (!ValidTarget(hero, action, target))
            {
                why = "That target is outside the item's reach or on the wrong side.";
                return false;
            }
            return true;
        }
        // hostile items need a fight, so damage can't get round initiative or what a win gives
        if (target >= HeroCount || action.Side == ActionSide.Enemy)
        {
            why = "Use hostile items during a fight.";
            return false;
        }
        return true;
    }

    /// <summary>Uses up one of an item on a target (the hero by default).</summary>
    public bool Consume(int hero, int item, int? target = null)
    {
        Refusal = "";
        int aimed = target ?? hero;
        if (!CanConsume(hero, item, aimed, out string why))
        {
            Refusal = why;
            return false;
        }
        CharacterSheet sheet = Creatures[hero].Sheet;
        Item used = sheet.Inventory[item];
        ActionDefinition action = used.Use!;
        bool inFight = Fighting;
        if (inFight)
        {
            Encounter!.SpendActions(action.Cost);
            SyncLog();
        }
        sheet.RemoveItem(item);
        Say($"{sheet.Name} uses {used.Name}.");
        RunActionEffect(hero, action, aimed);
        if (inFight && Encounter!.Finished)
        {
            EndFight();
        }
        else if (inFight && CurrentCreature == hero)
        {
            ComputeReach(hero);
        }
        return true;
    }

    /// <summary>Everyone the hero could use the item on now.</summary>
    public List<int> ConsumeTargets(int hero, int item)
    {
        return Enumerable.Range(0, Creatures.Count).Where(t => CanConsume(hero, item, t, out _)).ToList();
    }

    // ---------------------------------------------------------------- shops

    /// <summary>The NPC trades, stands peaceful beside the hero, and nothing else is going on.</summary>
    public bool CanTrade(int hero, int npc)
    {
        int creature = NpcStart + npc;
        return npc >= 0 && npc < Merchants.Count && Merchants[npc] != null && hero >= 0 && hero < HeroCount && !Creatures[hero].Sheet.Down
            && creature < Creatures.Count && Creatures[creature].Team == 2 && !Creatures[creature].Sheet.Down && !Creatures[creature].Fled
            && Calm && Tokens.Tokens[hero].Path.Count == 0 && Adjacent(hero, creature);
    }

    public int? MerchantNear(int hero)
    {
        for (int npc = 0; npc < Merchants.Count; npc++)
        {
            if (CanTrade(hero, npc))
            {
                return npc;
            }
        }
        return null;
    }

    /// <summary>Buys one of a stock entry with the hero's own coins.</summary>
    public bool Buy(int hero, int npc, int item) => Trade(hero, npc, item, buying: true);

    /// <summary>Sells one of an inventory entry that isn't worn.</summary>
    public bool Sell(int hero, int npc, int item) => Trade(hero, npc, item, buying: false);

    private bool Trade(int hero, int npc, int item, bool buying)
    {
        Refusal = "";
        if (!CanTrade(hero, npc))
        {
            Refusal = "Stand beside a peaceful merchant to trade.";
            return false;
        }
        Merchant shop = Merchants[npc]!;
        CharacterSheet sheet = Creatures[hero].Sheet;
        string why;
        if (!(buying ? shop.CanBuy(sheet, item, Rules, out why) : shop.CanSell(sheet, item, out why)))
        {
            Refusal = why;
            return false;
        }
        Item traded = buying ? shop.Inventory[item] : sheet.Inventory[item];
        string name = traded.Name;
        int cost = buying ? shop.BuyPrice(traded) : shop.SellPrice(traded);
        if (!(buying ? shop.Buy(sheet, item, Rules) : shop.Sell(sheet, item)))
        {
            return false;
        }
        Say($"{sheet.Name}{(buying ? " buys " : " sells ")}{name} for {Coins.Text(cost)}{(buying ? " from " : " to ")}{Chapter.Npcs[npc].Name}.");
        return true;
    }

    private void FillMerchants()
    {
        Merchants.Clear();
        foreach (ChapterNpc npc in Chapter.Npcs)
        {
            Merchants.Add(npc.Merchant == null ? null : Merchant.From(npc.Merchant, Chapter.Compendium));
        }
    }
}
