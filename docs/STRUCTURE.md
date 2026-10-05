# Structure

## Folders

| Folder | What is in it |
|---|---|
| `rules/` | Plain C# with no Godot types: content loading, rules, World, AI, saves. Its own project (`Yorehold.Rules.csproj`). |
| `tests/` | xunit tests for `rules/`, plus the content check that parses every JSON file under `assets/`. |
| `tests/visual/scripts/` | Input scripts for screenshot runs. |
| `src/` | The Godot side: drawing, input and UI. Calls into `rules/`, never the other way. |
| `scenes/` | Godot scenes. `Main.tscn` is the start scene. |
| `assets/` | Content as JSON and images, same formats as the C++ client (`../yorehold/docs/CONTENT.md`). |
| `docs/` | `ROADMAP.md` (order of work) and this file. |

`Yorehold.csproj` is the game project and leaves `rules/` and `tests/` out of its own build.
`Yorehold.slnx` builds all three. Scratch files, logs and screenshots go in `../.dev/`, never in the repo.

## Checking

`check.ps1` builds, runs the tests and starts the game headless once. `ALL OK` and exit code 0 means good.

`check.ps1 -Shot name.png -Frames 120 -Script tests\visual\scripts\smoke.txt` also runs the game in a
window kept off screen, plays the script and saves `../.dev/name.png`. Look at the picture for anything drawn.

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
- Bad content fails with a message that names the file.
