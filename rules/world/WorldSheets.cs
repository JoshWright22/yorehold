namespace Yorehold.Rules;

/// <summary>
/// Sheets for the creatures a world starts with: they take their stat block as written, with
/// their items worn and their spells known. Heroes are built from their choices (CharacterBuild).
/// </summary>
public static class WorldSheets
{
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
        sheet.Spells.AddRange(definition.Spells.Where(compendium.Spells.ContainsKey));
        // Its gear is worn as the heroes' is, but the stat block's AC is already final: armour
        // overriding "ac" would count twice, so what it wears on top of that is left off.
        CharacterBuild.GiveItems(sheet, compendium, definition.Items);
        foreach (Item item in sheet.Inventory.Where(i => i.Equipped && i.Definition.Modifiers.Any(m => m.Stat == "ac")).ToList())
        {
            sheet.Unequip(sheet.Inventory.IndexOf(item));
        }
        return sheet;
    }
}
