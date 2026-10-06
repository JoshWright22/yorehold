using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

/// <summary>Weight and the magic item limit, loot tables, consumables kept with their effects, and shops. The C++ framework's checks in ChoicesTests.</summary>
public class ItemTests
{
    private static Item Thing(string id, double weight = 0, bool magic = false) =>
        new(new ItemDefinition { Id = id, Name = char.ToUpperInvariant(id[0]) + id[1..], Weight = weight, Magic = magic });

    [Fact]
    public void WeightSlowsAndStops()
    {
        Ruleset rules = CharacterTests.Modern();
        var c = new CharacterSheet();
        c.Stats.SetBase("str", 10); // carries 150 lb
        c.Stats.SetBase("speed", 30);
        c.Inventory.Add(Thing("crate", 100));
        Assert.Equal((0, 6), (c.Encumbrance(rules), c.SpeedSquares(rules)));
        c.Inventory.Add(Thing("crate", 100)); // 200 lb: over capacity
        Assert.Equal((1, 3), (c.Encumbrance(rules), c.SpeedSquares(rules)));
        c.Inventory[^1].Quantity = 3; // 400 lb: over double
        Assert.Equal((2, 0), (c.Encumbrance(rules), c.SpeedSquares(rules)));

        Ruleset Changed(string fields) => RulesTesting.Rules("""{"id":"w","name":"W","abilities":[{"id":"str","name":"Str"},{"id":"dex","name":"Dex"},{"id":"con","name":"Con"},{"id":"int","name":"Int"},{"id":"wis","name":"Wis"},{"id":"cha","name":"Cha"}],""" + fields + "}");
        Assert.Equal(1, c.Encumbrance(Changed("\"immobileAt\":0")));
        Assert.Equal((0, 6), (c.Encumbrance(Changed("\"immobileAt\":0,\"encumberedAt\":0")), c.SpeedSquares(Changed("\"immobileAt\":0,\"encumberedAt\":0"))));
        Assert.Equal(0, c.Encumbrance(Changed("\"carryPerStrength\":0")));
        Assert.Throws<ContentException>(() => Changed("\"encumberedSpeed\":2"));
        Ruleset slow = Changed("\"encumberedAt\":0.5,\"immobileAt\":1.5,\"encumberedSpeed\":0.25");
        Assert.Equal((0.5, 1.5, 0.25), (slow.EncumberedAt, slow.ImmobileAt, slow.EncumberedSpeed));
    }

    [Fact]
    public void MagicItemLimitCountsEverythingCarried()
    {
        var c = new CharacterSheet();
        c.Inventory.Add(Thing("ring", magic: true));
        c.Inventory.Add(Thing("ring", magic: true));
        c.Inventory[1].Quantity = 2;
        Assert.True(c.MagicItems() == 3 && c.RoomForMagic(CharacterTests.Modern(), 5)); // no limit in this ruleset
        Ruleset limited = RulesTesting.Rules("""{"id":"m","name":"M","abilities":[{"id":"str","name":"Str"},{"id":"dex","name":"Dex"},{"id":"con","name":"Con"},{"id":"int","name":"Int"},{"id":"wis","name":"Wis"},{"id":"cha","name":"Cha"}],"magicItemLimit":3}""");
        Assert.False(c.RoomForMagic(limited));
        c.Inventory.RemoveAt(1);
        Assert.True(c.RoomForMagic(limited, 2) && !c.RoomForMagic(limited, 3));
        Item read = Item.Read(TestContent.Json(c.Inventory[0].ToJson().ToJsonString()));
        Assert.True(read.Magic);
    }

    [Fact]
    public void LootTablesRollTheSameForTheSameSeed()
    {
        LootTable table = LootTable.Read(TestContent.Json("""
            {"coins":"2d6","items":["sword",{"item":"sword","chance":0,"quantity":3},{"item":"sword","chance":1,"quantity":2}]}
            """));
        LootRoll found = Loot.Roll(table, new Rng(3));
        Assert.InRange(found.Coins, 2, 12);
        Assert.Equal(2, found.Items.Count);
        Assert.Equal(2, found.Items[1].Quantity);
        Assert.Equal(found.Coins, Loot.Roll(table, new Rng(3)).Coins);
        List<Item> items = Loot.Items(CharacterTests.Classes(), found.Items);
        Assert.True(items.Count == 2 && items[1].Quantity == 2 && !items[1].Equipped);
        Assert.Empty(Loot.Items(CharacterTests.Classes(), new[] { ("gem", 1) }));
        Assert.Equal("1 gp 2 sp 5 cp", Coins.Text(125));
        Assert.Equal("3 sp", Coins.Text(30));
        Assert.Equal("0 cp", Coins.Text(0));
    }

    [Fact]
    public void ConsumablesKeepTheirEffects()
    {
        ItemDefinition tonic = ItemDefinition.Read(TestContent.Json("""
            {"id":"tonic","hands":2,"quantity":2,"use":{"target":{"kind":"creature","side":"ally","downed":true},"effects":[{"do":"heal","dice":"2d4+2"}]}}
            """));
        Assert.True(tonic.Use != null && tonic.Use.Cost == 2 && tonic.Use.AllowsDowned);
        var sheet = new CharacterSheet();
        sheet.Inventory.Add(new Item(tonic));
        Item back = Item.Read(TestContent.Json(sheet.Inventory[0].ToJson().ToJsonString()));
        Assert.True(back.Use != null && JsonNode.DeepEquals(JsonNode.Parse(back.Definition.UseJson), JsonNode.Parse(tonic.UseJson)) && back.Quantity == 2);
        Assert.True(sheet.RemoveItem(0) && sheet.Inventory[0].Quantity == 1);
        Assert.True(sheet.RemoveItem(0) && sheet.Inventory.Count == 0 && !sheet.RemoveItem(0));
        TestContent.Refused(() => ItemDefinition.Read(TestContent.Json("""{"id":"bad","use":{"effects":[{"do":"typo"}]}}""")));
        TestContent.Refused(() => ItemDefinition.Read(TestContent.Json("""{"id":"bad","use":{"effects":[]}}""")));
        TestContent.Refused(() => ItemDefinition.Read(TestContent.Json("""{"id":"bad","slot":"mainHand","use":{"effects":[{"do":"heal","dice":1}]}}""")));
    }

    [Fact]
    public void ShopsBuyAndSellOneAtATime()
    {
        Compendium compendium = CharacterTests.Classes();
        Ruleset rules = CharacterTests.Modern();
        MerchantDefinition definition = MerchantDefinition.Read(TestContent.Json("""
            {"coins":100,"buyMultiplier":1.25,"sellMultiplier":0.5,"stock":[{"item":"sword","quantity":2,"value":11}]}
            """), compendium);
        Merchant shop = Merchant.From(definition, compendium);
        Assert.Equal((14, 5), (shop.BuyPrice(shop.Inventory[0]), shop.SellPrice(shop.Inventory[0])));
        var buyer = new CharacterSheet { Coins = 13 };
        Assert.True(!shop.Buy(buyer, 0, rules) && buyer.Inventory.Count == 0 && shop.Coins == 100);
        buyer.Coins = 30;
        Assert.True(shop.Buy(buyer, 0, rules) && buyer.Coins == 16 && shop.Coins == 114 && shop.Inventory[0].Quantity == 1);
        Assert.True(shop.Sell(buyer, 0) && buyer.Coins == 21 && shop.Coins == 109 && buyer.Inventory.Count == 0);

        buyer.Inventory.Add(new Item(compendium.Items["sword"]) { Value = 11 });
        buyer.Equip(0);
        Assert.False(shop.CanSell(buyer, 0, out string why));
        Assert.Equal("Put that item away before selling it.", why);
        buyer.Unequip(0);
        shop.Coins = 4;
        Assert.True(!shop.Sell(buyer, 0) && buyer.Inventory.Count == 1);
        var armour = new Item(new ItemDefinition { Id = "armour", Slot = "armor", Modifiers = new List<Modifier> { new("ac", ModifierOp.Add, 2) } });
        buyer.Inventory.Add(armour);
        buyer.Equip(1);
        int ac = buyer.ArmorClass(rules);
        shop.Coins = 100;
        // selling the first entry keeps the armour behind it worn, with its AC
        Assert.True(shop.Sell(buyer, 0) && buyer.Inventory[0].Equipped && buyer.ArmorClass(rules) == ac);
        buyer.Unequip(0);
        Assert.Equal(ac - 2, buyer.ArmorClass(rules));
        shop.Coins = int.MaxValue;
        int before = buyer.Coins;
        Assert.True(!shop.Buy(buyer, 0, rules) && buyer.Coins == before);

        TestContent.Refused(() => MerchantDefinition.Read(TestContent.Json("""{"sellMultiplier":2}"""), compendium));
        TestContent.Refused(() => MerchantDefinition.Read(TestContent.Json("""{"stock":[{"item":"missing"}]}"""), compendium));
        TestContent.Refused(() => MerchantDefinition.Read(TestContent.Json("""{"coins":-1}"""), compendium));
        TestContent.Refused(() => MerchantDefinition.Read(TestContent.Json("""{"coins":4294967396}"""), compendium));
        TestContent.Refused(() => MerchantDefinition.Read(TestContent.Json("""{"stock":[{"item":"sword","quantity":4294967297}]}"""), compendium));
    }
}
