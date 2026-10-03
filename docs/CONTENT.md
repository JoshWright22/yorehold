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
rulesets/my-rules.json          optional custom rules
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

`chapter.json` declares `id`, `title`, `map`, `ruleset`, `party` and `encounters`. `ruleset` can be `modern`, `classic`, or a relative JSON path. Paths resolve in the chapter folder first, then at the content root. Absolute paths and parent traversal are rejected.

Party members have `name`, `class`, `color` and integer `at` cells. Encounter groups have unique `id`, optional starting `text`, and `creatures` with a `creature` id, optional `name` and `at`. Placements must be on walkable, distinct cells. Party size comes from the file. Seeing one enemy starts its authored encounter group.

Writer-owned text includes `intro` lines, encounter `text`, `victoryText` (supports `{xp}`), `defeatText`, `resumeText` and `clearedText`. `xpPerVictory` supplies the reward. The code only provides generic defaults for omitted text.

## NPCs, dialogue, quests and story flags

`npcs` lists people the party can talk to: unique `id`, `name`, optional `color`, an integer `at` cell (walkable, not shared with anyone) and a `dialogue` file in the framework's dialogue format (see the framework's DIALOGUE.md). Clicking an NPC walks the selected hero over and opens the conversation; replies are buttons or keys 1-9, Esc walks away. Skill checks roll for the selected hero. NPCs have a sheet too: optional `creature` names a compendium creature (default `commoner`). Right-click > Attack picks a fight with that NPC alone; they can't be talked to after that. Optional `attacked` and `killed` flag lists are set when that happens (the keep's quest fails if Tobb dies). Fights with NPCs don't count toward clearing the chapter. The keep's `dialogue/wren.json` and `dialogue/tobb.json` are working examples.

Story flags are the chapter's memory. Dialogue sets and clears them, and an encounter's optional `set` list is applied when that group is beaten. `quests` names a journal file in the framework's quest format (QUESTS.md); quests and objectives follow the flags, and the party is told when one appears, progresses, completes or fails. J opens the journal. Flags are saved with the adventure.

`completeWhen` lists flags that finish the chapter once all are set (checked after fights and after a conversation ends). Without it, the chapter is complete when every encounter is won.

`endings.cleared` names a cutscene file. It is checked before play. Omit it or use an empty string to finish with the authored `clearedText` banner. Cutscene steps support `camera` in world units, `zoom`, `caption`, `title`, `pause`, `fade`, `bars`, and `event`, with `seconds`, `ease` and `wait`. The shipped `ending.json` is the working example; its captions remain the chapter writer's text.

Saves record chapter id, folder and a signature of chapter/map/rules/definitions/ending data. Edited content or another chapter cannot silently accept old sheets and placements. Earlier keep saves migrate from versions 1/2, with count and fog-size checks retained.
