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

## For people coming from Foundry

Start with FIRST_ADVENTURE.md: New adventure to a played fight, and the example files a new
adventure comes with.

| In Foundry | Here |
| --- | --- |
| a game system (`system.json`, its data models) | a rules system: a ruleset folder with `ruleset.json` |
| a module or compendium pack of items | a content set (`content.json` with `kind` and `ruleset`) |
| World | an adventure; its Scenes are chapters, each with one map |
| Actor (character, npc) | a hero (built from choices) or a creature file |
| Item: weapon, equipment, consumable, loot | an item file |
| Item: spell | a spell file (an action with `level`, `hands`) |
| Item: feat, class feature | a feat file, or a feature in a class's level rows |
| Item: class, subclass, background, race or ancestry | class, option (`optionKinds`), background, race |
| Advancement (ItemGrant, Trait, ScaleValue, AbilityScoreImprovement, Subclass) | level rows: `features`, `ranks` and proficiencies, `scale`, `boosts`, `options` |
| Activity (attack, save, damage, heal, utility) | an action's `effects` steps: `roll` (attack, save, check), `damage`, `heal`, `condition` |
| Active Effect with `changes` | a condition with `modifiers` (`add`, `multiply`, `override`, `max` for upgrade, `min` for downgrade) |
| Rule element FlatModifier with a predicate | a modifier with `if` |
| Rule elements that do something on a roll (a damage rider) | a trigger (`on: hit`, `if`) |
| Reactions | `reactions/` with a trigger moment |
| Roll data `@abilities.str.mod`, `@details.level`, `@prof`, `@scale.x.y` | the same, or `mod.str`, `level`, `proficiency`, `scale.y` |
| Journal entries | the chapter's journal |

## Bringing your own Foundry content across

Create > Compendium > From Foundry... reads a JSON file exported from your own Foundry world
(right-click an Item or Actor > Export Data, or a list of them) and adds what it can to the
package as one change Undo takes back (`rules/import/FoundryImport.cs`):

| Foundry | Becomes |
| --- | --- |
| dnd5e weapon | an item: damage dice and type, finesse and other properties as traits, two hands, range in squares, price in copper |
| dnd5e armour and shield | an item: AC as the 5e rules here read it (override, Dex cap), a shield's bonus |
| dnd5e spell | a spell from its first activity: attack, save (with half on a save), heal or damage; range, area, concentration, bonus action |
| dnd5e npc | a creature: HP, AC, speed, CR as level, scores, darkvision, and its weapons and armour as items |
| pf2e npc | a creature: level, HP, AC, speed, scores from modifiers, its strikes as items, its printed attack bonus kept as a flat `attack` stat |
| pf2e weapon, armour, spell | an item or spell the same way |
| a feat (either) | a feat; pf2e FlatModifier rule elements as modifiers (fortitude, reflex, will and skill checks with an `if`) |
| pf2e class | a class: HP per level, key ability, first-level ranks (perception, saves, the best weapon and armour ranks, class DC, trained skills), a level row each for its ancestry, class, skill and general feats and skill increases, and its features by level (from feats in the same export, else by name). Ability boosts and rank rises from features are named in the log |
| dnd5e ring, cloak or other worn equipment | an item in the slot its name suggests (ring, neck, cloak, head, hands, feet), else carried with a log line |
| Active Effects on an item or feat | modifiers, for changes of a plain amount to AC, initiative, walking speed, max HP, darkvision, saves and checks (all, one ability's saves, one skill's checks), spell DC, attack and damage; Foundry's modes as add, multiply, min, max, override. Disabled effects and ones not passed to the wearer are skipped; formulas and other paths are named in the log |
| dnd5e class | a class: hit die, casting ability, first-level saves, armour and weapons from its Trait advancements (not the multiclass copy), skill picks as a count, and a level row each for its ItemGrant features, ScaleValues (`scale.<id>`; dice count their dice) and Ability Score Improvements. A feat it grants that is in the same export becomes that feature, with its name and words, instead of a feat. Spell slots, subclasses and skill lists are named in the log |

Everything else (loot, journal entries, other rule elements, predicates, activities on feats)
is named in the log and left out, so nothing goes missing quietly.

A scene comes in through Create > Map > From Foundry scene... (right-click a scene > Export Data):
the map takes the scene's size in squares (its grid size and padding counted), each wall is drawn
as wall squares along its length, lights keep their reach (dim, in the scene's distance per square)
and colour, the first token's square becomes the party's start, and the background becomes the
picture to trace over once it is copied into the package's `pictures/` (`rules/import/FoundryScene.cs`).
Doors, tokens and notes are counted in the log: doors and creatures are placed here in Map and
Encounters.

## Writing files by hand

`docs/schemas/` has a JSON schema per kind of file (item, creature, action, spell, condition,
feat, option, race, background, trigger, reaction, class). VS Code uses them in this repo through
`.vscode/settings.json`; for a package folder of your own, copy `docs/schemas/vscode-settings.json`
into its `.vscode/settings.json` with the paths pointed at the schemas. The editor then completes
field names and flags mistakes as you type; the game's own check stays the last word. The system file itself has one too (`ruleset.schema.json`, for any `ruleset.json`), its keys described as in RULES_LANGUAGE.md. While playing, F6 (Reload content, also on the pause menu) reads every file again and carries on where the party is, so a change made in the editor shows at once, between fights. To check a whole package as the game reads it: `check.ps1 -Package <folder>` prints every problem with its file and field (`items/rusty-sword.json: range: is a whole number from 1 to 1000`).

Not taken: Foundry's document ids and folder records (our file names are the ids), its
per-document permission data, and its HTML descriptions (ours are plain words).
