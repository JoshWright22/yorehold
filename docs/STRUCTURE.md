# Structure

## Folders

| Folder | What is in it |
|---|---|
| `rules/` | Plain C# with no Godot types: content loading, rules, World, AI, saves. Its own project (`Yorehold.Rules.csproj`). |
| `rules/content/` | One type per content kind (ruleset, conditions, effects, actions, classes, creatures, maps, chapters...) and the code that reads it from JSON. |
| `rules/core/` | The rules themselves: `Rng` and `Dice`, `Checks`, `StatBlock` and `CharacterSheet` (modifiers, proficiency, conditions), `EffectHost` and the effect runner, `Grid`, `Sight` and `Positioning`. |
| `rules/world/` | An adventure in play between fights: `World` (party, creatures, flags, triggers, objects in use, sight and sneaking), `MapState` (walls, roofs and objects as they are now), `Paths`, `TokenMover`, `FogOfWar`, `LightLevels` and `Stealth`. |
| `tests/` | xunit tests for `rules/`, plus the content check that loads every JSON file under `assets/` into its type. `WorldFixture` builds a World from a chapter folder, from files written in the test, or from a few map rows (`WorldFixture.Small`). |
| `tests/visual/scripts/` | Input scripts for screenshot runs. |
| `src/` | The Godot side: drawing, input and UI. Calls into `rules/`, never the other way. |
| `scenes/` | Godot scenes. `Main.tscn` is the start scene and holds `PlayScreen.tscn`. |
| `assets/` | Content as JSON and images, same formats as the C++ client (`docs/CONTENT.md`). |
| `docs/` | `ROADMAP.md` (order of work), `CONTENT.md` (the file formats) and this file. |

`Yorehold.csproj` is the game project and leaves `rules/` and `tests/` out of its own build.

## The play screen

`scenes/PlayScreen.tscn` with `src/PlayScreen.cs` loads a chapter into a `World`, steps it each frame and
turns taps into `World` calls. It draws through its children, one script each:

| Node | Does |
|---|---|
| `Map` (`MapView`) | tiles from `TileArt` (the C++ placeholder painters) and grid lines, in blocks of 8 by 8 cells |
| `Objects` (`ObjectsView`) | doors, levers, chests, found traps and lamp flames |
| `Tokens` (`TokensView`) | one `Token.tscn` (`TokenView`) per creature, and the heroes' paths |
| `Camera` (`PlayCamera`) | pan and zoom from keys, wheel, drags and pinches; a click or tap that isn't a drag comes out as `Tapped` |
| `LightMap` (SubViewport) and `Lighting` (`LightingView`) | the light map: white ground under the ambient colour, `Light.tscn` lamps and carried lights, walls as occluders |
| `Shading/LightMap` | multiplies the light map over the world, like the C++ lighting pass |
| `Overlay/Fog` (`FogView`), `Overlay/Floaters` | fog of war from the party's view, words that float up |
| `Hud` | chapter title, banner and the last log lines |

The input actions (`pan_left`, `pan_right`, `pan_up`, `pan_down`, `zoom_in`, `zoom_out`, `recenter`) are in
`project.godot`. `--chapter chapters/goblin-keep` after `--` plays another chapter than the scene's.
`Yorehold.slnx` builds all three. Scratch files, logs and screenshots go in `../.dev/`, never in the repo.

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
| `pinch F` | two fingers either side of the mouse move apart by F (below 1 pinches in) and lift |

The game reads these after `--` on the command line: `--shot file`, `--frames N`, `--script file`
(`src/ShotRunner.cs`).

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
