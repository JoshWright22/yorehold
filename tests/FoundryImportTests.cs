using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

public class FoundryImportTests
{
    // Shaped like Foundry's exports; the numbers and words are made up for the test.
    private const string Dnd5e = """
        [
          {"name": "Bone Sabre", "type": "weapon", "system": {
            "damage": {"base": {"number": 1, "denomination": 8, "bonus": "", "types": ["slashing"]}},
            "properties": ["fin", "lgt"], "range": {"value": null}, "weight": {"value": 2}, "price": {"value": 12, "denomination": "gp"},
            "activities": {"a": {"type": "attack"}}}},
          {"name": "Ember Burst", "type": "spell", "system": {
            "level": 2, "properties": ["vocal"], "range": {"value": 60, "units": "ft"},
            "description": {"value": "<p>A burst of embers. See @UUID[Compendium.x.y]{Fire}.</p>"},
            "activities": {"s": {"type": "save", "activation": {"type": "action"},
              "target": {"template": {"type": "sphere", "size": "10"}},
              "save": {"ability": ["dex"]}, "damage": {"onSave": "half", "parts": [{"number": 3, "denomination": 6, "types": ["fire"]}]}}}}},
          {"name": "Marsh Raider", "type": "npc", "system": {
            "abilities": {"str": {"value": 12}, "dex": {"value": 14}, "con": {"value": 11}},
            "attributes": {"hp": {"max": 13}, "ac": {"flat": 13}, "movement": {"walk": "30"}, "senses": {"ranges": {"darkvision": 60}}},
            "details": {"cr": "1/2", "biography": {"value": "<p>Lives in the reeds.</p>"}}},
           "items": [
             {"name": "Bone Sabre", "type": "weapon", "system": {"damage": {"base": {"number": 1, "denomination": 8, "types": ["slashing"]}}, "properties": ["fin"]}},
             {"name": "Reed Armor", "type": "equipment", "system": {"type": {"value": "light"}, "armor": {"value": 12}}},
             {"name": "Ambusher", "type": "feat", "system": {}}]},
          {"name": "Gold Chalice", "type": "loot", "system": {}}
        ]
        """;

    private const string Pf2e = """
        {"name": "Tunnel Biter", "type": "npc", "system": {
          "abilities": {"str": {"mod": 2}, "dex": {"mod": 3}, "con": {"mod": 1}},
          "attributes": {"hp": {"max": 18}, "ac": {"value": 17}, "speed": {"value": 25}},
          "perception": {"mod": 6, "senses": [{"type": "darkvision"}]}, "traits": {"value": ["animal"]},
          "details": {"level": {"value": 1}, "publicNotes": "<p>Digs and bites.</p>"}},
         "items": [
           {"name": "Jaws", "type": "melee", "system": {"bonus": {"value": 9}, "traits": {"value": ["agile", "deadly-d8"]},
             "damageRolls": {"x": {"damage": "1d8+2", "damageType": "piercing"}}}}]}
        """;

    [Fact]
    public void A5eExportBecomesTheGamesFiles()
    {
        FoundryImport import = FoundryImport.Read(Dnd5e);
        Assert.Equal(new[] { "creatures/marsh-raider.json", "items/bone-sabre.json", "items/reed-armor.json", "spells/ember-burst.json" },
            import.Files.Keys.OrderBy(k => k));
        // each loads with the game's own reader
        ItemDefinition sabre = ItemDefinition.Read(Node(import, "items/bone-sabre.json"));
        Assert.Equal(("1d8", "slashing", "dex", 1200), (sabre.Damage, sabre.DamageType, sabre.AttackAbility, sabre.Value));
        Assert.Contains("finesse", sabre.Traits);
        SpellDefinition burst = SpellDefinition.Read(Node(import, "spells/ember-burst.json"));
        Assert.Equal(2, burst.Level);
        Assert.Equal("A burst of embers. See Fire.", burst.Action.Description);
        CreatureDefinition raider = CreatureDefinition.Read(Node(import, "creatures/marsh-raider.json"));
        Assert.Equal((13, 13, 1, 60), (raider.Hp, raider.ArmorClass, raider.Level, raider.Darkvision));
        Assert.Equal(new[] { "bone-sabre", "reed-armor" }, raider.Items);
        // what had no place is named
        Assert.Contains(import.Report, line => line.Contains("Gold Chalice") && line.Contains("loot"));
        Assert.Contains(import.Report, line => line.Contains("Ambusher"));
    }

    [Fact]
    public void APf2eCreatureStrikesAsPrinted()
    {
        FoundryImport import = FoundryImport.Read(Pf2e);
        CreatureDefinition biter = CreatureDefinition.Read(Node(import, "creatures/tunnel-biter.json"));
        Assert.Equal((18, 17, 1, 16), (biter.Hp, biter.ArmorClass, biter.Level, biter.Abilities["dex"]));
        // +9 printed = dex 3 + trained 2 + level 1 + 3 more
        Assert.Equal(3, biter.Stats["attack"]);
        ItemDefinition jaws = ItemDefinition.Read(Node(import, "items/tunnel-biter-jaws.json"));
        Assert.Equal(("1d8+2", "piercing"), (jaws.Damage, jaws.DamageType));
        Assert.Contains("deadly", jaws.Traits);
    }

    private static ContentNode Node(FoundryImport import, string path) => ContentNode.Parse(path, import.Files[path].ToJsonString());

    [Fact]
    public void ActiveEffectsBecomeModifiers()
    {
        const string json = """
            [
              {"name": "Ring of Warding", "type": "equipment", "system": {"armor": {"value": null}},
               "effects": [{"name": "Warding", "transfer": true, "changes": [
                 {"key": "system.attributes.ac.bonus", "mode": 2, "value": "+1"},
                 {"key": "system.abilities.dex.bonuses.save", "mode": 2, "value": "1"},
                 {"key": "system.attributes.movement.walk", "mode": 4, "value": "40"},
                 {"key": "system.traits.dr.value", "mode": 0, "value": "fire"}]}]},
              {"name": "Quiet Feet", "type": "feat", "system": {},
               "effects": [{"name": "Hush", "changes": [{"key": "system.skills.ste.bonuses.check", "mode": 2, "value": "2"}]},
                           {"name": "Off", "disabled": true, "changes": [{"key": "system.attributes.ac.bonus", "mode": 2, "value": "5"}]}]}
            ]
            """;
        FoundryImport import = FoundryImport.Read(json);
        FeatDefinition feet = FeatDefinition.Read(Node(import, "feats/quiet-feet.json"));
        Modifier hush = Assert.Single(feet.Gives.Modifiers);
        Assert.Equal(("checks", 2.0), (hush.Stat, hush.Value));
        Assert.NotNull(hush.If);
        ItemDefinition ring = ItemDefinition.Read(Node(import, "items/ring-of-warding.json"));
        Assert.Equal("ring", ring.Slot);
        Assert.Equal(new[] { ("ac", ModifierOp.Add, 1.0), ("saves", ModifierOp.Add, 1.0), ("speed", ModifierOp.Max, 40.0) },
            ring.Modifiers.Select(m => (m.Stat, m.Op, m.Value)));
        Assert.Contains(import.Report, line => line.Contains("system.traits.dr.value"));
    }

    [Fact]
    public void A5eClassBecomesAClassWithItsLevelRows()
    {
        // made-up class: two features granted by id, one in the export and one not
        const string json = """
            [
              {"_id": "cls1", "name": "Reed Warden", "type": "class", "system": {
                "identifier": "reed-warden", "hd": {"denomination": "d10"},
                "spellcasting": {"progression": "half", "ability": "wis"},
                "advancement": [
                  {"type": "HitPoints", "level": 1},
                  {"type": "Trait", "level": 1, "configuration": {"grants": ["saves:str", "saves:wis", "armor:lgt", "weapon:mar"],
                    "choices": [{"count": 2, "pool": ["skills:ste", "skills:sur"]}]}},
                  {"type": "Trait", "level": 1, "classRestriction": "secondary", "configuration": {"grants": ["saves:cha"]}},
                  {"type": "ItemGrant", "level": 1, "configuration": {"items": [{"uuid": "Compendium.x.features.Item.f1"}, "Compendium.x.features.Item.f9"]}},
                  {"type": "ScaleValue", "title": "Thorn Strike", "configuration": {"identifier": "thorn-strike", "type": "dice",
                    "scale": {"1": {"number": 1, "faces": 6}, "3": {"number": 2, "faces": 6}}}},
                  {"type": "AbilityScoreImprovement", "level": 4, "configuration": {"points": 2}},
                  {"type": "Subclass", "level": 3}
                ]}},
              {"_id": "f1", "name": "Mire Step", "type": "feat", "system": {"description": {"value": "<p>Moves through mud.</p>"}}},
              {"_id": "f2", "name": "Lone Feat", "type": "feat", "system": {}}
            ]
            """;
        FoundryImport import = FoundryImport.Read(json);
        // the granted feat is the class's feature, not a feat of its own
        Assert.Equal(new[] { "classes/reed-warden.json", "feats/lone-feat.json" }, import.Files.Keys.OrderBy(k => k));
        ClassDefinition warden = ClassDefinition.Read(Node(import, "classes/reed-warden.json"));
        Assert.Equal((10, "wis"), (warden.HitDie, warden.DcAbility));
        Assert.Equal(new[] { "str", "wis", "armor", "weapons" }, warden.Proficiencies);
        Assert.Equal(4, warden.Levels.Count);
        Assert.Equal(2, warden.Levels[0].Skills);
        Assert.Equal("Mire Step", Assert.Single(warden.Levels[0].Features).Name);
        Assert.Equal(1, warden.Levels[0].Scale["thorn-strike"]);
        Assert.Equal(2, warden.Levels[2].Scale["thorn-strike"]);
        Assert.Equal((2, 1, true), (warden.Levels[3].Boosts, warden.Levels[3].BoostStep, warden.Levels[3].BoostsRepeat));
        Assert.Contains(import.Report, line => line.Contains("f9"));
        Assert.Contains(import.Report, line => line.Contains("Subclass"));
        Assert.Contains(import.Report, line => line.Contains("\"half\""));
    }

    [Fact]
    public void AFoundrySceneBecomesAMapOnTheGrid()
    {
        // 10 x 8 squares of 100 px with Foundry's quarter padding: the map starts at (300, 200)
        const string json = """
            {"name": "Crypt", "width": 1000, "height": 800, "padding": 0.25, "grid": {"size": 100, "distance": 5},
             "background": {"src": "worlds/mine/maps/crypt.webp"},
             "walls": [{"c": [300, 250, 800, 250]}, {"c": [550, 300, 550, 600], "door": 1}],
             "lights": [{"x": 550, "y": 650, "config": {"dim": 20, "bright": 10, "color": "#ff8800"}}],
             "tokens": [{"x": 400, "y": 500}, {"x": 900, "y": 500}]}
            """;
        FoundryScene scene = FoundryScene.Read(json, out string why)!;
        Assert.True(scene != null, why);
        Assert.Equal((10, 8), (scene.Width, scene.Height));
        // the wall along y = 250 crosses squares 0 to 5 of row 0
        Assert.True(Enumerable.Range(0, 6).All(x => scene.Walls.Contains(new Cell(x, 0))) && !scene.Walls.Contains(new Cell(0, 1)), string.Join(" ", scene.Walls));
        Assert.Single(scene.Lights);
        Assert.Equal(4, scene.Lights[0].Radius, 3);
        Assert.Equal(new Cell(1, 3), scene.Start);
        Assert.Equal("pictures/crypt.webp", scene.Trace!.Path);
        Assert.Contains(scene.Report, line => line.Contains("1 doors left open"));
        Assert.Null(FoundryScene.Read("""{"name": "Sword", "type": "weapon"}""", out string notScene));
        Assert.Contains("not a Foundry scene", notScene);

        var editor = new MapEditor(new History());
        Assert.True(editor.Load(MapEditor.BlankMap("x", 4, 4), out string error), error);
        editor.ApplyScene(scene);
        GameMap map = editor.Map();
        Assert.Equal((10, 8), (map.Width, map.Height));
        Assert.False(map.Walkable(new Cell(2, 0)));
        Assert.Equal(new Cell(1, 3), map.Markers["partyStart"]);
    }
}
