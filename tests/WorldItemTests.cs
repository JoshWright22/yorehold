namespace Yorehold.Rules.Tests;

/// <summary>Items in a World: containers, what the dead leave, taking, giving, gear, shops and using things up. The C++ client's WorldCharacterTests (loot, trade, gear) and WorldItemTests.</summary>
public class WorldItemTests
{
    private static WorldFixture YardWith(Action<Dictionary<string, string>> change, ulong seed = 5)
    {
        Dictionary<string, string> files = WorldCharacterTests.YardFiles(50);
        change(files);
        return WorldFixture.LoadJson("chapters/choice-yard", files, seed);
    }

    // The yard's chapter file with more fields put in before its closing brace.
    private static void AddToChapter(Dictionary<string, string> files, string fields)
    {
        string chapter = files["chapters/choice-yard/chapter.json"].TrimEnd();
        files["chapters/choice-yard/chapter.json"] = chapter[..^1] + "," + fields + "}";
    }

    private static int Find(CharacterSheet sheet, string id) => sheet.Inventory.FindIndex(i => i.Id == id);

    [Fact]
    public void ContainersAndWhatTheDeadLeave()
    {
        using WorldFixture world = YardWith(f => AddToChapter(f, """ "containers":[{"id":"box","name":"Box","at":[2,2],"items":["mace"],"coins":30}]"""));
        World w = world.World;
        Assert.True(w.Piles.Count == 1 && w.Piles[0].Name == "Box" && w.Piles[0].Coins == 30 && w.Piles[0].Items.Count == 1
            && w.PileNear(0) == 0 && w.PileNear(1) == null, "A container starts full; only a hero beside it can reach it");
        Assert.True(!w.Take(1, 0, all: true) && w.Refusal.Contains("too far"), "A hero further away is told so");
        Assert.True(w.Take(0, 0, coins: true) && w.Creatures[0].Sheet.Coins == 30 && w.Piles[0].Coins == 0);
        Assert.True(world.Said("Ana takes 3 sp (Box)."), "Coins are taken by themselves");
        CharacterSheet ana = w.Creatures[0].Sheet;
        Assert.True(w.Take(0, 0, item: 0) && ana.Inventory[^1].Id == "mace" && !ana.Inventory[^1].Equipped
            && w.Piles[0].Empty && w.PileNear(0) == null && !w.Take(0, 0, all: true), "Items go into the pack, and an empty container has nothing more");

        // and back in: an emptied chest still takes things, worn ones taken off first
        int worn = ana.Inventory.FindIndex(i => i.Equipped && i.Slot == "mainHand");
        string weaponId = ana.Inventory[worn].Id;
        Assert.True(w.Put(0, 0, worn) && w.Piles[0].Items[^1].Id == weaponId && !w.Piles[0].Items[^1].Equipped && ana.WeaponItem == null);
        Assert.True(world.Said($"Ana puts {w.Piles[0].Items[^1].Name} in Box."));
        Assert.True(!w.Put(1, 0, 0) && w.Refusal.Contains("too far"), "Only from beside it");
        Assert.True(w.Take(0, 0, item: 0) && ana.Inventory[^1].Id == weaponId && w.Equip(0, ana.Inventory.Count - 1, true));

        Assert.True(w.Give(0, 1, item: Find(ana, "mace")) && w.Creatures[1].Sheet.Inventory[^1].Id == "mace");
        Assert.True(world.Said("Ana gives Mace to Bo."), "An item can be handed to an ally");
        Assert.True(w.Give(0, 1, coins: 20) && ana.Coins == 10 && w.Creatures[1].Sheet.Coins == 20
            && !w.Give(0, 1, coins: 11) && !w.Give(0, 0, coins: 1), "Coins can be handed over, but not more than there are");
        int ac = ana.ArmorClass(w.Rules);
        int weapon = ana.Inventory.FindIndex(i => i.Equipped && i.Slot == "mainHand");
        string first = ana.Inventory[weapon].Id;
        Assert.True(w.Give(0, 1, item: weapon) && w.Creatures[1].Sheet.Inventory[^1].Id == first && !w.Creatures[1].Sheet.Inventory[^1].Equipped
            && ana.ArmorClass(w.Rules) == ac && ana.WeaponItem == null, "Giving a held weapon away leaves armour and shield as they were");
        int shield = Find(ana, "shield");
        Assert.True(shield >= 0 && w.Equip(0, shield, false) && ana.ArmorClass(w.Rules) < ac, "What is still worn can still be put away");

        w.Creatures[1].Sheet.Stats.SetBase("dex", 2000); // Bo acts first
        world.Fight();
        Assert.True(w.CurrentCreature == 1 && world.Use("finish-test"), "The goblin is beaten");
        Assert.True(w.Piles.Count == 2 && w.Piles[1].Name == "Gik" && w.Piles[1].At == w.CellOf(2) && w.Piles[1].Container == -1
            && w.Piles[1].Coins >= 2 && w.Piles[1].Coins <= 12 && w.Piles[1].Items.Count == 1 && w.Piles[1].Items[0].Id == "scimitar"
            && !w.Piles[1].Items[0].Equipped, "A dead goblin leaves its scimitar and some coins");
    }

    [Fact]
    public void TheMagicItemLimitAndWeight()
    {
        using WorldFixture world = YardWith(f => AddToChapter(f,
            """ "containers":[{"id":"vault","name":"Vault","at":[2,2],"items":["warding-ring","warding-ring","warding-ring","warding-ring","mace"]}]"""));
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        Assert.True(w.Take(0, 0, all: true) && ana.MagicItems() == 3 && w.Piles[0].Items.Count == 1 && w.Piles[0].Items[0].Id == "warding-ring");
        Assert.True(world.Said("Ana leaves Warding ring: 3 magic items is all anyone can carry."), "Taking everything leaves the magic item there is no room for");
        Assert.True(!w.Take(0, 0, item: 0) && w.Refusal.Contains("already carries 3 magic items"), "A fourth magic item can't be picked up");
        int RingOf(int hero) => w.Creatures[hero].Sheet.Inventory.FindIndex(i => i.Magic);
        Assert.True(w.Give(0, 1, item: RingOf(0)) && w.Creatures[1].Sheet.MagicItems() == 1
            && w.Take(0, 0, item: 0) && ana.MagicItems() == 3, "Giving one up makes room");
        Assert.True(!w.Give(1, 0, item: RingOf(1)) && w.Refusal.Contains("Ana already carries"), "Nobody can be handed more magic items than the limit");

        var anvil = new Item(new ItemDefinition { Id = "anvil", Name = "Anvil", Weight = ana.CarryCapacity(w.Rules) * 1.5 });
        ana.Inventory.Add(anvil);
        w.Walk(0); // just the pace; a whole step would let the goblin see the party
        Assert.True(ana.Encumbrance(w.Rules) == 1 && Math.Abs(w.Tokens.Tokens[0].Pace - (float)w.Rules.EncumberedSpeed) < 1e-6
            && w.Tokens.Tokens[1].Pace == 1.0f, "Carrying more than the limit slows a hero's walking");
        anvil.Quantity = 2;
        w.Walk(0);
        Assert.True(ana.Encumbrance(w.Rules) == 2 && w.Tokens.Tokens[0].Pace == 0 && ana.SpeedSquares(w.Rules) == 0,
            "Twice the limit and they can't move, in a fight or out of one");
    }

    [Fact]
    public void GearChangesFreeBetweenFightsAndCostsAnActionInOne()
    {
        using WorldFixture world = WorldCharacterTests.Yard();
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        int shield = Find(ana, "shield");
        int ac = ana.ArmorClass(w.Rules);
        Assert.True(shield >= 0 && w.Equip(0, shield, false) && ana.ArmorClass(w.Rules) < ac);
        Assert.True(world.Said("Ana puts away Shield."), "A shield can be put away between fights");
        Assert.True(!w.Equip(0, shield, false) && !w.Equip(0, 99, true), "Nothing happens for an item already away or not there");
        Assert.True(w.Equip(0, shield, true) && ana.ArmorClass(w.Rules) == ac, "And taken up again");
        ana.Inventory.Add(new Item(w.Chapter.Compendium.Item("greataxe")!));
        Assert.True(w.Equip(0, Find(ana, "greataxe"), true) && ana.WeaponItem?.Id == "greataxe" && !ana.Inventory[shield].Equipped && ana.HandsInUse() == 2);
        Assert.True(world.Said("Ana takes up Greataxe, putting away Longsword, Shield."), "A two-handed weapon takes both hands");

        ana.Stats.SetBase("dex", 2000); // acts first
        world.Fight();
        Assert.Equal(0, w.CurrentCreature);
        int actions = w.ActionsLeft;
        Assert.False(w.Equip(1, 0, false), "Gear can't be changed on someone else's turn");
        Assert.True(w.Equip(0, Find(ana, "longsword"), true) && w.ActionsLeft == actions - 1 && ana.WeaponItem?.Id == "longsword",
            "In a fight, changing gear costs an action");
    }

    [Fact]
    public void ShopsTradeWithTheHerosOwnCoins()
    {
        using WorldFixture world = YardWith(f =>
        {
            AddToChapter(f, """
                "npcs":[{"id":"trader","name":"Trader","at":[2,3],"dialogue":"dialogue/trader.json",
                  "merchant":{"coins":1000,"buyMultiplier":1,"sellMultiplier":0.5,"stock":[{"item":"mace","quantity":2,"value":100},{"item":"warding-ring","value":100}]}}]
                """);
            f["dialogue/trader.json"] = """{"id":"trader","start":"greeting","nodes":[{"id":"greeting","text":"Trade test."}]}""";
        });
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        Assert.True(w.MerchantNear(0) == 0 && w.Merchants[0]!.Inventory[0].Quantity == 2, "An adjacent merchant can trade");
        w.Tokens.Tokens[0].Path.Add(w.Grid.Center(new Cell(3, 2)));
        Assert.False(w.CanTrade(0, 0), "A hero walking toward the shop must arrive before trading");
        w.Tokens.Tokens[0].Path.Clear();
        Assert.True(!w.Buy(0, 0, 0) && w.Refusal == "Not enough coins.", "Buying needs the hero's own coins");
        ana.Coins = 300;
        Merchant shop = w.Merchants[0]!;
        Assert.True(w.Buy(0, 0, 0) && ana.Coins == 200 && shop.Coins == 1100 && shop.Inventory[0].Quantity == 1
            && ana.Inventory[^1].Id == "mace" && ana.Inventory[^1].Quantity == 1 && !ana.Inventory[^1].Equipped, "Buying moves one unit into the pack");
        Assert.True(w.Buy(0, 0, 0) && shop.Inventory.Count == 1 && shop.Inventory[0].Id == "warding-ring", "Buying the last unit removes that stock entry");
        Assert.True(w.Sell(0, 0, ana.Inventory.Count - 1) && ana.Coins == 150 && shop.Coins == 1150 && shop.Inventory[^1].Id == "mace",
            "Selling moves one item to the merchant for half its value");
        Assert.True(!w.Sell(0, 0, 0) && w.Refusal.Contains("Put that item away"), "Worn gear must be put away before it is sold");
        for (int i = 0; i < 3; i++)
        {
            ana.Inventory.Add(new Item(w.Chapter.Compendium.Item("warding-ring")!));
        }
        Assert.True(!w.Buy(0, 0, 0) && w.Refusal.Contains("magic item"), "Buying keeps to the magic item limit");
        w.Place(0, new Cell(6, 6));
        Assert.True(!w.Buy(0, 0, 0) && w.MerchantNear(0) == null, "Walking away ends trading");
    }

    [Fact]
    public void APotionGetsADownedAllyUp()
    {
        using WorldFixture world = WorldCharacterTests.Yard();
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        CharacterSheet bo = w.Creatures[1].Sheet;
        int potion = Find(ana, "healing-potion");
        Assert.True(potion >= 0 && ana.Inventory[potion].Use != null && !ana.Resources.ContainsKey("potions"), "New heroes carry a real healing potion");
        ana.Inventory[potion].Quantity = 2;
        bo.TakeDamage(1000, w.Rules);
        bo.Death.Stable = true;
        bo.Death.Successes = w.Rules.Death.Successes;
        bo.SyncDeath(w.Rules);
        w.Tokens.Tokens[0].Path.Add(w.Grid.Center(new Cell(2, 3)));
        Assert.False(w.Consume(0, potion, 1), "Walking heroes stop before using an item");
        w.Tokens.Tokens[0].Path.Clear();
        Assert.True(w.Consume(0, potion, 1));
        Assert.True(bo.Hp >= 4 && bo.Hp <= 10 && !bo.Death.Stable && bo.Death.Successes == 0 && !bo.HasCondition("downed"), "A carried potion gets a stable ally up");
        Assert.Equal(1, ana.Inventory[potion].Quantity);
        w.Place(1, new Cell(6, 6));
        Assert.True(!w.Consume(0, potion, 1) && ana.Inventory[potion].Quantity == 1, "An ally out of reach costs no potion");
        w.Place(1, new Cell(3, 4));
        bo.Hp = 0;
        bo.Death.Dead = true;
        Assert.True(!w.Consume(0, potion, 1) && ana.Inventory[potion].Quantity == 1, "Potions don't bring back the dead");
        int count = ana.Inventory.Count;
        Assert.True(w.Consume(0, potion, 0) && ana.Inventory.Count == count - 1, "The last unit used removes its entry");
    }

    [Fact]
    public void ScrollsWaitForAFightWhenHostile()
    {
        using WorldFixture world = WorldCharacterTests.Yard(0);
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        ana.Inventory.Add(new Item(w.Chapter.Compendium.Item("ward-scroll")!));
        ana.Inventory.Add(new Item(w.Chapter.Compendium.Item("ember-scroll")!));
        ana.Stats.SetBase("dex", 1000);
        w.Creatures[2].Sheet.Stats.SetBase("maxHp", 100);
        w.Creatures[2].Sheet.Hp = 100;
        int ward = ana.Inventory.Count - 2;
        Assert.False(w.Consume(0, ward, 1), "A self-only scroll refuses another target");
        Assert.True(!w.Consume(0, ward + 1, 2) && w.Refusal.Contains("during a fight"), "Hostile scrolls can't get round initiative");
        Assert.True(w.Consume(0, ward) && ana.TempHp >= 3 && ana.TempHp <= 8, "A ward scroll gives temporary HP through the effect steps");
        world.Fight();
        Assert.Equal(0, w.CurrentCreature);
        int resources = ana.Resources.Count;
        Assert.True(w.Consume(0, ward, 2), "An ember scroll rolls its save and damage in a fight");
        Assert.True(w.Creatures[2].Sheet.Hp < 100 && w.Creatures[2].Sheet.Hp >= 88 && w.ActionsLeft == 0, "Scroll damage takes its actions");
        Assert.Equal(resources, ana.Resources.Count);
        int potion = Find(ana, "healing-potion");
        Assert.False(w.Consume(0, potion, 0), "No actions left refuses a potion without using it");
        Assert.False(w.Consume(1, Find(w.Creatures[1].Sheet, "healing-potion"), 1), "Another hero waits for their own turn");
    }
}
