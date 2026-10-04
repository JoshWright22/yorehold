# Content files and transfer

Content is a folder of JSON files and assets. Copy individual definitions between projects, or zip a complete folder into a `.yore` package. `.yore` is a regular ZIP archive, mounted directly; the game does not extract it or execute code from it.

## Adding content to the game

A `.yore` file is how rulesets, classes, items, creatures, maps and whole adventures are shared. Any of these opens it:

- Double-click it. Running the game once registers `.yore` files for the current user, so they open with that copy of the game.
- Drag it onto the game window.
- Pass it on the command line: `yorehold.exe my-adventure.yore`.

The game checks the whole file first. If it is valid, it is copied into the library (`library/` beside the save, `%APPDATA%/Yorehold/Yorehold/library` on Windows) and its first adventure is selected on the Play menu. Play > Adventures lists every built-in and added adventure; the last one picked is remembered. Adding a file with the same name again replaces the old copy. A file that fails its checks is not copied, and the title screen says what was wrong. To remove one, delete it from the library folder.

Each added adventure keeps its own autosave. A file may hold only definitions (classes, items, creatures, rulesets) and no chapter: leave `chapters` out of the manifest. It is checked, stored and listed, and its definitions join the compendium: everything there is to build with when making characters or adventures. If two files define the same id, the later file name wins there.

Added files never change an adventure. Adventures are prewritten: each plays with exactly what its own file carries, so it behaves the same for everyone whatever else is installed.

## Layout

```text
content.json
classes/fighter.json
items/longsword.json
creatures/goblin.json
rulesets/yorehold/             the game's rules (built in; see Rulesets)
  ruleset.json
  stealth.json
  conditions/prone.json        one file per condition
rulesets/my-rules.json         optional custom rules, as one file or a folder like the above
ui/theme.json                  colors and frame styling
design/                        optional game/UI design documents
dialogues/                     optional dialogue documents
chapters/goblin-keep/
  chapter.json
  map.json
  ending.json
  classes/                     optional local definitions/overrides
  items/
  creatures/
  art/                         optional token images
```

A transferable folder contains all referenced definitions and story files. Built-in framework fonts still come with the app. Game/UI design documents can be stored as Markdown or JSON and travel with the package; they are not interpreted as gameplay or UI layout code.

The manifest lists each playable chapter and its default:

```json
{
  "format": "yorehold.content",
  "version": 1,
  "name": "The Goblin Keep",
  "defaultChapter": "chapters/goblin-keep",
  "chapters": ["chapters/goblin-keep"],
  "theme": "ui/theme.json",
  "dialogues": [],
  "cutscenes": []
}
```

`name` (optional, up to 80 characters) is what the library shows; without it the file name is used. `chapters` and `defaultChapter` are left out of a definitions-only file. `theme`, `dialogues` and `cutscenes` are optional. Chapter endings are validated automatically, so they need not also appear in `cutscenes`. Declared dialogue files use the framework's `Dialogue` JSON format. They transfer and validate today; interactive chapter dialogue is still to be wired into gameplay.

## Validate, export and load

Build normally. From the client folder, using the standard `out` build:

```powershell
.\out\bin\RelWithDebInfo\yorehold-content.exe check .\assets
.\out\bin\RelWithDebInfo\yorehold-content.exe pack .\assets .\out\goblin-keep.yore
.\out\bin\RelWithDebInfo\yorehold-content.exe check .\out\goblin-keep.yore

$env:YOREHOLD_CONTENT = (Resolve-Path .\out\goblin-keep.yore).Path
.\out\bin\RelWithDebInfo\yorehold.exe
Remove-Item Env:YOREHOLD_CONTENT
```

The output must be a new file outside the source content folder. Existing packages are not overwritten. `pack` validates the manifest and every declared chapter before exporting, using only that folder's content so missing dependencies cannot be hidden by the installed game. It copies the entire folder, including art, dialogue and design documents.

For a loose chapter without a package manifest, `check <content-root> <chapter-folder>` validates that chapter and its dependencies. `YOREHOLD_CHAPTER=chapters/my-chapter` selects a different chapter inside the current package; `YOREHOLD_CONTENT` also accepts a plain folder. Missing/broken content displays an error on the title screen and disables starting the game.

## Definitions

One file per id. Filenames must match `id`, which uses lowercase letters, digits, hyphens and underscores. Shared entries load first; `chapters/<id>/items`, `classes` and `creatures` then add/replace entries with matching ids.

| Kind | Fields |
|---|---|
| Item | `id`, `name`, `slot`, `damage` dice, `attackAbility`, `weight`, `value`, `quantity`, `modifiers` |
| Class | `id`, `name`, `description`, `hitDie`, `bonusHp`, `speed`, `proficiencies`, starting `items` ids |
| Creature | `id`, `name`, `description`, `hp`, `armorClass`, `speed`, fixed `abilities`, `proficiencies`, `items`, `token` |

Modifiers use `stat`, `op` (`add`, `multiply`, `override`) and `value`. `token` supports `color`, radius `size` in cells and optional `image`. Token image paths resolve in the chapter folder first, then at the content root. Unknown item/class/creature ids, invalid values and missing token images are reported before play. The framework's `Compendium` also serializes these three definitions back to JSON.

## Maps

`map.json` contains named `tiles`, a `legend` mapping one character to each tile name, and `layers` of text `rows`. Spaces mean empty cells. All layers have equal dimensions, derived from the first layer. Empty ground is not walkable. Walls/obstacles can be painted over ground on later layers.

Tiles declare `walkable` and `blocksSight` independently. Sight-blocking tile edges create the vision/lighting walls. `art` selects a built-in placeholder painter (grass, dirt, stone, wood, wall, water, tree); other names use the tile's `color`.

Optional `lighting` picks how light works on the map. `mode` is `off` (everything lit, no darkness drawn), `mood` (lights and darkness are only for looks; the default) or `rules` (heroes only see cells that are lit or within their darkvision). `ambient` is the light level where no lamp reaches (`dark`, `dim` or `bright`), `brightFraction` the part of each light's radius that is bright, `carried` the radius in cells of the light each hero carries (0 = none) and `sight` how far heroes see, in cells. Players can override the mode in Settings. Classes and creatures take `darkvision` in feet.

Optional `ambient` is an RGB/RGBA color. `lights` have `at: [x, y]`, `radius` in cells, `color`, and `flame` (false gives a steady light). Light positions are fractional cell coordinates, so `[4.5, 3.5]` centers a light on cell `[4, 3]`. `markers` map names to integer cells. One cell is 64 world units.

## Chapters and writer-owned text

`chapter.json` declares `id`, `title`, `map`, `party` and `encounters`, and optionally `ruleset`. Without `ruleset` the chapter plays by the game's own rules, `rulesets/yorehold` (see Rulesets); leave it out unless the chapter is a test of other rules. It can be `modern` or `classic` (sets built into the framework, kept for tests), a relative JSON path, or a folder with `ruleset.json` in it. Paths resolve in the chapter folder first, then at the content root. Absolute paths and parent traversal are rejected.

Party members have `name`, `class`, `color` and integer `at` cells. Encounter groups have unique `id`, optional starting `text`, and `creatures` with a `creature` id, optional `name` and `at`. Placements must be on walkable, distinct cells. Party size comes from the file. Seeing one enemy starts its authored encounter group.

A creature placement may set `facing`: the direction it looks until it notices the party, in degrees from -360 to 360, where 0 is east (right on the map), 90 south, 180 west and 270 north. Without it the creature looks toward where the party starts. Facing only matters to sneaking heroes, who are noticed inside the vision cone in front of an enemy and not behind it. In the keep, Gob has `"facing": 180` and watches the door.

## Rulesets

The game's rules are a folder, `rulesets/yorehold/`, and every number the rules use is in it. Changing a number there changes the game; nothing in the code repeats it. `ruleset.json` is the framework's ruleset format:

| Field | Meaning |
|---|---|
| `version`, `id`, `name` | Format version (1), the ruleset's id and its display name. |
| `abilities` | List of `id` and `name`. The first is the one carrying uses. |
| `skills` | List of `id`, `name` and the `ability` each uses. |
| `modifierTable` | `d20` ((score - 10) / 2, rounded down) or `classic`. |
| `scoreMin`, `scoreMax` | Bounds on ability scores. |
| `baseArmorClass`, `armorClassAbility`, `initiativeAbility` | Unarmoured AC, and the abilities added to AC and initiative (empty = none). |
| `passiveBase` | A passive score, such as the passive Perception sneaking is rolled against, is this plus the modifier. |
| `proficiencyByLevel` | Proficiency bonus at each level, level 1 first. |
| `xpForLevel` | Total XP needed for each level, level 2 first. |
| `actionsPerTurn`, `bonusActions`, `strikeCostsHands` | Actions in a turn (1 to 10), whether there is a bonus action as well, and whether a Strike costs one action per hand the weapon needs. |
| `feetPerSquare` | Size of a map square. |
| `carryPerStrength` | Pounds carried per point of the first ability. |
| `magicItemLimit` | Magic items one character may carry (0 = no limit). Held here until inventory rules use it. |
| `rests` | Each has `id`, `name`, `perAdventure` (0 = unlimited) and a `recovery`. The first is the one R takes. |
| `afterVictory`, `reviveAfterVictory` | A `recovery` for the winners of a fight, and the HP downed winners get back up with (0 = they stay down). |
| `defaultHitDie`, `hitDieByClass`, `hitDieAbility` | Sides of the hit die, by class name, and the ability added per die. |
| `conditions` | Optional list of conditions written inline; a ruleset folder keeps them as files instead (see Conditions). |

A `recovery` has `kind` (`none`, `full`, `fraction` of max HP, `flat` HP or `hitDice`), with `fraction` (0 to 1), `amount` (HP, or dice with 0 meaning one per level) and `reviveDowned`. A ruleset that fails its checks stops the chapter from loading and names the file.

A package can carry rules of its own as one JSON file or as a folder of the same shape and name it in `chapter.json`. The shipped adventures do not.

## Conditions

Each condition is one file in the ruleset folder, `conditions/<id>.json`, named after its `id`. A new condition is a new file. The game ships off-guard, frightened, prone, slowed, grabbed, hidden, downed, dying, dead and shielded.

```json
{
  "id": "frightened",
  "name": "Frightened",
  "description": "Shaken: worse at attacking and defending by its value, which drops by one each round.",
  "modifiers": [
    { "stat": "attack", "op": "add", "value": -1 },
    { "stat": "ac", "op": "add", "value": -1 }
  ],
  "flags": ["frightened"],
  "stacking": "value",
  "maxValue": 4,
  "perValue": true,
  "decay": 1,
  "ends": ["rest"]
}
```

| Field | Meaning |
|---|---|
| `id`, `name`, `description` | `id` must match the file name. `name` and `description` are what players read. |
| `modifiers` | Changes to stats while it lasts: `stat` (`ac`, `attack`, `damage`, `speed`, an ability...), `op` (`add`, `multiply`, `override`) and `value`. |
| `advantageOnAttacks`, `disadvantageOnAttacks` | `true` forces the creature's attack rolls. |
| `flags` | What it stops or marks. `cantAct` takes the creature's actions and reaction for the turn and `cantMove` its movement. `offGuard`, `prone`, `hidden`, `downed`, `dying`, `dead`, `frightened`, `slowed` and `shielded` mark the state for rules that ask about it. |
| `duration` | Rounds it lasts unless the effect applying it says otherwise. Leave out (or -1) for "until something ends it". |
| `stacking` | Applying it again: `refresh` (default) restarts the duration, `longest` keeps the longer one, `value` adds the values up to `maxValue`. |
| `maxValue`, `perValue`, `decay` | With `value` stacking: the highest value, whether `add` modifiers count once per point of value, and how much the value falls at the end of each round. |
| `ends` | What ends it: `turnStart` or `turnEnd` (its own turn), `attack` (it attacks), `damage` (it is hit), `healed` (it is brought back above 0 HP), `move`, `rest`, `fightStart`, `fightEnd`. |
| `save` | `{ "ability": "wis", "dc": 12 }`: a save at the end of each round that ends it on a success. |
| `removes` | Other conditions taken off when this one goes on. |

A file with an unknown event or stacking, a save with an ability the ruleset lacks, or `removes` naming a condition that does not exist stops the chapter from loading and is named in the error.

The game puts three of them on by itself. A sneaking hero is `hidden`. A hero at 0 HP is `downed` until healed; any other creature at 0 HP is `dead`, unless it got away or was let go. The rest are for actions, spells and items to apply.

## Stealth rules

`stealth.json` in the ruleset's folder sets the numbers sneaking runs on. Content made before rulesets were folders keeps working: a `rules/stealth.json` at the content root is still read, and wins. Every field is optional, and so is the file; these are the defaults:

```json
{
  "checkEvery": 5,
  "sneakSpeed": 0.5,
  "darkBonus": 5,
  "dimBonus": 2,
  "brightBonus": 0,
  "critical": true
}
```

| Field | Meaning |
|---|---|
| `checkEvery` | Metres a sneaking hero moves inside an enemy's vision cone between Stealth checks (above 0). A check is also made on first coming into view. One square is the ruleset's `feetPerSquare`. |
| `sneakSpeed` | Walking speed while sneaking, as a share of normal speed (above 0, at most 1). |
| `darkBonus`, `dimBonus`, `brightBonus` | Added to a Stealth check made in darkness, dim light or bright light (-20 to 20). Light levels only differ on maps whose lighting `mode` is `rules`; elsewhere everything counts as bright. |
| `critical` | With `true`, a natural 1 is always spotted and a natural 20 never is. |

How it plays: C or the Sneak button makes a player's heroes sneak. They walk at `sneakSpeed`, cover their carried light, and see a red cone in front of each visible enemy that has not noticed the party. A cone reaches as far as heroes see on that map (`sight` under a roof) and stops at walls. Inside one, a hero rolls Stealth (the `stealth` skill, or Dexterity in a ruleset without it) plus the light bonus against the enemy's passive Perception (the ruleset's `passiveBase`, 10, plus its Perception modifier). In darkness an enemy only sees as far as its `darkvision`. A failed check starts the fight with that enemy's group. Right-click > Attack on an unaware enemy while sneaking starts the fight as an ambush: its group is surprised and loses its first turn. Heroes who are not sneaking start the fight as soon as they and an enemy see each other, whichever way it faces. A value out of range fails the content check and names the file.

Writer-owned text includes `intro` lines, encounter `text`, `victoryText` (supports `{xp}`), `defeatText`, `resumeText` and `clearedText`. `xpPerVictory` supplies the reward. The code only provides generic defaults for omitted text.

## NPCs, dialogue, quests and story flags

`npcs` lists people the party can talk to: unique `id`, `name`, optional `color`, an integer `at` cell (walkable, not shared with anyone) and a `dialogue` file in the framework's dialogue format (see the framework's DIALOGUE.md). Clicking an NPC walks the selected hero over and opens the conversation; replies are buttons or keys 1-9, Esc walks away. Skill checks roll for the selected hero. NPCs have a sheet too: optional `creature` names a compendium creature (default `commoner`). Right-click > Attack picks a fight with that NPC alone; they can't be talked to after that. Optional `attacked` and `killed` flag lists are set when that happens (the keep's quest fails if Tobb dies). Fights with NPCs don't count toward clearing the chapter. The keep's `dialogue/wren.json` and `dialogue/tobb.json` are working examples.

Story flags are the chapter's memory. Dialogue sets and clears them, and an encounter's optional `set` list is applied when that group is beaten. `quests` names a journal file in the framework's quest format (QUESTS.md); quests and objectives follow the flags, and the party is told when one appears, progresses, completes or fails. J opens the journal. Flags are saved with the adventure.

`completeWhen` lists flags that finish the chapter once all are set (checked after fights and after a conversation ends). Without it, the chapter is complete when every encounter is won.

`endings.cleared` names a cutscene file. It is checked before play. Omit it or use an empty string to finish with the authored `clearedText` banner. Cutscene steps support `camera` in world units, `zoom`, `caption`, `title`, `pause`, `fade`, `bars`, and `event`, with `seconds`, `ease` and `wait`. The shipped `ending.json` is the working example; its captions remain the chapter writer's text.

Saves record chapter id, folder and a signature of chapter/map/rules/definitions/ending data. Edited content or another chapter cannot silently accept old sheets and placements. Earlier keep saves migrate from versions 1/2, with count and fog-size checks retained.
