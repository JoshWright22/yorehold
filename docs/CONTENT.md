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
  races/elf.json               player options: one file each (see Races, backgrounds and feats)
  backgrounds/sage.json
  feats/tough.json
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
  "kind": "adventure",
  "id": "goblin-keep",
  "revision": 1,
  "ruleset": "yorehold@1.0",
  "requires": [],
  "defaultChapter": "chapters/goblin-keep",
  "chapters": ["chapters/goblin-keep"],
  "theme": "ui/theme.json",
  "dialogues": [],
  "cutscenes": []
}
```

Manifest fields:
- `name` (optional, up to 80 characters) is what the library shows; without it the file name is used.
- `kind` (optional) categorizes the package: `adventure`, `ruleset`, `compendium`, `character_class`, `race`, or `feat`. Empty means the kind is inferred from the structure (if it has chapters, it is an adventure; otherwise definitions).
- `id` (optional) is a unique identifier for this package across versions, using lowercase letters, digits, hyphens and underscores.
- `revision` (optional, default 0) is a version number that goes up with each publish.
- `ruleset` (optional) names the ruleset version this adventure requires, e.g. `"yorehold@1.0"`. Empty means the game's own ruleset.
- `requires` (optional) is an array of package ids/revisions this content depends on (e.g. asset packs or shared definitions).

Old packages without manifest fields still load, with empty defaults for all new fields. `chapters` and `defaultChapter` are left out of a definitions-only file. `theme`, `dialogues` and `cutscenes` are optional. Chapter endings are validated automatically, so they need not also appear in `cutscenes`. Declared dialogue files use the framework's `Dialogue` JSON format. They transfer and validate today; interactive chapter dialogue is still to be wired into gameplay.

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
| Item | `id`, `name`, `slot`, `hands`, `damage` dice, `attackAbility`, `weight` (lb), `value` (cp), `quantity`, `magic`, `supplies` (camp supply points per unit), `modifiers` |
| Class | `id`, `name`, `description`, `hitDie`, `bonusHp`, `speed`, `proficiencies`, starting `items` ids |
| Creature | `id`, `name`, `description`, `hp`, `armorClass`, `speed`, fixed `abilities`, `proficiencies`, `items`, `loot`, `token` |

An item's `slot` is where it is worn or held (`mainHand`, `offHand`, `armor`...; none = it can only be carried). Slots ending in `Hand` are held, and `hands` (default 1) is how many of a character's two hands the item takes: a `"hands": 2` weapon can't be held with a shield. Players change gear in the gear panel (I): free between fights, the Interact action's cost on the hero's turn in one. `"magic": true` makes an item count toward the ruleset's `magicItemLimit` (3), carried or worn; weight counts toward the carrying capacity (STR x `carryPerStrength`), past which a hero is slowed and then stopped (`encumberedAt`, `immobileAt`, `encumberedSpeed` in `ruleset.json`).

Modifiers use `stat`, `op` (`add`, `multiply`, `override`) and `value`. `token` supports `color`, radius `size` in cells and optional `image`. Token image paths resolve in the chapter folder first, then at the content root. Unknown item/class/creature ids, invalid values and missing token images are reported before play. The framework's `Compendium` also serializes these three definitions back to JSON.

## Maps

`map.json` contains named `tiles`, a `legend` mapping one character to each tile name, and `layers` of text `rows`. Spaces mean empty cells. All layers have equal dimensions, derived from the first layer. Empty ground is not walkable. Walls/obstacles can be painted over ground on later layers.

Tiles declare `walkable` and `blocksSight` independently. Sight-blocking tile edges create the vision/lighting walls. `art` selects a built-in placeholder painter (grass, dirt, stone, wood, wall, water, tree); other names use the tile's `color`.

Optional `lighting` picks how light works on the map. `mode` is `off` (everything lit, no darkness drawn), `mood` (lights and darkness are only for looks; the default) or `rules` (heroes only see cells that are lit or within their darkvision). `ambient` is the light level where no lamp reaches (`dark`, `dim` or `bright`), `brightFraction` the part of each light's radius that is bright, `carried` the radius in cells of the light each hero carries (0 = none) and `sight` how far heroes see, in cells. Players can override the mode in Settings. Classes and creatures take `darkvision` in feet.

Optional `ambient` is an RGB/RGBA color. `lights` have `at: [x, y]`, `radius` in cells, `color`, and `flame` (false gives a steady light). Light positions are fractional cell coordinates, so `[4.5, 3.5]` centers a light on cell `[4, 3]`. `markers` map names to integer cells. One cell is 64 world units.

The text rows are an import for maps written by hand. The game keeps every map in the framework's `TileMap` and `Objects` (one `Region` per chapter), and a map can be written that way too: `tiles` as an array (`{"name": "stone", "art": "stone", ...}`, tile id = place in the array + 1) and `tileMap` holding the framework's TileMap JSON (FRAMEWORK.md, "Maps, objects and streamed worlds"; `tileSize` 64) in place of `legend` and `layers`. Only floor 0 layers count for walking and sight for now. `GameMap::toJson` writes a loaded map in this form.

`objects` places doors, levers, chests and traps. Each entry is a kit on a cell, `{"kit": "door", "at": [x, y]}`, where any other field changes that copy (`name`, `door`, `lock`, `trap`, `contents`) and `tags` add to the kit's. An entry without `kit` is a whole object in the framework's format, one cell big unless it has an `area`. Kits are one file each in `kits/` at the root or in the chapter's own `kits/` (which wins), named after their id, in the framework's Kit format. The game ships `door`, `locked-door`, `lever`, `chest`, `locked-chest` and `dart-trap`.

- A door with `blocksMovement` and `blocksSight` stops walking and sight until it is opened. `"door": {"locked": true}` needs a key: an item whose id matches the door's `key:<item id>` tag. Add `"lock": {"dc": 15, "skill": "dex"}` and a check can open it too (`dc` 0 or no lock means a key only).
- A lever toggles every door that shares one of its `link:<name>` tags, locked or not.
- Any object with a `flag:<name>` tag sets that story flag the first time it is used.
- A `container` holds `contents`: item ids with counts, and `"coins"` in copper. It is looted like a chapter container. Give it a locked `door` (the lid) and a `lock` and it has to be opened first.
- A `trap` has `detectDc`, `disarmDc`, `detectSkill` (default perception), `disarmSkill` (default dex), `effect` (the effects format, FRAMEWORK.md "Effects", written in place) and `rearms`. A hero within `trapSpotRange` squares (top-level map field, default 2) with a clear view finds it when their passive score reaches `detectDc`. Found traps are drawn and paths go around them. A hero stepping on an armed trap sets it off: its effect runs on them, with its save. Only heroes set traps off.

Heroes use doors, levers, locks and found traps with E when standing beside them: free between fights, the Interact action's cost on the hero's own turn in a fight. A failed disarm by 5 or more sets the trap off. Doors, chests and traps are saved as they were left; saves from before objects load with the map's own.

## Chapters and writer-owned text

`chapter.json` declares `id`, `title`, `map`, `party` and `encounters`, and optionally `ruleset`. Without `ruleset` the chapter plays by the game's own rules, `rulesets/yorehold` (see Rulesets); leave it out unless the chapter is a test of other rules. It can be `modern` or `classic` (sets built into the framework, kept for tests), a relative JSON path, or a folder with `ruleset.json` in it. Paths resolve in the chapter folder first, then at the content root. Absolute paths and parent traversal are rejected.

Party members have `name`, `class`, `color` and integer `at` cells. Encounter groups have unique `id`, optional starting `text`, and `creatures` with a `creature` id, optional `name` and `at`. Placements must be on walkable, distinct cells. Party size comes from the file, one to four. Seeing one enemy starts its authored encounter group.

Each party member is a seat with a ready-made hero. Starting the adventure with New adventure lets players put a character from their library (or one made there and then) in any seat; it keeps that seat's `at` and `color`, and the ready-made hero only plays the seats nobody filled. Quick start plays the ready-made party. `level` (1 to 20, default 1) is the level the chapter is written for: ready-made heroes start at it, and a character made for a seat gets the XP to level up to it.

Companions are NPCs who can join the party through dialogue and approval. The chapter limits the party to 4 player characters plus 2 companion NPCs (6 total). Each NPC may optionally have:
- `approvalStart`: the starting approval score for this NPC (default 0)
- `approvalJoinThreshold`: approval score needed to recruit them (default 0, meaning always recruiteable)

NPCs track approval throughout the adventure and are placed in the party when recruited through dialogue.

`containers` puts things to open on the map: each has a unique `id`, a `name` (default "Chest"), an `at` cell of its own, and any of `items` (item ids), `coins` (in copper: 10 cp to the sp, 10 sp to the gp) and a `loot` table (FRAMEWORK.md, "Loot tables") rolled when the adventure starts. A hero standing on or beside one opens it with E and takes what they click. Creature files can carry `loot` too: when the party wins a fight, each dead enemy leaves what it carried plus what its table gives in a small sack where it fell. Enemies that got away or gave up leave nothing. In the keep, the storeroom has a chest and goblins carry a few coppers.

A creature placement may set `facing`: the direction it looks until it notices the party, in degrees from -360 to 360, where 0 is east (right on the map), 90 south, 180 west and 270 north. Without it the creature looks toward where the party starts. Facing only matters to sneaking heroes, who are noticed inside the vision cone in front of an enemy and not behind it. In the keep, Gob has `"facing": 180` and watches the door.

## Adventures

`adventure.json` at the content root ties chapters together into a playable journey. It defines:

- `id`: unique identifier for this adventure
- `title`: display name for the Play menu
- `description`: optional description of the adventure
- `minLevel`, `maxLevel`: recommended level range (default 1 to 20)
- `recommendedPartySize`: suggested party size (default 4)
- `chapters`: array of chapter folder paths (e.g., `"chapters/chapter-one"`)
- `transitions`: how chapters connect when the party reaches markers
- `flags`: adventure-wide story flags that track across chapters
- `camp`: optional chapter folder the party makes camp in (see Camp, supplies and the stash)

A `transition` specifies how to move from one chapter to another:

- `from`: the chapter id the party is leaving
- `exitMarker`: the marker name on that chapter's map where the exit happens
- `to`: the chapter id the party enters
- `entryMarker`: the marker name on the destination chapter's map where they arrive
- `when`: optional array of story flags that must all be set for this transition to be available

Example:

```json
{
  "id": "goblin-adventure",
  "title": "The Goblin Menace",
  "minLevel": 1,
  "maxLevel": 5,
  "chapters": ["chapters/goblin-keep", "chapters/goblin-warren"],
  "transitions": [
    {
      "from": "goblin-keep",
      "exitMarker": "north-passage",
      "to": "goblin-warren",
      "entryMarker": "southern-entrance"
    },
    {
      "from": "goblin-keep",
      "exitMarker": "secret-exit",
      "to": "goblin-warren",
      "entryMarker": "hidden-passage",
      "when": ["key-found"]
    }
  ],
  "flags": ["key-found", "warren-cleared"]
}
```

The `flags` array pre-declares the adventure's story flags; undeclared flags still work. Transitions with unmet `when` flags are unavailable (the marker doesn't trigger a move). Multiple chapters can share the same entry marker name; the transition specifies which one. `from` and `to` may also be objects, `{"chapter": "goblin-keep", "marker": "north-passage"}`, in place of the two marker fields.

The file is checked when the package is checked or packed, and when one of its chapters is opened: every listed chapter loads, chapter ids are unique, each transition names chapters in the list and markers on their maps (the entry marker on a cell you can stand on), every chapter's `level` is inside `minLevel` to `maxLevel` (1 to 20), and every chapter seats the same number of heroes, since the party travels as one. `recommendedPartySize` is 1 to 4.

In play, a chapter the file lists belongs to the adventure. Between fights, a hero stepping onto an exit marker whose transition is open takes the whole party to the next chapter: the first hero stands on the entry marker, the others on the nearest free squares. Heroes keep their sheets as they are (wounds, spent slots, conditions, gear, coins and XP), rests already taken still count, and every story flag goes along except the ones the old chapter lists in its `localFlags` array in `chapter.json`. Arriving on a marker doesn't trigger it; a hero has to step off and on again. The autosave after travelling is in the new chapter, and loading it opens that chapter even though the adventure starts in the first.

The game's built-in files carry a test adventure in `adventure.json`: `chapters/chapter-one` and `chapters/chapter-two`, with a way there, a way back and a way back that opens once `chapter_two_complete` is set. It is test content, not story.

## Camp, supplies and the stash

Between fights the party can make camp from wherever it is (the Make camp button), unless the chapter's `chapter.json` says `"camp": false`. Camp is a chapter of its own: `camp` in `adventure.json` names the folder (it must load, and can't be one of the adventure's chapters); without it every chapter uses the shared `chapters/camp`, a clearing. A camp chapter needs a `party` list like any chapter but seats as many heroes as come, the extra ones on the first seat's square; they arrive on its `entry` marker if the map has one. Camp is drawn and played like any map, so it can have NPCs and dialogue.

The chapter the party left is kept as it was: Leave camp puts them back where they stood, with the same doors open, enemies down and chests taken. Heroes, flags, rests taken and the stash go both ways. A save made at camp loads at camp and still knows the way back.

At camp only:

- The rests the ruleset marks `campOnly` (the long rest). Its `supplyCost` comes from supplies in the stash first, then from what the heroes who aren't dead carry, in party order and cheapest first. Whole units are used, so a cost that doesn't divide evenly takes a little more.
- The stash: one shared list of items. The selected hero puts a whole entry in or takes one out (Stash button). Worn items come off first; the magic item limit still counts.
- Revival: a dead hero comes back for the ruleset's `revivePrice`, paid by the selected hero, with `reviveHp`. Rests never bring back the dead.

An item's `supplies` is how many supply points each unit is worth (0 = not food). The shipped `supplies` item is worth 10 and costs 5 sp, so a long rest takes four. The keep's storeroom chest holds four.

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
| `proficiencyByLevel` | Legacy proficiency bonus at each level, level 1 first; used when there are no ranks. |
| `proficiencyRanks` | Optional list of ranks with unique `id`, display `name`, `bonus` (0 to 100) and `addsLevel`. |
| `proficientRank`, `untrainedRank` | Rank IDs used when a sheet has no explicit choice, according to its old proficiency list. |
| `baseDc` | Base for a character's action DC before ability and proficiency, 10 by default. |
| `death` | Optional death-save rules; absent/disabled keeps older play (see Downed and death). |
| `xpForLevel` | Total XP needed for each level, level 2 first. |
| `actionsPerTurn`, `bonusActions`, `strikeCostsHands` | Actions in a turn (1 to 10), whether there is a bonus action as well, and whether a Strike costs one action per hand the weapon needs. |
| `sharedTurns` | Consecutive allies share an active initiative block. True for Yorehold; absent means sequential turns for older rulesets. |
| `feetPerSquare` | Size of a map square. |
| `carryPerStrength` | Pounds carried per point of the first ability. |
| `magicItemLimit` | Magic items one character may carry (0 = no limit). Held here until inventory rules use it. |
| `rests` | Each has `id`, `name`, `perAdventure` (uses, 0 = unlimited), a `recovery` and optional `restores`, the resources it refills (`"slots-*"` for every spell slot). Optional `supplyCost` (supply points it uses up), `campOnly` (only at camp) and `resets` (rest ids whose uses come back). The first is the one R takes. The long rest restores spell slots and is taken at camp for 40 supplies, as often as supplies last, and gives back the 2 short rests; both rests restore `focus`. |
| `afterVictory`, `reviveAfterVictory` | A `recovery` for the winners of a fight, and the HP downed winners get back up with (0 = they stay down). |
| `revivePrice`, `reviveHp` | What bringing a dead hero back at camp costs, in copper (0 = it can't be bought; 20000 = 200 gp), and the HP they come back with (0 = full; 1 here). |
| `defaultHitDie`, `hitDieByClass`, `hitDieAbility` | Sides of the hit die, by class name, and the ability added per die. |
| `conditions` | Optional list of conditions written inline; a ruleset folder keeps them as files instead (see Conditions). |

A `recovery` has `kind` (`none`, `full`, `fraction` of max HP, `flat` HP or `hitDice`), with `fraction` (0 to 1), `amount` (HP, or dice with 0 meaning one per level) and `reviveDowned`. A ruleset that fails its checks stops the chapter from loading and names the file.

A package can carry rules of its own as one JSON file or as a folder of the same shape and name it in `chapter.json`. The shipped adventures do not.

## Races, backgrounds and feats

Player options live only in the ruleset folder: `races/`, `backgrounds/` and `feats/`, one file each named after its id, in the framework's format (FRAMEWORK.md, "Races, backgrounds and feats"). A chapter's own folder can't add them. A file with an unknown field, a feat a race or background gives that doesn't exist, or a feat requiring a race or class that doesn't exist stops the chapter loading and names the file.

The game ships human, elf, dwarf and halfling; acolyte, criminal, farmhand, sage, soldier and noble; and a first feat set of each kind: race feats each race gives, skill feats the backgrounds give (expert in one of their skills), and general and class feats for the level slots the class level tables open. A character's `race` and `background` in a save name these ids. The content check builds every race and background with every class and takes every feat once.

## Class level tables

`classes/<id>.json` may have `levels`, one row per class level, in the framework's format (FRAMEWORK.md, "Class level tables"): features, rank rises, the feat kinds and skill picks offered, and spell slots. Fighter, rogue, cleric and wizard ship with rows for levels 1 to 20; the top of each file is still the first-level character. Every table offers a class feat at levels 2, 6, 10, 14 and 18, a skill feat at 4, 12 and 20, a general feat at 8 and 16, and a skill to train at odd levels from 3 (the rogue also picks two at level 1). Cleric and wizard rows carry the full caster's slots, levels 1 to 9. A class without `levels` (the barbarian, older packages) still plays: its levels add HP only.

`xpForLevel` in `ruleset.json` runs to level 20. A character levels into its latest class when XP crosses a threshold; choosing the class and the picks comes with the level-up screen. The content check builds each of the four classes at every level and a mixed fighter/wizard/rogue.

## Character files

The player's characters live in `characters/` beside the save, one file each, written by the game in the save envelope (`"format": "yorehold.character"`, `"version": 1`). The data holds `choices` (the framework's character choices, FRAMEWORK.md), `inventory` (items as a sheet writes them), `coins`, and `away`: the file name of the adventure save the character is playing in, empty when free. `characters/graveyard/` holds characters that can't be played any more; the game lists them after the others and never writes to them. A save names each hero's library file in `library` (empty for heroes made for that adventure; older saves have none).

Characters are made and levelled up under Play > Characters, always with the game's own ruleset (`rulesets/yorehold`), the built-in classes plus those of installed packages, and the ruleset's races, backgrounds and feats. The score methods' numbers are `scoreMethods` in `ruleset.json` (FRAMEWORK.md, "Character choices").

## Proficiency ranks

Yorehold uses Untrained/Trained/Expert/Master/Legendary: bonuses 0/2/4/6/8, with level added
except when untrained. These are entries in `ruleset.json`, so other styles can change both
the names and the maths. Modern and classic retain their older tables.

Classes, creatures and saved sheets accept `"proficiencyRanks": {"weapons":"trained",
"armor":"trained","str":"trained","athletics":"expert","dc":"trained"}` and a
`"dcAbility":"str"`. Skill targets apply to checks, ability targets to saves, weapons to
attacks, armour to AC and DC to action DCs. Explicit choices take precedence over the legacy
`proficiencies` list. Missing choices are trained for listed targets and untrained for others.
Ability checks and initiative keep their existing modifier. Choices are retained but inactive
in table-based rulesets. Unknown ranks, targets or DC abilities stop loading with a named file;
invalid saved choices are refused before any adventure state changes.

A DC is `baseDc + ability modifier + DC proficiency + dc stat modifiers`. Effects whose DC is
`"caster"` use the acting sheet's live DC. An empty `dcAbility` adds no ability modifier.
Creatures may set `level` (default 1). Their `armorClass` remains the final AC written in their
stat block, including ability and rank; increasing the sheet's level later increases trained AC.
Old sheets without these fields still load, using their existing proficiency lists. Saves whose
chapter content changed report that change instead of applying an incompatible sheet.

The starting classes have trained weapons, armour and DCs, plus two trained saves: Strength and
Constitution for fighter/barbarian, Wisdom and Charisma for cleric, Dexterity and Intelligence
for rogue. Existing skill lists remain the fallback. Class level tables will supply later ranks.

## Downed and death

The Yorehold ruleset's `death` object enables death saves. An unmodified d20 rolls against DC 10
at the start of the downed character's turn, including shared initiative blocks. Three successes
make it stable at 0 HP; three failures make it dead. A natural 1 counts as two failures; a natural
20 gets it up with 1 HP. These are the fields `saveDc`, `successes`, `failures`,
`naturalOneFailures`, `naturalTwentyHp`. Damage at 0 HP adds `damageFailures` (1), or
`criticalDamageFailures` (2). Temporary HP absorbs damage first. Hurt while stable starts dying
again with fresh counters. `downedCondition`, `dyingCondition`, `stableCondition`, `deadCondition`
name condition files. Modern/classic do not enable these rules.

Sheets save `death: {saves, successes, failures, stable, dead}`. Old sheets without the object
use their original class/creature eligibility and begin with no counters. Creature files may
opt into saves with `deathSaves: true`; the default is immediate death. Ordinary healing, rests
and victory recovery cannot revive a dead character; paying at camp can. Help gets an adjacent downed ally up with
1 HP; healing effects and potions remove dying/stable state and reset the counters.

The provisional Potion action costs one action, consumes one `potions` resource from its user
and heals a living adjacent ally or the user for `2d4+2`. Class/creature files may set
`resources: {"potions":{"current":1,"max":1}}`; current defaults to max. Each starting class
has one use. Saves preserve what remains; rests do not refill potions. Consumable inventory
will supply this resource in D5. Dead targets are refused before spending the use.

When everyone goes down, the host sends a return command with the latest autosave state. It
restores health, supplies, story, dice counters and positions, and clears the fight. A chapter
may set `"onWipe":{"cutscene":"wipe.json","destination":[[2,3],[2,4],[1,3],[1,4]]}`.
Both fields are optional. The cutscene plays before return and can be skipped; destination
has one distinct, walkable cell per hero in this chapter, away from authored creature/NPC
placements. An occupied destination at the checkpoint falls back to its saved positions.
Otherwise an absent destination keeps the checkpoint positions. Starting a chapter or loading
a save establishes its checkpoint, including in test runs where disk writes are disabled.
Snapshots carry that checkpoint so co-op return uses the host's state.

## Triggers

Triggers fire dialogue or cutscenes in response to chapter events. A chapter may have any number:

```json
"triggers": [
  {
    "id": "treasure-found",
    "when": ["treasure-discovered"],
    "dialogue": "dialogue/found-treasure.json"
  },
  {
    "id": "greeting",
    "dialogue": "dialogue/welcome.json"
  },
  {
    "id": "betrayal",
    "when": ["betrayed-party"],
    "cutscene": "cutscenes/betrayal.json"
  }
]
```

- `id`: unique trigger id, uses a-z, 0-9, - and _; required.
- `when`: optional list of story flags. Empty or absent = fire when the chapter starts (onEnter);
  non-empty = fire once all listed flags are set (onFlag). A trigger fires only once.
- `dialogue`: optional virtual path to a yh::Dialogue JSON file.
- `cutscene`: optional virtual path to a yh::Cutscene JSON file.
- At least one of `dialogue` or `cutscene` must be present.

Trigger state is saved with the adventure: each trigger fires only once per playthrough.

## Non-combat chapter completion

A chapter may define a non-combat win condition with `winCondition`:

```json
"winCondition": {
  "when": ["treasure-delivered", "party-escaped"],
  "dialogue": "dialogue/safe-arrival.json",
  "cutscene": "cutscenes/end-credits.json"
}
```

- `when`: list of story flags that must all be set; required and non-empty.
- `dialogue`: optional virtual path to fire when the condition is met.
- `cutscene`: optional virtual path to fire when the condition is met.

When the last flag in `when` is set (outside of combat), the chapter completes, bypassing the
normal encounter-based completion. If `completeWhen` is also set, both must be satisfied; if
only `winCondition` is set, the chapter ignores encounters. The dialogue and cutscene are
optional; without them, chapter completion shows the standard banner and proceeds.

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

## Actions

What a creature can do on its turn is a file in the ruleset folder, `actions/<id>.json`, named after its `id`. The action bar lists the ones the acting creature has, in `order`. The game ships Strike, Dash (`stride`), Defend, Help, Hide, Seek, Shove, Grapple, Interact, Ready and End turn; a ruleset without an `actions` folder gets Strike, Dash and End turn built in.

```json
{
  "id": "strike",
  "name": "Strike",
  "description": "Attack a creature next to you with the weapon in hand.",
  "order": 10,
  "cost": "hands",
  "target": { "kind": "creature", "side": "enemy", "range": 1 },
  "effects": [
    { "do": "roll", "kind": "attack", "steps": [
      { "do": "damage", "dice": "weapon", "when": "hit", "minimum": 1 }
    ] }
  ]
}
```

| Field | Meaning |
|---|---|
| `id`, `name`, `description` | `id` must match the file name. `name` and `description` are what players read. |
| `order` | Where it sits on the action bar, lowest first. |
| `cost` | Actions it takes (0 to 10, default 1), or `"hands"`: one per hand the weapon needs. |
| `endsTurn` | `true` ends the turn once it is done. |
| `general` | `true` (default): every creature has it. `false`: only creatures something grants it to. |
| `requires` | `{ "flags": [...], "without": [...], "resources": { "name": 1 } }`: condition flags needed, flags that bar it, and resources it uses. |
| `target` | `kind` `self` (default), `creature` or `point` (a square, for an action with an `area`); a creature or point target has `side` (`enemy`, `ally`, `any`), `range` in squares, and `downed` (default false) to allow unconscious targets. Dead or withdrawn creatures cannot be targeted. Ranged creature actions need a clear line of sight. |
| `area` | `shape` `burst`, `cone`, `line` or `square` with `size` in squares (plus `width` for a line, `angle` for a cone). The effect lands on everyone of the target's `side` inside it with a clear line from where it starts; see Spells. |
| `readies` | Records this action id for a reaction, until the creature's next turn or the fight ends. It must name an existing action that neither readies another nor ends the turn. |
| `log` | A line for the log when it is done; `{name}` is whoever does it. |
| `effects`, `save` | What it does, in the effect steps the framework reads (see FRAMEWORK.md, Effects). Movement left this turn is the resource `movement`. |

Three ids are ones the game itself uses: clicking an enemy uses `strike`, Space uses `end-turn`, and enemies use `stride` to dash. A ruleset may change them but should keep them. A file with a bad field, an unknown condition or a step the effects do not know stops the chapter from loading and is named in the error.

Each effect step accepts `ifFlag` to apply only to targets carrying that condition flag. Help heals only `downed` allies; its `aided` condition grants advantage until the next attack or the ally's turn ends. Hidden grants attack advantage and ends on attacking, taking damage or moving in combat. Hide checks against each standing enemy's passive Perception; all checks must pass. Seek checks one enemy's passive Stealth. Shove and Grapple check passive Athletics. A push stops at a wall or occupied square and spends no movement from its target.

Interact currently stands up from prone; equipment and map objects extend it in D1 and F1. Ready records a Strike and ends the turn. The next enemy entering reach triggers it, spending the creature's reaction.

## Flanking and cover

The ruleset folder may contain `positioning.json`. Missing files keep the older targeting and AC
rules. Yorehold enables positioning and names `off-guard` as `flankingCondition`, with
`flankingReach: 1`. Two standing foes on exactly opposite sides, both within that reach and able
to act with clear centre rays, flank the creature. Its AC and effect flags are evaluated with that
condition while the geometry holds. An existing copy of the condition does not stack with flanking;
moving away leaves no condition on the saved sheet. The condition file supplies the -2 AC.

Cover traces rays from the attacker's centre to the target's inset corners: any obstructed corner
gives half cover, three of four gives three-quarters, and four gives full cover. Terrain can obscure
a shot completely; one clear corner permits a ranged action. Standing creatures screen rays but
provide at most half cover; downed creatures provide none. `halfCoverArmorClass: 2` and
`threeQuartersCoverArmorClass: 4` add to AC for ranged attacks (action range above 1).
`creaturesProvideCover: true` enables body screening; `coverAgainstMelee: false` keeps melee
unaffected by cover. `enabled: false` disables positioning. The generic defaults and validation
are in FRAMEWORK.md; an unknown flanking condition names the file and problem when loading.

The HUD marks visible flanked creatures and previews cover against the selected ranged action.
The **Flanking/cover** test scene gives the first hero a ranged test Strike: the enemy is
flanked by two allies and screened by one of them. The shipped Strike remains melee.

## Shared turns

With `sharedTurns: true`, adjacent entries on the same side in initiative share a block, for heroes
and enemies alike. Click an unfinished hero's party card or initiative card to switch actors. Green
borders mark available members; the current one has the accent border. Each player may select only
creatures they own. Actions, free movement and reactions remain separate per creature and refresh
together at block start. Switching preserves spent budgets and conditions; End turn finishes only
that member. The block ends when every standing member finishes. Enemy turns default to initiative
order, but the host can choose another member.

Movement, an attack queued after movement and reaction prompts must resolve before switching.
Surprise skips the first block; a member down when its block starts waits until next round even if
healed. Initiative entries still separate blocks when down or withdrawn. Reinforcements roll into
the list immediately and first act next round. Saves continue to be made between fights. The test
browser's **Shared turns** scene begins with one hero's action spent and another hero selected.

## Reactions

The ruleset's `reactions/<id>.json` files name movement triggers and the action they offer. The game ships `opportunity` (Strike when a foe leaves reach) and `readied` (the recorded action when a foe enters reach). A ruleset without this folder has no movement reactions. New definitions are included in the chapter signature, and malformed definitions stop loading with the file and field named.

```json
{ "id": "opportunity", "name": "Opportunity Strike", "trigger": "leavesReach", "action": "strike", "order": 10, "promptSeconds": 2 }
```

`trigger` is `leavesReach` or `entersReach`. Name `action`, or use `readied: true` in its place. `order` defaults to 0; `promptSeconds` defaults to 2 (0.1 to 30). Reach comes from the action's targeting range. The same action effects run for a reaction, spending one reaction instead of turn actions, including for two-handed weapons. A creature gets its reaction back at the start of its turn.

Every edge of voluntary movement is checked, including free movement and movement bought with Dash. An opportunity happens before leaving; a readied action happens after entering. Forced movement does not provoke. A lethal reaction stops movement where it happened and advances the turn. Planned movement is paid when the move starts.

Reactions are automatic by default. The host's Settings switch asks the reacting hero's owner to Take or Skip and defaults to Take after `promptSeconds`. The move waits for the answer; Skip preserves the reaction budget. Each offer has its own id, so stale replies cannot choose a later offer. Other actions wait while an offer is open; the host supplies the prompt setting in the movement command so peers use the same rule. The test browser's Reaction prompt scene holds time for inspection.

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

## Consumables

Items may have a `use` action: for example, `"use": {"cost":1,
"target":{"kind":"creature","side":"ally","range":1,"downed":true},
"effects":[{"do":"heal","dice":"2d4+2"}]}`. Effects and optional `save` use the framework's
effect format. The item must have no equipment slot. `cost` is a numeric action cost (defaults
to the item's hands); id and name default to the item's. Item effects are checked against the
chapter's ruleset at load. Bad fields and unknown conditions report the offending item.

In Gear (I), click Use and choose a target. One unit is removed on use, including when a target
saves; an invalid target or insufficient actions spends nothing. Reach, line of sight, side,
death state and ownership are checked. Healing can get a living downed ally up. Between fights,
use is free and limited to party targets; hostile scrolls require a fight. In combat the acting
hero pays the item's cost. Scrolls consume no spell slots. Pages keep all inventory entries and
valid targets accessible. Saved, traded and handed-over consumables retain their effects.

The starter items are `healing-potion` (2d4+2 HP, one action, 50 gp), `ward-scroll`
(1d6+2 temporary HP on self, one action, 25 gp), and `ember-scroll` (2d6 fire, Dexterity DC 12
for half, range 6 squares, two actions, 50 gp). Consumables do not count as magic items unless
their file sets `magic`. Each starting class carries one potion; Wren sells all three. Legacy
character sheets retaining a `potions` resource can still use the old Potion action; new sheets
use inventory instead. Older adventure saves whose content signature changed report that clearly.

## Spells

Spells live only in the ruleset folder, `spells/<id>.json`, in the framework's spell format
(FRAMEWORK.md, "Spells"): an action with `level` (0 = cantrip), `hands` and `concentration`.
A spell costs one action per hand unless it gives a `cost`, and may not share an id with an action.
Class files list them by level, `"spells": {"0": ["spark"], "1": ["flame-fan", "mire"]}`, and say
how the class casts with `"casting"`: `known` (the default: every listed spell of a level it has
slots for), `prepared` (it chooses that many of them, its level rows' `"spells": N`) or
`spontaneous` (a fixed repertoire of N, from the character's `"spells"` picks, else the list's
first). Listed cantrips are always known, and so are listed focus spells once the hero has a focus
pool. All of it is rebuilt from the hero's choices on every load; prepared spells are kept.
Effects are checked against the ruleset when the chapter loads; a bad
field, an unknown condition or a spell listed under the wrong level names the file.

`spellcasting.json` beside `ruleset.json` says how the ruleset casts (every field optional):

```json
{ "hands": "free", "slotPrefix": "slots-", "upcast": true, "prepareAfter": ["long"],
  "concentration": { "onDamage": "save", "ability": "con", "minimumDc": 10, "damageShare": 0.5, "endsWhenDown": true } }
```

`hands: "free"` means the spell's hands must be empty: put a shield or weapon away in Gear first
(`"ignored"` makes hands only set the cost). Cantrips spend nothing; a levelled spell spends the
lowest slot of its level or above (`upcast`), and a step with `"scale": {"by": "slot"}` grows with
the slot spent. Concentration: one such spell at a time per caster. Casting another ends the
first; damage asks for a save against the larger of `minimumDc` and `damageShare` of the damage
(`onDamage` may be `breaks` or `ignored`); dropping to 0 HP, the end of the fight and a rest end
it, and so does everything it left running out. Ending it takes off the conditions and modifiers
the spell put on creatures. Concentration and spent slots are saved.

Preparing: the cleric and wizard are `prepared` casters. They prepare 2 spells at level 1, 3 at
level 3, 4 at 5, 5 at 7 and 6 from level 9; a new hero starts with the first ones on its list. In
the spell panel (K) each spell of the list has a Prepare or Unprepare button while the hero may
choose: before its first fight and after a rest named in `prepareAfter` (the long rest), until the
next fight starts. The choice and whether it is still open are saved; saves from before keep
loading with the first spells of the list prepared.

Focus spells: a spell with `"spends": {"focus": 1}` spends a focus point instead of a slot, and is
known by a class listing it once the hero has a `focus` resource. Both casters get one point at
level 1 (a "Focus Pool" feature in their level table); the short and long rests restore `focus`
(`restores` in ruleset.json). The spell panel shows the pool beside the slots.

In a fight a hero's spells are on the action bar. A creature target is clicked like Strike; a
point target is aimed at a square: the burst, cone, line or square is drawn on the map with the
squares it covers, a ring on everyone it would land on and the range ruler in feet, in orange
when it can be cast there and red when not. Esc puts an aimed spell away. K opens the spell panel:
slots, free hands, what the hero concentrates on, and every spell with why it can't be cast now.
Between fights, spells that help can be cast from there on the party; spells that harm or aim at
the map need a fight.

The starter lists, ten spells each: two cantrips, one focus spell and seven to prepare (three of
level 1, two of 2, two of 3), so a caster always has more on its list than it can prepare. A
levelled spell grows with each slot above its own level unless it says otherwise; a cantrip or
focus spell that grows does so every four character levels.

| Spell | Class, level | What it does |
|---|---|---|
| `spark` | wizard, 0 | One enemy within 6: 1d6 lightning, Dexterity save negates. |
| `chill-bite` | wizard, 0 | One enemy within 6: 1d4 cold and slowed for a round, Constitution save negates. |
| `arcane-dart` | wizard, focus | One enemy within 12: 2d4+1 force, no save. |
| `flame-fan` | wizard, 1 | Two hands, three-square cone: 2d6 fire, Dexterity save for half. |
| `mire` | wizard, 1 | Concentration, one-square burst within 6: enemies who fail a Strength save are slowed. |
| `glass-skin` | wizard, 1 | The caster gets +2 AC for ten rounds. |
| `rams-breath` | wizard, 2 | Two hands, three-square cone of enemies: 3d6 force, Strength save for half; a failed save pushes them two squares. |
| `lead-limbs` | wizard, 2 | Concentration, one enemy within 8: slowed and -2 to attacks, Constitution save negates. |
| `cinder-burst` | wizard, 3 | Two hands, two-square burst within 12, friend or foe: 5d6 fire, Dexterity save for half. |
| `earth-heave` | wizard, 3 | Two-square burst of enemies within 8: 3d6 bludgeoning, Dexterity save for half; a failed save knocks them prone. |
| `rebuke` | cleric, 0 | One enemy within 6: 1d6 radiant and frightened 1, Wisdom save negates. |
| `kind-word` | cleric, 0 | No hands, one action: an ally within 6 is aided (advantage on their next attack this turn). |
| `shield-of-faith` | cleric, focus | An ally within 6 gains 1d8+2 temporary HP. |
| `mend` | cleric, 1 | Touch: 1d8+2 healing, gets a downed ally up. |
| `brand-of-light` | cleric, 1 | One enemy within 12: 3d6 radiant, Dexterity save for half; a failed save leaves it off-guard. |
| `rallying-hymn` | cleric, 1 | No hands, one action, concentration: allies within 3 of the cleric get +1 to attacks. |
| `gathered-mending` | cleric, 2 | Allies within 2 of the cleric, downed ones too: 2d4+2 healing. |
| `iron-vow` | cleric, 2 | Concentration, an ally within 6: +2 AC. |
| `dawnburst` | cleric, 3 | Two hands, two-square burst of enemies within 10: 4d8 radiant, Constitution save for half. |
| `steadfast-chorus` | cleric, 3 | No hands, two actions: allies within 3 of the cleric stop being frightened and gain 2d6+3 temporary HP. |

The **Spell targeting** test scene arms Mire on a caster's turn with the pointer on a
goblin; **Preparing spells** opens the spell panel of the first preparing hero before any fight.

## NPCs, dialogue, quests and story flags

An NPC may have a `merchant` object. Example:

```json
"merchant": {
  "coins": 10000, "buyMultiplier": 1, "sellMultiplier": 0.5,
  "stock": [{ "item": "mace", "quantity": 2, "value": 500 }]
}
```

Stock uses known item ids; `quantity` defaults to the item's quantity and optional `value`
overrides its unit value in copper. Prices are the unit value times the multiplier, rounded
up to buy and down to sell. Both stock and purse are finite and saved. Between fights, stand
beside the peaceful NPC and use Trade or E to open the shop. A click buys or sells one unit;
I opens Gear to put a worn item away before selling. The selected hero spends and receives
the coins; the magic item limit still applies. Sold items join the merchant's stock with
their saved properties. An item with no positive price has no offer. Wren carries two maces
and two shields; all shipped gear has a provisional copper value in its item file.

`npcs` lists people the party can talk to: unique `id`, `name`, optional `color`, an integer `at` cell (walkable, not shared with anyone) and a `dialogue` file in the framework's dialogue format (see the framework's DIALOGUE.md). Clicking an NPC walks the selected hero over and opens the conversation; replies are buttons or keys 1-9, Esc walks away. Skill checks roll for the selected hero. NPCs have a sheet too: optional `creature` names a compendium creature (default `commoner`). Right-click > Attack picks a fight with that NPC alone; they can't be talked to after that. Optional `attacked` and `killed` flag lists are set when that happens (the keep's quest fails if Tobb dies). Fights with NPCs don't count toward clearing the chapter. The keep's `dialogue/wren.json` and `dialogue/tobb.json` are working examples.

Story flags are the chapter's memory. Dialogue sets and clears them, and an encounter's optional `set` list is applied when that group is beaten. `quests` names a journal file in the framework's quest format (QUESTS.md); quests and objectives follow the flags, and the party is told when one appears, progresses, completes or fails. J opens the journal. Flags are saved with the adventure.

`completeWhen` lists flags that finish the chapter once all are set (checked after fights and after a conversation ends). Without it, the chapter is complete when every encounter is won.

`endings.cleared` names a cutscene file. It is checked before play. Omit it or use an empty string to finish with the authored `clearedText` banner. Cutscene steps support `camera` in world units, `zoom`, `caption`, `title`, `pause`, `fade`, `bars`, and `event`, with `seconds`, `ease` and `wait`. The shipped `ending.json` is the working example; its captions remain the chapter writer's text.

Saves record chapter id, folder and a signature of chapter/map/rules/definitions/ending data. Edited content or another chapter cannot silently accept old sheets and placements. Earlier keep saves migrate from versions 1/2, with count and fog-size checks retained.

A save's `choices` list holds one character file per hero, in party order: name, scores and how they were reached, one entry per level with its class and picks, XP (the format is in FRAMEWORK.md, Character choices). Loading rebuilds each hero's sheet from its choices and the current class files, then keeps what happened in play from the saved sheet: HP lost, conditions, inventory, death saves, resources spent. Editing a class file therefore changes saved heroes on their next load. A save from before `choices` takes them from its sheets (the scores on the sheet, every level in the class the chapter gave that hero). A hero whose XP passes a level gains it in their latest class, with the extra HP, until the level-up screen exists.
