namespace Yorehold.Rules;

/// <summary>
/// Sheets for the creatures a world starts with. Heroes are a plain build from their class file
/// until characters are built from their choices (P7); creatures take their stat block as written.
/// Until inventories come (P8) the first weapon among their items is the one they fight with.
/// </summary>
public static class WorldSheets
{
    public static CharacterSheet Hero(Ruleset rules, Compendium compendium, ClassDefinition characterClass, string name, int level)
    {
        var sheet = new CharacterSheet { Name = name, Level = Math.Max(1, level), DcAbility = characterClass.DcAbility };
        foreach (AbilityDefinition ability in rules.Abilities)
        {
            sheet.Stats.SetBase(ability.Id, 10);
        }
        int maxHp = characterClass.HitDie + characterClass.BonusHp + (sheet.Level - 1) * (characterClass.HitDie / 2 + 1);
        sheet.Stats.SetBase("maxHp", maxHp);
        sheet.Stats.SetBase("speed", characterClass.Speed);
        sheet.Stats.SetBase("darkvision", characterClass.Darkvision);
        sheet.Stats.SetBase("ac", 10);
        sheet.Hp = maxHp;
        foreach (string proficiency in characterClass.Proficiencies)
        {
            sheet.Proficiencies.Add(proficiency);
        }
        foreach (KeyValuePair<string, string> rank in characterClass.ProficiencyRanks)
        {
            sheet.ProficiencyRanks[rank.Key] = rank.Value;
        }
        foreach (KeyValuePair<string, Resource> resource in characterClass.Resources)
        {
            sheet.Resources[resource.Key] = resource.Value;
        }
        sheet.Weapon = WeaponFrom(compendium, characterClass.Items);
        return sheet;
    }

    public static CharacterSheet Creature(Ruleset rules, Compendium compendium, CreatureDefinition definition, string name)
    {
        var sheet = new CharacterSheet
        {
            Name = name.Length == 0 ? definition.Name : name,
            Level = definition.Level,
            DcAbility = definition.DcAbility,
            Hp = definition.Hp,
        };
        sheet.Death.Saves = definition.DeathSaves;
        foreach (AbilityDefinition ability in rules.Abilities)
        {
            sheet.Stats.SetBase(ability.Id, 10);
        }
        foreach (KeyValuePair<string, int> score in definition.Abilities)
        {
            sheet.Stats.SetBase(score.Key, score.Value);
        }
        sheet.Stats.SetBase("speed", definition.Speed);
        sheet.Stats.SetBase("darkvision", definition.Darkvision);
        sheet.Stats.SetBase("maxHp", definition.Hp);
        foreach (string proficiency in definition.Proficiencies)
        {
            sheet.Proficiencies.Add(proficiency);
        }
        foreach (KeyValuePair<string, string> rank in definition.ProficiencyRanks)
        {
            sheet.ProficiencyRanks[rank.Key] = rank.Value;
        }
        // Stat blocks give the final AC, so take off what ArmorClass will add back.
        int fromAbility = rules.ArmorClassAbility.Length == 0 ? 0 : sheet.AbilityModifier(rules, rules.ArmorClassAbility);
        int fromArmor = rules.ProficiencyRanks.Count == 0 ? 0 : sheet.ProficiencyModifier(rules, "armor");
        sheet.Stats.SetBase("ac", definition.ArmorClass - fromAbility - fromArmor);
        foreach (KeyValuePair<string, Resource> resource in definition.Resources)
        {
            sheet.Resources[resource.Key] = resource.Value;
        }
        sheet.Weapon = WeaponFrom(compendium, definition.Items);
        return sheet;
    }

    // The first item that does damage, as a held weapon. Null leaves an unarmed strike.
    private static Weapon? WeaponFrom(Compendium compendium, IEnumerable<string> items)
    {
        foreach (string id in items)
        {
            ItemDefinition? item = compendium.Item(id);
            if (item != null && item.Damage.Length > 0)
            {
                return new Weapon(item.Damage, item.AttackAbility, item.Hands);
            }
        }
        return null;
    }
}
