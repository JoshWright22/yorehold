# What Foundry VTT's systems teach the rules language

Josh, 10/9: Foundry VTT's game systems are public on GitHub, so we can see how they shape their
JSON. Foundry itself is closed; the systems are open: `foundryvtt/dnd5e` (MIT, branch 6.1.x) and
`foundryvtt/pf2e` (Apache-2.0, branch v14-dev). Read on 10/9 for structure only: no code or
content of theirs is copied into Yorehold (their rules text comes from the same SRD and ORC
sources ours does, and is written fresh here). R21 in the roadmap carries what we adopt.

## How they lay out content

| | dnd5e | pf2e | Yorehold today |
| --- | --- | --- | --- |
| A package | `system.json` manifest; packs of documents | the same | `content.json` manifest; a ruleset folder |
| One entry | one YAML/JSON file per document in `packs/_source/<pack>/`, with `_id`, `name`, `type`, `system` | the same, JSON | one JSON file per entry in its kind's folder |
| Creature | an Actor of type `npc`: abilities, skills, bonuses (formula strings), with its attacks and features as embedded Items | an Actor; strikes and abilities as embedded items | a creature file listing items and granted actions |
| What a spell or item does | "activities" on the item: attack, save, damage, heal, check, utility, cast, summon, each with activation cost, consumption, range, target template, damage parts (number, die, types, scaling) | the item's own fields (damage, defense, area) plus "rule elements" | an action's `effects` steps; an item has one `use` |
| Classes | "advancement" entries by level: HitPoints, Trait (proficiencies), ItemGrant (features), ItemChoice, ScaleValue (a named per-level table, read in formulas as `@scale.barbarian.rage-damage`), AbilityScoreImprovement, Subclass | feat and skill-increase levels as lists (`classFeatLevels: [1, 2, 4, ...]`), features as items granted at a level | level rows: features, feats, skills, slots, boosts, options |
| Changing numbers | Active Effects: `changes` of `{key: a data path, mode: add / multiply / override / upgrade / downgrade, value}`, with a duration and statuses | "rule elements" on any item: `FlatModifier` (a selector such as `attack-roll` or `fortitude`, a type, a value formula, a predicate), `RollOption`, `GrantItem`, `TempHP`, `DamageDice`, `Resistance`, `AdjustDegreeOfSuccess`, `ActiveEffectLike` (path, mode, value, phase) | modifiers `{stat, op, value, type}`, always on while their source is |
| When something applies | effect transfer and statuses | predicates over "roll options" (tags like `self:condition:raging`, `target:trait:undead`, `item:trait:agile`) combined with `all`, `any`, `not`, `nor` | formulas over `flag.x`, `targetFlag.x`, `trait.x` in triggers only |
| Formulas | `@abilities.con.mod`, `@details.level` in strings | `@actor.level + @actor.abilities.con.mod` | `level + mod.con` |

## What to take (R21)

1. **Conditional modifiers.** The largest gap. Our modifiers are always on; theirs say which rolls
   they touch and when. A modifier gains `on` (the rolls it touches: `attack`, `damage`, `save`,
   a save or skill id, `ac`, `initiative`), and `if` (a formula over the same names triggers
   read: `trait.ranged`, `targetFlag.undead`, `flag.raging`). That covers Archery (+2 to ranged
   attacks), Danger Sense, Fate stunts ("+2 when you Forcefully attack"), a weapon that only
   bites undead, and PF2e's FlatModifier.
2. **Scale values.** A class row may set named numbers (`"scale": {"rage-damage": 2}`), carried up
   the levels and read in formulas as `scale.rage-damage`, in place of `{1 + (level >= 5) + ...}`.
3. **Items with several actions.** An item lists `actions` (a wand with two spells, a weapon's
   special attack) as well as its `use`.
4. **Modifier ops `max` and `min`** (their upgrade and downgrade) beside add and set: "your speed
   is at least 30", "AC can't drop below 10".
5. **Roll tags.** Triggers and conditions already read flags; giving each roll a set of tags
   (`attack`, `melee`, `spell`, `weapon:longsword`, `trait:agile`) that `if` formulas can test
   is the same idea as their roll options, without a second language.

Not taken: Foundry's document ids and folder records (our file names are the ids), its
per-document permission data, and its HTML descriptions (ours are plain words).
