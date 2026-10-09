# Content files and transfer

## In the Godot port

This file came over from the C++ client and the formats are the same: every file it describes loads
unchanged. The readers are in `rules/content/`, one type per content kind, and the tests load every
shipped file through them (`tests/ContentTests.cs`). Where this file says FRAMEWORK.md it means
`../yorehold-framework/docs/FRAMEWORK.md`, which still describes effects, actions, player options,
class level tables, spells and loot tables.

What the port reads so far, and where it differs:

- A bad file fails with the file, then the field, then what is wrong:
  `items/mace.json: hands: is a whole number from 0 to 4`. Fields inside lists carry their place:
  `chapters/pit/chapter.json: encounters[0].creatures[1].at: Nib starts on an occupied cell`.
- Faces: a creature's cards and its token show the picture its `token.image` names, or else
  `portraits/<creature id>.png`, and a hero's show `portraits/<class name in lower case>.png`. Any
  size; small pixel art is drawn sharp. Paths are content paths, from the package's
  root (not the chapter folder). With no picture the disc and initial are drawn.
  The token and the cards are cut from the picture around `token.focus`, `[x, y]` from 0 to 1
  across it (default `[0.5, 0.5]`), as large as the frame's shape fits, divided by `token.zoom`
  (1 to 8, default 1). A focus near an edge is pushed back in, so nothing outside the picture
  shows.
  The game ships no faces: every picture comes from the content, and a package's own pictures win
  over the game's folder. png, jpg or webp. A conversation shows the same picture whole and large,
  standing on the bottom of the screen, so a tall picture with a see-through background (a figure
  from the knees or waist up) looks best there; the hero's is mirrored to face the speaker.
- Tiles: a tile type shows its `image`, or else `tiles/<art>.png` for its kind of ground
  (`grass`, `dirt`, `stone`, `wood`, `wall`, `water`, `tree`), or else a plain palette fill.
- Art packs: each folder in the user folder's `art/` (`%APPDATA%\Godot\app_userdata\Yorehold\art\<pack>\`)
  holds pictures by the same content paths (`tiles/grass.png`, `portraits/goblin.png`,
  `icons/<id>.png`) and sits over the game's content in play and in Create's playtest, so a
  player's pictures are used everywhere. Packs are read in name order, the last winning; a
  package's own pictures win over every pack.
- Icons: an action or spell shows `icons/<action or spell id>.png` on the hotbar and in the spell
  book when the content or an art pack has one, else its first letter.
- Objects: doors, chests, levers, found traps, sacks the dead leave and lamp flames show
  `objects/<name>.png`: `door`, `door-open`, `door-locked`, `chest`, `chest-open` (emptied),
  `lever`, `trap`, `trap-off` (disarmed), `sack`, `flame`, `block` (anything else). Without one
  they are plain palette blocks; a locked door or chest gets a brass edge either way.
- Token frames (the skin): every token wears its side's frame over its face,
  `ui/tokens/<side>.png` for `mine` (this player's heroes), `party` (other players' heroes), `ally`
  (companions and others on the party's side), `enemy` and `neutral`. A frame is drawn over the
  whole token, so its middle should be see-through. `ui/tokens/token.json` says how faces sit in
  the frames: `{"shape": "round" | "square", "inset": 0.1}` (the face starts `inset` of the frame's
  width in from its edge; 0 to 0.45). Without frames each side is a plain ring in its colour.
- The game draws no pictures of its own: faces, tiles, icons and objects all come from content or
  art packs, and the fallback is a plain fill, a disc or a letter.
- Hotbars: a sheet saved by the game carries `hotbar`: `{"slots": [action ids, "" for empty, at most
  24], "seen": [ids]}`. An action not in `seen` is put in the first empty slot; one taken off stays
  off. A sheet without it gets every action in order.
- Content is read from folders. `.yore` archives, the library, packing and the tools named under
  "Validate, export and load" are not ported yet.
- A chapter's `ruleset` is a folder or a file. The framework's built-in `modern` and `classic` sets are
  not in the port, and a chapter naming one is refused. Content with no `rulesets/yorehold` folder under
  it has no rules to fall back on.
- Surfaces (`rulesets/yorehold/surfaces/`) are read with the ruleset. Besides `id`, `name`,
  `description`, `effects`, `duration`, `ends` and `isSpellEffect`, the fields the shipped files carry are
  kept: `damagePerRound` (dice), `damageType`, `save` (`ability`, `dc`), `onSave`, `slows`, `speed` (0 to
  1), `slips`, `slipAbility`, `slipDc`, `extinguishes` and `extinguishedBy`. A creature that starts its
  turn in one takes `damagePerRound` (with the save and `onSave`), and a surface laid where one it
  `extinguishes` (or that names it in `extinguishedBy`) lies puts that one out. A surface's `size` is a
  radius in squares; between fights a round is six seconds. Slowing and slipping are not used yet.
- A creature file may list `"spells": ["spark"]`, ids from the ruleset's `spells/`. It casts them with
  the slots and `focus` its `resources` give it, and the game plays them when one is worth more than a
  strike. An unknown id fails the load.
- `chapters/spell-test` is a test chapter outside the adventure, for the spell screenshot runs. Its own
  `creatures/goblin-hexer.json` casts Spark and Chill bite.
- A merchant is read from `stock` only. A save keeps each shop's purse and stock itself, so the saved
  `inventory` form in a chapter file isn't needed.
- A map's `tileMap` may not carry a `delta`. Nothing in play changes tiles yet; doors, chests and traps
  are saved as objects.
- The adventure save is `saves/adventure.json` in the game's user folder, in the `yorehold.adventure`
  envelope at version 4, with the file before it kept as `.bak`. It holds the chapter id and folder, the
  seed and the dice counters (so a loaded game rolls the same as one that never stopped), flags, fired
  triggers, explored fog, objects, piles, shops, surfaces, rests used, the stash, the companion roster,
  the way back from camp, the heroes' choices and every creature's sheet and place. The C++ client's
  versions 1 to 3 kept sheets in another shape and are refused with a message.
- The save signature of a chapter is not worked out yet. A save is checked against the chapter id, the
  map's size and the counts of creatures, objects and NPCs instead, and a hero's sheet is built again from
  their choices so a class that is gone fails by name.
- A cutscene step with no `ease` uses `inOutCubic`, the C++ client's curve. `chapters/trigger-test` has
  `intro-scene.json`, played by its opening trigger, for the cutscene screenshot run.
- `ui/theme.json` and the two files in `create/` are not rules content. They only have to be JSON here.
- `ui/keys.json` is new: what the keys do. `{"actions": [{"id", "name", "group", "description", "keys"}]}`,
  one entry per thing a key can do, with the keys it ships with as names (`"C"`, `"F5"`, `"Kp Add"`, the
  names Godot gives keys). The id is the input action the game listens for (`pan_left`, `sheet`,
  `end_turn`). Two actions may not ship with the same key. Escape, Enter and the number keys are fixed
  and are not listed.
- `ui/colors.json` and `ui/fonts.json` are the screens' look, read at start: colours by role
  (`greys.panel`, `red.main`...) and faces by role (`sans`, `book`, `mono`, each a list of system
  fonts tried in order). A file of the same name laid on top (an art pack now, a skin later)
  replaces the roles it names, in what the code draws and in the shared theme alike. A role's
  list may start with a font file the skin brings (`"book": ["ui/fonts/body.ttf", "Georgia"]`,
  .ttf, .otf, .woff or .woff2, one per role); the system names after it fill in missing letters.
  `ui/shapes.json` cuts the boxes: `corners` (the radius of every corner that is rounded at
  all; square ones stay square), `edges` (the width of the thin 1 px lines; thick accent bars
  keep theirs) and `shadow` (a hard offset in px, 0 for none), each optional.
  `ui/dice.json` is the thrown dice's look: `body` and `numbers` for a die that counts, `unkept`
  and `unkeptNumbers` for one that doesn't (each a role of ui/colors.json or "#rrggbb"), `most`
  shown at once (the rest as "+N") and `size` (1 is the game's own).
- `ui/animations/<id>.json` is how an action plays out after its dice land: `seconds`, `impact`
  (when the numbers come out), and `hit`, `miss` and `critical` lists of steps, each `do` (`lunge`,
  `recoil`, `dodge`, `projectile`, `arc`, `ring`, `flash`, `glow`, `line`, `shake`), `at` and
  `seconds`, `on` (`doer` or `target`), `distance` and `size` in squares, `height` (a projectile's
  arc), `color` (a role of ui/colors.json or "#rrggbb"). An action, spell or item may name its set
  with `"animation"`; otherwise one is picked from what it does.
- `ui/credits.json` is new: the game's own credits. `{"entries": [{"name", "kind", "by", "licence",
  "text"}]}`; `kind` is the tab it shows under. The engine and the libraries inside it are not in the
  file: the credits screen asks the engine for them and their licence texts.
- `settings.json` in the game's user folder is the player's settings, with the C++ client's field names
  (`zoomToCursor`, `edgeScroll`, `cameraFollows`, `panSpeed` 200 to 3000, `fullscreen`, `lighting`
  `map`/`off`/`mood`/`rules`, `timeOfDay` `map`/`day`/`dusk`/`night`, `sharedFog`, `reactionPrompts`,
  `lastCreatePackage`, `server` up to 253 characters, `serverKey` and `deviceId` up to 128), `shownHelp`
  (the controls card was shown on the first adventure; F1 brings it back) and `keys`: action id to key names, only for the actions the player moved off their shipped keys.
  A missing or wrong value keeps its default and never stops the game. Fields this port doesn't use yet
  (`controls`, `playerName`, `joinAddress`, `skin`...) are written back as they were read.
- Account sync ("Account sync files" below) works as described there, with the port's folders: saves
  from `saves/`, characters from `characters/` and `characters/graveyard/`, and `sync.json` and
  `sync-backup/` in the user folder itself. Screenshot runs never sync, and files left alone are named
  in Godot's warnings.
- Character files ("Character files" below) are read and written in the same envelope and shape, so a
  library made by the C++ client opens here. The choices inside are FRAMEWORK.md's character choices.
  Until saves (P10) nothing marks a character `away`; its copy goes back to the file when the chapter
  is cleared, when a new adventure starts and when the game closes. The library is `characters/` in
  Godot's user folder.
- Create's Map and Encounters modes ("Drawing a map in Create" and "Placing encounters in Create"
  below) work as described, with these differences. New makes a folder under `create/` in Godot's user
  folder and names its chapter folder after the package (`chapters/new-adventure`), so it never stands
  in for one of the game's own chapters when the package is played over them. The problems list reads
  the package's files over the game's content, the way a playtest plays them, so the game's classes and
  creatures count. Playtest plays the open chapter with nothing saved and Escape comes back. Lights
  take one of six palette colours. `-- --screen create` starts the game in Create, in place of
  `YOREHOLD_CREATE`.
- Dialogue mode ("Writing dialogue in Create") works as described. New conversation is the button
  for New, and Skill check is a toggle rather than a tick box.
- Compendium mode ("Editing definitions in Create") reads the same `create/compendium.json` with the
  same rules. Its list is a data panel: a tab per kind (and All), search, chips for changed entries,
  ones with errors and a chapter's own, and columns that sort. The picked entry's form is where the
  book page would be, with New id, Add and Copy under it. A flag is a yes toggle with Clear beside it.
- Cutscene mode ("Making cutscenes in Create") works as described. New cutscene is the button for New,
  and "The next step waits for it" is a toggle. The preview draws the fade in palette ink with the
  file's alpha, the way play does, whatever colour the file names.
- Story mode ("Planning the story in Create") works as described, with `story.json` in the same format.
- Voice lines ("Voice lines in Create") use the same voice file, `create/voice.json` and
  `voice/vocabulary.txt`. Match to line, Use as line, Look again and the SRT and VTT copies work. This
  port can't listen to a recording yet: it has no speech model, so Import is greyed and says so. When
  it can, recordings are read as WAV (PCM or float); OGG waits for that step too. There is no
  `yorehold-voice` command.
- Account sync is described below as the C++ client has it. It is ported in a later step (see
  ROADMAP.md).

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
classes/fighter.json           a package's own definitions, over its system's
items/longsword.json
creatures/goblin.json
rulesets/yorehold/             the game's rules (built in; see Rulesets)
  ruleset.json
  classes/fighter.json         the system's own classes, items and creatures; a chapter loads
  items/longsword.json         its ruleset folder's first, then the root's, then its own, each
  creatures/goblin.json        replacing entries with the same id
  stealth.json
  conditions/prone.json        one file per condition
  races/elf.json               player options: one file each (see Races, backgrounds and feats)
  backgrounds/sage.json
  feats/tough.json
rulesets/my-rules.json         optional custom rules, as one file or a folder like the above
ui/theme.json                  colors and frame styling
create/compendium.json         the game's own: the forms of Create's Compendium mode
create/voice.json              the game's own: settings for voice lines in Create
voice/wren.hello.wav           optional recorded lines and their words with timings (see Voice lines in Create)
voice/wren.hello.voice.json
voice/vocabulary.txt
story.json                     optional story graph from Create's Story mode; the game doesn't read it
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
- `kind` (optional) categorizes the package: `adventure`, `system` (a rules system), `skin`, `art`, or a content set of one type for one system: `creatures`, `items`, `spells`, `classes`, `feats`, `races`, `backgrounds` or `maps`. The older `ruleset`, `compendium`, `character_class`, `race` and `feat` still read. Empty means the kind is inferred from the structure (if it has chapters, it is an adventure; otherwise definitions). A content set names its system in `ruleset` and holds only its own type, in the folder of that name (`creatures/`), and `pictures/` (plus a LICENSE or README); every entry has to load under that system beside the system's own. It can't change rules, other entries, screens or code (`ContentPackage.CheckSet` says what is wrong). Sets the player installs go in the user folder's `sets/`, one folder each; a game takes those for the system its adventure plays, under the adventure, and leaves the rest out with the reason in the log.
- `id` (optional) is a unique identifier for this package across versions, using lowercase letters, digits, hyphens and underscores.
- `revision` (optional, default 0) is a version number that goes up with each publish.
- `ruleset` (optional) names the rules system this adventure plays, e.g. `"yorehold"` or `"yorehold@1.0"`; the part before the `@` is the ruleset's id. Empty means whatever its chapters name. Every chapter of a package must play that one system (a chapter that doesn't is refused with both ids), since a character is made for one system: a character file carries its `ruleset` id, and an adventure on another system turns it away with the reason and seats its ready-made hero. A character with no `ruleset` (older files) may join any. The version after the `@` isn't checked yet.
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
| Item | `id`, `name`, `slot`, `hands`, `damage` dice, `attackAbility`, `range` (squares a weapon reaches, 1 by default; a bow's more), `weight` (lb), `value` (cp), `quantity`, `magic`, `supplies` (camp supply points per unit), `modifiers` |
| Class | `id`, `name`, `description`, `hitDie`, `bonusHp`, `speed`, `proficiencies`, starting `items` ids |
| Creature | `id`, `name`, `description`, `hp`, `armorClass`, `speed`, fixed `abilities`, `proficiencies`, `items`, `loot`, `token` |

An item's `slot` is where it is worn or held (`mainHand`, `offHand`, `armor`...; none = it can only be carried). Slots ending in `Hand` are held, and `hands` (default 1) is how many of a character's two hands the item takes: a `"hands": 2` weapon can't be held with a shield. Players change gear in the gear panel (I): free between fights, the Interact action's cost on the hero's turn in one. `"magic": true` makes an item count toward the ruleset's `magicItemLimit` (3), carried or worn; weight counts toward the carrying capacity (STR x `carryPerStrength`), past which a hero is slowed and then stopped (`encumberedAt`, `immobileAt`, `encumberedSpeed` in `ruleset.json`).

Modifiers use `stat`, `op` (`add`, `multiply`, `override`, `max`: at least this much, `min`: at most this much; floors and ceilings apply after the rest) and `value`. An item may also list `actions`: granted-only actions of the ruleset it gives while worn or held (or carried, when it has no slot). `token` supports `color`, radius `size` in cells, optional `image` and where it is cut, `focus` and `zoom` (see Faces above). Token image paths resolve in the chapter folder first, then at the content root. Unknown item/class/creature ids, invalid values and missing token images are reported before play. The framework's `Compendium` also serializes these three definitions back to JSON.

## Maps

`map.json` contains named `tiles`, a `legend` mapping one character to each tile name, and `layers` of text `rows`. Spaces mean empty cells. All layers have equal dimensions, derived from the first layer. Empty ground is not walkable. Walls/obstacles can be painted over ground on later layers.

Tiles declare `walkable` and `blocksSight` independently. Sight-blocking tile edges create the vision/lighting walls. `image` is the creator's picture for the tile, a content path (`tiles/grass.png`; png, jpg or webp, square, any size up to 256 px; all of a map's tiles are scaled to the biggest). The game draws no tile pictures of its own: a tile without one is a plain fill in the nearest palette colour to its `color`, or, with no `color`, the usual colour of its `art` (grass, dirt, stone, wood, wall, water, tree). A layer of a `tileMap` map can lie on a painted picture: `"image": {"path": "maps/yard.png", "area": [x, y, width, height]}` in world units, drawn under the tiles of floor 0.

Optional `lighting` picks how light works on the map. `mode` is `off` (everything lit, no darkness drawn), `mood` (lights and darkness are only for looks; the default) or `rules` (heroes only see cells that are lit or within their darkvision). `ambient` is the light level where no lamp reaches (`dark`, `dim` or `bright`), `brightFraction` the part of each light's radius that is bright, `carried` the radius in cells of the light each hero carries (0 = none) and `sight` how far heroes see, in cells. Players can override the mode in Settings. Classes and creatures take `darkvision` in feet.

Optional `ambient` is an RGB/RGBA color. `lights` have `at: [x, y]`, `radius` in cells, `color`, and `flame` (false gives a steady light). Light positions are fractional cell coordinates, so `[4.5, 3.5]` centers a light on cell `[4, 3]`. `markers` map names to integer cells. One cell is 64 world units.

The text rows are an import for maps written by hand. The game keeps every map in the framework's `TileMap` and `Objects` (one `Region` per chapter), and a map can be written that way too: `tiles` as an array (`{"name": "stone", "art": "stone", ...}`, tile id = place in the array + 1) and `tileMap` holding the framework's TileMap JSON (FRAMEWORK.md, "Maps, objects and streamed worlds"; `tileSize` 64) in place of `legend` and `layers`. Only floor 0 layers count for walking and sight for now. `GameMap::toJson` writes a loaded map in this form.

Optional `trace` is a picture to draw rooms over in Create, never shown in play: `{"path": "pictures/p4-1.jpg", "area": [x, y, width, height]}` in squares (it may run off the map). Map mode shows it half see-through over the tiles under BOOK MAP, with Show, Left, Right, Up and Down (half a square), Smaller and Bigger (a twentieth) and Fit (over the whole map); each press is an undo step, a run of them one. An imported chapter gets its book's map here.

Optional `areas` are rectangles of squares that set story flags: `[{"id": "room-mill", "area": [x, y, width, height], "set": ["entered_mill"]}]`. The first time a hero who isn't down stands in one outside a fight, its flags are set; flags set only once, so a trigger waiting on them fires once (a room's passage read out as the party walks in). In a fight they wait until it ends.

`objects` places doors, levers, chests and traps. Each entry is a kit on a cell, `{"kit": "door", "at": [x, y]}`, where any other field changes that copy (`name`, `door`, `lock`, `trap`, `contents`) and `tags` add to the kit's. An entry without `kit` is a whole object in the framework's format, one cell big unless it has an `area`. Kits are one file each in `kits/` at the root or in the chapter's own `kits/` (which wins), named after their id, in the framework's Kit format. The game ships `door`, `locked-door`, `lever`, `chest`, `locked-chest` and `dart-trap`.

- A door with `blocksMovement` and `blocksSight` stops walking and sight until it is opened. `"door": {"locked": true}` needs a key: an item whose id matches the door's `key:<item id>` tag. Add `"lock": {"dc": 15, "skill": "dex"}` and a check can open it too (`dc` 0 or no lock means a key only).
- A lever toggles every door that shares one of its `link:<name>` tags, locked or not.
- Any object with a `flag:<name>` tag sets that story flag the first time it is used.
- A `container` holds `contents`: item ids with counts, and `"coins"` in copper. It is looted like a chapter container. Give it a locked `door` (the lid) and a `lock` and it has to be opened first.
- A `trap` has `detectDc`, `disarmDc`, `detectSkill` (default perception), `disarmSkill` (default dex), `effect` (the effects format, FRAMEWORK.md "Effects", written in place) and `rearms`. A hero within `trapSpotRange` squares (top-level map field, default 2) with a clear view finds it when their passive score reaches `detectDc`. Found traps are drawn and paths go around them. A hero stepping on an armed trap sets it off: its effect runs on them, with its save. Only heroes set traps off.

Heroes use doors, levers, locks and found traps with E when standing beside them: free between fights, the Interact action's cost on the hero's own turn in a fight. A failed disarm by 5 or more sets the trap off. Doors, chests and traps are saved as they were left; saves from before objects load with the map's own.

### Drawing a map in Create

Create > Map edits the `map.json` of the chapter named in the top right (click it for the package's next chapter). Create > New writes a new adventure folder to `create/` beside the save: a manifest, `chapters/chapter-one` and an empty 24 x 16 map with a tile type for each kind of ground (plain fills until the creator gives them pictures). It carries no classes yet, so the validation list says which is missing until they are added.

| Tool | Left button | Right button |
|---|---|---|
| Paint | the picked tile on the picked layer, dragged | erase from that layer |
| Fill box | drag out a box of the picked tile | drag out a box to erase |
| Wall | the picked sight-blocking tile (or the map's first one) on the floor's `walls` layer | erase from the `walls` layer |
| Light | place a light at the cell's centre with the radius, colour and flame on the right, or select one to change it | remove |
| Marker | place the named marker, or move it if the map has it; clicking a marker picks its name | remove |
| Kit | place the picked kit on the cell | remove the object there |

The middle button drags the view, the arrow keys move it and the wheel zooms. The right column picks the floor (-9 to 9) and the layer, adds and removes layers and changes the map's size; cells are kept from the top-left, and a smaller map drops the lights, markers and objects left outside it. The orange lines are the walls the game will build from the tiles and shut doors on floor 0. On a higher floor the one below shows through, dimmed.

Kits offered are the game's own plus the package's `kits/` and the chapter's. Placed kits are written as whole objects, so the saved map does not need the kit files. Undo and redo (Ctrl+Z, Ctrl+Y) step through every edit made since the package was opened, in every chapter; one stroke or one slider drag is one step. Save (Ctrl+S) writes each changed map in the `tiles` array and `tileMap` form described above, keeping the fields the editor has no tool for (`lighting`, `ambient`, `trapSpotRange`). A hand-written map is only rewritten once it has been changed. A `.yore` has to be unpacked to a folder before it can be saved into.

`YOREHOLD_CREATE=<folder or .yore>` starts the game in Create with that package open, and `YOREHOLD_CREATE=new` with a new one.

## Chapters and writer-owned text

`chapter.json` declares `id`, `title`, `map`, `party` and `encounters`, and optionally `ruleset`. A `party` seat is `name`, `class`, `color`, `at` and an optional `image`, a picture in the content (`pictures/p7-1.png`) that the seat's token and cards show in place of its class's portrait. Without `ruleset` the chapter plays by the game's own rules, `rulesets/yorehold` (see Rulesets); leave it out unless the chapter is a test of other rules. It can be `modern` or `classic` (sets built into the framework, kept for tests), a relative JSON path, or a folder with `ruleset.json` in it. Paths resolve in the chapter folder first, then at the content root. Absolute paths and parent traversal are rejected.

Party members have `name`, `class`, `color` and integer `at` cells. Encounter groups have unique `id`, optional starting `text`, and `creatures` with a `creature` id, optional `name` and `at`. Placements must be on walkable, distinct cells. Party size comes from the file, one to four. Seeing one enemy starts its authored encounter group.

Each party member is a seat with a ready-made hero. Starting the adventure with New adventure lets players put a character from their library (or one made there and then) in any seat; it keeps that seat's `at` and `color`, and the ready-made hero only plays the seats nobody filled. Quick start plays the ready-made party. `level` (1 to 20, default 1) is the level the chapter is written for: ready-made heroes start at it, and a character made for a seat gets the XP to level up to it.

An NPC with a `companion` object can join the party. It may have `approval` (where approval starts, default 0), `joinAt` (the least approval they join with, default 0), `leaveAt` (a member whose approval falls to it or below leaves; leave it out and they only leave when sent away) and `flags`, an object of story flag to approval change, counted once the first time the flag is set. `"companion": {}` is someone who joins whenever asked. The older `approvalStart` and `approvalJoinThreshold` fields still load as `approval` and `joinAt`.

Their dialogue does the rest with `do` actions. `recruit` asks the one being talked to along: they join if their approval is high enough and the party has room. `dismiss` sends them away. `approve 5` or `approve -3` changes the approval of the one being talked to, and `approve tam 2` that of any companion the party has met, so any conversation can move it. Approval stays inside the ruleset's range. The log says who approves or disapproves and by how much.

How many can come is the ruleset's `companions` object (FRAMEWORK.md, "Companions"): Yorehold allows 2 companions and a party of 6, so 4 heroes and 2 companions. A companion in the party is on its side in fights, played by the host, goes down and gets back up like a hero and walks behind the heroes. They come along to camp and to the adventure's other chapters, as they are, and can still be talked to there. Sent away in their own chapter they stay where they are as an NPC; elsewhere they stay behind in that chapter. They don't gain XP.

`containers` puts things to open on the map: each has a unique `id`, a `name` (default "Chest"), an `at` cell of its own, and any of `items` (item ids), `coins` (in copper: 10 cp to the sp, 10 sp to the gp) and a `loot` table (FRAMEWORK.md, "Loot tables") rolled when the adventure starts. A hero standing on or beside one opens it with E and takes what they click. Creature files can carry `loot` too: when the party wins a fight, each dead enemy leaves what it carried plus what its table gives in a small sack where it fell. Enemies that got away or gave up leave nothing. In the keep, the storeroom has a chest and goblins carry a few coppers.

A creature placement may set `facing`: the direction it looks until it notices the party, in degrees from -360 to 360, where 0 is east (right on the map), 90 south, 180 west and 270 north. Without it the creature looks toward where the party starts. Facing only matters to sneaking heroes, who are noticed inside the vision cone in front of an enemy and not behind it. In the keep, Gob has `"facing": 180` and watches the door.

An encounter group may set `xp`, a whole number from 0: what each hero gets for winning the fight that group starts. Without it the win gives the chapter's `xpPerVictory`, and `{xp}` in `victoryText` is whichever was given. A group may also carry a `loot` table (FRAMEWORK.md, "Loot tables"). It is rolled once nobody in the group is left to fight, and lies in the sack of the last of them to die, with what that one carried. A group whose last members ran or gave up leaves no group loot.

### Placing encounters in Create

Create > Encounters edits the `encounters` and `xpPerVictory` of the same chapter's `chapter.json`, over that chapter's map as it is drawn in Map mode. Heroes, NPCs and chests show as squares and can't be moved here. Creatures are discs with a ring in their group's colour and a line for where they look: solid if `facing` is set, faint if they only watch for the party.

| Tool | Left button | Right button |
|---|---|---|
| Select | pick a creature, or drag it to another cell | remove the creature there |
| Place | put the creature picked on the right into the group picked on the left; the first one in a chapter starts its first group | remove the creature there |

The left column lists the groups with how many are in each, and adds or removes one. Picking a group shows it on the right: its id, the line shown when its fight starts, the flags set on a win (`set`, separated by commas), an AI profile for everyone in it, its `xp` beside the chapter's `xpPerVictory`, and its loot (coins as dice, then items with a chance and a count that step on each click). "Use N from levels" fills in the proposed XP, 25 for each level of each creature in the group. Picking a creature shows its name, its group (the arrows move it to another), the eight ways it can face or "Party" for no `facing`, its own AI profile and Remove. Del removes the picked creature.

What can be placed are the game's own creatures, AI profiles and items plus the package's and the chapter's. Nobody can be put on a wall, off the map or on a taken cell. An `ai` written by hand as an object shows as "custom" and stays until a profile is picked over it. Fields this mode has no tool for (`surrender`, and everything else in the chapter) are written back as they were, in the order the file had them. A group with nobody in it is listed as a warning and left out of the file, since the game refuses one; `aiChanges` that name a renamed group follow it, and those that name a removed one go with it.

Undo and redo are the same history as Map mode. Save writes `chapter.json` only once something in it has changed, and writes nothing at all while a creature is on a wall or a taken cell or names something the package doesn't have: the status line says which.

### Writing dialogue in Create

Create > Dialogue edits the chapter's conversation files, in the framework's dialogue format. The bar at the top steps through them: every `.json` in the chapter's `dialogue/` folder, the files its `chapter.json` names (NPC `dialogue`, `surrender`, trigger and `winCondition` dialogue, or the package's `dialogue/surrender.json` when the chapter has no `surrender` of its own) and the package's declared `dialogues`. New starts `dialogue/conversation.json` in the chapter, written at the next save. It isn't used until an NPC or trigger in `chapter.json` names it.

The left column is the conversation's id and its nodes; `>` marks the start, and Start here moves it. The middle is the picked node: its id (replies and checks that led to it follow a rename), speaker and line, the whole line as it reads, and its replies with where each goes, what it needs and what it does. Add reply, Up, Down and Remove work on the list, and Del removes the picked reply. A node with no replies is the last line.

The right column is the picked reply, or the node's own flags and actions when none is picked. A reply has an id, its words and either Goes to (a node or "end") or a Skill check with what it rolls, the difficulty and where a pass and a fail go. Ticking Skill check moves Goes to into the pass; unticking moves the pass back. "New line after it" makes a node and points the reply (or the check's empty way) at it in one step. Shown only with flags and Hidden by flags are `require` and `forbid`. The flags it sets, the ones it takes away and What happens are `set`, `clear` and `do`, separated by commas. The editor calls nodes lines and ids names, since that is what a writer sees. Recruit, Dismiss, Approve +1 and Approve -1 add the companion actions; the approve buttons step one `approve N` up or down instead of adding another.

The validation list checks each opened file. Errors stop a save: anything the game would refuse to load, and an action the game would do nothing with, like `approve` without a number or `recruit tam`.

### Voice lines in Create

A node's recording goes in the package's `voice/` folder named after its conversation id and node id: `voice/wren.hello.wav` (or `.ogg`) for node `hello` of conversation `wren`. Importing it writes the words of the line with their timings to `voice/wren.hello.voice.json` beside it. The recording itself is never changed and is what plays. The game only reads the voice file; nothing listens to speech while it runs.

Voice in Dialogue mode's top bar swaps the node editor for the conversation's lines. The left column lists the nodes: `-` has no recording, `to import` has one, `ok` has a voice file with nothing to look at, `check` has one worth a look. The right side is the picked node's line and recording, and:

- Import (Import again) listens to the recording with the speech model that ships with the game and lines the heard words up with the written line. The written line wins: its words keep their spelling and only take the heard timings, so "Carlos" heard for "Kharos" is still written Kharos. A written word with nothing heard for it gets a time between its neighbours and shows as guessed. With no line written yet, what was heard is shown as a suggestion and Use as line makes it the node's line.
- Match to line lines the words up again after the line was changed, without listening again.
- Look again finds a recording added since. Copy SRT and Copy VTT put subtitles for the line on the clipboard; they aren't stored.

Under the buttons is the line along the recording (green heard, red heard unsurely, gold guessed) and every word with its start, end and how sure the model was. Import runs in the background and is one undo step, like Match; both are on the same history as the other modes, and Save writes the voice files with everything else.

`voice/vocabulary.txt` in the package lists names the model should spell your way, one per line (`#` starts a comment). They are given to it as a hint, nothing more. `create/voice.json` in the game's assets holds the cutoff under which a word is flagged (`flagBelow`, 0.6) and the recording types looked for (`extensions`, `["wav", "ogg"]`). Warnings: a recording not imported yet, a voice file with no recording, a line changed since its voice was matched, words heard unsurely and words guessed. A voice file that can't be read is an error.

The voice file:

```json
{
  "format": 1,
  "audio": "wren.hello.wav",
  "model": "base.en",
  "text": "Halt! Who goes there?",
  "words": [
	{"text": "Halt!", "start": 0.12, "end": 0.62, "confidence": 0.94, "matched": true}
  ]
}
```

Times are seconds into the recording. `confidence` is the lowest probability the model gave any part of the word (1 for a guessed one); `matched` is false for a word whose time is a guess. `confidence` and `matched` may be left out (1 and true). A file with a newer `format` is refused with a message saying so. Timings are good to a few tens of milliseconds, enough for subtitles and word highlighting, not for lip-sync.

For many lines at once, `yorehold-voice <package folder>` imports every recording in `voice/` that has no voice file yet (`--again` redoes them all), each matched to the node it is named after. `--model <file>` uses another model file. Warnings don't: a node nothing leads to, a node with no line, a skill the chapter's ruleset doesn't have, an action the game doesn't know, `approve tam 2` for someone who isn't a companion in this chapter, and `recruit`, `dismiss` or a bare `approve` in a file no companion NPC uses. Fields this mode has no tool for are written back as they were. A file is only rewritten once something in it changed, and undo and redo are the same history as the other modes.

### Editing definitions in Create

Create > Compendium edits the package's definition files: items, creatures, classes, AI profiles and kits at the root and in each chapter's own folders, and the spells, races, backgrounds and feats of its ruleset folders (`rulesets/yorehold` and any folder a chapter's `ruleset` names). The left column picks the kind, the next one the entry (a chapter's own shows the chapter, `*` marks one changed since the last save), and the right side is its form.

The forms come from `create/compendium.json` in the game's assets, in the framework's form format (FRAMEWORK.md, "Saves, undo and forms"): a `kinds` list where each kind is a form with two keys of Yorehold's own, `reader` (which of the game's readers checks the file: `item`, `creature`, `class`, `ai`, `kit`, `spell`, `race`, `background` or `feat`) and `ruleset` (true for kinds that live in a ruleset folder). A new field on a form is a line in that file. What the choice fields offer comes from the ruleset (`abilities`, `skills`), the built-in AI profiles, and the ids of the game's and the package's files of each kind, named after the kind's folder (`items`, `feats`...).

Text, number and list fields are boxes (lists as `a, b, c`); a number out of range or text that isn't one is refused and the line under the form says why. Flags are a button, and choices step with the arrows, "(none)" leaving the field out. Nested parts with no fields of their own (a class's `levels`, an item's `use`, a spell's `effects`) are edited as JSON in their box. Fields the form doesn't list are shown under it and written back as they were, in the order the file had them.

Add makes an entry from the form's defaults with the id typed above it (or `new-<kind>`), Copy a copy of the picked one; both go in the kind's folder at the root or in the first ruleset folder. A new package has no ruleset folder, so its spells, races, backgrounds and feats can't be added there. The id is the file name and isn't changed here; nothing is deleted here either.

The validation list checks every entry. Errors stop a save: a field of the wrong type or out of range, a missing required field, an `id` that isn't the file name, and anything the game's reader refuses. A name the lists don't offer (an item another package carries, an AI written as an object) is only a warning. A file is only rewritten once it changed, and undo and redo are the same history as the other modes.

### Making cutscenes in Create

Create > Cutscene edits the chapter's cutscene files, in the framework's cutscene format (its steps are listed with `endings.cleared` below). The bar at the top steps through them: every `.json` in the chapter's `cutscenes/` folder, the files its `chapter.json` plays (`endings.cleared`, `onWipe`, triggers and `winCondition`) and the package's declared `cutscenes`. New starts `cutscenes/cutscene.json` in the chapter (bars in, an empty caption, bars out), written at the next save.

The left column is the steps in order, each with the second it starts at; a step pulled in starts together with the one above it (the one above has "The next step waits for it" off). The buttons under it add a camera, caption, title, pause, fade, bars or event step after the picked one, and move, copy or remove it. Del removes the picked step too.

The middle is a preview of what the players see at one moment: the chapter's map through the camera, with the bars, fade, captions and titles drawn the way the game draws them on a 1280 x 720 screen. It starts from the middle of the map at zoom 1, since in play it starts from wherever the party is. Whole map shows the full map with the camera's frame on it instead. Play (or Space) runs it, `|<` goes back to the start, and the timeline under it shows when each step runs: click a step to pick it, or click or drag on the bar to jump. With a camera step picked, a click on the preview aims it there.

Under the timeline is when this file plays. Chapter cleared, Party wiped and Chapter won set `endings.cleared`, `onWipe.cutscene` and `winCondition.cutscene` to this file (Chapter won only once the chapter has a `winCondition`). Add trigger adds a trigger that plays it; pick one to change its id and its flags (empty plays it when the chapter starts). Remove trigger takes it away, or only its cutscene when it also opens a conversation. These are written into `chapter.json` along with whatever Encounters mode changed there, and the rest of the file is left as it was.

The right column is the picked step: its seconds, whether the next step waits for it, and its own fields. Camera has where it looks in world units, the zoom (0 keeps it) and the ease. Caption and title have the line, and a button to turn one into the other. Fade has the colour as r, g, b and how solid (0 fades back in), with To black and Back in. Bars go in or out. An event has a name; the game knows `finished`.

The validation list checks each opened file and the chapter's triggers. Errors stop a save: anything the game would refuse to load, a trigger id that isn't a-z, 0-9, - and _, and a cutscene the chapter names that isn't there. Warnings don't: no steps, a caption or title with no line or no time, an ease the game doesn't know, a camera aimed outside the map, an event the game does nothing with, and two triggers with one id. Fields this mode has no tool for are written back as they were. A file is only rewritten once something in it changed, and undo and redo are the same history as the other modes.

### Planning the story in Create

Create > Story is the adventure as a graph: scenes, fights, conversations, quests and endings, with links for how the story gets from one to the next and notes for the writer. It is kept in `story.json` at the package root. The game doesn't play it; a node points at the chapter, encounter group, conversation file, quest or ending cutscene that does.

```json
{
  "format": 1,
  "nodes": [
	{"id": "keep", "kind": "scene", "title": "The keep", "text": "notes", "at": [0, 0], "chapter": "chapters/goblin-keep"},
	{"id": "entry-hall", "kind": "encounter", "title": "entry-hall", "at": [250, 0], "chapter": "chapters/goblin-keep", "group": "entry-hall", "xp": 50},
	{"id": "wren", "kind": "dialogue", "title": "wren", "at": [250, 74], "chapter": "chapters/goblin-keep", "dialogue": "chapters/goblin-keep/dialogue/wren.json"},
	{"id": "rescue", "kind": "quest", "title": "Find Tobb", "at": [250, 148], "quest": "rescue", "steps": ["entry-hall"]},
	{"id": "cave", "kind": "scene", "title": "The cave", "at": [500, 0], "map": {"width": 32, "height": 20}},
	{"id": "end", "kind": "ending", "title": "Ending", "at": [250, 222], "chapter": "chapters/goblin-keep", "cutscene": "chapters/goblin-keep/ending.json"}
  ],
  "links": [{"from": "keep", "to": "entry-hall", "text": "They go in", "when": ["gate-open"]}],
  "dismissed": ["ending|chapters/goblin-keep"]
}
```

A node has a unique `id` (a-z, 0-9, - and _), a `kind` (`scene`, `encounter`, `dialogue`, `quest` or `ending`), a `title`, the writer's `text` and where it sits, `at`. `chapter` is a chapter folder; empty means it isn't made yet. What it points at is named after its kind: `group`, `dialogue`, `quest` or `cutscene`. Fights and quests can have `xp`, quests `steps`, and a scene with no chapter yet the `map` it should get. A link has `from` and `to` node ids, optional `text` and `when` flags. `dismissed` lists suggestions turned down. Fields Create has no tool for are kept. A file with a higher `format` than the game knows doesn't open.

The left column adds nodes of each kind, lists them, and under that lists the suggestions with Take and Not this, or Take all. The middle is the graph: drag a node to move it, drag the background (or with the right button) to look around, click a link to pick it. Link to... then a click on another node links the picked one to it; Esc stops. Del removes the picked node or link. The right column is the picked node (kind, id, title, notes, chapter, what it points at, XP, steps, map size, its links) or link (where it goes, its words and flags).

Suggestions come from the package and the graph:
- a scene for each chapter, and a node for each encounter group, conversation file, quest and chapter ending the graph doesn't show yet, linked from that chapter's scene. A new scene is linked to the others the way `adventure.json` travels between them
- XP for a fight, 25 for each level of each creature in its group, as in Encounters mode
- steps for a quest, from the titles of the nodes it links to
- a map for a scene with no chapter: 24 by 16, and 8 by 4 more for each fight linked from it

Taking one is one undo step, and Take all is one too. One turned down is saved in `dismissed` and doesn't come back until Bring back. Taking a map suggestion only records the size on the scene; making the chapter is still done by hand.

The validation list warns about a node with no title, a chapter or a group, file, quest or cutscene the package doesn't have, a node with no links, an ending the story goes on from, and a link between two scenes that `adventure.json` has no transition for. None of them stops a save. `story.json` is only written once something in it changed, and undo and redo are the same history as the other modes.

## Story import files

Importing a book (ROADMAP.md, "Story import") leaves its working files in the package's `import/` folder. The game never reads them in play; they are there so a stage can be run again without the book.

`import/source.json` is the book as read, before anything is made of it:

```json
{
  "format": "yorehold.source",
  "version": 1,
  "title": "The Old Mill",
  "file": "mill.pdf",
  "bodySize": 10,
  "pages": [
	{"number": 1, "width": 600, "height": 800, "blocks": [
	  {"kind": "heading", "text": "THE OLD MILL", "at": [50, 46, 137, 14], "font": "Helvetica", "size": 20},
	  {"kind": "text", "box": "shaded", "text": "The wheel turns though the race is dry.", "at": [50, 218, 172, 9], "font": "Helvetica", "size": 10}
	]}
  ],
  "pictures": [{"file": "pictures/p1-1.png", "page": 1, "at": [330, 220, 80, 100], "width": 80, "height": 100}],
  "skipped": []
}
```

- It reads `.pdf`, `.txt` and `.md`. A scanned PDF has no text and says so in `skipped`.
- `at` is `[x, y, width, height]` in points from the page's top left. Blocks are in reading order: each column whole before the one to its right, and a block across the columns ends the ones above it. Columns are found from where the paragraphs are, so they need not meet in the middle of the page.
- `kind` is `heading` for a short line set at least 1.15 times `bodySize` (the size most of the book's letters are), else `text`. A heading right on top of its paragraph is still its own block. Words broken over two lines are joined.
- `box` is `shaded` for text on a filled rectangle and `framed` for text inside a ruled one; books set passages to read out and notes for whoever runs the game apart this way. Left out when there is none.
- Cells side by side on one row of a column are one block, joined by spaces: `Attack: +3 bonus Climb: +2 bonus`, or a name and the boxes to tick after it.
- `pictures` are saved to `import/pictures/` named by page and order. A JPEG is kept byte for byte, anything else is written as PNG. A picture covering more than 0.8 of its page is the page's paper and is left out, and so is anything under 64 pixels across. `width` and `height` are in pixels.
- In a `.txt` or `.md`, paragraphs are split at blank lines and pages at form feeds; a `#` line in a `.md` and a short line all in capitals are headings. Blocks carry no `at`, `font` or `size`.

`import/outline.json` is the book cut down to the game's own data, the file the builder turns
into a package (`rules/import/Outline.cs`; the tests' `SampleOutline.cs` is a whole example):

```json
{"format": "yorehold.outline", "version": 1, "title": "The Old Mill", "system": "",
 "entries": [{"id": "marn", "kind": "hero", "data": {"name": "Marn", "class": "fighter"},
			  "from": {"page": 1, "quote": "MARN"}, "picture": "pictures/p1-1.png"}]}
```

- Each entry has an `id` (a-z, 0-9, - and _, one per outline), a `kind`, its `data`, `from`
  (`{"page", "quote"}`: the book's words it rests on, or `"invented"`), an optional `picture` from
  `import/pictures/` and an optional `chapter` (a chapter entry's id; the first chapter when left out).
- `system` names a table in `import/systems/` that turns the book's numbers into the game's; empty
  when the book has none or already uses the game's.
- Kinds whose `data` is a content file, read by the game's own reader: `creature` and `item` (their
  files, see Definitions), `dialogue` (a dialogue file), `quest` (one quest of a quests file). Their
  `id` may be left out; it is the entry's.
- Kinds that are part of an adventure or chapter: `adventure` (`title`, `description`, `level`; at
  most one), `chapter` (`title`, `intro`, `level`, `completeWhen`, `mapPicture`: the book's map of it), `hero` (a party seat: `name`,
  `class`, `race`, `color`, `image`, `description`), `npc` (`name`, `creature`, `place`, `dialogue`
  as an entry id, `merchant`, `color`), `encounter` (`place`, `creatures`: `[{"creature", "name",
  "count"}]`, `text`, `set`), `container` (`place`, `name`, `items`, `coins`, `locked`, `key`,
  `check`), `trigger` (`when`, `dialogue` as an entry id, `place`).
- The outline's own kinds, which have no file: `place` (a room or area: `name`, `label` (the number
  the book's map gives it), `size` `[w, h]` in squares, `readAloud` (passages to read out, word for
  word), `description`, `dark`, `outdoors`, `mapAt`: where its number is on the chapter's map
  picture, `[x, y]` as shares of its width and height), `link` (`from` and `to` places, `way`: `open`, `door`,
  `locked`, `secret`, `climb` or `jump`, `key`, `check` `{"skill", "difficulty"}`; a locked one needs
  a key or a check) and `note` (`text`, `place`, `why`: what the game can't play yet).
- Places, dialogue and chapters named by an entry must be entries. Creatures, items and classes
  may be the game's own; the builder checks those against the game's content before it writes.
- `assets/import/schemas.json` holds the JSON schema of each kind's `data`, which the story model is
  asked to answer in.

Building (`OutlineBuilder`) writes the package around the import folder: `content.json`,
`adventure.json` (chapters, and a transition for every link between places in two chapters),
`creatures/` and `items/` (a creature or item the game already has by that name is the game's,
unless the book gives it a picture), `pictures/` (the book's pictures in use), each chapter's
`chapter.json`, `map.json`, `dialogue/` and `quests.json`, and `story.json`. The map is the places
as rooms side by side with one wall between, stone floor and walls inside, grass and trees
outdoors; a link is a gap in that wall, a `door` kit, or a `locked-door` kit with `key:<item>` and
the link's check as its lock. The party starts in the first place of the chapter, whose passage
to read out joins the chapter's `intro`; every other room's passage is read the first time a hero
steps in (an area setting `entered_<place>`, a trigger on it and a one-line conversation
`dialogue/read-<place>.json`, with no speaker). Foes stand on the far side of their room. An NPC with a
picture gets a creature of its own carrying it. A locked chest is a `locked-chest` map object. When the chapter has a `mapPicture`, it
is taken as 60 squares across: a first place with `mapAt` sits where its number is, and a place
no link reaches sits where its number is too (in the nearest free spot), joined to the nearest
room by an open way; the picture goes in the map's `trace`, lined up with them.
Every chapter gets `xpPerVictory` 50. `import/report.json` lists what didn't go in as written:
`{"format": "yorehold.import-report", "version": 1, "lines": [{"entry", "text"}], "rooms": {"<place>": {"chapter", "at": [x, y, w, h]}}}`;
`rooms` is where each place's floor was put. The same outline always gives the same files.

A picture of a hero, creature or person standing on plain light paper (most of its edge one
colour, lighter than 170 of 255) is written as a PNG with that paper see-through, flooded in from
the edge; paper-coloured parts inside the figure stay. `ClearPaper = false` on the builder turns it off.

`import/systems/<name>.json` turns one source system's numbers into the game's; the outline's
`system` names it and the builder applies it before writing:

```json
{"format": "yorehold.system", "version": 1, "name": "D&D 3.0 and 3.5",
 "skills": {"open-lock": "dex", "climb": "athletics"}, "otherwise": "perception",
 "difficulty": {"scale": 1, "add": 0}, "hitPoints": {"scale": 1, "add": 0}, "armorClass": {"scale": 1, "add": 0}}
```

`skills` maps the book's skill names (written lower case with `-`: "Move Silently" is
`move-silently`) to the game's skills or abilities; a name the game already has is kept, and one
neither knows becomes `otherwise`, with a line in the report. Checks on links, chests and dialogue
replies, and creatures' hit points and armour class, are scaled and added to. The game ships
`dnd-3.0` and `dnd-5e`.

With a story model (`StoryReader`), the book goes in chunks of whole pages (about 14,000
characters each) with the outline so far, and the model answers with entries in the outline's
own shape. Every entry is checked by the outline's reader; the ones that fail go back once with
what is wrong, and the ones that fail again are dropped and listed. A `from.quote` that isn't on
that page of the book (give or take a page, ignoring punctuation) makes the entry invented. The
model is anything that takes the usual chat request, `POST <address>/v1/chat/completions` with
`model`, `messages` and the answer's JSON schema as `response_format`; OpenRouter and Ollama both
do. What it is told is three files in `import/`: `prompt-system.txt` (the rules and each kind's
schema; `{{kinds}}`, `{{schemas}}`, `{{gameCreatures}}`, `{{gameItems}}` and `{{gameClasses}}` are
filled in), `prompt-chunk.txt` (`{{outline}}`, `{{pictures}}`, `{{pages}}`, `{{title}}`,
`{{text}}`) and `prompt-fix.txt` (`{{outline}}`, `{{errors}}`). In the book's text a line starting
`#` is a heading, `>` a passage boxed to be read out and `|` a boxed note.

After the model's pass, a hero, person or creature with no picture gets the picture captioned with
its name (the whole name, or its first word: JEZER for "Jezer the Ogre"), if no other entry has it.

Create > Import (`Import a book` on Create's list) reads a `.pdf`, `.txt` or `.md` into a new folder
in the create folder, named after the book, with everything in its `import/`. The story model is
the settings file's `storyModel` (a chat address, like `http://127.0.0.1:11434` for Ollama) and
`storyModelName`, or `YOREHOLD_IMPORT_MODEL` and `YOREHOLD_IMPORT_MODEL_NAME`; with none, only the
layout's draft is made. The review lists the entries by kind with chips for From the book,
Invented and Dropped, and shows each as a page: its quote and page, its picture, its passages and
its fields. Drop and Keep mark entries (kept in `import/dropped.json`, so a review can be closed
and opened again from the list, where an import not yet built shows as `import`); Build writes
the package without the dropped entries and what stands in them (a fight in a dropped room), and
opens it. `-- --import <book>` reads, builds and opens a book with no review; `check.ps1 -Shot
x.png -Import <book>` does that in a screenshot run.

Without a story model, the layout of the book alone gives a first outline (`BookLayout.Draft`):
the adventure and one chapter named for the book, a place for every numbered heading
(`1: OUTSIDE THE CAVES`, `4. Crossing the Crevice`) with the shaded passages after it to read out
and its framed boxes as notes, and a note with the map picture: the picture with the most place
numbers printed on it, and where each number sits on it. `BookLayout.PictureNames` gives each
picture the short heading set right under it in its column (or over it), the name of who it shows.

`BookCast.Add` then reads, still with no model (before the model's pass when there is one):
- a hero for a picture's name followed by a line of a race and a class the game has ("Human
  Fighter"), with that picture and the paragraph after as its description;
- the shaded passages before the first numbered place as the chapter's `intro`;
- in each numbered place's part of the book, a framed box listing foes with hit point boxes
  (`Orc #1: o o o o o o`, a run-on row of boxes adding to the one before): a creature per kind
  (the last word of the name before any "with ..."), its hit points the most boxes, its armour
  class the box's "N or better" for it (else for all, else 12), level hit points / 6, and a weapon
  item (the weapon the place's text has it hold, else the nearest named; damage the box's "roll N
  die/dice ... damage" as Nd6). A fight in the place holds them, counted, with a name the text
  gives ("Jezer the Ogre");
- bullet questions ("•Who are you?") each followed by a quoted answer: a conversation spoken by the
  person the text names ("The old man's name is Jeffries", else "Stranger"), opening on the first
  answer, every other question a reply, and "Farewell." to end; that person stands in the place;
- a heading "Search / Look in / Open / Check / Examine the X" whose text gives gold ("50 gold
  pieces", not "sold for 50 gold") or known things (rope, crossbow, greatsword, statue, necklace...,
  potions by colour, a healing one being the game's `healing-potion`): something to open named X
  (or the bag or sack the text names), or, when X is a foe of that place, coins and things it
  carries;
- "go to Area 3" (or proceed, continue, head) as a link to that place: locked with a check when
  the text since the last heading says locked (Dex to pick a lock, else Athletics; the "N or
  better" as its difficulty, else 15), a jump past a chasm or crevice, a door when it names one.

When the builder lays rooms out from links, a room whose number is on the book's map goes where
the map draws it rather than beside the room it links from, and the way is cut through as a
corridor. A room's passage is narration, with no speaker and no faces.

### How much of the book got in

Every import is scored when it is read and again when it is built (`ImportScore`), into
`import/score.json`: `{"format": "yorehold.import-score", "version": 1, "book", "modelRun", "built",
"key", "overall", "parts": [{"id", "name", "found", "of", "missing", "facts"}]}`. Each part counts
something the book itself shows against what is in the kept outline or the built package:

- `pictures`: the book's pictures copied into the package (before the build: named by an entry).
- `words`: the book's paragraphs of 8 words or more, by the share of their runs of three words
  found in what the game shows or says (notes don't count). The book's rules and credits are in it
  too, so it never reaches the whole; its `missing` is the largest paragraphs left out.
- `places`: numbered headings with a place of that label. `readout`: shaded passages found in the game.
- `ways`: places with a link given (the rest are joined by the builder). `shape`: for each two
  places numbered on the book's map, whether their rooms lie left/right and over/under the way the
  numbers do (built only). `spoken`: paragraphs opening with a quotation mark found in a conversation.
- `cast`: counts only (fights, creatures, heroes, people, items, chests, quests).
- `key`: the lines of the book's answer key the outline meets, when it has one.

`overall` is the mean of the counted parts. The review shows the score on its first page and a
build prints it to the log. Answer keys and `history.jsonl` (one line per build: when, book,
package, model run, overall, each part's found and of) are in `user://import-scores/`. A key is
`<book-slug>.key.json`, written by hand by someone who read the book:

```json
{"format": "yorehold.import-key", "version": 1, "book": "Caves of Shadow",
 "notPictures": ["pictures/p2-1.jpg"],
 "expect": [{"place": "3"}, {"way": ["5", "6"], "how": "locked"},
			{"fight": "3", "creatures": {"orc": 1, "rat": 1}}, {"creature": "ogre|jezer"},
			{"hero": "Lidda", "class": "rogue", "picture": true},
			{"person": "Jeffries", "place": "1", "talks": true},
			{"says": "My name is Jeffries", "who": "Jeffries"}, {"item": "crossbow"},
			{"chest": "6", "holds": ["crossbow"], "coins": 50}, {"quest": "orcs|ogre"}]}
```

A line's first field is its kind; places are named by the book's labels; `a|b` takes either name,
matched as whole words in the id, name or the foe's own name. A chest's `coins` are gold, as books
give them (the game counts copper, 100 to the gold). `notPictures` aren't counted in
`pictures`. Setting `YOREHOLD_SCORE_IMPORT` (with `_PACKAGE` and `_KEY`) makes the tests score an
import folder into its `score.txt`.

With a key, the score also lists `extras`: what the outline has that the key doesn't (a fight
where the key has none, a creature, hero, person or quest it doesn't name, something to open where
it lists nothing), only for kinds the key lists at all. `judged` is the number to steer changes
by: 70% the key's lines met marked down for extras (the harmonic mean of met/lines and
met/(met + extras)), 30% the other parts' mean; without a key it is `overall`. The book's room
`shape` part also counts, built, each place whose walls are the book's map's own (`drawn` in
report.json's `rooms`, with the `spot` square its number is on).

**Comparison runs** (`ImportBench`): a folder with `books/` (each book with its
`<slug>.key.json` beside it) and `bench.json` listing versions of the import:

```json
{"format": "yorehold.import-bench",
 "versions": [{"name": "layout", "cast": false, "walls": false}, {"name": "full"},
			  {"name": "local", "model": "http://127.0.0.1:11434", "modelName": "qwen"}]}
```

`cast` (BookCast) and `walls` (the map's walls) default to on; `model` is a chat address, none
for layout only. `check.ps1 -Bench <folder> [-Label name]` builds, runs every book through every
version into `runs/<label>/<book>/<version>/`, and prints `runs/<label>/report.txt`: each
version's mean judged score with its change from the last run and the best before, then per book
the parts and the key's lines won and lost. `history.jsonl` keeps every result; a label run again
replaces its earlier run. Without `-Label` the run is named after the commit.

**The book's map** (`BookMap`): when every place of a chapter has its number on the map picture,
the picture is read into squares (its drawn grid, else 40 across), floor is what looks like the
ground under the numbers, small white or grey boxes are doors, and each floor square goes to the
place whose number is the shortest walk away (a door ends a room). The chapter's map is that floor
with walls round it; a link's door, lock or check goes on the door the map draws between its two
places. A map that can't be read falls back to boxes, and the report says why.

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

Between fights the party can make camp from wherever it is (the Make camp button), unless the chapter's `chapter.json` says `"camp": false`. Camp is a chapter of its own: `camp` in `adventure.json` names the folder (it must load, and can't be one of the adventure's chapters); without it every chapter uses the shared `chapters/camp`, a clearing. A camp chapter needs a `party` list like any chapter but seats as many heroes as come, the extra ones on the first seat's square; they arrive on its `entry` marker if the map has one. A camp chapter that names no `ruleset` plays the adventure's system, and party classes that system lacks stand in as its first class, since the real party replaces them. Camp is drawn and played like any map, so it can have NPCs and dialogue.

The chapter the party left is kept as it was: Leave camp puts them back where they stood, with the same doors open, enemies down and chests taken. Heroes, flags, rests taken and the stash go both ways. A save made at camp loads at camp and still knows the way back.

At camp only:

- The rests the ruleset marks `campOnly` (the long rest). Its `supplyCost` comes from supplies in the stash first, then from what the heroes who aren't dead carry, in party order and cheapest first. Whole units are used, so a cost that doesn't divide evenly takes a little more.
- The stash: one shared list of items. The selected hero puts a whole entry in or takes one out (Stash button). Worn items come off first; the magic item limit still counts.
- Revival: a dead hero comes back for the ruleset's `revivePrice`, paid by the selected hero, with `reviveHp`. Rests never bring back the dead.

An item's `supplies` is how many supply points each unit is worth (0 = not food). The shipped `supplies` item is worth 10 and costs 5 sp, so a long rest takes four. The keep's storeroom chest holds four.

## Rulesets

RULES_LANGUAGE.md has the whole rules language on one page; this section is the long form.

The game's rules are a folder, `rulesets/yorehold/`, and every number the rules use is in it. Changing a number there changes the game; nothing in the code repeats it. `ruleset.json` is the framework's ruleset format:

| Field | Meaning |
|---|---|
| `version`, `id`, `name` | Format version (1), the ruleset's id and its display name. |
| `abilities` | List of `id` and `name`. The first is the one carrying uses. |
| `skills` | List of `id`, `name` and the `ability` each uses. |
| `modifierTable` | `d20` ((score - 10) / 2, rounded down) or `classic`. |
| `scoreMin`, `scoreMax` | Bounds on ability scores. |
| `baseArmorClass`, `armorClassAbility` | Unarmoured AC, and the ability added to AC (empty = none). |
| `roles` | The ids the game's own procedures use, so a system can name things its own way. `hpAbility` (added to HP per level and per hit die), `attackAbility` (for weapons that name none), `carryAbility` (empty = the first ability), `initiative`, `perception`, `stealth` and `thievery` (an ability or a skill; thievery is for locks and traps that name none), the `hidden`, `downed` and `dead` conditions, the `strike`, `stride`, `endTurn` and `interact` actions, `slotPrefix` (spell slot resources; `spellcasting.json` must agree) and `focus` (the focus resource). Every field is optional; an empty name means the system has none. Without `roles`, the older `initiativeAbility` and `hitDieAbility` still count, and a default the system lacks falls back (perception to `wis`, stealth to `dex`) or to none. |
| `checks` | How the system's rolls are made and read; see "Checks and formulas" below. Left out, the game's own: a d20, a 1 misses and a 20 is a critical hit, checks and saves pass on the DC or more, critical damage rolls its dice twice. |
| `passiveBase` | A passive score, such as the passive Perception sneaking is rolled against, is this plus the modifier. |
| `proficiencyByLevel` | Legacy proficiency bonus at each level, level 1 first; used when there are no ranks. |
| `proficiencyRanks` | Optional list of ranks with unique `id`, display `name`, `bonus` (0 to 100) and `addsLevel`. |
| `proficientRank`, `untrainedRank` | Rank IDs used when a sheet has no explicit choice, according to its old proficiency list. |
| `baseDc` | Base for a character's action DC before ability and proficiency, 10 by default. |
| `death` | Optional death-save rules; absent/disabled keeps older play (see Downed and death). With `track`, a dying value replaces death saves: `start` (from `wounded`, `critical`) when the creature drops, `damage` (from `dying`, `wounded`, `critical`) added by a hit while down, each turn a roll of the check kind `roll` (default `check`) against `dc` (from `dying`, `wounded`) moving it by `change` (outcome id to a number: `{"criticalFailure": 2, "failure": 1, "success": -1, "criticalSuccess": -2}`), death when `dead` holds (`"dying >= 4"`), and at 0 it is stable and `wounded` goes up by `woundedStep` (1); the rests listed in `woundedClearedBy` (`["rest"]`) take it back to 0. The dying condition carries the value. |
| `xpForLevel` | Total XP needed for each level, level 2 first. |
| `actionsPerTurn`, `bonusActions`, `strikeCostsHands` | Actions in a turn (1 to 10), whether there is a bonus action as well, and whether a Strike costs one action per hand the weapon needs. |
| `trapFumble` | A formula that says a failed disarm sets the trap off, from `margin` (the total less the DC) and `outcome` (the check's outcome by place, 0 = worst). `"margin <= -5"` when left out; PF2e's is `"outcome == 0"`. Disarming, lock picking and dialogue checks pass the way the system's `check` kind reads them. |
| `freeMove`, `attackPenalty` | Whether a turn starts with its speed to move for free (true; false: moving takes an action, the Stride/Dash action), and a formula added to each attack from `attacks`, the attacks made earlier that turn (`"attacks >= 2 ? -10 : attacks * -5"`; none by default). A condition adds or takes actions with a modifier on the `actions` stat (`{"stat": "actions", "op": "add", "value": -1}`). An action's `cost` may be `"bonus"`: it takes the bonus action instead. |
| `turnWords` | What the system calls the parts of a turn, for costs on the hotbar, the spell book and refusals: `action`, `actions`, `bonus`, `reaction` and `free` (default "action", "actions", "bonus action", "reaction", "free"). |
| `fields` | Words a character is made of beside its numbers: `[{"id": "high-concept", "name": "High concept", "hint": "Who they are in a phrase"}, {"id": "aspects", "name": "Aspects", "count": 3}]`. `count` is how many lines (default 1, up to 20), `required` makes creation wait for one, `hint` is the empty box's text. Creation asks for them where a step lists the part `fields`; the sheet shows them in its `fields` section; a character file keeps them as `"fields": {"aspects": ["...", "..."]}`; formulas read `field.<id>` as how many lines are written. |
| `advancement` | How characters go up levels: `xp` (the default: fights give experience, `xpForLevel` turns it into levels), `milestone` (fights give none; going on to the next chapter raises every living hero one level, as far as `xpForLevel` goes) or `none`. Fate uses milestones whose class rows each give one +1 boost (a significant milestone), and its sheet shows no level section, so no level is named. |
| `turnOrder` | Who acts when: `mode` `initiative` (one order, the default) or `sides` (one whole side, then the other, each in initiative order; with `sharedTurns` the side picks who goes), `first` (`initiative`: the side with the best initiative, `party` or `foes`), and `roll` (false: order by the initiative modifier alone, as Fate orders by Quick). Reinforcements join their own side. `picked: true` (Fate): whoever just acted names who goes next among those yet to act this round; without a pick an ally of theirs goes next, and a hero's player picks by clicking someone in the turn order during the hero's turn. |
| `defences` | Defences of the system's own beside `ac` (the armour class from the `ac` stat, armour and the `armorClass` formula, which every system has): `[{"id": "defend", "name": "Defend", "value": "max(mod.quick, mod.careful)"}]`, each value a formula over the sheet's names and `ac`. A roll kind in `checks` names the defence its attacks are rolled against with `"defence"` (default `ac`), and an attack step may name another with `"against"` (`{"do": "roll", "kind": "attack", "against": "reflex", ...}`). Flanking and cover add to whichever is used; the sheet and the HUD show the attack roll's defence by its name. |
| `tracks` | What harm uses up, in the order damage runs through it; none = plain HP. Each: `id`, `name`, `max` (a formula over the creature's sheet names, like `"stat.stress > 0 ? stat.stress : 3"`; creature files set such stats in `stats`), `absorbs` (damage one point takes, default 1; a consequence slot is one point that takes 2, 4 or 6 and is used up whole), `heals` (ordinary healing refills it, default true), `shared` (damage aimed at another track still reaches it) and `clears` (`fightEnd` or rest ids that fill it again). Damage no track takes puts the creature down; a hit every track takes leaves it standing. HP becomes what the tracks can still take and max HP what they could when full, so bars and the AI read them as before. Fate: 3 stress, then mild, moderate and severe consequences. A damage step may aim at one track (`"track": "mental"`): it goes to that track and the shared ones only, as Fate Core keeps physical and mental stress apart with consequences shared. An effect's `resource` step that names a track restores or spends its points (`{"do": "resource", "id": "mild", "op": "restore"}`); that alone never puts anyone down or gets them up. |
| `sheet` | What a character sheet shows: `sections`, some of `vitals` (AC, HP, speed), `tracks` (each track's points), `level` (hit die, XP), `scores`, `saves` (the system's own saves, else the trained abilities), `skills`, `defences` (resistances, weaknesses, immunities), `weapon`, `feats`, `uses`, `conditions` and `carrying`, in the order drawn (default all of them); `names` renames the labels `ac`, `hp`, `speed`, `hitDie`, `xp`, `saves`, `skills`, `defences`, `weapon`, `feats`, `uses`, `conditions`, `carrying` (Fate: `{"hp": "Stress", "feats": "Stunts"}`). |
| `sharedTurns` | Consecutive allies share an active initiative block. True for Yorehold; absent means sequential turns for older rulesets. |
| `feetPerSquare` | Size of a map square. |
| `carryPerStrength` | Pounds carried per point of the first ability. |
| `magicItemLimit` | Magic items one character may carry (0 = no limit). Held here until inventory rules use it. |
| `rests` | Each has `id`, `name`, `perAdventure` (uses, 0 = unlimited), a `recovery` and optional `restores`, the resources it refills (`"slots-*"` for every spell slot). Optional `supplyCost` (supply points it uses up), `campOnly` (only at camp) and `resets` (rest ids whose uses come back). The first is the one R takes. The long rest restores spell slots and is taken at camp for 40 supplies, as often as supplies last, and gives back the 2 short rests; both rests restore `focus`. |
| `afterVictory`, `reviveAfterVictory` | A `recovery` for the winners of a fight, and the HP downed winners get back up with (0 = they stay down). |
| `revivePrice`, `reviveHp` | What bringing a dead hero back at camp costs, in copper (0 = it can't be bought; 20000 = 200 gp), and the HP they come back with (0 = full; 1 here). |
| `defaultHitDie`, `hitDieByClass` | Sides of the hit die, and by class name. The ability added per die is `roles.hpAbility`. |
| `conditions` | Optional list of conditions written inline; a ruleset folder keeps them as files instead (see Conditions). |

### Checks and formulas

`checks` is an object of roll kinds. The game rolls `attack`, `check`, `save` and `initiative`; a kind the system leaves out resolves like `check`. Each kind has:

- `dice`, `advantage`, `disadvantage`: the dice rolled (`"1d20"`, `"3d6"`, `"2d20kh1"`). Giving only `dice` uses it for all three. Dice anywhere in the game may also explode (`"1d6!"`: a die on its top face rolls again and adds, at most 20 times), count successes (`"6d6s5"`: the number of dice showing 5 or more) or be Fate dice (`"4dF"`: each -1, 0 or +1).
- `outcomes`: 2 to 12 ways it can come out, worst first. Each has an `id`, a `name` for the log, `passes` (what "hit", "success" and a held save mean to the rest of the game), `critical` (critical damage goes with it) and `damage`, the share of an effect's damage a save with this outcome lets through under `"onSave": "half"` (0.5 by default for a save that passes, else 1; a critical failure can be 2 and a critical success 0).
- `opposed` (false): the one it is rolled against rolls too. An attack's DC is then the defender's roll of the same dice plus their AC; a check `against` something meets the other side's roll plus their modifier rather than their passive score. Fate's active defence, or contests. The log shows "Gik defends: ..." and the odds count both rolls.
- `degree`: a formula giving the outcome's place in the list (0 = the first), from `total`, `die` (the dice without the modifier), `modifier` and `dc`.

`criticalDamage` is `"doubleDice"` (the dice are rolled twice) or a formula from `dice` (what the damage dice came to), `flat` (the rest) and `max` (the most the dice could show): `"(dice + flat) * 2"`, `"max + dice + flat"`.

A step's `when` may name any outcome id of the system besides the game's own words (`hit`, `miss`, `crit`, `success`, `failure`, `saveFailed`, `saveSucceeded`), which keep their meaning everywhere: `hit` is an attack that passes, `crit` one that is critical.

A formula is arithmetic: numbers, the names listed for its place, `+ - * / %`, comparisons, `&& || !`, `a ? b : c`, and `min`, `max`, `floor`, `ceil`, `round`, `abs`, `clamp(value, low, high)`. True is 1 and false 0; a place that needs a whole number rounds down. It can't read files or run for long, so a system from anywhere is safe to load. Four degrees of success, ten over or under moving a step:

```json
"attack": {
  "outcomes": [{"id": "criticalFailure"}, {"id": "failure"}, {"id": "success", "passes": true},
               {"id": "criticalSuccess", "passes": true, "critical": true}],
  "degree": "clamp((total >= dc + 10 ? 3 : total >= dc ? 2 : total > dc - 10 ? 1 : 0) + (die == 20 ? 1 : 0) - (die == 1 ? 1 : 0), 0, 3)"
}
```

`formulas` says how a creature's own numbers are counted, for a system that counts them differently; one left out is counted as the game always has (shown in brackets). Each is handed the names listed and may also read `level`, `mod.<ability>`, `score.<ability>`, `stat.<name>` (any number on the sheet, with its modifiers) and `prof.<target>` (the proficiency bonus for a skill, a save, `weapons`, `armor` or `dc`).

| Formula | Names | The game's own |
|---|---|---|
| `abilityModifier` | `score` | `floor((score - 10) / 2)`, or the `classic` table |
| `proficiency` | `rankBonus`, `addsLevel`, `proficient`, `tableBonus` | with ranks `rankBonus + (addsLevel ? level : 0)`, else `proficient ? tableBonus : 0` |
| `attack` | `ability`, `proficiency`, `bonus` | `ability + proficiency + bonus` |
| `damage` | `ability`, `bonus` | `ability + bonus` |
| `armorClass` | `armor`, `ability`, `proficiency` | `armor + ability + proficiency` |
| `check` | `ability`, `proficiency` | `ability + proficiency` |
| `save` | `ability`, `proficiency` | `ability + proficiency` |
| `dc` | `base`, `ability`, `proficiency`, `bonus` | `base + ability + proficiency + bonus` |
| `passive` | `base`, `modifier` | `base + modifier` |
| `hpFirstLevel` | `hitDie`, `bonus` (the race's and class's bonus HP), `ability` (`roles.hpAbility`'s modifier) | `hitDie + bonus + ability` |
| `hpPerLevel` | `hitDie`, `ability` | `floor(hitDie / 2) + 1 + ability` |
| `damageTaken` | `amount`, `resist`, `weak`, `immune` (the creature's `resist.<type>`, `weak.<type>`, `immune.<type>` stats plus the `.all` ones) | `immune ? 0 : max(0, amount - resist + weak)` |

Formulas about a creature may also read `trait.<name>`: 1 when the weapon in hand has that trait. An item's `traits` lists them (`"traits": ["finesse", "agile"]`), and its `damageType` (`"slashing"`) is the type a `"dice": "weapon"` damage step deals when the step names none, so resistances apply to Strikes; `attackPenalty` can read them too (`"attacks == 0 ? 0 : (trait.agile ? -4 : -5) * min(attacks, 2)"`). A modifier may have a `type` (`"item"`, `"status"`, `"circumstance"`): adds of one type don't stack, only the biggest bonus and the biggest penalty of each type count; untyped adds all count.

Dice anywhere in an effect may hold a formula in braces, worked out from the doer's sheet when rolled: `"2d8+{mod.caster}"`, `"1d10+{level}"`, `"{1 + (level >= 5) + (level >= 11)}d10"`. `mod.caster` (and `score.caster`, `prof.caster`) is the ability the creature's spells and DC use. An attack roll step may say `"ability": "caster"` to be a spell attack (the DC's ability and proficiency) instead of a weapon attack. A condition may give `attackersAdvantage` or `attackersDisadvantage`: attacks against whoever has it; and `advantageOnChecks` or `disadvantageOnChecks`: its own checks (5e's poisoned and frightened). `attackersWithin` (squares) limits what it does to attackers to those that close, with `attackersBeyond` (`advantage` or `disadvantage`) for those further off (5e's prone); `hitsAreCritical` makes a hit from within that reach (or beside it) the system's critical outcome (paralysed, unconscious). `attackersFlatCheck` is a plain d20 an attacker has to reach before its attack can land (PF2e's hidden: 11). A weapon's damage dice may hold a formula in braces read from the wielder's sheet, with `hands.free` for the hands it has free: 5e's longsword is `"1d{hands.free >= 1 ? 10 : 8}"`.

A `move` step's `how` may be `approach`: the doer walks up to the target, at most `distance` squares, stopping beside it. `teleport` puts the step's target (usually `self`) on the square the action was aimed at, when it is open and within `distance` (Misty Step: a point target with a small area). An attack roll step's `reach` (squares) skips the attack when the target isn't that close when it is rolled, so a charge that falls short doesn't hit: Sudden Charge is `[{"do": "move", "how": "approach", "distance": "{2 * floor(stat.speed / 5)}"}, {"do": "roll", "kind": "attack", "reach": 1, "steps": [...]}]`.

A ruleset folder's `triggers/` holds effects that go off by themselves for whoever is granted them: `{"id": "sneak-attack", "name": "Sneak Attack", "on": "hit", "if": "trait.finesse && advantage", "once": "turn", "effects": [{"do": "damage", "dice": "{ceil(level / 2)}d6"}]}`. `on` is `hit`, `miss` or `crit` (after one of its attacks; the effect lands on the one attacked), `hitBy` (an attack hit it; the effect lands on the attacker), `kill` (it dropped someone) or `turnStart` (its turn began); the last two land on itself. One that is down sets off nothing, and triggers set off by triggers stop four deep. `if` is a formula over the owner's sheet names plus `advantage` (the attack was rolled with it), `critical`, `flag.<flag>` and `targetFlag.<flag>`. `once: "turn"` lets it go off once in each of its turns. `"general": true` gives it to everyone, granted or not, so a weapon trait's rider works for whoever wields it (PF2e's deadly: `"on": "crit", "if": "trait.deadly", "general": true`). They are granted like actions (`"actions": ["sneak-attack"]`), and the log says "Ana: Sneak Attack".

An action or reaction file with `"general": false` belongs only to creatures granted it. A class feature, a feat or a creature file grants them with `"actions": ["sudden-charge", "attack-of-opportunity"]` (ids of the ruleset's actions or reactions; an unknown one stops the chapter loading). A sheet keeps what it was granted in `granted`.

`saves` (beside `abilities`) lists saves of their own, each an `id`, `name` and the `ability` it rolls with (`{"id": "fortitude", "name": "Fortitude", "ability": "con"}`); anywhere a save is named, one of these or an ability will do. Proficiency in a save is by its id. A creature file's `stats` sets any other number on its sheet: `"stats": {"resist.fire": 5, "weak.cold": 5, "immune.poison": 1}`; items and conditions change them with modifiers like any stat.

```json
"formulas": {
  "proficiency": "proficient ? rankBonus + level : 0",
  "armorClass": "10 + stat.ac + min(mod.dex, 2) + proficiency"
}
```

The game works the chance of each outcome out from the same data by counting every way the dice can fall, which is where the hit chance on screen comes from.

A `recovery` has `kind` (`none`, `full`, `fraction` of max HP, `flat` HP or `hitDice`), with `fraction` (0 to 1), `amount` (HP, or dice with 0 meaning one per level) and `reviveDowned`. A ruleset that fails its checks stops the chapter from loading and names the file.

A package can carry rules of its own as one JSON file or as a folder of the same shape and name it in `chapter.json`. The shipped adventures do not.

## Races, backgrounds and feats

Player options live only in the ruleset folder: `races/`, `backgrounds/` and `feats/`, one file each named after its id, in the framework's format (FRAMEWORK.md, "Races, backgrounds and feats"). A chapter's own folder can't add them. A file with an unknown field, a feat a race or background gives that doesn't exist, or a feat requiring a race or class that doesn't exist stops the chapter loading and names the file.

The game ships human, elf, dwarf and halfling; acolyte, criminal, farmhand, sage, soldier and noble; and a first feat set of each kind: race feats each race gives, skill feats the backgrounds give (expert in one of their skills), and general and class feats for the level slots the class level tables open. A character's `race` and `background` in a save name these ids. The content check builds every race and background with every class and takes every feat once.

A system may have kinds of pick of its own beside race, background and class: `"optionKinds": [{"id": "heritage", "name": "Heritage"}]` in `ruleset.json`, and one file per choice in the ruleset folder's `options/`: `{"id": "forge-dwarf", "name": "Forge dwarf", "kind": "heritage", "races": ["dwarf"], "modifiers": [{"stat": "resist.fire", "op": "add", "value": 2}]}`. `races` and `classes` say who may take it (anyone when left out); it grants what a class feature grants (`modifiers`, `proficiencies`, `ranks`, `resources`, `actions`, `spells`) and `feats`. A class feature, feat or option's `spells` are spells the character knows from it (5e's Magic Initiate, a tiefling's Fire Bolt): cantrips at will, others with a slot. A sheet's `initiative` stat adds to initiative, whatever the system rolls it with (Alert, Incredible Initiative). Any modifier may carry `"if"`, a formula: then it is situational and counts only on a roll it holds for, never in the sheet's standing numbers. On `attack` and `damage` it reads `ranged`, `melee`, `spell`, `ability.<id>` (the ability the attack is made with), `trait.<trait>` (the weapon's) and `targetFlag.<flag>`; on `saves` it reads `save.<ability>`; on `checks`, `check.<id>`; always the sheet's own names and `flag.<flag>`. Archery: `{"stat": "attack", "value": 2, "if": "ranged && !spell"}`; Danger Sense: `{"stat": "saves", "value": 2, "if": "save.dex"}`. Creation asks for one of each kind where a step lists the part `options` (only when one is open to the character); a character file keeps them as `"options": {"heritage": "forge-dwarf"}`, and the sheet names them after the race.

A feat's `kind` and a class level's `feats` name one of the ruleset's `featKinds`: `[{"id": "ancestry", "name": "Ancestry feat"}, ...]`, 1 to 16 of them, each name what the creation screen calls a slot of that kind. Without the list a ruleset has the game's own: `class`, `skill`, `general` and `race`. A kind the ruleset doesn't list stops the ruleset loading and names the file.

## Class level tables

`classes/<id>.json` may have `levels`, one row per class level, in the framework's format (FRAMEWORK.md, "Class level tables"): features, rank rises, the feat kinds and skill picks offered, and spell slots. Fighter, rogue, cleric and wizard ship with rows for levels 1 to 20; the top of each file is still the first-level character. Every table offers a class feat at levels 2, 6, 10, 14 and 18, a skill feat at 4, 12 and 20, a general feat at 8 and 16, and a skill to train at odd levels from 3 (the rogue also picks two at level 1). Cleric and wizard rows carry the full caster's slots, levels 1 to 9. A class without `levels` (the barbarian, older packages) still plays: its levels add HP only. A row may set `scale`, named numbers the class reaches at that level and keeps until a later row changes them (`"scale": {"sneak-attack": 2}`); formulas read them as `scale.<id>` with `-` written `_` (`{max(1, scale.sneak_attack)}d6`). A row may give `boosts`: that many different abilities the player raises by `boostStep` (default 2) at that level, picked on the level-up screen and kept as the level's `"boosts"` picks (5e's Ability Score Improvement at 4 is `"boosts": 2, "boostStep": 1, "boostsRepeat": true`: with `boostsRepeat` one ability may take more than one, so +2 to one or +1 to two); a score never goes past the ruleset's `scoreMax`. A row's `options` lists the system's option kinds it offers, one pick each (an archetype or subclass, `"options": ["subclass"]`), from the `options/` files open to the character's race and that level's class and not taken before.

`xpForLevel` in `ruleset.json` runs to level 20. A character levels into its latest class when XP crosses a threshold; choosing the class and the picks comes with the level-up screen. The content check builds each of the four classes at every level and a mixed fighter/wizard/rogue.

## Character files

The player's characters live in `characters/` beside the save, one file each, written by the game in the save envelope (`"format": "yorehold.character"`, `"version": 1`). The data holds `choices` (the framework's character choices, FRAMEWORK.md), `inventory` (items as a sheet writes them), `coins`, and `away`: the file name of the adventure save the character is playing in, empty when free. `characters/graveyard/` holds characters that can't be played any more; the game lists them after the others and never writes to them. A save names each hero's library file in `library` (empty for heroes made for that adventure; older saves have none). The choices may hold `portrait`: `{"picture": "portraits/wren.png", "focus": [0.5, 0.3], "zoom": 1.5}`, the player's picture from the content or an art pack (always under a `portraits` folder) and its one 3:4 crop, used on the hero's token and every card; characters without it show their class's picture. A picture file the player brings is copied into the art pack `user://art/my-pictures/portraits/`.

## Account sync files

With a server set and the player signed in, the game keeps the adventure saves (`adventure.json`, `adventure-<package>-<chapter>.json`) and the character files the same as the copies on the account. A pass runs at sign-in, after the game writes or removes one of those files, and every 30 seconds. Without a server nothing changes: the files are the truth, and whatever was done offline is sent at the next sign-in. Test runs (`YOREHOLD_SEED`) never sync.

- On the account a save's id is its file name without `.json`, a character's is the same, and a graveyard character's is `graveyard.<name>`. A file whose name isn't an id the server takes (1 to 64 of letters, digits, `_`, `-`, `.`, not starting with `.`), that isn't JSON, or that is over the server's limit (256 KB for a character, 1 MB for a save) is left alone and named on stderr.
- Per file the newest change wins, by the file's modified time against the account's. A file received from the account is given the time of the change it holds. On a tie the account's copy is taken.
- A file removed here is deleted on the account, and a file deleted on another install is removed here, unless the other side changed it since: then the changed copy comes back.
- `sync-backup/characters/<id>.json` and `sync-backup/saves/<id>.json` hold the last file a pass replaced or removed, one per id. The account keeps the copy each write replaced as well (the server's README, "Characters" and "Saves").
- `sync.json` is how a pass knows what changed since the last one. It can be deleted: the next pass compares times instead, and takes the account's copy where they match.

```json
{ "format": "yorehold.sync", "version": 1, "account": "<user id>",
  "characters": { "ser-ada": { "revision": 4, "hash": "9f2c1e0b7a6d5c43" } },
  "saves": { "adventure": { "revision": 2, "hash": "" } } }
```

`revision` is the account's revision of the record when the two last matched, and `hash` is the file's contents then (`""` = no file: it was deleted). Signing in to a different account starts the list over.

Characters are made and levelled up under Play > Characters, always with the game's own ruleset (`rulesets/yorehold`), the built-in classes plus those of installed packages, and the ruleset's races, backgrounds and feats. The score methods' numbers are `scoreMethods` in `ruleset.json` (FRAMEWORK.md, "Character choices").

The steps of making a character are `creation` in `ruleset.json`; every key is optional and the default is the yorehold set's three steps:

```json
"creation": {
  "steps": [ { "name": "Ancestry and background", "parts": ["name", "race", "background"] },
             { "name": "Class and attributes", "parts": ["class", "scores"] },
             { "name": "Skills and feats", "parts": ["skills", "feats"] } ],
  "names": { "race": "Ancestry", "scores": "Attributes" },
  "scoreMethods": ["array", "pointBuy", "roll"]
}
```

- `steps`: 1 to 8, each a tab on the creation screen. Parts are `name`, `fields` (the ruleset's fields), `race`, `background`, `class`, `scores`, `skills` and `feats`; each is in at most one step, a part left out is not asked (Fate has no race, background or class), and `name` must be in one. A system that never asks for a class builds every character on its first class and the sheet doesn't name it; with `advancement` `none` it doesn't name a level either.
- `names`: what the screen and its messages call a part ("Pick an ancestry.").
- `scoreMethods`: the ways offered to set scores, the first picked to start with. Methods are `array`, `pointBuy`, `roll` and `boosts`: every score starts at the ruleset's `scoreMethods.boostBase` (10) and `boostCount` boosts (4) each raise a different one by `boostStep` (2), as PF2e sets attributes.
- A level-up makes its picks in the step that has `skills` or `feats`.

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

`dead` is a formula for a blow that kills outright, past any saves or dying value; it reads
`amount` (the damage after temporary HP), `over` (what was left after dropping to 0), `maxHp`,
`level`, `critical` and `down` (1 when it was down already). 5e: `"over >= maxHp"`; PF2e:
`"amount >= maxHp * 2"`. Without it nothing does.

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
| `target` | `kind` `self` (default), `creature` or `point` (a square, for an action with an `area`); a creature or point target has `side` (`enemy`, `ally`, `any`), `range` in squares (or `"weapon"`: as far as the weapon in hand reaches, one square without one), and `downed` (default false) to allow unconscious targets. Dead or withdrawn creatures cannot be targeted. Ranged creature actions need a clear line of sight. |
| `area` | `shape` `burst`, `cone`, `line` or `square` with `size` in squares (plus `width` for a line, `angle` for a cone). The effect lands on everyone of the target's `side` inside it with a clear line from where it starts; see Spells. |
| `readies` | Records this action id for a reaction, until the creature's next turn or the fight ends. It must name an existing action that neither readies another nor ends the turn. |
| `log` | A line for the log when it is done; `{name}` is whoever does it. |
| `secret` | `true`: its rolls are not thrown on screen (PF2e's secret trait). Rolls by anyone not in the party whom the party can't see are never thrown. |
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

The place can give an attacker a condition for one roll, as flanking does the target:
`unseenAttackerCondition` when its target can't see it (it stands where no light reaches,
beyond the target's darkvision) and `unseenTargetCondition` when it can't see its target. The
conditions say what that means (5e: `unseen-attacker` with `advantageOnAttacks`,
`unseen-target` with `disadvantageOnAttacks`). They count only under the rules lighting mode,
for the roll and the hit chance shown, and never stay on the sheet.

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

`trigger` is `leavesReach` or `entersReach` (a move), or `hit`, `missed` (an attack on the reactor) or `allyHit` (an attack that hit one of its allies); the attack ones go off once the attack is done, at the attacker, and are taken at once with no prompt. `beforeHit` goes off as an attack on the reactor would hit, before its damage: the reaction runs (a Shield raising AC), and the same roll is read again against the defence it left. `spellCast` goes off when a foe the reaction's action can reach starts casting a spell, before the spell does anything; if the reaction drops the caster, or leaves it a condition with the flag `spellLost` (a counterspell), the spell is lost and its casting spent. Name `action`, or use `readied: true` in its place. A reaction may name a `spell` in place of an action (5e's Shield: `{"trigger": "beforeHit", "spell": "shield"}`): anyone who knows it, can cast it and has a slot may take it, and taking it casts the spell, slot spent. `unless` names a flag that keeps the reaction from going off when whoever set it off has it (5e's Opportunity Attack: `"unless": "disengaged"`, the flag Disengage's condition gives). `order` defaults to 0; `promptSeconds` defaults to 2 (0.1 to 30). Reach comes from the action's targeting range. The same action effects run for a reaction, spending one reaction instead of turn actions, including for two-handed weapons. A creature gets its reaction back at the start of its turn.

Every edge of voluntary movement is checked, including free movement and movement bought with Dash. An opportunity happens before leaving; a readied action happens after entering. Forced movement does not provoke. A lethal reaction stops movement where it happened and advances the turn. Planned movement is paid when the move starts.

A hero's reactions are listed on Sheet > Features; pressing one holds it back (not offered, not taken) until it is pressed again. This is how a player keeps a Shield or a counterspell for later, since those are taken in the middle of someone else's roll with no prompt. Saves keep the held ones per creature as `heldReactions`.

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

Saves record chapter id, folder, `package` (the folder of an adventure made in Create or imported, played over the game's content; empty for the game's own, and absent in older saves, which then mean the game's) and a signature of chapter/map/rules/definitions/ending data. Edited content or another chapter cannot silently accept old sheets and placements. Earlier keep saves migrate from versions 1/2, with count and fog-size checks retained.

A save's `choices` list holds one character file per hero, in party order: name, scores and how they were reached, one entry per level with its class and picks, XP (the format is in FRAMEWORK.md, Character choices). Loading rebuilds each hero's sheet from its choices and the current class files, then keeps what happened in play from the saved sheet: HP lost, conditions, inventory, death saves, resources spent. Editing a class file therefore changes saved heroes on their next load. A save from before `choices` takes them from its sheets (the scores on the sheet, every level in the class the chapter gave that hero). A hero whose XP passes a level gains it in their latest class, with the extra HP, until the level-up screen exists.

`ui/motion.json` (`"format": "yorehold.motion"`, `"version": 1`) says how the screens move: `panelSeconds` and `panelDistance` (an opening panel slides up this far into place), `chatSeconds` (the chat column sliding in and out), `barSeconds` (an HP bar running down across its whole length) and `bannerSeconds`. Every move is a slide, never a fade. A skin's file changes only what it names; Options > Display > Less motion makes every move a cut.
