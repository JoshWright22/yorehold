using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>A sheet as a save keeps it: every number as it stands, modifiers with their sources, so it reads back the same.</summary>
public sealed partial class CharacterSheet
{
    public JsonObject ToJson()
    {
        var stats = new JsonObject();
        foreach (KeyValuePair<string, float> stat in Stats.Bases)
        {
            stats[stat.Key] = stat.Value;
        }
        var modifiers = new JsonArray();
        foreach (AppliedModifier applied in Stats.Modifiers)
        {
            modifiers.Add(new JsonObject
            {
                ["stat"] = applied.Modifier.Stat,
                ["op"] = applied.Modifier.Op switch { ModifierOp.Multiply => "multiply", ModifierOp.Override => "override", _ => "add" },
                ["value"] = applied.Modifier.Value,
                ["source"] = applied.Source,
            });
        }
        var resources = new JsonObject();
        foreach (KeyValuePair<string, Resource> resource in Resources)
        {
            resources[resource.Key] = new JsonArray(resource.Value.Current, resource.Value.Max);
        }
        var conditions = new JsonArray();
        foreach (ActiveCondition active in Conditions)
        {
            conditions.Add(new JsonObject { ["id"] = active.Id, ["rounds"] = active.RoundsLeft, ["value"] = active.Value });
        }
        var ranks = new JsonObject();
        foreach (KeyValuePair<string, string> rank in ProficiencyRanks.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            ranks[rank.Key] = rank.Value;
        }
        var j = new JsonObject
        {
            ["name"] = Name,
            ["ancestry"] = Ancestry,
            ["className"] = ClassName,
            ["hitDie"] = HitDie,
            ["notes"] = Notes,
            ["level"] = Level,
            ["xp"] = Xp,
            ["hp"] = Hp,
            ["tempHp"] = TempHp,
            ["coins"] = Coins,
            ["dcAbility"] = DcAbility,
            ["stats"] = stats,
            ["modifiers"] = modifiers,
            ["resources"] = resources,
            ["conditions"] = conditions,
            ["proficiencies"] = Strings(Proficiencies.OrderBy(p => p, StringComparer.Ordinal)),
            ["ranks"] = ranks,
            ["death"] = new JsonObject
            {
                ["saves"] = Death.Saves, ["successes"] = Death.Successes, ["failures"] = Death.Failures,
                ["stable"] = Death.Stable, ["dead"] = Death.Dead,
            },
            ["spells"] = Strings(Spells),
            ["preparable"] = Strings(Preparable),
            ["prepareLimit"] = PrepareLimit,
            ["prepared"] = Strings(Prepared),
            ["inventory"] = new JsonArray(Inventory.Select(i => (JsonNode)i.ToJson()).ToArray()),
            ["hotbar"] = Hotbar.ToJson(),
        };
        if (_weapon != null)
        {
            j["weapon"] = new JsonObject { ["damage"] = _weapon.Damage, ["attackAbility"] = _weapon.AttackAbility, ["hands"] = _weapon.Hands };
        }
        return j;
    }

    /// <summary>A sheet ToJson wrote. Worn items keep their modifiers from the saved list, not from being put on again.</summary>
    public static CharacterSheet Read(ContentNode node)
    {
        node.RequireObject("a sheet is an object");
        var sheet = new CharacterSheet
        {
            Name = node.Text("name", ""),
            Ancestry = node.Text("ancestry", ""),
            ClassName = node.Text("className", ""),
            HitDie = node.Text("hitDie", ""),
            Notes = node.Text("notes", ""),
            Level = node.Int("level", 1, 1, 1000),
            Xp = node.Int("xp", 0, 0),
            Hp = node.Int("hp", 0),
            TempHp = node.Int("tempHp", 0, 0),
            Coins = node.Int("coins", 0, 0),
            DcAbility = node.Text("dcAbility", ""),
            PrepareLimit = node.Int("prepareLimit", 0, 0),
        };
        foreach (KeyValuePair<string, ContentNode> stat in node.Get("stats")?.Members() ?? Array.Empty<KeyValuePair<string, ContentNode>>())
        {
            sheet.Stats.SetBase(stat.Key, (float)stat.Value.AsNumber());
        }
        foreach (ContentNode m in node.Get("modifiers")?.Items() ?? Array.Empty<ContentNode>())
        {
            var modifier = new Modifier(m.At("stat").AsText(), ContentParts.OpFrom(m, "op"), m.At("value").AsNumber());
            sheet.Stats.AddModifier(modifier, m.Text("source", ""));
        }
        foreach (KeyValuePair<string, ContentNode> resource in node.Get("resources")?.Members() ?? Array.Empty<KeyValuePair<string, ContentNode>>())
        {
            int[] pair = resource.Value.Items().Select(n => n.AsInt()).ToArray();
            if (pair.Length != 2)
            {
                throw resource.Value.Fail("is [current, max]");
            }
            sheet.Resources[resource.Key] = new Resource(pair[0], pair[1]);
        }
        foreach (ContentNode c in node.Get("conditions")?.Items() ?? Array.Empty<ContentNode>())
        {
            sheet.Conditions.Add(new ActiveCondition { Id = c.At("id").AsText(), RoundsLeft = c.Int("rounds", -1), Value = c.Int("value", 1) });
        }
        sheet.Proficiencies.UnionWith(node.Texts("proficiencies"));
        foreach (KeyValuePair<string, ContentNode> rank in node.Get("ranks")?.Members() ?? Array.Empty<KeyValuePair<string, ContentNode>>())
        {
            sheet.ProficiencyRanks[rank.Key] = rank.Value.AsText();
        }
        if (node.Get("death") is ContentNode death)
        {
            sheet.Death.Saves = death.Bool("saves", true);
            sheet.Death.Successes = death.Int("successes", 0, 0);
            sheet.Death.Failures = death.Int("failures", 0, 0);
            sheet.Death.Stable = death.Bool("stable", false);
            sheet.Death.Dead = death.Bool("dead", false);
        }
        sheet.Spells.AddRange(node.Texts("spells"));
        sheet.Preparable.AddRange(node.Texts("preparable"));
        sheet.Prepared.AddRange(node.Texts("prepared"));
        foreach (ContentNode item in node.Get("inventory")?.Items() ?? Array.Empty<ContentNode>())
        {
            sheet.Inventory.Add(Item.Read(item));
        }
        // saves from before the bars could be arranged have none: every action goes on in order
        if (node.Get("hotbar") is ContentNode hotbar)
        {
            sheet.Hotbar = Hotbar.Read(hotbar);
        }
        if (node.Get("weapon") is ContentNode weapon)
        {
            sheet._weapon = new Weapon(weapon.Text("damage", ""), weapon.Text("attackAbility", ""), weapon.Int("hands", 1, 0, HandCount));
        }
        return sheet;
    }

    private static JsonArray Strings(IEnumerable<string> list) => new(list.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
}
