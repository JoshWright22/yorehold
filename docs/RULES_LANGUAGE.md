# The rules language

A rules system is a folder of JSON files. There is no script format and no code: a procedure
is a JSON list of steps, a rule is a short formula inside the JSON, and dice are dice text. The
game reads a system the same way whether it ships with the game, comes in a package from the
site, or the story model writes it from a rulebook (R13). This page is the whole language in one
place, for people and for the story model; CONTENT.md has the long form of each part.

The one thing the game assumes: the table is a square grid seen from above, played as a
multiplayer turn-based game where the AI plays every creature that isn't a hero. Everything
else here is the system's to say: what a turn is, what a roll means, what harm uses up, what a
character is made of, how they advance.

Nothing in a system can reach files, the network or the engine. Every loop has a limit and every
roll comes from the game's seeded dice, so an uploaded system is safe to run and a fight can be
played again exactly. A draft is checked by loading it: a refusal names the file and the field
and says what was expected ("checks.attack.defence: unknown defence \"will\"").

## The folder

| Path | What it holds |
| --- | --- |
| `ruleset.json` | The system's own rules: everything in "ruleset.json" below. |
| `actions/` | Things a creature does on its turn: cost, target, area, effects. |
| `reactions/` | Off-turn answers: a trigger moment and the action it runs. |
| `triggers/` | Effects that go off by themselves for whoever is granted them. |
| `conditions/` | States with flags, modifiers, events that end them, saves. |
| (any `modifiers`) | `stat`, `op`, `value`, `type`, and `if`: a formula that makes it count only on the rolls it holds for (`ranged`, `save.dex`, `trait.agile`, `targetFlag.undead`). |
| `surfaces/` | Ground that does something to whoever stands in it. |
| `spells/` and `spellcasting.json` | Actions with a level and a cost in slots or resources. |
| `classes/`, `races/`, `backgrounds/`, `feats/`, `options/` | What characters are built from. |
| `items/`, `creatures/` | Gear and the creatures adventures place. |
| `positioning.json`, `stealth.json` | Flanking and cover; how hiding works. |

## Formulas

A formula is text: numbers, names, `+ - * / %`, comparisons (`== != < <= > >=`), `&& || !`,
`a ? b : c`, and `min`, `max`, `floor`, `ceil`, `round`, `abs`, `clamp(x, low, high)`. True is 1,
false is 0. A formula reads only the names its place hands it; a name nobody gives is refused at
load, so a typo never reaches play.

Names every formula about a creature can read (its sheet):

| Name | Value |
| --- | --- |
| `level` | Its level. |
| `mod.<ability>`, `score.<ability>` | An ability's modifier and score (`mod.caster`: its spellcasting ability). |
| `stat.<name>` | Any number on the sheet: `stat.ac`, `stat.maxHp`, `stat.resist.fire`, numbers a creature file sets in `stats`. |
| `prof.<target>` | Its proficiency bonus with a skill, save, weapon or armour. |
| `trait.<trait>` | 1 when the weapon in hand has the trait (`trait.finesse`). |
| `worn.<slot>` | 1 while something is worn or held in that slot (`worn.armor`, `worn.offHand`): 5e's unarmoured defence reads it. |
| `field.<id>` | How many lines of one of the system's fields are written. |
| `scale.<id>` | A number its class rows set by level (`scale.sneak_attack`; `-` in the id written `_`). |
| `proficiency` | The proficiency bonus by its level. |

Foundry VTT's spelling works as well, so a formula copied from a Foundry item reads: `@abilities.str.mod`
(or `@actor.abilities.str.mod`) is `mod.str`, `.value` is `score.str`, `@details.level`, `@actor.level`
and `@level` are `level`, `@prof` is `proficiency`, `@scale.rogue.sneak-attack` is `scale.sneak_attack`,
`@attributes.hp.max` is `stat.maxHp`. Dice take them without braces: `1d10 + @abilities.con.mod`. A
path the game doesn't read is refused with its name.

Each place adds its own names: a roll kind's `degree` reads `total`, `die`, `modifier`, `dc`;
critical damage reads `dice`, `flat`, `max`; a defence may read `ac`; `damageTaken` reads
`amount`, `resist`, `weak`, `immune`; the dying track reads `dying`, `wounded`, `critical`; a
trigger's `if` reads `advantage`, `critical`, `flag.<flag>`, `targetFlag.<flag>`. CONTENT.md,
"Checks and formulas", lists every formula a system may set and its names.

## Dice

`2d6+3`, `1d20-1`, `4d6kh3` (keep highest), `2d20kl1` (keep lowest), `d%`, `1d6!` (exploding,
at most 20 times), `6d6s5` (count dice showing 5 or more), `4dF` (Fate dice, -1/0/+1), and dice
whose count is a formula in braces: `{ceil(level / 2)}d6`. A system's own die lists what each
face counts in braces after the `d`: `2d{0,0,1,1,2,1}` is two six-faced dice that count 0, 0, 1,
1, 2 and 1 (up to 100 faces, each -1000 to 1000). A list of whole numbers with commas is always
faces; anything else in braces after the `d` is a formula for the number of sides
(`1d{hands.free >= 1 ? 10 : 8}`). Such dice keep, drop and count successes like any other but
don't explode; odds and averages are counted from the faces, and a thrown one shows on the
smallest solid its faces fit evenly (three faces on a d6, each twice).

## ruleset.json

Identity and numbers:

| Key | Says |
| --- | --- |
| `id`, `name`, `version` | Which system this is. |
| `abilities`, `skills` | The numbers a character has (Fate's approaches are its abilities). |
| `saves` | Saves of their own (Fortitude, Reflex, Will) or none: abilities are saves. |
| `modifierTable`, `scoreMin`, `scoreMax`, `scoreMethods` | How a score becomes a modifier and how scores are set. |
| `proficiencyByLevel`, `proficiencyRanks`, `proficientRank`, `untrainedRank` | Proficiency as a number or ranks. |
| `roles` | Which ability or skill the game uses for initiative, HP, attacks, carrying, perception, stealth. |
| `formulas` | The system's own count of a creature's numbers (attack, AC, DC, saves, HP per level...). |
| `fields` | Words a character is made of (Fate's high concept, trouble, aspects). |

Rolls and defences:

| Key | Says |
| --- | --- |
| `checks` | Roll kinds (`attack`, `check`, `save`, `initiative` and any of the system's own): dice, dice with advantage and disadvantage, outcomes worst to best, the `degree` formula that picks one, `opposed`, the `defence` attacks meet, and `criticalDamage`. |
| `defences` | Defences beside `ac`, each a formula (Fate's Defend). |
| `baseDc`, `passiveBase`, `trapFumble` | DCs and passive scores; when a trap check fumbles. |

Harm, dying and rest:

| Key | Says |
| --- | --- |
| `tracks` | What harm uses up, in order (HP; Fate's stress and consequences). |
| `damageTaken` (in `formulas`) | Resistances, weaknesses and immunities. |
| `death` | Death saves or a dying track; the downed, dying, stable and dead conditions; what clears wounded. |
| `rests`, `afterVictory`, `reviveAfterVictory`, `reviveHp`, `revivePrice` | Recovery. |

The turn:

| Key | Says |
| --- | --- |
| `actionsPerTurn`, `bonusActions`, `freeMove`, `strikeCostsHands` | What a turn holds. |
| `attackPenalty` | A formula from attacks made this turn (PF2e's multiple attack penalty). |
| `turnOrder` | One initiative order or side by side; rolled or by the modifier alone; `picked`: whoever acted names who's next. |
| `sharedTurns` | Members of a side next in order pick who goes. |
| `turnWords` | What the system calls an action, a bonus action, a reaction, a free action. |

Characters:

| Key | Says |
| --- | --- |
| `creation` | The steps of making a character and the parts each picks; what parts are called. |
| `featKinds`, `optionKinds` | Kinds of feat; kinds of pick beside race and class (PF2e's heritage). |
| `advancement`, `xpForLevel`, `hitDieByClass`, `defaultHitDie` | How characters go up: experience, milestones, or not at all. |
| `sheet` | Which parts the character sheet shows, in order, and their labels. |
| `encumberedAt`, `immobileAt`, `encumberedSpeed`, `carryPerStrength`, `magicItemLimit`, `feetPerSquare` | Carrying and distance. |
| `companions` | Who joins the party and how. |

## Effects

An action, spell, trigger, item use, trap or surface does what its `effects` list says. Each
step has `do` and may have `target` (`self`, `target`, `area`, `allies`, `enemies`), `when` (an
outcome of the roll above it, like `hit` or `criticalSuccess`, or an event), `ifFlag`, `scale`
(by level or slot) and `onSave`.

| `do` | Fields | Does |
| --- | --- | --- |
| `damage` | `dice`, `type`, `crit`, `minimum` | Harm, through resistances and the tracks. |
| `heal`, `tempHp` | `dice` | Healing (tracks that heal), temporary HP. |
| `condition` | `id`, `remove`, `duration`, `value` | Adds or removes a condition. |
| `modifier` | `id`, `stat`, `op`, `value`, `duration`, `type` | A timed change to a number. |
| `move` | `how` (`push`, `pull`, `teleport`, `approach`), `distance` | Moves someone. |
| `resource` | `id`, `op` (`spend`, `restore`), `amount` | A resource, or a track by its id. |
| `summon`, `light`, `surface`, `flag` | ... | Creatures, light, ground, story flags. |
| `roll` | `kind` (`attack`, `check`, `save` or a system kind), `ability`, `dc`, `against`, `reach`, `steps` | A roll; its `steps` may wait on its outcomes. An attack's `against` names a defence. |
| `repeat` | `times`, `steps` | The steps again, a bounded number of times. |
| `choose` | `options` | The doer picks one list of steps. |

Steps nest at most six deep; triggers set off by triggers stop four deep.

## Moments

| Where | Moments |
| --- | --- |
| Trigger `on` | `hit`, `miss`, `crit` (its own attack), `hitBy`, `kill`, `turnStart`. |
| Reaction `trigger` | `leavesReach`, `entersReach`, `hit`, `missed`, `allyHit`, `beforeHit`, `spellCast`. |
| Condition `ends` and step `when` | `turnStart`, `turnEnd`, `attack`, `damage`, `healed`, `move`, `rest`, `fightStart`, `fightEnd`. |
| Track `clears` | `fightEnd` or a rest's id. |

## The fixed core

What a system can choose from but not add to; a mechanic outside these needs a game update:
the effect step kinds and moments above; the sheet's sections (`vitals`, `tracks`, `fields`,
`level`, `scores`, `saves`, `skills`, `defences`, `weapon`, `feats`, `uses`, `conditions`,
`carrying`); creation's parts (`name`, `fields`, `race`, `options`, `background`, `class`,
`scores`, `skills`, `feats`); turn order modes; advancement modes; and the screens (the fight
HUD, hotbar and portraits).

## Checking a system

Load it: the game refuses anything it can't run and says where. Then measure it:
`CheckKind.Odds` gives the chance of every outcome of a roll, `CheckKind.ExpectedDamage` what an
attack is worth, and `FightSimulation.Forecast` plays an encounter many times with the AI on both
sides ("this fight: won 9 in 10, 1 in 5 lose a hero, 3 rounds"). Create's encounter panel shows
the forecast; the adventure builder will balance by it.
