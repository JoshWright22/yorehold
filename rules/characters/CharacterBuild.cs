namespace Yorehold.Rules;

/// <summary>
/// Turns a character's choices into a sheet with the ruleset and the compendium: scores, race,
/// background, each level's row of its class table, feats in the order taken, spell slots and
/// spells, HP, and the starting gear.
/// </summary>
public static class CharacterBuild
{
    /// <summary>The sheet the choices make, or null with error naming the choice the rules refuse.</summary>
    public static CharacterSheet? Build(Ruleset rules, Compendium compendium, CharacterChoices choices, out string error)
    {
        error = choices.Check(rules);
        if (error.Length > 0)
        {
            return null;
        }
        var classes = new List<ClassDefinition>();
        for (int i = 0; i < choices.Levels.Count; i++)
        {
            ClassDefinition? found = compendium.Class(choices.Levels[i].ClassId);
            if (found == null)
            {
                error = $"levels[{i}].class: no class \"{choices.Levels[i].ClassId}\"";
                return null;
            }
            classes.Add(found);
        }
        ClassDefinition first = classes[0];
        RaceDefinition? race = null;
        if (choices.Race.Length > 0 && !compendium.Races.TryGetValue(choices.Race, out race))
        {
            error = $"race: no race \"{choices.Race}\"";
            return null;
        }
        BackgroundDefinition? background = null;
        if (choices.Background.Length > 0 && !compendium.Backgrounds.TryGetValue(choices.Background, out background))
        {
            error = $"background: no background \"{choices.Background}\"";
            return null;
        }

        var c = new CharacterSheet
        {
            Name = choices.Name,
            Ancestry = race?.Name ?? choices.Race,
            Notes = choices.Notes,
            Level = choices.Level,
            Xp = choices.Xp,
            HitDie = "1d" + first.HitDie,
            DcAbility = first.DcAbility,
        };
        c.ClassName = string.Join(" / ", classes.Select(k => k.Id).Distinct().Select(id => classes.First(k => k.Id == id).Name));
        foreach (KeyValuePair<string, int> score in choices.Scores)
        {
            c.Stats.SetBase(score.Key, score.Value);
        }
        c.Stats.SetBase("ac", rules.BaseArmorClass);
        c.Stats.SetBase("speed", first.Speed);
        c.Stats.SetBase("darkvision", first.Darkvision);
        c.Proficiencies.UnionWith(first.Proficiencies);
        foreach (KeyValuePair<string, string> rank in first.ProficiencyRanks)
        {
            c.ProficiencyRanks[rank.Key] = rank.Value;
        }
        foreach (KeyValuePair<string, Resource> resource in first.Resources)
        {
            c.Resources[resource.Key] = resource.Value;
        }

        // race and background: score changes, skills, speed and senses
        string Adjust(Dictionary<string, int> abilities, string field)
        {
            foreach (KeyValuePair<string, int> change in abilities)
            {
                if (rules.Ability(change.Key) == null)
                {
                    return $"{field}: \"{change.Key}\" isn't an ability of this ruleset";
                }
                c.Stats.SetBase(change.Key, c.Stats.Base(change.Key) + change.Value);
            }
            return "";
        }
        if (race != null)
        {
            error = Adjust(race.Abilities, "race");
            if (error.Length > 0)
            {
                return null;
            }
            c.Proficiencies.UnionWith(race.Proficiencies);
            if (race.Speed > 0)
            {
                c.Stats.SetBase("speed", race.Speed);
            }
            c.Stats.SetBase("darkvision", Math.Max(first.Darkvision, race.Darkvision));
        }
        if (background != null)
        {
            error = Adjust(background.Abilities, "background");
            if (error.Length > 0)
            {
                return null;
            }
            c.Proficiencies.UnionWith(background.Proficiencies);
        }

        // Feats: the race's and background's, then each level's picks in order, so a feat's
        // requirements are judged on the character as it stood when it was taken.
        int RankIndex(string rank) => rules.ProficiencyRanks.FindIndex(r => r.Id == rank);
        void Raise(Dictionary<string, string> ranks)
        {
            foreach (KeyValuePair<string, string> rank in ranks)
            {
                if (!c.ProficiencyRanks.ContainsKey(rank.Key) || rules.ProficiencyRanks.Count == 0
                    || RankIndex(rank.Value) > RankIndex(c.ProficiencyRank(rules, rank.Key)))
                {
                    c.ProficiencyRanks[rank.Key] = rank.Value;
                }
            }
        }
        void Grant(Grants gives, string source)
        {
            foreach (Modifier modifier in gives.Modifiers)
            {
                c.Stats.AddModifier(modifier, source);
            }
            c.Proficiencies.UnionWith(gives.Proficiencies);
            Raise(gives.Ranks);
            foreach (KeyValuePair<string, int> resource in gives.Resources)
            {
                Resource old = c.Resources.GetValueOrDefault(resource.Key) ?? new Resource(0, 0);
                c.Resources[resource.Key] = new Resource(old.Current + resource.Value, old.Max + resource.Value);
            }
        }
        var taken = new HashSet<string>();
        void Take(FeatDefinition feat)
        {
            taken.Add(feat.Id);
            Grant(feat.Gives, "build:feat:" + feat.Id);
        }
        foreach (string id in (race?.Feats ?? new List<string>()).Concat(background?.Feats ?? new List<string>()))
        {
            if (compendium.Feats.TryGetValue(id, out FeatDefinition? given) && !taken.Contains(id))
            {
                Take(given);
            }
        }

        var classesSoFar = new HashSet<string>();
        var classLevels = new Dictionary<string, int>();
        var slotsByClass = new SortedDictionary<string, SortedDictionary<int, int>>(StringComparer.Ordinal);
        var spellCountByClass = new Dictionary<string, int>();
        for (int i = 0; i < choices.Levels.Count; i++)
        {
            LevelChoice level = choices.Levels[i];
            ClassDefinition definition = classes[i];
            classesSoFar.Add(level.ClassId);
            int classLevel = classLevels[level.ClassId] = classLevels.GetValueOrDefault(level.ClassId) + 1;
            bool tabled = definition.Levels.Count > 0;
            ClassLevel? row = classLevel <= definition.Levels.Count ? definition.Levels[classLevel - 1] : null;
            if (row != null)
            {
                foreach (ClassFeature feature in row.Features)
                {
                    Grant(feature.Gives, $"build:feature:{definition.Id}:{feature.Id}");
                }
                Raise(row.Ranks);
                if (row.Slots.Count > 0)
                {
                    slotsByClass[definition.Id] = row.Slots;
                }
                if (row.Spells > 0)
                {
                    spellCountByClass[definition.Id] = row.Spells;
                }
            }

            string at = $"levels[{i}].picks.";
            if (level.Picks.TryGetValue("skills", out List<string>? skills))
            {
                int offered = row?.Skills ?? 0;
                if (tabled && skills.Count > offered)
                {
                    error = $"{at}skills: {skills.Count} picked, this level offers {offered}";
                    return null;
                }
                c.Proficiencies.UnionWith(skills);
            }
            if (!level.Picks.TryGetValue("feats", out List<string>? picked))
            {
                continue;
            }
            string field = at + "feats: ";
            List<string> open = row?.Feats.ToList() ?? new List<string>(); // kinds not yet used at this level
            foreach (string id in picked)
            {
                if (!compendium.Feats.TryGetValue(id, out FeatDefinition? feat))
                {
                    error = $"{field}no feat \"{id}\"";
                    return null;
                }
                FeatRequirements needs = feat.Needs;
                if (tabled)
                {
                    if (!open.Remove(feat.Kind))
                    {
                        error = $"{field}\"{id}\" is a {feat.Kind} feat, and this level has no {feat.Kind} feat to choose";
                        return null;
                    }
                }
                if (taken.Contains(id) && !feat.Repeatable)
                {
                    error = $"{field}\"{id}\" is already taken";
                    return null;
                }
                if (i + 1 < needs.Level)
                {
                    error = $"{field}\"{id}\" needs level {needs.Level}";
                    return null;
                }
                if (needs.Races.Count > 0 && !needs.Races.Contains(choices.Race))
                {
                    error = $"{field}\"{id}\" is for another race";
                    return null;
                }
                if (needs.Classes.Count > 0 && !needs.Classes.Any(classesSoFar.Contains))
                {
                    error = $"{field}\"{id}\" is for another class";
                    return null;
                }
                foreach (KeyValuePair<string, int> least in needs.Abilities)
                {
                    if (c.AbilityScore(least.Key) < least.Value)
                    {
                        error = $"{field}\"{id}\" needs {least.Key} {least.Value}";
                        return null;
                    }
                }
                foreach (string target in needs.Proficiencies)
                {
                    bool trained = rules.ProficiencyRanks.Count == 0
                        ? c.Proficiencies.Contains(target)
                        : RankIndex(c.ProficiencyRank(rules, target)) >= Math.Max(0, RankIndex(rules.ProficientRank));
                    if (!trained)
                    {
                        error = $"{field}\"{id}\" needs training in {target}";
                        return null;
                    }
                }
                Take(feat);
            }
        }

        // Spell slots: for each slot level the most any one class's row gives, so a second casting
        // class widens the choice of spells rather than stacking slots.
        var slots = new SortedDictionary<int, int>();
        foreach (SortedDictionary<int, int> row in slotsByClass.Values)
        {
            foreach (KeyValuePair<int, int> slot in row)
            {
                slots[slot.Key] = Math.Max(slots.GetValueOrDefault(slot.Key), slot.Value);
            }
        }
        foreach (KeyValuePair<int, int> slot in slots.Where(s => s.Value > 0))
        {
            c.Resources[rules.Roles.SlotPrefix + slot.Key] = new Resource(slot.Value, slot.Value);
        }
        AddSpells(c, compendium, choices, classes, slots, spellCountByClass);

        // HP last, so race and feat changes to the HP ability (CON) count
        int con = rules.Roles.HpAbility.Length == 0 ? 0 : c.AbilityModifier(rules, rules.Roles.HpAbility);
        // the system's own formulas where it has them (PF2e: ancestry HP once, class HP + CON each level)
        Formula? firstLevel = rules.Formulas.Of("hpFirstLevel");
        Formula? perLevel = rules.Formulas.Of("hpPerLevel");
        int bonus = first.BonusHp + (race?.BonusHp ?? 0);
        int hp = Math.Max(1, firstLevel?.Whole(name => name switch
        {
            "hitDie" => first.HitDie, "bonus" => bonus, "ability" => con, "level" => 1, _ => null,
        }) ?? first.HitDie + bonus + con);
        for (int i = 1; i < classes.Count; i++)
        {
            int die = classes[i].HitDie;
            int level = i + 1;
            hp += Math.Max(1, perLevel?.Whole(name => name switch
            {
                "hitDie" => die, "ability" => con, "level" => level, _ => null,
            }) ?? die / 2 + 1 + con);
        }
        c.Stats.SetBase("maxHp", hp);
        c.Hp = c.MaxHp;

        if (rules.ProficiencyRanks.Count > 0)
        {
            foreach (KeyValuePair<string, string> rank in c.ProficiencyRanks)
            {
                if (rules.Rank(rank.Value) == null)
                {
                    error = $"proficiencyRanks.{rank.Key}: unknown rank \"{rank.Value}\"";
                    return null;
                }
            }
        }
        GiveItems(c, compendium, first.Items);
        if (background != null)
        {
            GiveItems(c, compendium, background.Items);
        }
        return c;
    }

    public static CharacterSheet? Build(Ruleset rules, Compendium compendium, CharacterChoices choices)
    {
        return Build(rules, compendium, choices, out _);
    }

    /// <summary>Items by id onto a sheet, worn where a slot is free. Unknown ids are skipped.</summary>
    public static void GiveItems(CharacterSheet sheet, Compendium compendium, IEnumerable<string> ids)
    {
        foreach (string id in ids)
        {
            if (compendium.Item(id) is ItemDefinition item)
            {
                sheet.GiveItem(item);
            }
        }
    }

    // Always known: each class's cantrips, and the spells it lists that spend resources instead of
    // slots once the sheet has those resources. A levelled spell needs a slot of its level; a
    // "known" class knows all of those, a "prepared" one chooses its count of them and a
    // "spontaneous" one keeps a fixed set: the character's "spells" picks, then the list's first.
    private static void AddSpells(CharacterSheet c, Compendium compendium, CharacterChoices choices, List<ClassDefinition> classes,
        SortedDictionary<int, int> slots, Dictionary<string, int> spellCountByClass)
    {
        int highestSlot = slots.Where(s => s.Value > 0).Select(s => s.Key).DefaultIfEmpty(0).Max();
        bool AlwaysKnown(SpellDefinition spell)
        {
            if (spell.Level == 0)
            {
                return true;
            }
            return spell.Spends.Count > 0 && spell.Spends.Keys.All(id => c.Resources.TryGetValue(id, out Resource? r) && r.Max > 0);
        }
        List<string> picked = choices.Levels.SelectMany(l => l.Picked("spells")).ToList();
        var chosen = new List<string>();
        foreach (ClassDefinition definition in classes.Distinct())
        {
            var levelled = new List<string>();
            foreach (KeyValuePair<int, List<string>> list in definition.Spells)
            {
                foreach (string id in list.Value)
                {
                    if (!compendium.Spells.TryGetValue(id, out SpellDefinition? spell))
                    {
                        continue;
                    }
                    if (AlwaysKnown(spell))
                    {
                        if (!c.Spells.Contains(id))
                        {
                            c.Spells.Add(id);
                        }
                    }
                    else if (spell.Spends.Count == 0 && list.Key <= highestSlot && !levelled.Contains(id))
                    {
                        levelled.Add(id);
                    }
                }
            }
            int count = spellCountByClass.GetValueOrDefault(definition.Id);
            if (definition.Casting == "prepared")
            {
                int added = 0;
                foreach (string id in levelled.Where(id => !c.Preparable.Contains(id)))
                {
                    c.Preparable.Add(id);
                    if (added < count && !c.Prepared.Contains(id))
                    {
                        c.Prepared.Add(id);
                        added++;
                    }
                }
                c.PrepareLimit += count;
            }
            else if (definition.Casting == "spontaneous")
            {
                var kept = new List<string>();
                foreach (string id in picked.Concat(levelled))
                {
                    if (kept.Count < count && levelled.Contains(id) && !kept.Contains(id))
                    {
                        kept.Add(id);
                    }
                }
                chosen.AddRange(kept.Where(id => !chosen.Contains(id)));
            }
            else
            {
                chosen.AddRange(levelled.Where(id => !chosen.Contains(id)));
            }
        }
        // a spell another class knows outright is not one to prepare
        c.Preparable.RemoveAll(id => c.Spells.Contains(id) || chosen.Contains(id));
        c.Prepared.RemoveAll(id => !c.Preparable.Contains(id));
        c.Spells.AddRange(chosen.Where(id => !c.Spells.Contains(id)));
        c.Spells.AddRange(c.Prepared);
    }
}
