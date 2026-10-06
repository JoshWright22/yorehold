# Structure

## Folders

| Folder | What is in it |
|---|---|
| `rules/` | Plain C# with no Godot types: content loading, rules, World, AI, saves. Its own project (`Yorehold.Rules.csproj`). |
| `rules/content/` | One type per content kind (ruleset, conditions, effects, actions, classes, creatures, maps, chapters...) and the code that reads it from JSON. |
| `rules/core/` | The rules themselves: `Rng` and `Dice`, `Checks`, `StatBlock` and `CharacterSheet` (modifiers, proficiency, conditions, gear and hands, spells known), `Item` (one inventory entry), `Coins`, `Loot` (rolling loot tables, stacking what is taken), `Merchant` (a shop's purse, prices and stock), `EffectHost` and the effect runner, `Grid`, `Sight`, `Positioning` and `SaveFormat` (the versioned file envelope). |
| `rules/characters/` | Characters as choices: `CharacterChoices` (the file), `CharacterBuild` (choices to a sheet), `CharacterDraft` (making one or adding a level, step by step) and `CharacterLibrary` (the player's files and the graveyard). |
| `rules/fight/` | A fight on its own, with no map: `Encounter` (initiative, rounds, shared turn blocks, each combatant's actions, movement and reaction, death saves, its log) and `Tactics` (the AI scorer: who to hit, where to stand, when to run or give up). |
| `rules/world/` | An adventure in play: `World` (party, creatures, flags, triggers, objects in use, sight and sneaking), `WorldCharacters` (who takes each seat, levels from XP, the copy that goes back to the library), `MapState` (walls, roofs and objects as they are now), `Paths`, `TokenMover`, `FogOfWar`, `LightLevels` and `Stealth`. Fights are the `World` files beside it: `WorldFight` (starting, turns, ending, the AI's turn), `WorldActions` (the `actions/` files and what a screen asks before using one), `WorldReactions` (moving and what it sets off), `WorldPositioning` (flanking and cover) and `WorldAi` (profiles and what the AI sees). `WorldItems` is what is carried: piles to take from (containers, chests, what the dead leave), giving, gear on and off, using things up and shops. |
| `tests/` | xunit tests for `rules/`, plus the content check that loads every JSON file under `assets/` into its type. `WorldFixture` builds a World from a chapter folder, from files written in the test, or from a few map rows (`WorldFixture.Small`). |
| `tests/visual/scripts/` | Input scripts for screenshot runs. |
| `src/` | The Godot side: drawing, input and UI. Calls into `rules/`, never the other way. `src/hud/` is the panels, `src/characters/` the character screens. |
| `scenes/` | Godot scenes. `Main.tscn` is the start scene and holds `PlayScreen.tscn`. `scenes/hud/` is the panels and their theme, `scenes/characters/` the character screens. |
| `assets/` | Content as JSON and images, same formats as the C++ client (`docs/CONTENT.md`). |
| `docs/` | `ROADMAP.md` (order of work), `CONTENT.md` (the file formats) and this file. |

`Yorehold.csproj` is the game project and leaves `rules/` and `tests/` out of its own build.

## The play screen

`scenes/PlayScreen.tscn` with `src/PlayScreen.cs` loads a chapter into a `World`, steps it each frame and
turns taps into `World` calls. It draws through its children, one script each:

| Node | Does |
|---|---|
| `Map` (`MapView`) | tiles from `TileArt` (the C++ placeholder painters) and grid lines, in blocks of 8 by 8 cells |
| `Objects` (`ObjectsView`) | doors, levers, chests, sacks the dead left, found traps and lamp flames |
| `Tokens` (`TokensView`) | one `Token.tscn` (`TokenView`) per creature, and the heroes' paths |
| `Camera` (`PlayCamera`) | pan and zoom from keys, wheel, drags and pinches; a click or tap that isn't a drag comes out as `Tapped` |
| `LightMap` (SubViewport) and `Lighting` (`LightingView`) | the light map: white ground under the ambient colour, `Light.tscn` lamps and carried lights, walls as occluders |
| `Shading/LightMap` | multiplies the light map over the world, like the C++ lighting pass |
| `Overlay/Fog` (`FogView`), `Overlay/Floaters` | fog of war from the party's view, words that float up (damage big and red, healing green, a miss pale) |
| `Overlay/FightGround` (`FightGroundView`) | on a hero's turn: the squares in reach, the path a click would walk, a picked action's range and area |
| `Overlay/TokenBars` (`TokenBarsView`) | in a fight: HP bars over tokens, a ring on whose turn it is and on who can be targeted |
| `Fight` (`FightControl`) | the player's side of a fight: the picked action, what the pointer is over (`FightAim`), taps and keys into `World` calls |
| `Hud` | chapter title, banner, and `Panels` (`scenes/hud/PlayHud.tscn`) |
| `Characters` (`CharacterScreen`) | the character screens over everything; the world waits while they are up |

The input actions (`pan_left`, `pan_right`, `pan_up`, `pan_down`, `zoom_in`, `zoom_out`, `recenter`) are in
`project.godot`. `--chapter chapters/goblin-keep` after `--` plays another chapter than the scene's, and
`--seed 7` another run of the dice.
`Yorehold.slnx` builds all three. Scratch files, logs and screenshots go in `../.dev/`, never in the repo.

## The panels

`scenes/hud/PlayHud.tscn` with `src/hud/PlayHud.cs` is every panel over the map, laid out with containers and
anchors and styled by `scenes/hud/hud-theme.tres` (dark panels, a thin leather trim; the type variations in it
are the bars, frames, tabs, chips, rows, the book page and label styles). Every colour in the theme and in the
panels' own drawing is from the CC-29 palette, named in `src/hud/Palette.cs`. `PlayScreen` hands it the `World` and the `FightAim` each frame. It
only shows them and raises an event when something is pressed; `FightControl` does the acting.

| Part | Is |
|---|---|
| `Party` | a `PartyCard.tscn` per hero down the left edge: portrait, HP, conditions as small badges, a gold frame on whose turn it is (or who is selected between fights) and a green one on who can take the shared turn. Pressing one selects that hero |
| `Top` | the turn order in a fight: an `InitiativeCard.tscn` each from the block whose turn it is, the current one bigger, allies who share a turn together with a gap before the next block, the dead left out |
| `Bottom` | in a fight: the acting hero's portrait and HP, the hotbar (action, reaction and bonus pips, the movement bar, an `ActionSlot.tscn` per action with its key) and End Turn |
| `Log` (`LogPanel.tscn`) | the log, bottom right; its header folds it |
| `Reaction` | use it or pass, with the time left, when a hero's reaction is offered |
| `Defeat` | the chapter's defeat text once the party is wiped |
| `Tip`, `Cursor` | the tooltip, and the words at the pointer (chance to hit, what a move costs) |
| `Menu` | the buttons along the top right: Characters, and one per panel (Sheet, C; Gear, I). A panel's key or button opens it and again closes it, so does Escape between fights; one is open at a time |
| `Sheet` (`DataPanel.tscn`, filled by `SheetPanel`) | the sheet panel (C): the hero's abilities, skills, feats, uses and conditions as rows by type, with their stat block (`SheetPage`) on the right and the picked row spelled out under it. The heroes are buttons in its head. It shows the acting hero in a fight and the selected one between fights |
| `Gear` (`DataPanel.tscn`, filled by `GearPanel`) | the gear panel (I): the hero's pack, or a pile or shop beside them, by kind of item, with the picked item's page and what can be done with it (put on, use, give, take, buy, sell). What is pressed goes out as an `ItemOrder` and `PlayScreen` does it |

`DataPanel` is the data screens' shared look: a tab bar, search box and filter chips over tight rows that sort
by any column header, and the picked row's entry on the right as a book page (`BookPage` writes its bbcode)
with buttons under it. Its owner fills it every frame; it only rebuilds what changed.

## The character screens

`scenes/characters/CharacterScreen.tscn` with `src/characters/CharacterScreen.cs`, opened by Characters on the
menu. Characters is the library as a `DataPanel` (`Library`): every character a row, tabs for those ready, away
and in the graveyard, a chip for who can level up, the picked one's stat block on the right with New character
and Level up (when the XP is there) under it. The other views have the left side made again after every click
from `CharacterDraft`, and on the right a `SheetView` (the same stat block) of whatever is picked. New adventure has one seat per hero the chapter places: each holds its
ready-made hero until a library character or a new one takes it, and Start (or Enter) plays the chapter again
with them. Making a character has three steps, Origin, Class and scores, Skills and feats; levelling up only
the class and the last. The library folder is `characters/` in Godot's user folder (`Places`).

Cards and slots are `TipButton`s: the pointer over one shows its tooltip, and so does holding it down, which
is how a touch screen gets one. A greyed slot can still be pressed and then says why it is greyed. Nothing
needs a hover: on a touch screen the first tap on a square shows the path or the chance to hit and the second
tap does it.

In a fight: a click on a square in reach walks there, a click on an enemy strikes it (walking up first), the
number keys or a slot pick an action and the next click aims it, a right click or Escape puts it away, Space
ends the turn. Portraits are the token's disc and action icons are plain shapes (`ActionIcon`) until there is
art; `assets/ui/action-icons.json` says which shape each action id gets.

## Checking

`check.ps1` builds, runs the tests and starts the game headless once. `ALL OK` and exit code 0 means good.

`check.ps1 -Shot name.png -Frames 120 -Script tests\visual\scripts\smoke.txt` also runs the game in a
window kept off screen, plays the script and saves `../.dev/name.png`. Look at the picture for anything drawn.
`-Chapter chapters\goblin-keep` plays another chapter in that run.

Input scripts have one step per line: a frame number, a command and its words. Lines that do not
start with a number are skipped, so `#` notes work.

| Command | Does |
|---|---|
| `move X Y` | moves the mouse, in viewport pixels (1280 by 720) |
| `down left` / `up left` | mouse button, also `right` and `middle` |
| `wheel N` | scrolls, positive is up |
| `key Name` / `keyup Name` | a key by name, underscores for spaces (`Left_Ctrl`). SDL names like `Return` work |
| `text some words` | types the rest of the line |
| `shot .dev/name.png` | saves an extra picture on that frame, path from the workspace folder |
| `cell X Y` | moves the mouse to the middle of map cell X, Y wherever the camera is |
| `creature Name` | moves the mouse onto the token with that name, wherever it stands (underscores for spaces: `Goblin_1`) |
| `button Words` | moves the mouse onto the first button on screen whose text starts with the words (`New_character`), a text box by the words it shows when empty, or a data panel's row by its first cell (`Healing`) |
| `tap` | one finger down and up where the mouse is, the way a touch screen clicks |
| `pinch F` | two fingers either side of the mouse move apart by F (below 1 pinches in) and lift |

The game reads these after `--` on the command line: `--shot file`, `--frames N`, `--script file`
(`src/ShotRunner.cs`).

A screenshot run prints every log line with the frame it came on (`../.dev/godot-shot.log`), which is how to
time a script against a fight. The camera goes to whoever's turn it is, so a click on a cell should wait
until it has settled. `tests/visual/scripts/fight.txt` plays chapter one's fight from the door to the last
goblin with keys and clicks only (440 frames), and `fight-hud.txt` goes through the tooltips, cancelling,
the log and a touch tap (570 frames). `characters.txt` opens the sheet, makes a character and starts the
chapter with them (280 frames). `loot.txt` opens the gear panel and empties the chest in chapter one's corner
(290 frames), `loot-fight.txt` wins the fight and takes what the goblin left (650 frames), and `shop.txt`
sells and buys at Wren's in the goblin keep (270 frames, `-Chapter chapters\goblin-keep`). A run keeps its library in `../.dev/shot-characters`, emptied as it starts.

## C# style

- 4 spaces, braces on their own line.
- PascalCase types and methods, `_camelCase` fields, camelCase locals and parameters.
- File-scoped namespaces: `Yorehold.Rules` in `rules/`, `Yorehold` in `src/`.
- Nullable is on. No `!` to quiet a warning unless a comment says why it is safe.
- One main type per file, file named after it.
- Comments say why, not what.
- New rules get xunit tests. Anything drawn gets a screenshot run.
- Numbers and names that belong to the game live in `assets/`, not in code.
- Bad content fails with a message that names the file and the field (`ContentException`).

## Reading content

`ContentFiles` is the content tree: one or more folders, later ones on top, with forward-slash paths
(`classes/fighter.json`). `ContentNode` is one JSON value plus the file and field path it came from, so a
reader says where a problem is with `node.Fail(...)`. Each content type has a static `Read(ContentNode)`
that checks the file on its own; what needs other files (an item a class names, a condition an effect
applies) is checked by whatever loads them together: `Compendium`, `RulesFolder`, `Chapter`, `Adventure`,
`ContentPackage`.

A new field goes in the type, its `Read`, a test in `tests/` and `docs/CONTENT.md`, in the same commit.
A new file under `assets/` has to be loaded by something or `ContentTests.EveryShippedFileLoads` fails.
